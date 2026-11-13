using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

/// <summary>受控真实文件系统失败验证发布不覆盖、原异常链和两层现场保护，不把注入故障当历史根因。</summary>
public sealed class PackagePublicationTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task Publication_race_preserves_original_error_nonempty_target_and_nested_stage(bool frontend)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-publication"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "application");
        var calls = 0; string? blocked = null;
        try
        {
            var packager = new ApplicationPackager((stage, target) =>
            {
                calls++;
                var isFrontend = Path.GetFileName(stage).StartsWith("frontend.staging-", StringComparison.Ordinal);
                if (isFrontend == frontend) { blocked = target; Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "owner.txt"), "原目标私有正文不得进入诊断"); }
                PackagePublication.Publish(stage, target);
            });
            var error = Assert.Throws<PackagePublicationException>(() => packager.Create(Catalog(), output, "Example.Publication", SourceRoot()));
            Assert.Equal(frontend ? 1 : 2, calls); Assert.NotNull(error.InnerException); Assert.Equal(error.InnerException.HResult, error.OriginalHResult); Assert.Equal(error.OriginalHResult, error.HResult);
            Assert.NotNull(blocked); Assert.Equal("原目标私有正文不得进入诊断", File.ReadAllText(Path.Combine(blocked, "owner.txt")));
            Assert.True(Directory.Exists(error.StageDirectory)); Assert.True(Directory.Exists(error.ContainingStageDirectory));
            Assert.StartsWith(directory + Path.DirectorySeparatorChar, error.StageDirectory, StringComparison.OrdinalIgnoreCase);
            var text = File.ReadAllText(error.DiagnosticPath!); Assert.DoesNotContain("原目标私有正文", text); Assert.DoesNotContain("ConnectionString", text);
            using var report = JsonDocument.Parse(text); Assert.Equal(error.OriginalHResult, report.RootElement.GetProperty("originalHResult").GetInt32());
            Assert.Equal(Environment.CurrentDirectory, report.RootElement.GetProperty("currentDirectory").GetString());
            Assert.True(File.Exists(Path.Combine(error.ContainingStageDirectory!, ".metarill-publication-failure.json")));
            if (frontend) { Assert.False(Directory.Exists(output)); Assert.True(File.Exists(Path.Combine(error.ContainingStageDirectory!, "backend/generated/.metarill-generation.json"))); }
            else Assert.True(File.Exists(Path.Combine(error.StageDirectory, "frontend/.metarill-frontend.json")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    [Fact]
    public async Task Non_publication_error_keeps_original_cleanup_and_does_not_preserve_arbitrary_markers()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-publication"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "application"); string? stagePath = null;
        try
        {
            var packager = new ApplicationPackager((stage, _) => { stagePath = stage; File.WriteAllText(Path.Combine(stage, ".metarill-publication-failure.json"), "任意marker不构成发布异常"); throw new InvalidOperationException("受控普通失败"); });
            Assert.Equal("受控普通失败", Assert.Throws<InvalidOperationException>(() => packager.Create(Catalog(), output, "Example.Publication", SourceRoot())).Message);
            Assert.NotNull(stagePath); Assert.False(Directory.Exists(stagePath)); Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory) : []);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>Windows真实不共享删除句柄可复现发布错误，并校验路径受限的句柄探针。</summary>
    [WindowsPublicationFact]
    public async Task Windows_open_file_without_delete_sharing_captures_real_error_and_own_handle()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-publication"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var stage = Path.Combine(directory, "stage"); Directory.CreateDirectory(stage);
        try
        {
            var locked = Path.Combine(stage, "locked.txt"); File.WriteAllText(locked, "受控共享模式");
            using var stream = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var error = Assert.Throws<PackagePublicationException>(() => PackagePublication.Publish(stage, Path.Combine(directory, "output")));
            var path = PublicationHandleProbe.Capture(error); Assert.NotNull(path);
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Contains(report.RootElement.GetProperty("matches").EnumerateArray(), row => row.GetProperty("processId").GetInt32() == Environment.ProcessId && row.GetProperty("path").GetString() == locked);
            log.WriteLine("受控真实Windows异常：" + error.InnerException!.GetType().FullName + "，原HResult=" + error.OriginalHResult + "；探针识别本进程精确文件句柄。历史两次失败持有者仍未知。");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>仅读取已提交demo元数据，定向故障测试不连接数据库或读取私有设置。</summary>
    private static MetadataCatalog Catalog() => JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "backend/metadata/demo.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>取本测试源码所在clone，避免把主线与候选生成器混用。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}

/// <summary>共享删除模式只对Windows成立，其他系统明确跳过而非空测试通过。</summary>
public sealed class WindowsPublicationFactAttribute : FactAttribute
{
    public WindowsPublicationFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "该用例验证Windows真实共享删除句柄。"; }
}
