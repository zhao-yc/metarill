using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实库前先以真实完整包证明固定门槛；不会创建数据库或把摘要核验算作正式身份验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedSourceNativePreparationTests
{
    /// <summary>原模型和文件父变体都独立编译，再检查源码、程序集、手写覆盖和额外文件篡改。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_application_proof_rejects_changed_code_host_handwritten_rule_and_extra_file(bool parentFiles)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-source-native-proof");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"app");var relativeBoundary=Path.GetFullPath(Path.Combine(".local","metarill-cached-copy-path-proof"));var relativeRoot=Path.Combine(relativeBoundary,Guid.NewGuid().ToString("N"));try
        {
            new ApplicationPackager().Create(GlobalCachedSourceNativePreparation.Catalog(parentFiles),package,GlobalCachedSourceNativePreparation.RootNamespace,NonRedundantApplicationFixture.Root());
            var hand=Path.Combine(package,"backend/generated/Services/SampleContractService.cs");await File.WriteAllTextAsync(hand,GlobalCachedSourceNativePreparation.Handwritten());
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var proof=GlobalCachedSourceNativePreparation.Capture(package);var catalog=GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);Assert.Equal(parentFiles?47:46,catalog.ConfigColumns.Count);Assert.Equal(3,catalog.ConfigTables.Count);
            foreach(var relative in new[]{"backend/generated/Services/SampleContractService.g.cs","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {
                var path=Path.Combine(package,relative);var bytes=await File.ReadAllBytesAsync(path);await File.AppendAllTextAsync(path,"篡改");Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.Verify(proof,parentFiles));await File.WriteAllBytesAsync(path,bytes);
            }
            var original=await File.ReadAllTextAsync(hand);await File.WriteAllTextAsync(hand,original.Replace("\r\n","\n",StringComparison.Ordinal));GlobalCachedSourceNativePreparation.Verify(GlobalCachedSourceNativePreparation.Capture(package),parentFiles);await File.WriteAllTextAsync(hand,original);await File.WriteAllTextAsync(hand,"// 缺少原手写业务");var modified=GlobalCachedSourceNativePreparation.Capture(package);Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.Verify(modified,parentFiles));await File.WriteAllTextAsync(hand,original);
            var extra=Path.Combine(package,"未固定的文件.txt");await File.WriteAllTextAsync(extra,"不能进入正式包");Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.Verify(proof,parentFiles));File.Delete(extra);GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);
            // 执行副本的审计落点是模板固定路径；新增代码或其它运行文件不能借此放行。
            var execution=GlobalCachedSourceNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution"),parentFiles);var journal=Path.Combine(execution.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.Verify(execution,parentFiles));GlobalCachedSourceNativePreparation.VerifyRuntime(execution,parentFiles);var extraRuntime=Path.Combine(execution.PackageRoot,"未固定的运行文件.txt");await File.WriteAllTextAsync(extraRuntime,"拒绝");Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.VerifyRuntime(execution,parentFiles));GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);
            // 正式执行用Path.Combine拼入含/的局部路径；Windows下也必须规范根并逐字保留全部文件。
            var mixedOutput=Path.Combine(directory,"execution-mixed/application");var mixed=GlobalCachedSourceNativePreparation.CopyForExecution(proof,mixedOutput,parentFiles);Assert.Equal(Path.GetFullPath(mixedOutput),mixed.PackageRoot);Assert.Equal(proof.Files.Count,mixed.Files.Count);foreach(var file in proof.Files)Assert.Equal(file.Value,mixed.Files[file.Key]);GlobalCachedSourceNativePreparation.Verify(mixed,parentFiles);
            // 相对根在与当前工作目录同一卷内创建，确保此反例不被GetRelativePath隐式变回绝对路径。
            var relativeOutput=Path.GetRelativePath(Environment.CurrentDirectory,relativeRoot);Assert.False(Path.IsPathRooted(relativeOutput));var relativeProof=GlobalCachedSourceNativePreparation.CopyForExecution(proof,relativeOutput,parentFiles);Assert.Equal(relativeRoot,relativeProof.PackageRoot);foreach(var file in proof.Files)Assert.Equal(file.Value,relativeProof.Files[file.Key]);GlobalCachedSourceNativePreparation.Verify(relativeProof,parentFiles);
            Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.CopyForExecution(proof,relativeOutput,parentFiles));GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);
            Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.Verify(proof,!parentFiles));
        }
        finally{if(Directory.Exists(relativeRoot))await TestDirectoryCleanup.DeleteAsync(relativeRoot,relativeBoundary);if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>显式配置时直接消费首轮原901文件包；只复制/核摘要，不读取数据库配置或打开连接。</summary>
    [FrozenCachedCopyFact]
    public async Task Frozen95_packages_copy_with_actual_mixed_root_without_changing_originals()
    {
        var settings=Environment.GetEnvironmentVariable(FrozenCachedCopyFactAttribute.Settings)!;
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(settings));Assert.Equal(GlobalCachedSourceNativePreparation.ProductCommit,json.RootElement.GetProperty("productCommit").GetString());
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-frozen95-copy-proof");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            foreach(var (name,parentFiles) in new[]{("cached_original",false),("cached_parent",true)})
            {
                var proof=json.RootElement.GetProperty("preparation").GetProperty(name).Deserialize<GlobalCachedPackageProof>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal(901,proof.Files.Count);GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);
                var output=Path.Combine(directory,name+"/application");var copied=GlobalCachedSourceNativePreparation.CopyForExecution(proof,output,parentFiles);
                Assert.Equal(Path.GetFullPath(output),copied.PackageRoot);Assert.Equal(proof.ManifestSha256,copied.ManifestSha256);Assert.Equal(proof.AssemblySha256,copied.AssemblySha256);Assert.Equal(proof.MetadataSha256,copied.MetadataSha256);Assert.Equal(proof.Files.Count,copied.Files.Count);
                foreach(var file in proof.Files)Assert.Equal(file.Value,copied.Files[file.Key]);GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);GlobalCachedSourceNativePreparation.Verify(copied,parentFiles);
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

}

/// <summary>普通CI缺固定原包时明确跳过；配置了无效路径则由测试失败，不把缺证据算通过。</summary>
internal sealed class FrozenCachedCopyFactAttribute:FactAttribute
{
    internal const string Settings="METARILL_CACHED_COPY_PROOF_SETTINGS";
    /// <summary>只判断是否配置，不触碰连接或外部资源。</summary>
    public FrozenCachedCopyFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Settings)))Skip="未配置首轮冻结95bdfdc双包的离线复制证明。";}
}
