using System.Data.Common;
using FreeSql.DataAnnotations;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>离线真实FreeSql解析验证固定映射、别名和参数边界，不冒称SQL已经执行。</summary>
public sealed class JsonArrayNameQueryTests
{
    /// <summary>别名来自ORM且字面值绝不能拼入SQL；同一查询的两个成员条件可独立绑定。</summary>
    [Fact]
    public void Mapped_expression_keeps_orm_alias_and_values_out_of_sql()
    {
        using var session = Session();
        var query = session.Select<Row>().As("source_names").Where(row => row.ProjectId == 17);
        query = JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => row.Names, "中文'\\%!_;");
        query = JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => row.OtherNames, "第二关键词");
        var sql = query.ToSql();
        Assert.Contains("source_names.`stored_names`", sql);
        Assert.Contains("source_names.`other_names`", sql);
        Assert.Contains("JSON_SEARCH", sql);
        Assert.Contains("JSON_DEPTH", sql);
        Assert.Contains("CONCAT(LEFT(source_names.`stored_names`, 0), @mt_json_name_", sql);
        Assert.Contains("'!', '$[*]'", sql);
        Assert.DoesNotContain("中文", sql);
        Assert.DoesNotContain("第二关键词", sql);
        Assert.DoesNotContain("JSON_TABLE", sql);
        Assert.Contains("ProjectId", sql);
    }

    /// <summary>标量名称使用参数化LIKE与原列比较，不改用会改变重音语义的Contains。</summary>
    [Fact]
    public void Scalar_name_keeps_fixed_column_like_and_literal_pattern_without_json_or_inline_values()
    {
        using var session = Session();
        var query = StringNameQuery.ApplyLike(session, session.Select<Row>().As("fixed_scalar"), row => row.Names, "Café'_%!");
        var sql = query.ToSql();
        Assert.Contains("fixed_scalar.`stored_names` LIKE @mt_scalar_name_", sql); Assert.Contains("ESCAPE '!'", sql);
        Assert.DoesNotContain("JSON_SEARCH", sql); Assert.DoesNotContain("Café", sql); Assert.DoesNotContain("INSTR", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>方法、常量、忽略列及空关键词都在附加条件前明确拒绝。</summary>
    [Fact]
    public void Only_mapped_direct_string_property_and_nonblank_keyword_are_accepted()
    {
        using var session = Session();
        var query = session.Select<Row>();
        var before = query.ToSql();
        Assert.Throws<ArgumentException>(() => JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => row.Names!.Trim(), "x"));
        Assert.Throws<ArgumentException>(() => JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => "stored_names", "x"));
        Assert.Throws<ArgumentException>(() => JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => row.Ignored, "x"));
        Assert.Throws<ArgumentException>(() => JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => row.Names, " "));
        Assert.Throws<ArgumentException>(() => StringNameQuery.ApplyLike(session, query, row => row.Names!.Trim(), "x"));
        Assert.Throws<ArgumentException>(() => StringNameQuery.ApplyLike(session, query, row => row.Ignored, "x"));
        Assert.Throws<ArgumentException>(() => StringNameQuery.ApplyLike(session, query, row => row.Names, " "));
        Assert.Equal(before, query.ToSql());
    }

    /// <summary>即使注入可解析的ORM，未适配provider也不能借用MySQL方言。</summary>
    [Fact]
    public void Unsupported_provider_is_rejected_without_mutating_query()
    {
        using var owner = Session();
        using var session = new DatabaseSession(owner.Options, owner.Orm, new UnsupportedProvider());
        var query = session.Select<Row>();
        var before = query.ToSql();
        Assert.Throws<NotSupportedException>(() => JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, row => row.Names, "名称"));
        Assert.Throws<NotSupportedException>(() => StringNameQuery.ApplyLike(session, query, row => row.Names, "名称"));
        Assert.Equal(before, query.ToSql());
    }

    /// <summary>构建ORM不打开连接，真实数据库执行另由隔离库测试覆盖。</summary>
    private static DatabaseSession Session() => new(new() { Provider = "mysql", ExpectedDatabase = "metarill_business_test", ConnectionString = "Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;" });

    /// <summary>只验证既有provider注入合同的失败边界，不实现任何其他数据库能力。</summary>
    private sealed class UnsupportedProvider : IDatabaseProvider
    {
        public string Name => "postgresql";
        public DatabaseCapabilities Capabilities => new(Name, false, true);
        public void Validate(DatabaseOptions options) { }
        public string QuoteIdentifier(string identifier) => throw new NotSupportedException();
        public string ParameterPlaceholder(string name) => throw new NotSupportedException();
        public IFreeSql CreateOrm(DatabaseOptions options) => throw new NotSupportedException();
        public DbConnection CreateConnection(DatabaseOptions options) => throw new InvalidOperationException("纯拒绝测试不得打开连接。");
    }

    private sealed class Row
    {
        [Column(IsPrimary = true)] public long Id { get; set; }
        public long ProjectId { get; set; }
        [Column(Name = "stored_names")] public string? Names { get; set; }
        [Column(Name = "other_names")] public string? OtherNames { get; set; }
        [Column(IsIgnore = true)] public string? Ignored { get; set; }
    }
}
