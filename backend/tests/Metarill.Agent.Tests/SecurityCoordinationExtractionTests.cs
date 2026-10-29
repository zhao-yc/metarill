using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>真实协调核心接既有离线SQL传输，证明平台窄适配保持事务与目录revision，并以明确范围补齐继承读取锁序；不代替MySQL/RR。</summary>
public sealed class SecurityCoordinationExtractionTests
{
    /// <summary>下沉的是实际Reader与Guard，锁集合及工作台准备状态不公开；跨库入口只返回不可构造的不透明授权范围。</summary>
    [Fact]
    public void Coordination_types_are_shared_without_exposing_the_preparation_state()
    {
        var assembly=typeof(AuthenticationService).Assembly;
        Assert.Equal(assembly,typeof(SecurityRealmCurrentReader).Assembly);Assert.Equal(assembly,typeof(SecurityRealmMutationGuard).Assembly);
        Assert.False(assembly.GetType("Metarill.Runtime.Security.SecurityRealmLocks",true)!.IsPublic);
        var prepare=Assert.Single(typeof(SecurityRealmMutationGuard).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly),method=>method.Name.StartsWith("Prepare",StringComparison.Ordinal));
        Assert.Equal("PreparePlatformAdminInitializationAsync",prepare.Name);Assert.Equal(typeof(Task<PlatformAdminInitializationScope>),prepare.ReturnType);
        Assert.Empty(typeof(PlatformAdminInitializationScope).GetConstructors());
        Assert.All(typeof(PlatformAdminInitializationScope).GetProperties(BindingFlags.Public|BindingFlags.Instance),property=>Assert.True(property.PropertyType==typeof(string)||property.PropertyType==typeof(bool)));
        Assert.True(typeof(SecurityRealmMutationGuard).GetField("_states",BindingFlags.NonPublic|BindingFlags.Instance)!.IsPrivate);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),reference=>reference.Name is "Metarill.Runtime" or "Metarill.Generation" or "Metarill.Api");
    }

    /// <summary>DI接口与具体目录适配必须同一个Scoped实例，且保安全Guard先于配置Guard。</summary>
    [Fact]
    public void Platform_registration_shares_the_directory_instance_and_preserves_guard_order()
    {
        using var f=new PrincipalRealmFixture();var services=new ServiceCollection();services.AddSingleton(f.Store);services.AddSingleton(f.Registry);services.AddSingleton(f.Session);
        services.AddSingleton<MetadataRepository>();services.AddHttpContextAccessor();services.AddScoped<HttpRuntimeAuthorization>();services.AddRealmWriteCoordination();
        using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();
        var current=scope.ServiceProvider.GetRequiredService<PlatformSecurityCurrentResources>();
        Assert.Same(current,scope.ServiceProvider.GetRequiredService<ISecurityCurrentResources>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<HttpRuntimeAuthorization>(),scope.ServiceProvider.GetRequiredService<ISecurityIdentityAccessor>());
        Assert.Collection(scope.ServiceProvider.GetServices<IRecordMutationGuard>(),guard=>Assert.IsType<SecurityRealmMutationGuard>(guard),guard=>Assert.IsType<ConfigurationMutationGuard>(guard));
        using var second=provider.CreateScope();Assert.NotSame(current,second.ServiceProvider.GetRequiredService<PlatformSecurityCurrentResources>());
    }

    /// <summary>工作台取回原JSON只认实际投影引用和当前事务；等值复制、另适配及已提交快照全部拒绝。</summary>
    [Fact]
    public async Task Legacy_revision_directory_is_bound_to_reference_session_and_current_transaction()
    {
        using var f=new PrincipalRealmFixture();var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);
        var adapter=new PlatformSecurityCurrentResources(f.Store,metadata);SecurityResourceCatalog? snapshot=null;DbTransaction? previous=null;
        await f.Session.InTransactionAsync(async()=>
        {
            previous=f.Session.CurrentTransaction!;await LockRealm(f);f.Provider.Commands.Clear();
            snapshot=await adapter.ReadForUpdateAsync(f.Session,previous,long.Parse(f.PlatformProject),long.Parse(f.PlatformAdmin),CancellationToken.None);
            var catalog=RequireLoaded(adapter,f.Session,previous,snapshot);
            Assert.True(ReferenceEquals(catalog,RequireLoaded(adapter,f.Session,previous,snapshot)),"revision必须复用同一份原始目录。");
            Assert.True(catalog.ConfigPages.Any(page=>!string.IsNullOrEmpty(page.Name)),"完整目录保留被瘦投影排除的页面名称供原revision使用。");
            var clone=snapshot with{};Assert.True(snapshot.Equals(clone));
            Assert.Throws<InvalidOperationException>(()=>RequireLoaded(adapter,f.Session,previous,clone));
            Assert.Throws<InvalidOperationException>(()=>RequireLoaded(new(f.Store,metadata),f.Session,previous,snapshot));
            // 继承读取补上固定选择列、当前页字段与真实父子行；顺序、范围及原事务证明仍严格检查。
            Assert.Equal(new[]{"config_table","config_page","config_column","config_menu","config_page_column","config_child","config_button"},f.Provider.Commands.Select(Table));Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));
            var columns=f.Provider.Commands.Single(sql=>Table(sql)=="config_column");Assert.Contains("`id` IN (",columns);Assert.Contains("`yz_project_id`=@",columns);
            var fields=f.Provider.Commands.Single(sql=>Table(sql)=="config_page_column");Assert.Contains("`config_page_id` IN (",fields);Assert.Contains("`yz_project_id`=@",fields);
            var children=f.Provider.Commands.Single(sql=>Table(sql)=="config_child");Assert.Contains("`parent_config_page_id` IN (",children);Assert.Contains("`yz_project_id`=@",children);
            var declared=f.Registry.Platform.ConfigColumns.Where(column=>column.ControlKind is "SelectPage" or "MultiSelectPage").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
            Assert.All(catalog.ConfigColumns,column=>Assert.Contains(column.Id,declared));Assert.All(catalog.ConfigPageColumns,field=>Assert.Contains(catalog.ConfigPages,page=>page.Id==field.ConfigPageId));
            return true;
        });
        Assert.Throws<InvalidOperationException>(()=>RequireLoaded(adapter,f.Session,previous!,snapshot!));
        await f.Session.InTransactionAsync(()=>
        {
            Assert.Throws<InvalidOperationException>(()=>RequireLoaded(adapter,f.Session,previous!,snapshot!));
            Assert.Throws<InvalidOperationException>(()=>RequireLoaded(adapter,f.Session,f.Session.CurrentTransaction!,snapshot!));return Task.FromResult(true);
        });
    }

    /// <summary>固定关系在加载时编译，锁后必须消费当前覆盖；未发布重绑不能继续借旧源页授权目标。</summary>
    [Fact]
    public async Task Current_selection_rebinding_revokes_the_published_inherited_permission()
    {
        var boundary=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"metarill-current-inheritance"))+Path.DirectorySeparatorChar;
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(directory,"metadata"));
        try
        {
            using(var original=new PrincipalRealmFixture())
            {
                var catalog=original.Registry.Platform;var table=catalog.ConfigTables.Single(table=>table.TableName=="yz_role");var admin=original.PlatformAdmin;var project=original.PlatformProject;
                foreach(var id in new[]{"93001","93002","93005"})catalog.ConfigPages.Add(new(){Id=id,Name="当前继承"+id,ConfigTableId=table.Id,YzProjectId=project,YzAdminTypeId=admin});
                catalog.ConfigColumns.Add(new(){Id="93003",ConfigTableId=table.Id,YzProjectId=project,ColumnName="inheritance_test_id",LogicalType="int64",ControlKind="SelectPage",ControlDetail="{\"pageId\":\"93002\"}"});
                catalog.ConfigPageColumns.Add(new(){Id="93004",ConfigPageId="93001",ConfigColumnId="93003",YzProjectId=project});
                var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);await File.WriteAllTextAsync(Path.Combine(directory,"metadata/platform.json"),JsonSerializer.Serialize(catalog,json));await File.WriteAllTextAsync(Path.Combine(directory,"metadata/demo.json"),JsonSerializer.Serialize(original.Registry.Demo,json));
            }
            using var f=new PrincipalRealmFixture(super:false,file:Path.Combine(directory,"tests/Fake/Source.cs"));var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);
            f.Add("yz_role_permission",("id",991L),("yz_role_id",10L),("config_page_id",93001L),("permission_values","[\"is_query\"]"));var identity=await f.Identity();Assert.Contains("93002:is_query",identity.Permissions);Assert.DoesNotContain("93002:is_query",identity.FileQueryPermissions!);
            await f.Session.InTransactionAsync(async()=>
            {
                var first=await SecurityRealmCurrentReaderTests.Read(f,identity);Assert.Contains("93002:is_query",first.Permissions);
                f.Row("config_page_column",93004)["control_detail"]="{\"pageId\":\"93005\"}";
                var rebound=await SecurityRealmCurrentReaderTests.Read(f,identity);Assert.Contains("93001:is_query",rebound.Permissions);Assert.DoesNotContain("93002:is_query",rebound.Permissions);Assert.DoesNotContain("93005:is_query",rebound.Permissions);Assert.Contains("93002:is_query",first.Permissions);
                f.Row("config_page_column",93004)["control_detail"]="";var restored=await SecurityRealmCurrentReaderTests.Read(f,identity);Assert.Contains("93002:is_query",restored.Permissions);return true;
            });
        }
        finally
        {
            var target=Path.GetFullPath(directory);if(!target.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("继承测试临时目录越界。");if(Directory.Exists(target))Directory.Delete(target,true);
        }
    }

    /// <summary>错误Session、仓储和grant适配在任何读取前失败，不借另库或另事务上下文取得目录。</summary>
    [Fact]
    public async Task Wrong_session_or_repository_is_rejected_before_any_directory_or_grant_query()
    {
        using var f=new PrincipalRealmFixture();using var other=new PrincipalRealmFixture();var metadata=new MetadataRepository(f.Store,f.Registry);
        var correct=new PlatformSecurityCurrentResources(f.Store,metadata);var wrong=new PlatformSecurityCurrentResources(f.Store,new(other.Store,other.Registry));
        f.Provider.Commands.Clear();other.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>
        {
            var transaction=f.Session.CurrentTransaction!;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>correct.ReadForUpdateAsync(other.Session,transaction,1,1,CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>wrong.ReadForUpdateAsync(f.Session,transaction,1,1,CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new PlatformSecurityCurrentSessionPolicy(f.Store).ReadAgentGrantForUpdateAsync(other.Session,transaction,1,CancellationToken.None));
            return true;
        });
        Assert.True(f.Provider.Commands.Count==0,"错误上下文不得发出SQL。");Assert.True(other.Provider.Commands.Count==0,"错误仓储不得发出SQL。");
    }

    /// <summary>菜单存储定义缺失是能力错误，不能把未知表当作没有引用而允许删除后台。</summary>
    [Fact]
    public async Task Missing_menu_storage_is_not_interpreted_as_no_references()
    {
        using var f=new PrincipalRealmFixture();f.Registry.Platform.ConfigTables.RemoveAll(table=>table.TableName=="config_menu");
        // 仓储构造时已冻结发布白名单；必须在构造前缺失，不能用事后源对象变化伪造缺失能力。
        var missingStore=new SqlRowStore(f.Session,f.Registry.Platform);
        var adapter=new PlatformSecurityCurrentResources(missingStore,new(missingStore,f.Registry));f.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>adapter.HasAdminMenuReferencesForUpdateAsync(f.Session,f.Session.CurrentTransaction!,long.Parse(f.PlatformAdmin),CancellationToken.None));return true;
        });Assert.True(f.Provider.Commands.Count==0,"缺失菜单存储不得继续查询。");
    }

    /// <summary>Reader缺少适配在构造时拒绝，适配返回null或失败也不能退回发布资源继续授权。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Missing_or_failed_current_resources_never_fall_back_to_published_permissions(bool nullResult)
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity();var policy=new PlatformSecurityCurrentSessionPolicy(f.Store);
        Assert.Throws<ArgumentNullException>(()=>new SecurityRealmCurrentReader(f.Store,f.Registry,null!,policy));
        Assert.Throws<ArgumentNullException>(()=>new SecurityRealmCurrentReader(f.Store,f.Registry,new PlatformSecurityCurrentResources(f.Store,new(f.Store,f.Registry)),null!));
        var reader=new SecurityRealmCurrentReader(f.Store,f.Registry,new UnavailableResources(nullResult),policy);f.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>
        {
            if(nullResult)await Assert.ThrowsAsync<InvalidOperationException>(()=>SecurityRealmCurrentReaderTests.Read(f,identity,reader));
            else await Assert.ThrowsAsync<NotSupportedException>(()=>SecurityRealmCurrentReaderTests.Read(f,identity,reader));return true;
        });Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));
    }

    /// <summary>原复制与importMethod开关按平台投影进入真实Guard门禁，不能误复用修改开关或改变旧非空解释。</summary>
    [Theory]
    [InlineData("copy-on",true)][InlineData("copy-off",false)][InlineData("import-on",true)][InlineData("import-method",true)]
    [InlineData("import-space",false)][InlineData("import-number",true)][InlineData("import-null",false)]
    public void Copy_and_import_flags_use_the_original_effective_page_contract(string mode,bool allowed)
    {
        using var f=new PrincipalRealmFixture();var page=f.Registry.Platform.ConfigPages.First();page.IsCopy=mode=="copy-on"?1:0;page.IsImport=mode=="import-on"?1:0;page.IsSave=0;
        page.Extra.Remove("importMethod");if(mode=="import-method")page.Extra["importMethod"]=JsonSerializer.SerializeToElement(" handler ");
        if(mode=="import-space")page.Extra["importMethod"]=JsonSerializer.SerializeToElement("  ");if(mode=="import-number")page.Extra["importMethod"]=JsonSerializer.SerializeToElement(0);
        if(mode=="import-null")page.Extra["importMethod"]=JsonSerializer.SerializeToElement<object?>(null);
        var resource=((ISecurityResourceRegistry)f.Registry).PublishedCatalogs.SelectMany(catalog=>catalog.Pages).Single(value=>value.Id==page.Id);
        var operation=mode.StartsWith("copy",StringComparison.Ordinal)?RecordMutationOperation.Copy:RecordMutationOperation.Import;
        void Check()=>Invoke(typeof(SecurityRealmMutationGuard).GetMethod("EnsureEnabled",BindingFlags.Static|BindingFlags.NonPublic)!,null,[resource,operation]);
        if(allowed)Check();else Assert.Throws<UnauthorizedAccessException>(Check);
    }

    /// <summary>grant只在session锁后求值；撤销及坏时间仍先读session，再按同一规则拒绝。</summary>
    [Theory]
    [InlineData("approved",true)][InlineData("revoked",false)][InlineData("invalid-time",false)]
    public async Task Agent_proof_is_evaluated_after_the_original_grant_then_session_lock_order(string state,bool allowed)
    {
        using var f=new PrincipalRealmFixture();var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);
        f.Add("sys_agent_grant",("id",70L),("account_id",8L),("project_id",long.Parse(f.PlatformProject)),("client_id",AgentOAuthService.ClientId),("status","approved"),("refresh_expires_at",DateTime.UtcNow.AddHours(1)));
        f.WebSession["kind"]="agent";f.WebSession["grant_id"]=70L;var identity=await f.Identity();
        if(state=="revoked")f.Row("sys_agent_grant",70)["status"]="revoked";
        if(state=="invalid-time")f.Row("sys_agent_grant",70)["refresh_expires_at"]="无效时间";
        var observed=new ObservedPolicy(new(f.Store),f.Provider.Commands);var reader=new SecurityRealmCurrentReader(f.Store,f.Registry,new PlatformSecurityCurrentResources(f.Store,metadata),observed);f.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>
        {
            if(allowed)await SecurityRealmCurrentReaderTests.Read(f,identity,reader);else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>SecurityRealmCurrentReaderTests.Read(f,identity,reader));return true;
        });Assert.True(observed.Evaluated);Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));
    }

    /// <summary>后台删除的最终引用检查仍按角色→菜单→账号锁序，首个引用短路；只验证Guard，不假称执行删除。</summary>
    [Theory]
    [InlineData("role")][InlineData("menu")][InlineData("manager")][InlineData("none")]
    public async Task Admin_reference_locks_keep_the_original_order_and_short_circuit(string firstReference)
    {
        using var f=new PrincipalRealmFixture();var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);
        const long target=30L;f.Add("yz_admin_type",("id",target),("yz_project_id",long.Parse(f.PlatformProject)));
        if(firstReference=="role")f.Add("yz_role",("id",31L),("yz_admin_type_id",target));
        if(firstReference=="menu")f.Add("config_menu",("id",31L),("yz_admin_type_id",target),("yz_project_id",long.Parse(f.PlatformProject)));
        if(firstReference=="manager")f.Add("yz_manager",("id",31L),("yz_admin_type_id",target),("yz_project_id",long.Parse(f.PlatformProject)));
        var table=f.Registry.Platform.ConfigTables.Single(value=>value.TableName=="yz_admin_type");var page=f.Registry.Platform.ConfigPages.Single(value=>value.ConfigTableId==table.Id);
        page.IsDelete=1;f.Row("config_page",long.Parse(page.Id))["is_delete"]=1;
        var identity=await f.Identity();var context=new DefaultHttpContext();context.Items[typeof(AuthenticatedIdentity)]=identity;
        var authorization=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},f.Registry);var guard=new SecurityRealmMutationGuard(f.Store,f.Registry,authorization,f.CurrentReader());
        await f.Session.InTransactionAsync(async()=>
        {
            var transaction=f.Session.CurrentTransaction!;await guard.BeforeMutationAsync(new(page.Id,"yz_admin_type",RecordMutationOperation.Delete,identity.Actor,transaction){TargetId=target});
            var values=new Dictionary<string,object?>{{"yz_project_id",long.Parse(f.PlatformProject)}};
            var validation=new RecordMutationValidation(page.Id,"yz_admin_type",RecordMutationOperation.Delete,target,identity.Actor,values,values,new HashSet<string>(),transaction);
            f.Provider.Commands.Clear();
            if(firstReference=="none")await guard.BeforeWriteAsync(validation);else await Assert.ThrowsAsync<InvalidOperationException>(()=>guard.BeforeWriteAsync(validation));
            var afterCatalog=f.Provider.Commands.FindLastIndex(sql=>Table(sql)=="config_button");Assert.True(afterCatalog>=0);
            var expected=firstReference switch{"role"=>new[]{"yz_role"},"menu"=>new[]{"yz_role","config_menu"},_=>new[]{"yz_role","config_menu","yz_manager"}};
            Assert.Equal(expected,f.Provider.Commands.Skip(afterCatalog+1).Select(Table));Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));return true;
        });Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE ",StringComparison.Ordinal)||sql.StartsWith("DELETE ",StringComparison.Ordinal)||sql.StartsWith("INSERT ",StringComparison.Ordinal));
    }

    /// <summary>取回内部revision目录仅供测试验证绑定；不把该桥接开放为产品公共API。</summary>
    private static MetadataCatalog RequireLoaded(PlatformSecurityCurrentResources adapter,DatabaseSession session,DbTransaction transaction,SecurityResourceCatalog snapshot)=>
        (MetadataCatalog)Invoke(typeof(PlatformSecurityCurrentResources).GetMethod("RequireLoaded",BindingFlags.NonPublic|BindingFlags.Instance)!,adapter,[session,transaction,snapshot])!;
    private static object? Invoke(MethodInfo method,object? target,object?[] arguments)
    {try{return method.Invoke(target,arguments);}catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    private static string Table(string sql)=>System.Text.RegularExpressions.Regex.Match(sql," FROM `([^`]+)`").Groups[1].Value;
    private static async Task LockRealm(PrincipalRealmFixture f)
    {await f.Store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(f.PlatformProject)}});await f.Store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",long.Parse(f.PlatformAdmin)}});}
    private sealed class UnavailableResources(bool nullResult):ISecurityCurrentResources
    {
        public Task<SecurityResourceCatalog> ReadForUpdateAsync(DatabaseSession session,DbTransaction transaction,long projectId,long adminTypeId,CancellationToken cancellationToken)=>nullResult?
            Task.FromResult<SecurityResourceCatalog>(null!):Task.FromException<SecurityResourceCatalog>(new NotSupportedException("当前目录适配不可用。"));
        public Task<bool> HasAdminMenuReferencesForUpdateAsync(DatabaseSession session,DbTransaction transaction,long adminTypeId,CancellationToken cancellationToken)=>throw new NotSupportedException("菜单引用适配不可用。");
    }
    private sealed class ObservedPolicy(PlatformSecurityCurrentSessionPolicy original,List<string> commands):ISecurityCurrentSessionPolicy
    {
        internal bool Evaluated {get;private set;}
        public async Task<ISecurityLockedAgentGrant?> ReadAgentGrantForUpdateAsync(DatabaseSession session,DbTransaction transaction,long grantId,CancellationToken cancellationToken)
        {var proof=await original.ReadAgentGrantForUpdateAsync(session,transaction,grantId,cancellationToken);return proof is null?null:new ObservedProof(this,proof,commands);}
        private sealed class ObservedProof(ObservedPolicy owner,ISecurityLockedAgentGrant original,List<string> commands):ISecurityLockedAgentGrant
        {
            public bool IsValidFor(SecuritySessionBinding binding)
            {
                var grant=commands.FindIndex(sql=>Table(sql)=="sys_agent_grant");var session=commands.FindIndex(sql=>Table(sql)=="sys_session");
                Assert.True(grant>=0&&session>grant,"grant匹配必须延迟到原session锁之后。");owner.Evaluated=true;return original.IsValidFor(binding);
            }
        }
    }
}
