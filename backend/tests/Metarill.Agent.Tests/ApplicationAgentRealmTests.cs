using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>真实认证及当前Reader复用离线SQL传输，验证普通应用签发域；不替代Server-200正式验收。</summary>
public sealed class ApplicationAgentRealmTests
{
    /// <summary>只有显式固定的应用签发域可续用Agent；外项目/后台、撤销、无grant和默认宿主均拒绝。</summary>
    [Theory]
    [InlineData("valid",true)][InlineData("default",false)][InlineData("disabled-realm",false)]
    [InlineData("selected-other-project",false)][InlineData("account-moved",false)]
    [InlineData("revoked",false)][InlineData("wrong-grant-project",false)][InlineData("missing-grant",false)]
    public async Task Application_agent_uses_fixed_issuer_without_becoming_platform_administrator(string mode,bool allowed)
    {
        using var f=new PrincipalRealmFixture(business:true);AgentSession(f);
        var resources=new ApplicationResources(f.Registry);if(mode=="disabled-realm")resources.Enabled=false;
        if(mode=="account-moved")f.Account["yz_admin_type_id"]=long.Parse(f.PlatformAdmin);
        if(mode=="revoked")f.Row("sys_agent_grant",70)["status"]="revoked";
        if(mode=="wrong-grant-project")f.Row("sys_agent_grant",70)["project_id"]=long.Parse(f.PlatformProject);
        if(mode=="missing-grant")f.WebSession["grant_id"]=null;
        var authentication=new AuthenticationService(f.Store,f.Ids,mode=="default"?f.Registry:resources,new AgentSecuritySessionPolicy(f.Store));
        var selected=mode=="selected-other-project"?f.PlatformProject:f.DemoProject;
        var identity=await authentication.AuthenticateAsync(f.Token,selected);
        Assert.Equal(allowed,identity is not null);
        if(identity is null)return;
        Assert.False(identity.Actor.IsAdministrator);Assert.True(identity.IsRealmSuper);Assert.Equal(f.DemoProject,identity.PrincipalProjectId);Assert.Equal(f.DemoAdmin,identity.PrincipalAdminTypeId);Assert.Equal(70L,identity.GrantId);
        // /me前的真实传输认证再次经过同一共享认证及grant证明，不用固定身份替身。
        var transport=await new AgentAuthenticationService(f.Store,authentication).AuthenticateAsync(f.Token,"agent");Assert.Equal("agent",transport.Kind);Assert.False(transport.Identity.Actor.IsAdministrator);
        Assert.False(resources.IsPlatformRealm(f.DemoProject,f.DemoAdmin));Assert.False(resources.IsAgentRealm(f.PlatformProject,f.PlatformAdmin));
    }

    /// <summary>锁后仍使用当前签发域及grant状态，授权先锁于session；不能只修正/me而漏掉事务复核。</summary>
    [Theory]
    [InlineData("valid",true)][InlineData("disabled-realm",false)][InlineData("revoked",false)]
    public async Task Current_reader_checks_application_issuer_and_grant_again_after_locking(string mode,bool allowed)
    {
        using var f=new PrincipalRealmFixture(business:true);var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Demo);AgentSession(f);var resources=new ApplicationResources(f.Registry);
        var identity=await new AuthenticationService(f.Store,f.Ids,resources,new AgentSecuritySessionPolicy(f.Store)).AuthenticateAsync(f.Token,f.DemoProject);Assert.NotNull(identity);
        if(mode=="disabled-realm")resources.Enabled=false;if(mode=="revoked")f.Row("sys_agent_grant",70)["status"]="revoked";
        var reader=new SecurityRealmCurrentReader(f.Store,resources,new PlatformSecurityCurrentResources(f.Store,metadata),new AgentSecurityCurrentSessionPolicy(f.Store));f.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>
        {
            if(allowed)await SecurityRealmCurrentReaderTests.Read(f,identity,reader);else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>SecurityRealmCurrentReaderTests.Read(f,identity,reader));return true;
        });
        Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));
        var grant=f.Provider.Commands.FindIndex(sql=>sql.Contains(" FROM `sys_agent_grant`",StringComparison.Ordinal));var session=f.Provider.Commands.FindIndex(sql=>sql.Contains(" FROM `sys_session`",StringComparison.Ordinal));Assert.True(grant>=0&&session>grant);
    }

    /// <summary>会话/授权行均由原离线仓储读取，合法账号属于应用后台而非平台。</summary>
    private static void AgentSession(PrincipalRealmFixture f)
    {
        f.WebSession["kind"]="agent";f.WebSession["grant_id"]=70L;
        f.Add("sys_agent_grant",("id",70L),("account_id",8L),("project_id",long.Parse(f.DemoProject)),("client_id",AgentOAuthService.ClientId),("status","approved"),("refresh_expires_at",DateTime.UtcNow.AddHours(1)));
    }
    /// <summary>夹具模拟完整包可信目录装配，不给普通应用平台身份；开关只用于认证后撤销域的反例。</summary>
    private sealed class ApplicationResources(Metarill.Runtime.Metadata.MetadataRegistry registry):ISecurityResourceRegistry
    {
        private readonly AgentHostCatalog catalog=AgentHostCatalog.ForApplication(registry.Demo);
        internal bool Enabled {get;set;}=true;
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>((ISecurityResourceRegistry)registry).PublishedCatalogs;
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
        public bool IsAgentRealm(string projectId,string adminTypeId)=>Enabled&&catalog.OwnsIssuerRealm(projectId,adminTypeId);
        public string? ExactDataManageManagerField(string projectId,string adminTypeId)=>((ISecurityResourceRegistry)registry).ExactDataManageManagerField(projectId,adminTypeId);
    }
}
