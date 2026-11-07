using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>同表非父关联的正式生成编译，实际数据库语义另走MySQL验收。</summary>
public sealed class CascadeTreeSameRowTests
{
    /// <summary>四种ID/父键组合实际编译创建、更新、复制、导入和不同来源页的事务后验入口。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","string",true)]
    [InlineData("string","int64",false)][InlineData("string","int64",true)]
    [InlineData("int64","string",false)][InlineData("int64","string",true)]
    [InlineData("int64","int64",false)][InlineData("int64","int64",true)]
    public async Task Final_storage_validation_compiles_for_every_write_entry(string sourceType,string parentType,bool nonRedundant)
    {
        var catalog=Catalog("same",sourceType,parentType);if(nonRedundant)catalog.ConfigTables.Single().IsRongyu=0;var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.SameRow");var table=Assert.Single(model.Tables);var page=table.Pages.Single(page=>page.Source.Id=="201");var plan=Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));Assert.True(CascadeTreeMergedWriteRenderer.Applies(table,plan));
        Assert.Contains("submitted.Contains(\"amount\")",CascadeTreeMergedWriteRenderer.Trigger(table,plan));Assert.Contains("submitted.Contains(\"parent_id\")",CascadeTreeMergedWriteRenderer.Trigger(table,plan));Assert.Contains("ValidateStoredCascade201_6Async",CascadeTreeMergedWriteRenderer.After(model,table,page,false,true));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-same-row");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SameRow");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.SameRow.Generated.Controllers.SameNodePage203Controller",true));
            var hand=Path.Combine(directory,"generated/Services/SameNodeService.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SameRow");Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>来源201/203不含父字段，202为唯一树读取页；作用域使用真实数据库字符比较及金额舍入。</summary>
    internal static MetadataCatalog Catalog(string prefix="same",string sourceType="string",string parentType="int64")
    {
        const string project="9007199254740993";var catalog=new MetadataCatalog{YzProjectId=project,ConfigTables=[new(){Id="1",TableName=prefix+"_node"}]};
        catalog.ConfigColumns.AddRange([
            new(){Id="1",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="2",ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="3",ConfigTableId="1",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="4",ConfigTableId="1",ColumnName="name",Name="节点名",LogicalType="string",ColumnLength="100",SortNo=1},new(){Id="5",ConfigTableId="1",ColumnName="parent_id",Name="父节点",LogicalType=parentType,ColumnLength="64",LinkConfigTableId="1",SortNo=5},
            new(){Id="6",ConfigTableId="1",ColumnName="bookmark_id",Name="书签",LogicalType=sourceType,ColumnLength="64",LinkConfigTableId="1",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table=prefix+"_node",parentField="parent_id",nameField="name"},displayJoiner="/"}),SortNo=2},
            new(){Id="7",ConfigTableId="1",ColumnName="status",LogicalType="int32",DefaultValue="1",SortNo=3},new(){Id="8",ConfigTableId="1",ColumnName="amount",LogicalType="decimal",Precision=8,Scale=2,DefaultValue="1",SortNo=4},new(){Id="9",ConfigTableId="1",ColumnName="code",LogicalType="string",ColumnLength="20",DefaultValue="é",SortNo=6}]);
        foreach(var pageId in new[]{"201","202","203"})
        {
            var page=new PageMetadata{Id=pageId,ConfigTableId="1",YzAdminTypeId="1",IsCopy=1,IsImport=pageId=="202"?0:1,WhereSql="yz_project_id=@current_yz_project_id"+(pageId=="202"?" AND status=1 AND code='é' AND amount<>0.01 AND name LIKE '可%'":"")};if(pageId=="203")page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(page);
            foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "1" or "4" or "6" or "7" or "8" or "9"||pageId=="202"&&column.Id=="5"))catalog.ConfigPageColumns.Add(new(){Id=pageId+column.Id,ConfigPageId=pageId,ConfigColumnId=column.Id,SortNo=column.SortNo});
        }
        return catalog;
    }
}
