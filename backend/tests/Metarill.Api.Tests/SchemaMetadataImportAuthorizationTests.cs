using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>原接口新建独占应用账号：普通角色拒绝、另一会话不能消费、已认证排队期间撤权仍拒绝。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_current_web_role_is_rechecked_after_authenticated_request_queues()
    {
        var gate = new ImportRoleCommitGate();
        await using var source = await ImportCase.Create(1, factory => { var configured = factory.WithWebHostBuilder(builder => { builder.UseUrls("http://127.0.0.1:0"); builder.ConfigureServices(services => services.AddScoped<IRecordMutationObserver>(_ => gate)); }); configured.UseKestrel(0); return configured; });
        var registry = source.Factory.Services.GetRequiredService<MetadataRegistry>(); var principal = registry.Platform.ConfigPages.Single(page => page.Id == "1692532858529329154").YzAdminTypeId;
        const string roleRoute = "/admin/auto/yz_role/1692532858529329154"; const string accountRoute = "/admin/auto/yz_manager/6031906822946816";
        using var master = source.Factory.CreateClient(); master.DefaultRequestHeaders.Add("loginStateId", Assert.Single(source.Client.DefaultRequestHeaders.GetValues("loginStateId"))); master.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = registry.Platform.YzProjectId })));
        var suffix = Guid.NewGuid().ToString("N"); var roleName = "mt_import_role_" + suffix; var username = "mt_import_user_" + suffix; var password = Guid.NewGuid().ToString("N");
        Task<JsonElement>? revoking = null; Task<JsonElement>? applying = null;
        try
        {
            var role = Data(await Post(master, roleRoute + "/add", new { name = roleName, yzAdminTypeId = principal, isSuper = 0, isBuiltIn = 0, isScope = 0, sortNo = 0 })).GetProperty("id").GetString()!; gate.RoleId = long.Parse(role);
            Data(await Post(master, accountRoute + "/add", new { name = username, password, realName = "导入独占身份", yzAdminTypeId = principal, yzRoleIds = new[] { role }, isDisabled = 1 }));
            // 所有登录使用真实随机密码，不能伪造HttpContext身份或借主账号令牌充当普通账号。
            async Task<HttpClient> LoginOwned()
            {
                var client = source.Factory.CreateClient();
                try { var login = await Body(await client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["username"] = username, ["password"] = password }))); Data(login); client.DefaultRequestHeaders.Add("loginStateId", login.GetProperty("map").GetProperty("loginStateId").GetString()); client.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = registry.Platform.YzProjectId }))); return client; }
                catch { client.Dispose(); throw; }
            }
            using var ordinary = await LoginOwned(); Assert.Equal(500, (await Post(ordinary, ImportRoute + "prepare", new { tableNames = source.Names })).GetProperty("code").GetInt32());
            Data(await Post(master, roleRoute + "/update", new { id = role, isSuper = 1 }));
            ordinary.DefaultRequestHeaders.Remove("sysParams"); ordinary.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = Project })));
            var plan = Data(await Post(ordinary, ImportRoute + "prepare", new { tableNames = source.Names })); var proof = new { planId = plan.GetProperty("planId").GetString(), confirmSha256 = plan.GetProperty("confirmSha256").GetString() };
            using var anotherSession = await LoginOwned(); anotherSession.DefaultRequestHeaders.Remove("sysParams"); anotherSession.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = Project })));
            Assert.Equal(500, (await Post(anotherSession, ImportRoute + "apply", proof)).GetProperty("code").GetInt32()); Assert.Equal("unknown", Data(await Post(anotherSession, ImportRoute + "status", proof)).GetProperty("state").GetString()); Assert.Equal("prepared", Data(await Post(ordinary, ImportRoute + "status", proof)).GetProperty("state").GetString());
            var before = await source.Fingerprint(); gate.Enabled = true;
            // 原角色HTTP写入停在同事务提交前，此时旧身份仍能通过请求认证，然后排队在平台项目锁。
            revoking = Post(master, roleRoute + "/update", new { id = role, isSuper = 0 }); await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            applying = Post(ordinary, ImportRoute + "apply", proof);
            await using var probe = source.Sessions.Business.Provider.CreateConnection(source.Sessions.Business.Options); await probe.OpenAsync(); await using var command = probe.CreateCommand(); command.CommandText = "SET SESSION lock_wait_timeout=1"; await command.ExecuteNonQueryAsync();
            // 用源表既有空注释的无差异ALTER探针证明真实MDL；不改列或业务行，不靠延时猜测认证时点。
            var mdlWaited = false;
            for (var attempt = 0; attempt < 35 && !applying.IsCompleted; attempt++)
            {
                command.CommandText = $"ALTER TABLE `{source.Names[0]}` COMMENT=''";
                try { await command.ExecuteNonQueryAsync(); await Task.Delay(20); }
                catch (MySqlConnector.MySqlException error) when (error.Number == 1205) { mdlWaited = true; break; }
            }
            Assert.True(mdlWaited); Assert.False(applying.IsCompleted); gate.Release.TrySetResult(); Data(await revoking.WaitAsync(TimeSpan.FromSeconds(15)));
            var denied = await applying.WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(500, denied.GetProperty("code").GetInt32()); Assert.Equal("无权执行此操作。", denied.GetProperty("msg").GetString());
            Assert.Equal(before, await source.Fingerprint()); await source.AssertUnregistered(); await source.AssertSourceRows();
            Assert.Equal(0, Convert.ToInt32(Assert.Single(await source.Store.ReadAsync("yz_role", new Dictionary<string,object?> { ["id"] = gate.RoleId }))["is_super"]));
            // 只恢复本轮角色，重新审核后同一真实会话成功，排除结构或普通参数错误导致的偶然拒绝。
            gate.Enabled = false; Data(await Post(master, roleRoute + "/update", new { id = role, isSuper = 1 }));
            var retryPlan = Data(await Post(ordinary, ImportRoute + "prepare", new { tableNames = source.Names }));
            Assert.True(Data(await Post(ordinary, ImportRoute + "apply", new { planId = retryPlan.GetProperty("planId").GetString(), confirmSha256 = retryPlan.GetProperty("confirmSha256").GetString() })).GetProperty("configurationSaved").GetBoolean());
            Assert.Equal(before, await source.Fingerprint()); await source.AssertSourceRows();
            source.Record(new { result = "authenticated-web-revoked-during-queue", ordinaryDenied = true, separateSessionDenied = true, sourceMdl1205 = true, currentRoleDenied = true, regrantWithNewPlanSaved = true, realKestrel = true });
        }
        finally
        {
            gate.Release.TrySetResult(); gate.Enabled = false; if (revoking is not null) await revoking.WaitAsync(TimeSpan.FromSeconds(20)); if (applying is not null) await applying.WaitAsync(TimeSpan.FromSeconds(20));
            foreach (var account in await source.Store.ReadAsync("yz_manager", new Dictionary<string,object?> { ["name"] = username }))
            {
                Assert.Equal(registry.Platform.YzProjectId, SqlRowStore.Text(account, "yz_project_id")); var id = SqlRowStore.Id(account);
                foreach (var session in await source.Store.ReadAsync("sys_session", new Dictionary<string,object?> { ["account_id"] = id })) await source.Store.UpdateAsync("sys_session", SqlRowStore.Id(session), new Dictionary<string,object?> { ["delete_no"] = DateTime.UtcNow });
                Data(await Body(await master.SendAsync(new(HttpMethod.Delete, "/api" + accountRoute) { Content = JsonContent.Create(new { id = id.ToString() }) })));
                Assert.Empty(await source.Store.ReadAsync("sys_session", new Dictionary<string,object?> { ["account_id"] = id }));
            }
            foreach (var role in await source.Store.ReadAsync("yz_role", new Dictionary<string,object?> { ["name"] = roleName }))
            { Assert.Equal(registry.Platform.YzProjectId, SqlRowStore.Text(role, "yz_project_id")); Data(await Body(await master.SendAsync(new(HttpMethod.Delete, "/api" + roleRoute) { Content = JsonContent.Create(new { id = SqlRowStore.Text(role, "id") }) }))); }
            Assert.Empty(await source.Store.ReadAsync("yz_manager", new Dictionary<string,object?> { ["name"] = username })); Assert.Empty(await source.Store.ReadAsync("yz_role", new Dictionary<string,object?> { ["name"] = roleName }));
        }
    }

    /// <summary>只暂停本轮角色更新的原公开事务，创建和其他角色不受影响。</summary>
    private sealed class ImportRoleCommitGate : IRecordMutationObserver
    {
        internal long RoleId;
        internal bool Enabled;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>先证明原角色记录已写入且尚未提交，再让导入持源MDL等待项目预锁。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context, CancellationToken token = default)
        { if (Enabled && context.TableName == "yz_role" && context.RecordId == RoleId && !context.IsCreate && !context.IsDelete && context.SubmittedFields.ContainsKey("is_super")) { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(40), token); } }
    }
}
