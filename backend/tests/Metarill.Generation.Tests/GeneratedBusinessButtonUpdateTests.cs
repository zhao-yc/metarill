using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>编译并执行真正生成的按钮更新；只替代数据库传输，不把离线执行当作实库锁/回滚证明。</summary>
public sealed class GeneratedBusinessButtonUpdateTests(BusinessButtonUpdateFixture fixture):IClassFixture<BusinessButtonUpdateFixture>
{
    /// <summary>普通更新开关开/关均能执行固定按钮更新，守卫和文件观察者接收同一来源及事务。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Button_update_uses_one_generated_pipeline_without_page_update_or_query(bool ordinaryUpdate)
    {
        using var run=fixture.Run(ordinaryUpdate);run.Actor=run.Actor with{IsAdministrator=false};var auth=new Authorization(run.Actor);var guard=new Guard(run);var observer=new Observer(run);var service=run.Service([guard],[observer],auth);
        var request=run.Request(RecordMutationOperation.Update,"{\"id\":\"1\",\"name\":\"手写归一\",\"attachments\":[]}")!;
        await Invoke(service,request,run.Actor);var prepared=Assert.Single(guard.Prepared);var validated=Assert.Single(guard.Validated);var context=Assert.Single(observer.Contexts);
        Assert.Equal("86001",prepared.BusinessButtonId);Assert.Equal(prepared.BusinessButtonId,validated.BusinessButtonId);Assert.Equal(prepared.BusinessButtonId,context.BusinessButtonId);Assert.Equal(RecordMutationOperation.Update,validated.Operation);
        Assert.Same(prepared.Transaction,validated.Transaction);Assert.Same(validated.Transaction,context.Transaction);Assert.Equal(0,auth.PageChecks);Assert.True(auth.ButtonChecks>=3);Assert.True(auth.ScopeChecks>0);
        Assert.Equal("已归一",context.SubmittedFields["name"]);Assert.Equal("[]",context.SubmittedFields["attachments"]);Assert.Contains("attachments",validated.SubmittedFields);
        Assert.True(run.Trace.IndexOf("prepare")<run.Trace.IndexOf("read:ForUpdate"));Assert.True(run.Trace.IndexOf("validate")<run.Trace.IndexOf("write:Update"));Assert.True(run.Trace.IndexOf("write:Update")<run.Trace.IndexOf("observer"));Assert.Equal("commit",run.Trace[^1]);Assert.Null(run.Session.CurrentTransaction);
        Assert.Equal(ordinaryUpdate,service.GetType().GetMethod("Update101Async") is not null);
        Assert.Throws<JsonException>(()=>run.Request(RecordMutationOperation.Update,"{\"id\":1,\"businessButtonId\":\"86001\"}"));
    }
    /// <summary>标准写入仍检查自己的权限，不能因同对象存在按钮而改用按钮授权。</summary>
    [Fact]
    public async Task Standard_update_does_not_borrow_button_permission()
    {
        using var denied=fixture.Run(true);denied.Actor=denied.Actor with{IsAdministrator=false};var deniedAuth=new Authorization(denied.Actor);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>denied.Invoke(denied.Service([],[],deniedAuth),RecordMutationOperation.Update,denied.Request(RecordMutationOperation.Update,"{\"id\":1,\"name\":\"标准更新\"}")));Assert.DoesNotContain("write:Update",denied.Trace);Assert.True(deniedAuth.PageChecks>0);
        using var allowed=fixture.Run(true);allowed.Actor=allowed.Actor with{IsAdministrator=false};var auth=new Authorization(allowed.Actor){PageAllowed=true};var guard=new Guard(allowed);var observer=new Observer(allowed);await allowed.Invoke(allowed.Service([guard],[observer],auth),RecordMutationOperation.Update,allowed.Request(RecordMutationOperation.Update,"{\"id\":1,\"name\":\"标准更新\"}"));
        Assert.Null(Assert.Single(guard.Prepared).BusinessButtonId);Assert.Null(Assert.Single(guard.Validated).BusinessButtonId);Assert.Null(Assert.Single(observer.Contexts).BusinessButtonId);Assert.Equal(0,auth.ButtonChecks);Assert.Equal("commit",allowed.Trace[^1]);
    }
    /// <summary>未知按钮、撤权和换Actor在打开事务前拒绝；不是先写后报错。</summary>
    [Theory][InlineData("unknown")][InlineData("revoked")][InlineData("actor")]
    public async Task Preflight_rejects_before_transaction_or_any_record_access(string defect)
    {
        using var run=fixture.Run(false);var auth=new Authorization(defect=="actor"?run.Actor with{AccountId=9}:run.Actor){Allowed=defect!="revoked"};var service=run.Service([],[],auth);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Invoke(service,run.Request(RecordMutationOperation.Update,"{\"id\":1}")!,run.Actor,defect=="unknown"?"86002":"86001"));Assert.Empty(run.Trace);Assert.Null(run.Session.CurrentTransaction);
    }
    /// <summary>对象规则与最终守卫仍在实际持久化前执行，拒绝后观察者不运行、同一事务回滚。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Object_rule_or_final_guard_rejection_prevents_write_and_observer(bool objectRule)
    {
        using var run=fixture.Run(false);var auth=new Authorization(run.Actor);var guard=new Guard(run){Reject=!objectRule};var observer=new Observer(run);var request=run.Request(RecordMutationOperation.Update,objectRule?"{\"id\":1,\"name\":\"拒绝\"}":"{\"id\":1,\"name\":\"有效\"}")!;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Invoke(run.Service([guard],[observer],auth),request,run.Actor));Assert.Single(guard.Prepared);Assert.Equal(objectRule?0:1,guard.Validated.Count);Assert.DoesNotContain("write:Update",run.Trace);Assert.Empty(observer.Contexts);Assert.Equal("rollback",run.Trace[^1]);
    }
    /// <summary>内部严格DTO仍拒绝字符串伪装数组；原HTTP兼容转换不等于放宽Service规则。</summary>
    [Fact]
    public async Task File_field_type_validation_is_shared_with_button_updates()
    {
        using var run=fixture.Run(false);var auth=new Authorization(run.Actor);var guard=new Guard(run);var observer=new Observer(run);
        var request=run.Request(RecordMutationOperation.Update,"{\"id\":1,\"attachments\":\"[]\"}")!;
        await Assert.ThrowsAsync<ArgumentException>(()=>Invoke(run.Service([guard],[observer],auth),request,run.Actor));Assert.Single(guard.Prepared);Assert.Empty(guard.Validated);Assert.Empty(observer.Contexts);Assert.DoesNotContain("write:Update",run.Trace);Assert.Equal("rollback",run.Trace[^1]);
    }
    /// <summary>反射只调用真实生成方法，解包同步边界异常，不替换Service实现。</summary>
    private static Task Invoke(object service,object request,RuntimeActor actor,string button="86001")
    {
        try{return(Task)service.GetType().GetMethod("UpdateButtonRecord101Async")!.Invoke(service,[request,null,button,actor,CancellationToken.None])!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private sealed class Guard(GeneratedMutationGuardRun run):IRecordMutationGuard
    {
        internal bool Reject;internal List<RecordMutationPreparation> Prepared=[];internal List<RecordMutationValidation> Validated=[];
        /// <summary>检查生成核心准备上下文。</summary>
        public Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken token=default){Prepared.Add(preparation);run.Trace.Add("prepare");return Task.CompletedTask;}
        /// <summary>保持最终守卫拒绝先于Mapper。</summary>
        public Task BeforeWriteAsync(RecordMutationValidation validation,CancellationToken token=default){Validated.Add(validation);run.Trace.Add("validate");if(Reject)throw new InvalidOperationException("最终守卫拒绝。");return Task.CompletedTask;}
    }
    private sealed class Observer(GeneratedMutationGuardRun run):IRecordMutationObserver
    {
        internal List<RecordMutationContext> Contexts=[];
        /// <summary>记录真正通知上下文；不模拟文件行或引用持久化。</summary>
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){Contexts.Add(context);run.Trace.Add("observer");return Task.CompletedTask;}
    }
    private sealed class Authorization(RuntimeActor actor):IRuntimeAuthorization
    {
        internal bool Allowed=true,PageAllowed;internal int ButtonChecks,PageChecks,ScopeChecks;
        /// <summary>真实身份测试输入固定，生成代码不得替换主体。</summary>
        public RuntimeActor RequireActor()=>actor;
        /// <summary>没有页面权限的业务更新若误走普通路径必须失败。</summary>
        public void RequirePagePermission(string pageId,string operation){PageChecks++;if(!PageAllowed)throw new UnauthorizedAccessException();}
        /// <summary>记录普通路径解析，沿用自己的页权。</summary>
        public ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField){RequirePagePermission(pageId,operation);return ExactDataManageValues.Unrestricted;}
        /// <summary>此处只模拟已认证的权限结论，实际角色聚合另验。</summary>
        public void RequireButtonPermission(string pageId,string buttonId){Assert.Equal("101",pageId);Assert.Equal("86001",buttonId);ButtonChecks++;if(!Allowed)throw new UnauthorizedAccessException();}
        /// <summary>按钮范围必须由生成记录锁消费，不能复用页面权限。</summary>
        public ExactDataManageValues ResolveButtonExactDataManage(string pageId,string buttonId,string managerField){RequireButtonPermission(pageId,buttonId);Assert.Equal("real_name",managerField);ScopeChecks++;return new(true,JsonSerializer.SerializeToElement("原名称"));}
    }
}

