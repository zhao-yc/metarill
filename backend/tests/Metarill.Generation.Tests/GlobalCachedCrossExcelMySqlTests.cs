using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>隔离业务库与实例pin都明确提供才运行，不把普通CI或固定身份宿主当作正式Web认证验收。</summary>
public sealed class GlobalCachedCrossExcelMySqlTheoryAttribute : TheoryAttribute
{
    public GlobalCachedCrossExcelMySqlTheoryAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")) || !File.Exists(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")) || !File.Exists(Environment.GetEnvironmentVariable(GlobalCachedCrossExcelMySqlLease.EnvironmentKey))) Skip = "未提供Server-200隔离库、实例pin及本轮精确临时表租约，全局缓存级联R0真实Excel未执行。";
    }
}

/// <summary>原生成Controller、真实MySQL和XLSX验证R0级联消费全局缓存名称，源表只保存各控件最深选择。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GlobalCachedCrossExcelMySqlTests
{
    internal const string Namespace = "Example.GlobalCachedCrossExcelSql";
    private const long Country = 9007199254742011, Area = 9007199254742021, Place = 9007199254742031, Warehouse = 9007199254742041, Lookup = 9007199254742051;
    /// <summary>根／中／末、全局其它层及共享／短控件分别用真实生成方法与原接口往返；不解除公开Full门槛。</summary>
    [GlobalCachedCrossExcelMySqlTheory]
    [InlineData(0, "string", "int64", false, false, false)]
    [InlineData(1, "int64", "string", false, false, false)]
    [InlineData(2, "string", "string", false, false, false)]
    [InlineData(0, "int64", "int64", true, false, false)]
    [InlineData(1, "string", "int64", false, true, false)]
    [InlineData(1, "int64", "string", false, true, true)]
    public async Task R0_original_Excel_uses_real_cached_names_sparse_storage_and_atomic_rollback(int role, string key, string parent, bool allGlobal, bool shared, bool shorter)
    {
        var lease = GlobalCachedCrossExcelMySqlLease.Read(role, key, parent, allGlobal, shared, shorter);
        var options = await NonRedundantCrossTableExcelMySqlTests.Options();
        var nonce = lease.Nonce; var prefix = lease.Prefix;
        var catalog = shared ? GlobalCrossCachedTargetFixture.CreateShared(0, key, parent, shorter) : GlobalCrossCachedTargetFixture.Create(0, role, key, parent, allGlobal);
        Prefix(catalog, prefix); var source = catalog.ConfigTables.Single(table => table.Id == "3").TableName;
        var names = catalog.ConfigTables.Select(table => table.TableName).ToArray();
        Assert.Equal(lease.Tables.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
        var root = NonRedundantApplicationFixture.Root(); var evidence = Path.Combine(root, ".local/global-cached-cross-excel-mysql", nonce); Directory.CreateDirectory(evidence);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-cached-cross-excel-sql"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        using var session = new DatabaseSession(options); var schema = new SchemaService(session); var store = new SqlRowStore(session, catalog);
        var observer = new LateFailure(source); WebApplication? app = null; var created = false; var verified = false; Exception? primary = null;
        var exportActive = 0; var exportReadCount = 0;
        var trace = new ConcurrentQueue<string>(); session.Orm.Aop.CommandBefore += (_, args) =>
        {
            if (!names.Any(name => args.Command.CommandText.Contains(name, StringComparison.Ordinal))) return;
            trace.Enqueue(args.Command.CommandText);
            if (Volatile.Read(ref exportActive) == 1 && args.Command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            { Assert.Equal(System.Data.IsolationLevel.RepeatableRead, args.Command.Transaction?.IsolationLevel); Assert.False(args.Command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)); Interlocked.Increment(ref exportReadCount); }
        };
        await Receipt("intent", new { nonce, role, key, parent, allGlobal, shared, shorter, names, temporaryDirectory = directory, database = options.ExpectedDatabase, verified = false });
        try
        {
            var generated = Path.Combine(directory, "generated"); new CSharpGenerator().Generate(catalog, generated, Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.Empty((await schema.InspectAsync(catalog)).Tables); var plan = await schema.PreviewAsync(catalog); Assert.Equal(names.Length, plan.Changes.Count);
            created = true; Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table => table.Name == source).Columns, column => column.Name.EndsWith("_name", StringComparison.Ordinal));
            var levels = GlobalCrossCachedTargetFixture.Levels(catalog); var ids = new[] { Country, Area, Place }; var labels = new[] { "国甲", "区甲", "城甲" };
            // 种子只落本nonce显式创建的表，缓存生产者与字典为独立全局表，不伪造来源名称存储。
            await session.InTransactionAsync(async () =>
            {
                await store.InsertAsync(prefix + "_lookup", Row("id", Lookup, "name", labels[shared ? 1 : role], "status", 1));
                for (var index = 0; index < levels.Length; index++)
                {
                    var level = levels[index]; var table = catalog.ConfigTables.Single(table => table.TableName == level.GetProperty("table").GetString());
                    var row = Row("id", ids[index], level.GetProperty("nameField").GetString()!, labels[index], "status", 1);
                    if (!GlobalCrossTableTarget.Declared(table)) row["yz_project_id"] = 100L;
                    if (table.Id == "1") row["lookup_id"] = Key(parent, Lookup);
                    if (index > 0) row[level.GetProperty("parentField").GetString()!] = Key(parent, ids[index - 1]);
                    await store.InsertAsync(table.TableName, row);
                    await store.InsertAsync(table.TableName, new Dictionary<string, object?>(row) { ["id"] = ids[index] + 100, ["status"] = 0 });
                    await store.InsertAsync(table.TableName, new Dictionary<string, object?>(row) { ["id"] = ids[index] + 200, ["delete_no"] = new DateTime(2026, 1, 1) });
                    if (!GlobalCrossTableTarget.Declared(table)) await store.InsertAsync(table.TableName, new Dictionary<string, object?>(row) { ["id"] = ids[index] + 300, ["yz_project_id"] = 101L });
                }
                if (shared) await store.InsertAsync(prefix + "_cross_warehouse", Row("id", Warehouse, "name", "仓甲", "status", 1, "yz_project_id", 100L, "area_id", Key(parent, Area)));
                return true;
            });
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
            var exports = new ExcelPageExportService(new() { RootDirectory = Path.Combine(directory, "exports"), MaxRows = 100 });
            builder.Services.AddSingleton<IPageExportService>(exports); builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(session); builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(958)); builder.Services.AddSingleton<IRecordMutationObserver>(observer); builder.Services.AddScoped<IRuntimeAuthorization, Authorization>();
            assembly.GetType(Namespace + ".Generated.GeneratedRegistration", true)!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(settings => settings.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app = builder.Build();
            app.Use(async (context, next) => { try { await next(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or PageImportException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message)); } });
            app.MapControllers(); app.MapGet(ExportDownloadAccess.DownloadPath, async (HttpContext context) => { if (!ExportDownloadAccess.CanUseGrant(context.Request)) return Results.Unauthorized(); var file = await exports.OpenDownloadAsync(context.Request.Query["exportId"]!); return Results.Stream(file.Stream, file.ContentType, file.FileName); }); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(60) };
            var endpoint = "/api/admin/auto/" + source + "/30/";
            var full = shorter ? "国甲/区甲" : "国甲/区甲/城甲"; var middle = shorter ? full : "国甲/区甲/"; var first = shorter ? "国甲/" : "国甲//";
            Data(await Import(Input("完整", full, shared ? "国甲/区甲/仓甲" : null), Input("根级", first, shared ? "国甲//" : null), Input("中级", middle, shared ? "国甲/区甲/" : null), Input("空值", null, null)));
            await Sparse("完整", shorter ? 1 : 2, shorter ? Area : Place, shared ? 2 : -1, shared ? Warehouse : null);
            await Sparse("根级", 0, Country, shared ? 0 : -1, shared ? Country : null); await Sparse("中级", 1, Area, shared ? 1 : -1, shared ? Area : null); await Sparse("空值", -1, null, -1, null);
            await session.InTransactionAsync(async () => { await store.InsertAsync(source, Row("id", Lookup + 1, "name", "其它项目", "yz_project_id", 101L, shorter ? "area_id" : "place_id", Key(key, shorter ? Area : Place))); return true; });
            var cells = await Export(); Assert.Equal(4, cells.Count); Assert.False(cells.ContainsKey("其它项目")); Assert.Equal((full, shared ? "国甲/区甲/仓甲" : null), cells["完整"]); Assert.Equal((first, shared ? "国甲//" : null), cells["根级"]); Assert.Equal((middle, shared ? "国甲/区甲/" : null), cells["中级"]); Assert.Equal(((string?)null, (string?)null), cells["空值"]);
            await Receipt("original-import-export", new { nonce, passed = true, exportRows = cells.Count, sparseStorage = true, originalHeaders = true });
            // 实际下载XLSX后的原文本再走原导入请求，不能用仅调用共享解析工具代替端点往返。
            Data(await Import(cells.Select(cell => Input("往返" + cell.Key, cell.Value.Main, cell.Value.Second)).ToArray())); await Sparse("往返完整", shorter ? 1 : 2, shorter ? Area : Place, shared ? 2 : -1, shared ? Warehouse : null);
            var before = await Snapshot(); Assert.Equal(500, (await Import(Input("回滚首行", full, shared ? "国甲/区甲/仓甲" : null), Input("后置拒绝", full, shared ? "国甲/区甲/仓甲" : null))).GetProperty("code").GetInt32()); Assert.True(observer.ObservedPartialDml); Assert.Equal(before, await Snapshot());
            await Receipt("roundtrip-and-late-rollback", new { nonce, passed = true, realWorkbookRoundtrip = true, partialDmlRollback = observer.ObservedPartialDml });
            client.DefaultRequestHeaders.Add("denyImport", "1"); Assert.Equal(500, (await Import(Input("拒权", full, null))).GetProperty("code").GetInt32()); client.DefaultRequestHeaders.Remove("denyImport"); Assert.Equal(before, await Snapshot());
            client.DefaultRequestHeaders.Add("denyExport", "1"); Assert.Equal(500, (await Post("initExport", new { })).GetProperty("code").GetInt32()); client.DefaultRequestHeaders.Remove("denyExport");
            // 修改原始全局字典须通过真实生成更新／混合传播，随后Excel必须读取实际新缓存值。
            var renamed = labels.ToArray(); renamed[shared ? 1 : role] += "新";
            using (var response = await client.PostAsJsonAsync("/api/admin/auto/" + prefix + "_lookup/20/update", new { id = Lookup.ToString(CultureInfo.InvariantCulture), name = renamed[shared ? 1 : role] })) Data(await response.Content.ReadFromJsonAsync<JsonElement>());
            var cache = Assert.Single(await store.ReadIncludingDeletedAsync(prefix + "_cached_global"), row => SqlRowStore.Id(row) == ids[shared ? 1 : role]); Assert.Equal(renamed[shared ? 1 : role], SqlRowStore.Text(cache, "lookup_name"));
            var renamedFull = string.Join("/", renamed.Take(shorter ? 2 : 3)); var renamedWarehouse = shared ? renamed[0] + "/" + renamed[1] + "/仓甲" : null;
            cells = await Export(); Assert.Equal((renamedFull, renamedWarehouse), cells["完整"]); before = await Snapshot(); Assert.Equal(500, (await Import(Input("旧名不得写入", full, shared ? "国甲/区甲/仓甲" : null))).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            Data(await Import(Input("改名后", renamedFull, renamedWarehouse))); await Sparse("改名后", shorter ? 1 : 2, shorter ? Area : Place, shared ? 2 : -1, shared ? Warehouse : null);
            if (shared)
            {
                before = await Snapshot(); Assert.Equal(500, (await Import(Input("共享冲突", first, renamedWarehouse))).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            }
            var duplicate = new Dictionary<string, object?>(cache) { ["id"] = ids[shared ? 1 : role] + 500 };
            await session.InTransactionAsync(async () => { await store.InsertAsync(prefix + "_cached_global", duplicate); return true; }); before = await Snapshot();
            Assert.Equal(500, (await Import(Input("重名不得猜选", renamedFull, renamedWarehouse))).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            Assert.Equal(500, (await Post("initExport", new { })).GetProperty("code").GetInt32());
            Assert.NotEmpty(trace); Assert.Contains(trace, sql => sql.Contains("lookup_name", StringComparison.Ordinal)); Assert.True(exportReadCount > 0); Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            await Receipt("sql-trace", new { nonce, exportTargetReadsInRepeatableRead = exportReadCount, commands = trace.ToArray() });
            Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog)); verified = true;
            await Receipt("verified", new { nonce, role, key, parent, allGlobal, shared, shorter, names, passed = true, realMySql = true, realGeneratedHttp = true, realXlsx = true, sparseStorage = true, originalHeaders = true, exportTargetReadsInRepeatableRead = exportReadCount, globalCacheRenamePropagation = true, partialDmlRollback = observer.ObservedPartialDml, formalAuthentication = false, browserExecuted = false, performanceExecuted = false, publicFullStillClosed = true });

            /// <summary>只调用原生成入口，错误响应保持失败，不将HTTP200或排队当成功。</summary>
            async Task<JsonElement> Post(string action, object body) { using var response = await client.PostAsJsonAsync(endpoint + action, body); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
            /// <summary>沿原中文表头和requestJson格式，来源保存不借用普通create/update权限。</summary>
            Task<JsonElement> Import(params Dictionary<string, object?>[] rows) => Post("importDatas30", new { requestJson = rows });
            /// <summary>短控件保留原地区末端，另一路径只在原共享控件存在时提交。</summary>
            Dictionary<string, object?> Input(string name, string? main, string? second) { var row = Row("名称", name, "区域", main); if (shared) row["仓库"] = second; return row; }
            /// <summary>真实下载结果保留精确字符串ID和原表头，依赖ID不得新增为用户列。</summary>
            async Task<Dictionary<string, (string? Main, string? Second)>> Export()
            {
                JsonElement prepared; Volatile.Write(ref exportActive, 1); try { prepared = await Post("initExport", new { }); } finally { Volatile.Write(ref exportActive, 0); }
                var ticket = Data(prepared).GetString()!; using var response = await client.GetAsync(ExportDownloadAccess.DownloadPath + "?exportId=" + Uri.EscapeDataString(ticket)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return ReadOriginalWorkbook(await response.Content.ReadAsByteArrayAsync(), shared, shorter);
            }
            /// <summary>独立连接核对真实物理位，共享字段取两条路径并集的最深节点。</summary>
            async Task Sparse(string name, int depth, long? selected, int secondDepth, long? second)
            {
                using var independent = new DatabaseSession(options); var row = Assert.Single(await new SqlRowStore(independent, catalog).ReadIncludingDeletedAsync(source), value => Equals(value["name"], name));
                // 原表头没有ID；精度在真实物理记录与各级键上验证，不能为测试向导出新增列。
                Assert.True(SqlRowStore.Id(row) > 9007199254740991);
                var expected = new Dictionary<string, long?> { ["country_id"] = null, ["area_id"] = null }; if (!shorter) expected["place_id"] = null; if (shared) expected["warehouse_id"] = null;
                var fields = shorter ? new[] { "country_id", "area_id" } : new[] { "country_id", "area_id", "place_id" }; if (depth >= 0) expected[fields[depth]] = selected;
                if (shared && secondDepth >= 0) { var secondary = new[] { "country_id", "area_id", "warehouse_id" }; expected[secondary[secondDepth]] = second; if (depth > secondDepth || secondDepth > depth) foreach (var ancestor in secondary.Take(Math.Max(depth, secondDepth))) expected[ancestor] = null; }
                foreach (var pair in expected) Assert.Equal(pair.Value?.ToString(CultureInfo.InvariantCulture), row[pair.Key] is null ? null : Convert.ToString(row[pair.Key], CultureInfo.InvariantCulture)); Assert.DoesNotContain(row.Keys, value => value.EndsWith("_name", StringComparison.Ordinal));
            }
        }
        catch (Exception error) { primary = error; await Receipt("failure", new { nonce, role, key, parent, allGlobal, shared, shorter, names, errorType = error.GetType().FullName, error.Message, created, verified = false }); throw; }
        finally
        {
            try
            {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            if (created)
            {
                // 只清本nonce表；删除前以另一连接再核对完整结构和数据摘要，失败保留现场。
                Assert.All(names, name => Assert.StartsWith(prefix + "_", name)); using var cleanup = new DatabaseSession(options); var inspection = new SchemaService(cleanup); Assert.Empty((await inspection.PreviewAsync(catalog)).Changes); var fingerprint = MySqlSchemaPlanner.Fingerprint(await inspection.InspectAsync(catalog)); var state = await Snapshot();
                using var readback = new DatabaseSession(options); Assert.Equal(fingerprint, MySqlSchemaPlanner.Fingerprint(await new SchemaService(readback).InspectAsync(catalog))); Assert.Equal(state, await Snapshot()); await Receipt("cleanup-before", new { nonce, names, fingerprint, rowsSha256 = state });
                foreach (var name in names) { await using var connection = cleanup.Provider.CreateConnection(options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE " + cleanup.Provider.QuoteIdentifier(name); await command.ExecuteNonQueryAsync(); }
                await using var independent = cleanup.Provider.CreateConnection(options); await independent.OpenAsync();
                foreach (var name in names) { await using var command = independent.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name"; var parameter = command.CreateParameter(); parameter.ParameterName = "name"; parameter.Value = name; command.Parameters.Add(parameter); Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)); }
                await Receipt("cleanup", new { nonce, names, verified, independentTableAbsenceChecked = true, remaining = 0 });
            }
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
            await Receipt("local-cleanup", new { nonce, temporaryDirectory = directory, allAbsent = !Directory.Exists(directory) });
            }
            catch (Exception cleanup) when (primary is not null) { throw new AggregateException("真实Excel业务失败且清理未完成，首因与清理异常同时保留。", primary, cleanup); }
        }
        /// <summary>仅保存本轮无凭据业务和SQL证据，不写连接或认证正文。</summary>
        Task Receipt(string name, object value) => File.WriteAllTextAsync(Path.Combine(evidence, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        /// <summary>每次取独立已提交连接下的全部自有表摘要，既验证整批回滚也保护清理对象。</summary>
        async Task<string> Snapshot() { using var independent = new DatabaseSession(options); var data = new SortedDictionary<string, object?>(StringComparer.Ordinal); foreach (var name in names) data[name] = (await new SqlRowStore(independent, catalog).ReadIncludingDeletedAsync(name)).OrderBy(row => SqlRowStore.Id(row)).ToArray(); return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data))); }
    }

    /// <summary>核对实际原导出列及顺序，不假定隐式读取ID会成为Excel列；也供保留的真实失败工作簿离线复验。</summary>
    internal static Dictionary<string, (string? Main, string? Second)> ReadOriginalWorkbook(byte[] bytes, bool shared, bool shorter)
    {
        using var stream = new MemoryStream(bytes); using var book = new XLWorkbook(stream); var sheet = book.Worksheet(1);
        var labels = sheet.Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray();
        string[] expected = shared ? shorter ? ["区域", "附件", "名称", "仓库"] : ["附件", "名称", "区域", "仓库"] : ["附件", "名称", "区域"];
        Assert.Equal(expected, labels);
        var headers = sheet.Row(1).CellsUsed().ToDictionary(cell => cell.GetString(), cell => cell.Address.ColumnNumber);
        var result = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            string? Cell(string label) => row.Cell(headers[label]).IsEmpty() ? null : row.Cell(headers[label]).GetString();
            result.Add(row.Cell(headers["名称"]).GetString(), (Cell("区域"), shared ? Cell("仓库") : null));
        }
        return result;
    }

    /// <summary>只重命名夹具表及控件实际表引用；columnFullName是外键角色路径，不能误当物理表名改写。</summary>
    internal static void Prefix(MetadataCatalog catalog, string prefix)
    {
        var map = catalog.ConfigTables.ToDictionary(table => table.TableName, table => prefix + "_" + table.TableName, StringComparer.Ordinal);
        string Text(string value) => map.GetValueOrDefault(value, value);
        // 重建独立节点，不能把仍属于原数组的节点再次挂回，避免测试重命名在连接实库前失败。
        JsonNode? Rewrite(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Rewrite(pair.Value)))),
            JsonArray array => new JsonArray(array.Select(Rewrite).ToArray()),
            JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(Text(text)),
            _ => node?.DeepClone()
        };
        foreach (var column in catalog.ConfigColumns) if (!string.IsNullOrEmpty(column.ControlDetail)) column.ControlDetail = Rewrite(JsonNode.Parse(column.ControlDetail))!.ToJsonString();
        foreach (var table in catalog.ConfigTables) table.TableName = map[table.TableName];
    }
    /// <summary>来源及父键按实际元数据类型写入，外部JSON仍是精确字符串ID。</summary>
    private static object? Key(string type, long? value) => value is null ? null : type == "string" ? value.Value.ToString(CultureInfo.InvariantCulture) : value;
    /// <summary>测试数据只使用明确列和值，重复键拒绝。</summary>
    private static Dictionary<string, object?> Row(params object?[] pairs) { var result = new Dictionary<string, object?>(); for (var i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i]!, pairs[i + 1]); return result; }
    /// <summary>不把错误响应当下载凭证或更新成功。</summary>
    private static JsonElement Data(JsonElement value) { Assert.True(value.GetProperty("code").GetInt32() == 200, value.GetRawText()); return value.GetProperty("data"); }

    /// <summary>测试固定身份只允许来源import/export和字典更新，目标list及来源普通写入仍拒绝；不是正式认证。</summary>
    private sealed class Authorization(IHttpContextAccessor accessor) : IRuntimeAuthorization
    {
        /// <summary>真实SQL专项采用服务器固定测试身份，不从请求选择项目或后台。</summary>
        public RuntimeActor RequireActor() => new(958, "全局缓存级联Excel实库", false, "100", "9");
        /// <summary>仅两个来源操作和明确字典更新可用，其它页面读取及普通来源写入全部拒绝。</summary>
        public void RequirePagePermission(string page, string operation) { var headers = accessor.HttpContext!.Request.Headers; if (page == "30" && operation is "import" or "export" && !headers.ContainsKey(operation == "import" ? "denyImport" : "denyExport")) return; if (page == "20" && operation == "update") return; throw new UnauthorizedAccessException("本轮固定身份没有其它页面动作许可。"); }
    }
    /// <summary>确认首行已真实写入当前事务后才在第二行通知阶段失败，不能用预校验代替事务回滚。</summary>
    private sealed class LateFailure(string source) : IRecordMutationObserver
    {
        internal bool ObservedPartialDml;
        /// <summary>同一真实事务中检查首行DML，再制造末段通知失败。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context, CancellationToken cancellationToken = default)
        {
            if (context.TableName != source || !context.SubmittedFields.TryGetValue("name", out var value) || !Equals(value, "后置拒绝")) return;
            await using var command = context.Transaction.Connection!.CreateCommand(); command.Transaction = context.Transaction; command.CommandText = "SELECT COUNT(*) FROM `" + source + "` WHERE name=@name"; var parameter = command.CreateParameter(); parameter.ParameterName = "name"; parameter.Value = "回滚首行"; command.Parameters.Add(parameter); Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture)); ObservedPartialDml = true; throw new InvalidOperationException("本轮真实DML后的通知故障。");
        }
    }
}
