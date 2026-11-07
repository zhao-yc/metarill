using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class FreshInstallMySqlTests
{
    /// <summary>原两个配置页的后台候选新旧地址在正式宿主中执行同一授权、搜索和回读。</summary>
    private static async Task VerifyPlatformOptionAliasesAsync(HttpClient owner, SqlRowStore store, MetadataCatalog platform, MetadataCatalog demo)
    {
        const string child = "1868973685063311361", standalone = "6191175925956608", target = "1696799309006348290";
        const string user = "fresh-platform-options", keyword = "候选验收%_'";
        const string permission = "/api/admin/sys/yzp_permission";
        const long role = 9007199254742101, account = 9007199254742102;
        const long first = 9007199254742111, second = 9007199254742112, decoy = 9007199254742113;
        const long deleted = 9007199254742114, foreign = 9007199254742115;
        var project = Parse(platform.YzProjectId);
        var admin = Parse(platform.YzAdminTypes.Single().Id);
        var password = Guid.NewGuid().ToString("N");
        var parentId = platform.ConfigTables.Single(t => t.TableName == "config_page").Id;
        var foreignParentId = demo.ConfigTables[0].Id;

        await store.InsertAsync("yz_role", new Dictionary<string, object?> { ["id"] = role, ["name"] = user, ["is_super"] = 0, ["yz_project_id"] = project, ["yz_admin_type_id"] = admin });
        await store.InsertAsync("yz_manager", new Dictionary<string, object?> { ["id"] = account, ["name"] = user, ["real_name"] = user, ["password"] = PasswordHasher.Hash(password), ["yz_role_ids"] = JsonSerializer.Serialize(new[] { Key(role) }), ["is_disabled"] = 0, ["yz_project_id"] = project, ["yz_admin_type_id"] = admin });

        // 同名的高位ID检验稳定排序。通配符干扰、跨项目及墓碑分别独立造数。
        foreach (var (id, name, ownerId, tombstone) in new[]
        {
            (second, keyword, project, false), (first, keyword, project, false),
            (decoy, "候选验收AX'", project, false), (deleted, keyword, project, true),
            (foreign, keyword, Parse(demo.YzProjectId), false)
        })
            await store.InsertAsync("yz_admin_type", new Dictionary<string, object?> { ["id"] = id, ["name"] = name, ["yz_project_id"] = ownerId, ["delete_no"] = tombstone ? new DateTime(2026, 11, 1) : null });

        using var reader = new HttpClient { BaseAddress = owner.BaseAddress };
        await FreshWebHttp.LoginAsync(reader, user, password, form: true);
        var identity = (await FreshWebHttp.SendAsync(reader, HttpMethod.Get, "/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data");
        Assert.False(identity.GetProperty("isSuper").GetBoolean());

        // 平台配置页有独立管理员门槛。即使普通角色的草稿有页查询权，两种地址也必须拒绝。
        var snapshot = (await FreshWebHttp.SendAsync(owner, HttpMethod.Post, permission + "/snapshot", new { roleId = Key(role) })).GetProperty("data");
        var pagePermissions = new[] { child, standalone, target }.ToDictionary(page => page, _ => new[] { "is_query" }, StringComparer.Ordinal);
        await FreshWebHttp.SendAsync(owner, HttpMethod.Post, permission + "/save", new
        {
            roleId = Key(role), revision = snapshot.GetProperty("revision").GetString(),
            grants = new { pagePermissions, menuIds = Array.Empty<string>(), buttonIds = Array.Empty<string>() }
        });

        static string Route(string page, string field, string suffix) => $"/api/admin/auto/config_page/{page}/findNameIdYzAdminType2200000000000000{field}{suffix}";
        async Task<JsonElement> CompareAsync(string page, string suffix, object body)
        {
            var current = await FreshWebHttp.SendAsync(owner, HttpMethod.Post, Route(page, "105", suffix), body);
            var legacy = await FreshWebHttp.SendAsync(owner, HttpMethod.Post, Route(page, "104", suffix), body);
            Assert.True(JsonElement.DeepEquals(current, legacy), "候选新旧地址响应不一致。");
            return legacy.GetProperty("data");
        }
        async Task RejectBothAsync(HttpClient client, string page, string suffix, object body, string message = "无权执行此操作。")
        {
            foreach (var field in new[] { "104", "105" })
                await DeniedAsync(client, HttpMethod.Post, Route(page, field, suffix), body, message);
        }
        static string? Id(JsonElement option) => option.GetProperty("id").GetString();
        static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
        static long Parse(string id) => long.Parse(id, CultureInfo.InvariantCulture);

        foreach (var page in new[] { child, standalone })
        {
            var source = page == child ? parentId : null;
            var query = new { pageNo = 1, pageSize = 1, keyword, parentId = source };
            var selection = new { selectedIds = new[] { Key(second) }, parentId = source };
            var legacySelection = new { id = Key(second), parentId = source };
            await RejectBothAsync(reader, page, "/page", query);
            await RejectBothAsync(reader, page, "/selected", selection);
            await RejectBothAsync(reader, page, "", legacySelection);
            var leading = await CompareAsync(page, "/page", query);
            var trailing = await CompareAsync(page, "/page", new { pageNo = 2, pageSize = 1, keyword, parentId = source });
            Assert.Equal(2, leading.GetProperty("total").GetInt64());
            Assert.Equal(2, trailing.GetProperty("total").GetInt64());
            Assert.Equal(Key(first), Id(Assert.Single(leading.GetProperty("records").EnumerateArray())));
            var next = Assert.Single(trailing.GetProperty("records").EnumerateArray());
            Assert.Equal(Key(second), Id(next));
            Assert.Equal(keyword, next.GetProperty("name").GetString());

            var selected = await CompareAsync(page, "/selected", selection);
            Assert.Equal(Key(second), Id(Assert.Single(selected.EnumerateArray())));
            var legacySelected = await CompareAsync(page, "", legacySelection);
            Assert.True(JsonElement.DeepEquals(selected, legacySelected), "原只读回填与分页控件回读不一致。");
            foreach (var rejected in new[] { foreign, deleted })
                await RejectBothAsync(owner, page, "/selected", new { selectedIds = new[] { Key(second), Key(rejected) }, parentId = source }, "关联已选项不存在、已删除或不在当前可选范围。");
        }
        await RejectBothAsync(owner, child, "/page", new { pageNo = 1, pageSize = 1, keyword, parentId = foreignParentId }, "关联父记录不存在、已删除或不属于当前项目。");
        await RejectBothAsync(owner, child, "/selected", new { selectedIds = new[] { Key(second) } }, "关联来源页要求有效 parentId。");
    }
}