/// <summary>正式生成并严格编译普通更新开/关两种目录，已有传输夹具只负责离线事务/行回执。</summary>
public sealed class BusinessButtonUpdateFixture:IAsyncLifetime
{
    private readonly string boundary=Path.Combine(Path.GetTempPath(),"metarill-button-update-core");private string directory="";private readonly Dictionary<bool,Assembly> assemblies=[];
    /// <summary>不裁剪原CRUD或文件列，额外按钮和精确字段均来自元数据。</summary>
    public async Task InitializeAsync()
    {
        directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        foreach(var ordinary in new[]{false,true})
        {
            var root=Path.Combine(directory,ordinary?"open":"closed");var catalog=GeneratorTests.Catalog();var page=catalog.ConfigPages.Single();page.YzAdminTypeId="1";page.IsSave=ordinary?1:0;page.IsImport=0;page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(ordinary?1:0);page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");
            var admin=new Metarill.Core.Metadata.AdminTypeMetadata{Id="1",YzProjectId=catalog.YzProjectId};admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("realName");catalog.YzAdminTypes.Add(admin);
            catalog.ConfigColumns.Add(new(){Id="900",ConfigTableId="1",ColumnName="attachments",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});catalog.ConfigPageColumns.Add(new(){Id="901",ConfigPageId="101",ConfigColumnId="900",IsShow=1,IsEdit=1});
            catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="86001",configPageId="101",yzProjectId=catalog.YzProjectId,actionType=1,name="业务更新",sql="/admin/custom/business/update"}));
            new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.GuardContract");await File.WriteAllTextAsync(Path.Combine(root,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root,"generated/Services/CustomerService.cs"),"""
using Metarill.Core.Security;
using Example.GuardContract.Generated.Entities;
namespace Example.GuardContract.Generated.Services;
public partial class CustomerService
{
    /// <summary>同一对象规则对普通和按钮来源都生效，验证公共写入核心没有跳过手写partial。</summary>
    partial void BeforeSave(Customer entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {if(entity.Name=="拒绝")throw new InvalidOperationException("对象规则拒绝。");if(entity.Name=="手写归一"){entity.Name="已归一";submittedFields.Add("name");}}
}
""");
            assemblies[ordinary]=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
        }
    }
    /// <summary>每例独立Session与传输回执，不能共享事务或断言状态。</summary>
    public GeneratedMutationGuardRun Run(bool ordinary)=>new(assemblies[ordinary]);
    /// <summary>仅清理本夹具独占临时目录。</summary>
    public async Task DisposeAsync(){if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
}
