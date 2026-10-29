using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成/严格编译Service，真实固定SQL只替换ADO传输；不是MySQL或真实角色验收。</summary>
public sealed partial class DerivedRuleRecordGenerationTests
{
    /// <summary>物理/派生精确范围均保留；真实装载null与未装载默认值不同，客户端回显不能覆盖原记录。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task Actual_generated_rule_record_loads_derived_values_in_one_snapshot_and_preserves_scope(bool derivedScope,bool webWithoutParent)
    {
        var catalog=ExactDataManageGenerationTests.Catalog(true);var page=catalog.ConfigPages.Single(page=>page.Id=="101");
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("customerNotes == '禁止'");page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);page.IsDelete=1;page.IsCopy=1;
        if(derivedScope)page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_notes");
        if(webWithoutParent)
        {
            // 无父页面使用同样无父的候选目录；不能把原同父选择元数据改成未证明的上下文。
            page.WhereSql="";catalog.ConfigPages.Single(target=>target.Id=="201").WhereSql="";
        }
        page.WhereSql+=(string.IsNullOrWhiteSpace(page.WhereSql)?"":" AND ")+"private_payload IS NOT NULL";
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="86000",configPageId=page.Id,yzProjectId=catalog.YzProjectId,actionType=1,name="规则记录",sql="/admin/custom/derived-rule/probe",visibleScheme="VisibleWhenWritable"}));
        var model=GenerationModel.Create(catalog,"Example.DerivedRule");var table=model.Tables.Single(table=>table.Source.Id=="1");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-rule");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory,"RuleProbeController.cs"),RuleProbeControllerSource);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory,includeAgent:true);var entityType=assembly.GetType(model.Namespace+".Generated.Entities."+table.Name,true)!;
            var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            var plan=(NonRedundantReadPlan)serviceType.GetField("RuleRecordPlan101",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            var transport=new Transport(plan.OutputTypes);using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"},null,transport);
            var actor=new RuntimeActor(8,"派生条件",false,catalog.YzProjectId,page.YzAdminTypeId);var authorization=new Authorization(actor);
            var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
            var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(947),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            async Task<object?> Read(string operation)
            {
                authorization.Operation=operation;var task=(Task)serviceType.GetMethod("ReadAgentRecord101Async")!.Invoke(service,[Transport.Id,"71",operation,actor,authorization,CancellationToken.None])!;await task;return task.GetType().GetProperty("Result")!.GetValue(task);
            }
            foreach(var operation in new[]{"update","delete","copy"})
            {
                var record=Assert.IsAssignableFrom<IGeneratedRuleRecord>(await Read(operation));Assert.True(record.TryGetLoadedRuleValue("customer_notes",out var value));Assert.Equal("禁止",value);entityType.GetProperty("CustomerNotes")!.SetValue(record,"客户端可见属性后改");Assert.True(record.TryGetLoadedRuleValue("customer_notes",out var captured));Assert.Equal("禁止",captured);Assert.False(record.TryGetLoadedRuleValue("not_loaded",out _));Assert.Equal(operation,authorization.LastScope);
                var command=transport.Commands[^1];Assert.Contains(command.Values,value=>value is long id&&id==Transport.Id);if(webWithoutParent)Assert.DoesNotContain(command.Values,value=>value is long parent&&parent==71);else Assert.Contains(command.Values,value=>value is long parent&&parent==71);Assert.Contains("只允许甲",command.Values);Assert.DoesNotContain(Transport.Id.ToString(),command.Sql);Assert.DoesNotContain("禁止",command.Sql);Assert.Contains("IS NOT NULL",command.Sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("`delete_no`",command.Sql);Assert.Contains(" AS `customer_notes`",command.Sql);Assert.DoesNotContain("COUNT(",command.Sql);Assert.DoesNotContain("FOR SHARE",command.Sql);Assert.DoesNotContain("FOR UPDATE",command.Sql);Assert.Equal(IsolationLevel.RepeatableRead,command.Isolation);
            }
            Assert.Equal(3,transport.Commands.Count);Assert.Equal(3,transport.Commits);Assert.Equal(0,transport.Rollbacks);Assert.Null(session.CurrentTransaction);
            var forged=JsonSerializer.Deserialize("{\"id\":\"9007199254740993\",\"customerNotes\":\"允许\",\"generatedRuleValues\":{\"customer_notes\":\"允许\"}}",entityType,JsonDefaults.Options)!;
            Assert.False(((IGeneratedRuleRecord)forged).TryGetLoadedRuleValue("customer_notes",out _));Assert.DoesNotContain("generatedRuleValues",JsonSerializer.Serialize(forged,entityType,JsonDefaults.Options));
            // 真正能力目录与实际生成Service一同执行；只有ADO响应和当前角色结论为夹具。
            var definitions=new ServiceCollection();definitions.AddLogging();definitions.AddControllers().AddApplicationPart(assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));definitions.AddSingleton<IAgentHostResources>(new Resources(catalog));definitions.AddSingleton<IRuntimeAuthorization>(authorization);definitions.AddSingleton(serviceType,service);definitions.AddSingleton<GeneratedCapabilityIndex>();definitions.AddSingleton<AgentCapabilityCatalog>();var handler=new RuleHandler();var guard=new RuleExecutionGuard();definitions.AddSingleton<IOriginalBusinessButtonHandler>(handler);definitions.AddSingleton<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>(guard);definitions.AddSingleton<Metarill.Runtime.Security.ISecurityIdentityAccessor>(new RuleIdentity(new(actor,new HashSet<string>(),20,null)));definitions.AddSingleton<OriginalBusinessButtonExecutor>();using var services=definitions.BuildServiceProvider();
            var update=services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(action=>action.ControllerTypeInfo.Assembly==assembly&&action.AttributeRouteInfo?.Template?.EndsWith("/101/update",StringComparison.Ordinal)==true);
            var context=new DefaultHttpContext{RequestServices=services};context.Request.Method="POST";context.Request.ContentType="application/json";context.Request.Path="/"+update.AttributeRouteInfo!.Template;context.Request.QueryString=new("?parentId=71");context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(update),"实际生成更新"));
            authorization.Operation="update";var capabilities=services.GetRequiredService<AgentCapabilityCatalog>();var body=Encoding.UTF8.GetBytes("{\"id\":\"9007199254740993\",\"customerNotes\":\"允许\"}");
            var denied=await Assert.ThrowsAsync<AgentTaskException>(()=>capabilities.ValidateRecordWriteInvocationAsync("page:101:update",context,body));Assert.Equal("YZP_CAPABILITY_DENIED",denied.Code);Assert.Equal("not_applied",denied.Outcome);
            var action=services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(action=>action.MethodInfo.GetCustomAttribute<AgentActionAttribute>()?.Id=="derived-rule-probe");
            var actionContext=new DefaultHttpContext{RequestServices=services};actionContext.Request.Method="POST";actionContext.Request.Path="/"+action.AttributeRouteInfo!.Template;actionContext.Request.ContentType="application/json";actionContext.Request.QueryString=new("?parentId=71");actionContext.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(action),"真实显式动作"));
            await Assert.ThrowsAsync<BusinessRuleException>(()=>capabilities.ValidateActionInvocationAsync("action:derived-rule-probe",actionContext,Encoding.UTF8.GetBytes("{\"id\":\"9007199254740993\"}")));Assert.Equal(0,guard.Calls);Assert.Equal(0,handler.Calls);
            var original=new OriginalBusinessButtonRequest("86000",Transport.Id.ToString(),JsonSerializer.SerializeToElement(new{id=Transport.Id.ToString(),customerNotes="允许"}));var web=services.GetRequiredService<OriginalBusinessButtonExecutor>();
            if(webWithoutParent)await Assert.ThrowsAsync<BusinessRuleException>(()=>web.ExecuteAsync(original,context));else await Assert.ThrowsAsync<NotSupportedException>(()=>web.ExecuteAsync(original,context));Assert.Equal(0,guard.Calls);Assert.Equal(0,handler.Calls);
            transport.DerivedValue=null;await capabilities.ValidateActionInvocationAsync("action:derived-rule-probe",actionContext,Encoding.UTF8.GetBytes("{\"id\":\"9007199254740993\"}"));
            if(webWithoutParent){var allowed=Assert.IsType<OkObjectResult>(await web.ExecuteAsync(original,context));Assert.Equal(200,Assert.IsType<R<object>>(allowed.Value).Code);Assert.Equal(1,guard.Calls);Assert.Equal(1,handler.Calls);}
            await capabilities.ValidateRecordWriteInvocationAsync("page:101:update",context,body);var nullable=Assert.IsAssignableFrom<IGeneratedRuleRecord>(await Read("update"));Assert.True(nullable.TryGetLoadedRuleValue("customer_notes",out var nullValue));Assert.Null(nullValue);
            transport.HasRow=false;Assert.Null(await Read("delete"));authorization.Operation="update";var missing=await Assert.ThrowsAsync<AgentTaskException>(()=>capabilities.ValidateRecordWriteInvocationAsync("page:101:update",context,body));Assert.Equal("not_applied",missing.Outcome);transport.HasRow=true;
            var count=transport.Commands.Count;authorization.Allow=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read("copy"));Assert.Equal(count,transport.Commands.Count);authorization.Allow=true;
            var buttonTask=(Task)serviceType.GetMethod("ReadButtonRecord101Async")!.Invoke(service,[Transport.Id,"71","86000",actor,CancellationToken.None])!;await buttonTask;Assert.Equal("86000",authorization.LastButtonScope);
            var lockTask=(Task)serviceType.GetMethod("LockButtonRecord101Async")!.Invoke(service,[Transport.Id,"71","86000",actor,CancellationToken.None])!;await Assert.ThrowsAsync<InvalidOperationException>(async()=>await lockTask);Assert.Null(session.CurrentTransaction);
            // FreeSql MySQL驱动要求实际MySqlConnection；离线分别验证原排他SQL和同事务派生当前读。
            transport.DerivedValue="禁止";transport.Commands.Clear();
            await session.InTransactionAsync(async()=>
            {
                var permission=(Task)serviceType.GetMethod("RequireButtonCurrent101Async",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[Transport.Id,"86000",actor,CancellationToken.None])!;await permission;
                var query=serviceType.GetMethod("ButtonRecordQuery101",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[Transport.Id,"71","86000",actor,true])!;
                var sql=(string)typeof(DerivedRuleRecordGenerationTests).GetMethod(nameof(QuerySql),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(entityType).Invoke(null,[query])!;Assert.Contains("FOR UPDATE",sql,StringComparison.OrdinalIgnoreCase);
                var locked=(Task)serviceType.GetMethod("ReadRuleRecord101Async",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[Transport.Id,"71","86000",actor,true,true,CancellationToken.None])!;await locked;
                var row=Assert.IsAssignableFrom<IGeneratedRuleRecord>(locked.GetType().GetProperty("Result")!.GetValue(locked));Assert.True(row.TryGetLoadedRuleValue("customer_notes",out var current));Assert.Equal("禁止",current);
                Assert.All(transport.Commands,command=>{Assert.Same(session.CurrentTransaction,command.Transaction);Assert.Contains("FOR SHARE",command.Sql,StringComparison.OrdinalIgnoreCase);});return true;
            });
            Assert.Equal(1,authorization.CurrentCalls);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>直接消费实际FreeSql查询对象的SQL，不复制ORM或权限算法。</summary>
    private static string QuerySql<T>(object query)where T:class=>((ISelect<T>)query).ToSql();
    /// <summary>手写显式动作只用于离线入口发现，产品仍从真实生成Service读取规则记录。</summary>
    private const string RuleProbeControllerSource="""
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Mvc;
namespace Example.DerivedRule.Controllers;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuleProbeRequest { public long Id {get;set;} }
[ApiController][Route("api/admin/custom/derived-rule")]
public sealed class RuleProbeController:ControllerBase
{
    /// <summary>本例仅验证门禁，不把测试处理器当成已迁移的实际业务。</summary>
    [HttpPost("probe")][AgentAction("derived-rule-probe","规则记录","86000")]
    public R<object> Probe([FromBody]RuleProbeRequest request,[FromQuery]string? parentId)=>R<object>.Success(new{id=request.Id,parentId});
}
""";
    private sealed class RuleIdentity(Metarill.Runtime.Security.AuthenticatedIdentity identity):Metarill.Runtime.Security.ISecurityIdentityAccessor
    {public Metarill.Runtime.Security.AuthenticatedIdentity RequireIdentity()=>identity;}
    /// <summary>记录只有门禁通过后才进入执行器，测试不伪造数据库租约或真实业务写入。</summary>
    private sealed class RuleExecutionGuard:Metarill.Core.Persistence.IBusinessButtonExecutionGuard
    {internal int Calls;public async Task<T> ExecuteAsync<T>(string project,string button,Func<Task<T>> action,CancellationToken cancellationToken=default){Calls++;return await action();}}
    private sealed class RuleHandler:IOriginalBusinessButtonHandler
    {internal int Calls;public string ButtonId=>"86000";public string Path=>"/admin/custom/derived-rule/probe";public Task<IActionResult> ExecuteAsync(OriginalBusinessButtonRequest request,CancellationToken cancellationToken){Calls++;return Task.FromResult<IActionResult>(new OkObjectResult(R<object>.Success(new{id=request.EntityId})));}}
    /// <summary>固定授权替身仅给当前写操作与指定按钮，不提供普通query补权。</summary>
    private sealed class Authorization(RuntimeActor actor,string expectedPage="101"):IRuntimeAuthorization
    {
        internal string Operation="update",LastScope="",LastButtonScope="";internal bool Allow=true;internal int CurrentCalls;
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation){Assert.Equal(expectedPage,page);if(!Allow||operation!=Operation)throw new UnauthorizedAccessException("未授其它操作。");}
        public void RequireButtonPermission(string page,string button){Assert.Equal(expectedPage,page);Assert.Equal("86000",button);if(!Allow)throw new UnauthorizedAccessException();}
        /// <summary>当前授权替身验证事务传递；不冒充正式角色锁或并发撤权。</summary>
        public Task RequireButtonCurrentAsync(string page,string button,RuntimeActor current,DbTransaction transaction,CancellationToken cancellationToken=default){Assert.Equal(actor,current);Assert.NotNull(transaction.Connection);RequireButtonPermission(page,button);CurrentCalls++;return Task.CompletedTask;}
        public ExactDataManageValues ResolveExactDataManage(string page,string operation,string field){RequirePagePermission(page,operation);LastScope=operation;return new(true,JsonSerializer.SerializeToElement("[\"只允许甲\"]"));}
        public ExactDataManageValues ResolveButtonExactDataManage(string page,string button,string field){RequireButtonPermission(page,button);LastButtonScope=button;return new(true,JsonSerializer.SerializeToElement("[\"只允许甲\"]"));}
    }
    private sealed class Resources(Metarill.Core.Metadata.MetadataCatalog catalog):IAgentHostResources
    {public AgentHostCatalog AgentCatalog{get;}=AgentHostCatalog.ForApplication(catalog);public IReadOnlyList<Metarill.Runtime.Security.SecurityResourceCatalog> PublishedCatalogs=>[];public bool IsPlatformRealm(string project,string admin)=>false;}
    /// <summary>传输记录真实命令/参数/事务，仅返回固定测试行，不解析或模拟SQL授权判断。</summary>
    private sealed class Transport(IReadOnlyDictionary<string,string> outputs):IDatabaseProvider
    {
        internal const long Id=9007199254740993;internal string ProjectId="54";internal string? DerivedValue="禁止";internal string TargetIds="[101,102]";internal string[] TargetNames=["快照甲","快照乙"];internal Action? OnRootRead;internal bool HasRow=true;internal int Commits,Rollbacks;internal readonly List<(string Sql,object?[] Values,IsolationLevel Isolation,DbTransaction Transaction)> Commands=[];
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this);public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken cancellationToken=default)=>connection.BeginTransactionAsync(IsolationLevel.RepeatableRead,cancellationToken);
        public Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken cancellationToken=default){Assert.Equal(IsolationLevel.RepeatableRead,transaction.IsolationLevel);return Task.CompletedTask;}
        internal DbDataReader Read(DbCommand command,DbTransaction? transaction)
        {
            Assert.NotNull(transaction);Assert.StartsWith("SELECT ",command.CommandText);Commands.Add((command.CommandText,command.Parameters.Cast<DbParameter>().Select(value=>value.Value).ToArray(),transaction.IsolationLevel,transaction));
            var data=new DataTable();
            if(command.CommandText.StartsWith("SELECT multi_value.",StringComparison.Ordinal))
            {data.Columns.Add("id",typeof(long));data.Columns.Add("name",typeof(string));var names=((Transaction)transaction).Names;for(var index=0;index<names.Length;index++)data.Rows.Add(101L+index,names[index]);return data.CreateDataReader();}
            var extras=System.Text.RegularExpressions.Regex.Matches(command.CommandText,@" AS `(metarill_multi_input_[0-9]+)`").Select(match=>match.Groups[1].Value).ToArray();foreach(var name in outputs.Keys.Concat(extras))data.Columns.Add(name,typeof(object));
            if(HasRow)data.Rows.Add(outputs.Keys.Concat(extras).Select(name=>extras.Contains(name)?(object)TargetIds:name switch{"id"=>(object)Id,"customer_notes"=>DerivedValue??(object)DBNull.Value,"target_ids"=>TargetIds,"name" or "private_payload"=>"只允许甲","yz_project_id"=>ProjectId,"customer_id"=>"9","group_id" or "parent_id"=>"71",_=>DBNull.Value}).ToArray());OnRootRead?.Invoke();return data.CreateDataReader();
        }
    }
    private sealed class Connection(Transport owner):DbConnection
    {
        private ConnectionState state;[AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"8.4";public override ConnectionState State=>state;public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)=>new Transaction(this,owner,isolationLevel);protected override DbCommand CreateDbCommand()=>new Command(this,owner);
    }
    private sealed class Transaction(DbConnection connection,Transport owner,IsolationLevel isolation):DbTransaction
    {internal readonly string[] Names=owner.TargetNames.ToArray();public override IsolationLevel IsolationLevel=>isolation;protected override DbConnection DbConnection=>connection;public override void Commit()=>owner.Commits++;public override void Rollback()=>owner.Rollbacks++;}
    private sealed class Command(DbConnection connection,Transport owner):DbCommand
    {
        private readonly MySqlCommand parameters=new();[AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override void Cancel(){}public override void Prepare(){}public override object? ExecuteScalar()=>throw new NotSupportedException("禁止COUNT");public override int ExecuteNonQuery()=>throw new NotSupportedException("禁止DML");protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>owner.Read(this,DbTransaction);
    }
}
