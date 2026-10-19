using System.Net;
using System.Text.Json;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

public sealed class AgentOAuthIntegrationTests
{
    [AgentIntegrationFact]
    public async Task PublicDeviceRejectsClientPrivilegesAndStoresOnlyHash()
    {
        await using var f = await AgentFixture.CreateAsync();
        var bad = await f.Client.PostAsync("/api/agent/oauth/device", AgentFixture.Form(("client_id", AgentOAuthService.ClientId), ("project_id", f.DemoProject), ("is_admin", "true")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("invalid_request", (await AgentFixture.ReadAsync(bad)).GetProperty("error").GetString());
        var foreign = await f.Client.PostAsync("/api/agent/oauth/device", AgentFixture.Form(("client_id", "foreign-cli")));
        Assert.Equal("invalid_client", (await AgentFixture.ReadAsync(foreign)).GetProperty("error").GetString());
        var device = await f.DeviceAsync(); var raw = device.GetProperty("device_code").GetString()!;
        var stored = Assert.Single(await f.Store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["device_code_hash"] = PasswordHasher.Digest(raw) }));
        Assert.NotEqual(raw, SqlRowStore.Text(stored, "device_code_hash")); Assert.Null(stored["account_id"]); Assert.Null(stored["refresh_hash"]);
        Assert.StartsWith("http://127.0.0.1:8088/#/pages/custom/agent_authorize?user_code=", device.GetProperty("verification_uri_complete").GetString());
        var response = await f.PollAsync(raw);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("authorization_pending", (await AgentFixture.ReadAsync(response)).GetProperty("error").GetString());
        Assert.Equal("slow_down", (await AgentFixture.ReadAsync(await f.PollAsync(raw))).GetProperty("error").GetString());
        stored = Assert.Single(await f.Store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["device_code_hash"] = PasswordHasher.Digest(raw) }));
        Assert.NotNull(stored["last_poll_at"]); Assert.Equal(10, Convert.ToInt32(stored["poll_interval_seconds"]));
    }

    [AgentIntegrationFact]
    public async Task WebApprovalBindsDemoAndDeviceCanOnlyBeExchangedOnce()
    {
        await using var f = await AgentFixture.CreateAsync(); var device = await f.DeviceAsync(); var code = device.GetProperty("user_code").GetString();
        var shown = await AgentFixture.ReadAsync(await f.WebAsync(HttpMethod.Get, "/api/admin/sys/agent/device?userCode=" + code, project: f.DemoProject));
        Assert.Equal(f.DemoProject, shown.GetProperty("data").GetProperty("projectId").GetString());
        Assert.Equal("PENDING", shown.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(f.WebToken, "/api/admin/sys/agent/device?userCode=" + code)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.WebAsync(HttpMethod.Post, "/api/admin/sys/agent/approve", new { userCode = code, approve = true }, f.DemoProject)).StatusCode);
        var results = await Task.WhenAll(f.PollAsync(device.GetProperty("device_code").GetString()!), f.PollAsync(device.GetProperty("device_code").GetString()!));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);
        var token = await AgentFixture.ReadAsync(results.Single(r => r.IsSuccessStatusCode)); var access = token.GetProperty("access_token").GetString()!;
        Assert.Equal(f.DemoProject, token.GetProperty("project_id").GetString());
        var me = await AgentFixture.ReadAsync(await f.BearerAsync(access, "/api/admin/sys/agent/me")); Assert.Equal(f.DemoProject, me.GetProperty("data").GetProperty("projectId").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(access, "/api/admin/sys/agent/me", f.Registry.Platform.YzProjectId)).StatusCode);
        using var wrongTransport = new HttpRequestMessage(HttpMethod.Get, "/api/admin/sys/agent/grants"); wrongTransport.Headers.Add("loginStateId", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.SendAsync(wrongTransport)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(access, "/api/admin/sys/agent/device?userCode=" + code)).StatusCode);
    }

    [AgentIntegrationFact]
    public async Task NonAdministratorCannotApproveAndDeniedOrExpiredDevicesCannotIssueTokens()
    {
        await using var f = await AgentFixture.CreateAsync(false); var device = await f.DeviceAsync(); var code = device.GetProperty("user_code").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.WebAsync(HttpMethod.Post, "/api/admin/sys/agent/approve", new { userCode = code, approve = true }, f.DemoProject)).StatusCode);
        await f.Store.UpdateAsync("yz_role", f.RoleId, new Dictionary<string, object?> { ["is_super"] = 1 });
        Assert.Equal(HttpStatusCode.OK, (await f.WebAsync(HttpMethod.Post, "/api/admin/sys/agent/approve", new { userCode = code, approve = false }, f.DemoProject)).StatusCode);
        Assert.Equal("access_denied", (await AgentFixture.ReadAsync(await f.PollAsync(device.GetProperty("device_code").GetString()!))).GetProperty("error").GetString());
        var expired = await f.DeviceAsync(); f.Clock.Advance(601);
        Assert.Equal("expired_token", (await AgentFixture.ReadAsync(await f.PollAsync(expired.GetProperty("device_code").GetString()!))).GetProperty("error").GetString());
    }

    /// <summary>真实 HTTP 列表保留原毫秒时间协议，不受全局日期格式转换影响。</summary>
    [AgentIntegrationFact]
    public async Task BrowserGrantListDatesPreserveUtcAndDoNotExposeCredentials()
    {
        await using var f = await AgentFixture.CreateAsync();
        var token = await f.GrantAsync();
        var grant = Assert.Single(await f.Store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["account_id"] = f.AccountId, ["status"] = "approved" }));
        var response = await AgentFixture.ReadAsync(await f.WebAsync(HttpMethod.Get, "/api/admin/sys/agent/grants", project: f.DemoProject));
        var listed = Assert.Single(response.GetProperty("data").EnumerateArray());
        foreach (var pair in new[] { ("createdAt", "insert_time"), ("expiresAt", "refresh_expires_at") })
        {
            var actual = listed.GetProperty(pair.Item1).GetString()!;
            Assert.True(long.TryParse(actual, out var milliseconds));
            var expected = DateTime.SpecifyKind(Convert.ToDateTime(grant[pair.Item2], System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            Assert.Equal(new DateTimeOffset(expected).ToUnixTimeMilliseconds(), milliseconds);
        }
        Assert.False(listed.TryGetProperty("refresh_hash", out _));
        Assert.False(listed.TryGetProperty("device_code_hash", out _));
        Assert.DoesNotContain(token.GetProperty("access_token").GetString()!, listed.GetRawText());
        Assert.DoesNotContain(token.GetProperty("refresh_token").GetString()!, listed.GetRawText());
    }

    [AgentIntegrationFact]
    public async Task RefreshRotatesImmediatelyAndReplayRevokesFamily()
    {
        await using var f = await AgentFixture.CreateAsync(); var token = await f.GrantAsync(); var access = token.GetProperty("access_token").GetString()!; var refresh = token.GetProperty("refresh_token").GetString()!;
        var rotated = await AgentFixture.ReadAsync(await f.RefreshAsync(refresh)); var next = rotated.GetProperty("access_token").GetString()!;
        Assert.NotEqual(access, next); Assert.NotEqual(refresh, rotated.GetProperty("refresh_token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(access, "/api/admin/sys/agent/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.BearerAsync(next, "/api/admin/sys/agent/me")).StatusCode);
        var used = Assert.Single(await f.Store.ReadAsync("sys_session", new Dictionary<string, object?> { ["token_hash"] = PasswordHasher.Digest(refresh) }));
        Assert.Equal("agent_refresh_used", SqlRowStore.Text(used, "kind"));
        Assert.Null(await f.Authentication.AuthenticateAsync(refresh, f.DemoProject));
        Assert.Equal("invalid_grant", (await AgentFixture.ReadAsync(await f.RefreshAsync(refresh))).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(next, "/api/admin/sys/agent/me")).StatusCode);
    }

    [AgentIntegrationFact]
    public async Task RevokeDisableAndAccountExpiryApplyImmediately()
    {
        await using var f = await AgentFixture.CreateAsync(); var token = await f.GrantAsync(); var access = token.GetProperty("access_token").GetString()!;
        await f.Store.UpdateAsync("yz_manager", f.AccountId, new Dictionary<string, object?> { ["is_disabled"] = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(access, "/api/admin/sys/agent/me")).StatusCode);
        await f.Store.UpdateAsync("yz_manager", f.AccountId, new Dictionary<string, object?> { ["is_disabled"] = 0, ["validity_time"] = DateTime.UtcNow.AddMinutes(-1) });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(access, "/api/admin/sys/agent/me")).StatusCode);
        await f.Store.UpdateAsync("yz_manager", f.AccountId, new Dictionary<string, object?> { ["validity_time"] = null });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sys/agent/revoke"); request.Headers.Authorization = new("Bearer", access); request.Content = System.Net.Http.Json.JsonContent.Create(new { });
        Assert.Equal(HttpStatusCode.OK, (await f.Client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.BearerAsync(access, "/api/admin/sys/agent/me")).StatusCode);
        Assert.Equal("invalid_grant", (await AgentFixture.ReadAsync(await f.RefreshAsync(token.GetProperty("refresh_token").GetString()!))).GetProperty("error").GetString());
    }

    [AgentIntegrationFact]
    public async Task CapabilityDirectoryOnlyListsRealCurrentProjectRoutesAndSchemas()
    {
        await using var f = await AgentFixture.CreateAsync(); var token = await f.GrantAsync(); var access = token.GetProperty("access_token").GetString()!;
        var directory = (await AgentFixture.ReadAsync(await f.BearerAsync(access, "/api/admin/sys/agent/capabilities"))).GetProperty("data");
        Assert.Equal(1, directory.GetProperty("protocolVersion").GetInt32()); Assert.Equal(f.DemoProject, directory.GetProperty("projectId").GetString());
        var capabilities = directory.GetProperty("capabilities").EnumerateArray().ToArray(); Assert.Equal(9, capabilities.Length);
        Assert.All(capabilities, capability => { Assert.Equal(f.DemoPage, capability.GetProperty("pageId").GetString()); Assert.StartsWith("/admin/auto/demo_customer/", capability.GetProperty("path").GetString()); });
        var export = capabilities.Single(c => c.GetProperty("operation").GetString() == "export");
        Assert.True(export.GetProperty("confirmationRequired").GetBoolean()); Assert.DoesNotContain(export.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "exports");
        var create = capabilities.Single(c => c.GetProperty("operation").GetString() == "create");
        Assert.Equal("object", create.GetProperty("requestSchema").GetProperty("type").GetString());
        Assert.True(create.GetProperty("requestSchema").GetProperty("properties").TryGetProperty("name", out _));
        await f.GrantPermissionAsync(f.DemoPage, "is_query"); await f.Store.UpdateAsync("yz_role", f.RoleId, new Dictionary<string, object?> { ["is_super"] = 0 });
        directory = (await AgentFixture.ReadAsync(await f.BearerAsync(access, "/api/admin/sys/agent/capabilities"))).GetProperty("data");
        capabilities = directory.GetProperty("capabilities").EnumerateArray().ToArray(); Assert.Equal(3, capabilities.Length); Assert.All(capabilities, c => Assert.Contains(c.GetProperty("operation").GetString(), new[] { "list", "detail", "count" }));
        var described = await f.BearerAsync(access, "/api/admin/sys/agent/capabilities/" + Uri.EscapeDataString(create.GetProperty("id").GetString()!)); Assert.Equal(HttpStatusCode.Unauthorized, described.StatusCode);
    }
}
