using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class FreshInstallMySqlTests
{
    /// <summary>通过原两个 HTTP 入口重置本轮独占后台账号，并核对实际写入位置及旧会话。</summary>
    private static async Task VerifyAdminInitializationAsync(HttpClient owner, SqlRowStore platform, SqlRowStore target,
        string projectId, string adminTypeId, string platformAdminTypeId, bool sharedDatabase)
    {
        const string central = "/api/admin/sys/config_button/execute", direct = "/api/admin/custom/index/init_admin";
        const string permissions = "/api/admin/sys/yz_manager/getCurrentPermissions", login = "/api/admin/sys/yz_manager/login";
        const string username = "fresh-init-target", realName = "应保留的初始化账号姓名", notes = "应保留的初始化账号档案";
        const long accountId = 9007199254742201, roleId = 9007199254742202, sessionId = 9007199254742203;
        var project = long.Parse(projectId, CultureInfo.InvariantCulture);
        var admin = long.Parse(adminTypeId, CultureInfo.InvariantCulture);
        var oldPassword = Guid.NewGuid().ToString("N");
        var savedPassword = Guid.NewGuid().ToString("N");
        var oldHash = PasswordHasher.Hash(oldPassword);
        var bootstrapHash = await ScalarAsync(platform.Session, "SELECT password FROM yz_manager WHERE id=1");

        // 源后台的已保存配置是唯一口令来源。分库时先留空目标归属，验收由正式事务补齐。
        Assert.Equal(1, await platform.UpdateAsync("yz_admin_type", admin, new Dictionary<string, object?>
        {
            ["user_name"] = username, ["password"] = savedPassword
        }));
        await target.InsertAsync("yz_role", new Dictionary<string, object?>
        {
            ["id"] = roleId, ["name"] = "原普通角色", ["is_super"] = 0,
            ["yz_project_id"] = project, ["yz_admin_type_id"] = admin
        });
        await target.InsertAsync("yz_manager", new Dictionary<string, object?>
        {
            ["id"] = accountId, ["name"] = username, ["real_name"] = realName, ["notes"] = notes,
            ["password"] = oldHash, ["yz_role_ids"] = JsonSerializer.Serialize(new[] { roleId.ToString(CultureInfo.InvariantCulture) }),
            ["is_disabled"] = 0, ["yz_project_id"] = project, ["yz_admin_type_id"] = admin
        });
        using var targetWeb = new HttpClient { BaseAddress = owner.BaseAddress };
        if (sharedDatabase)
        {
            await FreshWebHttp.LoginAsync(targetWeb, username, oldPassword, form: true);
            var original = (await FreshWebHttp.SendAsync(targetWeb, HttpMethod.Get, permissions)).GetProperty("data");
            Assert.False(original.GetProperty("isSuper").GetBoolean());
        }
        else
        {
            // 平台登录在源库。本分支以目标库中符合 Web 会话结构的夹具，验证目标事务撤销。
            await target.InsertAsync("sys_session", new Dictionary<string, object?>
            {
                ["id"] = sessionId, ["account_id"] = accountId, ["token_hash"] = Guid.NewGuid().ToString("N").PadLeft(64, '0'),
                ["expires_at"] = DateTime.UtcNow.AddMinutes(20), ["kind"] = "web", ["project_id"] = project
            });
        }
        Assert.Equal(1L, await ScalarAsync(target.Session, $"SELECT COUNT(*) FROM sys_session WHERE account_id={accountId} AND delete_no IS NULL"));

        // 工作台项目选择仅由原 sysParams 传入。未切换与切换后指向平台后台都应拒绝。
        await DeniedAsync(owner, HttpMethod.Post, direct, new { id = adminTypeId });
        owner.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = projectId })));
        try
        {
            await DeniedAsync(owner, HttpMethod.Post, direct, new { id = platformAdminTypeId });
            Assert.True(PasswordHasher.Verify(oldPassword, SqlRowStore.Text(await ReadAccountAsync(), "password")));
            var first = await FreshWebHttp.SendAsync(owner, HttpMethod.Post, central, new
            {
                buttonId = AdminAccountInitializationBusiness.ButtonId, entityId = adminTypeId,
                entity = new { id = adminTypeId, password = "本轮客户端干扰值" }
            });
            Assert.Equal(JsonValueKind.Null, first.GetProperty("data").ValueKind);
            var initializedRole = await AssertResetAsync();
            if (sharedDatabase)
            {
                await DeniedAsync(targetWeb, HttpMethod.Get, permissions, expectedMessage: "登录超时");
                using var obsoleteLogin = new HttpClient { BaseAddress = owner.BaseAddress };
                using var response = await obsoleteLogin.PostAsync(login, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = username, ["password"] = oldPassword
                }));
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(500, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetInt32());
                // 原登录页使用 notSendSessionId，过期会话只用于上面的拒绝验证。
                targetWeb.DefaultRequestHeaders.Remove("loginStateId");
                await FreshWebHttp.LoginAsync(targetWeb, username, savedPassword, form: true);
                var current = (await FreshWebHttp.SendAsync(targetWeb, HttpMethod.Get, permissions)).GetProperty("data");
                // 平台权限回执的 isSuper 表示平台管理员。业务后台超级角色应能读取本后台角色工作台。
                Assert.False(current.GetProperty("isSuper").GetBoolean());
                var roles = (await FreshWebHttp.SendAsync(targetWeb, HttpMethod.Get, "/api/admin/sys/yzp_permission/roles"))
                    .GetProperty("data").GetProperty("roles");
                var activeRole = Assert.Single(roles.EnumerateArray(), role => role.GetProperty("isSuper").GetBoolean());
                Assert.Equal(initializedRole.ToString(CultureInfo.InvariantCulture), activeRole.GetProperty("id").GetString());
            }

            var repeated = await FreshWebHttp.SendAsync(owner, HttpMethod.Post, direct, new { id = adminTypeId });
            Assert.Equal(JsonValueKind.Null, repeated.GetProperty("data").ValueKind);
            Assert.Equal(initializedRole, await AssertResetAsync());
            if (sharedDatabase)
                await DeniedAsync(targetWeb, HttpMethod.Get, permissions, expectedMessage: "登录超时");
            else
            {
                var targetAdmin = Assert.Single(await target.ReadAsync("yz_admin_type", new Dictionary<string, object?> { ["id"] = admin }));
                Assert.Equal(projectId, SqlRowStore.Text(targetAdmin, "yz_project_id"));
                Assert.Null(targetAdmin.GetValueOrDefault("user_name"));
                Assert.Null(targetAdmin.GetValueOrDefault("password"));
                Assert.Empty(await platform.ReadAsync("yz_manager", new Dictionary<string, object?> { ["name"] = username }));
            }

            // 其他后台和删除记录共享用户名唯一约束；冲突不能触碰已经重置的账号或角色。
            var conflicting = await ReadAccountAsync();
            var conflictHash = SqlRowStore.Text(conflicting, "password");
            Assert.Equal(1, await target.UpdateAsync("yz_manager", accountId, new Dictionary<string, object?>
            {
                ["delete_no"] = new DateTime(2026, 11, 1)
            }));
            await DeniedAsync(owner, HttpMethod.Post, direct, new { id = adminTypeId },
                "初始化账号名已被其他后台或已删除记录占用，请修改已保存的初始化用户名。");
            Assert.Equal(1L, await ScalarAsync(target.Session, $"SELECT COUNT(*) FROM yz_manager WHERE id={accountId} AND delete_no IS NOT NULL"));
            Assert.True(Equals(conflictHash, await ScalarAsync(target.Session, $"SELECT password FROM yz_manager WHERE id={accountId}")), "同名墓碑拒绝后口令摘要必须保持。");
            Assert.True(Equals(bootstrapHash, await ScalarAsync(platform.Session, "SELECT password FROM yz_manager WHERE id=1")), "平台初始管理员的口令摘要必须保持。");
        }
        finally { owner.DefaultRequestHeaders.Remove("sysParams"); }
        await FreshWebHttp.SendAsync(owner, HttpMethod.Get, permissions);

        async Task<Dictionary<string, object?>> ReadAccountAsync() =>
            Assert.Single(await target.ReadAsync("yz_manager", new Dictionary<string, object?> { ["name"] = username }));

        async Task<long> AssertResetAsync()
        {
            var row = await ReadAccountAsync();
            Assert.Equal(accountId, SqlRowStore.Id(row));
            Assert.Equal(realName, row["real_name"]);
            Assert.Equal(notes, row["notes"]);
            Assert.Equal(0, Convert.ToInt32(row["is_disabled"], CultureInfo.InvariantCulture));
            Assert.True(PasswordHasher.Verify(savedPassword, SqlRowStore.Text(row, "password")));
            Assert.False(PasswordHasher.Verify(oldPassword, SqlRowStore.Text(row, "password")));
            var role = Assert.Single(await target.ReadAsync("yz_role", new Dictionary<string, object?> { ["yz_admin_type_id"] = admin, ["is_super"] = 1 }));
            var superId = SqlRowStore.Id(role);
            Assert.NotEqual(roleId, superId);
            Assert.Equal(superId.ToString(CultureInfo.InvariantCulture), Assert.Single(JsonSerializer.Deserialize<string[]>(SqlRowStore.Text(row, "yz_role_ids"))!));
            Assert.Equal(0L, await ScalarAsync(target.Session, $"SELECT COUNT(*) FROM sys_session WHERE account_id={accountId} AND delete_no IS NULL"));
            Assert.True(Convert.ToInt64(await ScalarAsync(target.Session, $"SELECT COUNT(*) FROM sys_session WHERE account_id={accountId} AND delete_no IS NOT NULL"), CultureInfo.InvariantCulture) > 0);
            return superId;
        }
    }
}
