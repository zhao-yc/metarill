using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原最深级优先和物理字符串语义，以及真实生成的同步查询入口。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableFilterTests
{
    /// <summary>深级优先，未采用祖先和名称不参与SQL；固定键数组在构造时复制。</summary>
    [Fact]
    public void Deepest_ID_and_ignored_ancestor_labels_follow_original_contract()
    {
        var levels=Levels();var filter=new CrossTableFilter(levels);levels[0]=new("changedId","changedName");var selected=filter.Parse(JsonSerializer.SerializeToElement(new{countryId=new{untrusted="忽略"},areaId="99",placeId="9007199254740993",placeName=new{sql="只回显"}}));Assert.Equal(new CrossTableFilterSelection(2,"9007199254740993"),selected);Assert.Equal(new CrossTableFilterSelection(1,"2"),filter.Parse(JsonSerializer.SerializeToElement(new{countryId="1",areaId="2",placeId=""})));Assert.Equal(new CrossTableFilterSelection(0,"1"),filter.Parse(JsonSerializer.SerializeToElement(new{countryId="1"})));
    }
    /// <summary>字符串前导零不被改写；JSON整数与int64上限精确，浮点/越界/注入均拒绝。</summary>
    [Fact]
    public void Exact_ID_text_is_preserved_and_invalid_selected_values_do_not_fall_back()
    {
        var filter=new CrossTableFilter(Levels());Assert.Equal("0001",filter.Parse(JsonSerializer.Deserialize<JsonElement>("{\"countryId\":\"0001\"}"))!.Id);Assert.Equal("9007199254740993",filter.Parse(JsonSerializer.Deserialize<JsonElement>("{\"placeId\":9007199254740993}"))!.Id);Assert.Equal("9223372036854775807",filter.Parse(JsonSerializer.Deserialize<JsonElement>("{\"placeId\":9223372036854775807}"))!.Id);
        foreach(var raw in new[]{"1.0","1e0","0","-1","9223372036854775808","true","{}","[]","\"1 OR 1=1\""})Assert.Throws<ArgumentException>(()=>filter.Parse(JsonSerializer.Deserialize<JsonElement>("{\"countryId\":\"1\",\"placeId\":"+raw+"}")));
    }
    /// <summary>原空节点不加谓词；非空未知/重复键不能伪装成动态SQL或项目范围。</summary>
    [Fact]
    public void Empty_nodes_and_fixed_member_validation_keep_original_boundary()
    {
        var filter=new CrossTableFilter(Levels());Assert.Null(filter.Parse(default));foreach(var raw in new[]{"null","\" \"","{}","{\"countryName\":\"只回显\",\"placeId\":null}"})Assert.Null(filter.Parse(JsonSerializer.Deserialize<JsonElement>(raw)));
        foreach(var raw in new[]{"[]","\"1\"","1","{\"countryId\":1,\"countryId\":2}","{\"table\":\"x\"}","{\"countryId\":1,\"projectId\":2}"})Assert.Throws<ArgumentException>(()=>filter.Parse(JsonSerializer.Deserialize<JsonElement>(raw)));
        Assert.Throws<ArgumentException>(()=>new CrossTableFilter([new("id","name")]));Assert.Throws<ArgumentException>(()=>new CrossTableFilter([new("id","name"),new("name","other")]));
    }
    /// <summary>原筛选键使用本页控件，物理辅助列不成为独立外层筛选键。</summary>
    [Fact]
    public void Prepared_query_and_capability_use_only_fixed_cross_filter_keys()
    {
        var catalog=Catalog();var saved=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossFilter");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.True(Assert.Single(page.Searchable).IsFixedCrossTableFilter);Assert.False(CascadeTreeQueryCompilation.Async(page));Assert.False(CascadeTreeQueryCompilation.Snapshot(model,table,page));Assert.Null(ControlRenderer.RenderFilter(model,page,page.Searchable[0]).UnsupportedReason);Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model),gap=>gap.Usage=="filter"&&gap.Column=="place_id");Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Reason.Contains("完整应用"));Assert.Equal(saved,JsonSerializer.Serialize(catalog));
    }
    /// <summary>四种来源/父键真实编译分页/数量/统计/已选及标量导出，不使用异步空等待代替同步eq。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Generated_synchronous_cross_queries_compile_for_all_consumers(string sourceType,string parentType)
    {
        var catalog=Catalog("crossfilter",sourceType,parentType);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-filter-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossFilter");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CrossFilter.Generated.Services.CrossfilterSourceService"));var text=File.ReadAllText(Path.Combine(directory,"generated/Services/CrossfilterSourceService.g.cs"));Assert.Contains("private ISelect<CrossfilterSource> Query201",text);Assert.DoesNotContain("async Task<ISelect<CrossfilterSource>>",text);Assert.Contains("CrossFilter201_25.Parse",text);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>普通来源及三个目标由元数据生成；导出明确是标量列表列，跨表路径单元格仍待完成。</summary>
    internal static MetadataCatalog Catalog(string prefix="crossfilter",string sourceType="string",string parentType="int64")
    {
        var catalog=CrossTableReadTests.Catalog(prefix,parentType,parentType);catalog.ConfigColumns.Single(column=>column.Id=="25").LogicalType=sourceType;catalog.ConfigColumns.Single(column=>column.Id=="22").Name="名称";var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25");field.IsShow=0;field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.WhereSql+=" AND status=1";page.IsExport=1;page.Extra["isSum"]=JsonSerializer.SerializeToElement(1);page.Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="amount",Name="金额",LogicalType="int64"});catalog.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="status",LogicalType="int32"});catalog.ConfigPageColumns.Add(new(){Id="20126",ConfigPageId="201",ConfigColumnId="26",Extra=new(){["sumKind"]=JsonSerializer.SerializeToElement(1)}});return catalog;
    }
    /// <summary>仅允许原层级ID与名称，不接收表、列、匹配模式或身份。</summary>
    private static CrossTableFilterLevel[] Levels()=>[new("countryId","countryName"),new("areaId","areaName"),new("placeId","placeName")];
}
