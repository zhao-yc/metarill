using Metarill.Data;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>维护计划与主生成链、Foundation和完整宿主同步交付；不靠平台运行时解释私有配置。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RelationMaintenanceGenerationTests
{
    /// <summary>两种多值控件、显式／自动和页级多个名称缓存均实际编译为可执行维护计划。</summary>
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task Generated_maintenance_plan_compiles_all_declared_and_automatic_names(bool checkbox,bool automatic)
    {
        var metadata=checkbox?DatabaseCheckboxGenerationTests.Catalog(false,automatic):MultiPageNameCacheTests.Catalog(automatic,false);var boundary=Path.Combine(Path.GetTempPath(),"metarill-maintenance-generation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(metadata,Path.Combine(directory,"generated"),"Example.MaintenancePlan");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var plan=(RelationMaintenanceCatalog)assembly.GetType("Example.MaintenancePlan.Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;Assert.Contains(plan.Tables,item=>item.Id=="2");using var database=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused"});using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new RelationMaintenanceExecutor(database).BackfillAsync(plan,"2",cancel.Token));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>直接生成的三个关系目录同样走发布脱敏，私有扩展键不能随C#源码外泄，原模型保持不变。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Embedded_relation_catalogs_redact_private_extension_configuration(bool nonRedundant)
    {
        const string marker="metarill-private-test-marker";var metadata=DatabaseCheckboxGenerationTests.Catalog(nonRedundant,true);metadata.ConfigTables[0].Extra["apiKey"]=System.Text.Json.JsonSerializer.SerializeToElement(marker);metadata.ConfigTables[0].Extra["publicNote"]=System.Text.Json.JsonSerializer.SerializeToElement("keep-public-setting");metadata.ConfigColumns[0].Extra["dataConfig"]=System.Text.Json.JsonSerializer.SerializeToElement("{\"connectionString\":\""+marker+"\"}");var before=System.Text.Json.JsonSerializer.Serialize(metadata);var boundary=Path.Combine(Path.GetTempPath(),"metarill-maintenance-redaction");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(metadata,directory,"Example.RedactedRelations");var registration=await File.ReadAllTextAsync(Path.Combine(directory,"GeneratedRegistration.g.cs"));Assert.DoesNotContain(marker,registration);Assert.Contains("keep-public-setting",registration);Assert.Contains("GeneratedRelationMaintenance",registration);Assert.Contains(nonRedundant?"GeneratedNonRedundantReadCatalog":"GeneratedRelationMutation",registration);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(metadata));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>项目品牌和导航变化不属于关系计划，不给原配置按钮引入额外的代码生成依赖。</summary>
    [Fact]
    public async Task Branding_and_navigation_changes_do_not_modify_relation_registration()
    {
        var metadata=MultiPageNameCacheTests.Catalog(true,false);var boundary=Path.Combine(Path.GetTempPath(),"metarill-relation-config-only");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(metadata,Path.Combine(directory,"before"),"Example.RelationConfiguration");metadata.YzProjectName="配置更新项目名";metadata.PrimaryColor="#aabbcc";metadata.ConfigMenus.Add(new(){Id="9001",YzProjectId=metadata.YzProjectId,YzAdminTypeId="5",Name="仅更新导航",Kind="DIRECTORY"});new CSharpGenerator().Generate(metadata,Path.Combine(directory,"after"),"Example.RelationConfiguration");Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(directory,"before/GeneratedRegistration.g.cs")),await File.ReadAllBytesAsync(Path.Combine(directory,"after/GeneratedRegistration.g.cs")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包编译原维护控制器与同版业务／适配器，再生成更新仍保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_application_builds_original_maintenance_routes_and_preserves_handwritten_code(bool checkbox)
    {
        var metadata=RedundantMultiNameCacheTests.Published(checkbox?DatabaseCheckboxGenerationTests.Catalog(false,true):MultiPageNameCacheTests.Catalog(true,false));metadata.ConfigMenus.Add(new(){Id="9001",YzProjectId=metadata.YzProjectId,YzAdminTypeId="5",Name="业务页",Kind="GENERATED_PAGE",ConfigPageId="201"});var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-maintenance-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            new ApplicationPackager().Create(metadata,output,"Example.MaintenanceApplication",root);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"backend/src/Metarill.Api/Controllers/SystemMaintenanceController.cs")),File.ReadAllBytes(Path.Combine(output,"backend/Application.Host/SystemMaintenanceController.cs")));foreach(var file in new[]{"RelationMaintenanceCatalog.cs","RelationMaintenanceExecutor.cs"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"backend/src/Metarill.Data/Relations",file)),File.ReadAllBytes(Path.Combine(output,"src/Metarill.Data/Relations",file)));Assert.Equal(File.ReadAllBytes(Path.Combine(root,"backend/src/Metarill.Security/SystemMaintenanceBusiness.cs")),File.ReadAllBytes(Path.Combine(output,"src/Metarill.Security/SystemMaintenanceBusiness.cs")));var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 维护能力下发仍保留手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(metadata,candidate,"Example.MaintenanceApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
