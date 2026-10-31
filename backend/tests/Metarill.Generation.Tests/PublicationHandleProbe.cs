using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Metarill.Generation.Tests;

/// <summary>仅失败诊断读取Windows句柄；输出严格过滤本stage，不输出其它进程路径或命令行。</summary>
internal static class PublicationHandleProbe
{
    /// <summary>进程不可访问和探针失败都显式记录为未知，绝不覆盖原发布异常。</summary>
    internal static string? Capture(PackagePublicationException error)
    {
        if (!OperatingSystem.IsWindows() || error.DiagnosticPath is null) return null;
        try
        {
            var path = Path.Combine(error.ContainingStageDirectory ?? error.StageDirectory, ".metarill-publication-handles.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Read(error.StageDirectory, error.DiagnosticPath), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
            return path;
        }
        catch (Exception probeError) { error.Data["HandleProbeErrorType"] = probeError.GetType().FullName; error.Data["HandleProbeHResult"] = probeError.HResult; return null; }
    }

    /// <summary>原始系统句柄表不落盘；只保留本stage匹配项和无法读取的进程编号/原始错误码。</summary>
    private static object Read(string stage, string diagnostic)
    {
        using var ownFile = File.Open(diagnostic, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var own = unchecked((ulong)ownFile.SafeFileHandle.DangerousGetHandle().ToInt64());
        var size = 1024 * 1024; IntPtr buffer = IntPtr.Zero;
        try
        {
            int status;
            while (true)
            {
                buffer = Marshal.AllocHGlobal(size); status = NtQuerySystemInformation(64, buffer, size, out var required);
                if (status == 0) break;
                Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero;
                if (status != unchecked((int)0xC0000004) || size >= 64 * 1024 * 1024) return new { ntStatus = status, note = "系统句柄快照不可读，持有者未知。" };
                size = Math.Max(size * 2, required);
            }
            var count = checked((int)Marshal.ReadIntPtr(buffer).ToInt64()); var entrySize = Marshal.SizeOf<HandleEntry>(); var entries = new List<HandleEntry>();
            for (var index = 0; index < count; index++) entries.Add(Marshal.PtrToStructure<HandleEntry>(buffer + IntPtr.Size * 2 + index * entrySize));
            var kind = entries.Single(entry => entry.ProcessId.ToUInt64() == (ulong)Environment.ProcessId && entry.Handle.ToUInt64() == own).Type;
            var matches = new List<object>(); var denied = new List<object>(); var scanned = 0;
            // 仅枚举本测试进程可确认的句柄；外部/保护进程不在异常路径同步扫描，避免诊断阻塞。
            foreach (var group in entries.Where(entry => entry.Type == kind && entry.ProcessId.ToUInt64() == (ulong)Environment.ProcessId).GroupBy(entry => (int)entry.ProcessId.ToUInt64()))
            {
                using var process = OpenProcess(0x40, false, group.Key);
                if (process.IsInvalid) { denied.Add(new { processId = group.Key, nativeError = Marshal.GetLastWin32Error() }); continue; }
                foreach (var entry in group)
                {
                    if (group.Key == Environment.ProcessId && entry.Handle.ToUInt64() == own) continue;
                    if (!DuplicateHandle(process, (IntPtr)unchecked((long)entry.Handle.ToUInt64()), new IntPtr(-1), out var copy, 0, false, 2)) continue;
                    using (copy)
                    {
                        if (GetFileType(copy) != 1) continue; scanned++;
                        var name = new StringBuilder(32768); var length = GetFinalPathNameByHandle(copy, name, name.Capacity, 8);
                        if (length == 0 || length >= name.Capacity) continue;
                        var full = name.ToString(); if (full.StartsWith("\\\\?\\", StringComparison.Ordinal)) full = full[4..];
                        if (full != stage && !full.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        string? processName = null; try { using var known = Process.GetProcessById(group.Key); processName = known.ProcessName; } catch (Exception) { }
                        matches.Add(new { processId = group.Key, processName, path = full, grantedAccess = entry.Access });
                    }
                }
            }
            return new { capturedUtc = DateTimeOffset.UtcNow, currentProcessId = Environment.ProcessId, stage, scanned, matches, inaccessibleProcesses = denied, otherProcessHandleCount = entries.Count(entry => entry.Type == kind && entry.ProcessId.ToUInt64() != (ulong)Environment.ProcessId), note = "仅检查本进程。匹配句柄不直接证明共享模式；外部/保护进程和瞬时已关闭句柄仍未知。" };
        }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleEntry { internal IntPtr Object; internal UIntPtr ProcessId; internal UIntPtr Handle; internal uint Access; internal ushort Creator; internal ushort Type; internal uint Attributes; internal uint Reserved; }
    /// <summary>读取内核句柄元数据，原始表不落盘。</summary>
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int kind, IntPtr buffer, int size, out int required);
    /// <summary>仅申请复制句柄权限，不读取进程内存或命令行。</summary>
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int process);
    /// <summary>复制句柄供只读查询，每次检查后立即释放。</summary>
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(SafeProcessHandle process, IntPtr source, IntPtr targetProcess, out SafeFileHandle target, uint access, bool inherit, uint options);
    /// <summary>跳过管道和其它非磁盘句柄。</summary>
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle file);
    /// <summary>只查打开路径，随后按stage边界过滤，不读取正文。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int size, uint flags);
}
