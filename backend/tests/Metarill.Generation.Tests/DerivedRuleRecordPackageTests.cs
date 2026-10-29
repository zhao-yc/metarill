using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>派生规则的新实体合同必须随完整应用和更新包一起交付，不能依赖开发仓库程序集。</summary>
public sealed class DerivedRuleRecordPackageTests
{
    /// <summary>真实原元数据生成完整包、独立构建并由原更新器应用，保护partial及重复更新。</summary>
    [Fact]
    public async Task Derived_rule_full_package_builds_and_updates_from_its_own_foundations()
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_rule_package");
        var page=catalog.ConfigPages.Single(page=>page.Id=="101");page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("customerNotes == '禁止'");page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-rule-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            new ApplicationPackager().Create(catalog,output,"Example.DerivedPackage",NonRedundantApplicationFixture.Root());
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Contracts/IGeneratedRuleRecord.cs")));
            var handwritten=Path.Combine(output,"backend/generated/Services/MtRulePackageOrderService.cs");await File.AppendAllTextAsync(handwritten,"\n// 本轮派生规则手写扩展保护。\n");var preserved=await File.ReadAllBytesAsync(handwritten);
            var candidate=Path.Combine(directory,"candidate");catalog.ConfigColumns.Single(column=>column.Id=="14").Name="规则记录名称";
            new ApplicationPackager().Create(catalog,candidate,"Example.DerivedPackage",NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(preserved,await File.ReadAllBytesAsync(handwritten));
            var repeat=updater.Preview(output,candidate,operations);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
