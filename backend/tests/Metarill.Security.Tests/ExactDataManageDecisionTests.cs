using System.Text.Json;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>对照原V8：范围控制按实际授予当前动作的角色取交集，不是账号具有任意范围角色即限制。</summary>
public sealed class ExactDataManageDecisionTests
{
    /// <summary>普通角色只有确实授予当前页/动作时才解除范围；is_all参与每个动作，角色/授权顺序不影响结果。</summary>
    [Theory]
    [InlineData("is_query","",true,false)]
    [InlineData("is_query","is_new",true,false)]
    [InlineData("is_query","is_query",false,false)]
    [InlineData("is_query","is_all",false,false)]
    [InlineData("is_all","",true,true)]
    [InlineData("is_all","is_query",false,true)]
    [InlineData("is_all","is_new",true,true)]
    [InlineData("is_all","is_update",true,false)]
    [InlineData("is_update","is_query",false,true)]
    [InlineData("is_all","is_all",false,false)]
    public void Scope_decision_uses_only_effective_operation_grants(string scoped,string ordinary,bool queryExact,bool updateExact)
    {
        var roles=new[]{Role(5,1),Role(6,0)};var grants=new[]{Grant(5,scoped),Grant(6,ordinary)};
        foreach(var reverse in new[]{false,true})
        {
            var realm=Evaluate(reverse?roles.Reverse().ToArray():roles,reverse?grants.Reverse().ToArray():grants);
            Assert.Equal(queryExact,realm.ExactDataManagePermissions.Contains("100:is_query"));
            Assert.Equal(updateExact,realm.ExactDataManagePermissions.Contains("100:is_update"));
            if(scoped!="is_all"&&ordinary!="is_all")Assert.DoesNotContain("100:is_all",realm.ExactDataManagePermissions);
            Assert.All(realm.ExactDataManagePermissions,key=>Assert.StartsWith("100:",key));
        }
    }
    /// <summary>外后台、已删角色、已删授权、坏JSON、未发布页和仅菜单授权均不能解除当前精确范围。</summary>
    [Theory]
    [InlineData("foreign-role")][InlineData("deleted-role")][InlineData("deleted-grant")]
    [InlineData("bad-json")][InlineData("other-page")][InlineData("menu-only")]
    public void Invalid_or_unrelated_ordinary_grant_does_not_remove_scope(string scenario)
    {
        var roles=new[]{Role(5,1),Role(6,0)};var grants=new[]{Grant(5,"is_query"),Grant(6,"is_query")};
        switch(scenario)
        {
            case "foreign-role":roles[1]["yz_admin_type_id"]=20L;break;
            case "deleted-role":roles[1]["delete_no"]=DateTime.UtcNow;break;
            case "deleted-grant":grants[1]["delete_no"]=DateTime.UtcNow;break;
            case "bad-json":grants[1]["permission_values"]="[{}]";break;
            case "other-page":grants[1]["config_page_id"]=999L;break;
            case "menu-only":grants[1].Remove("config_page_id");grants[1]["config_menu_id"]=20L;break;
        }
        var realm=Evaluate(roles,grants);Assert.Contains("100:is_query",realm.Permissions);Assert.Equal(new[]{"100:is_query"},realm.ExactDataManagePermissions);
    }
    /// <summary>后台super沿原规则不做精确过滤；默认/缺失is_scope角色仍是普通角色，旧构造不丢参数。</summary>
    [Fact]
    public void Super_and_missing_scope_flag_keep_original_behavior_and_serialization()
    {
        var super=Role(5,1);super["is_super"]=1;Assert.Empty(Evaluate([super],[Grant(5,"is_all")]).ExactDataManagePermissions);
        var ordinary=Role(6,0);ordinary.Remove("is_scope");var realm=Evaluate([Role(5,1),ordinary],[Grant(5,"is_query"),Grant(6,"is_query")]);Assert.Empty(realm.ExactDataManagePermissions);
        var actor=new Metarill.Core.Security.RuntimeActor(1,"账号",false,"1","10");
        var identity=new AuthenticatedIdentity(actor,realm.Permissions,2,null){ExactDataManagePermissions=new HashSet<string>{"100:is_query"}};
        Assert.False(JsonSerializer.SerializeToElement(identity,new JsonSerializerOptions(JsonSerializerDefaults.Web)).TryGetProperty("exactDataManagePermissions",out _));
        Assert.NotNull(typeof(AuthenticatedIdentity).GetConstructor([typeof(Metarill.Core.Security.RuntimeActor),typeof(IReadOnlySet<string>),typeof(long),typeof(long?)]));
    }
    /// <summary>不授予的动作即便是is_scope角色也不能凭空得到范围或页面权限。</summary>
    [Fact]
    public void Missing_or_disabled_operation_never_creates_authorization()
    {
        var catalog=Catalog() with{Pages=[Catalog().Pages[0] with{CanUpdate=false}]};
        var realm=PrincipalRolePolicy.Evaluate(new Registry(catalog),Row(("yz_role_ids","[\"5\",\"6\"]")),Row(("id",10L)),1,[Role(5,1)],[Grant(5,"is_update")]);
        Assert.Empty(realm.Permissions);Assert.Empty(realm.ExactDataManagePermissions);
    }
    /// <summary>真实策略入口使用同后台且已发布资源，测试不复制生产聚合算法。</summary>
    private static PrincipalRoleRealm Evaluate(IReadOnlyList<Dictionary<string,object?>> roles,IReadOnlyList<Dictionary<string,object?>> grants)=>
        PrincipalRolePolicy.Evaluate(new Registry(Catalog()),Row(("yz_role_ids","[\"5\",\"6\"]")),Row(("id",10L)),1,roles,grants);
    /// <summary>最小发布页开放四个标准动作，不引入平台字段或连接。</summary>
    private static SecurityResourceCatalog Catalog()=>new("1",[new("7","records")],[new("100","7","1","10",true,true,true,true)],[],[]);
    /// <summary>角色夹具固定真实后台，范围标志独立于super。</summary>
    private static Dictionary<string,object?> Role(long id,int scope)=>Row(("id",id),("yz_admin_type_id",10L),("is_scope",scope),("is_super",0));
    /// <summary>授权使用真实permission_values编码，空动作不虚构权限。</summary>
    private static Dictionary<string,object?> Grant(long role,string action)=>Row(("yz_role_id",role),("config_page_id",100L),("permission_values",JsonSerializer.Serialize(action.Length==0?Array.Empty<string>():new[]{action})));
    /// <summary>仅构造策略读取的窄字典，省略字段保持缺失。</summary>
    private static Dictionary<string,object?> Row(params (string Key,object? Value)[] values)=>values.ToDictionary(value=>value.Key,value=>value.Value,StringComparer.Ordinal);
    private sealed class Registry(SecurityResourceCatalog catalog):ISecurityResourceRegistry
    {
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[catalog];
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
    }
}
