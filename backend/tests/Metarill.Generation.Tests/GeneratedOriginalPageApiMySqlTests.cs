using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际生成 Controller/DTO/Service 接收原 V8 完整实体及旧候选请求，真实 MySQL 验证只写开放字段。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GeneratedOriginalPageApiMySqlTests
{
    private const long Project = 9007199254740993L, Parent = 9007199254742001L, First = 9007199254750000L;

    [StandaloneExportMySqlFact]
    public async Task Original_entity_options_sort_and_unpaged_requests_keep_strict_write_and_scope_boundaries()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("原页面兼容验收仅允许明确的隔离MySQL业务库。");
        using var session = new DatabaseSession(options); var prefix = "mt_legacy_" + Guid.NewGuid().ToString("N")[..12]; var catalog = Catalog(prefix);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-page-http"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        WebApplication? app = null; var installed = false;
        try
        {
            var schema = new SchemaService(session); Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var plan = await schema.PreviewAsync(catalog); Assert.Equal(3, plan.Changes.Count); installed = true; Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await Execute(session, "INSERT INTO " + Q(session, prefix + "_parent") + " (id,name,yz_project_id) VALUES (@id,'本轮父记录',@project)", ("id", Parent), ("project", Project));
            var eventTimes = new[] { "2024-02-01 00:00:00", "2024-02-28 23:59:59", "2024-02-29 00:00:00", "2024-02-29 12:00:00", "2024-02-29 23:59:59", "2024-03-01 00:00:00", "2024-01-31 23:59:59" };
            for (var index = 0; index < 23; index++)
            {
                await Execute(session, "INSERT INTO " + Q(session, prefix + "_target") + " (id,name,yz_project_id,delete_no) VALUES (@id,@name,@project,NULL)", ("id", First + index), ("name", "候选" + index.ToString("D2")), ("project", Project));
                await Execute(session, "INSERT INTO " + Q(session, prefix + "_source") + " (id,name,yz_project_id,parent_id,readonly_code,status,remark) VALUES (@id,@name,@project,@parent,'原只读值',1,'原备注')", ("id", First + 1000 + index), ("name", "来源" + index.ToString("D2")), ("project", Project), ("parent", Parent));
                if (index < eventTimes.Length) await Execute(session, "UPDATE " + Q(session, prefix + "_source") + " SET event_time=@time WHERE id=@id", ("time", DateTime.Parse(eventTimes[index], System.Globalization.CultureInfo.InvariantCulture)), ("id", First + 1000 + index));
            }
            await Execute(session, "INSERT INTO " + Q(session, prefix + "_target") + " (id,name,yz_project_id,delete_no) VALUES (@foreign,'外项目',@foreignProject,NULL),(@deleted,'已删',@project,@now)", ("foreign", First + 50), ("foreignProject", Project + 1), ("deleted", First + 51), ("project", Project), ("now", DateTime.UtcNow));
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.LegacyProbe");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
            builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(session); builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(858));
            builder.Services.AddScoped<IRuntimeAuthorization>(provider => new Authorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.LegacyProbe.Generated.GeneratedRegistration", true)!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration => configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app = builder.Build(); app.Use(async (context, next) => { try { await next(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or JsonException) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message)); } }); app.MapControllers(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(15) };
            var normal = "/api/admin/auto/" + prefix + "_source/201"; var child = normal.Replace("/201", "/202", StringComparison.Ordinal); var parentQuery = "?parentId=" + Parent;
            // 原新增表单带空 id、父实体键和展示回显，服务器仍自行分配 ID 与真实父键。
            var created = Data(await Post(client, child + "/add" + parentQuery, new { id = "", name = "原页新增", parentId = Parent.ToString(), yzProjectId = Project.ToString(), status = 1, statusShow = "伪标签", readonlyCode = "客户端不能写", remark = "初始备注", buyerId = First.ToString() }));
            var id = created.GetProperty("id").GetString()!; Assert.Equal("服务器默认值", created.GetProperty("readonlyCode").GetString()); Assert.Equal("启用", created.GetProperty("statusShow").GetString());
            var echo = JsonNode.Parse(created.GetRawText())!.AsObject(); echo["name"] = "原页整实体更新"; echo["readonlyCode"] = "仍不能写"; echo["remark"] = null; echo["parentId"] = Parent.ToString();
            var updated = Data(await Post(client, child + "/update" + parentQuery, echo)); Assert.Equal("服务器默认值", updated.GetProperty("readonlyCode").GetString()); Assert.Equal(JsonValueKind.Null, updated.GetProperty("remark").ValueKind);
            Data(await Post(client, child + "/update" + parentQuery, new { id, remark = "必须保留" }));
            Assert.Equal("必须保留", Data(await Post(client, child + "/update" + parentQuery, new { id, name = "仅修改名称" })).GetProperty("remark").GetString());
            async Task<string> Current() { var response = Data(await Read(client, child + "/detail/" + id + parentQuery)); return response.GetRawText(); }
            var before = await Current();
            foreach (var invalid in new object[] { new { id, sql = "1=1" }, new { id, yzProjectId = (Project + 1).ToString() }, new { id, parentId = (Parent + 1).ToString() } })
            { Assert.Equal(500, (await Post(client, child + "/update" + parentQuery, invalid)).GetProperty("code").GetInt32()); Assert.Equal(before, await Current()); }
            using (var duplicate = await client.PostAsync(child + "/update" + parentQuery, new StringContent("{\"id\":\"" + id + "\",\"name\":\"甲\",\"name\":\"乙\"}", Encoding.UTF8, "application/json"))) Assert.Equal(HttpStatusCode.InternalServerError, duplicate.StatusCode);
            Assert.Equal(before, await Current());
            // 来源字段 buyer_id 与目标物理表名不同，两种旧新固定路由均命中相同候选规则。
            var legacy = normal + "/findNameIdBuyer25"; var current = normal + "/findNameId" + Pascal(prefix + "_target") + "25";
            foreach (var endpoint in new[] { legacy, current })
            {
                using var empty = await client.PostAsync(endpoint, null); var all = Data(JsonSerializer.Deserialize<JsonElement>(await empty.Content.ReadAsStringAsync())); Assert.Equal(23, all.GetArrayLength());
                var selected = Data(await Post(client, endpoint, new { id = (First + 22).ToString(), formParameters = new { } })); Assert.Equal((First + 22).ToString(), Assert.Single(selected.EnumerateArray()).GetProperty("id").GetString());
                Assert.Empty(Data(await Post(client, endpoint, new { id = "" })).EnumerateArray());
                foreach (var bad in new[] { First + 50, First + 51, First + 99 }) Assert.Equal(500, (await Post(client, endpoint, new { id = bad.ToString() })).GetProperty("code").GetInt32());
                client.DefaultRequestHeaders.Add("denyPage", "301"); using var denied = await client.PostAsync(endpoint, null); Assert.Equal(HttpStatusCode.InternalServerError, denied.StatusCode); client.DefaultRequestHeaders.Remove("denyPage");
            }
            // 原 uni-th 第三次点击发 order:null，不能当成非法 SQL 排序方向。
            var defaultPage = Data(await Post(client, normal + "/page", new { pageNo = 1, pageSize = 5, filters = new { } }));
            foreach (var direction in new string?[] { null, "" })
            {
                var reset = Data(await Post(client, normal + "/page", new { pageNo = 1, pageSize = 5, filters = new { }, orderBy = new { column = "name", order = direction } }));
                Assert.Equal(defaultPage.GetProperty("records")[0].GetProperty("id").GetString(), reset.GetProperty("records")[0].GetProperty("id").GetString());
            }
            Assert.Equal(500, (await Post(client, normal + "/page", new { orderBy = new { column = "name", order = "name;DROP" } })).GetProperty("code").GetInt32());
            var unpaged = normal.Replace("/201", "/204", StringComparison.Ordinal);
            var entire = Data(await Post(client, unpaged + "/page", new { pageNo = 1, pageSize = 0, filters = new { } })); Assert.Equal(24, entire.GetProperty("records").GetArrayLength()); Assert.Equal("24", entire.GetProperty("total").ToString()); Assert.Equal("0", entire.GetProperty("pages").ToString()); Assert.Equal(0, entire.GetProperty("size").GetInt32());
            Assert.Equal(500, (await Post(client, normal + "/page", new { pageNo = 1, pageSize = 0 })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, unpaged + "/page", new { pageNo = 2, pageSize = 0 })).GetProperty("code").GetInt32());
            // 原Date/月筛选传YYYY-MM-DD范围，截止日白天不能漏；显式午夜时间仍只到该时刻。
            foreach (var (start, end, expected) in new[] { ("2024-02-01", "2024-02-29", 5), ("2024-02-29", "2024-02-29", 3), ("2024-02-29T00:00:00", "2024-02-29T00:00:00", 1), ("2024-02-29 00:00:00", "2024-02-29 12:00:00", 2), ("2024-02-29 12:00:00", "2024-02-29", 2), ("2024-02-29", "2024-03-01T00:00:00", 4) })
            {
                var filter = new { filters = new { eventTime = new[] { start, end } } };
                var found = Data(await Post(client, normal + "/page", filter)); Assert.Equal(expected, found.GetProperty("records").GetArrayLength());
                Assert.Equal(expected.ToString(), Data(await Post(client, normal + "/count", filter)).ToString());
                Assert.DoesNotContain(found.GetProperty("records").EnumerateArray(), row => row.GetProperty("eventTime").ValueKind == JsonValueKind.Null);
            }
            Assert.Equal("24", Data(await Post(client, normal + "/count", new { filters = new { eventTime = Array.Empty<string>() } })).ToString());
            foreach (var dates in new object[] { new[] { "2024-02-30", "2024-03-01" }, new[] { "2024-03-01", "2024-02-29" }, new[] { "2024-02-29T12:00:00", "2024-02-29T00:00:00" }, new[] { "2024-02-29" }, new object?[] { null, "2024-02-29" }, new[] { "1 OR 1=1", "2024-02-29" } })
                Assert.Equal(500, (await Post(client, normal + "/page", new { filters = new { eventTime = dates } })).GetProperty("code").GetInt32());
            using (var disabled = await client.PostAsJsonAsync(normal.Replace("/201", "/203", StringComparison.Ordinal) + "/update", new { id, name = "不能保存" })) Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
            client.DefaultRequestHeaders.Add("denyPage", "202"); Assert.Equal(500, (await Post(client, child + "/update" + parentQuery, new { id, name = "拒绝写" })).GetProperty("code").GetInt32()); client.DefaultRequestHeaders.Remove("denyPage"); Assert.Equal(before, await Current());
            // 原删除提交整个实体，服务端仍只读取 ID 并在真实父范围删除。
            var deleteEntity = JsonNode.Parse(before)!.AsObject(); deleteEntity["parentId"] = Parent.ToString();
            using var deletion = new HttpRequestMessage(HttpMethod.Delete, child + parentQuery) { Content = JsonContent.Create(deleteEntity) }; Data(JsonSerializer.Deserialize<JsonElement>(await (await client.SendAsync(deletion)).Content.ReadAsStringAsync()));
            Assert.Equal(500, (await Read(client, child + "/detail/" + id + parentQuery)).GetProperty("code").GetInt32());
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            if (installed) using (var cleanup = new DatabaseSession(options))
            {
                foreach (var table in catalog.ConfigTables) { Assert.StartsWith(prefix + "_", table.TableName); await Execute(cleanup, "DROP TABLE IF EXISTS " + Q(cleanup, table.TableName)); }
                Assert.Empty((await new SchemaService(cleanup).InspectAsync(catalog)).Tables);
            }
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>仅固定测试元数据包含三张随机独占表；不引用用户表或执行整库初始化。</summary>
    private static MetadataCatalog Catalog(string prefix)
    {
        var catalog = RelationOptionsContractTests.Catalog(prefix); var page = catalog.ConfigPages.Single(page => page.Id == "201"); page.WhereSql = "yz_project_id=@current_yz_project_id"; page.OrderBy = "name asc";
        catalog.ConfigPages.Single(page => page.Id == "301").WhereSql = "yz_project_id=@current_yz_project_id";
        var buyer = catalog.ConfigColumns.Single(column => column.Id == "25"); buyer.ColumnName = "buyer_id"; buyer.ControlDetail = "{\"tableId\":\"3\",\"nameField\":\"name\"}";
        catalog.ConfigColumns.AddRange([new() { Id = "27", ConfigTableId = "2", ColumnName = "readonly_code", LogicalType = "string", DefaultValue = "服务器默认值" }, new() { Id = "28", ConfigTableId = "2", ColumnName = "status", LogicalType = "int32", ControlKind = "ConstList", ControlDetail = "启用|1,停用|0" }, new() { Id = "29", ConfigTableId = "2", ColumnName = "remark", LogicalType = "string" }]);
        catalog.ConfigColumns.Add(new() { Id = "210", ConfigTableId = "2", ColumnName = "event_time", LogicalType = "datetime", ControlKind = "Date" });
        catalog.ConfigPageColumns.Add(new() { Id = "201_210", ConfigPageId = "201", ConfigColumnId = "210", IsEdit = 0, IsShow = 1, Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) } });
        foreach (var (id, editable) in new[] { ("23", false), ("27", false), ("28", true), ("29", true) }) catalog.ConfigPageColumns.Add(new() { Id = "201_" + id, ConfigPageId = "201", ConfigColumnId = id, IsEdit = editable ? 1 : 0, IsShow = 1 });
        foreach (var (id, parent, unpaged) in new[] { ("202", true, false), ("203", false, false), ("204", false, true) })
        {
            var copy = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!; copy.Id = id; if (parent) copy.WhereSql += " AND parent_id=@parentId"; if (unpaged) copy.PageSize = 0; if (id == "203") copy.Extra["isUpdate"] = JsonSerializer.SerializeToElement(0); catalog.ConfigPages.Add(copy);
            foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == "201").ToArray()) { var cloned = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!; cloned.Id = id + "_" + field.ConfigColumnId; cloned.ConfigPageId = id; catalog.ConfigPageColumns.Add(cloned); }
        }
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == "22").Extra["isSort"] = JsonSerializer.SerializeToElement(1);
        return catalog;
    }
    private sealed class Authorization(IHttpContextAccessor accessor) : IRuntimeAuthorization
    {
        /// <summary>固定真实宿主身份，不从请求选择账号或项目。</summary>
        public RuntimeActor RequireActor() => new(858, "原页面兼容验收", false, Project.ToString(), "1");
        /// <summary>拒绝头只用于模拟权限撤销。</summary>
        public void RequirePagePermission(string pageId, string operation) { if (accessor.HttpContext!.Request.Headers["denyPage"] == pageId) throw new UnauthorizedAccessException("没有页面权限。"); }
    }
    /// <summary>数据库值全部参数化，标识符只来自随机测试目录。</summary>
    private static async Task Execute(DatabaseSession session, string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter); } await command.ExecuteNonQueryAsync();
    }
    /// <summary>只引用本轮声明的随机物理标识符。</summary>
    private static string Q(DatabaseSession session, string table) => session.Provider.QuoteIdentifier(table);
    /// <summary>类型名称与生成器固定命名一致。</summary>
    private static string Pascal(string value) => string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    /// <summary>发送实际旧页面 JSON，不绕过生成 HTTP 边界。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string path, object body) => JsonSerializer.Deserialize<JsonElement>(await (await client.PostAsJsonAsync(path, body)).Content.ReadAsStringAsync());
    /// <summary>回读真实接口，不把内存对象当持久结果。</summary>
    private static async Task<JsonElement> Read(HttpClient client, string path) => JsonSerializer.Deserialize<JsonElement>(await (await client.GetAsync(path)).Content.ReadAsStringAsync());
    /// <summary>只接受统一业务成功响应。</summary>
    private static JsonElement Data(JsonElement response) { Assert.True(response.GetProperty("code").GetInt32() == 200, response.GetRawText()); return response.GetProperty("data"); }
}
