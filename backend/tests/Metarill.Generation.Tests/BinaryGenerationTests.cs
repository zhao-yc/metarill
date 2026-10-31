using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>编译执行真实生成器输出；事务和写命令沿既有离线传输，不冒充实库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class BinaryGenerationTests
{
    /// <summary>实际DTO、Service、导入与手写钩子保留字节，区分省略/null/空数组。</summary>
    [Fact]
    public async Task Generated_binary_crud_import_and_hooks_execute()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-binary-generation");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=GeneratorTests.Catalog();AddBinary(catalog);catalog.ConfigColumns.Single(column=>column.ColumnName=="payload").DefaultValue="AP8=";new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.GuardContract");
            var hand=Path.Combine(root,"generated/Services/CustomerService.cs");
            await File.AppendAllTextAsync(hand,"""

// 模拟手写规则产生超长内容，标准写入仍须拦截。
public partial class CustomerService
{
    partial void BeforeSave(global::Example.GuardContract.Generated.Entities.Customer entity,global::Metarill.Core.Security.RuntimeActor actor,bool creating,global::System.Collections.Generic.ISet<string> submittedFields)
    {if(entity.Name=="超长钩子")entity.Payload=new byte[5];}
}
""");
            var preserved=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.GuardContract");Assert.Equal(preserved,await File.ReadAllBytesAsync(hand));
            await File.WriteAllTextAsync(Path.Combine(root,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            object Request(string operation,string json)=>JsonSerializer.Deserialize(json,assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101"+operation+"Request",true)!,JsonDefaults.Options)!;
            using(var run=new GeneratedMutationGuardRun(assembly))
            {
                run.SeedRow=row=>row.GetType().GetProperty("Payload")!.SetValue(row,new byte[]{7,0,255});
                var service=run.Service([]);
                async Task<object?> Save(string operation,string json)=>await ClockTimeGenerationTests.Invoke(service,operation+"101Async",Request(operation,json),null,run.Actor,CancellationToken.None);
                byte[]? Bytes(object? value)=>(byte[]?)value!.GetType().GetProperty("Payload")!.GetValue(value);
                var defaultA=await Save("Create","""{"name":"默认甲"}""");var defaultB=await Save("Create","""{"name":"默认乙"}""");
                Assert.Equal(new byte[]{0,255},Bytes(defaultA));Assert.Equal(new byte[]{0,255},Bytes(defaultB));Assert.NotSame(Bytes(defaultA),Bytes(defaultB));
                Bytes(defaultA)![0]=99;Assert.Equal((byte)0,Bytes(defaultB)![0]);
                Assert.Null(Bytes(await Save("Create","""{"name":"主动空","payload":null}""")));
                Assert.Empty(Bytes(await Save("Create","""{"name":"主动零字节","payload":""}"""))!);
                var created=await Save("Create","""{"name":"字节","payload":"AP8Afw=="}""");Assert.Equal(new byte[]{0,255,0,127},Bytes(created));Assert.Contains("write:Insert",run.Trace);
                using(var json=JsonDocument.Parse(JsonSerializer.Serialize(created,created!.GetType(),JsonDefaults.Options)))Assert.Equal("AP8Afw==",json.RootElement.GetProperty("payload").GetString());
                Assert.Equal(new byte[]{7,0,255},Bytes(await Save("Update","""{"id":"1","remark":"省略"}""")));
                Assert.Empty(Bytes(await Save("Update","""{"id":"1","payload":""}"""))!);
                Assert.Null(Bytes(await Save("Update","""{"id":"1","payload":null}""")));
                Assert.Equal(new byte[]{0,255},Bytes(await Save("Update","""{"id":"1","payload":"AP8="}""")));
                foreach(var text in new[]{"AP8=",""})
                {
                    var parsed=(Dictionary<string,JsonElement>)(await ClockTimeGenerationTests.Invoke(service,"ParseImport101Async",JsonSerializer.SerializeToElement(new Dictionary<string,string>{{"名称","导入"},{"二进制",text}}),null,run.Actor,CancellationToken.None))!;
                    Assert.Equal(text,parsed["payload"].GetString());
                }
                var error=await Assert.ThrowsAsync<PageImportException>(()=>ClockTimeGenerationTests.Invoke(service,"ParseImport101Async",JsonSerializer.SerializeToElement(new Dictionary<string,string>{{"名称","错误"},{"二进制","不是Base64"}}),null,run.Actor,CancellationToken.None));
                Assert.Equal(PageImportError.Binary,error.Code);Assert.Equal("二进制",error.ColumnName);
            }
            foreach(var json in new[]{"""{"name":"超长输入","payload":"AQIDBAU="}""","""{"name":"超长钩子","payload":""}"""})
            {
                using var run=new GeneratedMutationGuardRun(assembly);
                await Assert.ThrowsAsync<ArgumentException>(()=>ClockTimeGenerationTests.Invoke(run.Service([]),"Create101Async",Request("Create",json),null,run.Actor,CancellationToken.None));Assert.DoesNotContain("write:Insert",run.Trace);Assert.Contains("rollback",run.Trace);
            }
            Assert.Throws<JsonException>(()=>Request("Create","""{"name":"数组","payload":[0,255]}"""));
            // 真正FreeSql MySQL表达式解析，仅取SQL，不连接或执行。
            using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            var type=assembly.GetType("Example.GuardContract.Generated.Services.CustomerService",true)!;var serviceSql=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);type.GetField("session",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(serviceSql,session);
            var mapper=Activator.CreateInstance(assembly.GetType("Example.GuardContract.Generated.Mappers.CustomerMapper",true)!,session);type.GetField("mapper",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(serviceSql,mapper);
            var query=type.GetMethod("Query101",BindingFlags.NonPublic|BindingFlags.Instance)!;
            foreach(var value in new object[]{"AP8=",new[]{"AA==","AP8="}})
            {
                var selected=query.Invoke(serviceSql,[new PageRequest{Filters=new(){{"payload",JsonSerializer.SerializeToElement(value)}}},new RuntimeActor(8,"离线",true,"100","1")])!;
                var sql=(string)typeof(BinaryGenerationTests).GetMethod(nameof(Sql),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(assembly.GetType("Example.GuardContract.Generated.Entities.Customer",true)!).Invoke(null,[selected])!;
                Assert.Matches(@"(?is)payload.{0,3}\s*(=|in\s*\()",sql);Assert.DoesNotContain("System.Byte[]",sql);Assert.Contains("yz_project_id",sql);Assert.Contains("delete_no",sql);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>非冗余读取也以byte[]参数和VO处理，避免变成数字集合或文本LIKE。</summary>
    [Fact]
    public async Task Nonredundant_binary_filters_and_vo_are_typed()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_binary_nr");AddBinary(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-binary-nonred");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.BinaryNonRed");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var name=GenerationModel.Pascal(catalog.ConfigTables[0].TableName);var suffix=catalog.ConfigPages[0].Id;var type=assembly.GetType("Example.BinaryNonRed.Generated.Services."+name+"Service",true)!;
            var service=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);var read=type.GetMethod("ReadFilters"+suffix,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)!;
            var request=new PageRequest{ParentId="71",Filters=new(){{"payload",JsonSerializer.SerializeToElement("AP8=")}}};var actor=new RuntimeActor(8,"字节",true,catalog.YzProjectId,"1");
            var filters=Assert.IsType<List<FixedReadFilter>>(read.Invoke(service,[request,actor]));Assert.Equal(new byte[]{0,255},Assert.IsType<byte[]>(Assert.Single(filters,filter=>filter.Field=="payload").Value));
            request.Filters["payload"]=JsonSerializer.SerializeToElement(new[]{"AA==","AP8="});filters=Assert.IsType<List<FixedReadFilter>>(read.Invoke(service,[request,actor]));Assert.Equal(2,Assert.IsAssignableFrom<System.Collections.IEnumerable>(Assert.Single(filters,filter=>filter.Field=="payload").Value).Cast<object>().Count());
            request.Filters["payload"]=JsonSerializer.SerializeToElement("无效");Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>read.Invoke(service,[request,actor])).InnerException);
            var vo=assembly.GetType("Example.BinaryNonRed.Generated.VOs."+name+"Page"+suffix+"Vo",true)!;var value=JsonSerializer.Deserialize("""{"payload":"AP8="}""",vo,JsonDefaults.Options)!;
            using var output=JsonDocument.Parse(JsonSerializer.Serialize(value,vo,JsonDefaults.Options));Assert.Equal("AP8=",output.RootElement.GetProperty("payload").GetString());
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>完整独立包从自身基础源码严格编译，保证新增合同实际下发。</summary>
    [Fact]
    public async Task Complete_binary_application_builds_from_own_sources()
    {
        var source=NonRedundantApplicationFixture.Root();var catalog=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;AddBinary(catalog);catalog.ConfigColumns.Single(column=>column.ColumnName=="payload").DefaultValue="AP8=";
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-binary-package");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(root,"application");new ApplicationPackager().Create(catalog,output,"Example.BinaryApplication",source);
            foreach(var command in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,command);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Contracts/BinaryFieldValue.cs")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>无效或超长二进制默认在发布前拒绝，不输出部分产物。</summary>
    [Theory][InlineData("不是Base64")][InlineData("AQIDBAU=")][InlineData("X'00FF'")]
    public void Binary_default_rejects_before_output(string value)
    {
        var catalog=GeneratorTests.Catalog();AddBinary(catalog);catalog.ConfigColumns.Single(column=>column.ColumnName=="payload").DefaultValue=value;
        var root=Path.Combine(Path.GetTempPath(),"metarill-binary-invalid-"+Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.InvalidBinary"));Assert.False(Directory.Exists(root));
    }
    /// <summary>只扩展隔离样本元数据，不变更平台或原前端。</summary>
    internal static void AddBinary(MetadataCatalog catalog)
    {
        var table=catalog.ConfigTables[0];var page=catalog.ConfigPages[0];page.IsImport=1;page.IsExport=1;page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("list");
        catalog.ConfigColumns.Add(new(){Id="9900",ConfigTableId=table.Id,ColumnName="payload",Name="二进制",LogicalType="binary",ColumnType="blob",ColumnLength="4"});
        catalog.ConfigPageColumns.Add(new(){Id="9901",ConfigPageId=page.Id,ConfigColumnId="9900",IsEdit=1,IsShow=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
    }
    /// <summary>通过实际FreeSql泛型入口取SQL，不执行命令。</summary>
    private static string Sql<T>(object query)where T:class=>((FreeSql.ISelect<T>)query).ToSql();
}
