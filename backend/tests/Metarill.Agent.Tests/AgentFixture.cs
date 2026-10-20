using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Api.AgentControllers;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
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

public sealed class AgentIntegrationFactAttribute : FactAttribute
{
    public AgentIntegrationFactAttribute()
    {
        var settings = Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS");
        var metadata = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA");
        if (string.IsNullOrEmpty(settings) || !File.Exists(settings) || string.IsNullOrEmpty(metadata) || !File.Exists(metadata))
            Skip = "未提供隔离MySQL配置及平台元数据，Agent真实验收未执行。";
    }
}
internal sealed class AgentClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
}

/// <summary>实际HTTP协议经过TestServer，身份、授权、令牌与任务都写入指定MySQL隔离库。</summary>
internal sealed class AgentFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids = new(853);
    private readonly List<long> _accounts = [];
    private readonly List<long> _roles = [];
    private WebApplication? _application;
    public DatabaseSession Session { get; private set; } = null!;
    public DatabaseSession BusinessSession { get; private set; } = null!;
    public AgentTaskStore Tasks { get; private set; } = null!;
    public MetadataRegistry Registry { get; private set; } = null!;
    public SqlRowStore Store { get; private set; } = null!;
    public AgentClock Clock { get; } = new();
    public HttpClient Client { get; private set; } = null!;
    public string WebToken { get; private set; } = "";
    public long AccountId { get; private set; }
    public long RoleId { get; private set; }
    public string DemoProject => Registry.Demo.YzProjectId;
    public string DemoPage => Registry.Demo.ConfigPages[0].Id;
    public AgentOAuthService OAuth { get; private set; } = null!;
    public AuthenticationService Authentication { get; private set; } = null!;

    public static async Task<AgentFixture> CreateAsync(bool administrator = true, Action<IServiceCollection>? customize = null)
    {
        var fixture = new AgentFixture();
        try
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            var options = json.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (options.ExpectedDatabase != "metarill_test") throw new InvalidOperationException("Agent测试只允许指定隔离库。");
            fixture.Session = new(options);
            var businessOptions = json.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (businessOptions.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("Agent测试只允许指定业务隔离库。");
            fixture.BusinessSession = new(businessOptions);
            var metadata = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
            fixture.Registry = new(metadata, Path.Combine(Path.GetDirectoryName(metadata)!, "demo.json"));
            fixture.Store = new(fixture.Session, fixture.Registry.Platform);
            fixture.Authentication = new(fixture.Store, Ids, fixture.Registry);
            fixture.OAuth = new(fixture.Store, fixture.Registry, Ids, new(), fixture.Clock);
            fixture.Tasks = new(fixture.Store, Ids, new(), fixture.Clock);
            fixture.AccountId = Ids.NextId(); fixture.RoleId = Ids.NextId();
            fixture._accounts.Add(fixture.AccountId); fixture._roles.Add(fixture.RoleId);
            var name = "mt_agent_" + Guid.NewGuid().ToString("N")[..16];
            await fixture.Store.InsertAsync("yz_role", new Dictionary<string, object?> { ["id"] = fixture.RoleId, ["name"] = name, ["is_super"] = administrator ? 1 : 0, ["yz_project_id"] = long.Parse(fixture.Registry.Platform.YzProjectId), ["yz_admin_type_id"] = long.Parse(fixture.Registry.Platform.YzAdminTypes[0].Id) });
            await fixture.Store.InsertAsync("yz_manager", new Dictionary<string, object?> { ["id"] = fixture.AccountId, ["name"] = name, ["password"] = PasswordHasher.Hash("虚构测试Password-853"), ["is_disabled"] = 0,
                ["yz_role_ids"] = JsonSerializer.Serialize(new[] { fixture.RoleId.ToString() }), ["yz_project_id"] = long.Parse(fixture.Registry.Platform.YzProjectId), ["yz_admin_type_id"] = long.Parse(fixture.Registry.Platform.YzAdminTypes[0].Id) });
            fixture.WebToken = (await fixture.Authentication.LoginAsync(name, "虚构测试Password-853")).Token;
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
            Action<IServiceCollection> configure = services =>
            {
                services.AddSingleton(fixture.Session); services.AddSingleton(fixture.Store); services.AddSingleton(fixture.Registry); services.AddSingleton<IIdGenerator>(Ids);
                services.AddSingleton(fixture.Authentication); services.AddSingleton<TimeProvider>(fixture.Clock); services.AddSingleton(new AgentOAuthOptions());
                services.AddSingleton(new AgentTaskOptions()); services.AddSingleton(fixture.Tasks);
                Metarill.Demo.Generated.GeneratedRegistration.AddGeneratedServices(services, _ => fixture.BusinessSession);
                services.AddSingleton(fixture.OAuth); services.AddSingleton<AgentAuthenticationService>(); services.AddSingleton<AgentPublicRateLimiter>();
                services.AddHttpContextAccessor(); services.AddScoped<IRuntimeAuthorization, HttpRuntimeAuthorization>(); services.AddSingleton<GeneratedCapabilityIndex>(); services.AddScoped<AgentCapabilityCatalog>();
                services.AddControllers().AddApplicationPart(typeof(AgentOAuthController).Assembly).AddJsonOptions(o => JsonDefaults.CopyTo(o.JsonSerializerOptions));
            }; configure(builder.Services); customize?.Invoke(builder.Services);
            var app = builder.Build();
                app.Use(async (context, next) =>
                {
                    try { await next(); }
                    catch (UnauthorizedAccessException error) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(R<object>.Error(error.Message), JsonDefaults.Options); }
                    catch (InvalidOperationException error) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(R<object>.Error(error.Message), JsonDefaults.Options); }
                    catch (Exception error) when (error is JsonException or ArgumentException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(R<object>.Error("请求字段无效。"), JsonDefaults.Options); }
                });
                app.UseRouting(); app.UseMiddleware<AgentAuthenticationMiddleware>(); app.UseMiddleware<AgentTaskMiddleware>();
                app.Use(async (context, next) => { await next(); if (context.Request.Headers["X-Test-After-Commit"] == "fail") throw new InvalidOperationException("测试模拟提交后响应丢失"); });
                app.MapControllers();
            fixture._application = app; await app.StartAsync(); fixture.Client = app.GetTestClient();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task<JsonElement> DeviceAsync() { var device = await ReadAsync(await Client.PostAsync("/api/agent/oauth/device", Form(("client_id", AgentOAuthService.ClientId), ("client_name", "隔离Agent测试")))); await TrackAsync(device); return device; }
    public async Task<HttpResponseMessage> PollAsync(string device) => await Client.PostAsync("/api/agent/oauth/token", Form(("client_id", AgentOAuthService.ClientId), ("grant_type", AgentOAuthService.DeviceGrant), ("device_code", device)));
    public async Task<HttpResponseMessage> RefreshAsync(string token) => await Client.PostAsync("/api/agent/oauth/token", Form(("client_id", AgentOAuthService.ClientId), ("grant_type", "refresh_token"), ("refresh_token", token)));
    public async Task<HttpResponseMessage> WebAsync(HttpMethod method, string path, object? body = null, string? project = null)
    {
        using var request = new HttpRequestMessage(method, path); request.Headers.Add("loginStateId", WebToken);
        if (project is not null) request.Headers.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = project })));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
    public async Task<HttpResponseMessage> BearerAsync(string token, string path, string? project = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Authorization = new("Bearer", token);
        if (project is not null) request.Headers.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = project })));
        return await Client.SendAsync(request);
    }
    public async Task<JsonElement> GrantAsync(string? project = null)
    {
        var device = await DeviceAsync(); var approved = await WebAsync(HttpMethod.Post, "/api/admin/sys/agent/approve", new { userCode = device.GetProperty("user_code").GetString(), approve = true }, project ?? DemoProject);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return await ReadAsync(await PollAsync(device.GetProperty("device_code").GetString()!));
    }
    public async Task GrantPermissionAsync(string page, string operation)
    {
        await Store.InsertAsync("yz_role_permission", new Dictionary<string, object?> { ["id"] = Ids.NextId(), ["yz_role_id"] = RoleId, ["config_page_id"] = long.Parse(page), ["permission_values"] = JsonSerializer.Serialize(new[] { operation }), ["yz_project_id"] = long.Parse(DemoProject) });
    }
    public static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)));
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.Clone(); }

    /// <summary>清理只按本fixture创建的账号与角色主键；不修改平台管理员或其他任务的数据。</summary>
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose(); if (_application is not null) await _application.DisposeAsync();
        if (Session is null) return;
        try
        {
            await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
            foreach (var id in _accounts)
            {
                foreach (var table in new[] { "sys_agent_task", "sys_session", "sys_agent_grant" }) await ExecuteAsync(connection, "DELETE FROM " + Session.Provider.QuoteIdentifier(table) + " WHERE account_id=@id", id);
                await ExecuteAsync(connection, "DELETE FROM yz_manager WHERE id=@id", id);
            }
            if (BusinessSession is not null)
            {
                await using var business = BusinessSession.Provider.CreateConnection(BusinessSession.Options); await business.OpenAsync();
                foreach (var id in _accounts) await ExecuteAsync(business, "DELETE FROM demo_customer WHERE insert_manager_id=@id", id);
            }
            foreach (var id in _roles) { await ExecuteAsync(connection, "DELETE FROM yz_role_permission WHERE yz_role_id=@id", id); await ExecuteAsync(connection, "DELETE FROM yz_role WHERE id=@id", id); }
            // 未批准设备没有账号，用本测试唯一客户端名称及创建的设备ID跟踪清理由测试统一登记。
            foreach (var id in _devices) await ExecuteAsync(connection, "DELETE FROM sys_agent_grant WHERE id=@id", id);
        }
        finally { BusinessSession?.Dispose(); Session.Dispose(); }
    }
    private readonly HashSet<long> _devices = [];
    public async Task TrackAsync(JsonElement device)
    { foreach (var row in await Store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["device_code_hash"] = PasswordHasher.Digest(device.GetProperty("device_code").GetString()!) })) _devices.Add(SqlRowStore.Id(row)); }
    private static async Task ExecuteAsync(DbConnection connection, string sql, long id)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter); await command.ExecuteNonQueryAsync(); }
}
