using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>关系计划随项目源码正规生成，不依赖平台进程或后处理补丁。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RelationPropagationGenerationTests
{
    /// <summary>两类普通控件的显式／自动缓存生成同一静态传播入口，消费者保存仍走原服务。</summary>
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task Generated_project_compiles_static_plan_and_target_hook(bool checkbox,bool automatic)
    {
        var catalog=checkbox?DatabaseCheckboxGenerationTests.Catalog(false,automatic):MultiPageNameCacheTests.Catalog(automatic,false);var boundary=Path.Combine(Path.GetTempPath(),"metarill-propagation-generation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,"Example.PropagationPlan");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var registration=File.ReadAllText(Path.Combine(output,"GeneratedRegistration.g.cs"));Assert.Contains("GeneratedRelationMutation",registration);Assert.Contains("RelationMutationPlan.Compile",registration);var target=File.ReadAllText(Path.Combine(output,"Services/RelationTargetService.g.cs"));Assert.Contains("AfterMutationAsync",target);Assert.DoesNotContain("AfterMutationAsync",File.ReadAllText(Path.Combine(output,"Services/RelationParentService.g.cs")));using var capabilities=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"generation-capabilities.json")));Assert.All(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),item=>Assert.True(item.GetProperty("renamePropagationSupported").GetBoolean()));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整应用从包内Foundation源码构建传播器，再生成与更新不覆盖手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_contains_same_executor_and_preserves_handwritten_code(bool checkbox)
    {
        var catalog=RedundantMultiNameCacheTests.Published(checkbox?DatabaseCheckboxGenerationTests.Catalog(false,true):MultiPageNameCacheTests.Catalog(true,false));catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="关系传播",Kind="GENERATED_PAGE",ConfigPageId="201"});var boundary=Path.Combine(Path.GetTempPath(),"metarill-propagation-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            new ApplicationPackager().Create(catalog,output,"Example.PropagationApplication",NonRedundantApplicationFixture.Root());foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);foreach(var file in new[]{"RelationMutationPlan.cs","RelationMutationPropagator.cs","MySqlRelationMutationExecutor.cs"})Assert.Equal(File.ReadAllBytes(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/src/Metarill.Data/Relations",file)),File.ReadAllBytes(Path.Combine(output,"src/Metarill.Data/Relations",file)));var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 传播下发后手写业务保持。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.PropagationApplication",NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,File.ReadAllBytes(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
