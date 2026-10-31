using System.Text.RegularExpressions;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>传输使用现有离线SqlRowStore夹具，验证平台适配没有添加查询、改变直接认证的读取顺序或旁路grant。</summary>
public sealed class SecurityCoreExtractionTests
{
    /// <summary>Web与Agent原查询序列保持，公共资源投影自身不读数据库。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Platform_authentication_keeps_its_original_query_sequence(bool agent)
    {
        using var fixture=new PrincipalRealmFixture();
        if(agent)
        {
            fixture.WebSession["kind"]="agent";fixture.WebSession["grant_id"]=50L;
            fixture.Add("sys_agent_grant",("id",50L),("status","approved"),("client_id",AgentOAuthService.ClientId),
                ("account_id",8L),("project_id",long.Parse(fixture.PlatformProject)),("refresh_expires_at",DateTime.UtcNow.AddHours(1)));
        }
        fixture.Provider.Commands.Clear();var identity=await fixture.Identity();
        Assert.True(identity.Actor.IsAdministrator);Assert.Equal(agent?50L:(long?)null,identity.GrantId);
        var actual=fixture.Provider.Commands.Select(sql=>Regex.Match(sql," FROM `([^`]+)`").Groups[1].Value).ToArray();
        string[] expected=["sys_session","yz_manager","yz_admin_type","yz_project","yz_role","yz_role_permission","yz_project"];
        if(agent)expected=[..expected,"sys_agent_grant"];
        Assert.Equal(expected,actual);
    }

    /// <summary>网页登录不调用Agent扩展，非Web种类也不能借宿主扩展扩大为登录会话。</summary>
    [Theory]
    [InlineData("web",true)][InlineData("unknown",false)]
    public async Task Session_policy_is_not_a_fallback_for_web_or_unknown_kinds(string kind,bool allowed)
    {
        using var fixture=new PrincipalRealmFixture();fixture.WebSession["kind"]=kind;
        var policy=new RejectAllExtensions();var authentication=new AuthenticationService(fixture.Store,fixture.Ids,fixture.Registry,policy);
        Assert.Equal(allowed,await authentication.AuthenticateAsync(fixture.Token,null) is not null);
        Assert.Equal(0,policy.Calls);
    }
    /// <summary>扩展获得的三值来自已验证真实行，拒绝结果不能回退Web或仅凭realm放行。</summary>
    [Fact]
    public async Task Agent_extension_receives_the_real_binding_and_its_denial_is_final()
    {
        using var fixture=new PrincipalRealmFixture();fixture.WebSession["kind"]="agent";fixture.WebSession["grant_id"]=50L;
        fixture.Account["yz_project_id"]=long.Parse(fixture.DemoProject);
        var policy=new RejectAllExtensions();var authentication=new AuthenticationService(fixture.Store,fixture.Ids,fixture.Registry,policy);
        Assert.Null(await authentication.AuthenticateAsync(fixture.Token,null));
        Assert.Equal(1,policy.Calls);Assert.Equal(new(50L,8L,fixture.PlatformProject),policy.Last);
        Assert.DoesNotContain(fixture.Provider.Commands,sql=>sql.Contains(" FROM `sys_agent_grant`",StringComparison.Ordinal));
    }
    private sealed class RejectAllExtensions:ISecuritySessionPolicy
    {
        internal int Calls {get;private set;}
        internal SecuritySessionBinding? Last {get;private set;}
        public Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding,CancellationToken cancellationToken)
        {Calls++;Last=binding;return Task.FromResult(false);}
    }
}
