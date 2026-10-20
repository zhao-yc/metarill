using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实可信生成物/授权实例/SQL翻译离线执行，不启动HTTP、数据库或默认Full发布。</summary>
public sealed class TrustedAdminProjectOptionsTests(TrustedSecurityServiceFixture fixture):IClassFixture<TrustedSecurityServiceFixture>
{
    private const string Page="1696799309006348290";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    /// <summary>只有factory完整冻结上下文可产生计划，原隐藏字段不增加页面和DOM或DTO写入口。</summary>
    [Fact]
    public void Issued_hidden_plan_preserves_ui_and_default_generation_without_a_project_crud_page()
    {
        var normal=GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace);
        var admin=normal.Tables.Single(table=>table.Source.Id==TrustedSecurityProjectOptionsCompilation.AdminTable);
        Assert.Null(TrustedSecurityProjectOptionsCompilation.ForTable(normal,admin));Assert.Empty(TrustedSecurityProjectOptionsRenderer.Service(normal,admin));
        var trusted=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        var source=trusted.Tables.Single(table=>table.Source.Id==admin.Source.Id);var plan=TrustedSecurityProjectOptionsCompilation.ForTable(trusted,source);
        Assert.NotNull(plan);Assert.Empty(plan.Project.Pages);Assert.DoesNotContain(plan.Page.DetailColumns,column=>column==plan.Column);Assert.DoesNotContain(plan.Page.Writable,column=>column==plan.Column);
        var original=UniappRenderer.Render(normal).ToDictionary(file=>file.Path,file=>file.Content);var current=UniappRenderer.Render(trusted).ToDictionary(file=>file.Path,file=>file.Content);
        Assert.Equal(original.Keys.Order(),current.Keys.Order());foreach(var file in original)Assert.Equal(file.Value,current[file.Key]);
        Assert.Contains(GenerationCapabilityAudit.Collect(trusted),gap=>gap.Usage=="runtime");Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(trusted.Catalog));
        var request=fixture.Type("Generated.DTOs.YzAdminTypePage"+Page+"CreateRequest");Assert.Null(request.GetProperty("YzProjectId"));
        Assert.Contains("provider.GetRequiredService<IRuntimeAuthorization>()",File.ReadAllText(Path.Combine(fixture.DirectoryPath,"generated/GeneratedRegistration.g.cs")));
        // 即使业务JSON自报同形标记，普通入口也不接受它。
        normal.Catalog.ConfigTables[0].Extra["trustedModule"]=JsonSerializer.SerializeToElement("security-management-v1");Assert.Null(TrustedSecurityProjectOptionsCompilation.ForTable(normal,admin));
    }
    /// <summary>受信后任何元数据/命名空间变动都不能降为普通候选或动态SQL。</summary>
    [Theory]
    [InlineData("namespace")][InlineData("sql")][InlineData("visible")][InlineData("target")]
    public void Changed_frozen_scope_is_rejected_instead_of_claiming_a_supported_relation(string change)
    {
        var model=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        if(change=="namespace")model=model with{Namespace="Other.Namespace"};
        if(change=="sql")model.Catalog.ConfigColumns.Single(column=>column.Id==TrustedSecurityProjectOptionsCompilation.ProjectColumn).ControlDetail="{\"sql\":\"select 1\"}";
        if(change=="visible")model.Catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==Page&&field.ConfigColumnId==TrustedSecurityProjectOptionsCompilation.ProjectColumn).Extra["isInEdit"]=JsonSerializer.SerializeToElement(1);
        if(change=="target")model.Catalog.ConfigTables.Single(table=>table.TableName=="yz_project").TableName="different_project";
        Assert.Throws<InvalidOperationException>(()=>TrustedSecurityProjectOptionsCompilation.ForTable(model,model.Tables.Single(table=>table.Source.Id==TrustedSecurityProjectOptionsCompilation.AdminTable)));
    }
    /// <summary>沿原四字段和长字符串ID；当前数据库名称是真实值，不回退包内名称或返回其它项目。</summary>
    [Theory]
    [InlineData("{}")] [InlineData("{\"formParameters\":{\"projectId\":\"42\",\"sql\":\"ignored\"}}")]
    public async Task Original_legacy_options_only_return_the_real_current_project(string body)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var row=run.Transport.Rows["yz_project"][0];row["name"]="实际项目改名";row["yz_project_id"]=null;
        run.Add("yz_project",("id",42L),("name","包外不得读"));var service=run.Service("YzAdminType");
        var result=await run.Call(service,"TrustedProjectOptionsAsync",Read("ReadTrustedProjectLegacy",body),run.Identity.Actor,CancellationToken.None);
        var option=Assert.Single(Serialize(result).EnumerateArray());Assert.Equal(fixture.Catalog.YzProjectId,option.GetProperty("id").GetString());Assert.Equal(fixture.Catalog.YzProjectId,option.GetProperty("value").GetString());Assert.Equal("实际项目改名",option.GetProperty("name").GetString());Assert.Equal("实际项目改名",option.GetProperty("text").GetString());
        Assert.Equal(new[]{"id","name","value","text"},option.EnumerateObject().Select(item=>item.Name));
        Assert.Same(run.Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>(),run.Scope.ServiceProvider.GetRequiredService<ISecurityIdentityAccessor>());
    }
    /// <summary>空ID/selected不退化全库；相同长ID可回读，外项目即使存在也整体拒绝。</summary>
    [Theory]
    [InlineData("null",0)] [InlineData("\"\"",0)] [InlineData("current",1)] [InlineData("numeric",1)]
    public async Task Legacy_explicit_empty_or_exact_selected_keeps_original_meaning(string id,int count)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var value=id=="current"?"\""+fixture.Catalog.YzProjectId+"\"":id=="numeric"?fixture.Catalog.YzProjectId:id;
        var result=await run.Call(run.Service("YzAdminType"),"TrustedProjectOptionsAsync",Read("ReadTrustedProjectLegacy","{\"id\":"+value+"}"),run.Identity.Actor,CancellationToken.None);
        Assert.Equal(count,Serialize(result).GetArrayLength());
        var selected=new RelationOptionsSelectedRequest{SelectedIds=[fixture.Catalog.YzProjectId,fixture.Catalog.YzProjectId]};Assert.Single(Serialize(await run.Call(run.Service("YzAdminType"),"TrustedProjectOptionsSelectedAsync",selected,run.Identity.Actor,CancellationToken.None)).EnumerateArray());
        var before=run.Transport.Commands.Count;await Assert.ThrowsAsync<ArgumentException>(()=>run.Call(run.Service("YzAdminType"),"TrustedProjectOptionsSelectedAsync",new RelationOptionsSelectedRequest{SelectedIds=["42"]},run.Identity.Actor,CancellationToken.None));Assert.Equal(before,run.Transport.Commands.Count);
    }
    /// <summary>原分页保持total/pages/current/size；非空关键词在数据库表达式中匹配而不是改客户端控件。</summary>
    [Theory]
    [InlineData(1,null,1,1)] [InlineData(2,null,0,1)] [InlineData(1,"固定",1,1)] [InlineData(1,"无匹配",0,0)]
    public async Task Page_and_keyword_are_bounded_to_the_one_real_project(int page,string? keyword,int records,int total)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);
        var result=Serialize(await run.Call(run.Service("YzAdminType"),"TrustedProjectOptionsPageAsync",new RelationOptionsPageRequest{PageNo=page,PageSize=5,Keyword=keyword},run.Identity.Actor,CancellationToken.None));
        Assert.Equal(records,result.GetProperty("records").GetArrayLength());Assert.Equal(total,long.Parse(result.GetProperty("total").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(total,long.Parse(result.GetProperty("pages").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(page,result.GetProperty("current").GetInt32());Assert.Equal(5,result.GetProperty("size").GetInt32());
    }
    /// <summary>候选使用真实Full授权类的源页任一开放动作，不增加不存在的目标项目页query权。</summary>
    [Theory]
    [InlineData("is_query")][InlineData("is_new")][InlineData("is_update")]
    public async Task Actual_web_authorization_accepts_each_original_source_action(string permission)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);SetIdentity(run,run.Identity with{Permissions=new HashSet<string>{Page+":"+permission}});
        Assert.Single(Serialize(await run.Call(run.Service("YzAdminType"),"TrustedProjectOptionsAsync",new RelationOptionsLegacyRequest(),run.Identity.Actor,CancellationToken.None)).EnumerateArray());
    }
    /// <summary>匿名、Agent、外realm、假actor或无页权均在ORM查询前拒绝，不能只拿RuntimeActor代持久身份。</summary>
    [Theory]
    [InlineData("anonymous")][InlineData("agent")][InlineData("session")][InlineData("project")][InlineData("admin")][InlineData("platform")][InlineData("actor")][InlineData("permission")]
    public async Task Missing_or_changed_persistent_identity_fails_before_candidate_query(string kind)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var actor=run.Identity.Actor;AuthenticatedIdentity? identity=run.Identity;
        if(kind=="anonymous")identity=null;if(kind=="agent")identity=identity! with{GrantId=3};if(kind=="session")identity=identity! with{SessionId=0};
        if(kind=="project"){actor=actor with{ProjectId="42"};identity=identity! with{Actor=actor,PrincipalProjectId="42"};}
        if(kind=="admin"){actor=actor with{AdminTypeId="42"};identity=identity! with{Actor=actor,PrincipalAdminTypeId="42"};}
        if(kind=="platform"){actor=actor with{IsAdministrator=true};identity=identity! with{Actor=actor};}
        if(kind=="actor")actor=actor with{AccountId=99};if(kind=="permission")identity=identity! with{Permissions=new HashSet<string>()};SetIdentity(run,identity);
        var probe=NoQueryService(run,run.Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>());using var readSession=probe.Session;
        var error=await Record.ExceptionAsync(()=>run.Call(probe.Service,"TrustedProjectOptionsAsync",new RelationOptionsLegacyRequest(),actor,CancellationToken.None));
        Assert.True(error is UnauthorizedAccessException or SecurityException,error?.ToString());Assert.Equal(0,probe.Orm.Selects);Assert.Empty(run.Writes);
    }
    /// <summary>删除/缺失真实项目不是空候选成功，包含显式空selected也不能绕过；不会复活或回退目录。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Deleted_or_missing_current_project_is_not_replaced_by_compiled_metadata(bool deleted)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);if(deleted)run.Transport.Rows["yz_project"][0]["delete_no"]=DateTime.UtcNow;else run.Transport.Rows["yz_project"].Clear();
        var before=run.Fingerprint();await Assert.ThrowsAsync<InvalidOperationException>(()=>run.Call(run.Service("YzAdminType"),"TrustedProjectOptionsSelectedAsync",new RelationOptionsSelectedRequest(),run.Identity.Actor,CancellationToken.None));Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Writes);
    }
    /// <summary>实际生成Controller直接执行，query不能指定project/admin；未启动服务器或HTTP客户端。</summary>
    [Fact]
    public async Task Actual_controller_rejects_query_scope_and_keeps_the_three_original_routes()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var type=fixture.Type("Generated.Controllers.YzAdminTypePage"+Page+"Controller");
        var controller=(ControllerBase)Activator.CreateInstance(type,run.Service("YzAdminType"),run.Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>())!;
        controller.ControllerContext=new(){HttpContext=new DefaultHttpContext()};controller.HttpContext.Request.QueryString=new("?projectId=42");var before=run.Transport.Commands.Count;
        await Assert.ThrowsAsync<ArgumentException>(()=>run.Call(controller,"TrustedProjectOptions",Parse("{}"),CancellationToken.None));Assert.Equal(before,run.Transport.Commands.Count);
        controller.HttpContext.Request.QueryString=QueryString.Empty;var result=Serialize(await run.Call(controller,"TrustedProjectOptions",Parse("{}"),CancellationToken.None));Assert.Equal(200,result.GetProperty("code").GetInt32());Assert.Single(result.GetProperty("data").EnumerateArray());
        var routes=type.GetMethods().SelectMany(method=>method.GetCustomAttributes<HttpPostAttribute>()).Select(attribute=>attribute.Template).Where(route=>route?.StartsWith(TrustedSecurityProjectOptionsPlan.Endpoint,StringComparison.Ordinal)==true).ToArray();
        Assert.Equal(new[]{TrustedSecurityProjectOptionsPlan.Endpoint,TrustedSecurityProjectOptionsPlan.Endpoint+"/page",TrustedSecurityProjectOptionsPlan.Endpoint+"/selected"},routes);
    }
    /// <summary>请求解析只允许既有槽，未知/重复SQL或超大form不可变成后端查询。</summary>
    [Theory]
    [InlineData("ReadTrustedProjectLegacy","{\"projectId\":\"42\"}")][InlineData("ReadTrustedProjectLegacy","{\"adminId\":\"42\"}")]
    [InlineData("ReadTrustedProjectLegacy","{\"sql\":\"select 1\"}")][InlineData("ReadTrustedProjectLegacy","{\"id\":null,\"id\":\"42\"}")]
    [InlineData("ReadTrustedProjectLegacy","{\"formParameters\":[]}")][InlineData("ReadTrustedProjectPage","{\"orderBy\":\"id\"}")]
    [InlineData("ReadTrustedProjectSelected","{\"keyword\":\"ignored\"}")][InlineData("ReadTrustedProjectSelected","null")]
    public void Unknown_or_duplicate_request_fields_do_not_acquire_query_scope(string method,string body)=>Assert.Throws<ArgumentException>(()=>Read(method,body));
    /// <summary>真实生成ORM调用到CurdBefore捕获SQL和驱动参数即中止；证明两列投影，不以ToSql字符串替代执行入口。</summary>
    [Fact]
    public async Task Actual_generated_query_only_projects_id_name_and_parameterizes_scope_and_keyword_before_open()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var opens=0;
        using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>new DeniedConnection(()=>opens++)).UseAutoSyncStructure(false).UseGenerateCommandParameterWithLambda(true).Build();
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=offline;Database=offline;User ID=unused;Password=unused;"},orm);
        var type=fixture.Type("Generated.Services.YzAdminTypeService");var mapper=Activator.CreateInstance(fixture.Type("Generated.Mappers.YzAdminTypeMapper"),session)!;
        var service=Activator.CreateInstance(type,session,mapper,new SnowflakeIdGenerator(882),Array.Empty<IRecordMutationObserver>(),run.Scope.ServiceProvider.GetServices<IRecordMutationGuard>(),run.Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>())!;
        string? sql=null;object?[]? values=null;orm.Aop.CurdBefore+=(_,args)=>{sql=args.Sql;values=args.DbParms.Select(parameter=>parameter.Value).ToArray();throw new CapturedSql();};
        var keyword="参数化'不能拼SQL";var error=await Record.ExceptionAsync(()=>run.Call(service,"ReadTrustedProjectAsync",run.Identity.Actor,CancellationToken.None,keyword));Assert.NotNull(error);Assert.NotNull(sql);Assert.NotNull(values);Assert.Equal(0,opens);
        var projection=sql[..sql.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.Equal(2,projection.Count(character=>character==',')+1);Assert.Contains("id",projection,StringComparison.OrdinalIgnoreCase);Assert.Contains("name",projection,StringComparison.OrdinalIgnoreCase);
        foreach(var secret in new[]{"mysql_conn_str","db_url","db_username","db_password","password","salt","old_password"})Assert.DoesNotContain(secret,sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete_no",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain(keyword,sql);Assert.Contains(values,value=>Convert.ToString(value)?.Contains(keyword,StringComparison.Ordinal)==true);Assert.Contains(values,value=>Convert.ToString(value)==fixture.Catalog.YzProjectId);
    }
    /// <summary>解析接受的父槽和分页仍须Service验证，不能借请求参数扩大一项目范围。</summary>
    [Theory]
    [InlineData("parent")][InlineData("target-parent")][InlineData("page-zero")][InlineData("page-size")][InlineData("keyword")][InlineData("selected-null")][InlineData("selected-many")]
    public async Task Invalid_parent_paging_or_selected_values_fail_before_query(string kind)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var probe=NoQueryService(run,run.Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>());using var readSession=probe.Session;var service=probe.Service;
        Func<Task> action;
        if(kind is "parent" or "target-parent")action=()=>run.Call(service,"TrustedProjectOptionsAsync",new RelationOptionsLegacyRequest{ParentId=kind=="parent"?"42":null,TargetParentId=kind=="target-parent"?"42":null},run.Identity.Actor,CancellationToken.None);
        else if(kind.StartsWith("selected",StringComparison.Ordinal))action=()=>run.Call(service,"TrustedProjectOptionsSelectedAsync",new RelationOptionsSelectedRequest{SelectedIds=kind=="selected-null"?null!:Enumerable.Repeat(fixture.Catalog.YzProjectId,201).ToList()},run.Identity.Actor,CancellationToken.None);
        else action=()=>run.Call(service,"TrustedProjectOptionsPageAsync",new RelationOptionsPageRequest{PageNo=kind=="page-zero"?0:1,PageSize=kind=="page-size"?101:20,Keyword=kind=="keyword"?new string('a',101):null},run.Identity.Actor,CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(action);Assert.Equal(0,probe.Orm.Selects);
    }
    /// <summary>候选成功不代替提交时当前读；真实项目后来删除时实际A协调器在同事务拒写并保持整行图。</summary>
    [Fact]
    public async Task Candidate_success_cannot_replace_the_final_write_guard_project_proof()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var service=run.Service("YzAdminType");
        Assert.Single(Serialize(await run.Call(service,"TrustedProjectOptionsAsync",new RelationOptionsLegacyRequest(),run.Identity.Actor,CancellationToken.None)).EnumerateArray());
        run.Transport.Rows["yz_project"][0]["delete_no"]=DateTime.UtcNow;var before=run.Fingerprint();
        var request=run.Request("YzAdminType",Page,"Create","{\"name\":\"项目已删除不得写入\"}");
        Assert.NotNull(await Record.ExceptionAsync(()=>run.Call(service,"Create"+Page+"Async",request,null,run.Identity.Actor,CancellationToken.None)));
        Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Writes);
    }
    /// <summary>仅有开发Actor不构成持久Web证明；缺接口或授权依赖均在ORM读前拒绝。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Missing_authorization_or_actor_only_development_contract_cannot_query(bool actorOnly)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var probe=NoQueryService(run,actorOnly?new ActorOnly(run.Identity.Actor):null);using var readSession=probe.Session;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Call(probe.Service,"TrustedProjectOptionsAsync",new RelationOptionsLegacyRequest(),run.Identity.Actor,CancellationToken.None));Assert.Equal(0,probe.Orm.Selects);
    }
    /// <summary>负例只能在Select之前失败：单独只读Session禁止任何ORM查询；此探针不执行守卫或声称证明写事务DI。</summary>
    private (object Service,DatabaseSession Session,QueryForbidden Orm) NoQueryService(TrustedSecurityServiceRun run,IRuntimeAuthorization? authorization)
    {
        var orm=DispatchProxy.Create<IFreeSql,QueryForbidden>();var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=offline;Database=offline;User ID=unused;Password=unused;"},orm);
        var type=fixture.Type("Generated.Services.YzAdminTypeService");var mapper=Activator.CreateInstance(fixture.Type("Generated.Mappers.YzAdminTypeMapper"),session)!;
        var service=Activator.CreateInstance(type,session,mapper,new SnowflakeIdGenerator(882),Array.Empty<IRecordMutationObserver>(),run.Scope.ServiceProvider.GetServices<IRecordMutationGuard>(),authorization)!;
        return(service,session,(QueryForbidden)orm);
    }
    /// <summary>不替代授权判断，仅统计所有查询入口并立即拒绝，防负例因没有记录ORM而假绿。</summary>
    public class QueryForbidden:DispatchProxy
    {
        internal int Selects;
        protected override object? Invoke(MethodInfo? method,object?[]? arguments)
        {
            if(method!.Name=="Dispose")return null;
            Selects++;throw new InvalidOperationException("负例不允许进入ORM查询。");
        }
    }
    /// <summary>仅作缺持久身份接口的开发合同反例，不复制Full授权判断。</summary>
    private sealed class ActorOnly(RuntimeActor actor):IRuntimeAuthorization{public RuntimeActor RequireActor()=>actor;public void RequirePagePermission(string page,string operation){}}
    /// <summary>只更换实际Full授权实例已有的已认证输入，不复制权限规则。</summary>
    private static void SetIdentity(TrustedSecurityServiceRun run,AuthenticatedIdentity? identity){var context=run.Scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;context.Items.Remove(typeof(AuthenticatedIdentity));if(identity is not null)context.Items[typeof(AuthenticatedIdentity)]=identity;}
    /// <summary>直接执行实际生成Service的固定解析方法，异常保留原始类型。</summary>
    private object Read(string method,string body)=>TrustedSecurityServiceFixture.Invoke(fixture.Type("Generated.Services.YzAdminTypeService"),method,Parse(body))!;
    /// <summary>JSON脱离文档生命周期后仍保留精确数值与显式null。</summary>
    private static JsonElement Parse(string body){using var document=JsonDocument.Parse(body);return document.RootElement.Clone();}
    /// <summary>按宿主现有long字符串转换验证协议，不自行改分页或ID类型。</summary>
    private static JsonElement Serialize(object? value){var options=new JsonSerializerOptions(Json);options.Converters.Add(new StringLongJsonConverter());return JsonSerializer.SerializeToElement(value,options);}
    private sealed class CapturedSql:Exception;
    /// <summary>离线SQL捕获没有可用连接，任何意外Open都计数并立即拒绝。</summary>
    private sealed class DeniedConnection(Action opening):DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"8.4";public override ConnectionState State=>ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName)=>throw new InvalidOperationException();public override void Open(){opening();throw new InvalidOperationException("离线禁止连接数据库");}public override void Close(){}
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)=>throw new InvalidOperationException();protected override DbCommand CreateDbCommand()=>throw new InvalidOperationException();
    }
}
