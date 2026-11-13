using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Xunit;
namespace Metarill.Security.Tests;

/// <summary>显式业务动作接线前，先保存原按钮的逐资源范围决策；不把该决策当成已执行记录过滤。</summary>
public sealed class BusinessButtonScopeDecisionTests
{
    /// <summary>普通角色只有确实授予同一按钮时才能解除限制；授权/角色顺序无关。</summary>
    [Theory][InlineData("none",true)][InlineData("same",false)][InlineData("other",true)][InlineData("query",true)][InlineData("all",true)][InlineData("menu",true)]
    public void Only_the_same_button_unrestricted_grant_removes_its_scope(string ordinary,bool required)
    {
        var roles=new[]{Role(5,1),Role(6,0)};var grants=new List<Dictionary<string,object?>>{Button(5,300)};
        if(ordinary=="same")grants.Add(Button(6,300));if(ordinary=="other")grants.Add(Button(6,301));if(ordinary is "query" or "all")grants.Add(Row(("yz_role_id",6L),("config_page_id",100L),("permission_values",ordinary=="all"?"[\"is_all\"]":"[\"is_query\"]")));if(ordinary=="menu")grants.Add(Row(("yz_role_id",6L),("config_menu_id",20L)));
        foreach(var reverse in new[]{false,true})
        {
            var realm=Evaluate(reverse?roles.Reverse().ToArray():roles,reverse?grants.AsEnumerable().Reverse().ToArray():grants);Assert.Contains("300",realm.ButtonIds);Assert.Equal(required,realm.ExactDataManageButtonIds.Contains("300"));Assert.DoesNotContain("301",realm.ExactDataManageButtonIds);
            if(ordinary=="none"){Assert.Empty(realm.Permissions);Assert.Equal("real_name",realm.ExactDataManageMatchField);Assert.Equal("内部匹配值",realm.ExactDataManageMatchValue!.Value.GetString());}
        }
    }
    /// <summary>无效普通授权不能解除范围，未发布或当前已换页的按钮不能产生授权或范围。</summary>
    [Theory][InlineData("foreign-role")][InlineData("deleted-role")][InlineData("deleted-grant")][InlineData("unassigned-role")][InlineData("different-button")][InlineData("unpublished")][InlineData("moved-page")]
    public void Invalid_grants_or_resource_drift_never_widen_button_scope(string defect)
    {
        var roles=new[]{Role(5,1),Role(6,0)};var grants=new[]{Button(5,300),Button(6,300)};SecurityResourceCatalog? current=null;
        if(defect=="foreign-role")roles[1]["yz_admin_type_id"]=11L;if(defect=="deleted-role")roles[1]["delete_no"]=DateTime.UtcNow;if(defect=="deleted-grant")grants[1]["delete_no"]=DateTime.UtcNow;if(defect=="unassigned-role")grants[1]["yz_role_id"]=7L;if(defect=="different-button")grants[1]["config_button_id"]=301L;if(defect=="unpublished"){grants[0]["config_button_id"]=999L;grants[1]["config_button_id"]=999L;}if(defect=="moved-page")current=Catalog() with{Buttons=[new("300","200"),new("301","100")]};
        var realm=Evaluate(roles,grants,current);if(defect is "unpublished" or "moved-page"){Assert.DoesNotContain("300",realm.ButtonIds);Assert.Empty(realm.ExactDataManageButtonIds);}else Assert.Equal(new[]{"300"},realm.ExactDataManageButtonIds);
    }
    /// <summary>后台super保持原无限制；身份上下文隐藏范围集合及账号值，不改变构造合同。</summary>
    [Fact]
    public void Super_and_serialization_keep_existing_contracts()
    {
        var role=Role(5,1);role["is_super"]=1;var super=Evaluate([role],[]);Assert.Equal(2,super.ButtonIds.Count);Assert.Empty(super.ExactDataManageButtonIds);
        var realm=Evaluate([Role(5,1)],[Button(5,300)]);var identity=Identity().WithPermissionContext(realm);var json=JsonSerializer.Serialize(identity,new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.DoesNotContain("exactDataManageButtonIds",json);Assert.DoesNotContain("内部匹配值",json);Assert.Contains("300",identity.ExactDataManageButtonIds);Assert.NotNull(typeof(AuthenticatedIdentity).GetConstructor([typeof(RuntimeActor),typeof(IReadOnlySet<string>),typeof(long),typeof(long?)]));
    }
    /// <summary>同一生产投影用于认证及锁后复核；撤权清空旧决策，原会话/Actor/匹配来源不被串改。</summary>
    [Fact]
    public void Permission_projection_refreshes_and_clears_buttons_without_replacing_identity()
    {
        var source=Identity();var realm=Evaluate([Role(5,1)],[Button(5,300)]);var current=source.WithPermissionContext(realm);Assert.Equal(source.Actor,current.Actor);Assert.Equal(source.SessionId,current.SessionId);Assert.Equal(source.GrantId,current.GrantId);Assert.Empty(source.ExactDataManageButtonIds);Assert.Contains("300",current.ExactDataManageButtonIds);
        var revoked=current.WithPermissionContext(Evaluate([Role(5,1)],[]));Assert.Empty(revoked.ButtonIds);Assert.Empty(revoked.ExactDataManageButtonIds);Assert.Equal("",revoked.ExactDataManageMatchField);Assert.Null(revoked.ExactDataManageMatchValue);Assert.Contains("300",current.ExactDataManageButtonIds);
        Assert.Throws<UnauthorizedAccessException>(()=>source.WithPermissionContext(realm with{ProjectId="2"}));Assert.Throws<UnauthorizedAccessException>(()=>source.WithPermissionContext(realm with{AdminTypeId="11"}));Assert.Throws<UnauthorizedAccessException>(()=>source.WithPermissionContext(realm with{IsSuper=true}));Assert.Throws<UnauthorizedAccessException>(()=>source.WithPermissionContext(realm with{IsPlatformAdministrator=true}));
    }
    /// <summary>平台工作区选择仍与主体分离；此内部投影不能把Actor改成角色记录的项目。</summary>
    [Fact]
    public void Platform_workspace_actor_is_preserved_when_principal_realm_is_unchanged()
    {
        var realm=Evaluate([Role(5,0)],[]) with{IsSuper=true,IsPlatformAdministrator=true,IsPlatformRealm=true};var identity=Identity() with{Actor=new RuntimeActor(1,"账号",true,"2","10"),IsRealmSuper=true};Assert.Equal("2",identity.WithPermissionContext(realm).Actor.ProjectId);
    }
    /// <summary>真实策略入口只接受同后台有效角色和已发布资源。</summary>
    private static PrincipalRoleRealm Evaluate(IReadOnlyList<Dictionary<string,object?>> roles,IReadOnlyList<Dictionary<string,object?>> grants,SecurityResourceCatalog? current=null)=>PrincipalRolePolicy.Evaluate(new Registry(),Row(("yz_role_ids","[\"5\",\"6\"]"),("real_name","内部匹配值")),Row(("id",10L),("exact_data_manage_match_field","real_name")),1,roles,grants,current);
    /// <summary>两个页面及两个独立按钮，便于验证角色与当前目录交集。</summary>
    private static SecurityResourceCatalog Catalog()=>new("1",[new("7","records"),new("8","other_records")],[new("100","7","1","10",true,true,true,true),new("200","8","1","10",true,true,true,true)],[new("20","1","10")],[new("300","100"),new("301","100")]);
    /// <summary>主体与会话固定，权限更新只能替换窄决策。</summary>
    private static AuthenticatedIdentity Identity()=>new(new RuntimeActor(1,"登录名",false,"1","10"),new HashSet<string>(),2,3){PrincipalProjectId="1",PrincipalAdminTypeId="10"};
    /// <summary>范围角色与超级角色独立，均明确属于当前后台。</summary>
    private static Dictionary<string,object?> Role(long id,int scope)=>Row(("id",id),("yz_admin_type_id",10L),("is_scope",scope),("is_super",0));
    /// <summary>按钮授权使用当前独立资源列，不猜页面动作。</summary>
    private static Dictionary<string,object?> Button(long role,long button)=>Row(("yz_role_id",role),("config_button_id",button));
    /// <summary>仅构造生产策略真实读取的字段。</summary>
    private static Dictionary<string,object?> Row(params(string Key,object? Value)[] values)=>values.ToDictionary(value=>value.Key,value=>value.Value,StringComparer.Ordinal);
    private sealed class Registry:ISecurityResourceRegistry
    {
        /// <summary>本夹具只发布当前目录，不访问数据库。</summary>
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[Catalog()];
        /// <summary>应用后台super不产生平台管理员身份。</summary>
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
    }
}
