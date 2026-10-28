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
    }
    /// <summary>测试只读取实际响应记录的公开业务字段，不反射改写业务实现。</summary>
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
