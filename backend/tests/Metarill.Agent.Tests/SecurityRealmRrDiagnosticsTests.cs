using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>纯离线验证RR诊断和空集权限检查的调度，不建立连接、不运行实库Fact。</summary>
public sealed class SecurityRealmRrDiagnosticsTests
{
    /// <summary>两个观测分别失败后仍抛原业务异常，不让第二个观测被第一个异常跳过。</summary>
    [Fact]
    public async Task Failed_observations_preserve_original_exception_and_record_each_failure()
    {
        var original=new InvalidOperationException("业务正文含秘密，不得写入receipt");var evidence=new ConcurrentDictionary<string,object>();var calls=new List<string>();
        var thrown=await Assert.ThrowsAsync<InvalidOperationException>(()=>SecurityRealmRrDiagnostics.RethrowAfterObservationsAsync(original,evidence,
        [new("deleteWaits",()=>{calls.Add("delete");throw new UnauthorizedAccessException("SELECT/连接/凭据正文不得写入receipt");}),
         new("workbenchWaits",()=>{calls.Add("workbench");throw new TimeoutException("超时正文不得写入receipt");})]));
        Assert.Same(original,thrown);Assert.Equal(new[]{"delete","workbench"},calls);Assert.Equal(nameof(InvalidOperationException),Assert.IsType<RealmFailure>(evidence["operationFailure"]).Type);
        Assert.Equal(nameof(UnauthorizedAccessException),Assert.IsType<RealmFailure>(evidence["deleteWaitsFailure"]).Type);Assert.Equal(nameof(TimeoutException),Assert.IsType<RealmFailure>(evidence["workbenchWaitsFailure"]).Type);
        Assert.DoesNotContain("正文",JsonSerializer.Serialize(evidence),StringComparison.Ordinal);Assert.DoesNotContain("Message",JsonSerializer.Serialize(evidence),StringComparison.OrdinalIgnoreCase);
        using var safe=JsonDocument.Parse(JsonSerializer.Serialize(SecurityRealmRrDiagnostics.Safe(original),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));Assert.Equal(new[]{"type","hresult","mysql"},safe.RootElement.EnumerateObject().Select(property=>property.Name));
    }
    /// <summary>观测成功可以保留结构化锁信息，原失败仍必须抛回，不会误记成诊断成功。</summary>
    [Fact]
    public async Task Successful_observation_does_not_replace_original_failure()
    {
        var original=new ArgumentException("不可写入的业务正文");var evidence=new ConcurrentDictionary<string,object>();
        Assert.Same(original,await Assert.ThrowsAsync<ArgumentException>(()=>SecurityRealmRrDiagnostics.RethrowAfterObservationsAsync(original,evidence,[new("locks",()=>Task.FromResult<object>(new[]{"结构化锁信息"}))])));
        Assert.Equal(new[]{"结构化锁信息"},Assert.IsType<string[]>(evidence["locks"]));Assert.DoesNotContain("locksFailure",evidence.Keys);
    }
    /// <summary>T2等所有任务故障都保留，取消/成功不是Fault；聚合异常的正文不能泄漏。</summary>
    [Fact]
    public void Task_drain_faults_preserve_each_safe_type_without_exception_messages()
    {
        Task[] tasks=[Task.FromException(new InvalidOperationException("原T1秘密")),Task.CompletedTask,Task.FromException(new AggregateException(new TimeoutException("T2秘密"),new UnauthorizedAccessException("SQL秘密"))),Task.FromCanceled(new CancellationToken(true))];
        var faults=SecurityRealmRrDiagnostics.TaskFaults(tasks);Assert.Equal(new[]{0,2},faults.Select(fault=>fault.TaskIndex));Assert.Single(faults[0].Failures);Assert.Equal(2,faults[1].Failures.Count);
        Assert.Equal(new[]{nameof(TimeoutException),nameof(UnauthorizedAccessException)},faults[1].Failures.Select(failure=>failure.Type));Assert.All(faults.SelectMany(fault=>fault.Failures),failure=>Assert.NotEqual(0,failure.HResult));Assert.DoesNotContain("秘密",JsonSerializer.Serialize(faults),StringComparison.Ordinal);
    }
    /// <summary>真实Open共用此前置调度；开关关闭时即使列授权可用也不会查询列或进入业务夹具。</summary>
    [Fact]
    public async Task Instrumentation_off_stops_before_existing_column_permission_checks()
    {
        var calls=0;var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>SecurityRealmRrProbe.CheckObservationPrerequisitesAsync(0,sql=>{calls++;return Task.CompletedTask;}));
        Assert.Equal(0,calls);Assert.Contains("@@performance_schema未开启",error.Message,StringComparison.Ordinal);Assert.Contains("不能取得真实锁证据",error.Message,StringComparison.Ordinal);Assert.Contains("尚未创建业务夹具",error.Message,StringComparison.Ordinal);Assert.Null(error.InnerException);
    }
    /// <summary>仅开启分支继续原三表17列权限检查，不跳过既有诊断，也不以开关ON宣称锁等待成立。</summary>
    [Fact]
    public async Task Instrumentation_on_keeps_all_seventeen_observation_columns()
    {
        var queries=new List<string>();await SecurityRealmRrProbe.CheckObservationPrerequisitesAsync(1,sql=>{queries.Add(sql);return Task.CompletedTask;});
        Assert.Equal(3,queries.Count);Assert.Equal(17,queries.Sum(sql=>sql[..sql.IndexOf(" FROM ",StringComparison.Ordinal)].Count(value=>value==',')+1));
        Assert.Contains("`data_lock_waits`",queries[0]);Assert.Contains("`data_locks`",queries[1]);Assert.Contains("`threads`",queries[2]);Assert.All(queries,sql=>Assert.EndsWith(" WHERE 1=0",sql,StringComparison.Ordinal));
    }

    /// <summary>三个固定表检查均请求实际列而非SELECT 1；空集查询不读业务行。</summary>
    [Fact]
    public async Task Column_permission_preflight_checks_all_three_fixed_tables_with_empty_results()
    {
        var queries=new List<string>();await SecurityRealmRrProbe.CheckObservationColumnsAsync(sql=>{queries.Add(sql);return Task.CompletedTask;});Assert.Equal(3,queries.Count);
        Assert.Contains("`data_lock_waits`",queries[0]);Assert.Contains("`data_locks`",queries[1]);Assert.Contains("`threads`",queries[2]);
        Assert.All(queries,sql=>{Assert.EndsWith(" WHERE 1=0",sql,StringComparison.Ordinal);Assert.DoesNotContain("SELECT *",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("SELECT 1",sql,StringComparison.OrdinalIgnoreCase);});
        Assert.Contains("`REQUESTING_THREAD_ID`",queries[0]);Assert.Contains("`LOCK_DATA`",queries[1]);Assert.Contains("`PROCESSLIST_ID`",queries[2]);
    }
    /// <summary>权限前置失败立即停止后续检查并保留原异常，调用方不会拿到可创建夹具的Probe。</summary>
    [Fact]
    public async Task Column_permission_preflight_stops_on_denial_and_preserves_inner_failure()
    {
        var denial=new UnauthorizedAccessException("数据库错误正文不外传");var calls=0;
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>SecurityRealmRrProbe.CheckObservationColumnsAsync(sql=>{calls++;throw denial;}));
        Assert.Equal(1,calls);Assert.Same(denial,error.InnerException);Assert.Contains("performance_schema.data_lock_waits",error.Message,StringComparison.Ordinal);Assert.Contains("尚未创建业务夹具",error.Message,StringComparison.Ordinal);
        Assert.DoesNotContain("正文",JsonSerializer.Serialize(SecurityRealmRrDiagnostics.Safe(error)),StringComparison.Ordinal);
    }
}
