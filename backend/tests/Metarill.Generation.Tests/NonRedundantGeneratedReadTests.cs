using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式生成入口的读写分离与手写保护；真实HTTP/SQL另测。</summary>
public sealed class NonRedundantGeneratedReadTests
{
    /// <summary>实体忽略派生写列，Service使用固定读计划，原完整表单回显继续可回传。</summary>
    [Fact]
    public async Task Official_generation_builds_nonredundant_crud_and_protects_handwritten_partial()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-generated",Guid.NewGuid().ToString("N"));var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");NonRedundantGeneratedFixture.AddSelectionPage(catalog);catalog.ConfigPages[0].IsExport=1;NonRedundantGeneratedFixture.EnableImports(catalog);
        try
        {
            var generated=Path.Combine(root,"generated");var first=new CSharpGenerator().Generate(catalog,generated,"Example.NonRedGenerated");Assert.NotEmpty(first.GeneratedFiles);Assert.DoesNotContain("GeneratedNonRedundantReadCatalog.g.cs",first.GeneratedFiles);Assert.Contains("GeneratedNonRedundantReadCatalog",await File.ReadAllTextAsync(Path.Combine(generated,"GeneratedRegistration.g.cs")));
            var entity=await File.ReadAllTextAsync(Path.Combine(generated,"Entities/MtNrgOfflineOrder.g.cs"));Assert.Contains("Name = \"customer_notes\", IsIgnore = true",entity);Assert.Contains("Name = \"customer_amount\", IsIgnore = true",entity);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtNrgOfflineOrderService.g.cs"));Assert.Contains("GeneratedNonRedundantReadCatalog.Compile",service);Assert.Contains("SelectionSelected102Async",service);Assert.Contains("selectedRows.Records.Count!=selected.Length",service);Assert.DoesNotContain("Query102(",service);Assert.Contains("await Detail101Async(entity.Id,parentId,actor,cancellationToken)",service);Assert.DoesNotContain("entity.CustomerNotes = source.CustomerNotes",service);
            var vo=await File.ReadAllTextAsync(Path.Combine(generated,"VOs/MtNrgOfflineOrderPage101Vo.g.cs"));Assert.Contains("customerName",vo);Assert.DoesNotContain("YzProjectId",vo);Assert.DoesNotContain("GroupId",vo);
            var handwritten=Path.Combine(generated,"Services/MtNrgOfflineOrderService.cs");await File.AppendAllTextAsync(handwritten,"\n// 本次手写保护标记。\n");var before=await File.ReadAllBytesAsync(handwritten);new CSharpGenerator().Generate(catalog,generated,"Example.NonRedGenerated");Assert.Equal(before,await File.ReadAllBytesAsync(handwritten));
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    /// <summary>未接线原功能不能以静默退化方式生成；公开完整包仍保留能力门槛。</summary>
    [Theory]
    [InlineData("customImport")][InlineData("global")][InlineData("select")][InlineData("filterType")][InlineData("conflictingTarget")][InlineData("effectiveOverride")]
    public void Unsupported_usages_still_reject_before_files(string usage)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");var page=catalog.ConfigPages[0];if(usage=="customImport"){page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("custom");}else if(usage=="global")catalog.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");else if(usage=="select")catalog.ConfigColumns.Single(column=>column.ColumnName=="customer_id").ControlKind="SelectPage";else if(usage=="filterType")catalog.ConfigColumns.Single(column=>column.Id=="14").LogicalType="json";else if(usage=="conflictingTarget")catalog.ConfigColumns.Single(column=>column.ColumnName=="customer_id").LinkConfigTableId="3";else catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"notes\"}");
        var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-reject",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.NonRedGenerated"));Assert.False(Directory.Exists(root));
    }
    /// <summary>自动名称即使被标为可编辑，也不能成为物理导入列。</summary>
    [Fact]
    public void Nonredundant_import_rejects_conflicting_display_columns_before_files()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");catalog.ConfigPages[0].IsImport=1;
        catalog.ConfigColumns.Add(new(){Id="111",ConfigTableId="1",ColumnName="customer_name",Name="客户名称",LogicalType="string",ColumnLength="100"});
        catalog.ConfigPageColumns.Add(new(){Id="101111",ConfigPageId="101",ConfigColumnId="111",IsEdit=1});
        var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-import-physical",Guid.NewGuid().ToString("N"));
        var error=Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.NonRedImport"));
        Assert.Contains("关联显示别名与真实业务字段冲突",error.Message);Assert.False(Directory.Exists(directory));
    }
    /// <summary>选择页不能以未声明项目归属的表形成不受限回读。</summary>
    [Fact]
    public void Nonredundant_selection_without_project_scope_rejects_before_writes()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");NonRedundantGeneratedFixture.AddSelectionPage(catalog);catalog.ConfigColumns.RemoveAll(column=>column.Id=="12");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="12");var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-selection-scope",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.NonRedSelection"));Assert.False(Directory.Exists(directory));
    }
    /// <summary>无有效变化的原页覆盖继续兼容，不能因JSON格式不同拒绝。</summary>
    [Theory]
    [InlineData("{}")][InlineData("{ \"nameField\": \"name\", \"table\": \"mt_nrg_offline_party\", \"tableId\": \"2\" }")]
    public void Equivalent_page_overrides_keep_the_same_relation(string overlay)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay);var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-overrides",Guid.NewGuid().ToString("N"));try{Assert.NotEmpty(new CSharpGenerator().Generate(catalog,root,"Example.NonRedGenerated").GeneratedFiles);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}

