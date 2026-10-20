using System.Security;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>直接执行真实Full包编译出的授权类，不重建近似授权；此处仅提供已认证请求上下文，不启动HTTP或数据库。</summary>
    [Theory]
    [InlineData("create","is_new")] [InlineData("update","is_update")]
    public void Real_full_role_option_authorization_accepts_only_create_or_only_update(string action,string permission)
    {
        var page=package.Metadata.ConfigPages[0];
        var identity=RoleOptionIdentity(new HashSet<string>{page.Id+":"+permission});
        var auth=RealRoleOptionAuthorization(identity);
        Assert.Throws<SecurityException>(()=>auth.RequirePagePermission(page.Id,"list"));
        auth.RequirePagePermission(page.Id,action);
        Assert.Equal(identity.Actor,RoleCheckboxAuthorization.Require(auth,page.Id,true,true));
    }

    /// <summary>原Full明确无权保持SecurityException供现有403映射使用；匿名仍保持Unauthorized供401映射使用。</summary>
    [Fact]
    public void Real_full_role_option_denials_preserve_forbidden_and_missing_identity_semantics()
    {
        var page=package.Metadata.ConfigPages[0];
        var error=Assert.Throws<SecurityException>(()=>RoleCheckboxAuthorization.Require(RealRoleOptionAuthorization(RoleOptionIdentity(new HashSet<string>())),page.Id,true,true));
        Assert.Equal("当前页面未授予查询、新增或修改权限。",error.Message);Assert.Null(error.InnerException);
        Assert.Throws<UnauthorizedAccessException>(()=>RoleCheckboxAuthorization.Require(RealRoleOptionAuthorization(null),page.Id,true,true));
        // 元数据关闭对应动作时不能因角色带有该动作权限而尝试它。
        var identity=RoleOptionIdentity(new HashSet<string>{page.Id+":is_new",page.Id+":is_update"});
        Assert.Throws<SecurityException>(()=>RoleCheckboxAuthorization.Require(RealRoleOptionAuthorization(identity),page.Id,false,false));
    }

    /// <summary>已有真实Full的项目/后台限制不能被候选任一动作规则绕开。</summary>
    [Theory]
    [InlineData("project")] [InlineData("admin")]
    public void Real_full_role_option_authorization_keeps_original_realm_boundary(string change)
    {
        var page=package.Metadata.ConfigPages[0];var identity=RoleOptionIdentity(new HashSet<string>{page.Id+":is_all"});
        identity=change=="project"?identity with{PrincipalProjectId="42",Actor=identity.Actor with{ProjectId="42"}}:identity with{PrincipalAdminTypeId="42",Actor=identity.Actor with{AdminTypeId="42"}};
        Assert.Throws<SecurityException>(()=>RoleCheckboxAuthorization.Require(RealRoleOptionAuthorization(identity),page.Id,true,true));
    }

    /// <summary>身份仅是当前测试请求输入；真正执行的权限实现来自固定完整包程序集。</summary>
    private AuthenticatedIdentity RoleOptionIdentity(IReadOnlySet<string> permissions)
    {
        var page=package.Metadata.ConfigPages[0];
        return new(new(8,"离线Full授权",false,package.Metadata.YzProjectId,page.YzAdminTypeId),permissions,99,null)
        {PrincipalProjectId=package.Metadata.YzProjectId,PrincipalAdminTypeId=page.YzAdminTypeId};
    }
    /// <summary>实际包构造器仅依赖上下文和已验编译目录，不注册数据库替身或近似授权类。</summary>
    private IRuntimeAuthorization RealRoleOptionAuthorization(AuthenticatedIdentity? identity)
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        if(identity is not null)accessor.HttpContext.Items[typeof(AuthenticatedIdentity)]=identity;
        return(IRuntimeAuthorization)package.New("ApplicationWebAuthorization",accessor,package.Authority());
    }
}
