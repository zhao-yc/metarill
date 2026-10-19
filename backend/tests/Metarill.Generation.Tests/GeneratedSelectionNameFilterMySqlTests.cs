using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
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
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

/// <summary>真实生成原名称键查询；分页、统计与导出必须在同一固定范围内匹配解码后的数组成员。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GeneratedSelectionNameFilterMySqlTests(ITestOutputHelper output)
{
    private const long Project = 9007199254740993L, Parent = 9007199254742001L, First = 9007199254750000L;
    [StandaloneExportMySqlFact]
    public async Task Original_scalar_and_multi_name_filters_apply_before_page_count_and_export_without_scope_fallback()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("名称筛选仅允许明确的隔离MySQL业务库。");
        using var session = new DatabaseSession(options); var prefix = "mt_name_filter_" + Guid.NewGuid().ToString("N")[..12]; var catalog = Catalog(prefix);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-name-filter"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        WebApplication? app = null; var installed = false;
        try
        {
            var schema = new SchemaService(session); Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var plan = await schema.PreviewAsync(catalog); Assert.Equal(3, plan.Changes.Count); installed = true; Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await Execute(session, "INSERT INTO " + Q(session, prefix + "_parent") + " (id,name,yz_project_id) VALUES (@id,'本轮父',@project)", ("id", Parent), ("project", Project));
            var rows = new[] {
                ("[\"\\u4e2d\\u6587\"]", "甲中文"), ("[\"中文\"]", "乙中文"), ("[\"字面%_!\"]", "字面%_!"),
                ("[\"字面XX\"]", "字面XX"), ("[\"a\\\"b\\\\c\"]", "引号与反斜杠"), ("{broken", "损坏文档"),
                ("{\"name\":\"中文\"}", "对象"), ("[[\"中文\"],\"中文\"]", "嵌套"), ("[123,true,null,\"中文\"]", "丙中文"),
                ("[\"中文\"]", "外项目"), ("[\"中文\"]", "外父"), ("[\"中文\"]", "已删"),
                ("[\"first\",\"second\"]", "两个成员"), ("[\"' OR 1=1 --\"]", "单引号"),
                ("[\"Cafe\"]", "Cafe"), ("[\"cafe\"]", "cafe"), ("[\"Café\"]", "Café"), ("[\"CAFÉ\"]", "CAFÉ")
            };
            for (var index = 0; index < rows.Length; index++)
                await Execute(session, "INSERT INTO " + Q(session, prefix + "_source") + " (id,name,yz_project_id,parent_id,delete_no,target_ids,target_names,single_id,single_name) VALUES (@id,@name,@project,@parent,@deleted,'[]',@names,@target,@single)", ("id", First + index + 1), ("name", "行" + (index + 1).ToString("D2")), ("project", index == 9 ? Project + 1 : Project), ("parent", index == 10 ? Parent + 1 : Parent), ("deleted", index == 11 ? DateTime.UtcNow : null), ("names", rows[index].Item1), ("target", First + 100), ("single", rows[index].Item2));
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.NameQuery");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
            builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(session); builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(862)); builder.Services.AddScoped<IRuntimeAuthorization>(provider => new Authorization(provider.GetRequiredService<IHttpContextAccessor>()));
            var sink = new ProjectionSink(); builder.Services.AddSingleton<IPageExportService>(sink);
            assembly.GetType("Example.NameQuery.Generated.GeneratedRegistration", true)!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration => configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app = builder.Build(); app.Use(async (context, next) => { try { await next(); } catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or JsonException) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message)); } }); app.MapControllers(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(15) };
            var route = "/api/admin/auto/" + prefix + "_source/201";
            var commands = new List<string>(); session.Orm.Aop.CommandBefore += (_, args) => { if (args.Command.CommandText.Contains(prefix + "_source", StringComparison.Ordinal)) commands.Add(args.Command.CommandText); };
            object Request(string key, object value, int number = 1, int size = 20) => new { parentId = Parent.ToString(), pageNo = number, pageSize = size, filters = new Dictionary<string, object> { [key] = value } };
            string[] Names(JsonElement page) => page.GetProperty("records").EnumerateArray().Select(row => row.GetProperty("name").GetString()!).ToArray();
            foreach (var key in new[] { "targetNames", "singleName" })
            {
                var first = Data(await Post(client, route + "/page", Request(key, "中文", size: 2))); Assert.Equal(new[] { "行01", "行02" }, Names(first)); Assert.Equal("3", first.GetProperty("total").ToString());
                Assert.Equal(new[] { "行09" }, Names(Data(await Post(client, route + "/page", Request(key, "中文", number: 2, size: 2)))));
                Assert.Equal("3", Data(await Post(client, route + "/count", Request(key, "中文"))).ToString());
                Data(await Post(client, route + "/initExport", Request(key, "中文"))); Assert.Equal(new[] { "行01", "行02", "行09" }, sink.Last!.Rows.Select(row => Assert.IsType<string>(row[0])));
            }
            foreach (var (text, expected) in new[] { ("%_!", "行03"), ("单引号", "行14") })
                Assert.Equal(new[] { expected }, Names(Data(await Post(client, route + "/page", Request("singleName", text)))));
            foreach (var (text, expected) in new[] { ("%_!", "行03"), ("a\"b\\c", "行05"), ("' OR 1=1 --", "行14") })
            {
                Assert.Equal(new[] { expected }, Names(Data(await Post(client, route + "/page", Request("targetNames", text)))));
                Assert.Equal("1", Data(await Post(client, route + "/count", Request("targetNames", text))).ToString());
            }
            foreach (var text in new[] { "first\",\"second", "123", "true", "null" }) Assert.Equal("0", Data(await Post(client, route + "/count", Request("targetNames", text))).ToString());
            output.WriteLine("当前随机列排序规则：" + await ColumnCollations(session, prefix + "_source"));
            foreach (var text in new[] { "cafe", "Cafe", "Café" })
            {
                var scalar = Names(Data(await Post(client, route + "/page", Request("singleName", text))));
                var json = Names(Data(await Post(client, route + "/page", Request("targetNames", text))));
                output.WriteLine("关键词=" + text + "；标量=" + string.Join(',', scalar) + "；JSON成员=" + string.Join(',', json));
                var baseline = await CompareOriginalLike(session, prefix + "_source", text);
                Assert.Equal(baseline["oldScalarLike"], scalar); Assert.Equal(baseline["oldJsonTextLike"], json);
                foreach (var comparison in baseline) output.WriteLine("基线/探针=" + text + "/" + comparison.Key + ":" + string.Join(',', comparison.Value));
                Assert.All(scalar.Concat(json), name => Assert.Contains(name, new[] { "行15", "行16", "行17", "行18" }));
            }
            // 源ID筛选仍独立开放，不能被名称别名替代；两名称条件也能在同一查询绑定不同参数。
            Assert.Equal("15", Data(await Post(client, route + "/count", Request("singleId", (First + 100).ToString()))).ToString());
            Assert.Equal(new[] { "行01" }, Names(Data(await Post(client, route + "/page", new { parentId = Parent.ToString(), filters = new { targetNames = "中文", singleName = "甲" } }))));
            foreach (var bad in new object[] { 1, new { sql = "1=1" }, new[] { "中文" }, Array.Empty<string>() })
                foreach (var key in new[] { "targetNames", "singleName" }) Assert.Equal(500, (await Post(client, route + "/count", Request(key, bad))).GetProperty("code").GetInt32());
            foreach (var key in new[] { "whereSql", "table", "privatePayload" }) Assert.Equal(500, (await Post(client, route + "/count", Request(key, "1=1"))).GetProperty("code").GetInt32());
            var beforeDenied = commands.Count; var grants = sink.Issued; client.DefaultRequestHeaders.Add("denyPage", "201");
            foreach (var endpoint in new[] { "/page", "/count", "/initExport" }) Assert.Equal(500, (await Post(client, route + endpoint, Request("targetNames", "中文"))).GetProperty("code").GetInt32());
            client.DefaultRequestHeaders.Remove("denyPage"); Assert.Equal(beforeDenied, commands.Count); Assert.Equal(grants, sink.Issued);
            Assert.NotEmpty(commands); Assert.Contains(commands, sql => sql.Contains("JSON_SEARCH", StringComparison.Ordinal));
            Assert.All(commands, sql => { Assert.DoesNotContain("中文", sql); Assert.DoesNotContain("OR 1=1", sql); Assert.DoesNotContain("%_!", sql); });
            // 缺口键与普通字段冲突后固定拒绝；空白初始筛选可忽略，但不能退回JSON文本LIKE。
            var gap = route.Replace("/201", "/202", StringComparison.Ordinal); var denied = await Post(client, gap + "/count", Request("targetNames", "中文")); Assert.Equal(500, denied.GetProperty("code").GetInt32()); Assert.Contains("冲突", denied.GetProperty("msg").GetString());
            Assert.Equal("15", Data(await Post(client, gap + "/count", Request("targetNames", ""))).ToString());
            Assert.Equal("15", Data(await Post(client, route + "/count", Request("targetNames", " "))).ToString());
            // 仅改变本轮随机表两列，证明派生模式确实跟随实际列而非写死默认unicode_ci。
            foreach (var collation in new[] { "utf8mb4_bin", "utf8mb4_0900_ai_ci" })
            {
                await Execute(session, "ALTER TABLE " + Q(session, prefix + "_source") + " MODIFY single_name VARCHAR(255) CHARACTER SET utf8mb4 COLLATE " + collation + " NULL, MODIFY target_names LONGTEXT CHARACTER SET utf8mb4 COLLATE " + collation + " NULL");
                output.WriteLine("仅随机fixture改列排序规则：" + await ColumnCollations(session, prefix + "_source"));
                foreach (var text in new[] { "cafe", "Cafe", "Café" })
                {
                    var comparison = await CompareOriginalLike(session, prefix + "_source", text);
                    Assert.Equal(comparison["oldJsonTextLike"], comparison["jsonColumnPattern"]);
                    Assert.Equal(comparison["oldJsonTextLike"], comparison["jsonPatternCollate"]);
                    Assert.Equal(comparison["oldScalarLike"], Names(Data(await Post(client, route + "/page", Request("singleName", text)))));
                    Assert.Equal(comparison["oldJsonTextLike"], Names(Data(await Post(client, route + "/page", Request("targetNames", text)))));
                    output.WriteLine("实际列=" + collation + "/" + text + "；旧LIKE=" + string.Join(',', comparison["oldJsonTextLike"]) + "；列派生JSON=" + string.Join(',', comparison["jsonColumnPattern"]));
                }
            }

        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            if (installed) using (var cleanup = new DatabaseSession(options)) { foreach (var table in catalog.ConfigTables) { Assert.StartsWith(prefix + "_", table.TableName); await Execute(cleanup, "DROP TABLE IF EXISTS " + Q(cleanup, table.TableName)); } Assert.Empty((await new SchemaService(cleanup).InspectAsync(catalog)).Tables); }
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>保留三张随机表并声明真正目标页与同父范围；名称均为既有物理辅助列。</summary>
    private static MetadataCatalog Catalog(string prefix)
    {
        var catalog = GeneratedMultiSelectContractTests.Catalog(prefix); var source = catalog.ConfigPages.Single(page => page.Id == "201"); source.IsExport = 1; source.OrderBy = "name asc";
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == "25").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.AddRange([new() { Id = "28", ConfigTableId = "2", ColumnName = "single_id", LogicalType = "int64", LinkConfigTableId = "3", ControlKind = "SelectPage", ControlDetail = "{\"pageId\":\"302\",\"nameField\":\"name\"}" }, new() { Id = "29", ConfigTableId = "2", ColumnName = "single_name", LogicalType = "string" }]);
        catalog.ConfigPageColumns.AddRange([new() { Id = "2004", ConfigPageId = "201", ConfigColumnId = "28", IsShow = 1, Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) } }, new() { Id = "2005", ConfigPageId = "201", ConfigColumnId = "29", IsShow = 1, IsEdit = 0 }]);
        var target = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page => page.Id == "301")))!; target.Id = "302"; target.Extra["type"] = JsonSerializer.SerializeToElement(1); catalog.ConfigPages.Add(target);
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == "301").ToArray()) { var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!; copy.Id = "302_" + field.ConfigColumnId; copy.ConfigPageId = "302"; catalog.ConfigPageColumns.Add(copy); }
        var gap = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!; gap.Id = "202"; catalog.ConfigPages.Add(gap);
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == "201").ToArray()) { var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!; copy.Id = "202_" + field.ConfigColumnId; copy.ConfigPageId = "202"; if (copy.ConfigColumnId == "27") copy.Extra["isSearch"] = JsonSerializer.SerializeToElement(1); catalog.ConfigPageColumns.Add(copy); }
        return catalog;
    }
    private sealed class Authorization(IHttpContextAccessor accessor) : IRuntimeAuthorization
    {
        /// <summary>身份由宿主固定建立，客户端只能模拟撤权，不能选择账号或项目。</summary>
        public RuntimeActor RequireActor() => new(862, "名称查询验收", false, Project.ToString(), "1");
        /// <summary>真实Controller仍检查固定页和操作授权。</summary>
        public void RequirePagePermission(string pageId, string operation) { if (accessor.HttpContext?.Request.Headers["denyPage"].ToString() == pageId) throw new UnauthorizedAccessException("没有页面权限。"); }
    }
    private sealed class ProjectionSink : IPageExportService
    {
        public PageExportRequest? Last { get; private set; }
        public int Issued { get; private set; }
        /// <summary>不把普通票据当短下载授权，也不伪称Excel字节验收。</summary>
        public Task<string> CreateAsync(PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default) => throw new InvalidOperationException("本fixture仅核导出查询投影。");
        /// <summary>接收真实生成Service已过滤的行，限制100条，不写文件。</summary>
        public async Task<string> CreateDownloadAsync(RuntimeActor actor, Func<int, CancellationToken, Task<PageExportRequest>> prepare, CancellationToken cancellationToken = default) { Last = await prepare(100, cancellationToken); Issued++; return new string('a', 64); }
    }
    /// <summary>同四条公开fixture并列旧列LIKE与受控JSON表达式，COLLATE仅取实际随机列的服务端标识符。</summary>
    private static async Task<IReadOnlyDictionary<string, string[]>> CompareOriginalLike(DatabaseSession session, string table, string keyword)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync();
        string collation;
        await using (var definition = connection.CreateCommand())
        {
            definition.CommandText = "SELECT collation_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name='target_names'";
            var tableParameter = definition.CreateParameter(); tableParameter.ParameterName = "table"; tableParameter.Value = table; definition.Parameters.Add(tableParameter); collation = (string)(await definition.ExecuteScalarAsync())!;
        }
        MetadataValidation.RequireIdentifier(collation);
        var document = "CASE WHEN JSON_VALID(target_names)=1 THEN CASE WHEN JSON_TYPE(target_names)='ARRAY' AND JSON_DEPTH(target_names)<=2 THEN target_names ELSE '[]' END ELSE '[]' END";
        var modes = new Dictionary<string, string> {
            ["oldScalarLike"] = "single_name LIKE @pattern ESCAPE '!'",
            ["oldJsonTextLike"] = "target_names LIKE @pattern ESCAPE '!'",
            ["jsonNative"] = "JSON_SEARCH(" + document + ",'one',@pattern,'!','$[*]') IS NOT NULL",
            ["jsonPatternCollate"] = "JSON_SEARCH(" + document + ",'one',CONVERT(@pattern USING utf8mb4) COLLATE " + collation + ",'!','$[*]') IS NOT NULL",
            ["jsonDocumentCollate"] = "JSON_SEARCH((" + document + ") COLLATE " + collation + ",'one',@pattern,'!','$[*]') IS NOT NULL",
            ["jsonColumnPattern"] = "JSON_SEARCH(" + document + ",'one',CONCAT(LEFT(target_names,0),@pattern),'!','$[*]') IS NOT NULL"
        };
        var result = new Dictionary<string, string[]>();
        foreach (var mode in modes)
        {
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT name FROM " + Q(session, table) + " WHERE yz_project_id=@project AND parent_id=@parent AND delete_no IS NULL AND id BETWEEN @first AND @last AND (" + mode.Value + ") ORDER BY id";
            foreach (var (name, value) in new (string, object)[] { ("project", Project), ("parent", Parent), ("first", First + 15), ("last", First + 18), ("pattern", "%" + keyword + "%") }) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
            await using var reader = await command.ExecuteReaderAsync(); var values = new List<string>(); while (await reader.ReadAsync()) values.Add(reader.GetString(0)); result.Add(mode.Key, values.ToArray());
        }
        return result;
    }
    /// <summary>只读取本fixture两列排序规则，实测JSON原生成比较边界，不推断整个部署的默认配置。</summary>
    private static async Task<string> ColumnCollations(DatabaseSession session, string table)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name,collation_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name IN ('target_names','single_name') ORDER BY column_name";
        var parameter = command.CreateParameter(); parameter.ParameterName = "table"; parameter.Value = table; command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(); var values = new List<string>(); while (await reader.ReadAsync()) values.Add(reader.GetString(0) + "=" + reader.GetString(1)); Assert.Collection(values, value => Assert.StartsWith("single_name=", value), value => Assert.StartsWith("target_names=", value)); return string.Join(',', values);
    }
    /// <summary>固定本轮随机标识符，数据值全部参数化。</summary>
    private static string Q(DatabaseSession session, string table) => session.Provider.QuoteIdentifier(table);
    /// <summary>显式执行本fixture语句，不接收客户端SQL。</summary>
    private static async Task Execute(DatabaseSession session, string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter); } await command.ExecuteNonQueryAsync();
    }
    /// <summary>调用实际生成Controller的原PageRequest协议。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string route, object body) => JsonSerializer.Deserialize<JsonElement>(await (await client.PostAsJsonAsync(route, body)).Content.ReadAsStringAsync());
    /// <summary>成功回执必须明确code=200，不能把错误当空结果。</summary>
    private static JsonElement Data(JsonElement response) { Assert.True(response.GetProperty("code").GetInt32() == 200, response.GetRawText()); return response.GetProperty("data"); }
}
