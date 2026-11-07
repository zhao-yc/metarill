using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式宿主验收前先证明四种原控件可共存于同一真实元数据，避免实库才发现未接线模型。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedRelationsFreshCatalogTests
{
    /// <summary>显式与自动数组分别生成编译，所有原名称筛选键均实际绑定。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Mixed_derived_relation_catalog_compiles_before_fresh_database(bool automatic)
    {
        var c=Catalog(automatic);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-fresh-catalog");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedFresh");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));
            var model=GenerationModel.Create(c,"Example.DerivedFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));var table=model.Tables.Single(table=>table.Source.Id=="2");var names=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="201"));Assert.Empty(names.Gaps);Assert.Equal(new[]{"multipleNames","singleName"},names.Bindings.Select(binding=>binding.FilterKey).Order());
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>保留原数据库复选、List、type1单选及type2多选；真实来源父与目标固定条件共同使用。</summary>
    internal static MetadataCatalog Catalog(bool automatic)
    {
        var c=DerivedCheckboxNamesTests.Catalog("derived_fresh",automatic);if(automatic){var defaults=c.ConfigColumns.Single(column=>column.Id=="25");using var parsed=JsonDocument.Parse(defaults.DefaultValue);defaults.DefaultValue=JsonSerializer.Serialize(parsed.RootElement,new JsonSerializerOptions{WriteIndented=true,NewLine="\n"});}var target=c.ConfigPages.Single(page=>page.Id=="301");target.WhereSql+=" AND segment=1 AND status=1";
        foreach(var (id,type) in new[]{("303",1),("304",0)})
        {
            var clonePage=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(target))!;clonePage.Id=id;clonePage.Extra["type"]=JsonSerializer.SerializeToElement(type);c.ConfigPages.Add(clonePage);
            foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="301").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id=id+field.Id;clone.ConfigPageId=id;c.ConfigPageColumns.Add(clone);}
        }
        foreach(var (id,column,label,kind,page,where) in new[]{("210","customer_id","下拉客户","List","304","parent_id=@parentId AND segment=@formParameters.segment AND status=1"),("211","single_id","单选客户","SelectPage","303","parent_id=@parentId AND segment=1 AND status=1"),("212","multiple_ids","多选客户","MultiSelectPage","301","")})
        {
            c.ConfigColumns.Add(new(){Id=id,ConfigTableId="2",ColumnName=column,Name=label,LogicalType=kind=="MultiSelectPage"?"string":"int64",ColumnLength=kind=="MultiSelectPage"?"max":"",LinkConfigTableId="3",ControlKind=kind,ControlDetail=JsonSerializer.Serialize(new{pageId=page,tableId="3",table="derived_fresh_target",nameField="name",whereSql=where,buttonText="选择客户"})});
            c.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        }
        c.ConfigColumns.Single(column=>column.Id=="210").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("304");
        c.ConfigColumns.Add(new(){Id="214",ConfigTableId="2",ColumnName="single_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("single.name")}});c.ConfigPageColumns.Add(new(){Id="201214",ConfigPageId="201",ConfigColumnId="214",IsEdit=0});
        c.ConfigColumns.Add(new(){Id="215",ConfigTableId="2",ColumnName="customer_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("customer.name")}});c.ConfigPageColumns.Add(new(){Id="201215",ConfigPageId="201",ConfigColumnId="215",IsEdit=0,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        if(!automatic){c.ConfigColumns.Add(new(){Id="213",ConfigTableId="2",ColumnName="multiple_names",LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("multiple.name")}});c.ConfigPageColumns.Add(new(){Id="201213",ConfigPageId="201",ConfigColumnId="213"});}
        foreach(var page in new[]{"201","301","303","304","401"})c.ConfigMenus.Add(new(){Id=(900+int.Parse(page)).ToString(),YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="正式派生关系"+page,Kind="GENERATED_PAGE",ConfigPageId=page});
        foreach(var record in c.ConfigColumns.Cast<MetadataRecord>().Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;
        return c;
    }
}
