using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成Service的写后物理TIME回读，事务/ORM替身不能作为MySQL持久化证据。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class PersistedTimeGenerationTests
{
    /// <summary>真实Mapper写入后回读先于观察器，普通新增/更新VO与数据库精度值一致。</summary>
    [Fact]
    public async Task Generated_time_response_and_observers_use_persisted_values()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-persisted-time");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=ClockTimeGenerationTests.Catalog();catalog.ConfigPages[0].IsCopy=1;
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.GuardContract");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run=new GeneratedMutationGuardRun(assembly,new MySqlDatabaseProvider().ValidateTimeValue,(_,value)=>value is {} time?TimeSpan.FromTicks(time.Ticks-time.Ticks%10):null);
            var observer=new Observer(run.Trace);var service=run.Service([],[observer]);
            object Request(string operation,string body)=>JsonSerializer.Deserialize(body,assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101"+operation+"Request",true)!,JsonDefaults.Options)!;
            var input=JsonSerializer.Serialize(new{name="物理微秒",clock="12:34:56.9876549",duration="-30:00:00.9876549"});
            var result=await ClockTimeGenerationTests.Invoke(service,"Create101Async",Request("Create",input),null,run.Actor,CancellationToken.None);
            using(var json=JsonDocument.Parse(JsonSerializer.Serialize(result,result!.GetType(),JsonDefaults.Options)))
            {Assert.Equal("12:34:56.987654",json.RootElement.GetProperty("clock").GetString());Assert.Equal("-30:00:00.987654",json.RootElement.GetProperty("duration").GetString());}
            Assert.Equal(ClockTimeValue.ParseText("12:34:56.987654"),observer.Values!["clock"]);
            Assert.True(run.Trace.IndexOf("write:Insert")<run.Trace.IndexOf("read:PersistedTimes"));Assert.True(run.Trace.IndexOf("read:PersistedTimes")<run.Trace.IndexOf("observe:persisted"));
            var update=JsonSerializer.Serialize(new{id="1",clock="11:22:33.1234567"});
            var changed=await ClockTimeGenerationTests.Invoke(service,"Update101Async",Request("Update",update),null,run.Actor,CancellationToken.None);
            Assert.Equal(ClockTimeValue.ParseText("11:22:33.123456"),changed!.GetType().GetProperty("Clock")!.GetValue(changed));Assert.Equal(ClockTimeValue.ParseText("11:22:33.123456"),observer.Values!["clock"]);
            Assert.Equal(2,run.Trace.Count(item=>item=="read:PersistedTimes"));
            await ClockTimeGenerationTests.Invoke(service,"Copy101Async",1L,null,run.Actor,CancellationToken.None);
            Assert.Equal(3,run.Trace.Count(item=>item=="read:PersistedTimes"));
            using var failed=new GeneratedMutationGuardRun(assembly,new MySqlDatabaseProvider().ValidateTimeValue,(_,_)=>throw new IOException("合成回读故障"));
            var failedObserver=new Observer(failed.Trace);
            await Assert.ThrowsAsync<IOException>(()=>ClockTimeGenerationTests.Invoke(failed.Service([],[failedObserver]),"Create101Async",Request("Create",input),null,failed.Actor,CancellationToken.None));
            Assert.Contains("rollback",failed.Trace);Assert.DoesNotContain("commit",failed.Trace);Assert.Null(failedObserver.Values);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>数据库低精度可能将日末舍入为24小时；写后必须拒绝并回滚，观察器不接收非法时刻。</summary>
    [Fact]
    public async Task Persisted_clock_overflow_rolls_back_before_observers()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-persisted-clock-overflow");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=ClockTimeGenerationTests.Catalog();catalog.ConfigColumns.Single(column=>column.ColumnName=="clock").ColumnType="time(0)";
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.GuardContract");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run=new GeneratedMutationGuardRun(assembly,new MySqlDatabaseProvider().ValidateTimeValue,(column,value)=>column=="clock"?TimeSpan.FromHours(24):value);
            var observer=new Observer(run.Trace);var service=run.Service([],[observer]);
            var type=assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101CreateRequest",true)!;
            var request=JsonSerializer.Deserialize(JsonSerializer.Serialize(new{name="日末精度",clock="23:59:59.9999999"}),type,JsonDefaults.Options)!;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>ClockTimeGenerationTests.Invoke(service,"Create101Async",request,null,run.Actor,CancellationToken.None));
            Assert.Contains("write:Insert",run.Trace);Assert.Contains("read:PersistedTimes",run.Trace);Assert.Contains("rollback",run.Trace);Assert.DoesNotContain("commit",run.Trace);Assert.Null(observer.Values);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    private sealed class Observer(List<string> trace):IRecordMutationObserver
    {
        internal IReadOnlyDictionary<string,object?>? Values;
        /// <summary>实际写后观察器必须看到持久化精度，不能依赖请求原始小数。</summary>
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){Values=context.SubmittedFields;trace.Add("observe:persisted");return Task.CompletedTask;}
    }
}
