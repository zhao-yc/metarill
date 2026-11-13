using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>平台文件特权只取真实当前Web身份；旧授权器不能因接口升级自动获得超级权限。</summary>
public sealed class FileRealmSuperIdentityTests
{
    /// <summary>工作区项目、后台或Agent来源变化均不能继承真实后台超级身份。</summary>
    [Theory]
    [InlineData("valid",true)][InlineData("ordinary",false)][InlineData("project",false)]
    [InlineData("admin",false)][InlineData("empty",false)][InlineData("agent",false)]
    public void Current_web_identity_must_match_principal_realm(string scenario,bool expected)
    {
        var identity=new AuthenticatedIdentity(new(1,"离线身份",false,"54","5"),new HashSet<string>(),2,null){PrincipalProjectId="54",PrincipalAdminTypeId="5",IsRealmSuper=true};
        identity=scenario switch{"ordinary"=>identity with{IsRealmSuper=false},"project"=>identity with{Actor=identity.Actor with{ProjectId="55"}},"admin"=>identity with{Actor=identity.Actor with{AdminTypeId="6"}},"empty"=>identity with{PrincipalAdminTypeId="",Actor=identity.Actor with{AdminTypeId=""}},"agent"=>identity with{GrantId=3},_=>identity};
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};accessor.HttpContext.Items[typeof(AuthenticatedIdentity)]=identity;
        Assert.Equal(expected,new HttpRuntimeAuthorization(accessor,null!).IsCurrentRealmSuper());
        accessor.HttpContext.Items.Clear();Assert.Throws<UnauthorizedAccessException>(()=>new HttpRuntimeAuthorization(accessor,null!).IsCurrentRealmSuper());
    }
    /// <summary>缺少宿主证明时缺省拒绝，不改变旧实现的两个必需方法。</summary>
    [Fact]
    public void Legacy_authorization_has_no_implicit_realm_super_proof()=>Assert.False(((IRuntimeAuthorization)new Legacy()).IsCurrentRealmSuper());
    private sealed class Legacy:IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>throw new NotSupportedException();
        public void RequirePagePermission(string pageId,string operation)=>throw new NotSupportedException();
    }
}
