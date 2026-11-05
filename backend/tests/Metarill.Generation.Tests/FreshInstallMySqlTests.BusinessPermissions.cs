using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class FreshInstallMySqlTests
{
    /// <summary>普通角色通过真实登录和工作台授权访问发布页，跨项目记录与撤权后旧会话始终受限。</summary>
    private static async Task VerifyPublishedBusinessPermissionsAsync(Uri address, SqlRowStore platform, SqlRowStore business, string platformId, string projectId)
    {
        const string page = "2200000000000100020", menu = "2200000000000100031";
        const string customer = "/api/admin/auto/demo_customer/" + page;
        const string permission = "/api/admin/sys/yzp_permission";
        const string platformPage = "/api/admin/auto/config_table/6191083005411328/page";
        const long adminType = 2200000000000100001L, role = 9007199254742011L, superRole = 9007199254742012L;
        const long account = 9007199254742021L, superAccount = 9007199254742022L, foreignId = 9007199254742031L;
        const string marker = "组合验收客户", table = "demo_customer";
        var password = Guid.NewGuid().ToString("N");
        var project = long.Parse(projectId, CultureInfo.InvariantCulture);
        var foreignProject = long.Parse(platformId, CultureInfo.InvariantCulture);

        // 账号和角色始终由平台库保存，业务记录通过各拓扑配置的业务连接落库。
        foreach (var (roleId, accountId, user, isSuper) in new[]
        {
            (role, account, "fresh-matrix-reader", 0), (superRole, superAccount, "fresh-matrix-owner", 1)
        })
        {
            await platform.InsertAsync("yz_role", new Dictionary<string, object?> { ["id"] = roleId, ["name"] = user, ["is_super"] = isSuper, ["yz_project_id"] = project, ["yz_admin_type_id"] = adminType });
            await platform.InsertAsync("yz_manager", new Dictionary<string, object?> { ["id"] = accountId, ["name"] = user, ["real_name"] = user, ["password"] = PasswordHasher.Hash(password), ["yz_role_ids"] = JsonSerializer.Serialize(new[] { roleId.ToString(CultureInfo.InvariantCulture) }), ["is_disabled"] = 0, ["yz_project_id"] = project, ["yz_admin_type_id"] = adminType });
        }
        await business.InsertAsync(table, new Dictionary<string, object?> { ["id"] = foreignId, ["name"] = marker, ["yz_project_id"] = foreignProject });
        var roleKey = role.ToString(CultureInfo.InvariantCulture);
        var filter = new { pageNo = 1, pageSize = 20, filters = new { name = marker } };

        using var manager = new HttpClient { BaseAddress = address };
        using var ordinary = new HttpClient { BaseAddress = address };
        await FreshWebHttp.LoginAsync(manager, "fresh-matrix-owner", password, form: true);
        await FreshWebHttp.LoginAsync(ordinary, "fresh-matrix-reader", password, form: true);
        await DeniedAsync(ordinary, HttpMethod.Post, customer + "/page", filter);

        // 使用原工作台的版本号授予和撤销，不由夹具直接写权限记录。
        async Task GrantAsync(params string[] actions)
        {
            var snapshot = await FreshWebHttp.SendAsync(manager, HttpMethod.Post, permission + "/snapshot", new { roleId = roleKey });
            var revision = snapshot.GetProperty("data").GetProperty("revision").GetString();
            var pagePermissions = actions.Length == 0 ? new Dictionary<string, string[]>() : new() { [page] = actions };
            await FreshWebHttp.SendAsync(manager, HttpMethod.Post, permission + "/save", new { roleId = roleKey, revision, grants = new { menuIds = new[] { menu }, pagePermissions, buttonIds = Array.Empty<string>() } });
        }
        await GrantAsync("is_query");
        var state = (await FreshWebHttp.SendAsync(ordinary, HttpMethod.Get, "/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data");
        Assert.False(state.GetProperty("isSuper").GetBoolean());
        Assert.Equal("is_query", Assert.Single(state.GetProperty("pagePermissions").GetProperty(page).EnumerateArray()).GetString());
        var navigation = await FreshWebHttp.SendAsync(ordinary, HttpMethod.Get, "/api/admin/sys/yz_manager/getMenu");
        Assert.True(HasPage(navigation.GetProperty("menu"), page, projectId, adminType.ToString(CultureInfo.InvariantCulture)));
        var empty = (await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, customer + "/page", filter)).GetProperty("data");
        Assert.Equal(0L, empty.GetProperty("total").GetInt64());
        await DeniedAsync(ordinary, HttpMethod.Post, customer + "/add", new { name = marker });
        await DeniedAsync(ordinary, HttpMethod.Post, permission + "/snapshot", new { roleId = roleKey });
        await DeniedAsync(ordinary, HttpMethod.Post, platformPage, new { filters = new { } });

        await GrantAsync("is_query", "is_new", "is_update", "is_delete");
        var added = (await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, customer + "/add", new { name = marker, notes = "原备注", amount = 12.34m, isEnabled = 1 })).GetProperty("data");
        var id = added.GetProperty("id").GetString();
        Assert.True(long.TryParse(id, CultureInfo.InvariantCulture, out var key) && key > 0);
        var rows = (await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, customer + "/page", filter)).GetProperty("data");
        Assert.Equal(1L, rows.GetProperty("total").GetInt64());
        Assert.Equal(id, Assert.Single(rows.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
        await DeniedAsync(ordinary, HttpMethod.Get, customer + "/detail/" + foreignId, expectedMessage: "数据不存在或无权限。");
        await DeniedAsync(ordinary, HttpMethod.Post, customer + "/update", new { id = foreignId.ToString(CultureInfo.InvariantCulture), notes = "不应写入" }, "数据不存在或无权限。");
        var detail = (await FreshWebHttp.SendAsync(ordinary, HttpMethod.Get, customer + "/detail/" + id)).GetProperty("data");
        Assert.Equal(id, detail.GetProperty("id").GetString());
        var update = (await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, customer + "/update", new { id, amount = 56.78m })).GetProperty("data");
        Assert.Equal("原备注", update.GetProperty("notes").GetString());
        await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, customer + "/update", new { id, notes = (string?)null });
        var physical = Assert.Single(await business.ReadAsync(table, new Dictionary<string, object?> { ["id"] = key }));
        Assert.Equal(account, SqlRowStore.Id(physical, "insert_manager_id"));
        Assert.Equal(project, SqlRowStore.Id(physical, "yz_project_id"));
        Assert.Equal(56.78m, physical["amount"]);
        Assert.Null(physical["notes"]);
        await FreshWebHttp.SendAsync(ordinary, HttpMethod.Delete, customer, new { id });
        Assert.Empty(await business.ReadAsync(table, new Dictionary<string, object?> { ["id"] = key }));
        var untouched = Assert.Single(await business.ReadAsync(table, new Dictionary<string, object?> { ["id"] = foreignId }));
        Assert.Equal(marker, untouched["name"]);
        Assert.Null(untouched["notes"]);

        await GrantAsync();
        await DeniedAsync(ordinary, HttpMethod.Post, customer + "/page", filter);
        await DeniedAsync(ordinary, HttpMethod.Post, customer + "/add", new { name = marker });
    }

    /// <summary>原 Web 拒绝沿用 HTTP 200 和业务码 500，异常正文不回显。</summary>
    private static async Task DeniedAsync(HttpClient client, HttpMethod method, string route, object? body = null, string expectedMessage = "无权执行此操作。")
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("code").GetInt32() == 500, "拒绝请求未返回原业务码，正文与凭据不回显。");
        Assert.True(result.GetProperty("msg").GetString() == expectedMessage, "拒绝请求的业务原因与预期不符，正文与凭据不回显。");
    }
}
