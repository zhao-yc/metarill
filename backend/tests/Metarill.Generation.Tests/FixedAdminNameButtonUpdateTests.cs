using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行真实生成按钮更新；仅终端传输离线替代，不冒充实库持久化或并发。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FixedAdminNameButtonUpdateTests(FixedAdminNameButtonFixture fixture):IClassFixture<FixedAdminNameButtonFixture>
{
    /// <summary>角色及配置表的按钮独立授权，写后名称回执不索取页面编辑或查询权。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Button_only_update_returns_the_admin_name_without_page_permission(bool configuration)
    {
        using var run=fixture.Run(configuration);var result=await run.Update();
        Assert.Equal("当前后台",result.GetProperty("yzAdminTypeName").GetString());Assert.Equal("新名称",result.GetProperty("name").GetString());
        Assert.Equal(0,run.Authorization.PageChecks);Assert.True(run.Authorization.ButtonChecks>=3);Assert.Equal(3,run.Authorization.CurrentTransactions.Count);
        Assert.All(run.Authorization.CurrentTransactions,transaction=>Assert.Same(run.Authorization.CurrentTransactions[0],transaction));
        Assert.Equal("Update",Assert.Single(run.Rows.Writes));Assert.Equal("commit",run.Transactions.Trace[^1]);Assert.Null(run.Session.CurrentTransaction);
        Assert.Single(run.NameQueries);
    }

    /// <summary>后台空值、外项目或软删除仍不回显名称，不能因按钮来源扩大目标读取。</summary>
    [Theory]
    [InlineData(false,"foreign")][InlineData(false,"deleted")]
    [InlineData(true,"null")][InlineData(true,"foreign")][InlineData(true,"deleted")]
    public async Task Button_name_echo_keeps_the_target_project_and_soft_delete_boundaries(bool configuration,string kind)
    {
        using var run=fixture.Run(configuration);
        if(kind=="null")run.Entity.GetType().GetProperty("YzAdminTypeId")!.SetValue(run.Entity,null);
        if(kind=="foreign")run.Admin.GetType().GetProperty("YzProjectId")!.SetValue(run.Admin,long.Parse(run.Actor.ProjectId,System.Globalization.CultureInfo.InvariantCulture)+1);
        if(kind=="deleted")run.Admin.GetType().GetProperty("DeleteNo")!.SetValue(run.Admin,DateTime.UtcNow);
        var result=await run.Update();Assert.Equal(JsonValueKind.Null,result.GetProperty("yzAdminTypeName").ValueKind);Assert.Equal("commit",run.Transactions.Trace[^1]);
        Assert.Equal(kind=="null"?0:1,run.NameQueries.Count());Assert.Equal(0,run.Authorization.PageChecks);
    }

    /// <summary>角色原必填后台不会因回显兼容而放开；物理写入前拒绝。</summary>
    [Fact]
    public async Task Required_role_admin_is_still_rejected_before_the_mapper()
    {
        using var run=fixture.Run(false);run.Entity.GetType().GetProperty("YzAdminTypeId")!.SetValue(run.Entity,null);
        var error=await Assert.ThrowsAsync<ArgumentException>(()=>run.Update());Assert.Equal("后台类型不能为空。",error.Message);
        Assert.Empty(run.Rows.Writes);Assert.Empty(run.NameQueries);Assert.Equal("rollback",run.Transactions.Trace[^1]);
    }
    /// <summary>未知按钮、换主体、已撤权和三处当前证明拒绝都保留原阶段及回滚。</summary>
    [Theory]
    [InlineData(false,"unknown")][InlineData(false,"actor")][InlineData(false,"revoked")]
    [InlineData(false,"current1")][InlineData(false,"current2")][InlineData(false,"current3")]
    [InlineData(true,"unknown")][InlineData(true,"actor")][InlineData(true,"revoked")]
    [InlineData(true,"current1")][InlineData(true,"current2")][InlineData(true,"current3")]
    public async Task Rejection_never_uses_page_permission_as_a_button_fallback(bool configuration,string kind)
    {
        using var run=fixture.Run(configuration);if(kind=="revoked")run.Authorization.ButtonAllowed=false;
        if(kind.StartsWith("current",StringComparison.Ordinal))run.Authorization.RejectCurrentAt=int.Parse(kind[^1..],System.Globalization.CultureInfo.InvariantCulture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Update(buttonId:kind=="unknown"?"86002":FixedAdminNameButtonFixture.Button,actor:kind=="actor"?run.Actor with{AccountId=9}:null));
        Assert.Equal(0,run.Authorization.PageChecks);Assert.Empty(run.NameQueries);
        if(kind is "unknown" or "actor" or "revoked"){Assert.Empty(run.Transactions.Trace);Assert.Empty(run.Rows.Writes);Assert.Empty(run.Rows.Commands);}
        else{Assert.Equal("rollback",run.Transactions.Trace[^1]);Assert.Equal(kind=="current3"?1:0,run.Rows.Writes.Count);}
    }

    /// <summary>普通更新继续只要求自己的页面权，已有按钮权不能借给标准入口。</summary>
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task Standard_update_remains_independent_from_button_authorization(bool configuration,bool allowed)
    {
        using var run=fixture.Run(configuration);run.Authorization.PageUpdateAllowed=allowed;run.Authorization.ButtonAllowed=false;
        if(allowed){var result=await run.Update(button:false);Assert.Equal("当前后台",result.GetProperty("yzAdminTypeName").GetString());Assert.Equal("commit",run.Transactions.Trace[^1]);}
        else
        {
            // 此例直接调用未装宿主守卫的Service，只证明回显拒绝且真实Session回滚；HTTP写前授权另由Controller回归证明。
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Update(button:false));Assert.Equal("Update",Assert.Single(run.Rows.Writes));Assert.Equal("rollback",run.Transactions.Trace[^1]);
        }
        Assert.True(run.Authorization.PageChecks>0);Assert.Equal(0,run.Authorization.ButtonChecks);Assert.Empty(run.Authorization.CurrentTransactions);
    }

    /// <summary>观察者后撤销按钮时，回显前同事务当前证明拒绝并回滚，不继续查询目标名称。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Revocation_after_the_observer_prevents_name_reads_and_rolls_back(bool configuration)
    {
        using var run=fixture.Run(configuration);var observer=new RevokeObserver(run.Authorization);run.Observers=[observer];
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Update());Assert.True(observer.Called);Assert.Equal("Update",Assert.Single(run.Rows.Writes));
        Assert.Equal("rollback",run.Transactions.Trace[^1]);Assert.Empty(run.NameQueries);Assert.Equal(0,run.Authorization.PageChecks);
    }
    /// <summary>只模拟观察者阶段撤权，仍由真实生成回执触发最终拒绝。</summary>
    private sealed class RevokeObserver(ButtonAuthorization authorization):IRecordMutationObserver
    {
        internal bool Called;
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){Called=true;Assert.NotNull(context.Transaction);authorization.ButtonAllowed=false;return Task.CompletedTask;}
    }

    /// <summary>原页和按钮权分别记录；当前证明必须使用同一真实Session事务。</summary>
    public sealed class ButtonAuthorization(RuntimeActor actor,string page):IRuntimeAuthorization
    {
        internal int PageChecks,ButtonChecks,RejectCurrentAt;internal bool ButtonAllowed=true,PageUpdateAllowed;
        internal List<DbTransaction> CurrentTransactions{get;}=[];
        /// <summary>测试真实主体固定，生成入口不能替换身份。</summary>
        public RuntimeActor RequireActor()=>actor;
        /// <summary>普通页权只允许明确开启的更新，不承接按钮权限。</summary>
        public void RequirePagePermission(string pageId,string operation){PageChecks++;if(pageId!=page||operation!="update"||!PageUpdateAllowed)throw new UnauthorizedAccessException("本例没有该普通页面权。");}
        /// <summary>本页固定按钮单独校验，撤权后不回退普通页权。</summary>
        public void RequireButtonPermission(string pageId,string buttonId){ButtonChecks++;if(pageId!=page||buttonId!=(page==FixedRoleAdminNameCompilation.ConfigurationTablePage?"86003":FixedAdminNameButtonFixture.Button)||!ButtonAllowed)throw new UnauthorizedAccessException("本例按钮未授权。");}
        /// <summary>记录同一事务的当前证明，精确阶段可模拟拒绝。</summary>
        public Task RequireButtonCurrentAsync(string pageId,string buttonId,RuntimeActor requested,DbTransaction transaction,CancellationToken cancellationToken=default)
        {Assert.Equal(actor,requested);Assert.NotNull(transaction.Connection);RequireButtonPermission(pageId,buttonId);CurrentTransactions.Add(transaction);if(CurrentTransactions.Count==RejectCurrentAt)throw new UnauthorizedAccessException("当前按钮权已撤销。");return Task.CompletedTask;}
    }
}

