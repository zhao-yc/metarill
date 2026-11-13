using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>来源表参与跨表链时，各层位均由元数据生成，不能按独立目标链绕过最终存储核验。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableSourceGraphTests
{
    /// <summary>来源在根、中、末级，字符串/整数来源及物理父键均实际生成编译。</summary>
    [Theory]
    [InlineData(0,"string","string")][InlineData(0,"string","int64")][InlineData(0,"int64","string")][InlineData(0,"int64","int64")]
    [InlineData(1,"string","string")][InlineData(1,"string","int64")][InlineData(1,"int64","string")][InlineData(1,"int64","int64")]
    [InlineData(2,"string","string")][InlineData(2,"string","int64")][InlineData(2,"int64","string")][InlineData(2,"int64","int64")]
    public async Task Source_in_each_level_generates_a_coordinated_write(int sourceLevel,string sourceType,string parentType)
    {
        var catalog=Catalog("sourcegraph",sourceLevel,sourceType,parentType);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.SourceGraph");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));Assert.Equal("2",plan.Levels[sourceLevel].Read.Target.Source.Id);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-source-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SourceGraph");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.SourceGraph.Generated.Services.SourcegraphSourceService"));Assert.Equal(before,JsonSerializer.Serialize(catalog));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>独立真实父键的R1重复角色由新证明接通，保留固定缓存及最终路径复核。</summary>
    [Fact]
    public void Repeated_source_roles_require_the_physical_role_proof()
    {
        var catalog=Catalog();var source=catalog.ConfigColumns.Single(column=>column.Id=="25");var detail=JsonNode.Parse(source.ControlDetail)!;detail["levels"]![2]!["table"]="sourcegraph_source";source.ControlDetail=detail.ToJsonString();source.LinkConfigTableId="2";catalog.ConfigColumns.Add(new(){Id="89",ConfigTableId="2",ColumnName="sourcegraph_area_id",LogicalType="int64",LinkConfigTableId="4"});catalog.ConfigPageColumns.Add(new(){Id="20189",ConfigPageId="201",ConfigColumnId="89"});var model=GenerationModel.Create(catalog,"Example.RepeatedSource");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var gap=CrossTableWriteCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="25"));Assert.NotNull(gap.Definition);Assert.Equal(2,gap.Definition.Levels.Count(level=>level.Read.Target.Source.Id=="2"));
    }
    /// <summary>替换恰好一个固定目标为来源；保留原级标识、前缀、表单与物理父字段。</summary>
    internal static MetadataCatalog Catalog(string prefix="sourcegraph",int sourceLevel=0,string sourceType="string",string parentType="int64")
    {
        var catalog=CrossTableReadTests.Catalog(prefix,parentType,parentType);var source=catalog.ConfigColumns.Single(column=>column.Id=="25");source.LogicalType=sourceType;var detail=JsonNode.Parse(source.ControlDetail)!.AsObject();var levels=detail["levels"]!.AsArray();var removed=(sourceLevel+3).ToString();levels[sourceLevel]!["table"]=prefix+"_source";source.ControlDetail=detail.ToJsonString();if(sourceLevel==2)source.LinkConfigTableId="2";
        catalog.ConfigTables.RemoveAll(table=>table.Id==removed);catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==removed);var removedPages=catalog.ConfigPages.Where(page=>page.ConfigTableId==removed).Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);catalog.ConfigPages.RemoveAll(page=>removedPages.Contains(page.Id));catalog.ConfigPageColumns.RemoveAll(field=>removedPages.Contains(field.ConfigPageId));
        foreach(var column in catalog.ConfigColumns.Where(column=>column.LinkConfigTableId==removed))column.LinkConfigTableId="2";
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsCopy=1;page.Extra["type"]=JsonSerializer.SerializeToElement(2);page.WhereSql="yz_project_id=@current_yz_project_id AND status=1 AND code='e' AND amount>=0.02";
        catalog.ConfigColumns.AddRange([new(){Id="81",ConfigTableId="2",ColumnName="status",LogicalType="int32",DefaultValue="1"},new(){Id="82",ConfigTableId="2",ColumnName="code",LogicalType="string",DefaultValue="e"},new(){Id="83",ConfigTableId="2",ColumnName="amount",LogicalType="decimal",Precision=18,Scale=2,DefaultValue="1"}]);foreach(var id in new[]{"81","82","83"})catalog.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id});
        if(sourceLevel>0){var previous=new[]{"country","area"}[sourceLevel-1];catalog.ConfigColumns.Add(new(){Id="88",ConfigTableId="2",ColumnName=prefix+"_"+previous+"_id",LogicalType=parentType,LinkConfigTableId=(sourceLevel+2).ToString()});catalog.ConfigPageColumns.Add(new(){Id="20188",ConfigPageId="201",ConfigColumnId="88"});}
        return catalog;
    }
}
