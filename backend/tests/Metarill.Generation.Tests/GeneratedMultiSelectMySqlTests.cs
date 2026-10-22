using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际生成HTTP/Service与真实文件观察者共用MySQL事务，不把前端回调当持久化校验。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GeneratedMultiSelectMySqlTests
{
    private const long Project = 9007199254740993, Parent = 9007199254742001, First = 9007199254750000;

    [StandaloneExportMySqlFact]
    public Task Source_transaction_derives_names_and_rejects_every_partial_scope_or_permission_failure_without_file_changes()=>VerifyAsync(false);
    /// <summary>原多选在明确物理联接缓存上复验文件引用、整个来源事务与复制保护。</summary>
    [StandaloneExportMySqlFact]
    public Task Joined_name_cache_uses_original_popup_scope_and_full_source_transaction()=>VerifyAsync(true);
    /// <summary>自动缓存不依赖显式页面名称字段，仍验证整个来源事务及文件引用。</summary>
    [StandaloneExportMySqlFact]
    public Task Automatic_name_cache_uses_original_popup_and_transaction()=>VerifyAsync(true,true);
    /// <summary>普通及联接缓存复用相同原协议验收，实际样本分别隔离。</summary>
    private static async Task VerifyAsync(bool joinedCache,bool automatic=false)
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("多选验收只允许明确的隔离MySQL业务测试库。");
        if(joinedCache)
        {
            using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("联接缓存验收缺少实例pin。")));
            await using var connection=new MySqlConnector.MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不匹配，未创建联接样本。");
        }
        using var session = new DatabaseSession(options); var prefix = "mt_multi_" + Guid.NewGuid().ToString("N")[..12]; var catalog = GeneratedMultiSelectContractTests.Catalog(prefix);
        if(joinedCache){var cache=catalog.ConfigColumns.Single(column=>column.Id=="27");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.name");}
        var page = catalog.ConfigPages.Single(page => page.Id == "201"); page.WhereSql = "yz_project_id=@current_yz_project_id"; page.IsCopy = 1;
        catalog.ConfigPageColumns.Add(new() { Id = "2004", ConfigPageId = "201", ConfigColumnId = "26", IsShow = 1, IsEdit = 1 });
        catalog.ConfigColumns.Add(new() { Id = "29", ConfigTableId = "2", ColumnName = "attachments", LogicalType = "string", ColumnLength = "max", ControlKind = "FileLibrary", ControlDetail = "{\"accessMode\":\"PROTECTED\",\"maxCount\":3}" });
        catalog.ConfigPageColumns.Add(new() { Id = "2005", ConfigPageId = "201", ConfigColumnId = "29", IsShow = 1, IsEdit = 1 });
        // 同表隐藏IDs页仍可保存其它字段；内部复制和父依赖变化不能绕过真实关系规则。
        var projectionPage = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!; projectionPage.Id = "202"; catalog.ConfigPages.Add(projectionPage);
        foreach (var column in new[] { "22", "26", "27", "29" }) catalog.ConfigPageColumns.Add(new() { Id = "projection_" + column, ConfigPageId = "202", ConfigColumnId = column, IsShow = 1, IsEdit = column == "27" ? 0 : 1 });
        // nameField=id只需读取原long主键，不能要求数据库翻译带CultureInfo的ToString。
        var idNamePage = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!; idNamePage.Id = "203"; catalog.ConfigPages.Add(idNamePage);
        catalog.ConfigPageColumns.Add(new() { Id = "id_name_22", ConfigPageId = "203", ConfigColumnId = "22", IsShow = 1, IsEdit = 1 });
        catalog.ConfigPageColumns.Add(new() { Id = "id_name_25", ConfigPageId = "203", ConfigColumnId = "25", IsShow = 1, IsEdit = 1, Extra = new() { ["controlDetail"] = JsonSerializer.SerializeToElement("{\"nameField\":\"id\"}") } });
        catalog.ConfigPageColumns.Add(new() { Id = "id_name_26", ConfigPageId = "203", ConfigColumnId = "26", IsShow = 1, IsEdit = 1 });
        catalog.ConfigPageColumns.Add(new() { Id = "id_name_29", ConfigPageId = "203", ConfigColumnId = "29", IsShow = 1, IsEdit = 1 });
        if(automatic)RedundantMultiNameCacheTests.RemoveCache(catalog);
        var filesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../metadata/files.json"));
        if (!File.Exists(filesPath)) filesPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "backend/metadata/files.json"));
        var files = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(filesPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-mysql"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var ids = new SnowflakeIdGenerator(859); var fileIds = new[] { ids.NextId(), ids.NextId() }; WebApplication? app = null; var cleanup = false;
        var receiptDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filesPath)!, "../../.local/multi-field-evidence/fixtures")); Directory.CreateDirectory(receiptDirectory);
        var receiptPath = Path.Combine(receiptDirectory, prefix + ".json");
        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(new { Prefix = prefix, FileIds = fileIds.Select(id => id.ToString()).ToArray(), Tables = catalog.ConfigTables.Select(table => table.TableName).ToArray(), TemporaryDirectory = directory, StartedUtc = DateTimeOffset.UtcNow }));
        try
        {
            var schema = new SchemaService(session); Assert.Empty((await schema.InspectAsync(catalog)).Tables); var plan = await schema.PreviewAsync(catalog); Assert.Equal(3, plan.Changes.Count); cleanup = true; Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            // 固定文件基础表只复用已初始化结构，绝不在本测试自动改动它们。
            Assert.Empty((await schema.PreviewAsync(files)).Changes);
            var store = new SqlRowStore(session, files); var rows = new SqlRowStore(session, catalog);
            foreach (var id in fileIds) await store.InsertAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = id, ["name"] = prefix + ".txt", ["content_type"] = "text/plain", ["size"] = 1L, ["storage_key"] = Guid.NewGuid().ToString("N") + ".blob", ["access_mode"] = "PROTECTED", ["status"] = "ready", ["owner_id"] = 859L, ["yz_project_id"] = Project });
            await Execute(session, "INSERT INTO " + Q(session, prefix + "_parent") + " (id,name,yz_project_id) VALUES (@p,'父甲',@project),(@other,'父乙',@project),(@foreign,'外项目父',@foreignProject)", ("p", Parent), ("other", Parent + 1), ("foreign", Parent + 2), ("project", Project), ("foreignProject", Project + 1));
            await Execute(session, "INSERT INTO " + Q(session, prefix + "_target") + " (id,name,parent_id,yz_project_id,delete_no) VALUES (@one,'目标甲',@p,@project,NULL),(@two,'目标乙',@p,@project,NULL),(@other,'另一父目标',@otherParent,@project,NULL),(@foreign,'外项目目标',@p,@foreignProject,NULL),(@deleted,'已删目标',@p,@project,@now)", ("one", First), ("two", First + 1), ("other", First + 2), ("foreign", First + 3), ("deleted", First + 4), ("p", Parent), ("otherParent", Parent + 1), ("project", Project), ("foreignProject", Project + 1), ("now", DateTime.UtcNow));
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.MultiProbe");
            var sourceType=GenerationModel.Pascal(prefix+"_source");
            if(joinedCache)await File.WriteAllTextAsync(Path.Combine(directory,"generated/Services/"+sourceType+".CacheProbe.cs"),$$"""
using Metarill.Core.Security;
using Example.MultiProbe.Generated.Entities;
namespace Example.MultiProbe.Generated.Services;
/// <summary>仅本次临时验收的手写partial故障探针，不进入正式模板。</summary>
public partial class {{sourceType}}Service
{
    public static int JoinedProbeMode {get;set;}
    partial void BeforeSave({{sourceType}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(creating)return;
        if(JoinedProbeMode is 1 or 2){entity.TargetNames="[\"不能持久化的名称\"]";if(JoinedProbeMode==2)submittedFields.Add("target_names");}
        if(JoinedProbeMode==3)entity.ParentId={{Parent+1}}L;
    }
}
""");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
            builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(session); builder.Services.AddSingleton<IIdGenerator>(ids);
            builder.Services.AddScoped<IRuntimeAuthorization>(provider => new Authorization(provider.GetRequiredService<IHttpContextAccessor>()));
            builder.Services.AddScoped<IRecordMutationObserver>(provider => new FileReferenceMutationObserver(store, new([catalog]), provider.GetRequiredService<IRuntimeAuthorization>(), ids));
            assembly.GetType("Example.MultiProbe.Generated.GeneratedRegistration", true)!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration => configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app = builder.Build(); app.Use(async (context, next) => { try { await next(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message)); } }); app.MapControllers(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(15) }; var endpoint = "/api/admin/auto/" + prefix + "_source/201";
            var selected = new[] { (First + 1).ToString(), First.ToString() };
            var created = Data(await Post(client, endpoint + "/add", new { name = "源记录", parentId = Parent.ToString(), targetIds = selected, attachments = new[] { fileIds[0].ToString() } })); var record = created.GetProperty("id").GetString()!;
            Assert.Equal(selected, created.GetProperty("targetIds").EnumerateArray().Select(value => value.GetString())); Assert.Equal(new[] { "目标乙", "目标甲" }, created.GetProperty("targetNames").EnumerateArray().Select(value => value.GetString()));
            Assert.Single(await store.ReadAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = long.Parse(record) }));
            async Task<string> Snapshot() => JsonSerializer.Serialize(new { Source = await rows.ReadIncludingDeletedAsync(prefix + "_source", new Dictionary<string, object?> { ["id"] = long.Parse(record) }), Refs = (await store.ReadIncludingDeletedAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = long.Parse(record) })).OrderBy(row => Convert.ToInt64(row["id"])).ToArray() });
            if(joinedCache)
            {
                var mode=assembly.GetType("Example.MultiProbe.Generated.Services."+sourceType+"Service",true)!.GetProperty("JoinedProbeMode")!;
                try
                {
                    var original=await Snapshot();mode.SetValue(null,1);Assert.Equal(500,(await Post(client,endpoint+"/update",new{id=record,name="未登记缓存不保存"})).GetProperty("code").GetInt32());Assert.Equal(original,await Snapshot());
                    mode.SetValue(null,2);var corrected=Data(await Post(client,endpoint+"/update",new{id=record}));Assert.Equal(new[]{"目标乙","目标甲"},corrected.GetProperty("targetNames").EnumerateArray().Select(item=>item.GetString()));
                    original=await Snapshot();mode.SetValue(null,3);Assert.Equal(500,(await Post(client,endpoint+"/update",new{id=record,targetIds=new[]{(First+2).ToString()}})).GetProperty("code").GetInt32());Assert.Equal(original,await Snapshot());
                }
                finally{mode.SetValue(null,0);}
            }
            var hiddenEndpoint = "/api/admin/auto/" + prefix + "_source/202";
            var before = await Snapshot(); Data(await Post(client, endpoint + "/update", new { id = record, targetNames = new[] { "客户端伪造" } })); Assert.Equal(before, await Snapshot());
            // 原详情整实体回传辅助名称，HTTP只剥离回显；IDs仍由同一源事务重新验证并派生名称。
            var echoed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(created.GetRawText())!; echoed["targetNames"] = JsonSerializer.SerializeToElement(new[] { "不能写入的伪名称" });
            Assert.Equal(new[] { "目标乙", "目标甲" }, Data(await Post(client, endpoint + "/update", echoed)).GetProperty("targetNames").EnumerateArray().Select(value => value.GetString()));
            // 省略IDs不刷新历史名称；客户端无法只写辅助名称。
            await Execute(session, "UPDATE " + Q(session, prefix + "_source") + " SET target_names=@names WHERE id=@id", ("names", "[\"历史名称\"]"), ("id", long.Parse(record)));
            Data(await Post(client, endpoint + "/update", new { id = record, name = "只改来源名称" })); Assert.Equal("[\"历史名称\"]", (await rows.ReadAsync(prefix + "_source", new Dictionary<string, object?> { ["id"] = long.Parse(record) })).Single()["target_names"]);
            var hidden = Data(await Post(client, hiddenEndpoint + "/update", new { id = record, name = "隐藏ID页普通保存" })); Assert.False(hidden.TryGetProperty("targetIds", out _));if(automatic)Assert.False(hidden.TryGetProperty("targetNames",out _));
            Assert.Equal("[\"历史名称\"]", (await rows.ReadAsync(prefix + "_source", new Dictionary<string, object?> { ["id"] = long.Parse(record) })).Single()["target_names"]);
            before = await Snapshot(); Assert.Equal(500, (await Post(client, hiddenEndpoint + "/update", new { id = record, parentId = (Parent + 1).ToString(), attachments = new[] { fileIds[1].ToString() } })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            var encoded = " [ \"" + First + "\" ] "; var legacy = Data(await Post(client, endpoint + "/update", new { id = record, targetIds = encoded })); Assert.Equal(JsonValueKind.Array, legacy.GetProperty("targetIds").ValueKind); Assert.Equal(encoded, (await rows.ReadAsync(prefix + "_source", new Dictionary<string, object?> { ["id"] = long.Parse(record) })).Single()["target_ids"]);
            var cleared = Data(await Post(client, endpoint + "/update", new { id = record, targetIds = (object?)null })); Assert.Equal(JsonValueKind.Null, cleared.GetProperty("targetIds").ValueKind); Assert.Equal(JsonValueKind.Null, cleared.GetProperty("targetNames").ValueKind);
            var empty = Data(await Post(client, endpoint + "/update", new { id = record, targetIds = Array.Empty<string>() })); Assert.Empty(empty.GetProperty("targetNames").EnumerateArray());
            Data(await Post(client, endpoint + "/update", new { id = record, targetIds = selected })); before = await Snapshot();
            foreach (var invalid in new object[] { new[] { First.ToString(), First.ToString() }, new[] { "9223372036854775808" }, new[] { "-1" }, new[] { "0" + First }, new[] { "1 OR 1=1" }, new[] { 1 }, "[", "{}", Enumerable.Repeat(First.ToString(), 201).ToArray() })
            { Assert.Equal(500, (await Post(client, endpoint + "/update", new { id = record, targetIds = invalid, attachments = new[] { fileIds[1].ToString() } })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot()); }
            foreach (var invalid in new[] { First + 2, First + 3, First + 4, First + 9999 })
            { Assert.Equal(500, (await Post(client, endpoint + "/update", new { id = record, targetIds = new[] { First.ToString(), invalid.ToString() }, attachments = new[] { fileIds[1].ToString() } })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot()); }
            Assert.Equal(500, (await Post(client, endpoint + "/update", new { id = record, parentId = (Parent + 1).ToString() })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            // 即使直接调用来源add/save/copy，也不得绕过真实目标页list权限。
            client.DefaultRequestHeaders.Add("denyTarget", "1");
            Assert.Equal(500, (await Post(client, endpoint + "/add", new { name = "越权新增", parentId = Parent.ToString(), targetIds = selected })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, endpoint + "/update", new { id = record, targetIds = selected, attachments = new[] { fileIds[1].ToString() } })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, endpoint + "/copy/" + record, new { })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            Assert.Equal(500, (await Post(client, hiddenEndpoint + "/copy/" + record, new { })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
            Data(await Post(client, hiddenEndpoint + "/update", new { id = record, name = "隐藏ID页省略关系仍可保存" }));
            Data(await Post(client, endpoint + "/update", new { id = record, name = "省略IDs仍可改其它字段" })); client.DefaultRequestHeaders.Remove("denyTarget");
            await Execute(session, "UPDATE " + Q(session, prefix + "_target") + " SET name=@name WHERE id=@id", ("name", "服务端新名称"), ("id", First));
            var fresh = Data(await Post(client, endpoint + "/update", new { id = record, targetIds = selected })); Assert.Equal(new[] { "目标乙", "服务端新名称" }, fresh.GetProperty("targetNames").EnumerateArray().Select(value => value.GetString()));
            var idNameEndpoint = "/api/admin/auto/" + prefix + "_source/203";
            var byId = Data(await Post(client, idNameEndpoint + "/add", new { name = "主键作为名称", parentId = Parent.ToString(), targetIds = new[] { First.ToString() } }));
            Assert.Equal(First.ToString(), Assert.Single(byId.GetProperty("targetIds").EnumerateArray()).GetString());
            var byIdUpdated = Data(await Post(client, idNameEndpoint + "/update", new { id = byId.GetProperty("id").GetString(), targetIds = new[] { (First + 1).ToString() } }));
            Assert.Equal((First + 1).ToString(), Assert.Single(byIdUpdated.GetProperty("targetIds").EnumerateArray()).GetString());
            var copied = Data(await Post(client, endpoint + "/copy/" + record, new { })); Assert.NotEqual(record, copied.GetProperty("id").GetString()); Assert.Single(await store.ReadAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = long.Parse(copied.GetProperty("id").GetString()!) }));
            await Execute(session, "UPDATE " + Q(session, prefix + "_target") + " SET delete_no=@now WHERE id=@id", ("now", DateTime.UtcNow), ("id", First)); before = await Snapshot(); Assert.Equal(500, (await Post(client, endpoint + "/copy/" + record, new { })).GetProperty("code").GetInt32()); Assert.Equal(before, await Snapshot());
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            if (cleanup) using (var final = new DatabaseSession(options))
            {
                foreach (var id in fileIds) { await Execute(final, "DELETE FROM yz_file_ref WHERE file_id=@id", ("id", id)); await Execute(final, "DELETE FROM yz_file_library WHERE id=@id", ("id", id)); }
                foreach (var table in catalog.ConfigTables) { Assert.StartsWith(prefix + "_", table.TableName); await Execute(final, "DROP TABLE IF EXISTS " + Q(final, table.TableName)); }
            }
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>身份来自真实宿主注入；只用拒绝头模拟权限失效，不允许客户端选择Actor或项目。</summary>
    private sealed class Authorization(IHttpContextAccessor accessor) : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => new(859, "多选真实验收", false, Project.ToString(), "1");
        public void RequirePagePermission(string pageId, string operation) { if (pageId == "301" && accessor.HttpContext!.Request.Headers.ContainsKey("denyTarget")) throw new UnauthorizedAccessException("没有目标页读取权限。"); }
    }
    /// <summary>固定元数据标识符由provider引用。</summary>
    private static string Q(DatabaseSession session, string name) => session.Provider.QuoteIdentifier(name);
    /// <summary>业务值使用真实数据库参数，不进入SQL结构。</summary>
    private static async Task Execute(DatabaseSession session, string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter); }
        await command.ExecuteNonQueryAsync();
    }
    /// <summary>HTTP仍通过实际生成控制器和Service，不直接反射调用私有规则。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string path, object body) => JsonSerializer.Deserialize<JsonElement>(await (await client.PostAsJsonAsync(path, body)).Content.ReadAsStringAsync());
    /// <summary>成功数据必须来自真实统一响应。</summary>
    private static JsonElement Data(JsonElement response) { Assert.True(response.GetProperty("code").GetInt32() == 200, response.GetRawText()); return response.GetProperty("data"); }
}
