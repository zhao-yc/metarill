using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.GlobalSample.Business;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式完整包实际编译与执行原手写业务；仅数据库传输使用内存替身，不冒充MySQL锁/回滚验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SampleContractActionPackageTests(SampleContractActionPackageFixture fixture) : IClassFixture<SampleContractActionPackageFixture>
{
    /// <summary>真实MVC发现三个原标识与确认设置，声明的正文仍是严格请求而非任意更新实体。</summary>
    [Fact]
    public void Actual_full_package_discovers_three_original_actions_and_preserves_all_handwritten_sources()
    {
        using var run = fixture.Run(); var services = new ServiceCollection(); services.AddLogging(); services.AddControllers().AddApplicationPart(fixture.Assembly);
        using var provider = services.BuildServiceProvider(); var index = new GeneratedCapabilityIndex(provider.GetRequiredService<IActionDescriptorCollectionProvider>(), new Published(fixture.Metadata));
        var catalog = new AgentCapabilityCatalog(index, run.Authorization);
        foreach (var (name, button, confirmation) in new[] { ("preview", "86000", false), ("complete", "86001", true), ("rollback", "86002", true) })
        {
            var action = catalog.Describe("action:sample-contract-" + name); Assert.Equal(button, action.ButtonId); Assert.Equal("action", action.Operation); Assert.Equal(confirmation, action.ConfirmationRequired);
            Assert.Equal("/admin/custom/sample-contract-agent/" + name, action.Path);
            using var schema = JsonDocument.Parse(JsonSerializer.Serialize(action.RequestSchema)); Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(new[] { "id", "expectedDigest" }, schema.RootElement.GetProperty("properties").EnumerateObject().Select(value => value.Name));
        }
        Assert.Equal(0, run.Authorization.PageChecks); Assert.True(fixture.HandwrittenProtected);
    }
    /// <summary>预览不打开写事务或发出更新，摘要与真实实体业务值一致。</summary>
    [Fact]
    public async Task Preview_reads_actual_generated_service_without_writing_or_requiring_query_permission()
    {
        using var run = fixture.Run(); var expected = run.Digest(); var result = await run.Invoke("PreviewAsync", 7L, CancellationToken.None);
        Assert.Equal(expected, Text(result, "ExpectedDigest")); Assert.Equal("待处理", Text(result, "CurrentRemarks")); Assert.Equal("已处理", Text(result, "NextRemarks"));
        Assert.DoesNotContain("begin", run.Trace); Assert.DoesNotContain("write", run.Trace); Assert.Null(run.Session.CurrentTransaction); Assert.Equal("待处理", run.Get("Remarks")); Assert.Equal(0, run.Authorization.PageChecks);
    }
    /// <summary>摘要匹配与待处理状态在锁后核验，生成更新及观察者加入同一外层事务。</summary>
    [Fact]
    public async Task Complete_locks_then_uses_actual_button_update_and_commits_once()
    {
        using var run = fixture.Run(); var expected = run.Digest(); var result = await run.Invoke("CompleteAsync", 7L, expected, false, CancellationToken.None);
        Assert.Equal("已处理", run.Get("Remarks")); Assert.Equal("已处理", Text(result, "Remarks")); Assert.Equal(expected, Text(result, "PreviousDigest"));
        Assert.Equal(1, run.Trace.Count(value => value == "begin")); Assert.Equal(1, run.Trace.Count(value => value == "commit")); Assert.Equal(1, run.Trace.Count(value => value == "write"));
        Assert.True(run.Trace.IndexOf("lock") < run.Trace.IndexOf("write")); Assert.Equal("commit", run.Trace[^1]); Assert.Null(run.Session.CurrentTransaction);
        var notification = Assert.Single(run.Observer.Contexts); Assert.Equal("86001", notification.BusinessButtonId); Assert.Contains("remarks", notification.SubmittedFields.Keys);
        Assert.All(run.Transactions, transaction => Assert.Same(notification.Transaction, transaction)); Assert.Equal(0, run.Authorization.PageChecks);
    }
    /// <summary>第二次真实生成写入被原partial金额规则拒绝，首笔更新尚未提交并回滚。</summary>
    [Fact]
    public async Task Rollback_action_rejects_second_write_and_restores_first_in_same_outer_transaction()
    {
        using var run = fixture.Run(); var expected = run.Digest();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => run.Invoke("CompleteAsync", 7L, expected, true, CancellationToken.None)); Assert.Equal("合同金额不能小于零", error.Message);
        Assert.Equal("待处理", run.Get("Remarks")); Assert.Equal(10.00m, run.Get("Amount")); Assert.Equal(1, run.Trace.Count(value => value == "write")); Assert.DoesNotContain("commit", run.Trace); Assert.Equal("rollback", run.Trace[^1]);
        Assert.Equal("86002", Assert.Single(run.Observer.Contexts).BusinessButtonId); Assert.Null(run.Session.CurrentTransaction);
    }
    /// <summary>缺摘要、过期、错误大小写或状态冲突均在首笔SQL前拒绝；锁后变化不能复用旧预览。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("uppercase")]
    [InlineData("state")]
    [InlineData("locked-change")]
    public async Task Conflict_before_first_update_never_notifies_or_commits(string reason)
    {
        using var run = fixture.Run(); string? digest = run.Digest();
        if (reason == "missing") digest = null;
        if (reason == "stale") run.Set("Name", "预览后改名");
        if (reason == "uppercase") digest = digest!.ToUpperInvariant();
        if (reason == "state") { run.Set("Remarks", "已处理"); digest = run.Digest(); }
        if (reason == "locked-change") run.BeforeLockedRead = () => run.Set("Name", "锁定当前值已变");
        var error = await Record.ExceptionAsync(() => run.Invoke("CompleteAsync", 7L, digest, false, CancellationToken.None)); Assert.NotNull(error); Assert.Equal("SampleContractConflictException", error.GetType().Name);
        Assert.DoesNotContain("write", run.Trace); Assert.DoesNotContain("commit", run.Trace); Assert.Empty(run.Observer.Contexts); Assert.Equal("rollback", run.Trace[^1]);
    }
    /// <summary>按钮撤销不借query权限补权；锁后授权器变更也在生成更新前拒绝。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Button_denial_before_transaction_or_after_lock_prevents_write(bool afterLock)
    {
        using var run = fixture.Run(); var expected = run.Digest();
        if (afterLock) run.BeforeLockedRead = () => run.Authorization.Allowed = false; else run.Authorization.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => run.Invoke("CompleteAsync", 7L, expected, false, CancellationToken.None)); Assert.DoesNotContain("write", run.Trace); Assert.Empty(run.Observer.Contexts);
        if (!afterLock) Assert.Empty(run.Trace); else Assert.Equal("rollback", run.Trace[^1]);
    }
    /// <summary>原Controller的Web冲突保留409；Agent明确冲突由共享中间件继续处理为unknown任务。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_controller_preserves_original_web_conflict_and_explicit_agent_conflict(bool agent)
    {
        using var run = fixture.Run(); var controller = (ControllerBase)Activator.CreateInstance(fixture.Assembly.GetType("Metarill.GlobalSample.Controllers.SampleContractAgentController", true)!, run.Business)!;
        var context = new DefaultHttpContext(); controller.ControllerContext = new() { HttpContext = context };
        if (agent) context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(new(run.Authorization.RequireActor(), new HashSet<string>(), 1, 2), "agent", 2);
        var request = JsonSerializer.Deserialize("{\"id\":\"7\",\"expectedDigest\":\"过期摘要\"}", fixture.Assembly.GetType("Metarill.GlobalSample.Controllers.ContractChangeRequest", true)!, Metarill.Runtime.JsonDefaults.Options)!;
        var task = (Task)controller.GetType().GetMethod("Complete")!.Invoke(controller, [request, CancellationToken.None])!;
        if (agent) await Assert.ThrowsAsync<AgentActionConflictException>(async () => await task);
        else { await task; var result = task.GetType().GetProperty("Result")!.GetValue(task)!; Assert.Equal(409, context.Response.StatusCode); Assert.Equal(409, result.GetType().GetProperty("Code")!.GetValue(result)); }
        Assert.DoesNotContain("write", run.Trace); Assert.Equal("rollback", run.Trace[^1]);
    }
    /// <summary>实际启动完整Web宿主验证三个路由没有绕过身份；空数据库配置不启动真实连接。</summary>
    [Fact]
    public async Task Actual_full_host_denies_anonymous_calls_to_all_three_actions()
    {
        var hostDirectory = Path.Combine(fixture.PackageDirectory, "backend/Application.Host");
        var start = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory);
        start.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; start.Environment["DOTNET_ENVIRONMENT"] = "Production"; start.Environment["Authentication__Mode"] = "Web"; start.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
        start.Environment["Database__ConnectionString"] = ""; start.Environment["Database__ExpectedDatabase"] = "";
        var evidence = Path.Combine(NonRedundantApplicationFixture.Root(), ".local", "sample-actions-http-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(evidence);
        start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = evidence;
        await using var host = await FreshHost.StartAsync(start, "/health");
        foreach (var action in new[] { "preview", "complete", "rollback" })
        {
            using var response = await host.Client.PostAsJsonAsync("/api/admin/custom/sample-contract-agent/" + action, new { id = "7" }); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var central=await host.Client.PostAsJsonAsync("/api/admin/sys/config_button/execute",new{buttonId="86000",entityId="7"});Assert.Equal(HttpStatusCode.Unauthorized,central.StatusCode);
    }
    /// <summary>测试只读取实际响应记录的公开业务字段，不反射改写业务实现。</summary>
    /// <summary>原详情正文经真实模板中央控制器/处理器/Business/Service执行，不改变页面或以服务器新值补摘要。</summary>
    [Theory][InlineData("preview")][InlineData("complete")][InlineData("stale")][InlineData("rollback")][InlineData("missing-original")][InlineData("numeric")][InlineData("fraction")][InlineData("explicit")]
    public async Task Original_detail_button_uses_registered_business_and_real_generated_pipeline(string scenario)
    {
        using var run=fixture.Run();using var services=ButtonServices(run);
        var entity=JsonSerializer.SerializeToElement(new{id="7",name=run.Get("Name"),amount=scenario=="numeric"?(object)10:scenario=="fraction"?10.001m:"10.00",remarks="待处理",unusedDisplay="原详情其他字段"});
        if(scenario=="explicit")entity=JsonSerializer.SerializeToElement(new{id="7",expectedDigest=run.Digest()});
        var request=new OriginalBusinessButtonRequest(scenario=="preview"?"86000":scenario=="rollback"?"86002":"86001","7",scenario=="missing-original"?null:entity);
        if(scenario=="stale")run.Set("Name","加载详情后已修改");
        var context=new DefaultHttpContext{RequestServices=services};
        var executor=services.GetRequiredService<OriginalBusinessButtonExecutor>();
        var controller=(ControllerBase)Activator.CreateInstance(fixture.Assembly.GetType("Metarill.GlobalSample.Host.OriginalBusinessButtonController",true)!,executor)!;
        controller.ControllerContext=new(){HttpContext=context};
        async Task<IActionResult> Execute()=>await(Task<IActionResult>)controller.GetType().GetMethod("Execute")!.Invoke(controller,[request,CancellationToken.None])!;
        if(scenario=="rollback")
        {
            await Assert.ThrowsAsync<BusinessRuleException>(Execute);Assert.Equal("待处理",run.Get("Remarks"));Assert.Equal("rollback",run.Trace[^1]);Assert.DoesNotContain("commit",run.Trace);
        }
        else
        {
            var result=Assert.IsType<ObjectResult>(await Execute());
            if(scenario is "stale" or "missing-original" or "fraction")
            {Assert.Equal(409,result.StatusCode);Assert.DoesNotContain("write",run.Trace);Assert.Equal("待处理",run.Get("Remarks"));}
            else
            {
                Assert.Equal(200,(int)result.Value!.GetType().GetProperty("Code")!.GetValue(result.Value)!);
                if(scenario is "complete" or "numeric" or "explicit"){Assert.Equal("已处理",run.Get("Remarks"));Assert.Single(run.Observer.Contexts);Assert.Equal("86001",run.Observer.Contexts[0].BusinessButtonId);}
                else Assert.DoesNotContain("begin",run.Trace);
            }
        }
        Assert.Equal(0,run.Authorization.PageChecks);Assert.Null(run.Session.CurrentTransaction);
        var gate=Assert.IsType<ButtonExecutionGuard>(services.GetRequiredService<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>());Assert.Equal(1,gate.Calls);Assert.Equal(1,gate.Releases);
    }
    /// <summary>错误身份、路径、处理器或相互冲突ID必须在查询实际业务记录之前拒绝。</summary>
    [Theory][InlineData("agent")][InlineData("identity")][InlineData("duplicate")][InlineData("wrong-path")]
    [InlineData("entity-id")][InlineData("large-id")][InlineData("array")][InlineData("revoked")][InlineData("missing-service")][InlineData("no-guard")]
    public async Task Original_button_rejects_bad_identity_binding_and_id_before_business(string scenario)
    {
        using var run=fixture.Run();using var services=ButtonServices(run,scenario);
        if(scenario=="revoked")run.Authorization.Allowed=false;
        var entity=scenario=="array"?JsonSerializer.SerializeToElement(new[]{1}):JsonSerializer.SerializeToElement(new{id=scenario=="entity-id"?"8":"7"});
        var request=new OriginalBusinessButtonRequest("86000",scenario=="large-id"?"9007199254740993":"7",entity);
        var context=new DefaultHttpContext{RequestServices=services};
        var error=await Record.ExceptionAsync(()=>services.GetRequiredService<OriginalBusinessButtonExecutor>().ExecuteAsync(request,context));
        Assert.NotNull(error);Assert.True(error is UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException,error.GetType().Name);
        Assert.Empty(run.Trace);Assert.Empty(run.Observer.Contexts);
    }
    /// <summary>正式包真实生成实体、原控制器和处理器共同校验，传输替身不能替换服务端可见条件。</summary>
    [Fact]
    public async Task Original_package_visibility_uses_true_generated_entity_before_lease_and_business()
    {
        using var run=fixture.Run();using var services=ButtonServices(run,"visibility");var context=new DefaultHttpContext{RequestServices=services};
        var executor=services.GetRequiredService<OriginalBusinessButtonExecutor>();
        var request=new OriginalBusinessButtonRequest("86000","7",JsonSerializer.SerializeToElement(new{id="7",name="允许",amount=10,remarks="待处理"}),"AlwaysVisible","{}");
        await Assert.ThrowsAsync<BusinessRuleException>(()=>executor.ExecuteAsync(request,context));Assert.Equal(new[]{"read"},run.Trace);Assert.Empty(run.Observer.Contexts);
        var gate=Assert.IsType<ButtonExecutionGuard>(services.GetRequiredService<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>());Assert.Equal(0,gate.Calls);
        run.Set("Name","允许");var result=Assert.IsType<ObjectResult>(await executor.ExecuteAsync(request with{Entity=JsonSerializer.SerializeToElement(new{id="7",name="拒绝"})},context));
        Assert.Null(result.StatusCode);Assert.Equal(1,gate.Calls);Assert.Equal(1,gate.Releases);Assert.DoesNotContain("begin",run.Trace);
    }
    /// <summary>注册处理器必须使用正式编译包中的类型，范围守卫仍解析请求Scope唯一生成Service。</summary>
    private ServiceProvider ButtonServices(SampleContractActionRun run,string mode="")
    {
        var services=new ServiceCollection();RegisterButtonServices(services,run,mode);return services.BuildServiceProvider();
    }
    /// <summary>同一正式类型装配供直接合同和MVC回环HTTP共用，替身只负责身份结论和传输。</summary>
    private void RegisterButtonServices(IServiceCollection services,SampleContractActionRun run,string mode="")
    {
        services.AddLogging();services.AddControllers().AddApplicationPart(fixture.Assembly).AddJsonOptions(options=>Metarill.Runtime.JsonDefaults.CopyTo(options.JsonSerializerOptions));
        var published=fixture.Metadata;
        if(mode=="visibility")
        {
            published=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(fixture.Metadata))!;
            for(var index=0;index<published.ConfigButtons.Count;index++)
            {
                var button=System.Text.Json.Nodes.JsonNode.Parse(published.ConfigButtons[index].GetRawText())!;
                button["visibleRuleJson"]="{\"conditions\":[{\"field\":\"name\",\"value\":\"允许\"}]}";
                published.ConfigButtons[index]=JsonSerializer.SerializeToElement(button);
            }
        }
        services.AddSingleton<IAgentHostResources>(new Published(published));services.AddSingleton<IRuntimeAuthorization>(run.Authorization);
        var actor=run.Authorization.RequireActor();
        if(mode!="identity")services.AddSingleton<ISecurityIdentityAccessor>(new ButtonIdentity(new(actor,new HashSet<string>(),20,mode=="agent"?99:null){PrincipalProjectId=actor.ProjectId,PrincipalAdminTypeId=actor.AdminTypeId}));
        if(mode!="missing-service")services.AddSingleton(run.Service.GetType(),run.Service);
        foreach(var id in new[]{"86000","86001","86002"})
        {
            var handler=(IOriginalBusinessButtonHandler)Activator.CreateInstance(fixture.Assembly.GetType("Metarill.GlobalSample.Business.SampleContractButtonHandler",true)!,id,run.Business,fixture.Metadata)!;
            services.AddSingleton<IOriginalBusinessButtonHandler>(mode=="wrong-path"&&id=="86000"?new InvalidButtonPath():handler);
            if(mode=="duplicate"&&id=="86000")services.AddSingleton(handler);
        }
        if(mode!="no-guard")services.AddSingleton<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>(new ButtonExecutionGuard());
        services.AddSingleton<OriginalBusinessButtonExecutor>();
    }
    /// <summary>实际MVC绑定原按钮JSON，原模板控制器与公开业务回传R；身份/数据库仍是离线替身。</summary>
    [Fact]
    public async Task Original_central_route_accepts_actual_json_and_returns_single_R_with_conflict_and_rollback()
    {
        using var run=fixture.Run();var builder=WebApplication.CreateBuilder(Array.Empty<string>());builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");RegisterButtonServices(builder.Services,run);
        await using var app=builder.Build();
        // 此处只复现已公开对象规则的HTTP200/R500边界，不声称复现完整生产认证或中间件。
        app.Use(async(context,next)=>{try{await next();}catch(BusinessRuleException error){context.Response.StatusCode=200;await context.Response.WriteAsJsonAsync(R<object>.Error(error.Message));}});
        app.MapControllers();await app.StartAsync();
        try
        {
            using var client=new HttpClient{BaseAddress=new Uri(app.Urls.Single()),Timeout=TimeSpan.FromSeconds(20)};
            const string path="/api/admin/sys/config_button/execute";
            var entity=new{id="7",name="待处理合同😀",amount=10,remarks="待处理",attachments=Array.Empty<string>()};
            var gate=(ButtonExecutionGuard)app.Services.GetRequiredService<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>();gate.Busy=true;
            using(var busy=await client.PostAsJsonAsync(path,new{buttonId="86000",entityId="7",entity}))
            {Assert.Equal(HttpStatusCode.OK,busy.StatusCode);using var busyBody=JsonDocument.Parse(await busy.Content.ReadAsStringAsync());Assert.Equal(500,busyBody.RootElement.GetProperty("code").GetInt32());Assert.Equal("按钮正在执行中，请稍后再试",busyBody.RootElement.GetProperty("msg").GetString());Assert.DoesNotContain("begin",run.Trace);}
            gate.Busy=false;
            using var preview=await client.PostAsJsonAsync(path,new{buttonId="86000",entityId="7",entity});Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
            using var previewBody=JsonDocument.Parse(await preview.Content.ReadAsStringAsync());Assert.Equal(200,previewBody.RootElement.GetProperty("code").GetInt32());Assert.Equal(run.Digest(),previewBody.RootElement.GetProperty("data").GetProperty("expectedDigest").GetString());
            run.Set("Name","另一次修改");using var stale=await client.PostAsJsonAsync(path,new{buttonId="86001",entityId="7",entity});Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);Assert.DoesNotContain("write",run.Trace);
            run.Set("Name","待处理合同😀");using var completed=await client.PostAsJsonAsync(path,new{buttonId="86001",entityId="7",entity});Assert.Equal(HttpStatusCode.OK,completed.StatusCode);
            using var body=JsonDocument.Parse(await completed.Content.ReadAsStringAsync());Assert.Equal(200,body.RootElement.GetProperty("code").GetInt32());Assert.Equal("已处理",body.RootElement.GetProperty("data").GetProperty("remarks").GetString());Assert.False(body.RootElement.GetProperty("data").TryGetProperty("code",out _));
            run.Set("Remarks","待处理");var commits=run.Trace.Count(value=>value=="commit");using var rollback=await client.PostAsJsonAsync(path,new{buttonId="86002",entityId="7",entity});Assert.Equal(HttpStatusCode.OK,rollback.StatusCode);
            using var rejected=JsonDocument.Parse(await rollback.Content.ReadAsStringAsync());Assert.Equal(500,rejected.RootElement.GetProperty("code").GetInt32());Assert.Equal("合同金额不能小于零",rejected.RootElement.GetProperty("msg").GetString());Assert.Equal("待处理",run.Get("Remarks"));Assert.Equal(commits,run.Trace.Count(value=>value=="commit"));Assert.Equal("rollback",run.Trace[^1]);
        }
        finally{await app.StopAsync();}
    }
    /// <summary>此夹具只替入租约决策，真实数据库租约状态机与跨进程SQL由专项覆盖。</summary>
    private sealed class ButtonExecutionGuard:Metarill.Core.Persistence.IBusinessButtonExecutionGuard
    {
        internal int Calls,Releases;internal bool Busy;
        public async Task<T> ExecuteAsync<T>(string project,string button,Func<Task<T>> action,CancellationToken token=default)
        {
            Calls++;Assert.NotEmpty(project);Assert.Contains(button,new[]{"86000","86001","86002"});
            if(Busy)throw new BusinessRuleException("按钮正在执行中，请稍后再试");
            try{return await action();}finally{Releases++;}
        }
    }
    private sealed class ButtonIdentity(AuthenticatedIdentity identity):ISecurityIdentityAccessor
    {
        public AuthenticatedIdentity RequireIdentity()=>identity;
    }
    private sealed class InvalidButtonPath:IOriginalBusinessButtonHandler
    {
        public string ButtonId=>"86000";
        public string Path=>"https://invalid.example/execute";
        public Task<IActionResult> ExecuteAsync(OriginalBusinessButtonRequest request,CancellationToken token)=>throw new InvalidOperationException("路径错误不应执行。");
    }
    private static string? Text(object value, string name) => (string?)value.GetType().GetProperty(name)!.GetValue(value);
    private sealed class Published(MetadataCatalog metadata) : IAgentHostResources
    {
        public AgentHostCatalog AgentCatalog { get; } = AgentHostCatalog.ForApplication(metadata);
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs => [];
        public bool IsPlatformRealm(string project, string admin) => false;
    }
}

