using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>六模型正式夹具必须分别准备导航与页动作，不能为测试放宽产品权限。</summary>
public sealed class GlobalCachedCrossExcelNavigationTests
{
    /// <summary>真实策略复现旧夹具：页动作齐全但MenuIds为空；补精确菜单行后32项可见且无按钮／超级身份。</summary>
    [Fact]
    public void Ordinary_fixture_requires_explicit_navigation_grants_separate_from_page_actions()
    {
        var metadata = GlobalCachedCrossExcelNativePreparation.Catalog(); var before = JsonSerializer.Serialize(metadata);
        var registry = new Registry(new("100", metadata.ConfigTables.Select(table => new SecurityTableResource(table.Id, table.TableName)).ToArray(),
            metadata.ConfigPages.Select(page => new SecurityPageResource(page.Id, page.ConfigTableId, "100", "9", true, true, true, true, true, true)).ToArray(),
            metadata.ConfigMenus.Select(menu => new SecurityMenuResource(menu.Id, "100", "9")).ToArray(),
            metadata.ConfigButtons.Select(button => new SecurityButtonResource(button.GetProperty("id").GetString()!, button.GetProperty("configPageId").GetString()!)).ToArray()));
        var pages = metadata.ConfigPages.Select(page => Row(("yz_role_id", 5L), ("config_page_id", page.Id), ("permission_values", "[\"is_all\"]"))).ToArray();
        var original = Evaluate(pages); Assert.Empty(original.MenuIds); Assert.All(metadata.ConfigPages, page => Assert.Contains(page.Id + ":is_all", original.Permissions));
        var id = 1000L; var navigation = GlobalCachedCrossExcelNativePreparation.NavigationGrants(metadata, 5L, () => ++id);
        Assert.Equal(32, navigation.Count); Assert.Equal(32, navigation.Select(row => row["id"]).Distinct().Count());
        Assert.All(navigation, row => { Assert.Equal(100L, row["yz_project_id"]); Assert.Equal(5L, row["yz_role_id"]); Assert.Equal("[]", row["permission_values"]); Assert.False(row.ContainsKey("config_page_id")); Assert.False(row.ContainsKey("config_button_id")); });
        var granted = Evaluate(pages.Concat(navigation).ToArray());
        Assert.Equal(metadata.ConfigMenus.Select(menu => menu.Id).Order(), granted.MenuIds.Order());
        Assert.Equal(original.Permissions.Order(), granted.Permissions.Order()); Assert.Empty(granted.ButtonIds); Assert.False(granted.IsSuper); Assert.False(granted.IsPlatformAdministrator);
        var onlyNavigation = Evaluate(navigation); Assert.Empty(onlyNavigation.Permissions); Assert.Empty(onlyNavigation.ButtonIds);
        var tombstones = navigation.Select(row => new Dictionary<string, object?>(row) { ["delete_no"] = new DateTime(2026, 11, 2) }).ToArray(); Assert.Empty(Evaluate(pages.Concat(tombstones).ToArray()).MenuIds);
        var foreignRole = navigation.Select(row => new Dictionary<string, object?>(row) { ["yz_role_id"] = 6L }).ToArray(); Assert.Empty(Evaluate(pages.Concat(foreignRole).ToArray()).MenuIds);
        Assert.Equal(before, JsonSerializer.Serialize(metadata));

        PrincipalRoleRealm Evaluate(IReadOnlyList<Dictionary<string, object?>> grants)
        {
            var method = typeof(PrincipalRolePolicy).GetMethod("Evaluate", BindingFlags.Static | BindingFlags.NonPublic)!;
            return Assert.IsType<PrincipalRoleRealm>(method.Invoke(null, [registry, Row(("yz_role_ids", "[\"5\"]")), Row(("id", 9L)), 100L, new[] { Row(("id", 5L), ("yz_admin_type_id", 9L), ("is_super", 0)) }, grants, null]));
        }
    }

    /// <summary>夹具导航种子不能因元数据漂移授予其它后台／项目或不存在的页。</summary>
    [Theory]
    [InlineData("project")][InlineData("admin")][InlineData("page")][InlineData("kind")]
    public void Navigation_seed_rejects_resources_outside_the_fixed_catalog(string kind)
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog(); var menu = catalog.ConfigMenus[0];
        switch (kind) { case "project": menu.YzProjectId = "101"; break; case "admin": menu.YzAdminTypeId = "10"; break; case "page": menu.ConfigPageId = "missing"; break; default: menu.Kind = "CUSTOM_PAGE"; break; }
        var allocated = false;
        Assert.Throws<InvalidOperationException>(() => GlobalCachedCrossExcelNativePreparation.NavigationGrants(catalog, 5L, () => { allocated = true; return 1; })); Assert.False(allocated);
    }

    /// <summary>保留实际授权表字段，不自行计算导航或动作许可。</summary>
    private static Dictionary<string, object?> Row(params (string Name, object? Value)[] values) => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
    private sealed class Registry(SecurityResourceCatalog catalog) : ISecurityResourceRegistry
    {
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs => [catalog];
        public bool IsPlatformRealm(string projectId, string adminTypeId) => false;
    }
}