using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Api.GenerationAssistant;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>Server-200隔离认证库与真实HTTP验证助手控制通道，队列落独占临时目录，不执行DDL。</summary>
public sealed class GenerationAssistantIntegrationTests
{
    [AgentIntegrationFact]
    public async Task Real_grants_bind_claim_and_report_preview_without_opening_generation_or_filesystem_capabilities()
    {
        var root = Path.Combine(Path.GetTempPath(), "metarill-assistant-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var fixture = await AgentFixture.CreateAsync(customize: services => { services.AddScoped<HttpRuntimeAuthorization>(); services.AddSingleton<MetadataRepository>(); services.AddSingleton(new GenerationAssistantOptions(root)); services.AddSingleton<GenerationAssistantQueue>(); });
            var first = await fixture.GrantAsync(); var access = first.GetProperty("access_token").GetString()!; var second = (await fixture.GrantAsync()).GetProperty("access_token").GetString()!;
            var capabilities = await AgentFixture.ReadAsync(await fixture.BearerAsync(access, "/api/admin/sys/agent/capabilities")); Assert.DoesNotContain("generation-assistant", capabilities.GetRawText());
            var binding = Guid.NewGuid().ToString("D"); var requestId = Guid.NewGuid().ToString("D");
            using var registered = await Send(fixture, "helper/bind", new { bindingId = binding, rootNamespace = "Example.Assistant", localBindingSha256 = new string('a', 64), mode = "apply", allowedActions = new[] { "sync-runtime" } }, access);
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode); Assert.Equal(binding, (await AgentFixture.ReadAsync(registered)).GetProperty("data").GetProperty("bindingId").GetString());
            using var unknown = await Send(fixture, "helper/bind", new { bindingId = Guid.NewGuid().ToString("D"), rootNamespace = "Example.Assistant", localBindingSha256 = new string('a', 64), targetDirectory = "禁止上传的本机目录" }, access); Assert.NotEqual(200, (await AgentFixture.ReadAsync(unknown)).GetProperty("code").GetInt32());
            using var queued = await Original(fixture,"sync_runtime",binding,requestId); var task = (await AgentFixture.ReadAsync(queued)).GetProperty("data"); Assert.Equal("queued", task.GetProperty("status").GetString()); Assert.False(task.GetProperty("applied").GetBoolean());
            using var wrongGrant = await Send(fixture, "helper/claim", new { bindingId = binding }, second); Assert.Equal(HttpStatusCode.Unauthorized, wrongGrant.StatusCode);
            using var claimed = await Send(fixture, "helper/claim", new { bindingId = binding }, access); var work = (await AgentFixture.ReadAsync(claimed)).GetProperty("data"); Assert.Equal(requestId, work.GetProperty("requestId").GetString()); Assert.Equal(fixture.DemoProject, work.GetProperty("snapshot").GetProperty("projectId").GetString());
            var report = new { bindingId = binding, requestId, leaseId = work.GetProperty("leaseId").GetString(), requestSha256 = work.GetProperty("snapshot").GetProperty("requestSha256").GetString(), status = "preview-ready", planSha256 = new string('b', 64), requiredActions = Array.Empty<string>(), createdFiles = 0, replacedFiles = 1, deletedFiles = 0, preservedFiles = 1, errorCode = (string?)null };
            using var returned = await Send(fixture, "helper/report", report, access); var view = (await AgentFixture.ReadAsync(returned)).GetProperty("data"); Assert.Equal("preview_ready", view.GetProperty("status").GetString()); Assert.False(view.GetProperty("applied").GetBoolean()); Assert.Equal("not_applied", view.GetProperty("outcome").GetString());
            using var queried = await fixture.WebAsync(HttpMethod.Get, "/api/admin/sys/generation-assistant/requests/" + requestId, project: fixture.DemoProject); Assert.Equal("preview_ready", (await AgentFixture.ReadAsync(queried)).GetProperty("data").GetProperty("status").GetString());
            using var bindings=await fixture.WebAsync(HttpMethod.Get,"/api/admin/sys/generation-assistant/bindings",project:fixture.DemoProject); Assert.Single((await AgentFixture.ReadAsync(bindings)).GetProperty("data").EnumerateArray());
            using var missing=await fixture.WebAsync(HttpMethod.Get,"/api/admin/custom/index/generate_project_config",project:fixture.DemoProject); Assert.NotEqual(200,(await AgentFixture.ReadAsync(missing)).GetProperty("code").GetInt32());
            using var badQuery=await Original(fixture,"sync_runtime?projectId=999",binding,Guid.NewGuid().ToString("D")); Assert.NotEqual(200,(await AgentFixture.ReadAsync(badQuery)).GetProperty("code").GetInt32());
            var metadataHash=work.GetProperty("snapshot").GetProperty("metadataSha256").GetString(); var groupId=Guid.NewGuid().ToString("D");
            using var approved=await Send(fixture,"helper/authorize",new {bindingId=binding,groupId,metadataSha256=metadataHash,requestIds=new[]{requestId},actions=new[]{"sync-runtime"},planSha256=new string('c',64)},access);
            Assert.Equal(HttpStatusCode.OK,approved.StatusCode); var approvedBody=await AgentFixture.ReadAsync(approved); Assert.Equal(200,approvedBody.GetProperty("code").GetInt32()); var ticket=approvedBody.GetProperty("data"); var check=new{bindingId=binding,groupId,ticketSha256=ticket.GetProperty("ticketSha256").GetString(),planSha256=new string('c',64)};
            using var verified=await Send(fixture,"helper/verify-ticket",check,access); Assert.Equal(HttpStatusCode.OK,verified.StatusCode);
            using var wrongTicketGrant=await Send(fixture,"helper/verify-ticket",check,second); Assert.Equal(HttpStatusCode.Unauthorized,wrongTicketGrant.StatusCode);
            using var forbiddenEnqueue = await Send(fixture, "requests", new { bindingId = binding, action = "sync-runtime" }, access, Guid.NewGuid().ToString("D")); Assert.NotEqual(HttpStatusCode.OK, forbiddenEnqueue.StatusCode);
            using var forbiddenOldGet = await fixture.BearerAsync(access, "/api/admin/custom/index/sync_runtime"); Assert.NotEqual(HttpStatusCode.OK, forbiddenOldGet.StatusCode);
            var persisted = string.Join("", Directory.GetFiles(root, "*.json").Select(File.ReadAllText)); Assert.DoesNotContain(access, persisted); Assert.DoesNotContain(fixture.WebToken, persisted); Assert.DoesNotContain("禁止上传的本机目录", persisted);
            using var revoked = await fixture.WebAsync(HttpMethod.Post, "/api/admin/sys/agent/revoke", new { grantId = long.Parse((await AgentFixture.ReadAsync(await fixture.BearerAsync(access, "/api/admin/sys/agent/me"))).GetProperty("data").GetProperty("grantId").ToString()) }, fixture.DemoProject); Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            using var rejected = await Send(fixture, "helper/claim", new { bindingId = binding }, access); Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            using var rejectedTicket=await Send(fixture,"helper/verify-ticket",check,access); Assert.Equal(HttpStatusCode.Unauthorized,rejectedTicket.StatusCode);
            using var rejectedBindings=await fixture.WebAsync(HttpMethod.Get,"/api/admin/sys/generation-assistant/bindings",project:fixture.DemoProject); Assert.Empty((await AgentFixture.ReadAsync(rejectedBindings)).GetProperty("data").EnumerateArray());
            using var rejectedWeb = await Send(fixture, "requests", new { bindingId = binding, action = "sync-runtime" }, null, Guid.NewGuid().ToString("D")); Assert.Equal(HttpStatusCode.Unauthorized, rejectedWeb.StatusCode);
        }
        finally { if (Directory.Exists(root)) { Assert.StartsWith(Path.GetTempPath(), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); Directory.Delete(root, true); } }
    }
    /// <summary>真实原按钮GET保持固定路径，确认和绑定仅使用明确头，未传本机目录。</summary>
    private static async Task<HttpResponseMessage> Original(AgentFixture fixture,string route,string binding,string id)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/admin/custom/index/"+route); request.Headers.Add("loginStateId",fixture.WebToken); request.Headers.Add("sysParams",Uri.EscapeDataString(JsonSerializer.Serialize(new{yzProjectId=fixture.DemoProject})));
        request.Headers.Add("X-Yzp-Agent-Confirm","true"); request.Headers.Add("X-Yzp-Agent-Request-Id",id); request.Headers.Add("X-Metarill-Assistant-Binding",binding); return await fixture.Client.SendAsync(request);
    }
    /// <summary>真实bearer/web传输保持分离；确认头只授权当前队列请求，不能跨入普通Agent能力目录。</summary>
    private static async Task<HttpResponseMessage> Send(AgentFixture fixture, string path, object body, string? bearer = null, string? requestId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sys/generation-assistant/" + path) { Content = JsonContent.Create(body) };
        if (bearer is null) { request.Headers.Add("loginStateId", fixture.WebToken); request.Headers.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = fixture.DemoProject }))); }
        else request.Headers.Authorization = new("Bearer", bearer);
        if (requestId is not null) { request.Headers.Add("X-Yzp-Agent-Request-Id", requestId); request.Headers.Add("X-Yzp-Agent-Confirm", "true"); }
        return await fixture.Client.SendAsync(request);
    }
}
