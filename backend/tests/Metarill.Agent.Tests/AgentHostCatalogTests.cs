using Metarill.Core.Metadata;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>固定发布目录与真实身份域分别约束独立Agent，测试不打开数据库或伪造平台身份。</summary>
public sealed class AgentHostCatalogTests
{
    /// <summary>普通角色、外项目及未发布后台不能批准；本应用超级角色可以批准。</summary>
    [Fact]
    public void Application_scope_requires_its_published_realm_and_current_super_role()
    {
        var catalog = AgentHostCatalog.ForApplication(Catalog("100", "31", "21"));
        Assert.True(catalog.CanApprove(Realm("100", "31", true)));
        Assert.False(catalog.CanApprove(Realm("100", "31", false)));
        Assert.False(catalog.CanApprove(Realm("200", "31", true, true)));
        Assert.False(catalog.CanApprove(Realm("100", "32", true, true)));
        // 后续业务权限按当前角色计算；失去超级角色本身不把已签发身份移到另一个域。
        Assert.True(catalog.IsEligibleRealm(Realm("100", "31", false)));
        Assert.False(catalog.IsEligibleRealm(Realm("100", "32", true)));
        Assert.Equal("100", catalog.PendingProjectId);
    }

    /// <summary>平台保留原平台管理员批准、平台身份续签规则，不把普通应用super变成平台管理员。</summary>
    [Fact]
    public void Platform_approval_and_renewal_keep_original_realm_rules()
    {
        var catalog = AgentHostCatalog.ForPlatform(Catalog("100", "31", "21"), Catalog("200", "32", "22"));
        Assert.True(catalog.CanApprove(Realm("100", "31", true, true)));
        Assert.False(catalog.CanApprove(Realm("100", "31", false, true)));
        Assert.False(catalog.CanApprove(Realm("200", "32", true)));
        Assert.True(catalog.IsEligibleRealm(Realm("100", "31", false, true)));
        Assert.False(catalog.IsEligibleRealm(Realm("200", "32", true)));
        Assert.Equal("批准Agent授权需要平台管理员权限。", catalog.ApprovalPermissionMessage);
        Assert.Equal("200", catalog.FindPage("22")!.ProjectId);
    }

    /// <summary>原必填/默认值及parent判断冻结后不受调用方对象或返回列表的后续修改影响。</summary>
    [Fact]
    public void Published_page_snapshot_is_deeply_readonly_and_keeps_existing_required_semantics()
    {
        var source = Catalog("100", "31", "21"); source.ConfigPages[0].WhereSql = "parent_id=@parentId";
        var catalog = AgentHostCatalog.ForApplication(source); var page = catalog.FindPage("21")!;
        Assert.Equal("work_item", page.TableName); Assert.Equal(new[] { "displayName" }, page.RequiredCreateFields); Assert.True(page.RequiresParent);
        source.ConfigPages[0].Name = "调用者后改"; source.ConfigPages[0].Id = "99"; source.ConfigColumns.Single(column => column.Id == "12").DefaultValue = "后改默认";
        source.YzAdminTypes[0].Id = "32";
        Assert.Equal("事项", catalog.FindPage("21")!.Name); Assert.Null(catalog.FindPage("99"));
        Assert.Equal(new[] { "displayName" }, page.RequiredCreateFields);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)page.RequiredCreateFields).Add("hidden"));
        Assert.True(catalog.CanApprove(Realm("100", "31", true))); Assert.False(catalog.CanApprove(Realm("100", "32", true)));
    }

    /// <summary>原create仅在无默认值时增加必填；读写控制不由发布快照额外扩张。</summary>
    [Fact]
    public void Defaults_and_readonly_fields_do_not_become_new_required_inputs()
    {
        var source = Catalog("100", "31", "21"); source.ConfigColumns.Single(column => column.Id == "12").DefaultValue = "默认";
        Assert.Empty(AgentHostCatalog.ForApplication(source).FindPage("21")!.RequiredCreateFields);
        source.ConfigColumns.Single(column => column.Id == "12").DefaultValue = ""; source.ConfigPageColumns[0].IsEdit = 0;
        Assert.Empty(AgentHostCatalog.ForApplication(source).FindPage("21")!.RequiredCreateFields);
    }

    /// <summary>页面ID跨发布项目重复不能静默命中第一份目录。</summary>
    [Fact]
    public void Duplicate_page_identity_is_rejected_before_indexing_routes()
    {
        Assert.Throws<InvalidOperationException>(() => AgentHostCatalog.ForPlatform(Catalog("100", "31", "21"), Catalog("200", "32", "21")));
    }

    /// <summary>外项目所有的同名后台不进入应用签发域。</summary>
    [Fact]
    public void Foreign_owned_admin_does_not_join_application_issuer_scope()
    {
        var source = Catalog("100", "31", "21"); source.YzAdminTypes[0].YzProjectId = "200";
        Assert.False(AgentHostCatalog.ForApplication(source).CanApprove(Realm("100", "31", true)));
    }

    /// <summary>仅提供已保存的静态模型，不用HTTP客户端参数构造权限。</summary>
    private static MetadataCatalog Catalog(string project, string admin, string page) => new()
    {
        YzProjectId = project, YzAdminTypes = [new() { Id = admin, YzProjectId = project }],
        ConfigTables = [new() { Id = "1", TableName = "work_item" }],
        ConfigColumns = [new() { Id = "11", ConfigTableId = "1", ColumnName = "id", LogicalType = "id" }, new() { Id = "12", ConfigTableId = "1", ColumnName = "display_name", LogicalType = "string" }],
        ConfigPages = [new() { Id = page, ConfigTableId = "1", Name = "事项" }],
        ConfigPageColumns = [new() { Id = "41", ConfigPageId = page, ConfigColumnId = "12", IsEdit = 1, IsRequired = 1 }]
    };
    /// <summary>测试输入模拟共享策略已从真实角色/后台加载的判定结果，未改AuthenticationService。</summary>
    private static PrincipalRoleRealm Realm(string project, string admin, bool super, bool platform = false) =>
        new(admin, project, super, platform, platform && super, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
}
