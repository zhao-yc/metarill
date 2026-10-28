using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json;
using Example.McpFixture.Generated.DTOs;
using Example.McpFixture.Generated.Services;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Mcp;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Mcp.Tests;

/// <summary>独立程序集、固定目录与真实方法分派合同；不把无数据库夹具计作正式业务验收。</summary>
public sealed class PublishedPageInvokerTests
{
    internal const string Page="2200000000000100020";
    /// <summary>公共执行器不能依赖平台Runtime/API/Generation或Agent。</summary>
    [Fact]
    public void Public_module_has_only_core_security_and_protocol_dependencies()
    {
        Assert.Equal("Metarill.Mcp",typeof(PublishedPageInvoker).Assembly.GetName().Name);
        Assert.DoesNotContain(typeof(PublishedPageInvoker).Assembly.GetReferencedAssemblies(),assembly=>assembly.Name is not null&&assembly.Name.StartsWith("Metarill.",StringComparison.Ordinal)&&assembly.Name is not("Metarill.Core" or "Metarill.Security"));
    }
    /// <summary>分页、明细和原三种记录写操作的路径/身份/取消参数保持。</summary>
    [Theory]
    [InlineData("list")][InlineData("detail")][InlineData("delete")][InlineData("copy")]
    public async Task Original_methods_receive_their_real_parameter_shape(string operation)
    {
        using var f=new Fixture();using var cancel=new CancellationTokenSource();await f.Invoker.InvokeAsync(Page,operation,Json(operation=="list"?"{\"parentId\":\"parent\",\"pageNo\":2}":"{\"id\":\"9007199254740993\"}"),true,f.Access,f.Access,f.Services,cancel.Token,"parent");
        Assert.Equal(new[]{operation},f.Service.Calls);Assert.Equal("parent",f.Service.Parent);Assert.Equal(f.Access.Identity.Actor,f.Service.Actor);Assert.Equal(cancel.Token,f.Service.Token);if(operation!="list")Assert.Equal(9007199254740993L,f.Service.Request);
    }
    /// <summary>强DTO保留省略/null并拒绝未知字段；MCP响应遵守字符串ID。</summary>
    [Fact]
    public async Task Strong_dtos_and_tool_json_keep_presence_null_and_string_ids()
    {
        using var f=new Fixture();var response=Assert.IsType<JsonElement>(await GeneratedPageMcpTools.Page(Page,"create",Json("{\"name\":\"测试\"}"),true,f.Invoker,f.Access,f.Access,f.Services,CancellationToken.None));Assert.Equal("9007199254740993",response.GetProperty("id").GetString());Assert.False(Assert.IsType<DemoCustomerPage2200000000000100020CreateRequest>(f.Service.Request).Notes.IsSpecified);
        await f.Invoker.InvokeAsync(Page,"update",Json("{\"id\":\"9007199254740993\",\"notes\":null}"),true,f.Access,f.Access,f.Services,CancellationToken.None);var update=Assert.IsType<DemoCustomerPage2200000000000100020UpdateRequest>(f.Service.Request);Assert.True(update.Notes.IsSpecified);Assert.Null(update.Notes.Value);
        var before=f.Service.Calls.Count;await Assert.ThrowsAsync<JsonException>(()=>f.Invoker.InvokeAsync(Page,"create",Json("{\"sql\":\"SELECT 1\"}"),true,f.Access,f.Access,f.Services,CancellationToken.None));Assert.Equal(before,f.Service.Calls.Count);
    }
    /// <summary>缺确认、Agent写、跨项目、身份不一致和拒绝页权均在解析Service前失败。</summary>
    [Theory]
    [InlineData("confirmation")][InlineData("agent-write")][InlineData("agent-project")][InlineData("mismatched-actor")][InlineData("no-session")][InlineData("permission")][InlineData("unknown-operation")]
    public async Task Rejected_invocations_never_resolve_or_call_service(string mode)
    {
        using var f=new Fixture();var operation=mode=="unknown-operation"?"GetType":mode=="agent-project"?"detail":"create";var confirmed=mode!="confirmation";
        if(mode.StartsWith("agent",StringComparison.Ordinal))f.Access.Identity=f.Access.Identity with{GrantId=70};
        if(mode=="agent-project")f.Access.Identity=f.Access.Identity with{Actor=f.Access.Identity.Actor with{ProjectId="999"}};
        if(mode=="mismatched-actor")f.Access.OverrideActor=f.Access.Identity.Actor with{AccountId=9};
        if(mode=="no-session")f.Access.Identity=f.Access.Identity with{SessionId=0};if(mode=="permission")f.Access.Denied.Add("create");
        var services=new NoServices();var data=Json(operation=="detail"?"{\"id\":\"9007199254740993\"}":"{\"name\":\"测试\"}");var error=await Record.ExceptionAsync(()=>f.Invoker.InvokeAsync(Page,operation,data,confirmed,f.Access,f.Access,services,CancellationToken.None));
        if(mode=="confirmation")Assert.IsType<InvalidOperationException>(error);else if(mode=="permission")Assert.IsType<SecurityException>(error);else if(mode=="unknown-operation")Assert.IsType<ArgumentException>(error);else Assert.IsType<UnauthorizedAccessException>(error);Assert.Equal(0,services.Calls);Assert.Empty(f.Service.Calls);
    }
    /// <summary>固定目录不会随调用者源对象变化；重复页面及命名空间路径注入提前拒绝。</summary>
    [Fact]
    public async Task Published_directory_is_frozen_and_does_not_accept_duplicate_or_type_path_inputs()
    {
        var catalog=Catalog();using var f=new Fixture(catalog);catalog.ConfigPages[0].Id="999";catalog.ConfigTables[0].TableName="changed";await f.Invoker.InvokeAsync(Page,"detail",Json("{\"id\":1}"),false,f.Access,f.Access,f.Services,CancellationToken.None);Assert.Single(f.Service.Calls);
        var source=Catalog();var app=new McpPublishedApplication(source,"Example.McpFixture",typeof(DemoCustomerService).Assembly);Assert.Throws<InvalidOperationException>(()=>new PublishedPageInvoker([app,app]));Assert.Throws<ArgumentException>(()=>new PublishedPageInvoker([app with{RootNamespace="Other/Assembly"}]));
    }
    /// <summary>目录只列实际允许动作；Agent列表不暗示可经MCP写入，外项目不泄露页面。</summary>
    [Fact]
    public void Directory_filters_permissions_and_agent_writes_on_every_call()
    {
        using var f=new Fixture();Assert.Equal(6,Assert.Single(f.Invoker.List(f.Access,f.Access)).Operations.Count);f.Access.Denied.Add("delete");Assert.DoesNotContain("delete",Assert.Single(f.Invoker.List(f.Access,f.Access)).Operations);
        f.Access.Identity=f.Access.Identity with{GrantId=70};Assert.Equal(new[]{"list","detail"},Assert.Single(f.Invoker.List(f.Access,f.Access)).Operations);f.Access.Identity=f.Access.Identity with{Actor=f.Access.Identity.Actor with{ProjectId="999"}};Assert.Empty(f.Invoker.List(f.Access,f.Access));
    }
    /// <summary>目录查询只读取检出中的公开示例，不连接数据库或配置文件。</summary>
    internal static MetadataCatalog Catalog([CallerFilePath]string file="")=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../metadata/demo.json"))),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    internal static JsonElement Json(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    private sealed class NoServices:IServiceProvider{internal int Calls{get;private set;}public object? GetService(Type serviceType){Calls++;throw new InvalidOperationException("不应解析Service。");}}
    internal sealed class Access:IRuntimeAuthorization,ISecurityIdentityAccessor
    {
        internal AuthenticatedIdentity Identity{get;set;}=new(new(8,"mcp-test",false,"2200000000000100000","2200000000000100030"),new HashSet<string>(),20,null);
        internal RuntimeActor? OverrideActor{get;set;}internal HashSet<string> Denied{get;}=[];
        public RuntimeActor RequireActor()=>OverrideActor??Identity.Actor;
        public AuthenticatedIdentity RequireIdentity()=>Identity;
        public void RequirePagePermission(string pageId,string operation){if(pageId!=Page||Denied.Contains(operation))throw new SecurityException("本次夹具无此页权。");}
    }
    internal sealed class Fixture:IDisposable
    {
        internal readonly PublishedPageInvoker Invoker;internal readonly Access Access=new();internal readonly DemoCustomerService Service=new();internal readonly ServiceProvider Services;
        internal Fixture(MetadataCatalog? catalog=null){Invoker=new([new(catalog??Catalog(),"Example.McpFixture",typeof(DemoCustomerService).Assembly)]);Services=new ServiceCollection().AddSingleton(Service).BuildServiceProvider();}
        public void Dispose()=>Services.Dispose();
    }
}
