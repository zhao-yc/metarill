using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>混合关系计划随独立包和Full更新一起交付，原页面与手写partial保持归属。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class MixedRelationPropagationGenerationTests
{
    /// <summary>源R1、中间R0、后续R1和多值均被同一个编译目录接线，不靠平台专用解释器。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_application_builds_mixed_plan_and_updates_runtime_preserving_handwritten(bool logical)
    {
        var metadata=RedundantMultiNameCacheTests.Published(MixedRelationPropagationMySqlTests.Catalog("mixed_package",logical));metadata.ConfigMenus.Add(new(){Id="9001",YzProjectId=metadata.YzProjectId,YzAdminTypeId="5",Name="原业务页",Kind="GENERATED_PAGE",ConfigPageId="101"});var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-mixed-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            new ApplicationPackager().Create(metadata,output,"Example.MixedApplication",root);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);foreach(var file in new[]{"MixedRelationMutationPlan.cs","MySqlMixedRelationMutationExecutor.cs","ProjectRelationMutationPropagator.cs","RelationMaintenanceExecutor.cs"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"backend/src/Metarill.Data/Relations",file)),File.ReadAllBytes(Path.Combine(output,"src/Metarill.Data/Relations",file)));var service=await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/Services/MixedPackageProviderService.g.cs"));Assert.Contains("ProjectRelationMutationPropagator",service);var hand=Path.Combine(output,"backend/generated/Services/MixedPackageSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 混合关系更新保护手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(metadata,candidate,"Example.MixedApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(output,"frontend/pages/yzp/mixed_package_source-detail-101.vue")),await File.ReadAllBytesAsync(Path.Combine(candidate,"frontend/pages/yzp/mixed_package_source-detail-101.vue")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
