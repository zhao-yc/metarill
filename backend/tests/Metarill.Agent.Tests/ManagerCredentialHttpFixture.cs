using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Runtime;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>复用真实角色安全宿主，仅登记本用例先证明不存在的随机账号名称，不登记任意既有账号。</summary>
internal sealed class ManagerCredentialHttpFixture(RoleSecurityFixture role) : IAsyncDisposable
{
    internal const string Page = "6031906822946816";
    internal const string Route = "/api/admin/auto/yz_manager/" + Page;
    internal RoleSecurityFixture Role { get; } = role;
    private readonly HashSet<string> ownedNames = new(StringComparer.Ordinal);
    internal int CleanedManagers { get; private set; }

    /// <summary>普通同后台编辑者通过原账号页query/new/update授权，不增加超级身份或替换任何guard。</summary>
    internal static async Task<ManagerCredentialHttpFixture> CreateAsync()
    {
        var role = await RoleSecurityFixture.CreateAsync(includeIdentityEndpoints: true);
        try
        {
            await role.GrantAsync(Page, "is_query", "is_new", "is_update");
            Assert.False((await role.IdentityAsync()).Actor.IsAdministrator);
            return new(role);
        }
        catch { await role.DisposeAsync(); throw; }
    }

    /// <summary>请求发出前登记唯一名称，以便成功响应丢失时也能按真实创建者精确恢复清理。</summary>
    internal async Task<string> ReserveNameAsync()
    {
        var name = "mt_credential_" + Guid.NewGuid().ToString("N")[..16];
        var existing = await Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["name"] = name });
        Assert.True(existing.Count == 0, "随机账号名称必须从未存在，失败时不输出账号行。");
        if (!ownedNames.Add(name)) throw new InvalidOperationException("随机账号名称重复，停止夹具写入。");
        return name;
    }

    /// <summary>原页面完整新增实体包含旧空回显、字符串角色ID及原“启用”值，不裁剪成简化新UI表单。</summary>
    internal async Task<Dictionary<string, object?>> OriginalCreateAsync(string name, string? password)
    {
        var identity = await Role.IdentityAsync();
        return new()
        {
            ["id"] = "", ["name"] = name, ["password"] = password, ["salt"] = "", ["oldPassword"] = "",
            ["yzRoleIds"] = new[] { Role.RoleId.ToString(CultureInfo.InvariantCulture) },
            ["realName"] = "原表单账号", ["mobile"] = "", ["lockIp"] = "", ["lastLoginTime"] = null,
            ["loginIp"] = "", ["email"] = "", ["phone"] = "", ["isDisabled"] = 1, ["notes"] = "",
            ["isEditPwd"] = 0, ["editPwdTime"] = null, ["openid"] = "", ["avatar"] = "", ["dingUserid"] = "",
            ["dingUnionid"] = "", ["loginRiseTime"] = null, ["loginStopTime"] = null, ["validityTime"] = null,
            ["yzAdminTypeId"] = identity.PrincipalAdminTypeId, ["yzProjectId"] = identity.Actor.ProjectId
        };
    }

    /// <summary>固定原生成Controller路由与真实loginStateId，不构造伪Actor或自定义测试保存端点。</summary>
    internal async Task<JsonElement> ManagerAsync(HttpMethod method, string suffix, object? body = null, int code = 200)
    {
        using var request = new HttpRequestMessage(method, Route + suffix); request.Headers.Add("loginStateId", Role.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await Role.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync(response); Assert.Equal(code, result.GetProperty("code").GetInt32()); return result;
    }

    /// <summary>只读取本用例登记的账号；快照仅返回全行摘要，断言失败也不打印数据库凭据。</summary>
    internal async Task<Dictionary<string, object?>> RowAsync(string name)
    {
        if (!ownedNames.Contains(name)) throw new InvalidOperationException("不允许读取未登记账号。");
        var rows = await Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["name"] = name });
        Assert.True(rows.Count == 1, "自有账号必须恰好一行，失败时不输出账号行。");
        var row = rows[0]; RequireOwned(row, name); return row;
    }
    internal static string Fingerprint(IReadOnlyDictionary<string, object?> row) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new SortedDictionary<string, object?>(row.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal), JsonDefaults.Options)));
    internal static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    { using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return parsed.RootElement.Clone(); }

    /// <summary>先证明id/name/创建者/项目完全属于夹具，再删该账号会话和账号；不碰操作者或既有默认账号。</summary>
    internal async Task CleanupOwnedAsync()
    {
        foreach (var name in ownedNames.ToArray())
        {
            var rows = await Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["name"] = name });
            if (rows.Count > 1) throw new InvalidOperationException("自有账号名称不唯一，保留现场停止清理。");
            if (rows.Count == 1)
            {
                var row = rows[0]; RequireOwned(row, name); var id = SqlRowStore.Id(row);
                await using var connection = Role.Session.Provider.CreateConnection(Role.Session.Options); await connection.OpenAsync();
                await DeleteAsync(connection, "DELETE FROM `sys_session` WHERE `account_id`=@id", id);
                await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM `yz_manager` WHERE `id`=@id AND `name`=@name AND `insert_manager_id`=@creator AND `yz_project_id`=@project";
                Add(command, "id", id); Add(command, "name", name); Add(command, "creator", Role.AccountId); Add(command, "project", long.Parse(Role.ProjectId, CultureInfo.InvariantCulture));
                if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("自有账号清理范围变化，保留现场停止。");
                var sessionsAfter = await Role.Store.ReadIncludingDeletedAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = id });
                var managersAfter = await Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["id"] = id });
                Assert.True(sessionsAfter.Count == 0, "自有账号会话清理后必须零残留，失败时不输出会话行。");
                Assert.True(managersAfter.Count == 0, "自有账号清理后必须零残留，失败时不输出账号行。");
                CleanedManagers++;
            }
            var byNameAfter = await Role.Store.ReadIncludingDeletedAsync("yz_manager", new Dictionary<string, object?> { ["name"] = name });
            Assert.True(byNameAfter.Count == 0, "自有账号名称清理后必须零残留，失败时不输出账号行。");
            ownedNames.Remove(name);
        }
    }
    /// <summary>读取和清理均独立核验真实所有权；名称相同、创建者或项目变化都不能按猜测继续。</summary>
    private void RequireOwned(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (SqlRowStore.Id(row) <= 0 || SqlRowStore.Id(row) == Role.AccountId || SqlRowStore.Text(row, "name") != name ||
            !name.StartsWith("mt_credential_", StringComparison.Ordinal) || SqlRowStore.Id(row, "insert_manager_id") != Role.AccountId || SqlRowStore.Text(row, "yz_project_id") != Role.ProjectId)
            throw new InvalidOperationException("账号不符合本夹具的精确所有权，停止读取或清理。");
    }
    /// <summary>仅执行固定自有会话清理语句，参数不拼入SQL。</summary>
    private static async Task DeleteAsync(DbConnection connection, string sql, long id)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; Add(command, "id", id); await command.ExecuteNonQueryAsync(); }
    private static void Add(DbCommand command, string name, object value)
    { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
    /// <summary>子账号先清理、操作者后清理；任一未知清理状态保留异常，不冒称零残留。</summary>
    public async ValueTask DisposeAsync()
    { try { await CleanupOwnedAsync(); } finally { await Role.DisposeAsync(); } }
}