/// <summary>同一元数据样本同时驱动生成、DDL及实际HTTP，隐藏项目/父键不加入VO。</summary>
internal static class NonRedundantGeneratedFixture
{
    /// <summary>所有表和页面来自同一测试元数据，不手写业务CRUD。</summary>
    internal static MetadataCatalog Catalog(string prefix)
    {
        var catalog=new MetadataCatalog{YzProjectId="54",ConfigTables=[new(){Id="1",TableName=prefix+"_order",IsRongyu=0},new(){Id="2",TableName=prefix+"_party"},new(){Id="3",TableName=prefix+"_group"}],YzAdminTypes=[new(){Id="5",YzProjectId="54"}],ConfigPages=[new(){Id="101",ConfigTableId="1",YzAdminTypeId="5",WhereSql="group_id=@parent_id",OrderBy="customer_notes asc,id desc",IsCopy=1,Extra=new(){{"isSum",JsonSerializer.SerializeToElement(1)}}},new(){Id="201",ConfigTableId="2",YzAdminTypeId="5"},new(){Id="301",ConfigTableId="3",YzAdminTypeId="5"}]};
        catalog.ConfigColumns=[C("11","1","id","int64"),C("12","1","yz_project_id","int64"),C("13","1","delete_no","datetime"),C("14","1","name"),C("15","1","customer_id","int64"),C("16","1","customer_notes"),C("17","1","customer_amount","decimal"),C("18","1","status","int32"),C("19","1","group_id","int64"),C("110","1","private_payload"),C("21","2","id","int64"),C("22","2","yz_project_id","int64"),C("23","2","delete_no","datetime"),C("24","2","name"),C("25","2","notes"),C("26","2","amount","decimal"),C("31","3","id","int64"),C("32","3","yz_project_id","int64"),C("33","3","delete_no","datetime"),C("34","3","name")];
        var customer=catalog.ConfigColumns.Single(column=>column.Id=="15");customer.ControlKind="List";customer.LinkConfigTableId="2";customer.ControlDetail=JsonSerializer.Serialize(new{tableId="2",table=prefix+"_party",nameField="name"});catalog.ConfigColumns.Single(column=>column.Id=="19").LinkConfigTableId="3";
        foreach(var (id,path) in new[]{("16","customer.notes"),("17","customer.amount")})catalog.ConfigColumns.Single(column=>column.Id==id).Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement(path)}};
        foreach(var page in catalog.ConfigPages)foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId))
        {var visible=column.ColumnName is not("yz_project_id" or "group_id" or "delete_no");catalog.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsShow=visible&&column.ColumnName!="private_payload"?1:0,IsEdit=visible?1:0,Extra=new(){{"isSearch",JsonSerializer.SerializeToElement(column.ColumnName is "name" or "customer_notes"?1:0)},{"isSort",JsonSerializer.SerializeToElement(visible?1:0)},{"sumKind",JsonSerializer.SerializeToElement(column.ColumnName=="customer_amount"?1:column.ColumnName=="status"?2:0)}}});}
        return catalog;
    }
    /// <summary>原type=2独立页只增加读取合同，不启用多值关系字段或改变前端流程。</summary>
    internal static void AddSelectionPage(MetadataCatalog catalog)
    {
        var source=catalog.ConfigPages.Single(page=>page.Id=="101");var selection=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;selection.Id="102";selection.IsNew=0;selection.IsSave=0;selection.IsDelete=0;selection.IsCopy=0;selection.Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigPages.Add(selection);
        var fields=catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray();foreach(var field in fields){field.Id="102"+field.Id;field.ConfigPageId="102";catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>原单选页与显式派生辅助名；可选同父候选验证实际父范围接线。</summary>
    internal static void EnableSingleSelection(MetadataCatalog catalog,bool targetParent)
    {
        var source=catalog.ConfigColumns.Single(column=>column.Id=="15");source.ControlKind="SelectPage";
        source.ControlDetail=JsonSerializer.Serialize(new{pageId="201",tableId="2",table=catalog.ConfigTables.Single(table=>table.Id=="2").TableName,nameField="name"});
        catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var display=C("111","1","customer_name");display.Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("customer.name")}};catalog.ConfigColumns.Add(display);
        catalog.ConfigPageColumns.Add(new(){Id="101111",ConfigPageId="101",ConfigColumnId="111",IsEdit=1,IsShow=0});
        if(targetParent)
        {
            var parent=C("27","2","group_id","int64");parent.LinkConfigTableId="3";catalog.ConfigColumns.Add(parent);
            catalog.ConfigPageColumns.Add(new(){Id="20127",ConfigPageId="201",ConfigColumnId="27"});catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="group_id=@parent_id";
        }
    }
    /// <summary>独立目标父来自本对象物理外键，不借来源parentId；整行导入通过独立开关启用。</summary>
    internal static void AddMappedSinglePage(MetadataCatalog catalog)
    {
        var source=catalog.ConfigPages.Single(page=>page.Id=="101");var mapped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        mapped.Id="104";mapped.WhereSql="";mapped.IsImport=0;mapped.Extra.Remove("importMethod");catalog.ConfigPages.Add(mapped);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray())
        {field.Id="104"+field.Id;field.ConfigPageId="104";if(field.ConfigColumnId=="19")field.IsEdit=1;catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>跨父追加与覆盖保持同一Excel接口，目标父来自整行或锁定原对象。</summary>
    internal static void EnableMappedImports(MetadataCatalog catalog)
    {
        var group=catalog.ConfigColumns.Single(column=>column.Id=="19");group.ControlKind="List";group.DefaultValue="71";
        group.ControlDetail=JsonSerializer.Serialize(new{tableId="3",table=catalog.ConfigTables.Single(table=>table.Id=="3").TableName,nameField="name"});
        var source=catalog.ConfigPages.Single(page=>page.Id=="104");source.IsImport=1;
        var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        overwrite.Id="105";overwrite.IsNew=0;overwrite.IsSave=0;overwrite.IsDelete=0;overwrite.IsCopy=0;overwrite.IsExport=0;
        overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(overwrite);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="104").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray())
        {field.Id="105"+field.Id;field.ConfigPageId="105";catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>追加与覆盖分别沿用原导入开关；覆盖页不额外开放普通新增/编辑按钮。</summary>
    internal static void EnableImports(MetadataCatalog catalog)
    {
        var source=catalog.ConfigPages.Single(page=>page.Id=="101");source.IsImport=1;
        catalog.ConfigColumns.Single(column=>column.Id=="14").Name="记录名";
        // 原V8覆盖键按表字段排序选首个可导入列，页面排序不能另换匹配键。
        catalog.ConfigColumns.Single(column=>column.Id=="14").SortNo=-1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="14").SortNo=-1;
        var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        overwrite.Id="103";overwrite.IsNew=0;overwrite.IsSave=0;overwrite.IsDelete=0;overwrite.IsCopy=0;overwrite.IsExport=0;overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(overwrite);
        var fields=catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray();
        foreach(var field in fields){field.Id="103"+field.Id;field.ConfigPageId="103";catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>只声明逻辑类型和长度，具体DDL仍走正式SchemaService。</summary>
    private static ColumnMetadata C(string id,string table,string name,string type="string")=>new(){Id=id,ConfigTableId=table,ColumnName=name,Name=name,LogicalType=type,ColumnLength="100",IsDbNotNull=name=="id"?1:0};
}