/// <summary>两个真实固定宿主共用一次生成编译；不更改发布产品元数据。</summary>
public sealed class FixedAdminNameButtonFixture:IAsyncLifetime
{
    internal const string Ns="Example.ButtonAdminName",Button="86001";
    private readonly string boundary=Path.Combine(Path.GetTempPath(),"metarill-button-admin-name");
    private string directory="";private Assembly assembly=null!;private string project="";
    /// <summary>保留真实稳定表／页／列身份，额外业务按钮明确来自测试元数据。</summary>
    public async Task InitializeAsync()
    {
        var catalog=FixedConfigurationAdminNameTests.Catalog();project=catalog.YzProjectId;
        var tables=new[]{FixedRoleAdminNameCompilation.RoleTable,FixedRoleAdminNameCompilation.AdminTable,FixedRoleAdminNameCompilation.ConfigurationTable};
        var pages=new[]{FixedRoleAdminNameCompilation.RolePage,FixedRoleAdminNameCompilation.AdminPage,FixedRoleAdminNameCompilation.ConfigurationTablePage};
        catalog.ConfigTables=catalog.ConfigTables.Where(table=>tables.Contains(table.Id)).ToList();
        catalog.ConfigColumns=catalog.ConfigColumns.Where(column=>tables.Contains(column.ConfigTableId)&&column.ColumnName is "id" or "name" or "yz_admin_type_id" or "yz_project_id" or "delete_no" or "insert_time").ToList();
        catalog.ConfigPages=catalog.ConfigPages.Where(page=>pages.Contains(page.Id)).ToList();var columns=catalog.ConfigColumns.Select(column=>column.Id).ToHashSet();
        catalog.ConfigPageColumns=catalog.ConfigPageColumns.Where(field=>pages.Contains(field.ConfigPageId)&&columns.Contains(field.ConfigColumnId)).ToList();
        catalog.ConfigMenus=[];catalog.ConfigChildren=[];catalog.ConfigCustomPages=[];catalog.ConfigIndexes=[];catalog.ConfigIndexColumns=[];
        catalog.ConfigButtons=pages.Where(page=>page!=FixedRoleAdminNameCompilation.AdminPage).Select(page=>JsonSerializer.SerializeToElement(new{id=Button,configPageId=page,yzProjectId=project,actionType=1,name="原业务更新",sql="/admin/custom/business/update"})).ToList();
        // 每页按钮ID必须唯一；两个宿主用独立固定ID，授权夹具使用本页实际ID。
        catalog.ConfigButtons[1]=JsonSerializer.SerializeToElement(new{id="86003",configPageId=FixedRoleAdminNameCompilation.ConfigurationTablePage,yzProjectId=project,actionType=1,name="原业务更新",sql="/admin/custom/business/update"});
        directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),Ns);
        assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
    }
    /// <summary>每例独立行、真实Session及受控连接，不共享授权或事务状态。</summary>
    internal RunContext Run(bool configuration)=>new(assembly,project,configuration);
    /// <summary>只清理本夹具独占目录。</summary>
    public async Task DisposeAsync(){if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

    internal sealed class RunContext:IDisposable
    {
        internal FixedRoleAdminNameTests.OfflineRows Rows{get;}=new(){AllowWrites=true};internal FixedRoleAdminNameTests.TransactionProvider Transactions{get;}=new();
        internal DatabaseSession Session{get;}internal RuntimeActor Actor{get;}internal object Entity{get;}internal object Admin{get;}
        internal FixedAdminNameButtonUpdateTests.ButtonAuthorization Authorization{get;}internal IRecordMutationObserver[] Observers=[];
        private readonly IFreeSql translation;private readonly Assembly assembly;private readonly string page,name,button;
        internal IEnumerable<string> NameQueries=>Rows.Commands.Where(sql=>sql.Contains("FROM `yz_admin_type`",StringComparison.OrdinalIgnoreCase));
        /// <summary>仅替换传输；SQL表达式交真实FreeSql，连接工厂始终拒绝网络。</summary>
        internal RunContext(Assembly assembly,string project,bool configuration)
        {
            this.assembly=assembly;name=configuration?"ConfigTable":"YzRole";page=configuration?FixedRoleAdminNameCompilation.ConfigurationTablePage:FixedRoleAdminNameCompilation.RolePage;button=configuration?"86003":Button;
            Actor=new(8015,"按钮操作者",false,project,"1");Authorization=new(Actor,page);
            translation=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("离线禁止数据库连接")).UseAutoSyncStructure(false).Build();
            var orm=DispatchProxy.Create<IFreeSql,FixedRoleAdminNameTests.ReadOrmProxy>();((FixedRoleAdminNameTests.ReadOrmProxy)orm).Initialize(translation,Rows);
            Session=new(new(){Provider="mysql",ExpectedDatabase="offline"},orm,Transactions);
            var entity=assembly.GetType(Ns+".Generated.Entities."+name,true)!;var admin=assembly.GetType(Ns+".Generated.Entities.YzAdminType",true)!;
            Entity=Activator.CreateInstance(entity)!;entity.GetProperty("Id")!.SetValue(Entity,1L);entity.GetProperty("Name")!.SetValue(Entity,"原名称");entity.GetProperty("YzAdminTypeId")!.SetValue(Entity,2L);entity.GetProperty("YzProjectId")!.SetValue(Entity,long.Parse(project,System.Globalization.CultureInfo.InvariantCulture));
            Admin=Activator.CreateInstance(admin)!;admin.GetProperty("Id")!.SetValue(Admin,2L);admin.GetProperty("Name")!.SetValue(Admin,"当前后台");admin.GetProperty("YzProjectId")!.SetValue(Admin,long.Parse(project,System.Globalization.CultureInfo.InvariantCulture));Rows.Data[entity]=[Entity];Rows.Data[admin]=[Admin];
        }
        /// <summary>调用真实生成公共入口，普通来源没有客户端可传的按钮槽。</summary>
        internal async Task<JsonElement> Update(bool button=true,string? buttonId=null,RuntimeActor? actor=null)
        {
            var mapper=Activator.CreateInstance(assembly.GetType(Ns+".Generated.Mappers."+name+"Mapper",true)!,Session)!;
            var service=Activator.CreateInstance(assembly.GetType(Ns+".Generated.Services."+name+"Service",true)!,[Session,mapper,new SnowflakeIdGenerator(965),Observers,Array.Empty<IRecordMutationGuard>(),Authorization])!;
            var dto=assembly.GetType(Ns+".Generated.DTOs."+name+"Page"+page+"UpdateRequest",true)!;var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var request=JsonSerializer.Deserialize("{\"id\":1,\"name\":\"新名称\"}",dto,json)!;Task task;
            try{task=(Task)service.GetType().GetMethod((button?"UpdateButtonRecord":"Update")+page+"Async")!.Invoke(service,button?[request,null,buttonId is null||buttonId==Button?this.button:buttonId,actor??Actor,CancellationToken.None]:[request,null,actor??Actor,CancellationToken.None])!;}
            catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            await task;return JsonSerializer.SerializeToElement(task.GetType().GetProperty("Result")!.GetValue(task),json);
        }
        /// <summary>释放本例Session和翻译器，不关闭其它测试资源。</summary>
        public void Dispose(){Session.Dispose();translation.Dispose();}
    }
}
