using System.Text.Json;
using System.Reflection;
using Metarill.Runtime.Agent;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式共享模型中的隐式字段必须与生成实体同源发布，不能依靠显式补列让测试通过。</summary>
public sealed class AgentRuleAliasesTests
{
    /// <summary>原目录没有显式country_id；发布仍须绑定逻辑祖先/名称别名，且不改原元数据。</summary>
    [Fact]
    public void Shared_rule_catalog_expands_implicit_record_columns_without_mutating_metadata()
    {
        var catalog=RuleRecordNativePreparation.Catalog();Assert.DoesNotContain(catalog.ConfigColumns,column=>column.ConfigTableId=="2"&&column.ColumnName=="country_id");var before=JsonSerializer.Serialize(catalog);var host=AgentHostCatalog.ForApplication(catalog);
        foreach(var id in new[]{"201","202","203","204","205","206"})
        {var page=host.FindPage(id);Assert.NotNull(page);var names=(IReadOnlyDictionary<string,string>?)typeof(AgentPublishedPage).GetProperty("RecordNames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page);Assert.NotNull(names);Assert.Equal("country_id",names["countryId"]);Assert.Equal("country_id",names["country_id"]);Assert.Equal("country_name",names["countryName"]);Assert.Equal("warehouse_id",names["warehouseId"]);}
        Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }
}
