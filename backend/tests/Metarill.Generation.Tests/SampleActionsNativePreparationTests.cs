using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实完整包准备与逐字固定门禁，不建立数据库、账号或网络身份。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SampleActionsNativePreparationTests
{
    /// <summary>真实构建包只离线读写临时文件，覆盖业务/程序集漂移和精确复制白名单。</summary>
    [Fact]
    public async Task Original_full_package_freezes_six_handwritten_sources_and_rejects_drift()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-sample-actions-native");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"prepared");
        try
        {
            SampleActionsNativePreparation.Create(source,package);
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var value in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(value);await NonRedundantApplicationFixture.RunAsync(start);
            var proof=SampleActionsNativePreparation.Capture(package);var catalog=SampleActionsNativePreparation.Verify(proof,source);Assert.Equal(3,catalog.ConfigTables.Count);Assert.Equal(3,catalog.ConfigButtons.Count);Assert.Equal(6,SampleActionsNativePreparation.Handwritten(source).Count);
            foreach(var relative in new[]{"backend/generated/Services/SampleContractService.cs","backend/Application.Host/SampleContractAgentBusiness.cs","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {
                var file=CSharpGenerator.SafePath(package,relative);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"漂移");Assert.Throws<InvalidOperationException>(()=>SampleActionsNativePreparation.Verify(proof,source));await File.WriteAllBytesAsync(file,bytes);
            }
            var extra=Path.Combine(package,"未固定.txt");await File.WriteAllTextAsync(extra,"拒绝");Assert.Throws<InvalidOperationException>(()=>SampleActionsNativePreparation.Verify(proof,source));File.Delete(extra);
            var copied=SampleActionsNativePreparation.CopyForExecution(proof,source,Path.Combine(directory,"execution/application"));var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>SampleActionsNativePreparation.Verify(copied,source));SampleActionsNativePreparation.Verify(copied,source,true);Assert.Throws<InvalidOperationException>(()=>SampleActionsNativePreparation.Verify(SampleActionsNativePreparation.Capture(copied.PackageRoot),source));
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/unknown.json");await File.WriteAllTextAsync(unknown,"{}");Assert.Throws<InvalidOperationException>(()=>SampleActionsNativePreparation.Verify(copied,source,true));File.Delete(unknown);
            Assert.Throws<InvalidOperationException>(()=>SampleActionsNativePreparation.CopyForExecution(proof,source,copied.PackageRoot));SampleActionsNativePreparation.Verify(proof,source);
            // 显式离线导出只允许该检出的固定私有准备位置，不能覆盖已有输出或触碰B98。
            var output=Environment.GetEnvironmentVariable("METARILL_SAMPLE_ACTION_PREPARE_OUTPUT");if(!string.IsNullOrEmpty(output))
            {
                var allowed=CSharpGenerator.SafePath(source,".local/sample-actions-native/prepared");Assert.Equal(allowed,Path.GetFullPath(output));var exported=SampleActionsNativePreparation.CopyForExecution(proof,source,allowed);
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(allowed)!,"prepared-receipt.json"),JsonSerializer.Serialize(exported,new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
