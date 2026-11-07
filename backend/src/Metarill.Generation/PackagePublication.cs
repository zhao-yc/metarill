using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>只描述发布路径和原始IO错误，不记录配置、文件正文、环境变量或命令行。</summary>
public sealed class PackagePublicationException : IOException
{
    public string StageDirectory { get; }
    public string OutputDirectory { get; }
    public string? ContainingStageDirectory { get; private set; }
    public string? DiagnosticPath { get; private set; }
    public int OriginalHResult => InnerException!.HResult;

    /// <summary>构造函数仅供受控发布边界使用，原异常类型、错误码和堆栈保留在InnerException。</summary>
    internal PackagePublicationException(string stage, string output, Exception error)
        : base("生成包发布失败；已停止自动清理，诊断stage：" + stage + "。原错误：" + error.Message, error)
    {
        HResult = error.HResult; StageDirectory = Path.GetFullPath(stage); OutputDirectory = Path.GetFullPath(output);
        DiagnosticPath = SaveDiagnostic(StageDirectory);
    }

    /// <summary>外层仅保留包含真实失败stage的本次目录，不由任意marker控制清理。</summary>
    internal void PreserveContainingStage(string stage)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        if (StageDirectory != full && !StageDirectory.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("发布失败stage不在本次外层目录内。");
        ContainingStageDirectory = full;
        if (full != StageDirectory) SaveDiagnostic(full);
    }

    /// <summary>诊断失败不得覆盖原发布异常；只在现存受控stage内写一个固定文件。</summary>
    private string? SaveDiagnostic(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            var path = CSharpGenerator.SafePath(directory, ".metarill-publication-failure.json");
            using var process = Process.GetCurrentProcess();
            var record = new
            {
                schemaVersion = 1, occurredUtc = DateTimeOffset.UtcNow, processId = Environment.ProcessId,
                processName = process.ProcessName, currentDirectory = Environment.CurrentDirectory,
                stageDirectory = StageDirectory, outputDirectory = OutputDirectory, containingStageDirectory = ContainingStageDirectory,
                originalExceptionType = InnerException!.GetType().FullName, originalHResult = OriginalHResult,
                originalHResultHex = "0x" + unchecked((uint)OriginalHResult).ToString("X8"),
                stage = State(StageDirectory), output = State(OutputDirectory), parent = State(Path.GetDirectoryName(StageDirectory)!),
                note = "只记录路径和错误码；未捕获的持有句柄进程仍属未知。"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n", new UTF8Encoding(false));
            return path;
        }
        catch (Exception diagnosticError) when (diagnosticError is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Data["DiagnosticWriteErrorType"] = diagnosticError.GetType().FullName; Data["DiagnosticWriteHResult"] = diagnosticError.HResult; return null; }
    }

    /// <summary>目录状态只读，读取失败保留确切错误码而不把未知状态解释成不存在。</summary>
    private static object State(string path)
    {
        try { return new { exists = Directory.Exists(path), attributes = File.GetAttributes(path).ToString(), errorType = (string?)null, hResult = (int?)null }; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new { exists = Directory.Exists(path), attributes = (string?)null, errorType = error.GetType().FullName, hResult = (int?)error.HResult }; }
    }
}

/// <summary>发布仅尝试一次；失败保存原异常和stage，不覆盖非空目标，也不默默重试。</summary>
internal static class PackagePublication
{
    /// <summary>只删除空目标并单次重命名；任何IO失败保留stage和原始异常。</summary>
    internal static void Publish(string stage, string output)
    {
        try
        {
            if (Directory.Exists(output)) Directory.Delete(output, false);
            Directory.Move(stage, output);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new PackagePublicationException(stage, output, error); }
    }
}
