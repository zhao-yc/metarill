using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class PlatformMetadataFactAttribute : FactAttribute
{
    public PlatformMetadataFactAttribute()
    {
        var settings = Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS");
        var metadata = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA");
        if (string.IsNullOrWhiteSpace(settings) || !File.Exists(settings) || string.IsNullOrWhiteSpace(metadata) || !File.Exists(metadata))
            Skip = "未同时提供专用 MySQL 配置与平台元数据路径，平台反查预览未验收。";
    }
}

public sealed class IndexTests
{
    [Fact]
    public void Composite_index_retains_order_and_unique_constraint()
    {
        var current = new DatabaseSchema("db", [new("record", [new("id", "bigint", false, true), new("a", "bigint", false), new("b", "bigint", false)])]);
        var desired = new DatabaseSchema("db", [new("record", current.Tables[0].Columns, null, [new("ix_pair", true, ["b", "a"])])]);
        var change = Assert.Single(MySqlSchemaPlanner.Compare(current, desired));
        Assert.Equal("ALTER TABLE `record` ADD UNIQUE INDEX `ix_pair` (`b`, `a`)", change.Sql);
        Assert.Empty(MySqlSchemaPlanner.Compare(desired, desired));
        var reordered = new DatabaseSchema("db", [new("record", current.Tables[0].Columns, null, [new("ix_pair", true, ["a", "b"])])]);
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compare(reordered, desired));
    }

    [Fact]
    public void Index_cannot_reference_another_table_or_duplicate_column()
    {
        var catalog = new MetadataCatalog
        {
            ConfigTables = [new() { Id = "table", TableName = "record" }],
            ConfigColumns = [new() { Id = "id", ConfigTableId = "table", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 }],
            ConfigIndexes = [JsonSerializer.SerializeToElement(new { id = "index", configTableId = "table", indexName = "ix_id", isUnique = 1 })],
            ConfigIndexColumns = [JsonSerializer.SerializeToElement(new { configIndexId = "index", configColumnId = "missing" })]
        };
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compile(catalog, "db"));
        var reference = JsonSerializer.SerializeToElement(new { configIndexId = "index", configColumnId = "id" });
        catalog.ConfigIndexColumns = [reference,reference];
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compile(catalog, "db"));
    }

    [PlatformMetadataFact]
    public async Task Platform_metadata_compiles_and_only_previews()
    {
        var path = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA");
        Assert.False(string.IsNullOrEmpty(path));
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path!), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("metarill_test", options.ExpectedDatabase);
        using var session = new DatabaseSession(options);
        var plan = await new SchemaService(session).PreviewAsync(catalog);
        Assert.Equal(options.ExpectedDatabase, plan.Database);
        Assert.NotEmpty(catalog.ConfigTables);
        Assert.All(plan.Changes, change => Assert.DoesNotContain("DROP ", change.Sql, StringComparison.OrdinalIgnoreCase));
    }
}

