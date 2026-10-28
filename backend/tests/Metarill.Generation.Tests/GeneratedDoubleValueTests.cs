using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>编译执行实际生成Service／DTO；复用明确离线ORM传输，不宣称真实SQL或事务并发通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GeneratedDoubleValueTests
{
    /// <summary>普通创建／更新、默认值、主动null及手写非有限钩子走同一生成管线。</summary>
    [Fact]
    public async Task Generated_double_defaults_updates_filters_and_hook_guards_execute()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-generated-double");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var catalog=GeneratorTests.Catalog();catalog.ConfigColumns.Add(new(){Id="900",ConfigTableId="1",ColumnName="score",Name="测量值",LogicalType="double",DefaultValue="+1.25e100"});catalog.ConfigPageColumns.Add(new(){Id="901",ConfigPageId="101",ConfigColumnId="900",IsEdit=1,IsShow=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.GuardContract");
            await File.WriteAllTextAsync(Path.Combine(root,"DoubleHook.cs"),"""
using Metarill.Core.Security;
using Example.GuardContract.Generated.Entities;
namespace Example.GuardContract.Generated.Services;
public partial class CustomerService
{
    // 模拟手写业务产生非有限值，不能因HTTP输入合法就绕过数据库前边界。
    partial void BeforeSave(Customer entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {if(entity.Name=="钩子NaN")entity.Score=double.NaN;if(entity.Name=="钩子无穷")entity.Score=double.PositiveInfinity;}
}
""");
            await File.WriteAllTextAsync(Path.Combine(root,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            object Request(string operation,string json)=>JsonSerializer.Deserialize(json,assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101"+operation+"Request",true)!,JsonDefaults.Options)!;
            using(var run=new GeneratedMutationGuardRun(assembly))
            {
                var service=run.Service([]);var created=await Invoke(service,"Create101Async",Request("Create","{\"name\":\"默认值\"}"),null,run.Actor,CancellationToken.None);Assert.Equal(1.25e100,created!.GetType().GetProperty("Score")!.GetValue(created));Assert.Contains("write:Insert",run.Trace);
                var measured=await Invoke(service,"Update101Async",Request("Update","{\"id\":\"1\",\"score\":\"1.5e100\"}"),null,run.Actor,CancellationToken.None);Assert.Equal(1.5e100,measured!.GetType().GetProperty("Score")!.GetValue(measured));
                var updated=await Invoke(service,"Update101Async",Request("Update","{\"id\":\"1\",\"score\":null}"),null,run.Actor,CancellationToken.None);Assert.Null(updated!.GetType().GetProperty("Score")!.GetValue(updated));Assert.Contains("write:Update",run.Trace);
                var query=service.GetType().GetMethod("Query101",BindingFlags.Instance|BindingFlags.NonPublic)!;var request=new PageRequest{Filters=new(){["score"]=JsonSerializer.Deserialize<JsonElement>("1e309")}};Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>query.Invoke(service,[request,run.Actor])).InnerException);
            }
            foreach(var name in new[]{"钩子NaN","钩子无穷"})
            {
                using var run=new GeneratedMutationGuardRun(assembly);var service=run.Service([]);await Assert.ThrowsAsync<ArgumentException>(()=>Invoke(service,"Create101Async",Request("Create",JsonSerializer.Serialize(new{name,score=1.5})),null,run.Actor,CancellationToken.None));Assert.DoesNotContain("write:Insert",run.Trace);Assert.Contains("rollback",run.Trace);
            }
            Assert.Throws<JsonException>(()=>Request("Create","{\"name\":\"溢出\",\"score\":1e309}"));
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>默认值在发布时即验证，不把错误留到运行时或让下溢默认变为0。</summary>
    [Theory][InlineData("NaN")][InlineData("Infinity")][InlineData("1e309")][InlineData("1e-999")]
    public void Invalid_double_default_is_rejected_before_output(string value)
    {
        var catalog=GeneratorTests.Catalog();catalog.ConfigColumns.Single(column=>column.ColumnName=="name").LogicalType="double";catalog.ConfigColumns.Single(column=>column.ColumnName=="name").DefaultValue=value;var output=Path.Combine(Path.GetTempPath(),"metarill-invalid-double-"+Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,output,"Example.BadDouble"));Assert.False(Directory.Exists(output));
    }
    /// <summary>只解包真实异步调用的异常，不将测试期待值写入生成结果。</summary>
    private static async Task<object?> Invoke(object service,string method,params object?[] args)
    {
        Task task;try{task=(Task)service.GetType().GetMethod(method)!.Invoke(service,args)!;}catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        await task;return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
