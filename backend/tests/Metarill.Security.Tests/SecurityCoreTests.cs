using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>只引用共享安全工程，验证离开平台配置与Agent程序集仍可执行真实权限决策。</summary>
public sealed class SecurityCoreTests
{
    /// <summary>类型确实来自新工程，且编译闭包没有借平台Runtime间接提供实现。</summary>
    [Fact]
    public void Authentication_and_decision_are_in_the_independent_core_data_closure()
    {
        var assembly=typeof(AuthenticationService).Assembly;
        Assert.Equal("Metarill.Security",assembly.GetName().Name);
        Assert.Equal(assembly,typeof(PrincipalRolePolicy).Assembly);
        Assert.Equal(assembly,typeof(PasswordHasher).Assembly);
        Assert.Equal(assembly,typeof(AuthenticatedIdentity).Assembly);
        Assert.Equal(assembly,typeof(SecurityRealmCurrentReader).Assembly);Assert.Equal(assembly,typeof(SecurityRealmMutationGuard).Assembly);
        var references=assembly.GetReferencedAssemblies().Select(value=>value.Name).ToArray();
        Assert.Contains("Metarill.Core",references);Assert.Contains("Metarill.Data",references);
        Assert.DoesNotContain(references,name=>name is not null&&name.StartsWith("Metarill.",StringComparison.Ordinal)&&name is not ("Metarill.Core" or "Metarill.Data"));
    }

    /// <summary>扩展合同只有实际绑定三值，不让宿主扩展接触账号/会话的多余秘密字段。</summary>
    [Fact]
    public void Session_extension_receives_only_the_validated_fixed_binding()
    {
        Assert.Equal(new[]{"AccountId","GrantId","ProjectId"},typeof(SecuritySessionBinding).GetProperties().Select(property=>property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(long),typeof(SecuritySessionBinding).GetProperty("GrantId")!.PropertyType);
        Assert.Equal(typeof(long),typeof(SecuritySessionBinding).GetProperty("AccountId")!.PropertyType);
        Assert.Equal(typeof(string),typeof(SecuritySessionBinding).GetProperty("ProjectId")!.PropertyType);
        var method=typeof(ISecuritySessionPolicy).GetMethod("ValidateAgentSessionAsync")!;
        Assert.Equal(new[]{typeof(SecuritySessionBinding),typeof(CancellationToken)},method.GetParameters().Select(parameter=>parameter.ParameterType));
    }

    /// <summary>同后台普通授权按页面动作取交集，外后台角色、页面、菜单和按钮不参与。</summary>
    [Fact]
    public void Same_realm_grants_keep_only_published_resources_and_enabled_actions()
    {
        var catalog=Catalog();
        var roles=new[]{Row(("id",5L),("yz_admin_type_id",10L),("is_super",0)),Row(("id",6L),("yz_admin_type_id",20L),("is_super",1))};
        var grants=new[]{Row(("id",1L),("yz_role_id",5L),("config_page_id",100L),("permission_values","[\"is_query\",\"is_update\",\"is_new\",\"unknown\"]")),
            Row(("id",2L),("yz_role_id",6L),("config_page_id",200L),("permission_values","[\"is_all\"]")),
            Row(("id",3L),("yz_role_id",5L),("config_menu_id",21L),("config_button_id",31L))};
        var realm=PrincipalRolePolicy.Evaluate(new Registry(catalog),Row(("yz_role_ids","[\"5\",\"6\"]")),Row(("id",10L)),1L,roles,grants);
        Assert.False(realm.IsSuper);Assert.False(realm.IsPlatformAdministrator);
        Assert.Equal(new[]{"100:is_new","100:is_query"},realm.Permissions.Order(StringComparer.Ordinal));
        Assert.Empty(realm.MenuIds);Assert.Empty(realm.ButtonIds);Assert.Equal(new[]{"5"},realm.RoleIds);
    }

    /// <summary>当前目录不借已发布旧动作补权，也不接受相同ID换物理表或把按钮换绑另页。</summary>
    [Theory]
    [InlineData("actions")][InlineData("table")][InlineData("button")]
    public void Current_resources_must_still_match_the_published_identity(string change)
    {
        var published=Catalog();var current=published with {Menus=[]};
        if(change=="actions")current=current with{Pages=[published.Pages[0] with{CanCreate=false}]};
        if(change=="table")current=current with{Tables=[published.Tables[0] with{TableName="other_table"}]};
        if(change=="button")current=current with{Buttons=[new("30","200")]};
        var realm=PrincipalRolePolicy.Evaluate(new Registry(published),Row(("yz_role_ids","[\"5\"]")),Row(("id",10L)),1L,
            [Row(("id",5L),("yz_admin_type_id",10L),("is_super",1))],[],current);
        if(change=="actions")Assert.DoesNotContain("100:is_new",realm.Permissions);
        if(change=="table")Assert.Empty(realm.Permissions);
        if(change=="button")Assert.Empty(realm.ButtonIds);
        Assert.Empty(realm.MenuIds);
    }

    /// <summary>合法后台super仍须宿主证明平台realm；兼容项目列和角色super不能生成平台特权。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Realm_super_is_separate_from_host_platform_administrator(bool platformRealm)
    {
        var realm=PrincipalRolePolicy.Evaluate(new Registry(Catalog(),platformRealm),Row(("yz_role_ids","[\"5\"]"),("yz_project_id",999L)),Row(("id",10L)),1L,
            [Row(("id",5L),("yz_admin_type_id",10L),("is_super",1),("yz_project_id",999L))],[]);
        Assert.True(realm.IsSuper);Assert.Equal(platformRealm,realm.IsPlatformAdministrator);
        Assert.Equal("1",realm.ProjectId);Assert.Equal("10",realm.AdminTypeId);
    }

    /// <summary>长ID保留字符串且损坏集合整体不授权，不把浮点、前导零或对象转成角色ID。</summary>
    [Theory]
    [InlineData("[\"9007199254740993\"]",true)]
    [InlineData("[9007199254740993]",true)]
    [InlineData("[\"09007199254740993\"]",false)]
    [InlineData("[9007199254740993.0]",false)]
    [InlineData("[\"9007199254740993\",{}]",false)]
    public void Role_id_precision_and_whole_document_rejection_are_preserved(string encoded,bool allowed)
    {
        var realm=PrincipalRolePolicy.Evaluate(new Registry(Catalog()),Row(("yz_role_ids",encoded)),Row(("id",10L)),1L,
            [Row(("id",9007199254740993L),("yz_admin_type_id",10L),("is_super",1))],[]);
        Assert.Equal(allowed,realm.IsSuper);Assert.Equal(allowed,realm.RoleIds.Contains("9007199254740993"));
    }

    /// <summary>最小发布资源夹具不含字段、SQL、连接、菜单布局或任何平台元数据文件。</summary>
    private static SecurityResourceCatalog Catalog()=>new("1",[new("7","business_table")],
        [new("100","7","1","10",true,false,true,false),new("200","7","1","20",true,true,true,true)],
        [new("20","1","10"),new("21","1","20")],[new("30","100"),new("31","200")]);
    private static Dictionary<string,object?> Row(params (string Key,object? Value)[] values)=>values.ToDictionary(value=>value.Key,value=>value.Value,StringComparer.Ordinal);
    private sealed class Registry(SecurityResourceCatalog catalog,bool platform=false):ISecurityResourceRegistry
    {
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[catalog];
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>platform&&projectId=="1"&&adminTypeId=="10";
    }
}
