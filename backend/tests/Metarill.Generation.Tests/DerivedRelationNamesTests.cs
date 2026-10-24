using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>普通R0及已发布R1来源的List使用R0目标的联接名称，候选、原导入导出和写回显必须共用同一计划。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedRelationNamesTests
{
    /// <summary>真实生成编译，派生名称不允许回退为Entity忽略属性上的物理SQL。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Derived_list_name_compiles_candidates_import_and_export(bool parentAndForm,bool cached)
    {
        var catalog=Catalog("dr_names",parentAndForm);if(cached)catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;var before=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-relation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedNames");
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            Assert.Contains("DerivedOptionsPlan101_15",service);Assert.DoesNotContain("entity.CompanyName.Contains(keyword)",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.DerivedNames.Generated.Services.DrNamesOrderService"));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.DerivedNames")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>实际完整包包含原List组件和只读名称，重新更新保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_keeps_original_list_and_protects_handwritten_service(bool contextual)
    {
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var catalog=Catalog("dr_names",contextual);
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog,output,"Example.DerivedNameApplication",root).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var component in new[]{"common/yzp_runtime/remoteRelationOptions.js","components/yzp-relation-select/yzp-relation-select.vue"})
                Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subsets=profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.GetProperty("controlKind").GetString()=="List").ToArray();Assert.Equal(2,subsets.Length);Assert.All(subsets,item=>Assert.True(item.GetProperty("derivedNameSupported").GetBoolean()));
            var hand=Path.Combine(output,"backend/generated/Services/DrNamesOrderService.cs");await File.AppendAllTextAsync(hand,"\n// 派生名称不覆盖手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.DerivedNameApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>隐藏有效关系只生成私有写校验；目标条件不能因控件不可见而消失。</summary>
    [Fact]
    public async Task Hidden_derived_relation_keeps_private_validation_without_public_options()
    {
        var catalog=Catalog("dr_names",true);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-hidden");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.HiddenDerivedName");var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));Assert.Contains("OptionsCondition101_15",service);Assert.Contains("private async Task<ISelect<DrNamesParty>> OptionsScope101_15Async",service);Assert.DoesNotContain("public Task<IReadOnlyList<GeneratedOption>> Options101_15Async",service);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明目标、派生范围及隐藏坏声明在写入stage之前拒绝，不能回退宽范围或IsIgnore SQL。</summary>
    [Theory][InlineData("global")][InlineData("derivedScope")][InlineData("virtual")][InlineData("brokenPath")][InlineData("privateName")][InlineData("unpublishedRedundantOwner")][InlineData("hiddenScope")]
    public void Invalid_derived_name_configuration_is_rejected_before_staging(string kind)
    {
        var c=Catalog("dr_names",true);
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind is "derivedScope" or "hiddenScope")c.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND company_name='X'";
        if(kind=="hiddenScope")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");
        if(kind=="virtual")c.ConfigColumns.Single(column=>column.Id=="28").IsVirtualColumn=1;
        if(kind=="brokenPath")c.ConfigColumns.Single(column=>column.Id=="28").Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="privateName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28");
        if(kind=="unpublishedRedundantOwner"){var owner=c.ConfigTables.Single(table=>table.Id=="1");owner.IsRongyu=1;owner.YzProjectId="";}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.RejectedDerivedName",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }

    /// <summary>声明同项目四表：来源订单→非冗余客户→公司名称；原字段和菜单方式均由元数据生成。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool parentAndForm)
    {
        var c=NonRedundantGeneratedFixture.Catalog(prefix);c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});
        c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="43",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="44",ConfigTableId="4",ColumnName="name",LogicalType="string"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id"});
        c.ConfigPageColumns.Add(new(){Id="40144",ConfigPageId="401",ConfigColumnId="44"});
        c.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail=JsonSerializer.Serialize(new{tableId="4",nameField="name"})});
        c.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="company_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}});
        c.ConfigColumns.Add(new(){Id="210",ConfigTableId="2",ColumnName="status",LogicalType="int32"});
        foreach(var id in new[]{"27","28","210"})c.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id});
        var relation=c.ConfigColumns.Single(column=>column.Id=="15");relation.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",table=prefix+"_party",nameField="company_name",whereSql=parentAndForm?"segment=@formParameters.segment":""});
        var target=c.ConfigPages.Single(page=>page.Id=="201");target.WhereSql="yz_project_id=@current_yz_project_id AND status=1";
        if(parentAndForm)
        {
            c.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"});c.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29"});target.WhereSql+=" AND group_id=@parent_id";
            foreach(var (id,table) in new[]{("112","1"),("211","2")}){c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName="segment",Name="分组条件",LogicalType="int32",DefaultValue="1"});c.ConfigPageColumns.Add(new(){Id=table+id,ConfigPageId=table=="1"?"101":"201",ConfigColumnId=id});}
        }
        relation.Name="客户";
        c.ConfigColumns.Add(new(){Id="113",ConfigTableId="1",ColumnName="customer_company_name",Name="客户公司",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("customer.company_name")}});
        c.ConfigPageColumns.Add(new(){Id="101113",ConfigPageId="101",ConfigColumnId="113",IsEdit=0,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1),["isSort"]=JsonSerializer.SerializeToElement(1)}});
        NonRedundantGeneratedFixture.EnableImports(c);c.ConfigPages.Single(page=>page.Id=="101").IsExport=1;
        // 隐藏页故意继承一个不可选默认FK，验证最终服务不能因控件不可见而略过候选条件。
        relation.DefaultValue="9007199254748001";
        var hidden=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(page=>page.Id=="101")))!;hidden.Id="104";hidden.IsImport=0;hidden.IsExport=0;hidden.IsCopy=0;hidden.Extra.Remove("importMethod");c.ConfigPages.Add(hidden);
        foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId!="15").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="104"+field.Id;copy.ConfigPageId="104";c.ConfigPageColumns.Add(copy);}
        RedundantMultiNameCacheTests.Published(c);return c;
    }
}
