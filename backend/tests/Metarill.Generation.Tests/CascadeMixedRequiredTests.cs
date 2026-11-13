using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>跨表、自树和多选在同一原页面，必填与物理非空分别验证。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CascadeMixedRequiredTests
{
    /// <summary>四种来源/父键实际生成编译，包含两种装饰、混合筛选、只读快照和复制。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Mixed_controls_and_required_pages_compile(string sourceType,string parentType)
    {
        var catalog=Catalog("mixed",sourceType,parentType);var saved=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-mixed");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeMixed");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CascadeMixed.Generated.Services.MixedSourceService"));var model=GenerationModel.Create(catalog,"Example.CascadeMixed");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="201");Assert.True(CascadeTreeQueryCompilation.Async(page));Assert.Single(CrossTableDisplayRenderer.ForPage(model,table,page));Assert.Single(CascadeTreeViewRenderer.ForPage(model,table,page));Assert.Equal(saved,JsonSerializer.Serialize(catalog));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>多值空检查不依赖序列化空格或单元格文本形式；非数组不能冒充选择。</summary>
    [Fact]
    public void Required_arrays_distinguish_empty_values_and_invalid_shapes()
    {
        foreach(var value in new string?[]{null,""," ","null","[]"," [  ] "})Assert.True(Metarill.Core.Contracts.RequiredFieldValue.IsEmptyArray(value));Assert.False(Metarill.Core.Contracts.RequiredFieldValue.IsEmptyArray("[\"9007199254740993\"]"));foreach(var value in new[]{"{}","1","\"[]\"","[invalid"})Assert.Throws<ArgumentException>(()=>Metarill.Core.Contracts.RequiredFieldValue.IsEmptyArray(value));
    }
    /// <summary>五张表均由元数据生成；目标树可兼作明确type=2多选页，原接口与前端不变。</summary>
    internal static MetadataCatalog Catalog(string prefix="mixed",string sourceType="string",string parentType="int64")
    {
        var catalog=CrossTableReadTests.Catalog(prefix,parentType,parentType);catalog.ConfigColumns.Single(column=>column.Id=="25").LogicalType=sourceType;var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsCopy=1;page.Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigTables.Add(new(){Id="6",TableName=prefix+"_tree",IsRongyu=1});catalog.ConfigColumns.AddRange([new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"},new(){Id="65",ConfigTableId="6",ColumnName="parent_"+prefix+"_tree_id",LogicalType=parentType,LinkConfigTableId="6"},new(){Id="66",ConfigTableId="6",ColumnName="status",LogicalType="int32"}]);catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id AND status=1",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});catalog.ConfigPageColumns.AddRange([new(){Id="60162",ConfigPageId="601",ConfigColumnId="62"},new(){Id="60165",ConfigPageId="601",ConfigColumnId="65"}]);
        catalog.ConfigColumns.AddRange([new(){Id="81",ConfigTableId="2",ColumnName="tree_id",LogicalType=sourceType,ControlKind="CascadeTree",LinkConfigTableId="6",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",displayJoiner="/",selfTree=new{table=prefix+"_tree",parentField="parent_"+prefix+"_tree_id",nameField="name",filterMatchMode="selfAndDescendants"}})},new(){Id="82",ConfigTableId="2",ColumnName="notes",Name="备注",LogicalType="string",IsDbNotNull=1,DefaultValue="默认备注"},new(){Id="83",ConfigTableId="2",ColumnName="choice_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="6",ControlDetail=JsonSerializer.Serialize(new{pageId="601",nameField="name",buttonText="选择"})},new(){Id="84",ConfigTableId="2",ColumnName="choice_names",LogicalType="string",ColumnLength="max"},new(){Id="85",ConfigTableId="2",ColumnName="country_id",LogicalType=sourceType,LinkConfigTableId="3"}]);catalog.ConfigPageColumns.AddRange([new(){Id="20181",ConfigPageId="201",ConfigColumnId="81",Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}},new(){Id="20182",ConfigPageId="201",ConfigColumnId="82"},new(){Id="20183",ConfigPageId="201",ConfigColumnId="83"},new(){Id="20184",ConfigPageId="201",ConfigColumnId="84"}]);
        foreach(var id in new[]{"202","203","204","205"}){var copy=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!;copy.Id=id;catalog.ConfigPages.Add(copy);foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var next=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;next.Id=id+field.Id;next.ConfigPageId=id;if(id=="202"&&field.ConfigColumnId=="82"||id=="203"&&field.ConfigColumnId=="83"||id=="204"&&field.ConfigColumnId is "25" or "81")next.IsRequired=1;catalog.ConfigPageColumns.Add(next);}}
        catalog.ConfigPageColumns.Add(new(){Id="20585",ConfigPageId="205",ConfigColumnId="85",IsRequired=1,Extra=new(){["isReadOnly"]=JsonSerializer.SerializeToElement(1)}});return catalog;
    }
}
