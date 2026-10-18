using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class IndexCompilationSafetyTests
{
    private static MetadataCatalog Catalog(int? enabled = 1, string direction = "ASC") => new()
    {
        ConfigTables = [new() { Id = "table", TableName = "mt_index_record" }],
        ConfigColumns = [new() { Id = "id", ConfigTableId = "table", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 }],
        ConfigIndexes = [enabled is { } flag ? JsonSerializer.SerializeToElement(new { id = "index", configTableId = "table", indexName = "yzp_idx_id", isUnique = 0, isEnabled = flag }) : JsonSerializer.SerializeToElement(new { id = "index", configTableId = "table", indexName = "yzp_idx_id", isUnique = 0 })],
        ConfigIndexColumns = [JsonSerializer.SerializeToElement(new { configIndexId = "index", configColumnId = "id", sortNo = 10, sortDirection = direction })]
    };
    [Fact]
    public void DisabledIndexNeverCreatesOrDropsPhysicalIndexes()
    {
        var enabled = MySqlSchemaPlanner.Compile(Catalog(), "db"); var disabled = MySqlSchemaPlanner.Compile(Catalog(0), "db");
        Assert.Empty(disabled.Tables[0].Indexes!); Assert.Empty(MySqlSchemaPlanner.Compare(enabled, disabled));
        var create = Assert.Single(MySqlSchemaPlanner.Compare(new("db", []), disabled)); Assert.DoesNotContain("INDEX", create.Sql);
    }
    [Fact]
    public void DescendingOrUnknownDirectionCannotSilentlyBecomeAscending()
    {
        Assert.Throws<NotSupportedException>(() => MySqlSchemaPlanner.Compile(Catalog(direction: "DESC"), "db"));
        Assert.Throws<NotSupportedException>(() => MySqlSchemaPlanner.Compile(Catalog(direction: "random"), "db"));
        Assert.Empty(MySqlSchemaPlanner.Compile(Catalog(0, "DESC"), "db").Tables[0].Indexes!);
    }
    [Fact]
    public void LegacyMissingEnabledRemainsEnabledAndUnknownFlagIsRejected()
    {
        Assert.Single(MySqlSchemaPlanner.Compile(Catalog(null), "db").Tables[0].Indexes!);
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compile(Catalog(2), "db"));
    }
}