/// <summary>从公开手写源建立独占完整包；再生成保护先核字节，严格编译后隔离加载。</summary>
public sealed class SampleContractActionPackageFixture : IAsyncLifetime
{
    private readonly string boundary = Path.Combine(Path.GetTempPath(), "metarill-sample-contract-actions"); private string directory = ""; private AssemblyLoadContext? loadContext;
    internal Assembly Assembly { get; private set; } = null!;
    internal MetadataCatalog Metadata { get; } = GlobalCachedSourceGenerationTests.Catalog();
    internal bool HandwrittenProtected { get; private set; }
    internal string PackageDirectory => directory;
    /// <summary>只向本夹具新包安装原样板，普通生成器和已冻结B98包不自动接入。</summary>
    public async Task InitializeAsync()
    {
        directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); new ApplicationPackager().Create(Metadata, directory, "Metarill.GlobalSample", NonRedundantApplicationFixture.Root());
        var sample = Path.Combine(NonRedundantApplicationFixture.Root(), "tools/acceptance/sample-host"); var handwritten = new Dictionary<string, byte[]>();
        foreach (var file in Directory.GetFiles(sample, "*.cs"))
        {
            var destination = Path.Combine(directory, Path.GetFileName(file) == "SampleContractService.cs" ? "backend/generated/Services" : "backend/Application.Host", Path.GetFileName(file));
            await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(file)); handwritten[destination] = await File.ReadAllBytesAsync(destination);
        }
        foreach (var round in new[] { 1, 2 })
        {
            _ = round; new CSharpGenerator().Generate(Metadata, Path.Combine(directory, "backend/generated"), "Metarill.GlobalSample");
            foreach (var file in handwritten) Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key));
        }
        HandwrittenProtected = true;
        foreach (var operation in new[] { "restore", "build" })
        {
            var info = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", directory);
            foreach (var value in new[] { operation, "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet" }) info.ArgumentList.Add(value);
            if (operation == "build") { info.ArgumentList.Add("--no-restore"); info.ArgumentList.Add("-warnaserror"); }
            await NonRedundantApplicationFixture.RunAsync(info);
        }
        // DispatchProxy动态程序集不能引用可卸载类型；每个测试进程只加载这一份完整包，仍与同名宿主隔离。
        loadContext = new AssemblyLoadContext("sample-contract-actions-" + Guid.NewGuid().ToString("N"), isCollectible: false);
        Assembly = loadContext.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(directory, "backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"))));
    }
    /// <summary>每例使用独立实体、权限、ORM传输与事务状态。</summary>
    internal SampleContractActionRun Run() => new(Assembly, Metadata);
    /// <summary>只清理本夹具已核定临时根，不触及其他验收或应用目录。</summary>
    public async Task DisposeAsync() { if (loadContext?.IsCollectible == true) loadContext.Unload(); if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
}

