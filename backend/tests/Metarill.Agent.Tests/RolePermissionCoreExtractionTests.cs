using System.Data.Common;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>工作台核心离开平台程序集，仍使用真实平台适配与固定离线SQL传输；不启动HTTP或数据库。</summary>
public sealed class RolePermissionCoreExtractionTests
{
    /// <summary>平台必要接口映射同一实例，工作台继续解析真实身份与统一协调器。</summary>
    [Fact]
    public async Task Platform_registration_resolves_shared_business_and_the_same_catalog_and_identity_instances()
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity();var registrations=Services(f,identity);registrations.AddScoped<RolePermissionBusiness>();
        using var provider=registrations.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true});using var scope=provider.CreateScope();var services=scope.ServiceProvider;
        Assert.Equal("Metarill.Security",services.GetRequiredService<RolePermissionBusiness>().GetType().Assembly.GetName().Name);
        Assert.Same(services.GetRequiredService<PlatformSecurityCurrentResources>(),services.GetRequiredService<IRolePermissionCatalog>());
        Assert.Same(services.GetRequiredService<ISecurityCurrentResources>(),services.GetRequiredService<IRolePermissionCatalog>());
        Assert.Same(services.GetRequiredService<HttpRuntimeAuthorization>(),services.GetRequiredService<ISecurityIdentityAccessor>());
        Assert.Same(identity,services.GetRequiredService<ISecurityIdentityAccessor>().RequireIdentity());Assert.Same(f.Session,services.GetRequiredService<SqlRowStore>().Session);
        registrations=Services(f,identity);registrations.Remove(registrations.Single(item=>item.ServiceType==typeof(IRolePermissionCatalog)));registrations.AddScoped<RolePermissionBusiness>();
        using var missing=registrations.BuildServiceProvider();Assert.Throws<InvalidOperationException>(()=>missing.GetRequiredService<RolePermissionBusiness>());
    }

    /// <summary>目录缺失在构造时拒绝；普通读取的错误Session或仓储在任何SQL之前拒绝。</summary>
    [Fact]
    public async Task Required_catalog_and_normal_read_contexts_cannot_fall_back_to_another_store()
    {
        using var f=new PrincipalRealmFixture();using var other=new PrincipalRealmFixture();var identity=await f.Identity();var authorization=new Identity(identity);
        var original=new PlatformSecurityCurrentResources(f.Store,new(f.Store,f.Registry));var guard=new SecurityRealmMutationGuard(f.Store,f.Registry,authorization,new(f.Store,f.Registry,original,new PlatformSecurityCurrentSessionPolicy(f.Store)));
        Assert.Throws<ArgumentNullException>(()=>new RolePermissionBusiness(f.Store,null!,authorization,f.Ids,f.Registry,guard));
        IRolePermissionCatalog catalog=original;IRolePermissionCatalog mismatched=new PlatformSecurityCurrentResources(f.Store,new(other.Store,other.Registry));f.Provider.Commands.Clear();other.Provider.Commands.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>catalog.ReadAsync(other.Session,f.PlatformProject,CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>mismatched.ReadAsync(f.Session,f.PlatformProject,CancellationToken.None));Assert.Empty(f.Provider.Commands);Assert.Empty(other.Provider.Commands);
    }

    /// <summary>新接口仍消费原CWT引用证明；等值对象、另实例、另Session和旧事务全部拒绝。</summary>
    [Fact]
    public async Task Full_catalog_requires_the_original_current_read_instance_and_actual_transaction()
    {
        using var f=new PrincipalRealmFixture();using var other=new PrincipalRealmFixture();var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);
        var actual=new PlatformSecurityCurrentResources(f.Store,metadata);IRolePermissionCatalog catalog=actual;SecurityResourceCatalog? resources=null;DbTransaction? transaction=null;
        await f.Session.InTransactionAsync(async()=>
        {
            transaction=f.Session.CurrentTransaction!;await f.Store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(f.PlatformProject)}});await f.Store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",long.Parse(f.PlatformAdmin)}});
            resources=await actual.ReadForUpdateAsync(f.Session,transaction,long.Parse(f.PlatformProject),long.Parse(f.PlatformAdmin),CancellationToken.None);
            var complete=catalog.RequireLoaded(f.Session,transaction,resources);Assert.NotEmpty(complete.ConfigPages);Assert.Same(complete,catalog.RequireLoaded(f.Session,transaction,resources));
            Assert.Throws<InvalidOperationException>(()=>catalog.RequireLoaded(f.Session,transaction,resources with{}));
            Assert.Throws<InvalidOperationException>(()=>((IRolePermissionCatalog)new PlatformSecurityCurrentResources(f.Store,metadata)).RequireLoaded(f.Session,transaction,resources));
            Assert.Throws<InvalidOperationException>(()=>catalog.RequireLoaded(other.Session,transaction,resources));return true;
        });
        Assert.Throws<InvalidOperationException>(()=>catalog.RequireLoaded(f.Session,transaction!,resources!));
        await f.Session.InTransactionAsync(()=>{Assert.Throws<InvalidOperationException>(()=>catalog.RequireLoaded(f.Session,f.Session.CurrentTransaction!,resources!));return Task.FromResult(true);});
    }

    /// <summary>完整目录失败不能借发布资源成功，保存须整批回滚且不接受瘦投影。</summary>
    [Theory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Missing_full_catalog_read_or_current_proof_fails_without_changing_grants(bool save,bool nullResult)
    {
        using var f=new PrincipalRealmFixture();var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);
        f.Add("yz_role",("id",13L),("name","原角色"),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("is_super",0));
        var identity=await f.Identity();var authorization=new Identity(identity);var resources=new PlatformSecurityCurrentResources(f.Store,metadata);var coordinator=new SecurityRealmMutationGuard(f.Store,f.Registry,authorization,new(f.Store,f.Registry,resources,new PlatformSecurityCurrentSessionPolicy(f.Store)));
        var business=new RolePermissionBusiness(f.Store,new UnavailableCatalog(nullResult),authorization,f.Ids,f.Registry,coordinator);var before=JsonSerializer.Serialize(f.Provider.Rows);
        Task Call()=>save?business.SaveAsync(new("13",new string('a',64),new([],new(),[]))):business.SnapshotAsync("13");
        if(nullResult)await Assert.ThrowsAsync<InvalidOperationException>(Call);else await Assert.ThrowsAsync<NotSupportedException>(Call);
        Assert.Equal(before,JsonSerializer.Serialize(f.Provider.Rows));
    }

    /// <summary>沿真实平台注册接口，仅隔离已经建立的请求上下文，不替代认证或SQL实现。</summary>
    private static ServiceCollection Services(PrincipalRealmFixture f,AuthenticatedIdentity identity)
    {
        var services=new ServiceCollection();services.AddSingleton(f.Session);services.AddSingleton(f.Store);services.AddSingleton(f.Registry);services.AddSingleton<ISecurityResourceRegistry>(provider=>provider.GetRequiredService<MetadataRegistry>());services.AddSingleton<IIdGenerator>(f.Ids);services.AddSingleton<MetadataRepository>();
        var context=new DefaultHttpContext();context.Items[typeof(AuthenticatedIdentity)]=identity;services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor{HttpContext=context});services.AddScoped<HttpRuntimeAuthorization>();services.AddRealmWriteCoordination();return services;
    }
    private sealed class Identity(AuthenticatedIdentity identity):ISecurityIdentityAccessor{public AuthenticatedIdentity RequireIdentity()=>identity;}
    private sealed class UnavailableCatalog(bool nullResult):IRolePermissionCatalog
    {
        public Task<MetadataCatalog> ReadAsync(DatabaseSession session,string projectId,CancellationToken cancellationToken)=>nullResult?Task.FromResult<MetadataCatalog>(null!):throw new NotSupportedException("原完整目录不可用。");
        public MetadataCatalog RequireLoaded(DatabaseSession session,DbTransaction transaction,SecurityResourceCatalog resources)=>nullResult?null!:throw new NotSupportedException("原完整目录证明不可用。");
    }
}
