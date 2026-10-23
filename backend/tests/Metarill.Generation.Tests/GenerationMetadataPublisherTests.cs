using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>冻结关系快照须与输入记录排列无关，且不能改写源配置或业务数组。</summary>
public sealed class GenerationMetadataPublisherTests
{
    /// <summary>实际混合关系目录同时覆盖表、字段、页面、后台及维护按钮次序。</summary>
    [Fact]
    public void Relation_catalog_is_stable_without_reordering_business_arrays_or_mutating_source()
    {
        var catalog=NonRedundantParentedSelfTreeTests.Catalog("nr_publish_order","int64",false);
        catalog.YzAdminTypes.Add(new(){Id="99",YzProjectId=catalog.YzProjectId});
        catalog.ConfigButtons.AddRange(new[]{("902","/admin/sys/system_maintenance/tables"),("901","/admin/sys/system_maintenance/backfill_join_columns")}.Select(item=>JsonSerializer.SerializeToElement(new{id=item.Item1,sql=item.Item2})));
        catalog.ConfigTables[0].Extra["orderedBusinessArray"]=JsonSerializer.SerializeToElement(new[]{new{id="2"},new{id="1"}});
        var source=JsonSerializer.Serialize(catalog);var first=GenerationMetadataPublisher.ForRelations(catalog);
        Assert.Equal(source,JsonSerializer.Serialize(catalog));
        Assert.Equal(new[]{"2","1"},first.ConfigTables.Single(table=>table.Id==catalog.ConfigTables[0].Id).Extra["orderedBusinessArray"].EnumerateArray().Select(item=>item.GetProperty("id").GetString()));
        catalog.ConfigTables.Reverse();catalog.ConfigColumns.Reverse();catalog.ConfigPages.Reverse();catalog.YzAdminTypes.Reverse();catalog.ConfigButtons.Reverse();
        source=JsonSerializer.Serialize(catalog);var second=GenerationMetadataPublisher.ForRelations(catalog);
        Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(second));Assert.Equal(source,JsonSerializer.Serialize(catalog));
    }

    /// <summary>直接发布保留原参数错误，关系编译转换为定义错误；两个边界都拒绝重复键且不改输入。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Duplicate_nested_keys_preserve_publish_and_relation_compile_contracts(bool extensionObject)
    {
        var catalog=GeneratorTests.Catalog();var column=catalog.ConfigColumns.Single(column=>column.ColumnName=="name");
        const string duplicate="{\"tableId\":\"3\",\"tableId\":\"1\"}";
        if(extensionObject)column.Extra["relationOptions"]=JsonSerializer.Deserialize<JsonElement>(duplicate);else column.ControlDetail=duplicate;
        var source=JsonSerializer.Serialize(catalog);var publishError=Assert.Throws<ArgumentException>(()=>GenerationMetadataPublisher.Publish(catalog));
        Assert.Equal("key",publishError.ParamName);Assert.Contains("tableId",publishError.Message);
        var error=Assert.Throws<InvalidOperationException>(()=>GenerationMetadataPublisher.ForRelations(catalog));
        var original=Assert.IsType<ArgumentException>(error.InnerException);Assert.Equal("key",original.ParamName);Assert.Contains("tableId",original.Message);
        Assert.Contains("重复",error.Message);Assert.Equal(source,JsonSerializer.Serialize(catalog));
    }
}
