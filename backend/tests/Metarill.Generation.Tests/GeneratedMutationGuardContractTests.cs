using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>编译并执行实际生成 Service；仅替代数据库传输，不把离线调用顺序当作真实锁或撤权验收。</summary>
public sealed class GeneratedMutationGuardContractTests(GeneratedMutationGuardFixture fixture) : IClassFixture<GeneratedMutationGuardFixture>
{
    [Theory]
    [InlineData(RecordMutationOperation.Create)]
    [InlineData(RecordMutationOperation.Update)]
    [InlineData(RecordMutationOperation.Delete)]
    [InlineData(RecordMutationOperation.Copy)]
    public async Task Preparation_and_final_validation_surround_actual_record_access_and_share_transaction(RecordMutationOperation operation)
    {
        using var run = fixture.Run(); var guard = new Guard(run); var observer = new Observer(run); var service = run.Service([guard], [observer]);
        var request = run.Request(operation, "{\"id\":1,\"name\":\"手写归一\",\"yzAdminTypeId\":\"91\",\"yzRoleIds\":\"[\\\"12\\\"]\",\"pwd\":\"不应进入守卫\"}");
        await run.Invoke(service, operation, request);
        var preparation = Assert.Single(guard.Preparations); var validation = Assert.Single(guard.Validations);
        Assert.Equal(operation == RecordMutationOperation.Create ? null : 1L, preparation.TargetId);
        Assert.Same(preparation.Transaction, validation.Transaction); Assert.Same(preparation.Transaction, observer.Transaction);
        Assert.Equal(run.Actor, validation.Actor); Assert.Equal("customer", validation.TableName); Assert.Equal("101", validation.PageId);
        Assert.Equal(operation, validation.Operation); Assert.True(validation.RecordId > 0);
        Assert.False(preparation.ScopeValues.ContainsKey("pwd")); Assert.False(preparation.ScopeValues.ContainsKey("name")); Assert.False(preparation.ScopeValues.ContainsKey("yz_role_ids"));
        Assert.False(validation.ScopeValues.ContainsKey("pwd")); Assert.False(validation.ScopeValues.ContainsKey("name"));
        Assert.False(validation.OriginalScopeValues.ContainsKey("pwd"));
        if (operation is RecordMutationOperation.Create or RecordMutationOperation.Update)
        {
            Assert.Equal(91L, preparation.ScopeValues["yz_admin_type_id"]);
            Assert.Equal(88L, validation.ScopeValues["yz_admin_type_id"]); // 手写规则归一化后的真实值。
            Assert.Equal("[\"12\"]", validation.ScopeValues["yz_role_ids"]);
            Assert.Contains("yz_role_ids", validation.SubmittedFields);
        }
        if (operation == RecordMutationOperation.Create) Assert.Empty(validation.OriginalScopeValues);
        else Assert.Equal(90L, validation.OriginalScopeValues["yz_admin_type_id"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)preparation.ScopeValues).Add("伪字段", 1));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)validation.ScopeValues).Add("伪字段", 1));
        Assert.Equal("prepare", run.Trace[0]);
        Assert.True(run.Trace.IndexOf("validate") < run.Trace.FindIndex(item => item.StartsWith("write:", StringComparison.Ordinal)));
        Assert.True(run.Trace.FindIndex(item => item.StartsWith("write:", StringComparison.Ordinal)) < run.Trace.IndexOf("observer"));
        Assert.Equal("commit", run.Trace[^1]); Assert.Null(run.Session.CurrentTransaction);
    }

    [Theory]
    [InlineData(RecordMutationOperation.Create)]
    [InlineData(RecordMutationOperation.Update)]
    [InlineData(RecordMutationOperation.Delete)]
    [InlineData(RecordMutationOperation.Copy)]
    public async Task Final_guard_rejection_prevents_mapper_and_observer_for_every_single_object_entry(RecordMutationOperation operation)
    {
        using var run = fixture.Run(); var guard = new Guard(run) { RejectFinal = true }; var observer = new Observer(run);
        await Assert.ThrowsAsync<GuardRejectedException>(() => run.Invoke(run.Service([guard], [observer]), operation, run.Request(operation, "{\"id\":1,\"name\":\"有效\"}")));
        Assert.Single(guard.Validations); Assert.DoesNotContain(run.Trace, item => item.StartsWith("write:", StringComparison.Ordinal));
        Assert.DoesNotContain("observer", run.Trace); Assert.Equal("rollback", run.Trace[^1]); Assert.Null(run.Session.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Omitted_and_explicit_null_scope_values_remain_distinct(bool explicitNull)
    {
        using var run = fixture.Run(); var guard = new Guard(run);
        await run.Invoke(run.Service([guard]), RecordMutationOperation.Update, run.Request(RecordMutationOperation.Update, explicitNull ? "{\"id\":1,\"yzAdminTypeId\":null}" : "{\"id\":1}"));
        var prepared = Assert.Single(guard.Preparations); var validated = Assert.Single(guard.Validations);
        Assert.Equal(explicitNull, prepared.ScopeValues.ContainsKey("yz_admin_type_id"));
        Assert.Equal(explicitNull, validated.SubmittedFields.Contains("yz_admin_type_id"));
        Assert.Equal(90L, validated.OriginalScopeValues["yz_admin_type_id"]);
        Assert.Equal(explicitNull ? null : 90L, validated.ScopeValues["yz_admin_type_id"]);
    }

    [Fact]
    public async Task Existing_guard_without_final_override_and_application_without_guards_remain_executable()
    {
        using var first = fixture.Run(); var legacy = new LegacyGuard(first);
        await first.Invoke(first.Service([legacy]), RecordMutationOperation.Create, first.Request(RecordMutationOperation.Create, "{\"name\":\"普通写入\"}"));
        Assert.Equal(1, legacy.Calls); Assert.Contains("write:Insert", first.Trace); Assert.Equal("commit", first.Trace[^1]);
        using var second = fixture.Run();
        await second.Invoke(second.Service([]), RecordMutationOperation.Create, second.Request(RecordMutationOperation.Create, "{\"name\":\"未注册守卫\"}"));
        Assert.Contains("write:Insert", second.Trace); Assert.Equal("commit", second.Trace[^1]);
    }

    [Fact]
    public async Task Import_outer_preparation_and_each_real_row_share_the_same_transaction()
    {
        using var run = fixture.Run(); var guard = new Guard(run);
        var request = run.Request(RecordMutationOperation.Import, "{\"requestJson\":[{\"名称\":\"甲\"},{\"名称\":\"乙\"}]}");
        await run.Invoke(run.Service([guard]), RecordMutationOperation.Import, request);
        Assert.Equal(new[] { RecordMutationOperation.Import, RecordMutationOperation.Create, RecordMutationOperation.Create }, guard.Preparations.Select(item => item.Operation));
        Assert.Null(guard.Preparations[0].TargetId); Assert.Equal(100L, guard.Preparations[0].ScopeValues["yz_project_id"]);
        Assert.Single(guard.Preparations[0].ScopeValues);
        Assert.Equal(2, guard.Validations.Count); Assert.All(guard.Validations, item => Assert.Same(guard.Preparations[0].Transaction, item.Transaction));
        Assert.Equal(2, run.Trace.Count(item => item == "write:Insert")); Assert.Equal("commit", run.Trace[^1]);
    }

    [Fact]
    public async Task Preparation_rejection_and_invalid_identity_happen_before_any_record_access()
    {
        using var run = fixture.Run(); var guard = new Guard(run) { RejectPreparation = true };
        await Assert.ThrowsAsync<GuardRejectedException>(() => run.Invoke(run.Service([guard]), RecordMutationOperation.Update, run.Request(RecordMutationOperation.Update, "{\"id\":1}")));
        Assert.Equal(new[] { "prepare", "rollback" }, run.Trace); Assert.Empty(guard.Validations);
        using var invalid = fixture.Run(); invalid.Actor = invalid.Actor with { AccountId = 0 };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invalid.Invoke(invalid.Service([new Guard(invalid)]), RecordMutationOperation.Delete, null));
        Assert.Equal(new[] { "rollback" }, invalid.Trace);
    }

    [Fact]
    public async Task Required_validation_still_precedes_final_guard_and_write()
    {
        using var run = fixture.Run(); var guard = new Guard(run);
        await Assert.ThrowsAsync<ArgumentException>(() => run.Invoke(run.Service([guard]), RecordMutationOperation.Create, run.Request(RecordMutationOperation.Create, "{}")));
        Assert.Single(guard.Preparations); Assert.Empty(guard.Validations); Assert.DoesNotContain(run.Trace, item => item.StartsWith("write:", StringComparison.Ordinal));
        Assert.Equal("rollback", run.Trace[^1]);
    }

    /// <summary>复核实际事务和记录访问先后，不实现任何授权替身逻辑。</summary>
    private sealed class Guard(GeneratedMutationGuardRun run) : IRecordMutationGuard
    {
        public bool RejectPreparation { get; init; } public bool RejectFinal { get; init; }
        public List<RecordMutationPreparation> Preparations { get; } = []; public List<RecordMutationValidation> Validations { get; } = [];
        public Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken token = default)
        {
            Assert.Same(run.Session.CurrentTransaction, preparation.Transaction); Assert.NotNull(preparation.Transaction.Connection);
            run.Trace.Add("prepare"); Preparations.Add(preparation); if (RejectPreparation) throw new GuardRejectedException(); return Task.CompletedTask;
        }
        public Task BeforeWriteAsync(RecordMutationValidation validation, CancellationToken token = default)
        {
            Assert.Same(run.Session.CurrentTransaction, validation.Transaction); run.Trace.Add("validate"); Validations.Add(validation);
            if (RejectFinal) throw new GuardRejectedException(); return Task.CompletedTask;
        }
    }
    /// <summary>刻意只实现旧接口成员，编译及实际调用验证默认方法的兼容边界。</summary>
    private sealed class LegacyGuard(GeneratedMutationGuardRun run) : IRecordMutationGuard
    {
        public int Calls { get; private set; }
        public Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken token = default) { Assert.Same(run.Session.CurrentTransaction, preparation.Transaction); Calls++; return Task.CompletedTask; }
    }
    private sealed class Observer(GeneratedMutationGuardRun run) : IRecordMutationObserver
    {
        public DbTransaction? Transaction { get; private set; }
        public Task BeforeCommitAsync(RecordMutationContext context, CancellationToken token = default) { Transaction = context.Transaction; run.Trace.Add("observer"); return Task.CompletedTask; }
    }
    private sealed class GuardRejectedException : Exception;
}

