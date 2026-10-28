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
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-source-native-proof");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"app");try
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
            Assert.Throws<InvalidOperationException>(()=>GlobalCachedSourceNativePreparation.Verify(proof,!parentFiles));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