/// <summary>只替代单表传输与事务回执；真实Service、Business、DTO和对象规则均来自编译完整包。</summary>
internal sealed class SampleContractActionRun : IDisposable
{
    private readonly Type entityType; private readonly object business; private object row;
    internal readonly List<string> Trace = []; internal readonly List<DbTransaction> Transactions = [];
    internal readonly SampleAuthorization Authorization; internal readonly SampleObserver Observer = new();
    internal DatabaseSession Session { get; }
    internal Action? BeforeLockedRead;
    internal object Business => business;
    internal object Service {get;}
    internal SampleContractActionRun(Assembly assembly, MetadataCatalog metadata)
    {
        entityType = assembly.GetType("Metarill.GlobalSample.Generated.Entities.SampleContract", true)!; row = Activator.CreateInstance(entityType)!;
        Set("Id", 7L); Set("Name", "待处理合同😀"); Set("Amount", 10.00m); Set("Remarks", "待处理"); Set("SampleCustomerId", "9"); Set("SampleCustomerName", "客户");
        Authorization = new(new(8, "固定样板身份", false, metadata.YzProjectId, metadata.ConfigPages.Single(page => page.Id == "81002").YzAdminTypeId));
        var orm = Proxy<IFreeSql>((method, args) => method.Name switch
        {
            "Select" => Select(method.GetGenericArguments()[0]), "Update" => Update(method.ReturnType), _ => throw new NotSupportedException("样板传输拒绝未知ORM：" + method.Name)
        });
        Session = new(new() { Provider = "mysql", ExpectedDatabase = "offline" }, orm, new OfflineProvider(this));
        var mapper = Activator.CreateInstance(assembly.GetType("Metarill.GlobalSample.Generated.Mappers.SampleContractMapper", true)!, Session)!;
        var service = Activator.CreateInstance(assembly.GetType("Metarill.GlobalSample.Generated.Services.SampleContractService", true)!, Session, mapper, new SnowflakeIdGenerator(891), new IRecordMutationObserver[] { Observer }, Array.Empty<IRecordMutationGuard>(), Authorization)!;
        Service=service;
        business = Activator.CreateInstance(assembly.GetType("Metarill.GlobalSample.Business.SampleContractAgentBusiness", true)!, Session, service, Authorization)!;
    }
    /// <summary>执行实际公开业务方法，反射只处理生成后才可知的类型。</summary>
    internal async Task<object> Invoke(string method, params object?[] arguments)
    {
        Task task;
        try { task = (Task)business.GetType().GetMethod(method)!.Invoke(business, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        await task; return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    /// <summary>只为输入样本赋值，不修改被测业务或生成方法。</summary>
    internal void Set(string name, object? value) => entityType.GetProperty(name)!.SetValue(row, value);
    internal object? Get(string name) => entityType.GetProperty(name)!.GetValue(row);
    internal string Digest() => SampleContractDigest.Calculate((long)Get("Id")!, (string?)Get("Name"), (decimal?)Get("Amount"), (string?)Get("Remarks"));
    /// <summary>返回实体副本，防止生成器合并DTO时直接改写已保存样本。</summary>
    private object Clone(object source)
    { var copy = Activator.CreateInstance(entityType)!; foreach (var field in entityType.GetProperties().Where(field => field.CanRead && field.CanWrite)) field.SetValue(copy, field.GetValue(source)); return copy; }
    private object Select(Type type)
    {
        if (type != entityType) throw new NotSupportedException("样板离线传输不伪造关系目标。");
        object? query = null; var locking = false; query = Proxy(typeof(ISelect<>).MakeGenericType(type), (method, args) =>
        {
            if (method.Name == "ForUpdate") locking = true;
            if (method.Name == "FirstAsync")
            {
                if (locking) { Assert.NotNull(Session.CurrentTransaction); Transactions.Add(Session.CurrentTransaction!); Trace.Add("lock"); var change = BeforeLockedRead; BeforeLockedRead = null; change?.Invoke(); }
                else Trace.Add("read");
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(entityType).Invoke(null, [Clone(row)]);
            }
            return query;
        }); return query;
    }
    /// <summary>只解释真实Mapper的SetSource/UpdateColumns，更新按实际提交列执行。</summary>
    private object Update(Type type)
    {
        object? command = null, source = null; string[] columns = [];
        command = Proxy(type, (method, args) =>
        {
            if (method.Name == "SetSource") source = args![0];
            if (method.Name == "UpdateColumns") columns = Assert.IsType<string[]>(args![0]);
            if (method.Name == "ExecuteAffrowsAsync")
            {
                Assert.NotNull(Session.CurrentTransaction); Assert.NotNull(source); Assert.NotEmpty(columns); Transactions.Add(Session.CurrentTransaction!);
                foreach (var column in columns)
                {
                    var property = entityType.GetProperties().Single(field => field.Name.Equals(column.Replace("_", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)); property.SetValue(row, property.GetValue(source));
                }
                Trace.Add("write"); return Task.FromResult(1);
            }
            return command;
        }); return command;
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> action) where T : class => (T)Proxy(typeof(T), action);
    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> action) { var value = DispatchProxy.Create(type, typeof(CallProxy)); ((CallProxy)value).Action = action; return value; }
    public class CallProxy : DispatchProxy { internal Func<MethodInfo, object?[]?, object?> Action = null!; protected override object? Invoke(MethodInfo? method, object?[]? args) => Action(method!, args); }
    internal sealed class SampleAuthorization(RuntimeActor actor) : IRuntimeAuthorization
    {
        internal bool Allowed = true; internal int PageChecks;
        public RuntimeActor RequireActor() => actor;
        public void RequirePagePermission(string page, string operation) { PageChecks++; throw new UnauthorizedAccessException("普通页面权不能代替按钮。"); }
        public void RequireButtonPermission(string page, string button) { Assert.Equal("81002", page); Assert.Contains(button, new[] { "86000", "86001", "86002" }); if (!Allowed) throw new UnauthorizedAccessException("按钮已撤销"); }
        /// <summary>只替入当前授权结论，不冒充真实数据库角色或锁证明。</summary>
        public Task RequireButtonCurrentAsync(string page,string button,RuntimeActor requested,DbTransaction transaction,CancellationToken cancellationToken=default)
        {Assert.Equal(actor,requested);Assert.NotNull(transaction.Connection);RequireButtonPermission(page,button);return Task.CompletedTask;}
        public ExactDataManageValues ResolveButtonExactDataManage(string page, string button, string field) { RequireButtonPermission(page, button); return ExactDataManageValues.Unrestricted; }
    }
    internal sealed class SampleObserver : IRecordMutationObserver
    {
        internal readonly List<RecordMutationContext> Contexts = [];
        public Task BeforeCommitAsync(RecordMutationContext context, CancellationToken cancellationToken = default) { Contexts.Add(context); return Task.CompletedTask; }
    }
    private sealed class OfflineProvider(SampleContractActionRun owner) : IDatabaseProvider
    {
        public string Name => "mysql"; public DatabaseCapabilities Capabilities => new("mysql", true, false); public void Validate(DatabaseOptions options) { }
        public string QuoteIdentifier(string value) => new MySqlDatabaseProvider().QuoteIdentifier(value); public string ParameterPlaceholder(string value) => "@" + value;
        public IFreeSql CreateOrm(DatabaseOptions options) => throw new NotSupportedException(); public DbConnection CreateConnection(DatabaseOptions options) => new Connection(owner);
    }
    private sealed class Connection(SampleContractActionRun owner) : DbConnection
    {
        private ConnectionState state; [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline"; public override string DataSource => "offline"; public override string ServerVersion => "offline"; public override ConnectionState State => state;
        public override void ChangeDatabase(string value) => throw new NotSupportedException(); public override void Open() => state = ConnectionState.Open; public override void Close() => state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) { owner.Trace.Add("begin"); return new Transaction(this, owner); } protected override DbCommand CreateDbCommand() => throw new NotSupportedException("本用例不允许未说明的原生SQL。");
    }
    private sealed class Transaction(Connection connection, SampleContractActionRun owner) : DbTransaction
    {
        private readonly object snapshot = owner.Clone(owner.row);
        public override IsolationLevel IsolationLevel => IsolationLevel.RepeatableRead; protected override DbConnection DbConnection => connection;
        public override void Commit() => owner.Trace.Add("commit"); public override void Rollback() { owner.row = owner.Clone(snapshot); owner.Trace.Add("rollback"); }
    }
    public void Dispose() => Session.Dispose();
}
