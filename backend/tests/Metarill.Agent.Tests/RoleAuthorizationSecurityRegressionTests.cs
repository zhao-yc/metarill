using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Platform.Generated.DTOs;
using Metarill.Platform.Generated.Mappers;
using Metarill.Platform.Generated.Services;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>角色安全回归只使用metarill_test和本用例唯一主键，不连接业务库或清理既有数据。</summary>
internal sealed class RoleSecurityFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids = new(855);
    internal DatabaseSession Session { get; private set; } = null!;
    internal SqlRowStore Store { get; private set; } = null!;
    internal MetadataRegistry Registry { get; private set; } = null!;
    internal AuthenticationService Authentication { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal long AccountId { get; private set; }
    internal long RoleId { get; private set; }
    internal string Token { get; private set; } = "";
    internal string ProjectId { get; private set; } = "";
    internal string RolePage => Registry.Platform.ConfigPages.Single(p => p.ConfigTableId == Registry.Platform.ConfigTables.Single(t => t.TableName == "yz_role").Id).Id;
    private WebApplication? _application;
    private readonly List<long> _permissions = [];

    internal static async Task<RoleSecurityFixture> CreateAsync(bool demo = false, bool administrator = false)
    {
        var fixture = new RoleSecurityFixture();
        try
        {
            using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            var options = settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_test") throw new InvalidOperationException("角色安全测试只允许metarill_test隔离库。");
            fixture.Session = new(options);
            var path = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
            fixture.Registry = new(path, Path.Combine(Path.GetDirectoryName(path)!, "demo.json")); fixture.Store = new(fixture.Session, fixture.Registry.Platform);
            fixture.ProjectId = demo ? fixture.Registry.Demo.YzProjectId : fixture.Registry.Platform.YzProjectId;
            fixture.AccountId = Ids.NextId(); fixture.RoleId = Ids.NextId(); var name = "mt_role_sec_" + Guid.NewGuid().ToString("N")[..16];
            await fixture.Store.InsertAsync("yz_role", new Dictionary<string, object?> { ["id"] = fixture.RoleId, ["name"] = name, ["sort_no"] = 10, ["is_super"] = administrator ? 1 : 0,
                ["is_built_in"] = 0, ["yz_project_id"] = long.Parse(fixture.ProjectId), ["yz_admin_type_id"] = long.Parse(fixture.Registry.Platform.YzAdminTypes[0].Id) });
            await fixture.Store.InsertAsync("yz_manager", new Dictionary<string, object?> { ["id"] = fixture.AccountId, ["name"] = name, ["is_disabled"] = 0, ["password"] = PasswordHasher.Hash("隔离RoleSecurity-Pass855"),
                ["yz_role_ids"] = JsonSerializer.Serialize(new[] { fixture.RoleId.ToString() }), ["yz_project_id"] = long.Parse(fixture.ProjectId), ["yz_admin_type_id"] = long.Parse(fixture.Registry.Platform.YzAdminTypes[0].Id) });
            fixture.Authentication = new(fixture.Store, Ids); fixture.Token = (await fixture.Authentication.LoginAsync(name, "隔离RoleSecurity-Pass855")).Token;
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(fixture.Session); builder.Services.AddSingleton(fixture.Store); builder.Services.AddSingleton(fixture.Registry); builder.Services.AddSingleton<IIdGenerator>(Ids);
            builder.Services.AddSingleton(fixture.Authentication); builder.Services.AddSingleton<AgentAuthenticationService>(); builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<HttpRuntimeAuthorization>(); builder.Services.AddScoped<IRuntimeAuthorization>(sp => sp.GetRequiredService<HttpRuntimeAuthorization>());
            builder.Services.AddScoped<YzRoleMapper>(); builder.Services.AddScoped<YzRoleService>(sp => new(fixture.Session, sp.GetRequiredService<YzRoleMapper>(), Ids, []));
            builder.Services.AddControllers().AddApplicationPart(typeof(Metarill.Platform.Generated.Controllers.YzRolePage1692532858529329154Controller).Assembly).AddJsonOptions(o => JsonDefaults.CopyTo(o.JsonSerializerOptions));
            var app = builder.Build();
            // 专用TestServer只检查是否执行，403外观不替代宿主已有R响应约定。
            app.Use(async (context, next) => { try { await next(); } catch (UnauthorizedAccessException) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(R<object>.Error("无此操作权限。")); } });
            app.UseRouting(); app.UseMiddleware<AgentAuthenticationMiddleware>(); app.MapControllers();
            fixture._application = app; await app.StartAsync(); fixture.Client = app.GetTestClient(); return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    internal async Task GrantAsync(string pageId, params string[] operations)
    {
        var id = Ids.NextId(); _permissions.Add(id);
        await Store.InsertAsync("yz_role_permission", new Dictionary<string, object?> { ["id"] = id, ["yz_role_id"] = RoleId, ["config_page_id"] = long.Parse(pageId), ["permission_values"] = JsonSerializer.Serialize(operations), ["yz_project_id"] = long.Parse(ProjectId) });
    }
    internal async Task<AuthenticatedIdentity> IdentityAsync() => await Authentication.AuthenticateAsync(Token, null) ?? throw new InvalidOperationException("测试登录未建立有效身份。");
    internal static HttpRuntimeAuthorization Authorization(AuthenticatedIdentity identity, MetadataRegistry registry)
    { var context = new DefaultHttpContext(); context.Items[typeof(AuthenticatedIdentity)] = identity; return new(new HttpContextAccessor { HttpContext = context }, registry); }
    internal YzRoleService Service() => new(Session, new YzRoleMapper(Session), Ids, []);
    internal async Task<HttpResponseMessage> UpdateHttpAsync(object body)
    { using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auto/yz_role/" + RolePage + "/update"); request.Headers.Add("loginStateId", Token); request.Content = JsonContent.Create(body); return await Client.SendAsync(request); }
    internal async Task<Dictionary<string, object?>> RoleAsync() => Assert.Single(await Store.ReadAsync("yz_role", new Dictionary<string, object?> { ["id"] = RoleId }));
    /// <summary>按本用例唯一账号、角色和授权主键清理，绝不按全表或既有项目清理。</summary>
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose(); if (_application is not null) await _application.DisposeAsync(); if (Session is null) return;
        try
        {
            await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
            var rows = _permissions.Select(id => (Table: "yz_role_permission", Field: "id", Id: id)).Concat(new[] { ("sys_session", "account_id", AccountId), ("yz_manager", "id", AccountId), ("yz_role", "id", RoleId) });
            foreach (var (table, field, id) in rows)
            { if (id <= 0) continue; await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM " + Session.Provider.QuoteIdentifier(table) + " WHERE " + Session.Provider.QuoteIdentifier(field) + "=@id"; var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter); await command.ExecuteNonQueryAsync(); }
        }
        finally { Session.Dispose(); }
    }
}

public sealed class RoleAuthorizationSecurityRegressionTests
{
    [AgentIntegrationFact]
    public async Task NonAdministratorWithActualRolePageGrantCannotPromoteViaHttpOrService()
    {
        await using var f = await RoleSecurityFixture.CreateAsync(); await f.GrantAsync(f.RolePage, "is_all", "is_update", "is_new");
        var identity = await f.IdentityAsync(); Assert.False(identity.Actor.IsAdministrator); Assert.Contains(f.RolePage + ":is_all", identity.Permissions);
        // 原攻击路径：自己持有角色页写权限，提交isSuper=1；平台门禁应在控制器调用Service前阻断。
        Assert.Equal(HttpStatusCode.Forbidden, (await f.UpdateHttpAsync(new { id = f.RoleId.ToString(), isSuper = 1 })).StatusCode);
        Assert.Equal("0", SqlRowStore.Text(await f.RoleAsync(), "is_super"));
        var request = JsonSerializer.Deserialize<YzRolePage1692532858529329154UpdateRequest>(JsonSerializer.Serialize(new { id = f.RoleId.ToString(), isSuper = 1 }), JsonDefaults.Options)!;
        // 内部入口即使绕过HTTP页鉴权，也必须由手写partial钩子阻止提升。
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service().Update1692532858529329154Async(request, null, identity.Actor, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service().Create1692532858529329154Async(new() { Name = new("不可提权角色"), IsSuper = new(1) }, null, identity.Actor, CancellationToken.None));
        Assert.Equal("0", SqlRowStore.Text(await f.RoleAsync(), "is_super")); Assert.False((await f.IdentityAsync()).Actor.IsAdministrator);
        var business = new RolePermissionBusiness(f.Store, new MetadataRepository(f.Store, f.Registry), RoleSecurityFixture.Authorization(identity, f.Registry), new SnowflakeIdGenerator(856));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.SaveAsync(new(f.RoleId.ToString(), new string('a', 64), new([], new(), []))));
    }

    [AgentIntegrationFact]
    public async Task BusinessPageGrantsStillAuthorizeOnlyGrantedOperationsAndCurrentProject()
    {
        await using var f = await RoleSecurityFixture.CreateAsync(demo: true); var page = f.Registry.Demo.ConfigPages[0].Id;
        await f.GrantAsync(page, "is_query", "is_new", "is_update"); var identity = await f.IdentityAsync(); var authorization = RoleSecurityFixture.Authorization(identity, f.Registry);
        Assert.Equal(f.Registry.Demo.YzProjectId, identity.Actor.ProjectId); Assert.False(identity.Actor.IsAdministrator);
        foreach (var operation in new[] { "list", "detail", "count", "create", "update" }) authorization.RequirePagePermission(page, operation);
        Assert.Throws<UnauthorizedAccessException>(() => authorization.RequirePagePermission(page, "delete"));
        Assert.Throws<UnauthorizedAccessException>(() => authorization.RequirePagePermission(f.RolePage, "update"));
        Assert.Null(await f.Authentication.AuthenticateAsync(f.Token, f.Registry.Platform.YzProjectId));
    }

    [AgentIntegrationFact]
    public async Task DefaultLoginProjectComesFromAccountForNormalAndAdministratorAccounts()
    {
        foreach (var administrator in new[] { false, true })
        {
            await using var f = await RoleSecurityFixture.CreateAsync(demo: true, administrator: administrator); var identity = await f.IdentityAsync();
            Assert.Equal(f.Registry.Demo.YzProjectId, identity.Actor.ProjectId); Assert.Equal(administrator, identity.Actor.IsAdministrator);
            var session = Assert.Single(await f.Store.ReadAsync("sys_session", new Dictionary<string, object?> { ["token_hash"] = PasswordHasher.Digest(f.Token) })); Assert.Equal(f.Registry.Demo.YzProjectId, SqlRowStore.Text(session, "project_id"));
            var strict = await new AgentAuthenticationService(f.Store, f.Authentication).AuthenticateAsync(f.Token, "web"); Assert.Equal(f.Registry.Demo.YzProjectId, strict.Identity.Actor.ProjectId);
            await f.Store.UpdateAsync("yz_manager", f.AccountId, new Dictionary<string, object?> { ["validity_time"] = DateTime.UtcNow.AddMinutes(-1) });
            Assert.Null(await f.Authentication.AuthenticateAsync(f.Token, null));
        }
    }

    [AgentIntegrationFact]
    public async Task AdministratorCanStillUpdateValidRoleFieldsAndInvalidSuperFlagRollsBack()
    {
        await using var f = await RoleSecurityFixture.CreateAsync(administrator: true); var identity = await f.IdentityAsync();
        Assert.Equal(HttpStatusCode.OK, (await f.UpdateHttpAsync(new { id = f.RoleId.ToString(), sortNo = 20, isSuper = 1 })).StatusCode);
        Assert.Equal("20", SqlRowStore.Text(await f.RoleAsync(), "sort_no"));
        var invalid = new YzRolePage1692532858529329154UpdateRequest { Id = f.RoleId, IsSuper = new(2) };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service().Update1692532858529329154Async(invalid, null, identity.Actor, CancellationToken.None));
        Assert.Equal("1", SqlRowStore.Text(await f.RoleAsync(), "is_super")); Assert.Equal("20", SqlRowStore.Text(await f.RoleAsync(), "sort_no"));
    }
}
