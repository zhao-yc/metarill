using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class SchemaPlannerTests
{
    private static ColumnMetadata Column(string name = "id", string type = "int64", int required = 1) => new()
    { Id = name, ConfigTableId = "table1", ColumnName = name, LogicalType = type, IsDbNotNull = required };
    private static MetadataCatalog Catalog(params ColumnMetadata[] extra) => new()
    {
        ConfigTables = [new() { Id = "table1", TableName = "test_record" }],
        ConfigColumns = [Column(), .. extra]
    };

    [Theory]
    [InlineData("string", "varchar(255)")]
    [InlineData("int32", "int")]
    [InlineData("int64", "bigint")]
    [InlineData("decimal", "decimal(18,2)")]
    [InlineData("bool", "tinyint(1)")]
    [InlineData("date", "date")]
    [InlineData("datetime", "datetime(6)")]
    [InlineData("time", "time(6)")]
    [InlineData("json", "json")]
    [InlineData("binary", "longblob")]
    public void Logical_types_have_explicit_physical_mapping(string logical, string sql)
    {
        Assert.Equal(sql, MySqlSchemaPlanner.CompileColumn(Column("value", logical, 0)).StoreType);
    }

    [Theory]
    [InlineData("value; DROP TABLE users")]
    [InlineData("a`b")]
    [InlineData("other.table")]
    [InlineData("")]
    public void Unsafe_identifiers_are_rejected(string name) => Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.CompileColumn(Column(name)));

    [Fact]
    public void Unknown_type_and_invalid_precision_never_fall_back()
    {
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.CompileColumn(Column("value", "geography")));
        var column = Column("value", "decimal"); column.Precision = 2; column.Scale = 3;
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.CompileColumn(column));
        column = Column("value", "string"); column.ColumnLength = "1);DROP TABLE x;--";
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.CompileColumn(column));
    }

    [Fact]
    public void Schema_does_not_silently_add_columns_and_requires_explicit_bigint_id()
    {
        var expected = MySqlSchemaPlanner.Compile(Catalog(), "test_db");
        Assert.Single(expected.Tables.Single().Columns);
        Assert.True(expected.Tables.Single().Columns.Single().PrimaryKey);
        var metadata = Catalog(); metadata.ConfigColumns[0].IsDbNotNull = 0;
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compile(metadata, "test_db"));
    }

    [Fact]
    public void Delete_number_must_use_nullable_datetime()
    {
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compile(Catalog(Column("delete_no", "datetime", 1)), "test_db"));
        Assert.Equal("datetime(6)", MySqlSchemaPlanner.Compile(Catalog(Column("delete_no", "datetime", 0)), "test_db").Tables.Single().Columns.Single(c => c.Name == "delete_no").StoreType);
    }

    [Fact]
    public void Create_table_preview_is_explicit_and_does_not_include_unmanaged_tables()
    {
        var desired = MySqlSchemaPlanner.Compile(Catalog(Column("name", "string", 0)), "test_db");
        var actual = new DatabaseSchema("test_db", [new("unmanaged", [new("legacy", "int", true)])]);
        var change = Assert.Single(MySqlSchemaPlanner.Compare(actual, desired));
        Assert.Equal("createTable", change.Kind);
        Assert.Contains("`test_record`", change.Sql);
        Assert.DoesNotContain("unmanaged", change.Sql);
        Assert.DoesNotContain("DROP", change.Sql);
    }

    [Fact]
    public void Widening_is_allowed_but_shrinking_and_nullable_to_required_are_rejected()
    {
        var desired = new DatabaseSchema("test_db", [new("test_record", [new("value", "varchar(50)", true)])]);
        var actual = new DatabaseSchema("test_db", [new("test_record", [new("value", "varchar(20)", true)])]);
        Assert.Equal("widenColumn", Assert.Single(MySqlSchemaPlanner.Compare(actual, desired)).Kind);
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compare(desired, actual));
        var required = new DatabaseSchema("test_db", [new("test_record", [new("value", "varchar(50)", false)])]);
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compare(desired, required));
    }

    [Fact]
    public void Required_new_column_without_backfill_is_rejected_and_extra_columns_are_preserved()
    {
        var actual = new DatabaseSchema("test_db", [new("test_record", [new("legacy", "int", true)])]);
        var desired = new DatabaseSchema("test_db", [new("test_record", [new("value", "int", false)])]);
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.Compare(actual, desired));
        Assert.Empty(MySqlSchemaPlanner.Compare(actual, new("test_db", [new("test_record", [])])));
    }

    [Fact]
    public void Fingerprint_is_order_independent_but_catches_columns_and_indexes()
    {
        var a = new ColumnSchema("a", "int", true); var b = new ColumnSchema("b", "int", true);
        var before = new DatabaseSchema("test_db", [new("test_record", [a,b], "index_a")]);
        var reordered = new DatabaseSchema("test_db", [new("test_record", [b,a], "index_a")]);
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(before), MySqlSchemaPlanner.Fingerprint(reordered));
        Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(before), MySqlSchemaPlanner.Fingerprint(new("test_db", [new("test_record", [a,b], "index_b")])));
        Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(before), MySqlSchemaPlanner.Fingerprint(new("test_db", [new("test_record", [a], "index_a")])));
    }

    /// <summary>保留65位精度，DECIMAL补零只改变显示，不产生伪DDL或指纹差异。</summary>
    [Theory]
    [InlineData("1","1.00")]
    [InlineData("-1.20","-001.2000")]
    [InlineData("0","-0.000")]
    [InlineData("+000123.4500","123.45")]
    [InlineData("123456789012345678901234567890123.010000","123456789012345678901234567890123.01")]
    public void Decimal_default_formatting_is_equivalent_without_precision_loss(string first,string second)
    {
        DatabaseSchema Schema(string value)=>new("test_db",[new("sample",[new("amount","decimal(65,30)",true,DefaultSql:value)])]);
        Assert.Empty(MySqlSchemaPlanner.Compare(Schema(first),Schema(second)));Assert.Equal(MySqlSchemaPlanner.Fingerprint(Schema(first)),MySqlSchemaPlanner.Fingerprint(Schema(second)));
    }
    /// <summary>真实金额变化、舍入差异、文本格式与缺省状态仍须保留差异。</summary>
    [Theory]
    [InlineData("decimal(8,2)","1","2")]
    [InlineData("decimal(8,2)","0.014","0.01")]
    [InlineData("varchar(50)","'1'","'1.00'")]
    [InlineData("decimal(8,2)",null,"0")]
    public void Canonical_defaults_keep_real_changes_visible(string type,string? first,string second)
    {
        DatabaseSchema Schema(string? value)=>new("test_db",[new("sample",[new("amount",type,true,DefaultSql:value)])]);
        Assert.Single(MySqlSchemaPlanner.Compare(Schema(first),Schema(second)));Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(Schema(first)),MySqlSchemaPlanner.Fingerprint(Schema(second)));
    }

    [Fact]
    public void Defaults_are_literals_and_never_raw_sql()
    {
        var column = Column("name", "string", 0); column.DefaultValue = "x'); DROP TABLE users;--";
        Assert.Equal("'x''); DROP TABLE users;--'", MySqlSchemaPlanner.CompileColumn(column).DefaultSql);
        column = Column("number", "int32", 0); column.DefaultValue = "1);DROP TABLE users";
        Assert.Throws<InvalidOperationException>(() => MySqlSchemaPlanner.CompileColumn(column));
    }

    [Fact]
    public void Options_reject_wrong_or_system_database_before_connecting()
    {
        var provider = new MySqlDatabaseProvider();
        Assert.Throws<InvalidOperationException>(() => provider.Validate(new() { ConnectionString = "Server=localhost;Database=one", ExpectedDatabase = "two" }));
        Assert.Throws<InvalidOperationException>(() => provider.Validate(new() { ConnectionString = "Server=localhost;Database=mysql", ExpectedDatabase = "mysql" }));
        Assert.Throws<InvalidOperationException>(() => provider.Validate(new() { ConnectionString = "Server=localhost;Database=one" }));
        Assert.Throws<NotSupportedException>(() => DatabaseProviders.Resolve("postgresql"));
    }
}
