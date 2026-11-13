using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>原接口建立真实项目/后台/配置，实际同库活动和墓碑归属都不能被另一个项目认领。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_same_database_foreign_active_and_tombstone_are_reserved()
    {
        await using var source = await ImportCase.Create(1, factory => { factory.UseKestrel(0); return factory; }); await using var other = await ForeignImportCase.Create(source);
        var before = await source.Fingerprint();
        var saved = Data(await Post(other.Other, ForeignImportCase.TableRoute + "/add", new { name = "跨项目归属验收", tableName = source.Names[0], yzAdminTypeId = other.AdminId, isRongyu = 1 })); var id = saved.GetProperty("id").GetString()!;
        // 父宿主没有外项目的可信连接时必须拒绝，不能使用项目表中的显示连接描述或猜测同库。
        var unbound = await Post(source.Client, ImportRoute + "prepare", new { tableNames = source.Names }); Assert.Equal(500, unbound.GetProperty("code").GetInt32()); Assert.Contains("项目尚未配置 Metarill:ProjectDatabases", unbound.ToString());
        foreach (var deleted in new[] { false, true })
        {
            if (deleted) await other.DeleteOwnedTables();
            var response = await Post(other.Demo, ImportRoute + "prepare", new { tableNames = source.Names }); Assert.Equal(500, response.GetProperty("code").GetInt32()); Assert.Contains("该物理表已有项目配置或删除墓碑", response.ToString());
            Assert.Empty(await source.Store.ReadIncludingDeletedAsync("config_table", new Dictionary<string,object?> { ["table_name"] = source.Names[0], ["yz_project_id"] = long.Parse(Project) }));
            var row = Assert.Single(await source.Store.ReadIncludingDeletedAsync("config_table", new Dictionary<string,object?> { ["id"] = long.Parse(id) })); Assert.Equal(other.ProjectId, SqlRowStore.Text(row, "yz_project_id")); Assert.Equal(deleted, row["delete_no"] is not null);
        }
        Assert.Equal(before, await source.Fingerprint()); await source.AssertSourceRows(); source.Record(new { result = "same-physical-foreign-active-and-tombstone", project = other.ProjectId, sourceUnchanged = true });
    }

    /// <summary>两项目先各自审核，再真实并发确认，同一物理表只能出现一套配置，失败项目不得留下部分字段/索引。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_two_projects_cannot_concurrently_claim_same_physical_table()
    {
        await using var source = await ImportCase.Create(1, factory => { factory.UseKestrel(0); return factory; }); await using var other = await ForeignImportCase.Create(source); var before = await source.Fingerprint();
        async Task<JsonElement> Prepare(HttpClient client) => Data(await Post(client, ImportRoute + "prepare", new { tableNames = source.Names }));
        var beforeProjects = new Dictionary<string,string> { [Project] = await ImportProjectConfigurationState(source.Store, Project), [other.ProjectId] = await ImportProjectConfigurationState(source.Store, other.ProjectId) };
        var first = await Prepare(other.Demo); var second = await Prepare(other.Other);
        object Proof(JsonElement plan) => new { planId = plan.GetProperty("planId").GetString(), confirmSha256 = plan.GetProperty("confirmSha256").GetString() };
        var results = await Task.WhenAll(Post(other.Demo, ImportRoute + "apply", Proof(first)), Post(other.Other, ImportRoute + "apply", Proof(second)));
        Assert.Single(results, response => response.GetProperty("code").GetInt32() == 200); var rejected = Assert.Single(results, response => response.GetProperty("code").GetInt32() == 500); Assert.Contains("该物理表已有项目配置或删除墓碑", rejected.ToString());
        var winner = Assert.Single(await source.Store.ReadAsync("config_table", new Dictionary<string,object?> { ["table_name"] = source.Names[0] })); var winnerId = SqlRowStore.Id(winner); var winnerProject = SqlRowStore.Text(winner, "yz_project_id"); Assert.Contains(winnerProject, new[] { Project, other.ProjectId });
        var columns = await source.Store.ReadAsync("config_column", new Dictionary<string,object?> { ["config_table_id"] = winnerId }); Assert.Equal(3, columns.Count); Assert.All(columns, row => Assert.Equal(winnerProject, SqlRowStore.Text(row, "yz_project_id")));
        Assert.Single(await source.Store.ReadAsync("config_index", new Dictionary<string,object?> { ["config_table_id"] = winnerId })); Assert.Empty(await source.Store.ReadAsync("config_page", new Dictionary<string,object?> { ["config_table_id"] = winnerId }));
        var losingProject = winnerProject == Project ? other.ProjectId : Project; Assert.Empty(await source.Store.ReadIncludingDeletedAsync("config_table", new Dictionary<string,object?> { ["table_name"] = source.Names[0], ["yz_project_id"] = long.Parse(losingProject) }));
        Assert.Equal(beforeProjects[losingProject], await ImportProjectConfigurationState(source.Store, losingProject));
        foreach (var (client, plan, saved) in new[] { (other.Demo, first, winnerProject == Project), (other.Other, second, winnerProject == other.ProjectId) })
        { var status = Data(await Post(client, ImportRoute + "status", Proof(plan))); Assert.Equal(saved ? "saved" : "unconfirmed", status.GetProperty("state").GetString()); if (!saved) Assert.Equal(JsonValueKind.Null, status.GetProperty("configurationSaved").ValueKind); }
        Assert.Equal(before, await source.Fingerprint()); await source.AssertSourceRows(); source.Record(new { result = "same-physical-concurrent-claim", winnerProject, losingProject, activeConfigurations = 1 });
    }

    /// <summary>整套配置按真实ID/字段名规范化后取摘要，证明失败方没有残留孤立字段/索引，不输出配置正文。</summary>
    private static async Task<string> ImportProjectConfigurationState(SqlRowStore store, string project)
    {
        var rows = new Dictionary<string,object>(StringComparer.Ordinal);
        foreach (var table in new[] { "config_table", "config_column", "config_index", "config_index_column", "config_page", "config_page_column" })
            rows[table] = (await store.ReadIncludingDeletedAsync(table, new Dictionary<string,object?> { ["yz_project_id"] = long.Parse(project) })).OrderBy(row => SqlRowStore.Id(row)).Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value)).ToArray();
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    /// <summary>其他项目是真实原接口配置，新增的可信服务端连接只指向既有业务测试库，不接受客户端连接串。</summary>
    private sealed class ForeignImportCase : IAsyncDisposable
    {
        internal const string TableRoute = "/admin/auto/config_table/6191083005411328";
        private const string ProjectRoute = "/admin/auto/yz_project/6186303424888832";
        private const string AdminRoute = "/admin/auto/yz_admin_type/1696799309006348290";
        private readonly ImportCase source;
        private readonly string name = "mt_import_owner_" + Guid.NewGuid().ToString("N");
        private WebApplicationFactory<Program>? host;
        private HttpClient? demo;
        private HttpClient? other;
        internal string ProjectId { get; private set; } = "";
        internal string AdminId { get; private set; } = "";
        internal HttpClient Demo => demo!;
        internal HttpClient Other => other!;
        private ForeignImportCase(ImportCase source) { this.source = source; }
        /// <summary>复用真实主身份会话，仅改变工作区，仍由服务端权限和当前归属校验。</summary>
        private HttpClient Client(WebApplicationFactory<Program> factory, string project)
        {
            var client = factory.CreateClient();
            // 派生WebApplicationFactory的首个客户端可能继承父地址；以本宿主实际监听地址绑定，避免误打父宿主。
            client.BaseAddress = new Uri(Assert.Single(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses));
            client.DefaultRequestHeaders.Add("loginStateId", Assert.Single(source.Client.DefaultRequestHeaders.GetValues("loginStateId"))); client.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = project }))); return client;
        }
        /// <summary>项目连接描述只用于原必填显示，不包含秘密；实际接入仅来自宿主注入的已验证配置。</summary>
        internal static async Task<ForeignImportCase> Create(ImportCase source)
        {
            var fixture = new ForeignImportCase(source);
            try
            {
                var project = Data(await Post(source.Client, ProjectRoute + "/add", new { name = fixture.name, mysqlConnStr = "服务端配置的既有业务测试库", shortName = "importfixture", dbName = source.Sessions.Business.Options.ExpectedDatabase, contextRoot = "/api", eclipseBaseName = "Example.ImportFixture", yzYzpVersionId = TemplateVersionCatalog.CurrentId, dbType = 2, primaryColor = "#0ab3ad" })); fixture.ProjectId = project.GetProperty("id").GetString()!;
                var platformOptions = source.Sessions.Platform.Options; var businessOptions = source.Sessions.Business.Options;
                fixture.host = source.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(provider => new TargetDatabaseSessions(platformOptions, businessOptions, provider.GetRequiredService<MetadataRegistry>(), new Dictionary<string,Metarill.Data.DatabaseOptions> { [fixture.ProjectId] = businessOptions }))));
                fixture.host.UseKestrel(0);
                fixture.demo = fixture.Client(fixture.host, Project); fixture.other = fixture.Client(fixture.host, fixture.ProjectId); Assert.Equal(fixture.demo.BaseAddress, fixture.other.BaseAddress); Assert.NotEqual(source.Client.BaseAddress, fixture.demo.BaseAddress);
                fixture.AdminId = Data(await Post(fixture.other, AdminRoute + "/add", new { name = fixture.name + "后台" })).GetProperty("id").GetString()!;
                var schemas = fixture.host.Services.GetRequiredService<ProjectSchemaService>(); Assert.Equal((await schemas.Resolve(Project).InspectTargetIdentityAsync()).Fingerprint, (await schemas.Resolve(fixture.ProjectId).InspectTargetIdentityAsync()).Fingerprint);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        /// <summary>只删除该独占项目中本轮随机表的活动配置，保留墓碑，原聚合入口处理字段/索引/页面。</summary>
        internal async Task DeleteOwnedTables()
        {
            foreach (var name in source.Names) foreach (var row in await source.Store.ReadAsync("config_table", new Dictionary<string,object?> { ["table_name"] = name, ["yz_project_id"] = long.Parse(ProjectId) }))
                Data(await Body(await Other.SendAsync(new(HttpMethod.Delete, "/api" + TableRoute) { Content = JsonContent.Create(new { id = SqlRowStore.Text(row, "id") }) })));
        }
        /// <summary>所有失败路径按唯一名称发现项目后清理；不删物理源表，交由其独占源夹具独立核零。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                var project = (await source.Store.ReadAsync("yz_project", new Dictionary<string,object?> { ["name"] = name })).SingleOrDefault(); if (project is null) return; ProjectId = SqlRowStore.Text(project, "id");
                using var fallback = other is null ? Client(source.Factory, ProjectId) : null; other ??= fallback; await DeleteOwnedTables();
                foreach (var admin in await source.Store.ReadAsync("yz_admin_type", new Dictionary<string,object?> { ["yz_project_id"] = long.Parse(ProjectId) }))
                    Data(await Body(await Other.SendAsync(new(HttpMethod.Delete, "/api" + AdminRoute) { Content = JsonContent.Create(new { id = SqlRowStore.Text(admin, "id") }) })));
                Data(await Body(await source.Client.SendAsync(new(HttpMethod.Delete, "/api" + ProjectRoute) { Content = JsonContent.Create(new { id = ProjectId }) })));
                foreach (var table in new[] { "config_table", "config_column", "config_index", "config_index_column", "config_page", "config_page_column", "yz_admin_type" }) Assert.Empty(await source.Store.ReadAsync(table, new Dictionary<string,object?> { ["yz_project_id"] = long.Parse(ProjectId) }));
                Assert.Empty(await source.Store.ReadAsync("yz_project", new Dictionary<string,object?> { ["id"] = long.Parse(ProjectId) }));
            }
            finally { demo?.Dispose(); other?.Dispose(); if (host is not null) await host.DisposeAsync(); }
        }
    }
}
