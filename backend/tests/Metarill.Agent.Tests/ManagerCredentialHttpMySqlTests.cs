using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Contracts;
using Metarill.Platform.Generated.DTOs;
using Metarill.Platform.Generated.Services;
using Metarill.Runtime;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>固定原账号页经真实Legacy Controller、A守卫和手写partial；只在明确Server200隔离配置下执行。</summary>
[Collection(SecurityRealmRrCollection.Name)]
public sealed class ManagerCredentialHttpMySqlTests
{
    /// <summary>原完整实体新增、掩码回显更新、省略角色、真实表单登录及注销形成同一可清理闭环。</summary>
    [AgentIntegrationFact]
    public async Task Original_full_form_hashes_password_preserves_echo_and_logs_in_then_out()
    {
        RequireRegeneratedCredentialContract();
        await using var f = await ManagerCredentialHttpFixture.CreateAsync();
        var name = await f.ReserveNameAsync(); var password = "Metarill-" + Guid.NewGuid().ToString("N")[..20];
        var added = await f.ManagerAsync(HttpMethod.Post, "/add", await f.OriginalCreateAsync(name, password));
        AssertEcho(added.GetProperty("data"), password); var row = await f.RowAsync(name);
        var id = SqlRowStore.Id(row).ToString(CultureInfo.InvariantCulture); var digest = SqlRowStore.Text(row, "password"); var roleEncoding = SqlRowStore.Text(row, "yz_role_ids");
        Assert.True(PasswordHasher.Verify(password, digest), "数据库必须保存可验证的产品PBKDF2摘要。");
        Assert.True(digest != password && digest.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal), "不能把原表单口令保存为明文。");
        Assert.Equal("0", SqlRowStore.Text(row, "is_disabled"));
        Assert.Equal(new[] { f.Role.RoleId.ToString(CultureInfo.InvariantCulture) }, JsonSerializer.Deserialize<string[]>(roleEncoding));
        var detail = (await f.ManagerAsync(HttpMethod.Get, "/detail/" + id)).GetProperty("data"); AssertEcho(detail, password);
        var echoed = JsonNode.Parse(detail.GetRawText())!.AsObject(); echoed["realName"] = "原掩码姓名更新"; echoed["password"] = ManagerCredentialValue.PasswordMask; echoed["salt"] = ""; echoed["oldPassword"] = "";
        echoed.Remove("yzRoleIds"); echoed.Remove("yzRoleNames");
        AssertEcho((await f.ManagerAsync(HttpMethod.Post, "/update", echoed)).GetProperty("data"), password);
        var updated = await f.RowAsync(name);
        Assert.True(SqlRowStore.Text(updated, "password") == digest, "精确掩码回显必须保持数据库摘要字节。");
        Assert.True(SqlRowStore.Text(updated, "yz_role_ids") == roleEncoding, "角色省略不能重写或清空旧角色编码。");
        Assert.Equal("原掩码姓名更新", SqlRowStore.Text(updated, "real_name"));
        var page = await f.ManagerAsync(HttpMethod.Post, "/page", new { filters = new { name }, pageNo = 1, pageSize = 20 });
        var records = page.GetProperty("data").GetProperty("records").EnumerateArray().ToArray();
        Assert.True(records.Length == 1, "固定名称过滤只返回自有账号，失败时不输出响应实体。"); var item = records[0];
        Assert.Equal(id, item.GetProperty("id").GetString()); Assert.Equal(1, item.GetProperty("isDisabled").GetInt32());
        foreach (var key in new[] { "password", "salt", "oldPassword" }) Assert.False(item.TryGetProperty(key, out _), "列表不能投影凭据字段。");
        using var login = await f.Role.Client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = name, ["password"] = password }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var signed = await ManagerCredentialHttpFixture.ReadAsync(login); Assert.Equal(200, signed.GetProperty("code").GetInt32());
        var token = signed.GetProperty("map").GetProperty("loginStateId").GetString()!; var identity = await f.Role.Authentication.AuthenticateAsync(token, null);
        Assert.NotNull(identity); Assert.Equal(id, identity.Actor.AccountId.ToString(CultureInfo.InvariantCulture)); Assert.False(identity.Actor.IsAdministrator);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login"); logoutRequest.Headers.Add("loginStateId", token);
        using var logout = await f.Role.Client.SendAsync(logoutRequest); Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(200, (await ManagerCredentialHttpFixture.ReadAsync(logout)).GetProperty("code").GetInt32()); Assert.Null(await f.Role.Authentication.AuthenticateAsync(token, null));
        var sessionRows = await f.Role.Store.ReadIncludingDeletedAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = long.Parse(id, CultureInfo.InvariantCulture) });
        Assert.True(sessionRows.Count == 1, "新账号只建立一个会话，失败时不输出会话行。"); Assert.NotNull(sessionRows[0]["delete_no"]);
        await f.CleanupOwnedAsync(); Assert.Equal(1, f.CleanedManagers);
    }

    /// <summary>新增缺失、null、空值、掩码和预编码全部拒绝；任何失败不留下半个账号。</summary>
    [AgentIntegrationFact]
    public async Task Invalid_new_credentials_are_rejected_without_partial_accounts()
    {
        RequireRegeneratedCredentialContract(); await using var f = await ManagerCredentialHttpFixture.CreateAsync();
        foreach (var value in new string?[] { null, "", ManagerCredentialValue.PasswordMask, "pbkdf2-sha256$pre-encoded" })
        {
            var name = await f.ReserveNameAsync(); var body = await f.OriginalCreateAsync(name, value);
            await f.ManagerAsync(HttpMethod.Post, "/add", body, code: 500);
            var rejected = await f.Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["name"] = name });
            Assert.True(rejected.Count == 0, "非法新增不得留下账号，失败时不输出账号行。");
        }
        var missingName = await f.ReserveNameAsync(); var missing = await f.OriginalCreateAsync(missingName, null); missing.Remove("password");
        await f.ManagerAsync(HttpMethod.Post, "/add", missing, code: 500);
        var missingRows = await f.Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["name"] = missingName });
        Assert.True(missingRows.Count == 0, "缺少口令不得留下账号，失败时不输出账号行。");
        await f.CleanupOwnedAsync(); Assert.Equal(0, f.CleanedManagers);
    }

    /// <summary>HTTP非法凭据、废弃非空字段和直接Service掩码都回滚全行，不能只以接口失败代替持久化证据。</summary>
    [AgentIntegrationFact]
    public async Task Invalid_updates_and_direct_service_masks_leave_the_owned_row_unchanged()
    {
        RequireRegeneratedCredentialContract(); await using var f = await ManagerCredentialHttpFixture.CreateAsync();
        var name = await f.ReserveNameAsync(); await f.ManagerAsync(HttpMethod.Post, "/add", await f.OriginalCreateAsync(name, "Metarill-original-Pass2026"));
        var row = await f.RowAsync(name); var id = SqlRowStore.Id(row); var before = ManagerCredentialHttpFixture.Fingerprint(row);
        foreach (var value in new string?[] { null, "", "pbkdf2-sha256$pre-encoded" })
        {
            await f.ManagerAsync(HttpMethod.Post, "/update", new { id = id.ToString(CultureInfo.InvariantCulture), realName = "不得写入", password = value }, code: 500);
            Assert.Equal(before, ManagerCredentialHttpFixture.Fingerprint(await f.RowAsync(name)));
        }
        foreach (var key in new[] { "salt", "oldPassword" })
        {
            await f.ManagerAsync(HttpMethod.Post, "/update", new Dictionary<string, object?> { ["id"] = id.ToString(CultureInfo.InvariantCulture), ["realName"] = "不得写入", [key] = "不能设置" }, code: 500);
            Assert.Equal(before, ManagerCredentialHttpFixture.Fingerprint(await f.RowAsync(name)));
        }
        var identity = await f.Role.IdentityAsync(); using var scope = f.Role.RequestScope(identity); var service = scope.ServiceProvider.GetRequiredService<YzManagerService>();
        foreach (var value in new string?[] { ManagerCredentialValue.PasswordMask, null, "pbkdf2-sha256$pre-encoded" })
        {
            // 从真实DTO反序列化，Optional显式null不能被测试手工构造偷换为省略。
            var request = JsonSerializer.Deserialize<YzManagerPage6031906822946816UpdateRequest>(JsonSerializer.Serialize(new { id = id.ToString(CultureInfo.InvariantCulture), realName = "不得写入", password = value }), JsonDefaults.Options)!;
            await Assert.ThrowsAsync<ArgumentException>(() => service.Update6031906822946816Async(request, null, identity.Actor, CancellationToken.None));
            Assert.Equal(before, ManagerCredentialHttpFixture.Fingerprint(await f.RowAsync(name)));
        }
        await f.CleanupOwnedAsync(); Assert.Equal(1, f.CleanedManagers);
    }

    /// <summary>不能把尚未再生的旧平台DTO用于验收；检查在建立数据库夹具之前完成，失败不能自动跳过。</summary>
    private static void RequireRegeneratedCredentialContract()
    {
        Assert.True(typeof(YzManagerPage6031906822946816CreateRequest).GetProperty("Password") is not null && typeof(YzManagerPage6031906822946816UpdateRequest).GetProperty("Password") is not null,
            "请先从已提交元数据和生成器正式再生平台凭据DTO，不能用旧产物冒充本回归通过。");
    }
    /// <summary>固定详情和写回执只允许八位掩码及空废弃字段；断言不把输入口令或摘要作为错误正文输出。</summary>
    private static void AssertEcho(JsonElement data, string password)
    {
        Assert.True(data.GetProperty("password").GetString() == ManagerCredentialValue.PasswordMask, "详情密码必须是固定八位掩码。");
        foreach (var key in new[] { "salt", "oldPassword" }) Assert.True(data.GetProperty(key).GetString() == "", "废弃凭据字段只能空回显。");
        Assert.False(data.GetRawText().Contains(password, StringComparison.Ordinal), "页面回执不能含输入口令。");
        Assert.False(data.GetRawText().Contains("pbkdf2-", StringComparison.OrdinalIgnoreCase), "页面回执不能含摘要。");
    }
}
