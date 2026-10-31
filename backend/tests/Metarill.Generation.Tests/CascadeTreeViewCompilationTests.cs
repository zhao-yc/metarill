using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实编译所有Query调用入口，不用静态文本断言冒充生成项目可运行。</summary>
public sealed class CascadeTreeViewCompilationTests
{
    /// <summary>列表/数量/统计/导出/选择共用异步筛选；普通页不改调用字节。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("int64","int64")]
    public async Task Generated_async_queries_and_batch_display_compile_for_every_consumer(string sourceType,string parentType)
    {
        var catalog=Catalog("view",sourceType,parentType);var before=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-view");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeView");var source=File.ReadAllText(Path.Combine(directory,"generated/Services/ViewSourceService.g.cs"));Assert.Contains("async Task<ISelect<global::Example.CascadeView.Generated.Entities.ViewSource>> Query201",source);Assert.Contains("FilterIdsAsync",source);Assert.Contains("DecorateCascade201Async",source);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CascadeView.Generated.VOs.ViewSourcePage201Vo",true)!.GetProperty("TreeIdDisplayText"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>固定筛选/展示声明仅来自元数据，名称回显和前端组件仍沿原协议。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string prefix="view",string sourceType="string",string parentType="int64")
    {
        var catalog=CascadeTreeWriteTests.Catalog(prefix,sourceType,parentType);catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsExport=1;page.Extra["isSum"]=JsonSerializer.SerializeToElement(1);page.Extra["type"]=JsonSerializer.SerializeToElement(2);
        catalog.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="amount",LogicalType="int64"});catalog.ConfigPageColumns.Add(new(){Id="2007",ConfigPageId="201",ConfigColumnId="27",Extra=new(){["sumKind"]=JsonSerializer.SerializeToElement(1)}});return catalog;
    }
}
