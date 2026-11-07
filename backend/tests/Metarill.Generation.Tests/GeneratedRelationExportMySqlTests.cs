using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
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

namespace Metarill.Generation.Tests;

/// <summary>实际生成HTTP/Service与随机MySQL三表验证导出名称授权和双父隔离；投影接收器不冒充XLSX文件验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GeneratedRelationExportMySqlTests
{
    private const long Project = 9007199254740993L, Parent = 9007199254742001L, SourceParent = 9007199254743001L, Target = 9007199254744001L, Row = 9007199254745001L;
    private static readonly RuntimeActor Actor = new(859, "关系导出验收", false, Project.ToString(CultureInfo.InvariantCulture), "1");

    /// <summary>经真实生成HTTP读取名称，拒绝目标权限或任一真实父范围失败，清理仅限随机三表。</summary>
    [StandaloneExportMySqlFact]
    public async Task Relation_exports_require_target_permissions_and_read_names_only_within_each_real_target_parent()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("关系导出只允许明确的MySQL业务隔离库。");
        using var session = new DatabaseSession(options); var prefix = "mt_rel_export_" + Guid.NewGuid().ToString("N")[..12]; var catalog = Catalog(prefix);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-export"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        WebApplication? app = null; var installed = false;
        try
        {
            var schema = new SchemaService(session); Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var plan = await schema.PreviewAsync(catalog); Assert.Equal(3, plan.Changes.Count); installed = true; Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await Seed(session, prefix);
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.RelationExport");
            var generated = File.ReadAllText(Path.Combine(directory, "generated", "Services", Pascal(prefix + "_source") + "Service.g.cs"));
            Assert.Contains("OptionsScope201_25Async", generated); Assert.Contains("Dictionary<(string? Parent, long Id), string?>", generated);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
            builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(session); builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(859));
            builder.Services.AddScoped<IRuntimeAuthorization>(provider => new Authorization(provider.GetRequiredService<IHttpContextAccessor>()));
            var sink = new ProjectionSink(); builder.Services.AddSingleton<IPageExportService>(sink);
            assembly.GetType("Example.RelationExport.Generated.GeneratedRegistration", true)!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration => configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app = builder.Build(); app.Use(async (context, next) => { try { await next(); } catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException or InvalidOperationException) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message)); } }); app.MapControllers(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(15) };
            var route = "/api/admin/auto/" + prefix + "_source/201/initExport";
            var targetCommands = new List<string>(); session.Orm.Aop.CommandBefore += (_, args) => { if (args.Command.CommandText.Contains(prefix + "_target", StringComparison.Ordinal)) targetCommands.Add(args.Command.CommandText); };
            Data(await Post(client, route, new { parentId = SourceParent.ToString() }));
            var projection = Assert.IsType<PageExportRequest>(sink.Last); Assert.Equal(new[] { "记录", "关联名称" }, projection.Headers);
            var values = projection.Rows.ToDictionary(row => Assert.IsType<string>(row[0]), row => row[1]);
            Assert.Equal("父一合法名称", values["记录A"]); Assert.Equal("父二合法名称", values["记录B"]);
            Assert.Equal(Target.ToString(), values["记录C"]); // 同ID在父二范围不可借父一名称。
            Assert.Equal((Target + 2).ToString(), values["记录D"]); Assert.Equal((Target + 3).ToString(), values["记录E"]); Assert.Equal((Target + 99).ToString(), values["记录F"]);
            Assert.Equal(6, values.Count); Assert.DoesNotContain(values.Values, value => Equals(value, "外项目不可读") || Equals(value, "已删除不可读"));
            Assert.NotEmpty(targetCommands); Assert.All(targetCommands, command => { Assert.DoesNotContain("private_payload", command, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("SELECT *", command, StringComparison.OrdinalIgnoreCase); });
            // 目标权限在进入导出准备前检查；不能只拥有来源export就借名称读取目标。
            foreach (var page in new[] { "201", "301" })
            {
                var attempts = sink.Attempts; var issued = sink.Issued; targetCommands.Clear(); client.DefaultRequestHeaders.Add("denyPage", page);
                Assert.Equal(500, (await Post(client, route, new { parentId = SourceParent.ToString() })).GetProperty("code").GetInt32());
                Assert.Equal(attempts, sink.Attempts); Assert.Equal(issued, sink.Issued); Assert.Empty(targetCommands); client.DefaultRequestHeaders.Remove("denyPage");
            }
            // 相同真实父合同仍按原parentId；独立父读取绝不使用来源根ID替代。
            Data(await Post(client, route.Replace("/201/", "/202/", StringComparison.Ordinal), new { parentId = Parent.ToString() }));
            Assert.Equal("父一合法名称", sink.Last!.Rows.Single(row => Equals(row[0], "记录A"))[1]); Assert.DoesNotContain(sink.Last.Rows, row => Equals(row[0], "记录B"));
            foreach (var invalid in new[] { Parent + 2, Parent + 3 })
            {
                var issued = sink.Issued; await Execute(session, "UPDATE " + Q(session, prefix + "_source") + " SET parent_id=@parent WHERE id=@id", ("parent", invalid), ("id", Row));
                Assert.Equal(500, (await Post(client, route, new { parentId = SourceParent.ToString() })).GetProperty("code").GetInt32()); Assert.Equal(issued, sink.Issued);
            }
            await Execute(session, "UPDATE " + Q(session, prefix + "_source") + " SET parent_id=@parent WHERE id=@id", ("parent", Parent), ("id", Row));
            foreach (var invalid in new[] { SourceParent + 1, SourceParent + 2, 42L })
                Assert.Equal(500, (await Post(client, route, new { parentId = invalid.ToString() })).GetProperty("code").GetInt32());
            // 隐藏/多义目标父可继续生成页面，但有名称可导出时明确拒绝，不扩大目标读取。
            foreach (var page in new[] { "203", "204" })
            {
                var issued = sink.Issued; targetCommands.Clear(); var denied = await Post(client, route.Replace("/201/", "/" + page + "/", StringComparison.Ordinal), new { parentId = SourceParent.ToString() });
                Assert.Equal(500, denied.GetProperty("code").GetInt32()); Assert.Contains("目标父范围", denied.GetProperty("msg").GetString()); Assert.Equal(issued, sink.Issued); Assert.Empty(targetCommands);
            }
            using (var scope = app.Services.CreateScope())
            {
                var serviceType = assembly.GetType("Example.RelationExport.Generated.Services." + Pascal(prefix + "_source") + "Service", true)!;
                var service = scope.ServiceProvider.GetRequiredService(serviceType); var method = serviceType.GetMethod("Export201Async")!;
                var mismatched = (Task)method.Invoke(service, [new PageRequest { ParentId = SourceParent.ToString() }, Actor with { AccountId = 1 }, 100, CancellationToken.None])!;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mismatched);
                // 宿主漏注入关系授权时失败，不用空授权或测试跳过掩盖新的Service依赖。
                var mapper = scope.ServiceProvider.GetRequiredService(assembly.GetType("Example.RelationExport.Generated.Mappers." + Pascal(prefix + "_source") + "Mapper", true)!);
                var unbound = Activator.CreateInstance(serviceType, [session, mapper, new SnowflakeIdGenerator(860), Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(), null, null])!;
                var missing = (Task)method.Invoke(unbound, [new PageRequest { ParentId = SourceParent.ToString() }, Actor, 100, CancellationToken.None])!; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => missing);
            }
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

    /// <summary>固定父/目标定义覆盖独立父、同父和无法证明的投影，均不提供请求自选页面。</summary>
    private static MetadataCatalog Catalog(string prefix)
    {
        var catalog = RelationOptionsContractTests.Catalog(prefix); var page = catalog.ConfigPages.Single(page => page.Id == "201"); page.WhereSql = "yz_project_id=@current_yz_project_id AND source_owner_id=@parentId"; page.IsExport = 1; page.OrderBy = "name asc";
        catalog.ConfigColumns.Single(column => column.Id == "22").Name = "记录"; catalog.ConfigColumns.Single(column => column.Id == "25").Name = "关联名称";
        catalog.ConfigColumns.AddRange([new() { Id = "28", ConfigTableId = "2", ColumnName = "source_owner_id", LogicalType = "int64", LinkConfigTableId = "2" }, new() { Id = "29", ConfigTableId = "2", ColumnName = "another_parent_id", LogicalType = "int64", LinkConfigTableId = "1" }, new() { Id = "39", ConfigTableId = "3", ColumnName = "private_payload", LogicalType = "string" }]);
        catalog.ConfigPageColumns.Add(new() { Id = "20126", ConfigPageId = "201", ConfigColumnId = "26", Extra = new() { ["isInGrid"] = JsonSerializer.SerializeToElement(0) } });
        foreach (var id in new[] { "202", "203", "204" })
        {
            var clone = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!; clone.Id = id; if (id == "202") clone.WhereSql = "yz_project_id=@current_yz_project_id AND parent_id=@parentId"; catalog.ConfigPages.Add(clone);
            foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == "201" && (id != "203" || field.ConfigColumnId != "26")).ToArray()) { var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!; copy.Id = id + "_" + field.ConfigColumnId; copy.ConfigPageId = id; catalog.ConfigPageColumns.Add(copy); }
            if (id == "204") catalog.ConfigPageColumns.Add(new() { Id = "20429", ConfigPageId = id, ConfigColumnId = "29", Extra = new() { ["isInGrid"] = JsonSerializer.SerializeToElement(0) } });
        }
        return catalog;
    }

    /// <summary>参数化预置明确fixture，不通过未授权的旧服务或现有客户表构造数据。</summary>
    private static async Task Seed(DatabaseSession session, string prefix)
    {
        await Execute(session, "INSERT INTO " + Q(session, prefix + "_parent") + " (id,name,yz_project_id,delete_no) VALUES (@one,'父一',@project,NULL),(@two,'父二',@project,NULL),(@foreign,'外项目父',@other,NULL),(@deleted,'已删父',@project,@now)", ("one", Parent), ("two", Parent + 1), ("foreign", Parent + 2), ("deleted", Parent + 3), ("project", Project), ("other", Project + 1), ("now", DateTime.UtcNow));
        await Execute(session, "INSERT INTO " + Q(session, prefix + "_source") + " (id,name,yz_project_id,delete_no) VALUES (@root,'来源根',@project,NULL),(@foreign,'外项目来源根',@other,NULL),(@deleted,'已删来源根',@project,@now)", ("root", SourceParent), ("foreign", SourceParent + 1), ("deleted", SourceParent + 2), ("project", Project), ("other", Project + 1), ("now", DateTime.UtcNow));
        await Execute(session, "INSERT INTO " + Q(session, prefix + "_target") + " (id,name,yz_project_id,parent_id,private_payload,delete_no) VALUES (@one,'父一合法名称',@project,@parent,'内部不可投影',NULL),(@two,'父二合法名称',@project,@second,'内部不可投影',NULL),(@foreign,'外项目不可读',@other,@parent,'外项目秘密',NULL),(@deleted,'已删除不可读',@project,@parent,'已删秘密',@now)", ("one", Target), ("two", Target + 1), ("foreign", Target + 2), ("deleted", Target + 3), ("project", Project), ("other", Project + 1), ("parent", Parent), ("second", Parent + 1), ("now", DateTime.UtcNow));
        foreach (var (offset, parent, target) in new[] { (0, Parent, Target), (1, Parent + 1, Target + 1), (2, Parent + 1, Target), (3, Parent, Target + 2), (4, Parent, Target + 3), (5, Parent, Target + 99) })
            await Execute(session, "INSERT INTO " + Q(session, prefix + "_source") + " (id,name,yz_project_id,source_owner_id,parent_id,target_id) VALUES (@id,@name,@project,@root,@parent,@target)", ("id", Row + offset), ("name", "记录" + (char)('A' + offset)), ("project", Project), ("root", SourceParent), ("parent", parent), ("target", target));
    }
    private sealed class Authorization(IHttpContextAccessor accessor) : IRuntimeAuthorization
    {
        /// <summary>身份固定由测试宿主建立，不接受客户端选择。</summary>
        public RuntimeActor RequireActor() => Actor;
        /// <summary>通过请求头模拟真实权限撤销，保留固定页和操作校验入口。</summary>
        public void RequirePagePermission(string pageId, string operation) { if (accessor.HttpContext?.Request.Headers["denyPage"].ToString() == pageId) throw new UnauthorizedAccessException("无此页面操作权限。"); }
    }
    /// <summary>只接收真实生成Service读出的有界投影；发行计数必须等全部范围校验成功后递增。</summary>
    private sealed class ProjectionSink : IPageExportService
    {
        public int Attempts { get; private set; } public int Issued { get; private set; } public PageExportRequest? Last { get; private set; }
        /// <summary>原页面不得误用普通账号票据绕过显式凭证发行合同。</summary>
        public Task<string> CreateAsync(PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default) => throw new InvalidOperationException("原页面须明确发行短时下载凭证。");
        /// <summary>执行真实Service有界准备，只有全范围通过后记录发行成功。</summary>
        public async Task<string> CreateDownloadAsync(RuntimeActor actor, Func<int, CancellationToken, Task<PageExportRequest>> prepare, CancellationToken cancellationToken = default) { Attempts++; Last = await prepare(100, cancellationToken); Issued++; return new string('a', 64); }
    }
    /// <summary>测试程序集类型名使用与生成器一致的固定表名转换。</summary>
    private static string Pascal(string value) => string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    /// <summary>物理标识符只来自本轮随机元数据。</summary>
    private static string Q(DatabaseSession session, string table) => session.Provider.QuoteIdentifier(table);
    /// <summary>发送真实原导出请求，不绕过Controller授权。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string route, object request) => JsonSerializer.Deserialize<JsonElement>(await (await client.PostAsJsonAsync(route, request)).Content.ReadAsStringAsync());
    /// <summary>仅接受明确的统一成功回执。</summary>
    private static JsonElement Data(JsonElement response) { Assert.True(response.GetProperty("code").GetInt32() == 200, response.GetRawText()); return response.GetProperty("data"); }
    /// <summary>只执行本fixture固定的随机表标识，所有数据值均作为参数。</summary>
    private static async Task Execute(DatabaseSession session, string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter); } await command.ExecuteNonQueryAsync();
    }
}
