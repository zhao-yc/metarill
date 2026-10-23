using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>五种原关系同页先离线编译，正式双库不能靠临时修改生成文件接通。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedSelfTreeFreshCatalogTests
{
    /// <summary>显式字符串叶子与自动int64叶子使用相反父键类型，元数据输入保持不变。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Five_original_relation_controls_compile_before_fresh_install(bool automatic)
    {
        var c=Catalog(automatic);Assert.Equal("5",Assert.Single(c.YzAdminTypes).Id);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-tree-fresh-catalog");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedFresh");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));var model=GenerationModel.Create(c,"Example.DerivedFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));var source=model.Tables.Single(x=>x.Source.Id=="2");var tree=Assert.Single(CascadeTreeReadCompilation.ForPage(model,source,source.Pages.Single(x=>x.Source.Id=="201")));Assert.True(tree.DerivedName);Assert.Equal("501",tree.TargetPage.Source.Id);}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>只增加独立R0树及标准状态维护页，原四控件模型完全沿用。</summary>
    internal static MetadataCatalog Catalog(bool automatic)
    {
        var c=DerivedRelationsFreshCatalogTests.Catalog(automatic);c.ConfigTables.Add(new(){Id="5",TableName="derived_fresh_tree",IsRongyu=0});
        c.ConfigColumns.AddRange([
            new(){Id="51",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="52",ConfigTableId="5",ColumnName="name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}},
            new(){Id="53",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="54",ConfigTableId="5",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="55",ConfigTableId="5",ColumnName="parent_derived_fresh_tree_id",LogicalType=automatic?"string":"int64",LinkConfigTableId="5"},
            new(){Id="56",ConfigTableId="5",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"name\"}"},
            new(){Id="57",ConfigTableId="5",ColumnName="status",LogicalType="int32",DefaultValue="1"},
            new(){Id="220",ConfigTableId="2",ColumnName="tree_id",Name="树节点",LogicalType=automatic?"int64":"string",LinkConfigTableId="5",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",displayJoiner="/",selfTree=new{table="derived_fresh_tree",idField="id",nameField="name",parentField="parent_derived_fresh_tree_id",rootMode="blank",filterMatchMode="selfAndDescendants"}})}]);
        c.ConfigPages.AddRange([new(){Id="501",ConfigTableId="5",WhereSql="yz_project_id=@current_yz_project_id AND status=1"},new(){Id="502",ConfigTableId="5",WhereSql="yz_project_id=@current_yz_project_id"}]);
        foreach(var column in new[]{"52","55","56","57"})c.ConfigPageColumns.Add(new(){Id="501"+column,ConfigPageId="501",ConfigColumnId=column});c.ConfigPageColumns.Add(new(){Id="50257",ConfigPageId="502",ConfigColumnId="57"});
        c.ConfigPageColumns.Add(new(){Id="201220",ConfigPageId="201",ConfigColumnId="220",Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        if(!automatic){c.ConfigColumns.Add(new(){Id="221",ConfigTableId="2",ColumnName="tree_name",LogicalType="string",ColumnLength="10",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("tree.name")}});c.ConfigPageColumns.Add(new(){Id="201221",ConfigPageId="201",ConfigColumnId="221",IsEdit=0});}
        c.ConfigMenus.Add(new(){Id="1401",YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="正式派生树",Kind="GENERATED_PAGE",ConfigPageId="501"});foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";return c;
    }
}
