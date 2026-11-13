using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>通过真实CLI进程核对显式仅后端开关，避免参数解析与打包API行为分离。</summary>
public sealed class PackageCliTests : IAsyncDisposable
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "metarill-package-cli", Guid.NewGuid().ToString("N"));
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    /// <summary>平台不能默认生成缺功能前端；开关只能属于package且不可重复或当成参数值。</summary>
    [Fact]
    public async Task Package_flag_is_explicit_and_unknown_or_duplicate_arguments_fail()
    {
        var source = SourceRoot();
        var built = await RunAsync(source, "build", "backend/src/Metarill.Cli/Metarill.Cli.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet");
        Assert.True(built.Code == 0, built.Output);
        var cli = Path.Combine(source, "backend/src/Metarill.Cli/bin/Debug/net10.0/Metarill.Cli.dll");
        string[] Args(string metadata, string folder) => [cli, "package", "--metadata", Path.Combine(source, "backend/metadata/" + metadata + ".json"), "--output", Path.Combine(temporary, folder), "--namespace", "Example.Cli", "--source-root", source];
        var platform = await RunAsync(source, Args("platform", "platform-full"));
        Assert.Equal(1, platform.Code); Assert.Contains("--backend-only", platform.Output); Assert.False(Directory.Exists(temporary));
        var backend = await RunAsync(source, Args("platform", "platform-backend").Append("--backend-only").ToArray());
        Assert.True(backend.Code == 0, backend.Output);
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary, "platform-backend/.metarill-package.json")))) Assert.False(manifest.RootElement.GetProperty("frontendIncluded").GetBoolean());
        var full = await RunAsync(source, Args("demo", "demo-full")); Assert.True(full.Code == 0, full.Output); Assert.True(File.Exists(Path.Combine(temporary, "demo-full/frontend/manifest.json")));
        // 实际CLI新增模式必须明确选择包内后台，普通Full和BackendOnly继续原参数行为。
        var application=JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var managed=await RunAsync(source,Args("demo","managed").Concat(["--management-admin-type",application.YzAdminTypes[0].Id]).ToArray());Assert.True(managed.Code==0,managed.Output);
        using(var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary,"managed/.metarill-package.json"))))Assert.Equal("FullManagementApplication",manifest.RootElement.GetProperty("packageMode").GetString());
        var mixed=await RunAsync(source,Args("demo","mixed").Concat(["--backend-only","--management-admin-type",application.YzAdminTypes[0].Id]).ToArray());Assert.Equal(1,mixed.Code);Assert.False(Directory.Exists(Path.Combine(temporary,"mixed")));
        var repeatedManagement=await RunAsync(source,Args("demo","repeated-management").Concat(["--management-admin-type",application.YzAdminTypes[0].Id,"--management-admin-type",application.YzAdminTypes[0].Id]).ToArray());Assert.Equal(1,repeatedManagement.Code);Assert.False(Directory.Exists(Path.Combine(temporary,"repeated-management")));
        Assert.Equal(1,(await RunAsync(source,cli,"generate","--management-admin-type",application.YzAdminTypes[0].Id)).Code);
        var duplicate = await RunAsync(source, Args("demo", "duplicate").Concat(["--backend-only", "--backend-only"]).ToArray()); Assert.Equal(1, duplicate.Code); Assert.False(Directory.Exists(Path.Combine(temporary, "duplicate")));
        var wrongCommand = await RunAsync(source, cli, "generate", "--backend-only"); Assert.Equal(1, wrongCommand.Code);
        var missingValue = await RunAsync(source, cli, "package", "--metadata", "--backend-only"); Assert.Equal(1, missingValue.Code);
        var unknown = await RunAsync(source, Args("demo", "unknown").Concat(["--unknown", "value"]).ToArray()); Assert.Equal(1, unknown.Code); Assert.False(Directory.Exists(Path.Combine(temporary, "unknown")));
    }

    /// <summary>参数通过ArgumentList传递，不拼接shell；子进程输出限时收敛并保持UTF8。</summary>
    private static async Task<(int Code, string Output)> RunAsync(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = workingDirectory, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        // 只有 CLI build 会改写同根依赖的 obj；其余命令保持原并行行为。
        using var buildLease = await SharedSourceBuildGate.Shared.EnterAsync(arguments.FirstOrDefault());
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); } catch (Exception interrupted) { await SharedSourceBuildGate.StopOwnProcessAsync(process, stdout, stderr, interrupted); throw; }
        return (process.ExitCode, await stdout.WaitAsync(timeout.Token) + await stderr.WaitAsync(timeout.Token));
    }

    public async ValueTask DisposeAsync() => await TestDirectoryCleanup.DeleteAsync(temporary, Path.Combine(Path.GetTempPath(), "metarill-package-cli"));
}
