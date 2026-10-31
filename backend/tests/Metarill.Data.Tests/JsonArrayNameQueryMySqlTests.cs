using System.Data.Common;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreeSql;
using FreeSql.DataAnnotations;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Data.Tests;

/// <summary>真实隔离库验证解码后的string成员；不启动服务，也不向产品API传原生SQL。</summary>
public sealed class JsonArrayNameQueryMySqlTests(ITestOutputHelper output)
{
    /// <summary>计数、分页和导出投影共用已受限query，参数/别名不因多个名称谓词互相覆盖。</summary>
    [MySqlIntegrationFact]
    public async Task Decoded_members_literal_patterns_scope_count_page_and_projection_are_real()
    {
        var options = await ReadOptions();
        using var session = new DatabaseSession(options);
        var schema = new SchemaService(session);
        var table = "mt_test_json_names_" + Guid.NewGuid().ToString("N")[..20];
        var catalog = Catalog(table);
        var rows = Seed();
        var created = false;
        IReadOnlyList<NamesRow> written = [];
        DatabaseSchema? installed = null;
        var commands = new List<(string Sql, (string Name, object? Value)[] Parameters)>();
        session.Orm.Aop.CommandBefore += (_, args) =>
        {
            if (args.Command.CommandText.Contains("JSON_SEARCH", StringComparison.Ordinal))
                commands.Add((args.Command.CommandText, args.Command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName.TrimStart('@'), parameter.Value)).ToArray()));
        };
        try
        {
            await using (var connection = session.Provider.CreateConnection(options))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT VERSION()";
                output.WriteLine("已核对Server-200业务隔离库，真实MySQL版本：" + await command.ExecuteScalarAsync());
            }
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var preview = await schema.PreviewAsync(catalog);
            var create = Assert.Single(preview.Changes);
            Assert.Equal("createTable", create.Kind);
            Assert.Equal(table, create.Table);
            var applied = await schema.ApplyAsync(preview);
            Assert.True(applied.Succeeded);
            created = true;
            installed = await schema.InspectAsync(catalog);
            await session.InTransactionAsync(async () => { Assert.Equal(rows.Count, await session.Insert<NamesRow>().AsTable(table).AppendData(rows).ExecuteAffrowsAsync()); return 0; });
            written = rows;

            ISelect<NamesRow> Scoped() => session.Select<NamesRow>().AsTable((_, _) => table).As("fixed_source")
                .Where(row => row.ProjectId == 17 && row.ParentId == 5 && row.DeleteNo == null);
            ISelect<NamesRow> Search(string keyword) => JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, Scoped(), row => row.Names, keyword);
            async Task Match(string keyword, params long[] ids) => Assert.Equal(ids.Order(), (await Search(keyword).ToListAsync(row => row.Id)).Order());

            await Match("中文", 1, 2, 14, 17);
            await Match("张\"三\\路径", 3);
            await Match("%_!", 4);
            await Match("!", 4);
            await Match("%", 4);
            await Match("_", 4);
            await Match("line\nbreak", 6);
            await Match("'quoted'", 6);
            await Match("123", 19);
            await Match("true");
            await Match("null");
            await Match("first\",\"second");
            await Match("' OR 1=1;--");
            // 15/16即便同时包含合法顶层名称，嵌套非空结构仍整行不匹配；17的空容器不含可被递归匹配的值。
            Assert.DoesNotContain(15L, await Search("中文").ToListAsync(row => row.Id));
            Assert.DoesNotContain(16L, await Search("中文").ToListAsync(row => row.Id));
            var shared = Search("中文");
            Assert.Equal(4, await shared.CountAsync());
            Assert.Equal(new long[] { 14, 17 }, (await shared.OrderBy(row => row.Id).Page(2, 2).ToListAsync()).Select(row => row.Id));
            Assert.Equal(new long[] { 1, 2, 14, 17 }, (await Search("中文").OrderBy(row => row.Id).ToListAsync(row => new { row.Id, row.Names })).Select(row => row.Id));
            var dual = JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, Search("中文"), row => row.OtherNames, "第二%!_");
            Assert.Equal(new long[] { 2 }, (await dual.ToListAsync()).Select(row => row.Id));
            Assert.NotEmpty(commands);
            var dualCommand = commands.Last();
            var bound = dualCommand.Parameters.Where(parameter => parameter.Name.StartsWith("mt_json_name_", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, bound.Length);
            Assert.Equal(2, bound.Select(parameter => parameter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(bound, parameter => Equals(parameter.Value, "%中文%"));
            Assert.Contains(bound, parameter => Equals(parameter.Value, "%第二!%!!!_%"));
            Assert.All(commands, command =>
            {
                Assert.Contains("fixed_source.`names`", command.Sql);
                Assert.DoesNotContain("中文", command.Sql);
                Assert.DoesNotContain("第二", command.Sql);
                Assert.DoesNotContain("' OR 1=1;--", command.Sql);
                Assert.Equal(command.Parameters.Length, command.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            });
            output.WriteLine($"真实成员/shape/字面匹配与范围回归完成，受测行{rows.Count}，参数化查询{commands.Count}条。");
        }
        finally
        {
            if (created) await Cleanup(session, schema, catalog, table, installed!, written);
        }
    }

    /// <summary>只读取既有私有配置，测试目标必须解析到Server-200且锁定独立业务测试库。</summary>
    private static async Task<DatabaseOptions> ReadOptions()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var connection = new MySqlConnectionStringBuilder(options.ConnectionString);
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test" || connection.Database != options.ExpectedDatabase ||
            !(await Dns.GetHostAddressesAsync(connection.Server)).Any(address => address.ToString() == "192.168.31.200"))
            throw new InvalidOperationException("JSON名称筛选测试只允许Server-200的metarill_business_test隔离库。");
        return options;
    }

    /// <summary>由元数据同步唯一随机表，不手写安装DDL，不触碰平台基础设施。</summary>
    private static MetadataCatalog Catalog(string table) => new()
    {
        ConfigTables = [new() { Id = "json_names", TableName = table }],
        ConfigColumns = [new() { Id = "id", ConfigTableId = "json_names", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "project", ConfigTableId = "json_names", ColumnName = "yz_project_id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "parent", ConfigTableId = "json_names", ColumnName = "parent_id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "names", ConfigTableId = "json_names", ColumnName = "names", LogicalType = "string", ColumnLength = "max" },
            new() { Id = "other_names", ConfigTableId = "json_names", ColumnName = "other_names", LogicalType = "string", ColumnLength = "max" },
            new() { Id = "delete", ConfigTableId = "json_names", ColumnName = "delete_no", LogicalType = "datetime" }]
    };

    /// <summary>同时覆盖正常原值、序列化转义及异常历史形状，跨范围行故意保留相同匹配值。</summary>
    private static IReadOnlyList<NamesRow> Seed()
    {
        string Json(params string[] names) => JsonSerializer.Serialize(names);
        NamesRow Row(long id, string? names) => new() { Id = id, ProjectId = 17, ParentId = 5, Names = names, OtherNames = Json("普通第二名称") };
        var rows = new List<NamesRow> { Row(1, Json("前缀中文后缀")), Row(2, JsonSerializer.Serialize(new[] { "中文" }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })),
            Row(3, Json("张\"三\\路径")), Row(4, Json("literal%_!end")), Row(5, Json("literalAXend")), Row(6, Json("line\nbreak", "contains 'quoted'")), Row(7, null), Row(8, ""), Row(9, "not json"), Row(10, "{}"),
            Row(11, "\"中文\""), Row(12, "null"), Row(13, "[]"), Row(14, "[123,true,null,\"直接中文\"]"), Row(15, "[{\"name\":\"中文\"},\"直接中文\"]"), Row(16, "[[\"中文\"],\"直接中文\"]"),
            Row(17, "[[],{},\"直接中文\"]"), Row(18, "[123,true,null]"), Row(19, Json("10", "123")), Row(20, Json("first", "second")) };
        rows[1].OtherNames = Json("第二%!_");
        var outsideProject = Row(21, Json("中文")); outsideProject.ProjectId = 18; rows.Add(outsideProject);
        var outsideParent = Row(22, Json("中文")); outsideParent.ParentId = 6; rows.Add(outsideParent);
        var deleted = Row(23, Json("中文")); deleted.DeleteNo = new DateTime(2026, 1, 1); rows.Add(deleted);
        return rows;
    }

    /// <summary>结构、完整行及外部依赖均未漂移才清理当前随机表，未知内容保留现场并拒绝删除。</summary>
    private async Task Cleanup(DatabaseSession session, SchemaService schema, MetadataCatalog catalog, string table, DatabaseSchema installed, IReadOnlyList<NamesRow> written)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^mt_test_json_names_[a-f0-9]{20}$") || session.Options.ExpectedDatabase != "metarill_business_test")
            throw new InvalidOperationException("拒绝清理非本次JSON名称测试表。");
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(installed), MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog)));
        var actual = await session.Select<NamesRow>().AsTable((_, _) => table).OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(JsonSerializer.Serialize(written.OrderBy(row => row.Id)), JsonSerializer.Serialize(actual));
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=@schema AND EVENT_OBJECT_TABLE=@table) + (SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA=@schema AND REFERENCED_TABLE_NAME=@table)";
        var schemaParameter = command.CreateParameter(); schemaParameter.ParameterName = "@schema"; schemaParameter.Value = session.Options.ExpectedDatabase; command.Parameters.Add(schemaParameter);
        var tableParameter = command.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = table; command.Parameters.Add(tableParameter);
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        command.Parameters.Clear();
        command.CommandText = "DROP TABLE " + session.Provider.QuoteIdentifier(table);
        await command.ExecuteNonQueryAsync();
        output.WriteLine("已验证结构、完整行和外部FK/触发器后精确清理本次随机表。");
    }

    private sealed class NamesRow
    {
        [Column(Name = "id", IsPrimary = true)] public long Id { get; set; }
        [Column(Name = "yz_project_id")] public long ProjectId { get; set; }
        [Column(Name = "parent_id")] public long ParentId { get; set; }
        [Column(Name = "names")] public string? Names { get; set; }
        [Column(Name = "other_names")] public string? OtherNames { get; set; }
        [Column(Name = "delete_no")] public DateTime? DeleteNo { get; set; }
    }
}
