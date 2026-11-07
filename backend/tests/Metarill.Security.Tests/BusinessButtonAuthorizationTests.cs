using System.Security;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Xunit;
namespace Metarill.Security.Tests;

/// <summary>真实按钮授权与数据范围入口的反例，不用页面CRUD权限代替独立按钮。</summary>
public sealed class BusinessButtonAuthorizationTests
{
    /// <summary>按钮单独授权即可使用；同页query普通权限不能取消范围，普通/Agent/业务super遵循同一按钮集合。</summary>
    [Theory][InlineData("web")][InlineData("agent")][InlineData("super")]
    public void Actual_button_is_independent_from_page_permissions(string kind)
    {
        var identity=Identity() with{GrantId=kind=="agent"?3:null,IsRealmSuper=kind=="super"};
        BusinessButtonAuthorization.Require(identity,[Catalog()],"100","300",false);
        var scope=ExactDataManageIdentity.ResolveButton(identity,"300","realName");Assert.True(scope.IsRequired);Assert.Equal(new long[]{9007199254740993},scope.Read<long>());
        Assert.False(ExactDataManageIdentity.Resolve(identity,"100","detail","realName").IsRequired);
    }
    /// <summary>身份漂移、未授权、按钮换页、未发布和目录冲突均拒绝，不因super或页面is_all放行。</summary>
    [Theory][InlineData("project")][InlineData("admin")][InlineData("principal")][InlineData("revoked")][InlineData("super-only")]
    [InlineData("unpublished")][InlineData("moved")][InlineData("duplicate-button")][InlineData("duplicate-page")][InlineData("catalog-project")][InlineData("platform")]
    public void Invalid_identity_or_binding_never_bypasses_button(string defect)
    {
        var identity=Identity();var c=Catalog();
        identity=defect switch{
            "project"=>identity with{Actor=identity.Actor with{ProjectId="2"}},
            "admin"=>identity with{Actor=identity.Actor with{AdminTypeId="11"}},
            "principal"=>identity with{PrincipalProjectId="2"},
            "revoked"=>identity with{ButtonIds=new HashSet<string>(),Permissions=new HashSet<string>{"100:is_all"}},
            "super-only"=>identity with{IsRealmSuper=true,ButtonIds=new HashSet<string>()},
            "platform"=>identity with{Actor=identity.Actor with{IsAdministrator=true}},_=>identity};
        c=defect switch{
            "unpublished"=>c with{Buttons=[]},"moved"=>c with{Buttons=[new("300","200")]},
            "duplicate-button"=>c with{Buttons=[new("300","100"),new("300","100")]},
            "duplicate-page"=>c with{Pages=[c.Pages[0],c.Pages[0]]},"catalog-project"=>c with{ProjectId="2"},_=>c};
        Assert.Throws<SecurityException>(()=>BusinessButtonAuthorization.Require(identity,[c],"100","300",false));
    }
    /// <summary>平台宿主保留原管理员权限，但仍核验真实按钮/页面绑定；应用宿主拒绝此身份。</summary>
    [Fact]
    public void Platform_privilege_does_not_create_an_unpublished_button()
    {
        var identity=Identity() with{Actor=Identity().Actor with{IsAdministrator=true,ProjectId="2"},ButtonIds=new HashSet<string>()};
        BusinessButtonAuthorization.Require(identity,[Catalog()],"100","300",true);
        Assert.Throws<SecurityException>(()=>BusinessButtonAuthorization.Require(identity,[Catalog()],"100","300",false));
        Assert.Throws<SecurityException>(()=>BusinessButtonAuthorization.Require(identity,[Catalog()],"100","999",true));
        Assert.Throws<SecurityException>(()=>BusinessButtonAuthorization.Require(identity,[Catalog()],"200","300",true));
    }
    /// <summary>账号值缺失必须不匹配；错误的已发布账号字段、项目或后台不能借按钮解析解除限制。</summary>
    [Fact]
    public void Button_scope_keeps_missing_value_and_claim_provenance()
    {
        var identity=Identity();Assert.Throws<NotSupportedException>(()=>ExactDataManageIdentity.ResolveButton(identity,"300","other"));
        Assert.Throws<NotSupportedException>(()=>ExactDataManageIdentity.ResolveButton(identity with{Actor=identity.Actor with{ProjectId="2"}},"300","real_name"));
        var missing=ExactDataManageIdentity.ResolveButton(identity with{ExactDataManageMatchValue=null},"300","real_name");Assert.True(missing.IsRequired);Assert.Empty(missing.Read<long>());
        var disabled=ExactDataManageIdentity.ResolveButton(identity,"300","");Assert.True(disabled.IsRequired);Assert.Empty(disabled.Read<long>());
        Assert.False(ExactDataManageIdentity.ResolveButton(identity with{ExactDataManageButtonIds=new HashSet<string>()},"300","real_name").IsRequired);
    }
    /// <summary>旧宿主不能因接口扩展自动提供权限、范围或开发身份后门。</summary>
    [Fact]
    public void Old_authorizers_fail_closed_for_both_new_entries()
    {
        IRuntimeAuthorization authorization=new Legacy();Assert.Throws<NotSupportedException>(()=>authorization.RequireButtonPermission("100","300"));Assert.Throws<NotSupportedException>(()=>authorization.ResolveButtonExactDataManage("100","300","real_name"));
    }
    /// <summary>只声明当前项目后台资源，页面未开启任何标准写动作。</summary>
    private static SecurityResourceCatalog Catalog()=>new("1",[new("7","records")],[new("100","7","1","10",false,false,false,false)],[],[new("300","100")]);
    /// <summary>真实认证之后的窄上下文，按钮范围与页面普通query授权刻意冲突。</summary>
    private static AuthenticatedIdentity Identity()=>new(new(1,"账号",false,"1","10"),new HashSet<string>{"100:is_query"},2,null)
    {PrincipalProjectId="1",PrincipalAdminTypeId="10",ButtonIds=new HashSet<string>{"300"},ExactDataManageButtonIds=new HashSet<string>{"300"},ExactDataManageMatchField="real_name",ExactDataManageMatchValue=JsonSerializer.SerializeToElement("9007199254740993")};
    private sealed class Legacy:IRuntimeAuthorization
    {
        /// <summary>仅旧身份合同，没有按钮语义。</summary>
        public RuntimeActor RequireActor()=>new(1,"旧宿主",true,"1","10");
        /// <summary>已有CRUD放行不能代替新按钮边界。</summary>
        public void RequirePagePermission(string pageId,string operation){}
    }
}
