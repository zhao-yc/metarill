using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行真实Full包的正式DI；身份仅由离线HttpContext夹具提供，不冒称真实HTTP认证或MySQL锁验证。</summary>
public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>授权/身份及守卫别名必须同scope同实例，必要目录与当前事务证明沿原Reader传递。</summary>
    [Fact]
    public async Task WebCoordination_actual_registration_shares_identity_guard_and_transaction_adapters()
    {
        using var f=new CoordinationApplication(package);using var services=f.Build();using var scope=services.CreateScope();var provider=scope.ServiceProvider;
        var authorization=provider.GetRequiredService<IRuntimeAuthorization>();var identity=provider.GetRequiredService<ISecurityIdentityAccessor>();
        Assert.Same(authorization,identity);Assert.Same(authorization,provider.GetRequiredService(package.HostType("ApplicationWebAuthorization")));
        Assert.Throws<UnauthorizedAccessException>(()=>identity.RequireIdentity());f.Accessor.HttpContext!.Items[typeof(AuthenticatedIdentity)]=f.Identity;
        Assert.Same(f.Identity,identity.RequireIdentity());Assert.Equal(f.Identity.Actor,authorization.RequireActor());
        var reader=provider.GetRequiredService<SecurityRealmCurrentReader>();var guard=provider.GetRequiredService<SecurityRealmMutationGuard>();var business=provider.GetRequiredService<RolePermissionBusiness>();
        var guards=provider.GetServices<IRecordMutationGuard>().ToArray();Assert.Single(guards);Assert.Same(guard,guards[0]);
        Assert.Equal("Metarill.Security",reader.GetType().Assembly.GetName().Name);Assert.Equal("Metarill.Security",business.GetType().Assembly.GetName().Name);
        Assert.DoesNotContain(f.Registrations,item=>item.ServiceType.FullName?.Contains("ConfigurationMutationGuard",StringComparison.Ordinal)==true);
        var resources=provider.GetRequiredService<ISecurityCurrentResources>();var catalog=provider.GetRequiredService<IRolePermissionCatalog>();Assert.Same(resources,catalog);
        var policy=provider.GetRequiredService<ISecurityCurrentSessionPolicy>();Assert.Equal(package.HostType("ApplicationSecurityCurrentSessionPolicy"),policy.GetType());
        await f.Session.InTransactionAsync(async()=>
        {
            var tx=f.Session.CurrentTransaction!;
            // 直接执行协调器原私有身份校验；不新增产品入口，也不伪造聚合准备状态。
            Assert.Same(f.Identity,package.Invoke(guard,"RequireRequest",f.Identity.Actor,tx));
            Assert.Throws<UnauthorizedAccessException>(()=>package.Invoke(guard,"RequireRequest",f.Identity.Actor with{AccountId=999},tx));
            var proof=await resources.ReadForUpdateAsync(f.Session,tx,f.Project,f.Admin,CancellationToken.None);
            Assert.Equal(JsonSerializer.Serialize(await catalog.ReadAsync(f.Session,f.Project.ToString(),CancellationToken.None)),JsonSerializer.Serialize(catalog.RequireLoaded(f.Session,tx,proof)));
            Assert.Equal(package.Metadata.ConfigMenus.Any(menu=>menu.YzAdminTypeId==f.Admin.ToString()),await package.Call(reader,"HasAdminMenuReferencesForUpdateAsync",tx,f.Admin,CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>policy.ReadAgentGrantForUpdateAsync(f.Session,tx,1,CancellationToken.None));
            // 当前身份虽真实注入且事务有效，没有聚合准备仍不能写安全表。
            var failure=Assert.Throws<InvalidOperationException>(()=>SecurityRealmMutationGuard.RequirePrepared(guards,tx,"yz_role",55,f.Identity.Actor,false,false));Assert.StartsWith("YZP_CONFLICT",failure.Message);
            return true;
        });
        using var other=services.CreateScope();Assert.NotSame(authorization,other.ServiceProvider.GetRequiredService<IRuntimeAuthorization>());
        Assert.NotSame(reader,other.ServiceProvider.GetRequiredService<SecurityRealmCurrentReader>());Assert.NotSame(guard,other.ServiceProvider.GetRequiredService<SecurityRealmMutationGuard>());Assert.NotSame(business,other.ServiceProvider.GetRequiredService<RolePermissionBusiness>());
        Assert.NotSame(resources,other.ServiceProvider.GetRequiredService<ISecurityCurrentResources>());Assert.Empty(f.Provider.Commands);
    }

    /// <summary>任何必要适配缺失都在解析实际协调器时硬失败，不能补发布快照、假身份或另一个连接。</summary>
    [Theory]
    [InlineData("store")][InlineData("registry")][InlineData("resources")][InlineData("session-policy")][InlineData("identity")][InlineData("role-catalog")][InlineData("ids")]
    public void WebCoordination_missing_required_adapter_fails_before_opening_connection(string missing)
    {
        using var f=new CoordinationApplication(package);var removed=missing switch{"store"=>typeof(SqlRowStore),"registry"=>typeof(ISecurityResourceRegistry),"resources"=>typeof(ISecurityCurrentResources),"session-policy"=>typeof(ISecurityCurrentSessionPolicy),"identity"=>typeof(ISecurityIdentityAccessor),"role-catalog"=>typeof(IRolePermissionCatalog),_=>typeof(IIdGenerator)};
        f.Registrations.RemoveAll(removed);using var services=f.Build();using var scope=services.CreateScope();
        var requested=missing is "role-catalog" or "ids"?typeof(RolePermissionBusiness):missing=="identity"?typeof(SecurityRealmMutationGuard):typeof(SecurityRealmCurrentReader);
        Assert.Throws<InvalidOperationException>(()=>scope.ServiceProvider.GetRequiredService(requested));Assert.Equal(0,f.Provider.Opens);Assert.Empty(f.Provider.Commands);
    }

    /// <summary>开发模式保持原辅助授权；正式注册拒绝显式Development，原RuntimeActor或内存id不能冒充持久身份。</summary>
    [Fact]
    public void WebCoordination_development_and_actor_only_context_cannot_create_security_identity()
    {
        var registrations=new ServiceCollection();var development=package.Mode(new(){["Authentication:Mode"]="Development",["Authentication:DevelopmentBootstrap:Enabled"]="true"},"Development");
        Assert.Throws<InvalidOperationException>(()=>package.RegisterCoordination(registrations,development));Assert.Empty(registrations);
        Assert.False(typeof(ISecurityIdentityAccessor).IsAssignableFrom(package.HostType("DevelopmentAuthorization")));
        using var f=new CoordinationApplication(package);using var services=f.Build();using var scope=services.CreateScope();f.Accessor.HttpContext!.Items[typeof(RuntimeActor)]=new RuntimeActor(1,"开发内存身份",true,f.Project.ToString(),f.Admin.ToString());
        Assert.Throws<UnauthorizedAccessException>(()=>scope.ServiceProvider.GetRequiredService<ISecurityIdentityAccessor>().RequireIdentity());Assert.Throws<UnauthorizedAccessException>(()=>scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>().RequireActor());Assert.Equal(0,f.Provider.Opens);
    }

    /// <summary>只有本scope签发的当前目录能回取完整revision，Reader也不能复用另一事务或已结束事务。</summary>
    [Fact]
    public async Task WebCoordination_reader_and_catalog_reject_cross_scope_or_finished_transaction_proof()
    {
        using var f=new CoordinationApplication(package);using var services=f.Build();using var scope=services.CreateScope();var provider=scope.ServiceProvider;var reader=provider.GetRequiredService<SecurityRealmCurrentReader>();var resources=provider.GetRequiredService<ISecurityCurrentResources>();var catalog=provider.GetRequiredService<IRolePermissionCatalog>();System.Data.Common.DbTransaction previous=null!;SecurityResourceCatalog proof=null!;
        await f.Session.InTransactionAsync(async()=>
        {
            previous=f.Session.CurrentTransaction!;proof=await resources.ReadForUpdateAsync(f.Session,previous,f.Project,f.Admin,CancellationToken.None);
            using var other=services.CreateScope();Assert.Throws<InvalidOperationException>(()=>other.ServiceProvider.GetRequiredService<IRolePermissionCatalog>().RequireLoaded(f.Session,previous,proof));return true;
        });
        Assert.Throws<InvalidOperationException>(()=>catalog.RequireLoaded(f.Session,previous,proof));await Assert.ThrowsAsync<InvalidOperationException>(()=>package.Call(reader,"HasAdminMenuReferencesForUpdateAsync",previous,f.Admin,CancellationToken.None));
        await f.Session.InTransactionAsync(async()=>{Assert.Throws<InvalidOperationException>(()=>catalog.RequireLoaded(f.Session,f.Session.CurrentTransaction!,proof));await Assert.ThrowsAsync<InvalidOperationException>(()=>package.Call(reader,"HasAdminMenuReferencesForUpdateAsync",previous,f.Admin,CancellationToken.None));return true;});Assert.Empty(f.Provider.Commands);
    }

    /// <summary>只提供ADO连接/事务替身与明确离线身份，注册函数和协调器全部来自实际编译包。</summary>
    private sealed class CoordinationApplication:IDisposable
    {
        internal readonly StandaloneMemoryProvider Provider=new();internal readonly DatabaseSession Session;internal readonly ServiceCollection Registrations=new();internal readonly HttpContextAccessor Accessor=new(){HttpContext=new DefaultHttpContext()};internal readonly AuthenticatedIdentity Identity;internal readonly long Project,Admin;
        internal CoordinationApplication(WebAuthenticationPackageFixture fixture)
        {
            Project=long.Parse(fixture.Metadata.YzProjectId);Admin=long.Parse(fixture.Metadata.YzAdminTypes[0].Id);Identity=new(new(877,"明确离线身份",false,Project.ToString(),Admin.ToString()),new HashSet<string>(),878,null){PrincipalProjectId=Project.ToString(),PrincipalAdminTypeId=Admin.ToString()};
            Session=new(new(){ExpectedDatabase="offline",ConnectionString="offline"},Provider.Orm,Provider);Registrations.AddSingleton(Session);Registrations.AddSingleton(new SqlRowStore(Session,fixture.Security));Registrations.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(877));Registrations.AddSingleton<IHttpContextAccessor>(Accessor);
            var authority=fixture.Authority();fixture.RegisterCatalog(Registrations,authority);Registrations.AddSingleton<ISecurityResourceRegistry>((ISecurityResourceRegistry)fixture.New("ApplicationSecurityRegistry",authority));fixture.RegisterCoordination(Registrations,fixture.Mode(new(){["Database:ConnectionString"]="offline",["Database:ExpectedDatabase"]="offline"},"Production"));
        }
        internal ServiceProvider Build()=>Registrations.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true});
        public void Dispose()=>Session.Dispose();
    }
}
