using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class DoubleStatisticsGenerationTests
{
    /// <summary>真正执行生成Controller／Service和原R.map，只有ORM终端传输是受控替身。</summary>
    private static async Task ExerciseOrdinaryStatistics(Assembly assembly)
    {
        const string ns="Example.DoubleStatistics.Generated.";
        var entity=assembly.GetType(ns+"Entities.StatRecord",true)!;using var provider=new StatisticsMemoryProvider();
        object Row(long id,long project,string name,double? amount,bool deleted=false)
        {
            var row=Activator.CreateInstance(entity)!;
            foreach(var pair in new Dictionary<string,object?>{{"Id",id},{"YzProjectId",project},{"Name",name},{"Amount",amount},{"Status",2},{"AverageStatus",2},{"DeleteNo",deleted?DateTime.UtcNow:null}})entity.GetProperty(pair.Key)!.SetValue(row,pair.Value);
            return row;
        }
        provider.Rows=[Row(1,54,"目标甲",1.25e100),Row(2,54,"目标乙",null),Row(3,56,"目标外项目",1e200),Row(4,54,"目标已删除",9e199,true)];
        using var session=new DatabaseSession(new(){ConnectionString="offline",ExpectedDatabase="offline"},provider.Orm,provider);var authorization=new StatisticsAuthorization();
        var mapper=Activator.CreateInstance(assembly.GetType(ns+"Mappers.StatRecordMapper",true)!,session)!;
        var service=Activator.CreateInstance(assembly.GetType(ns+"Services.StatRecordService",true)!,session,mapper,new SnowflakeIdGenerator(966),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>())!;
        var controller=Activator.CreateInstance(assembly.GetType(ns+"Controllers.StatRecordPage301Controller",true)!,service,authorization)!;
        async Task<JsonElement> Page(int number,int size=1)
        {
            var task=(Task)controller.GetType().GetMethod("Page")!.Invoke(controller,[new PageRequest{PageNo=number,PageSize=size,Filters=new(){{"name",JsonSerializer.SerializeToElement("目标")}}},CancellationToken.None])!;await task;
            return JsonSerializer.SerializeToElement(task.GetType().GetProperty("Result")!.GetValue(task),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        var first=await Page(1);var stats=first.GetProperty("map").GetProperty("statistics").GetProperty("amount");Assert.Equal(0d,stats.GetProperty("currentPage").GetDouble());Assert.Equal(1.25e100,stats.GetProperty("total").GetDouble());Assert.Equal(2,first.GetProperty("data").GetProperty("total").GetInt64());
        var second=await Page(2);Assert.Equal(1.25e100,second.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("currentPage").GetDouble());
        Assert.All(provider.Calls.Where(call=>call.Method=="FloatingBuckets"),call=>Assert.Equal(0,call.Page));
        await CaptureRealAggregateSql(assembly,entity);
        provider.Rows=[];var empty=await Page(1);Assert.Equal(0d,empty.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").GetDouble());
        provider.Rows=[Row(1,54,"目标甲",1d)];provider.DoubleAggregateOverride=double.PositiveInfinity;await Assert.ThrowsAsync<InvalidOperationException>(()=>Page(1));
        provider.Rows=[Row(1,54,"目标甲",double.MaxValue),Row(2,54,"目标乙",double.MaxValue)];provider.DoubleAggregateOverride=0d;await Assert.ThrowsAsync<InvalidOperationException>(()=>Page(1,20));
        provider.Calls.Clear();authorization.Deny=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Page(1));Assert.Empty(provider.Calls);
    }
    /// <summary>把真实生成聚合表达式交实际FreeSql翻译，连接前截获SQL，不自己拼出期望聚合。</summary>
    private static async Task CaptureRealAggregateSql(Assembly assembly,Type entity)
    {
        using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("禁止网络连接")).UseAutoSyncStructure(false).UseGenerateCommandParameterWithLambda(true).Build();
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=offline;Database=offline;User ID=unused"},orm);
        const string ns="Example.DoubleStatistics.Generated.";
        var mapper=Activator.CreateInstance(assembly.GetType(ns+"Mappers.StatRecordMapper",true)!,session)!;
        var type=assembly.GetType(ns+"Services.StatRecordService",true)!;
        var service=Activator.CreateInstance(type,session,mapper,new SnowflakeIdGenerator(967),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>())!;
        var request=new PageRequest{PageSize=1,PageNo=1,Filters=new(){{"name",JsonSerializer.SerializeToElement("目标")}}};
        var query=type.GetMethod("Query301",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[request,new RuntimeActor(1,"翻译观察",false,"54","55")])!;
        string? sql=null;object?[] values=[];
        orm.Aop.CurdBefore+=(_,args)=>{sql=args.Sql;values=args.DbParms.Select(parameter=>parameter.Value).ToArray();throw new AggregateCaptured();};
        var method=typeof(Metarill.Data.Queries.FloatingStatistics).GetMethod(nameof(Metarill.Data.Queries.FloatingStatistics.SumAsync))!.MakeGenericMethod(entity);
        await Assert.ThrowsAsync<AggregateCaptured>(async()=>await(Task)method.Invoke(null,[session,query,"amount",CancellationToken.None])!);
        Assert.NotNull(sql);Assert.Contains("sum(",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("amount",sql);Assert.Contains("yz_project_id",sql);Assert.Contains("delete_no",sql);
        Assert.Contains("DECIMAL(65,0)",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("GROUP BY",sql,StringComparison.OrdinalIgnoreCase);Assert.Matches(@"(?i)\blimit\s+(?:0\s*,\s*)?2048\b",sql);Assert.DoesNotContain("ORDER BY",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains(values,value=>value is string text&&text.Contains("目标",StringComparison.Ordinal));Assert.DoesNotContain("目标",sql);
    }
    private sealed class AggregateCaptured:Exception;

}
