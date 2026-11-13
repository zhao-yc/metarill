using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实完整包、闭包与防漂移只在本地离线验证；不把构建记作正式数据库通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RuleRecordNativePreparationTests
{
    /// <summary>原共享模型、六类页面和逻辑ID规则完整发布，独立严格编译后验证摘要与运行白名单。</summary>
    [Fact]
    public async Task Full_rule_record_package_is_fixed_before_native_lease()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-rule-native-preparation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"prepared");
        try
        {
            RuleRecordNativePreparation.Create(source,package);var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(start);
            var proof=RuleRecordNativePreparation.Capture(package);var catalog=RuleRecordNativePreparation.Verify(proof);Assert.Equal(6,catalog.ConfigTables.Count);Assert.Equal(6,catalog.ConfigPages.Count(page=>page.ConfigTableId=="2"));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,RuleRecordNativePreparation.RootNamespace)));
            foreach(var relative in new[]{"src/Metarill.Core/Contracts/IGeneratedRuleRecord.cs","backend/metadata/project.json","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {var file=CSharpGenerator.SafePath(package,relative);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"漂移");Assert.Throws<InvalidOperationException>(()=>RuleRecordNativePreparation.Verify(proof));await File.WriteAllBytesAsync(file,bytes);}
            var copied=RuleRecordNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"));var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>RuleRecordNativePreparation.Verify(copied));RuleRecordNativePreparation.Verify(copied,true);
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/unknown.json");await File.WriteAllTextAsync(unknown,"{}");Assert.Throws<InvalidOperationException>(()=>RuleRecordNativePreparation.Verify(copied,true));File.Delete(unknown);Assert.Throws<InvalidOperationException>(()=>RuleRecordNativePreparation.CopyForExecution(proof,copied.PackageRoot));RuleRecordNativePreparation.Verify(proof);
            var exported=Environment.GetEnvironmentVariable("METARILL_RULE_RECORD_PREPARE_OUTPUT");if(!string.IsNullOrEmpty(exported))
            {Assert.Equal(CSharpGenerator.SafePath(source,".local/rule-record-native/prepared"),Path.GetFullPath(exported));var output=RuleRecordNativePreparation.CopyForExecution(proof,exported);await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(exported)!,"prepared-receipt.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));}
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
