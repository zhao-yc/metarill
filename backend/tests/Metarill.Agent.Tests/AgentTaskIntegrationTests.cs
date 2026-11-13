using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Demo.Generated.Entities;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Agent.Tests;

public sealed class AgentTaskIntegrationTests
{
    [AgentIntegrationFact]
    public async Task RequiredHeadersAndExactCapabilityProtectWriteAndDuplicatesNeverReplay()
    {
        await using var f = await AgentFixture.CreateAsync(); var access = (await f.GrantAsync()).GetProperty("access_token").GetString()!;
        var path = Base(f) + "/add"; var capability = "page:" + f.DemoPage + ":create"; var id = Guid.NewGuid().ToString("D");
        foreach (var missing in new[] { "capability", "confirm", "request" })
        {
            var rejected = await SendAsync(f, access, path, new { name = "不可写入" }, missing == "capability" ? null : capability, missing == "request" ? null : id, missing != "confirm");
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Empty(await f.Store.ReadAsync("sys_agent_task", new Dictionary<string, object?> { ["account_id"] = f.AccountId }));
        var mismatch = await SendAsync(f, access, path, new { name = "不可写入" }, "page:" + f.DemoPage + ":update", id);
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(f, access, "/api/admin/sys/auth/logout", new { }, capability, id)).StatusCode);
        var name = "mt_agent_success_" + Guid.NewGuid().ToString("N");
        var success = await SendAsync(f, access, path, new { name }, capability, id); Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var result = (await AgentFixture.ReadAsync(success)).GetProperty("data"); var resultId = result.GetProperty("id").GetString();
        var task = (await AgentFixture.ReadAsync(await f.BearerAsync(access, "/api/admin/sys/agent/tasks/" + id))).GetProperty("data");
        Assert.Equal("succeeded", task.GetProperty("status").GetString()); Assert.Equal("applied", task.GetProperty("outcome").GetString()); Assert.Equal(resultId, task.GetProperty("resultId").GetString());
        var duplicate = await SendAsync(f, access, path, new { name }, capability, id); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("YZP_REQUEST_ALREADY_RECORDED", Error(await AgentFixture.ReadAsync(duplicate)).GetProperty("code").GetString());
        var changed = await SendAsync(f, access, path, new { name = name + "changed" }, capability, id); Assert.Equal("YZP_CONFLICT", Error(await AgentFixture.ReadAsync(changed)).GetProperty("code").GetString());
        Assert.Equal(1, await f.BusinessSession.Select<DemoCustomer>().Where(e => e.InsertManagerId == f.AccountId).CountAsync());
    }

    [AgentIntegrationFact]
    public async Task ConcurrentSameRequestExecutesOnceAndPostCommitFailureRemainsUnknown()
    {
        await using var f = await AgentFixture.CreateAsync(); var access = (await f.GrantAsync()).GetProperty("access_token").GetString()!;
        var capability = "page:" + f.DemoPage + ":create"; var id = Guid.NewGuid().ToString("D"); var name = "mt_agent_race_" + Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(SendAsync(f, access, Base(f) + "/add", new { name }, capability, id), SendAsync(f, access, Base(f) + "/add", new { name }, capability, id));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await f.BusinessSession.Select<DemoCustomer>().Where(e => e.InsertManagerId == f.AccountId).CountAsync());
        var unknownId = Guid.NewGuid().ToString("D"); var unknown = await SendAsync(f, access, Base(f) + "/add", new { name = "mt_agent_unknown" }, capability, unknownId, failAfterCommit: true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unknown.StatusCode); Assert.Equal("unknown", Error(await AgentFixture.ReadAsync(unknown)).GetProperty("outcome").GetString());
        var task = (await AgentFixture.ReadAsync(await f.BearerAsync(access, "/api/admin/sys/agent/tasks/" + unknownId))).GetProperty("data"); Assert.Equal("unknown", task.GetProperty("status").GetString());
        Assert.Equal(2, await f.BusinessSession.Select<DemoCustomer>().Where(e => e.InsertManagerId == f.AccountId).CountAsync());
        var again = await SendAsync(f, access, Base(f) + "/add", new { name = "mt_agent_unknown" }, capability, unknownId); Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(2, await f.BusinessSession.Select<DemoCustomer>().Where(e => e.InsertManagerId == f.AccountId).CountAsync());
    }

    [AgentIntegrationFact]
    public async Task ReadsStillRequireExactCapabilityAndTaskScopeDoesNotGrantForeignAccess()
    {
        await using var f = await AgentFixture.CreateAsync(); var access = (await f.GrantAsync()).GetProperty("access_token").GetString()!;
        var read = await SendAsync(f, access, Base(f) + "/page", new { pageNo = 1, pageSize = 1 }, "page:" + f.DemoPage + ":list", null, confirm: false); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(f, access, Base(f) + "/page", new { }, null, null, confirm: false)).StatusCode);
        Assert.Empty(await f.Store.ReadAsync("sys_agent_task", new Dictionary<string, object?> { ["account_id"] = f.AccountId }));
        var actor = new RuntimeActor(f.AccountId, "虚构", true, f.DemoProject, "1"); var id = Guid.NewGuid().ToString("D");
        var hash = AgentTaskStore.Fingerprint("test", "POST", "/test", "", []); await f.Tasks.BeginAsync(actor, "test", id, hash);
        Assert.Equal("running", (await f.Tasks.GetAsync(actor, id)).Status);
        Assert.Equal("YZP_TASK_NOT_FOUND", (await Assert.ThrowsAsync<AgentTaskException>(() => f.Tasks.GetAsync(actor with { AccountId = f.AccountId + 1000 }, id))).Code);
        Assert.Equal("YZP_TASK_NOT_FOUND", (await Assert.ThrowsAsync<AgentTaskException>(() => f.Tasks.GetAsync(actor with { ProjectId = f.Registry.Platform.YzProjectId }, id))).Code);
        f.Clock.Advance(301); Assert.Equal("unknown", (await f.Tasks.GetAsync(actor, id)).Status);
        f.Clock.Advance(24 * 3600); Assert.Equal("unknown", (await f.Tasks.GetAsync(actor, id)).Outcome);
        Assert.Equal("YZP_REQUEST_ALREADY_RECORDED", (await Assert.ThrowsAsync<AgentTaskException>(() => f.Tasks.BeginAsync(actor, "test", id, hash))).Code);
    }

    [AgentIntegrationFact]
    public async Task OversizedRequestIsRejectedBeforeRegistrationAndBusinessFailureCannotClaimNotApplied()
    {
        await using var f = await AgentFixture.CreateAsync(); var access = (await f.GrantAsync()).GetProperty("access_token").GetString()!; var capability = "page:" + f.DemoPage + ":create";
        var response = await SendAsync(f, access, Base(f) + "/add", new { name = new string('x', 1024 * 1024) }, capability, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode); Assert.Empty(await f.Store.ReadAsync("sys_agent_task", new Dictionary<string, object?> { ["account_id"] = f.AccountId }));
        var id = Guid.NewGuid().ToString("D"); var failed = await SendAsync(f, access, Base(f) + "/add", new { }, capability, id);
        Assert.Equal("unknown", Error(await AgentFixture.ReadAsync(failed)).GetProperty("outcome").GetString());
        Assert.Equal("unknown", (await f.Tasks.GetAsync(new(f.AccountId, "虚构", true, f.DemoProject, "1"), id)).Status);
        Assert.Equal(0, await f.BusinessSession.Select<DemoCustomer>().Where(e => e.InsertManagerId == f.AccountId).CountAsync());
    }
    private static JsonElement Error(JsonElement root) => root.GetProperty("map").GetProperty("agentError");
    private static string Base(AgentFixture f) => "/api/admin/auto/demo_customer/" + f.DemoPage;
    private static async Task<HttpResponseMessage> SendAsync(AgentFixture f, string access, string path, object body, string? capability, string? requestId, bool confirm = true, bool failAfterCommit = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path); request.Headers.Authorization = new("Bearer", access); request.Content = JsonContent.Create(body);
        if (capability is not null) request.Headers.Add("X-Yzp-Agent-Capability", capability);
        if (requestId is not null) request.Headers.Add("X-Yzp-Agent-Request-Id", requestId);
        if (confirm) request.Headers.Add("X-Yzp-Agent-Confirm", "true");
        if (failAfterCommit) request.Headers.Add("X-Test-After-Commit", "fail");
        return await f.Client.SendAsync(request);
    }
}
