using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原多选页与嵌入复选共享全局物理字典，元数据仍是实体和页面的唯一来源。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalMultipleFreshCatalogTests
{
    /// <summary>显式／自动名称、可选项目列和真实来源父在实际Full包中编译。</summary>
    [Theory][InlineData("cross3")][InlineData("cross5")][InlineData("cross6")][InlineData("cross7")]
    public async Task Global_multiple_controls_compile_in_original_full_package(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-catalog");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{var output=Path.Combine(directory,"application");var model=GenerationModel.Create(c,"Example.GlobalMultipleFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.True(new ApplicationPackager().Create(c,output,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);var probe=NonRedundantApplicationFixture.Start("node",output);foreach(var argument in new[]{Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalMultipleVueProbe.mjs"),output})probe.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(probe);Assert.Equal(before,JsonSerializer.Serialize(c));foreach(var path in new[]{"uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue","common/yzp_runtime/originalSelectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin",path)),File.ReadAllBytes(Path.Combine(output,"frontend",path)));}finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>按原自动回传标志，复选首次读取实际表单值，字段变更隔离旧响应。</summary>
    [Theory]
    [InlineData("segment")]
    [InlineData("gsSingleSource.segment")]
    public async Task Linked_checkbox_loads_initial_form_values_and_ignores_stale_candidates(string slot)
    {
        var catalog=Catalog("cross3");
        var segment=catalog.ConfigColumns.Single(column=>column.Id=="2106");
        segment.Extra["isAutoPostBack"]=JsonSerializer.SerializeToElement(1);
        var checkbox=catalog.ConfigColumns.Single(column=>column.Id=="2204");
        checkbox.Extra["isAutoPostBackQuery"]=JsonSerializer.SerializeToElement(1);
        checkbox.ControlDetail=checkbox.ControlDetail.Replace("@formParameters.segment","@formParameters."+slot,StringComparison.Ordinal);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-linked-checkbox-vue");
        var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.LinkedCheckbox");
            var probe=NonRedundantApplicationFixture.Start("node",output);
            probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GeneratedLinkedCheckboxProbe.mjs"));
            probe.ArgumentList.Add(output);
            await NonRedundantApplicationFixture.RunAsync(probe);
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }


    /// <summary>隐藏全局复选也不能绕过发布证明；全局消费者、缓存目标和矛盾覆盖仍拒绝。</summary>
    [Theory][InlineData("ownerGlobal")][InlineData("targetR0WithoutSelectionPage")][InlineData("cachedName")][InlineData("unpublished")][InlineData("variable")][InlineData("multiPage")][InlineData("checkboxPage")][InlineData("hiddenBad")]
    public async Task Unproven_global_multiple_rejects_before_package_staging(string kind)
    {
        var c=Catalog("cross3");
        // R0 已支持；拒绝的是缺少原 type=2 多选候选页的全局 R0 关系。
        if(kind=="targetR0WithoutSelectionPage")
        {
            var target=c.ConfigTables.Single(table=>table.Id=="3");
            target.IsRongyu=0;
            c.ConfigPages.Single(page=>page.Id=="302").Extra["type"]=JsonSerializer.SerializeToElement(1);
            var owner=c.ConfigTables.Single(table=>table.Id=="2");
            var foreign=c.ConfigColumns.Single(column=>column.Id=="2202");
            var error=Assert.Throws<NotSupportedException>(()=>GlobalMultipleRelationTarget.Require(c,owner,foreign,target));
            Assert.Equal("全局R0多选要求原type=2页面；复选继续使用独立候选入口。",error.Message);
        }
        if(kind=="ownerGlobal")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="cachedName")c.ConfigColumns.Single(x=>x.ConfigTableId=="3"&&x.ColumnName=="name").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="unpublished")c.ConfigPages.Single(x=>x.Id=="302").YzProjectId="";if(kind=="variable")c.ConfigPages.Single(x=>x.Id=="302").WhereSql="status=1 AND yz_project_id=@current_yz_project_id";if(kind=="multiPage")c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="2202").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"301\"}");if(kind=="checkboxPage")c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="2204").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"tableId\":\"3\",\"pageId\":\"302\"}");if(kind=="hiddenBad")c.ConfigColumns.Single(x=>x.Id=="2208").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("missing");var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidGlobalMultiple",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));}finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>四个发布模型覆盖显式／自动名称及目标项目列／来源父，不把租约槽位当成键类型。</summary>
    internal static (bool Automatic,bool NoProject,bool Parent) Definition(string profile)=>profile switch{"cross3"=>(false,false,false),"cross5"=>(true,true,true),"cross6"=>(true,false,false),"cross7"=>(false,true,true),_=>throw new ArgumentOutOfRangeException(nameof(profile))};
    /// <summary>源、普通候选与父继续项目隔离，全局只声明在共享目标。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var c=GlobalSingleFreshCatalogTests.Catalog(profile);c.YzProjectName="全局多值正式验收";var remove=new[]{"2190","2191","2192","2194","2195","2197","2198","2200","2201"};c.ConfigColumns.RemoveAll(x=>remove.Contains(x.Id)||x.ConfigTableId=="6");c.ConfigPageColumns.RemoveAll(x=>remove.Contains(x.ConfigColumnId)||x.ConfigPageId is "601" or "303");c.ConfigTables.RemoveAll(x=>x.Id=="6");c.ConfigPages.RemoveAll(x=>x.Id is "601" or "303");c.ConfigMenus.RemoveAll(x=>x.ConfigPageId is "601" or "303");var shared=c.ConfigColumns.Single(x=>x.ConfigTableId=="3"&&x.ColumnName==GlobalSingleFreshCatalogTests.Definition(profile).NameField);shared.ColumnName="name";
        foreach(var(id,page,target)in new[]{("302","301","3"),("402","401","4")}){var copy=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(x=>x.Id==page)))!;copy.Id=id;copy.Extra["type"]=JsonSerializer.SerializeToElement(2);if(target=="3")copy.WhereSql="status=1 AND segment=1";c.ConfigPages.Add(copy);foreach(var f in c.ConfigPageColumns.Where(x=>x.ConfigPageId==page).ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(f))!;clone.Id=id+f.Id;clone.ConfigPageId=id;c.ConfigPageColumns.Add(clone);}c.ConfigMenus.Add(new(){Id="700"+id,YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="全局多值"+id,Kind="GENERATED_PAGE",ConfigPageId=id});}
        foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId is "301" or "302"&&field.ConfigColumnId==shared.Id))field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        Relation("2202","shared_ids","共享多选","MultiSelectPage","3","302","");Relation("2204","checked_ids","共享复选","MultiCheckBoxDatabase","3","301","status=1 AND segment=@formParameters.segment");Relation("2206","tenant_ids","项目多选","MultiSelectPage","4","402","");Relation("2208","hidden_ids","隐藏复选","MultiCheckBoxDatabase","3","301","status=1 AND segment=1");c.ConfigColumns.Single(x=>x.Id=="2208").DefaultValue="[\"9007199254745001\"]";c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="2208");if(!d.Automatic)foreach(var(id,prefix,label)in new[]{("2203","shared","多选名称"),("2205","checked","复选名称"),("2207","tenant","项目名称")}){var name=Column(id,prefix+"_names",label);name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement(prefix+".name");c.ConfigPageColumns.Add(new(){Id="201"+id,YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId=id,IsEdit=0,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(id=="2205"?1:0)}});}return c;
        /// <summary>原控件配置按实际目标表页保存，复选自己的where完整保留。</summary>
        void Relation(string id,string field,string label,string kind,string table,string page,string where){var col=Column(id,field,label);col.ControlKind=kind;col.LinkConfigTableId=table;col.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(page);col.ControlDetail=JsonSerializer.Serialize(new{tableId=table,table=c.ConfigTables.Single(x=>x.Id==table).TableName,pageId=page,nameField="name",whereSql=where,orderBy=kind=="MultiCheckBoxDatabase"?"name asc":"",buttonText="选择记录"});c.ConfigPageColumns.Add(new(){Id="201"+id,YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId=id,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});}
        /// <summary>只定义合成验收元数据，不手写实体或CRUD。</summary>
        ColumnMetadata Column(string id,string field,string label){var col=new ColumnMetadata{Id=id,YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName=field,Name=label,LogicalType="string",ColumnLength="max"};c.ConfigColumns.Add(col);return col;}
    }
}
