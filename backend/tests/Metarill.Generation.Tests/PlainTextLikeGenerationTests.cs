using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>普通文本按原LIKE模式生成，精确匹配与已确认的名称控件分支保持。</summary>
public sealed class PlainTextLikeGenerationTests
{
    /// <summary>两种存储共用普通筛选合同；元数据searchType=equal不能误走模式分支。</summary>
    [Theory][InlineData(0,false)][InlineData(1,false)][InlineData(0,true)][InlineData(1,true)]
    public void Physical_and_sparse_text_filters_use_pattern_only_when_declared(int redundant,bool exact)
    {
        var catalog=redundant==0?NonRedundantGeneratedFixture.Catalog("mt_plain_pattern"):GeneratorTests.Catalog();catalog.ConfigTables.Single(t=>t.Id=="1").IsRongyu=redundant;
        var name=catalog.ConfigColumns.Single(c=>c.ConfigTableId=="1"&&c.ColumnName=="name");var page=catalog.ConfigPages.Single(p=>p.ConfigTableId=="1");
        if(exact)catalog.ConfigPageColumns.Single(f=>f.ConfigPageId==page.Id&&f.ConfigColumnId==name.Id).Extra["searchType"]=JsonSerializer.SerializeToElement("equal");
        var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.TextPattern");var table=model.Tables.Single(t=>t.Source.Id=="1");var source=BackendRenderer.Render(model).Single(f=>f.Path=="Services/"+table.Name+"Service.g.cs").Content;
        Assert.DoesNotContain("entity.Name.Contains(value)",source);
        var marker=redundant==1?"StringNameQuery.ApplyPattern(session,query,entity => entity.Name,value!)":"new(\"name\",FixedReadComparison.LikePattern,";
        Assert.Equal(!exact,source.Contains(marker,StringComparison.Ordinal));
        Assert.Contains("string.IsNullOrWhiteSpace(filter.Value.GetString())",source);Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }
}
