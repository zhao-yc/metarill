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

        var blockedSessionIdForRace = SqlRowStore.Id(Assert.Single(await target.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = accountId })));
        // 工作台项目选择仅由原 sysParams 传入。未切换与切换后指向平台后台都应拒绝。
        await DeniedAsync(owner, HttpMethod.Post, direct, new { id = adminTypeId });
        owner.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = projectId })));
        try
        {
            await DeniedAsync(owner, HttpMethod.Post, direct, new { id = platformAdminTypeId });
            Assert.True(PasswordHasher.Verify(oldPassword, SqlRowStore.Text(await ReadAccountAsync(), "password")));
            var initialization = new
            {
                buttonId = AdminAccountInitializationBusiness.ButtonId, entityId = adminTypeId,
                entity = new { id = adminTypeId, password = "本轮客户端干扰值" }
            };

            // 最后一步撤销会话失败时，前面的账号、角色和分库归属写入须整体回滚。
            var accountBeforeFailure = await ReadAccountAsync();
            var sessionBeforeFailure = Assert.Single(await target.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = accountId }));
            var blockedSessionId = SqlRowStore.Id(sessionBeforeFailure);
            const string lateConstraint = "mt_admin_init_late_session";
            await ExecuteSessionConstraintAsync($"ADD CONSTRAINT {lateConstraint} CHECK (id <> {blockedSessionId} OR delete_no IS NULL)");
            try
            {
                await DeniedAsync(owner, HttpMethod.Post, central, initialization, "操作失败，请查看服务端日志。");
                var afterFailure = await ReadAccountAsync();
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(accountBeforeFailure), JsonSerializer.SerializeToElement(afterFailure)),
                    "会话撤销失败后，账号的口令、角色及档案必须整体回滚。");
                Assert.Empty(await target.ReadAsync("yz_role", new Dictionary<string, object?> { ["yz_admin_type_id"] = admin, ["is_super"] = 1 }));
                var activeSession = Assert.Single(await target.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = accountId }));
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(sessionBeforeFailure), JsonSerializer.SerializeToElement(activeSession)),
                    "会话撤销失败后，本轮原会话必须仍然有效。");
                if (sharedDatabase)
                    await FreshWebHttp.SendAsync(targetWeb, HttpMethod.Get, permissions);
                else
                {
                    Assert.Empty(await target.ReadAsync("yz_project", new Dictionary<string, object?> { ["id"] = project }));
                    Assert.Empty(await target.ReadAsync("yz_admin_type", new Dictionary<string, object?> { ["id"] = admin }));
                }
            }
            finally { await ExecuteSessionConstraintAsync($"DROP CHECK {lateConstraint}"); }

            var first = sharedDatabase
                ? await RunLoginRaceAsync(true, central, initialization, oldPassword)
                : await FreshWebHttp.SendAsync(owner, HttpMethod.Post, central, initialization);
            Assert.Equal(JsonValueKind.Null, first.GetProperty("data").ValueKind);
            var initializedRole = await AssertResetAsync();
            if (sharedDatabase)
            {
                await DeniedAsync(targetWeb, HttpMethod.Get, permissions, expectedMessage: "登录超时");
                using var obsoleteLogin = new HttpClient { BaseAddress = owner.BaseAddress };
                await RejectPasswordAsync(obsoleteLogin, oldPassword);
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

            var repeated = sharedDatabase
                ? await RunLoginRaceAsync(false, direct, new { id = adminTypeId }, savedPassword)
                : await FreshWebHttp.SendAsync(owner, HttpMethod.Post, direct, new { id = adminTypeId });
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

        // 第三连接控制确定的事务先后，只在内存识别本轮账号下的锁语句。
        async Task<JsonElement> RunLoginRaceAsync(bool resetFirst, string resetRoute, object resetBody, string loginPassword)
        {
            await using var holder = target.Session.Provider.CreateConnection(target.Session.Options);
            await using var observer = target.Session.Provider.CreateConnection(target.Session.Options);
            await holder.OpenAsync();
            await observer.OpenAsync();
            await using var transaction = await holder.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            await using (var command = holder.CreateCommand())
            {
                command.Transaction = transaction;
                // 登录先行时，在主键上持有下一条会话记录的插入区间，让登录持住账号行锁。
                command.CommandText = resetFirst
                    ? "SELECT id FROM sys_session WHERE id=@id FOR UPDATE"
                    : "SELECT id FROM sys_session FORCE INDEX (PRIMARY) WHERE id>@id FOR UPDATE";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = resetFirst ? blockedSessionIdForRace : await ScalarAsync(target.Session, "SELECT MAX(id) FROM sys_session");
                command.Parameters.Add(parameter);
                var locked = await command.ExecuteScalarAsync();
                if (resetFirst) Assert.Equal(blockedSessionIdForRace, locked);
                else Assert.Null(locked);
            }

            using var racingWeb = new HttpClient { BaseAddress = owner.BaseAddress };
            Task<JsonElement>? reset = null;
            Task? signingIn = null;
            try
            {
                if (resetFirst)
                {
                    reset = FreshWebHttp.SendAsync(owner, HttpMethod.Post, resetRoute, resetBody);
                    await ObserveAsync(reset, "FROM `sys_session`", "AND `account_id`=", "FOR UPDATE");
                    signingIn = RejectPasswordAsync(racingWeb, loginPassword);
                    await ObserveAsync(signingIn, "FROM `yz_manager`", "AND `name`=", "FOR UPDATE");
                }
                else
                {
                    signingIn = FreshWebHttp.LoginAsync(racingWeb, username, loginPassword, form: true);
                    await ObserveAsync(signingIn, "INSERT INTO `sys_session`");
                    reset = FreshWebHttp.SendAsync(owner, HttpMethod.Post, resetRoute, resetBody);
                    await ObserveAsync(reset, "FROM `yz_manager`", "WHERE `name`=", "FOR UPDATE");
                }
            }
            finally
            {
                await transaction.RollbackAsync();
                await Task.WhenAll(reset ?? Task.CompletedTask, signingIn ?? Task.CompletedTask);
            }

            if (!resetFirst)
                await DeniedAsync(racingWeb, HttpMethod.Get, permissions, expectedMessage: "登录超时");
            return await reset!;

            async Task ObserveAsync(Task operation, params string[] fragments)
            {
                await using var probe = observer.CreateCommand();
                probe.CommandText = "SHOW FULL PROCESSLIST";
                var started = System.Diagnostics.Stopwatch.StartNew();
                long previousMatch = 0;
                while (started.Elapsed < TimeSpan.FromSeconds(7) && !operation.IsCompleted)
                {
                    long matched = 0;
                    await using (var reader = await probe.ExecuteReaderAsync())
                    {
                        var id = reader.GetOrdinal("Id");
                        var database = reader.GetOrdinal("db");
                        var info = reader.GetOrdinal("Info");
                        while (await reader.ReadAsync())
                        {
                            if (reader.IsDBNull(database) || reader.IsDBNull(info) || reader.GetString(database) != target.Session.Options.ExpectedDatabase) continue;
                            var sql = reader.GetString(info);
                            if (fragments.All(fragment => sql.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                                matched = Convert.ToInt64(reader.GetValue(id), CultureInfo.InvariantCulture);
                        }
                    }
                    if (matched > 0 && matched == previousMatch) return;
                    previousMatch = matched;
                    await Task.Delay(100);
                }
                Assert.Fail("没有在同一数据库连接连续观察到本轮预期的登录或重置阻塞语句。");
            }
        }
        // 旧密码拒绝沿用原表单和响应协议，且绝不能返回会话标识。
        async Task RejectPasswordAsync(HttpClient client, string password)
        {
            using var response = await client.PostAsync(login, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username, ["password"] = password, ["captchaVerification"] = ""
            }));
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(500, result.GetProperty("code").GetInt32());
            Assert.Equal("用户名或者密码错误", result.GetProperty("msg").GetString());
            Assert.False(result.GetProperty("map").TryGetProperty("loginStateId", out _));
        }

        // DDL 仅用于本轮空库夹具，和待验证的业务事务分别提交。
        async Task ExecuteSessionConstraintAsync(string clause)
        {
            await using var connection = target.Session.Provider.CreateConnection(target.Session.Options);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE sys_session " + clause;
            await command.ExecuteNonQueryAsync();
        }
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
