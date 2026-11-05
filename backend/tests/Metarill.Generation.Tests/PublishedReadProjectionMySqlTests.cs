using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(GenerationMySqlCollection.Name)]
public sealed class PublishedReadProjectionMySqlTests
{
    private const long FirstId = 9007199254740993L;
    private const string Table = "mt_published_projection";
    private const string Route = "/api/admin/auto/" + Table + "/101";

    /// <summary>通过真实生成控制器与 MySQL 验证原响应、窄投影及项目范围。</summary>
    [FreshInstallMySqlFact]
    public async Task Generated_http_reads_use_the_published_projection_in_mysql()
    {
        var options = FreshInstallConfiguration.Read("PackagedDatabase", "package");
        using var session = new DatabaseSession(options);
        await VerifyEmptyLeaseAsync(session);

        var catalog = GeneratorTests.Catalog();
        catalog.ConfigTables[0].TableName = Table;
        catalog.ConfigPages[0].Extra["type"] = JsonSerializer.SerializeToElement(2);
        catalog.ConfigPageColumns.Single(column => column.ConfigColumnId == "13").IsShow = 0;
        catalog.ConfigColumns.Add(new() { Id = "16", ConfigTableId = "1", ColumnName = "hidden_payload", LogicalType = "string", ColumnLength = "max" });
        var schema = new SchemaService(session);
        var plan = await schema.PreviewAsync(catalog);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("createTable", change.Kind);
        Assert.Equal(Table, change.Table);
        Assert.True((await schema.ApplyAsync(plan)).Succeeded);

        var store = new SqlRowStore(session, catalog);
        foreach (var (id, project, deleted) in new[] { (FirstId, 100L, false), (FirstId + 1, 100L, false), (FirstId + 2, 101L, false), (FirstId + 3, 100L, true) })
            await store.InsertAsync(Table, new Dictionary<string, object?>
            {
                ["id"] = id, ["name"] = "同名客户", ["remark"] = "详情专用备注", ["yz_project_id"] = project,
                ["delete_no"] = deleted ? DateTime.UtcNow : null, ["hidden_payload"] = new string('x', 16384)
            });

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-published-projection-mysql");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            const string ns = "Example.PublishedProjection";
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(session);
            builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(895));
            builder.Services.AddSingleton<IRuntimeAuthorization>(new ReadAuthorization());
            assembly.GetType(ns + ".Generated.GeneratedRegistration")!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly)
                .AddJsonOptions(value => value.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                try { await next(); }
                catch (InvalidOperationException error)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));
                }
            });
            app.MapControllers();
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)), Timeout = TimeSpan.FromSeconds(15) };
            var commands = new ConcurrentQueue<string>();
            session.Orm.Aop.CommandBefore += (_, args) =>
            {
                var sql = args.Command.CommandText;
                if (sql.Contains(Table, StringComparison.OrdinalIgnoreCase)) commands.Enqueue(sql);
            };
            await VerifyReadsAsync(client, commands);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>核对分页、详情、已选顺序和整组拒绝，并核对每次实际 SQL。</summary>
    private static async Task VerifyReadsAsync(HttpClient client, ConcurrentQueue<string> commands)
    {
        var page = await ReadAsync(await client.PostAsJsonAsync(Route + "/page", new { pageNo = 2, pageSize = 1 }));
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        var row = Assert.Single(page.GetProperty("records").EnumerateArray());
        var id = row.GetProperty("id").GetString();
        Assert.Contains(id, new[] { FirstId.ToString(), (FirstId + 1).ToString() });
        AssertRow(row);
        AssertProjection(commands, paged: true);

        var detail = await ReadAsync(await client.GetAsync(Route + "/detail/" + id));
        Assert.Equal(row.GetRawText(), detail.GetRawText());
        AssertProjection(commands);

        var ids = new[] { (FirstId + 1).ToString(), FirstId.ToString() };
        var selection = await ReadAsync(await client.PostAsJsonAsync(Route + "/selection/selected", new { selectedIds = ids }));
        Assert.Equal(ids, selection.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.All(selection.EnumerateArray(), AssertRow);
        AssertProjection(commands);

        foreach (var rejected in new[] { FirstId + 2, FirstId + 3 })
        {
            using var response = await client.PostAsJsonAsync(Route + "/selection/selected", new { selectedIds = new[] { ids[0], rejected.ToString() } });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var denied = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("不在当前多选页范围", denied.GetProperty("msg").GetString());
            AssertProjection(commands);
        }
    }

    /// <summary>核对实例与空库，保证仅在本轮独占租约创建合成表。</summary>
    private static async Task VerifyEmptyLeaseAsync(DatabaseSession session)
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var instance = settings.RootElement.GetProperty("InstancePin");
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@hostname,@@server_uuid,DATABASE(),(SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE())";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(instance.GetProperty("hostname").GetString(), reader.GetString(0));
        Assert.Equal(instance.GetProperty("serverUuid").GetString(), reader.GetString(1));
        Assert.Equal(session.Options.ExpectedDatabase, reader.GetString(2));
        Assert.Equal(0L, reader.GetInt64(3));
    }

    /// <summary>SELECT 排除未发布列，WHERE 保留项目、软删除约束；分页保留 COUNT。</summary>
    private static void AssertProjection(ConcurrentQueue<string> commands, bool paged = false)
    {
        var sql = commands.ToArray();
        commands.Clear();
        Assert.Equal(paged ? 2 : 1, sql.Length);
        if (paged) Assert.Single(sql, query => query.Contains("count(", StringComparison.OrdinalIgnoreCase));
        var select = Assert.Single(sql, query => !query.Contains("count(", StringComparison.OrdinalIgnoreCase));
        var from = select.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        Assert.True(from > 0);
        var fields = select[..from];
        foreach (var name in new[] { "id", "name", "remark" }) Assert.Contains((char)96 + name + (char)96, fields);
        foreach (var excluded in new[] { "hidden_payload", "yz_project_id", "delete_no" }) Assert.DoesNotContain(excluded, fields);
        Assert.Contains("yz_project_id", select[from..]);
        Assert.Contains("delete_no", select[from..]);
        if (paged) Assert.Contains("LIMIT 1,1", select, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRow(JsonElement row)
    {
        Assert.Equal(JsonValueKind.String, row.GetProperty("id").ValueKind);
        Assert.Equal("详情专用备注", row.GetProperty("remark").GetString());
        Assert.Equal("同名客户", row.GetProperty("name").GetString());
        Assert.False(row.TryGetProperty("hiddenPayload", out _));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(200, body.GetProperty("code").GetInt32());
            return body.GetProperty("data");
        }
    }

    private sealed class ReadAuthorization : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => new(895, "固定读取验收", false, "100", "1");
        public void RequirePagePermission(string pageId, string operation)
        {
            if (pageId != "101" || operation is not ("list" or "detail"))
                throw new UnauthorizedAccessException("当前专项只授予固定读取。");
        }
    }
}
