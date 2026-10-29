using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Runtime;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成/编译/调用时间字段CRUD、导入与守卫；ORM仅替身，不冒充MySQL精度证明。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class ClockTimeGenerationTests
{
    /// <summary>原picker时分输入、微秒默认、显式清空、筛选与导入走同一类型合同。</summary>
    [Fact]
    public async Task Actual_generated_clock_crud_import_and_hook_guards_execute()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-clock-generation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog();new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.GuardContract");
            await File.AppendAllTextAsync(Path.Combine(directory,"generated/Services/CustomerService.cs"),"""
// 只在本隔离样本模拟手写规则产生跨日持续时间。
public partial class CustomerService
{
    partial void BeforeSave(global::Example.GuardContract.Generated.Entities.Customer entity,global::Metarill.Core.Security.RuntimeActor actor,bool creating,global::System.Collections.Generic.ISet<string> submittedFields)
    {if(entity.Name=="跨日钩子")entity.Clock=global::System.TimeSpan.FromHours(25);if(entity.Name=="超限钩子")entity.Duration=global::System.TimeSpan.FromHours(839);}
}
""");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            object Request(string operation,string json)=>JsonSerializer.Deserialize(json,assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101"+operation+"Request",true)!,JsonDefaults.Options)!;
            using(var run=new GeneratedMutationGuardRun(assembly,new MySqlDatabaseProvider().ValidateTimeValue))
            {
                var observer=new Observer();var service=run.Service([], [observer]);
                var created=await Invoke(service,"Create101Async",Request("Create","{\"name\":\"时刻\",\"clock\":\"08:09\",\"duration\":\"-25:01:02.123456\"}"),null,run.Actor,CancellationToken.None);
                Assert.Equal(SqlTimeValue.ParseText("-25:01:02.123456"),created!.GetType().GetProperty("Duration")!.GetValue(created));
                Assert.Equal(TimeSpan.FromHours(8)+TimeSpan.FromMinutes(9),created!.GetType().GetProperty("Clock")!.GetValue(created));
                Assert.Equal(ClockTimeValue.ParseText("12:34:56.123456"),created.GetType().GetProperty("ClockDefault")!.GetValue(created));
                using(var json=JsonDocument.Parse(JsonSerializer.Serialize(created,created.GetType(),JsonDefaults.Options))){Assert.Equal("-25:01:02.123456",json.RootElement.GetProperty("duration").GetString());Assert.Equal("08:09:00",json.RootElement.GetProperty("clock").GetString());Assert.Equal("12:34:56.123456",json.RootElement.GetProperty("clockDefault").GetString());}
                await Invoke(service,"Update101Async",Request("Update","{\"id\":\"1\",\"remark\":\"省略时刻\"}"),null,run.Actor,CancellationToken.None);Assert.DoesNotContain("clock",observer.Values!.Keys);
                var updated=await Invoke(service,"Update101Async",Request("Update","{\"id\":\"1\",\"clock\":\"\"}"),null,run.Actor,CancellationToken.None);Assert.Null(updated!.GetType().GetProperty("Clock")!.GetValue(updated));Assert.Contains("clock",observer.Values!.Keys);
                var query=service.GetType().GetMethod("Query101",BindingFlags.Instance|BindingFlags.NonPublic)!;
                Assert.NotNull(query.Invoke(service,[new PageRequest{Filters=new(){{"clock",JsonSerializer.SerializeToElement("12:34")}}},run.Actor]));
                Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>query.Invoke(service,[new PageRequest{Filters=new(){{"clock",JsonSerializer.SerializeToElement("24:00")}}},run.Actor])).InnerException);
                Assert.NotNull(query.Invoke(service,[new PageRequest{Filters=new(){{"duration",JsonSerializer.SerializeToElement("-25:01:02.123456")}}},run.Actor]));
                Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>query.Invoke(service,[new PageRequest{Filters=new(){{"clock",JsonSerializer.SerializeToElement(new[]{"12:34","30:00"})}}},run.Actor])).InnerException);
                var parsed=(Dictionary<string,JsonElement>)(await Invoke(service,"ParseImport101Async",JsonSerializer.SerializeToElement(new Dictionary<string,string>{{"名称","导入"},{"时刻","12:34"},{"时长","-30:00:00"}}),null,run.Actor,CancellationToken.None))!;
                Assert.Equal("12:34:00",parsed["clock"].GetString());Assert.Equal("-30:00:00",parsed["duration"].GetString());
                var error=await Assert.ThrowsAsync<PageImportException>(()=>Invoke(service,"ParseImport101Async",JsonSerializer.SerializeToElement(new Dictionary<string,string>{{"名称","无效"},{"时刻","不是时刻"}}),null,run.Actor,CancellationToken.None));Assert.Equal(PageImportError.DateTime,error.Code);Assert.Equal("时刻",error.ColumnName);
            }
            using(var run=new GeneratedMutationGuardRun(assembly,new MySqlDatabaseProvider().ValidateTimeValue))
            {await Assert.ThrowsAsync<ArgumentException>(()=>Invoke(run.Service([]),"Create101Async",Request("Create","{\"name\":\"跨日钩子\"}"),null,run.Actor,CancellationToken.None));Assert.DoesNotContain("write:Insert",run.Trace);Assert.Contains("rollback",run.Trace);}
            using(var run=new GeneratedMutationGuardRun(assembly,new MySqlDatabaseProvider().ValidateTimeValue))
            {await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>Invoke(run.Service([]),"Create101Async",Request("Create","{\"name\":\"超限钩子\"}"),null,run.Actor,CancellationToken.None));Assert.DoesNotContain("write:Insert",run.Trace);Assert.Contains("rollback",run.Trace);}
            var vue=Assert.Single(Directory.EnumerateFiles(Path.Combine(directory,"generated"),"*.vue",SearchOption.AllDirectories),path=>File.ReadAllText(path).Contains("<picker mode='time'"));
            Assert.Contains("@change='entity.clock=$event.detail.value'",await File.ReadAllTextAsync(vue));
            Assert.Contains("ClockTimeValue.FormatExport",await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/CustomerService.g.cs")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>非冗余固定筛选同样按字段选择合同，不能在混合表中把clock误交给宽TIME解析。</summary>
    [Fact]
    public async Task Nonredundant_time_filters_and_vo_execute_field_contracts()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_clock_nr");AddFields(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-clock-nonred");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NonRedClock");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var name=GenerationModel.Pascal(catalog.ConfigTables[0].TableName);var suffix=catalog.ConfigPages[0].Id;
            var type=assembly.GetType("Example.NonRedClock.Generated.Services."+name+"Service",true)!;
            var service=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            var read=type.GetMethod("ReadFilters"+suffix,BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic)!;
            var actor=new global::Metarill.Core.Security.RuntimeActor(8,"时间",true,catalog.YzProjectId,"1");
            var request=new PageRequest{ParentId="71",Filters=new(){{"clock",JsonSerializer.SerializeToElement("12:34:56.123456")},{"duration",JsonSerializer.SerializeToElement("-30:00")}}};
            var filters=Assert.IsType<List<Metarill.Data.Queries.FixedReadFilter>>(read.Invoke(service,[request,actor]));
            Assert.Contains(filters,filter=>filter.Field=="clock"&&Equals(filter.Value,ClockTimeValue.ParseText("12:34:56.123456")));
            Assert.Contains(filters,filter=>filter.Field=="duration"&&Equals(filter.Value,TimeSpan.FromHours(-30)));
            request.Filters["clock"]=JsonSerializer.SerializeToElement(new[]{"12:34","25:00"});
            Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>read.Invoke(service,[request,actor])).InnerException);
            var vo=assembly.GetType("Example.NonRedClock.Generated.VOs."+name+"Page"+suffix+"Vo",true)!;
            var value=JsonSerializer.Deserialize("{\"clock\":\"12:34:56.123456\",\"duration\":\"-30:00\"}",vo,JsonDefaults.Options)!;
            using var output=JsonDocument.Parse(JsonSerializer.Serialize(value,vo,JsonDefaults.Options));Assert.Equal("12:34:56.123456",output.RootElement.GetProperty("clock").GetString());Assert.Equal("-30:00:00",output.RootElement.GetProperty("duration").GetString());
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原完整包从包内基础源码严格构建，不能只编译仓库引用样本。</summary>
    [Fact]
    public async Task Complete_time_application_builds_from_its_own_sources()
    {
        var source=NonRedundantApplicationFixture.Root();var catalog=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        AddFields(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-clock-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"application");new ApplicationPackager().Create(catalog,output,"Example.ClockApplication",source);
            foreach(var command in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,command);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Contracts/ClockTimeJsonConverter.cs")));
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Contracts/ClockTimeValue.cs")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>非法或精度丢失的默认必须在写出前失败。</summary>
    [Theory][InlineData("CURRENT_TIME()")][InlineData("24:00")][InlineData("12:34:56.1234567")][InlineData("任意文本")]
    public void Invalid_clock_defaults_do_not_write_outputs(string value)
    {
        var catalog=Catalog();catalog.ConfigColumns.Single(column=>column.ColumnName=="clock_default").DefaultValue=value;
        var output=Path.Combine(Path.GetTempPath(),"metarill-invalid-clock-"+Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,output,"Example.InvalidClock"));Assert.False(Directory.Exists(output));
    }
    /// <summary>原示例模型扩展两个独立时间字段，页面和路由沿既有规则。</summary>
    private static MetadataCatalog Catalog(){var catalog=GeneratorTests.Catalog();AddFields(catalog);return catalog;}
    /// <summary>仅为隔离夹具增加受控元数据；原平台目录不增加业务时间列。</summary>
    private static void AddFields(MetadataCatalog catalog)
    {
        var table=catalog.ConfigTables[0];var page=catalog.ConfigPages[0];page.IsImport=1;page.IsExport=1;page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("list");
        foreach(var (id,name,label,value) in new[]{("900","clock","时刻",""),("902","clock_default","默认时刻","12:34:56.123456")})
        {
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId=table.Id,ColumnName=name,Name=label,LogicalType="time",ColumnType="time(6)",ControlKind="time",DefaultValue=value,YzProjectId=catalog.YzProjectId});
            catalog.ConfigPageColumns.Add(new(){Id=(long.Parse(id)+1).ToString(),ConfigPageId=page.Id,ConfigColumnId=id,IsEdit=1,IsShow=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)},YzProjectId=catalog.YzProjectId});
        }
        catalog.ConfigColumns.Add(new(){Id="904",ConfigTableId=table.Id,ColumnName="duration",Name="时长",LogicalType="time",ColumnType="time(6)",YzProjectId=catalog.YzProjectId});
        catalog.ConfigPageColumns.Add(new(){Id="905",ConfigPageId=page.Id,ConfigColumnId="904",IsEdit=1,IsShow=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)},YzProjectId=catalog.YzProjectId});
    }
    /// <summary>只解包实际异步调用的原异常，测试不替业务产生期望值。</summary>
    private static async Task<object?> Invoke(object service,string method,params object?[] args)
    {
        Task task;try{task=(Task)service.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(service,args)!;}catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        await task;return task.GetType().GetProperty("Result")?.GetValue(task);
    }
    private sealed class Observer:IRecordMutationObserver
    {
        internal IReadOnlyDictionary<string,object?>? Values;
        /// <summary>记录真实提交列，省略不能被误判为明确清空。</summary>
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){Values=context.SubmittedFields;return Task.CompletedTask;}
    }
}