/// <summary>一个真实编译批次供全部离线生命周期探针使用，临时文件仍由测试专属根清理。</summary>
public sealed class GeneratedMutationGuardFixture : IAsyncLifetime
{
    private readonly string boundary = Path.Combine(Path.GetTempPath(), "metarill-guard-contract");
    private readonly string directory; private Assembly assembly = null!;
    public GeneratedMutationGuardFixture() => directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory); var catalog = GeneratorTests.Catalog();
        foreach (var column in catalog.ConfigColumns.Where(column => column.LogicalType == "id")) column.LogicalType = "int64";
        foreach (var item in new[] { ("16", "yz_admin_type_id", "int64"), ("17", "yz_role_ids", "string"), ("18", "is_super", "int32"), ("19", "pwd", "string") })
        {
            catalog.ConfigColumns.Add(new() { Id = item.Item1, ConfigTableId = "1", ColumnName = item.Item2, LogicalType = item.Item3, Name = item.Item2 });
            catalog.ConfigPageColumns.Add(new() { Id = "1" + item.Item1, ConfigPageId = "101", ConfigColumnId = item.Item1, IsEdit = 1, IsShow = 1 });
        }
        catalog.ConfigPages.Single().IsCopy = 1; catalog.ConfigPages.Single().IsImport = 1;
        new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.GuardContract");
        await File.WriteAllTextAsync(Path.Combine(directory, "generated/Services/CustomerService.cs"), """
using Metarill.Core.Security;
using Example.GuardContract.Generated.Entities;
namespace Example.GuardContract.Generated.Services;
public partial class CustomerService
{
    // 测试专属手写规则，验证守卫拿到归一化后的值而非客户端原文。
    partial void BeforeSave(Customer entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    { if (entity.Name == "手写归一") { entity.YzAdminTypeId = 88; submittedFields.Add("yz_admin_type_id"); } }
}
""");
        assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
    }
    public GeneratedMutationGuardRun Run() => new(assembly);
    public async Task DisposeAsync() => await TestDirectoryCleanup.DeleteAsync(directory, boundary);
}

