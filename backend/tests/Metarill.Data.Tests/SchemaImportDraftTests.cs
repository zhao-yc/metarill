using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class SchemaImportDraftTests
{
    /// <summary>构建只读结构输入，包含标准主键且不依赖数据库或配置ID分配器。</summary>
    private static SchemaImportInspection Source(ColumnSchema column, IReadOnlyList<IndexSchema>? indexes = null, IReadOnlyList<SchemaImportIssue>? issues = null) =>
        new(new("mysql", new("schema_import_test", [new("source_record", [new("id", "bigint", false, true), column], Indexes: indexes)]), "fingerprint"), issues ?? []);

    /// <summary>每种已支持物理类型经过原正向编译器核验，不能只测试字符串类型映射表。</summary>
    [Theory]
    [InlineData("varchar(64)", "string", null)]
    [InlineData("varchar(64)", "string", "'O''Brien'")]
    [InlineData("text", "string", "('多行常量')")]
    [InlineData("longtext", "string", null)]
    [InlineData("int", "int32", "5")]
    [InlineData("bigint", "int64", "9223372036854775807")]
    [InlineData("decimal(18,2)", "decimal", "12.50")]
    [InlineData("tinyint(1)", "bool", "1")]
    [InlineData("date", "date", null)]
    [InlineData("datetime(6)", "datetime", null)]
    [InlineData("time(6)", "time", null)]
    [InlineData("json", "json", null)]
    [InlineData("varbinary(16)", "binary", null)]
    [InlineData("longblob", "binary", null)]
    public void Supported_columns_roundtrip_without_allocating_real_ids(string type, string logical, string? value)
    {
        var input = Source(new("value", type, true, DefaultSql: value)); var before = System.Text.Json.JsonSerializer.Serialize(input);
        var draft = SchemaImportDraftCompiler.Compile(input, "123");
        Assert.True(draft.ColumnsAndIndexesRepresentable, string.Join(";", draft.Issues.Select(issue => issue.Message)));
        var column = Assert.Single(draft.LogicalMetadata.ConfigColumns, column => column.ColumnName == "value");
        Assert.Equal(logical, column.LogicalType); Assert.StartsWith("draft_table_", column.ConfigTableId); Assert.StartsWith("draft_", column.Id);
        Assert.Empty(draft.LogicalMetadata.ConfigPages); Assert.Empty(draft.LogicalMetadata.ConfigMenus); Assert.True(draft.UsesDraftIdentifiers);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(input));
    }

    /// <summary>未知类型、表达式、自动列和不能表示的空默认值均报告缺口，不能静默改变源结构。</summary>
    [Theory]
    [InlineData("float", null, false)]
    [InlineData("char(20)", null, false)]
    [InlineData("bigint unsigned", null, false)]
    [InlineData("datetime", null, false)]
    [InlineData("varchar(64)", "''", false)]
    [InlineData("varchar(64)", "('')", false)]
    [InlineData("datetime(6)", "CURRENT_TIMESTAMP(6)", true)]
    [InlineData("bigint", null, true)]
    [InlineData("decimal(65,2)", null, false)]
    [InlineData("varchar(16001)", null, false)]
    [InlineData("varbinary(0)", null, false)]
    [InlineData("int", "1 + 2", false)]
    public void Lossy_columns_are_not_coerced_to_an_existing_logical_type(string type, string? value, bool generated)
    {
        var draft = SchemaImportDraftCompiler.Compile(Source(new("value", type, true, DefaultSql: value, Generated: generated)), "123");
        Assert.False(draft.ColumnsAndIndexesRepresentable); Assert.Contains(draft.Issues, issue => issue.Code == "COLUMN_UNREPRESENTABLE" && issue.Member == "value");
        Assert.DoesNotContain(draft.LogicalMetadata.ConfigColumns, column => column.ColumnName == "value");
    }

    /// <summary>复合主键不能被现有编译器按字段名推断成单一主键id。</summary>
    [Fact]
    public void Composite_primary_key_is_explicitly_rejected()
    {
        var draft = SchemaImportDraftCompiler.Compile(Source(new("other", "bigint", false, true)), "123");
        Assert.Contains(draft.Issues, issue => issue.Code == "PRIMARY_KEY_UNSUPPORTED");
    }

    /// <summary>普通/唯一复合索引保持名称与顺序，功能、前缀等特征缺口不能生成普通ASC索引。</summary>
    [Fact]
    public void Indexes_roundtrip_and_unmodeled_features_remain_blockers()
    {
        var indexes = new[] { new IndexSchema("yzp_uk_value", true, ["value", "id"]) };
        var supported = SchemaImportDraftCompiler.Compile(Source(new("value", "varchar(64)", true), indexes), "123");
        Assert.True(supported.ColumnsAndIndexesRepresentable); Assert.Single(supported.LogicalMetadata.ConfigIndexes);
        Assert.Equal(new[] { "value", "id" }, supported.LogicalMetadata.ConfigIndexColumns.Select(column => column.GetProperty("columnName").GetString()));
        var blocked = SchemaImportDraftCompiler.Compile(Source(new("value", "varchar(64)", true), indexes, [new("source_record", "yzp_uk_value", "INDEX_FEATURE_UNSUPPORTED", "前缀")]), "123");
        Assert.False(blocked.ColumnsAndIndexesRepresentable); Assert.Empty(blocked.LogicalMetadata.ConfigIndexes);
    }

    /// <summary>连接不可用时，危险名称/重复/空/超预算输入也必须先在本地拒绝。</summary>
    [Fact]
    public async Task Invalid_source_selections_never_open_a_database_connection()
    {
        using var session = new DatabaseSession(new() { Provider = "mysql", ConnectionString = "Server=127.0.0.1;Port=1;Database=schema_import_test;User ID=unused;Password=unused", ExpectedDatabase = "schema_import_test" });
        var service = new SchemaService(session);
        foreach (var names in new[] { Array.Empty<string>(), new[] { "x", "X" }, Enumerable.Range(0, 17).Select(index => "table_" + index).ToArray() })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => service.InspectForImportAsync(names));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InspectForImportAsync(["x;DROP TABLE users"]));
    }
}
