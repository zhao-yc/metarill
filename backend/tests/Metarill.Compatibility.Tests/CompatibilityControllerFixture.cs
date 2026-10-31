using Metarill.Api.Controllers;
using Metarill.Core.Persistence;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Metarill.Compatibility.Tests;

/// <summary>兼容控制器夹具不注册手写业务按钮；真实中央执行器保留，意外走该分支应拒绝而非默许执行。</summary>
internal sealed class CompatibilityControllerFixture:IDisposable
{
    private readonly ServiceProvider services=new ServiceCollection().BuildServiceProvider();
    internal CompatibilityButtonController Controller {get;}
    internal CompatibilityControllerFixture(CompatibilityButtonBusiness business)
    {
        var registry=CompatibilityButtonTests.Registry();var auth=new HttpRuntimeAuthorization(new HttpContextAccessor(),registry);
        var buttons=new OriginalBusinessButtonExecutor(registry,auth,services,new NoActions(),[],new RejectUnexpectedExecution());
        Controller=new(business,buttons);
    }
    public void Dispose()=>services.Dispose();
    /// <summary>本夹具只覆盖原兼容白名单，不伪造任何生成记录或已注册动作。</summary>
    private sealed class NoActions:IActionDescriptorCollectionProvider
    {public ActionDescriptorCollection ActionDescriptors{get;}=new(Array.Empty<ActionDescriptor>(),0);}
    /// <summary>新显式动作由独立完整包套件验证，不能借兼容夹具绕过其执行租约。</summary>
    private sealed class RejectUnexpectedExecution:IBusinessButtonExecutionGuard
    {public Task<T> ExecuteAsync<T>(string project,string button,Func<Task<T>> action,CancellationToken token=default)=>throw new InvalidOperationException("兼容夹具未声明手写业务动作。");}
}
