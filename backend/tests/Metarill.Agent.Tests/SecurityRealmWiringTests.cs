using System.Reflection;
using System.Net;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Platform.Generated;
using Metarill.Platform.Generated.DTOs;
using Metarill.Platform.Generated.Entities;
using Metarill.Platform.Generated.Services;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>实际宿主注册与生成Service的离线接线合同；不把partial调用当作MySQL持久化或RR验收。</summary>
public sealed class SecurityRealmWiringTests
{
    /// <summary>实际IEnumerable顺序固定，后台旧守卫只复用同一个准备对象，绝不先取旧项目锁。</summary>
    [Fact]
    public async Task Actual_registration_resolves_generated_services_and_orders_security_before_configuration()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);Grant(f,"yz_admin_type");var identity=await f.Identity();
        using var provider=Services(f,identity);using var scope=provider.CreateScope();var guards=scope.ServiceProvider.GetServices<IRecordMutationGuard>().ToArray();
        Assert.Collection(guards,guard=>Assert.IsType<SecurityRealmMutationGuard>(guard),guard=>Assert.IsType<ConfigurationMutationGuard>(guard));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<YzRoleService>());Assert.NotNull(scope.ServiceProvider.GetRequiredService<YzManagerService>());
        var service=scope.ServiceProvider.GetRequiredService<YzAdminTypeService>();
        await f.Session.InTransactionAsync(async()=>
        {
            var preparation=Preparation(f,identity,"yz_admin_type",RecordMutationOperation.Update,long.Parse(f.PlatformAdmin));
            // 反向执行会在旧guard取锁之前拒绝，而不是事后补安全锁。
            var before=f.Provider.Commands.Count;await Assert.ThrowsAsync<InvalidOperationException>(()=>guards[1].BeforeMutationAsync(preparation));Assert.Equal(before,f.Provider.Commands.Count);
            foreach(var guard in guards)await guard.BeforeMutationAsync(preparation);
            Hook(service,"BeforeSave",new YzAdminType{Id=long.Parse(f.PlatformAdmin)},identity.Actor,false);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>guards[1].BeforeMutationAsync(preparation with{}));return true;
        });
    }

    /// <summary>三个真实手写partial要求同事务、同actor和同目标的真实pending，而非仅存在某个guard实例。</summary>
    [Theory]
    [InlineData("yz_manager")][InlineData("yz_role")][InlineData("yz_admin_type")]
    public async Task Actual_partials_reject_missing_unprepared_wrong_target_actor_and_new_transaction(string table)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);Grant(f,table);var identity=await f.Identity();
        using var provider=Services(f,identity);using var scope=provider.CreateScope();var service=Resolve(scope.ServiceProvider,table);
        var guards=scope.ServiceProvider.GetServices<IRecordMutationGuard>().ToArray();var entity=Entity(table,30);
        await f.Session.InTransactionAsync(async()=>
        {
            Assert.Throws<InvalidOperationException>(()=>Hook(service,"BeforeSave",entity,identity.Actor,false));
            var preparation=Preparation(f,identity,table,RecordMutationOperation.Update,30);foreach(var guard in guards)await guard.BeforeMutationAsync(preparation);
            Hook(service,"BeforeSave",entity,identity.Actor,false);
            Assert.Throws<InvalidOperationException>(()=>Hook(service,"BeforeSave",Entity(table,31),identity.Actor,false));
            Assert.Throws<UnauthorizedAccessException>(()=>Hook(service,"BeforeSave",entity,identity.Actor with{AccountId=99},false));
            Assert.Throws<InvalidOperationException>(()=>Hook(service,"BeforeDelete",entity,identity.Actor,false));
            Assert.Throws<InvalidOperationException>(()=>SecurityRealmMutationGuard.RequirePrepared([],f.Session.CurrentTransaction,table,30,identity.Actor,false,false));return true;
        });
        await f.Session.InTransactionAsync(()=>
        {
            Assert.Throws<InvalidOperationException>(()=>Hook(service,"BeforeSave",entity,identity.Actor,false));return Task.FromResult(true);
        });
    }

    /// <summary>新增没有旧目标ID，仍需同固定表和create操作；删除则必须是同目标delete准备。</summary>
    [Theory]
    [InlineData("yz_manager")][InlineData("yz_role")][InlineData("yz_admin_type")]
    public async Task Actual_partials_accept_only_prepared_create_and_delete_modes(string table)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);Grant(f,table);var identity=await f.Identity();
        using var provider=Services(f,identity);using var scope=provider.CreateScope();var service=Resolve(scope.ServiceProvider,table);var guards=scope.ServiceProvider.GetServices<IRecordMutationGuard>().ToArray();
        await f.Session.InTransactionAsync(async()=>
        {
            var prep=Preparation(f,identity,table,RecordMutationOperation.Create,null);
            if(table!="yz_admin_type")prep=prep with{ScopeValues=new Dictionary<string,object?>{{"yz_admin_type_id",long.Parse(f.PlatformAdmin)}}};
            foreach(var guard in guards)await guard.BeforeMutationAsync(prep);
            Hook(service,"BeforeSave",Entity(table,99),identity.Actor,true);
            Assert.Throws<InvalidOperationException>(()=>Hook(service,"BeforeSave",Entity(table,99),identity.Actor,false));return true;
        });
        await f.Session.InTransactionAsync(async()=>
        {
            if(table=="yz_role")f.Row(table,30)["is_built_in"]=1;
            var preparation=Preparation(f,identity,table,RecordMutationOperation.Delete,30);foreach(var guard in guards)await guard.BeforeMutationAsync(preparation);
            // IsBuiltIn不再是额外删除门槛，真正引用约束仍由BeforeWrite复核。
            var entity=Entity(table,30);if(entity is YzRole role)role.IsBuiltIn=1;Hook(service,"BeforeDelete",entity,identity.Actor,false);
            var values=f.Row(table,30).Where(pair=>new[]{"yz_admin_type_id","yz_project_id","yz_role_ids","is_super","is_scope","is_built_in"}.Contains(pair.Key)).ToDictionary();
            var validation=new RecordMutationValidation(preparation.PageId,table,RecordMutationOperation.Delete,30,identity.Actor,values,values,new HashSet<string>(),f.Session.CurrentTransaction!);
            foreach(var guard in guards)await guard.BeforeWriteAsync(validation);
            Assert.Throws<InvalidOperationException>(()=>Hook(service,"BeforeDelete",entity,identity.Actor,false));return true;
        });
    }

    /// <summary>通过真正AddGenerated解析并调用标准Service，无页权必须在ORM业务查询之前失败。</summary>
    [Fact]
    public async Task Actual_generated_service_rechecks_permission_before_orm_access()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);var identity=await f.Identity();using var provider=Services(f,identity);using var scope=provider.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<YzRoleService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.Update1692532858529329154Async(new(){Id=30,IsSuper=new(1)},null,identity.Actor,CancellationToken.None));
        Assert.Null(f.Session.CurrentTransaction);
    }

    /// <summary>误new空guards的实际create方法也会执行手写partial拒绝，不能绕过新增协调。</summary>
    [Fact]
    public async Task Unregistered_generated_service_cannot_create_a_security_object()
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity();var service=new YzRoleService(f.Session,new(f.Session),f.Ids,[]);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>service.Create1692532858529329154Async(new(){Name=new("不能旁路"),IsSuper=new(1)},null,identity.Actor,CancellationToken.None));
        Assert.Contains("统一写入协调器",error.Message);Assert.Null(f.Session.CurrentTransaction);
    }

    /// <summary>HTTP恢复仅固定3表、同真实后台；伪造权限不开放普通配置、全局和跨realm资源。</summary>
    [Theory]
    [InlineData("yz_manager")][InlineData("yz_role")][InlineData("yz_admin_type")]
    public async Task Http_restores_only_same_realm_published_safety_page_actions(string table)
    {
        using var f=new PrincipalRealmFixture(super:false);var identity=await f.Identity();var page=Page(f,table);
        identity=identity with{Permissions=new HashSet<string>{page+":is_update",f.DemoPage+":is_all"}};var auth=Authorization(f,identity);
        auth.RequirePagePermission(page,"update");Assert.Throws<UnauthorizedAccessException>(()=>auth.RequirePagePermission(page,"delete"));
        Assert.Throws<UnauthorizedAccessException>(()=>auth.RequireAdministrator());Assert.Throws<UnauthorizedAccessException>(()=>auth.RequirePagePermission(f.DemoPage,"update"));
        var config=Page(f,"config_column");identity=identity with{Permissions=new HashSet<string>{config+":is_all"}};Assert.Throws<UnauthorizedAccessException>(()=>Authorization(f,identity).RequirePagePermission(config,"update"));
        var raw=Page(f,"yz_role_permission");identity=identity with{Permissions=new HashSet<string>{raw+":is_all"}};Assert.Throws<UnauthorizedAccessException>(()=>Authorization(f,identity).RequirePagePermission(raw,"update"));
    }

    /// <summary>平台注册超级角色切工作区保留既有控制面含义，普通身份不由工作区升级。</summary>
    [Fact]
    public async Task Platform_super_keeps_workspace_compatibility_without_realm_promotion()
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity(f.DemoProject);Authorization(f,identity).RequireAdministrator();Authorization(f,identity).RequirePagePermission(Page(f,"yz_role"),"update");
    }

    /// <summary>真实认证中间件和MVC路径恢复原页动作，下一HTTP请求立即看到撤权；不联网或代替RR实测。</summary>
    [Theory]
    [InlineData("yz_manager")][InlineData("yz_role")][InlineData("yz_admin_type")]
    public async Task Real_http_pipeline_uses_original_safety_page_actions_and_next_request_revocation(string table)
    {
        using var f=new PrincipalRealmFixture(super:false);Grant(f,table);f.Row("yz_role_permission",71)["permission_values"]="[\"is_update\"]";
        await using var host=await RealmHttpHost.Start(f);var path="/__principal-realm/page/"+Page(f,table);
        Assert.Equal(HttpStatusCode.OK,(await host.Send(HttpMethod.Get,path+"?operation=update")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,path+"?operation=delete")).StatusCode);
        f.Row("yz_role_permission",71)["permission_values"]="[]";
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,path+"?operation=update")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,path+"?operation=update",project:f.DemoProject)).StatusCode);
    }

    /// <summary>同一guard的已准备状态也不能用到另一连接事务；检查不是只比较存在某个pending。</summary>
    [Fact]
    public async Task Prepared_state_rejects_transaction_from_another_session()
    {
        using var f=new PrincipalRealmFixture();using var other=new PrincipalRealmFixture();await Seed(f);var identity=await f.Identity();
        using var provider=Services(f,identity);using var scope=provider.CreateScope();var guards=scope.ServiceProvider.GetServices<IRecordMutationGuard>().ToArray();
        await f.Session.InTransactionAsync(async()=>
        {
            var prep=Preparation(f,identity,"yz_role",RecordMutationOperation.Update,30);foreach(var guard in guards)await guard.BeforeMutationAsync(prep);
            await other.Session.InTransactionAsync(()=>
            {
                Assert.Throws<InvalidOperationException>(()=>SecurityRealmMutationGuard.RequirePrepared(guards,other.Session.CurrentTransaction,"yz_role",30,identity.Actor,false,false));
                return Task.FromResult(true);
            });return true;
        });
    }

    /// <summary>通过真实元数据仓储种子和固定内存记录准备最小安全目录。</summary>
    private static async Task Seed(PrincipalRealmFixture f)
    {
        await new MetadataRepository(f.Store,f.Registry).SeedAsync(f.Registry.Platform);
        f.Add("yz_manager",("id",30L),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("yz_role_ids","[]"));
        f.Add("yz_role",("id",30L),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("is_super",0));
        f.Add("yz_admin_type",("id",30L),("yz_project_id",long.Parse(f.PlatformProject)));f.Provider.Commands.Clear();
    }
    /// <summary>测试页面从发布表归属解析，不按请求臆造页面。</summary>
    private static string Page(PrincipalRealmFixture f,string table)=>f.Registry.Platform.ConfigPages.Single(page=>f.Registry.Platform.ConfigTables.Single(owner=>owner.Id==page.ConfigTableId).TableName==table).Id;
    /// <summary>只给真实操作者角色写入当前测试指定页面的动作。</summary>
    private static void Grant(PrincipalRealmFixture f,string table)=>f.Add("yz_role_permission",("id",71L),("yz_role_id",10L),("config_page_id",long.Parse(Page(f,table))),("permission_values","[\"is_all\"]"));
    /// <summary>用夹具实际当前事务建立产品准备合同。</summary>
    private static RecordMutationPreparation Preparation(PrincipalRealmFixture f,AuthenticatedIdentity identity,string table,RecordMutationOperation operation,long? target)=>new(Page(f,table),table,operation,identity.Actor,f.Session.CurrentTransaction!){TargetId=target};
    /// <summary>从实际AddGenerated注册解析三个手写扩展服务。</summary>
    private static object Resolve(IServiceProvider services,string table)=>table switch{"yz_role"=>services.GetRequiredService<YzRoleService>(),"yz_manager"=>services.GetRequiredService<YzManagerService>(),_=>services.GetRequiredService<YzAdminTypeService>()};
    /// <summary>实体只用于执行产品partial，不冒充Mapper持久化。</summary>
    private static object Entity(string table,long id)=>table switch{"yz_role"=>new YzRole{Id=id,IsSuper=1},"yz_manager"=>new YzManager{Id=id},_=>new YzAdminType{Id=id}};
    /// <summary>调用编译进真实Service的手写partial，不镜像实现或伪造协调器内部状态。</summary>
    private static void Hook(object service,string method,object entity,RuntimeActor actor,bool creating)
    {
        try{service.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,method=="BeforeSave"?[entity,actor,creating,new HashSet<string>()]:[entity,actor]);}
        catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    /// <summary>固定真实认证上下文供产品HTTP权限适配器消费。</summary>
    private static HttpRuntimeAuthorization Authorization(PrincipalRealmFixture f,AuthenticatedIdentity identity)
    {var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=identity;return new(new HttpContextAccessor{HttpContext=http},f.Registry);}
    /// <summary>与Program共用真实注册入口，DatabaseSession是夹具外部持有实例，服务容器不构造测试数据库。</summary>
    private static ServiceProvider Services(PrincipalRealmFixture f,AuthenticatedIdentity identity)
    {
        var services=new ServiceCollection();services.AddSingleton(f.Session);services.AddSingleton(f.Store);services.AddSingleton(f.Registry);services.AddSingleton(f.Ids);
        var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=identity;services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor{HttpContext=http});
        services.AddScoped<HttpRuntimeAuthorization>();services.AddScoped<IRuntimeAuthorization>(provider=>provider.GetRequiredService<HttpRuntimeAuthorization>());services.AddSingleton<MetadataRepository>();
        services.AddRealmWriteCoordination();services.AddGeneratedServices();return services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
    }
}
