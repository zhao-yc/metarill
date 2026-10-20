using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>只验证受控模型/原页源/真实生成编译，不使用数据库、服务或私有设置。</summary>
public sealed class TrustedSecurityManagementModuleTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly string[] PageIds=["6031906822946816","1692532858529329154","1696799309006348290"];
    private static readonly string[] TableIds=["6031906809708544","1692532858499969026","1696799308939239425"];

    [Fact]
    public void Fixed_factory_keeps_original_pages_and_composes_physical_shape_once_without_mutation()
    {
        var application=Read("demo"); var before=Serialize(application); var source=Read("platform");
        var models=Compose(application); var physical=models.Physical; var published=models.Published;
        Assert.Equal(before,Serialize(application)); Assert.Equal(10,physical.ConfigTables.Count); Assert.Equal(10,published.ConfigTables.Count);
        Assert.Equal(6,published.ConfigTables.Count(table=>Read("security").ConfigTables.Any(item=>item.Id==table.Id)));
        Assert.Equal(application.ConfigPages.Count,physical.ConfigPages.Count); Assert.Equal(application.ConfigPages.Count+3,published.ConfigPages.Count);
        Assert.Equal(62,published.ConfigPageColumns.Count(field=>PageIds.Contains(field.ConfigPageId)));
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(physical,"module_fixture")),MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(published,"module_fixture")));
        Assert.All(published.ConfigTables,table=>Assert.Equal(application.YzProjectId,table.YzProjectId));
        foreach(var pageId in PageIds)
        {
            var expected=source.ConfigPages.Single(page=>page.Id==pageId); var actual=published.ConfigPages.Single(page=>page.Id==pageId);
            RebindExpected(expected,application); expected.YzAdminTypeId=application.YzAdminTypes[0].Id;
            Assert.Equal(Serialize(expected),Serialize(actual));
            foreach(var original in source.ConfigPageColumns.Where(field=>field.ConfigPageId==pageId))
            {
                var actualField=published.ConfigPageColumns.Single(field=>field.Id==original.Id); RebindExpected(original,application);
                Assert.Equal(Serialize(original),Serialize(actualField));
            }
        }
        foreach(var original in source.ConfigColumns.Where(column=>TableIds.Contains(column.ConfigTableId)))
        {
            RebindExpected(original,application);
            Assert.Equal(Serialize(original),Serialize(published.ConfigColumns.Single(column=>column.Id==original.Id)));
        }
        Assert.Equal("security-management-v1",models.Binding.ModuleId); Assert.Equal(64,models.Binding.ModuleSha256.Length);
        Assert.Equal(application.YzProjectId,models.Binding.ProjectId); Assert.Equal(application.YzAdminTypes[0].Id,models.Binding.ManagementAdminTypeId);
    }

    [Fact]
    public void Explicit_second_admin_binds_only_three_pages_and_their_menu_tree_without_changing_business_admin()
    {
        var application=Read("demo"); var original=application.YzAdminTypes[0].Id;
        const string second="2300000000000000099";
        application.YzAdminTypes.Add(new(){Id=second,Name="第二管理后台",YzProjectId=application.YzProjectId});
        var models=SecurityManagementModuleFactory.Compose(application,second); var published=models.Published;
        Assert.Equal(2,published.YzAdminTypes.Count);
        Assert.All(published.ConfigPages.Where(page=>PageIds.Contains(page.Id)),page=>Assert.Equal(second,page.YzAdminTypeId));
        Assert.All(published.ConfigPages.Where(page=>!PageIds.Contains(page.Id)),page=>Assert.Equal(original,page.YzAdminTypeId));
        var originalMenus=application.ConfigMenus.Select(menu=>menu.Id).ToHashSet();
        Assert.All(published.ConfigMenus.Where(menu=>!originalMenus.Contains(menu.Id)),menu=>Assert.Equal(second,menu.YzAdminTypeId));
        Assert.All(published.ConfigMenus.Where(menu=>originalMenus.Contains(menu.Id)),menu=>Assert.Equal(original,menu.YzAdminTypeId));
        Assert.All(published.ConfigCustomPages,page=>Assert.Equal(second,page.YzAdminTypeId));
        Assert.Equal(3,published.ConfigPages.Count(page=>PageIds.Contains(page.Id)));
    }

    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("foreign-project")][InlineData("noncanonical-id")]
    [InlineData("reserved-name")][InlineData("reserved-table-id")][InlineData("reserved-field-id")][InlineData("reserved-page-id")]
    [InlineData("cross-field")][InlineData("wrong-field-table")][InlineData("foreign-menu")][InlineData("menu-cycle")]
    public void Business_claims_and_broken_ownership_cannot_request_trusted_module(string change)
    {
        var application=Read("demo"); var admin=application.YzAdminTypes[0].Id;
        switch(change)
        {
            case "missing": admin="42"; break;
            case "duplicate": application.YzAdminTypes.Add(SecurityManagementModuleFactory.Clone(application.YzAdminTypes[0])); break;
            case "foreign-project": application.YzAdminTypes[0].YzProjectId="42"; break;
            case "noncanonical-id": admin="0"+admin; break;
            case "reserved-name": application.ConfigTables[0].TableName="YZ_MANAGER"; break;
            case "reserved-table-id": application.ConfigTables[0].Id="6031906809708544"; foreach(var column in application.ConfigColumns)column.ConfigTableId=application.ConfigTables[0].Id; foreach(var page in application.ConfigPages)page.ConfigTableId=application.ConfigTables[0].Id; break;
            case "reserved-field-id": application.ConfigPageColumns[0].Id="1679698326770069506"; break;
            case "reserved-page-id": var old=application.ConfigPages[0].Id; application.ConfigPages[0].Id=PageIds[0]; foreach(var field in application.ConfigPageColumns.Where(field=>field.ConfigPageId==old))field.ConfigPageId=PageIds[0]; break;
            case "wrong-field-table":
                application.ConfigTables.Add(new(){Id="42",TableName="other_fixture",YzProjectId=application.YzProjectId});
                application.ConfigColumns.Add(new(){Id="43",ColumnName="other_name",ConfigTableId="42",YzProjectId=application.YzProjectId});
                application.ConfigPageColumns[0].ConfigColumnId="43"; break;
            case "cross-field": application.ConfigPageColumns[0].YzProjectId="42"; break;
            case "foreign-menu": application.ConfigMenus[0].YzAdminTypeId="42"; break;
            case "menu-cycle": application.ConfigMenus[0].ParentMenuId=application.ConfigMenus[0].Id; break;
        }
        // 业务自报标记没有消费者，不能免除任何冲突、所有权或引用检查。
        application.ConfigTables[0].Extra["trustedModule"]=JsonSerializer.SerializeToElement("security-management-v1");
        Assert.Throws<InvalidOperationException>(()=>SecurityManagementModuleFactory.Compose(application,admin));
    }

    [Fact]
    public void Issued_context_uses_frozen_snapshot_and_rejects_same_shape_forgery()
    {
        var models=Compose(Read("demo")); var before=Serialize(models.Published);
        models.Published.ConfigPages.Clear(); models.Physical.ConfigColumns.Clear();
        Assert.Equal(before,Serialize(SecurityManagementModuleFactory.RequirePublishedForGeneration(models)));
        var forged=new SecurityManagementModelSet(models.Physical,models.Published,models.Binding,models.PendingAdapters);
        Assert.Throws<InvalidOperationException>(()=>SecurityManagementModuleFactory.RequirePublishedForGeneration(forged));
        var second=Compose(Read("demo")); var copy=second.Published; copy.ConfigPages[0].Name="只能修改本次副本";
        Assert.Equal(before,Serialize(models.Published)); Assert.NotEqual(copy.ConfigPages[0].Name,second.Published.ConfigPages[0].Name);
    }

    [Fact]
    public void Pending_adapters_remain_precise_and_do_not_bypass_core_or_full_gate()
    {
        var models=Compose(Read("demo")); var catalog=models.Published; MetadataValidation.Validate(catalog);
        Assert.Equal(new[]{"admin-project-options","role-permission-workbench","initialize-account","security-write-runtime"},models.PendingAdapters.Select(item=>item.Code));
        Assert.DoesNotContain(catalog.ConfigPages,page=>page.ConfigTableId=="1669251289347932162");
        var model=GenerationModel.Create(catalog,"Example.TrustedModule");
        var page=model.Tables.Single(table=>table.Source.Id=="1696799308939239425").Pages.Single();
        Assert.DoesNotContain(RelationCompilation.ForPage(model,page),relation=>relation.SourceColumn.Source.ColumnName=="yz_project_id");
        Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId==PageIds[0]&&gap.Usage=="runtime");
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        Assert.Throws<InvalidOperationException>(()=>MetadataCatalogComposition.MergePhysical(catalog,Read("security")));
        Assert.Single(catalog.ConfigButtons); Assert.Equal("/admin/custom/index/init_admin",catalog.ConfigButtons[0].GetProperty("sql").GetString());
    }

    [Fact]
    public async Task Composed_original_three_page_backend_really_compiles_without_publishing_or_replacing_ui()
    {
        var models=Compose(Read("demo")); var directory=Path.Combine(Path.GetTempPath(),"metarill-security-module-"+Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=SecurityManagementModuleFactory.RequirePublishedForGeneration(models);
            var result=new CSharpGenerator().Generate(catalog,directory,"Example.TrustedModule");
            Assert.Contains(result.Warnings,warning=>warning.Contains("runtime",StringComparison.Ordinal));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.TrustedModule.Generated.Services.YzManagerService"));
            Assert.All(PageIds,pageId=>Assert.Contains(result.GeneratedFiles,file=>file.EndsWith(pageId+".vue",StringComparison.Ordinal)));
            // 本片仅生成待适配源，首次壳不含平台三手写安全规则，不能因此当作正式应用发布。
            Assert.Contains(result.CreatedHandwrittenFiles,file=>file.EndsWith("YzManagerService.cs",StringComparison.Ordinal));
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }

    /// <summary>对照只排除已确认的项目/后台归属内容适配；其余原控件与布局逐对象比较。</summary>
    private static void RebindExpected(MetadataRecord record,MetadataCatalog application)
    {
        record.YzProjectId=application.YzProjectId;
        if(record.Extra.ContainsKey("yzProjectName"))record.Extra["yzProjectName"]=JsonSerializer.SerializeToElement(application.YzProjectName);
        if(record.Extra.ContainsKey("yzAdminTypeName"))record.Extra["yzAdminTypeName"]=JsonSerializer.SerializeToElement(application.YzAdminTypes[0].Name);
    }
    private static SecurityManagementModelSet Compose(MetadataCatalog catalog)=>SecurityManagementModuleFactory.Compose(catalog,catalog.YzAdminTypes[0].Id);
    private static MetadataCatalog Read(string name,[CallerFilePath]string source="")=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../metadata",name+".json"))),Json)!;
    private static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Json);
}