/// <summary>只模拟执行结果与事务回执，固定行读取不承担权限、SQL或并发锁证明。</summary>
public sealed class GeneratedMutationGuardRun : IDisposable
{
    private readonly Assembly assembly; private readonly Type entityType; private object? lastWrittenEntity;
    public List<string> Trace { get; } = []; public DatabaseSession Session { get; }
    public RuntimeActor Actor { get; set; } = new(8015, "守卫合同", true, "100", "1");
    public string? ParentId { get; set; }
    public GeneratedMutationGuardRun(Assembly assembly,Action<TimeSpan>? validateTime=null,Func<string,TimeSpan?,TimeSpan?>? persistedTime=null)
    {
        this.assembly = assembly; entityType = assembly.GetType("Example.GuardContract.Generated.Entities.Customer", true)!;
        var orm = Proxy<IFreeSql>((method, args) => method.Name switch { "Select" => Select(method.GetGenericArguments()[0]), "Insert" or "Update" or "Delete" => Command(method.ReturnType, method.Name), _ => throw new NotSupportedException(method.Name) });
        Session = new(new() { Provider = "mysql", ExpectedDatabase = "offline" }, orm, new OfflineProvider(Trace,validateTime,()=>lastWrittenEntity,entityType,persistedTime));
    }
    /// <summary>旧样本保持五参数构造；按钮样本显式提供真实授权测试入口。</summary>
    public object Service(IRecordMutationGuard[] guards, IRecordMutationObserver[]? observers = null, IRuntimeAuthorization? authorization = null)
    {
        var mapper = Activator.CreateInstance(assembly.GetType("Example.GuardContract.Generated.Mappers.CustomerMapper", true)!, Session);
        var type=assembly.GetType("Example.GuardContract.Generated.Services.CustomerService",true)!;
        object?[] args=[Session,mapper,new SnowflakeIdGenerator(841),observers??[],guards];
        return Activator.CreateInstance(type,type.GetConstructors().Single().GetParameters().Length==6?[..args,authorization]:args)!;
    }
    public object? Request(RecordMutationOperation operation, string json)
    {
        if (operation is RecordMutationOperation.Delete or RecordMutationOperation.Copy) return null;
        var type = assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101" + operation + "Request", true)!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new Metarill.Core.Contracts.StringLongJsonConverter());
        using var document = JsonDocument.Parse(json);
        if (operation == RecordMutationOperation.Create) json = JsonSerializer.Serialize(document.RootElement.EnumerateObject().Where(item => item.Name != "id").ToDictionary(item => item.Name, item => item.Value));
        return JsonSerializer.Deserialize(json, type, options);
    }
    public async Task Invoke(object service, RecordMutationOperation operation, object? request)
    {
        var arguments = operation switch { RecordMutationOperation.Delete or RecordMutationOperation.Copy => new object?[] { 1L, ParentId, Actor, CancellationToken.None }, RecordMutationOperation.Import => [request, Actor, CancellationToken.None], _ => [request, ParentId, Actor, CancellationToken.None] };
        await (Task)service.GetType().GetMethod(operation + "101Async")!.Invoke(service, arguments)!;
    }
    private object Select(Type type)
    {
        object? query = null; query = Proxy(typeof(ISelect<>).MakeGenericType(type), (method, args) =>
        {
            if (method.Name == "ForUpdate") Trace.Add("read:ForUpdate");
            if (method.Name == "FirstAsync")
            {
                Trace.Add("read:First"); var row = Activator.CreateInstance(type)!;
                foreach (var pair in new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "原名称", ["YzProjectId"] = 100L, ["YzAdminTypeId"] = 90L, ["YzRoleIds"] = "[]", ["IsSuper"] = 0, ["Pwd"] = "原密码不得传入" }) type.GetProperty(pair.Key)?.SetValue(row, pair.Value);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, [row]);
            }
            if (method.Name == "CountAsync") return Task.FromResult(1L);
            return query;
        }); return query;
    }
    private object Command(Type type, string kind)
    {
        object? command = null; command = Proxy(type, (method, args) => { if(method.Name is "AppendData" or "SetSource" && args is {Length:>0} && entityType.IsInstanceOfType(args[0]))lastWrittenEntity=args[0]; if (method.Name == "ExecuteAffrowsAsync") { Trace.Add("write:" + kind); return Task.FromResult(1); } return command; }); return command;
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> action) where T : class => (T)Proxy(typeof(T), action);
    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> action) { var proxy = DispatchProxy.Create(type, typeof(CallProxy)); ((CallProxy)proxy).Action = action; return proxy; }
    public class CallProxy : DispatchProxy { internal Func<MethodInfo, object?[]?, object?> Action = null!; protected override object? Invoke(MethodInfo? method, object?[]? args) => Action(method!, args); }
    public void Dispose() => Session.Dispose();
    private sealed class OfflineProvider(List<string> trace,Action<TimeSpan>? validateTime,Func<object?> writtenEntity,Type entityType,Func<string,TimeSpan?,TimeSpan?>? persistedTime) : IDatabaseProvider
    {
        /// <summary>从真实Mapper提交实体投影时间，专项可模拟数据库精度；不冒充实库执行。</summary>
        public Task<IReadOnlyList<TimeSpan?>> ReadPersistedTimeValuesAsync(DbTransaction transaction,string table,long id,IReadOnlyList<string> columns,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();trace.Add("read:PersistedTimes");
            var entity=writtenEntity()??throw new InvalidOperationException("尚无真实Mapper写入。");
            IReadOnlyList<TimeSpan?> values=columns.Select(column=>
            {
                var property=entityType.GetProperties().Single(property=>property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()?.Name==column);
                var value=(TimeSpan?)property.GetValue(entity);return persistedTime is null?value:persistedTime(column,value);
            }).ToArray();
            return Task.FromResult(values);
        }
        /// <summary>时间专项显式借用真实方言范围验证，事务与SQL执行仍为替身。</summary>
        public void ValidateTimeValue(TimeSpan value){if(validateTime is not null)validateTime(value);else if(!Metarill.Core.Contracts.ClockTimeValue.IsClock(value))throw new ArgumentOutOfRangeException(nameof(value));}
        public string Name => "mysql"; public DatabaseCapabilities Capabilities => new("mysql", true, false); public void Validate(DatabaseOptions options) { }
        public string QuoteIdentifier(string value) => new MySqlDatabaseProvider().QuoteIdentifier(value); public string ParameterPlaceholder(string value) => "@" + value;
        public IFreeSql CreateOrm(DatabaseOptions options) => throw new NotSupportedException(); public DbConnection CreateConnection(DatabaseOptions options) => new Connection(trace);
    }
    private sealed class Connection(List<string> trace) : DbConnection
    {
        private ConnectionState state; [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline"; public override string DataSource => "offline"; public override string ServerVersion => "offline"; public override ConnectionState State => state;
        public override void ChangeDatabase(string value) => throw new NotSupportedException(); public override void Open() => state = ConnectionState.Open; public override void Close() => state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) => new Transaction(this, trace); protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
    private sealed class Transaction(Connection connection, List<string> trace) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.RepeatableRead; protected override DbConnection DbConnection => connection;
        public override void Commit() => trace.Add("commit"); public override void Rollback() => trace.Add("rollback");
    }
}
