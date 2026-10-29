using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class MaterializedDerivedWriteTests
{
    /// <summary>实际生成Service执行新增/更新/复制/导入/软删；仅ORM为离线代理，不代替物理写入验收。</summary>
    [Fact]
    public async Task Generated_audits_follow_host_calendar_and_preserve_omitted_insert_time()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-audit-calendar");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog();
            foreach(var (id,name) in new[]{("9001","insert_time"),("9002","update_time")})catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="3",ColumnName=name,LogicalType="datetime",ColumnType="datetime(0)"});
            new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.Derived");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new StringLongJsonConverter());var runtime=new RecordedOrm(assembly);
            using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString=""},runtime.Orm,new OfflineProvider());var actor=new RuntimeActor(8,"日历审计核验",true,"7","");var service=runtime.Service("ConfigPage",session);
            var before=DateTime.Now;await Invoke(service,"Create301Async",Request("Create","{\"name\":\"新增\"}"),null,actor,CancellationToken.None);Audit(runtime.WrittenEntities.Last(),"InsertTime",before);Audit(runtime.WrittenEntities.Last(),"UpdateTime",before);
            var old=new DateTime(2020,1,2,3,4,5,DateTimeKind.Unspecified);runtime.Set("ConfigPage",new(){["Id"]=20L,["YzProjectId"]=7L,["InsertTime"]=old,["UpdateTime"]=old});
            before=DateTime.Now;await Invoke(service,"Update301Async",Request("Update","{\"id\":\"20\",\"name\":\"更新\"}"),null,actor,CancellationToken.None);Assert.Equal(old,Property(runtime.Current("ConfigPage"),"InsertTime"));Audit(runtime.Current("ConfigPage"),"UpdateTime",before);
            before=DateTime.Now;await Invoke(service,"Copy301Async",20L,null,actor,CancellationToken.None);Audit(runtime.WrittenEntities.Last(),"InsertTime",before);Audit(runtime.WrittenEntities.Last(),"UpdateTime",before);
            before=DateTime.Now;await Invoke(service,"Import301Async",Request("Import","{\"requestJson\":[{\"名称\":\"导入\"}]}"),actor,CancellationToken.None);Audit(runtime.WrittenEntities.Last(),"InsertTime",before);Audit(runtime.WrittenEntities.Last(),"UpdateTime",before);
            before=DateTime.Now;await Invoke(service,"Delete301Async",20L,null,actor,CancellationToken.None);Audit(runtime.Current("ConfigPage"),"DeleteNo",before);
            object Request(string kind,string json)=>JsonSerializer.Deserialize(json,assembly.GetType($"Example.Derived.Generated.DTOs.ConfigPagePage301{kind}Request",true)!,options)!;
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>用独立宿主时钟核对实际生成方法的日历值及真实JSON，不复用产品时钟作预期。</summary>
    private static void Audit(object entity,string property,DateTime before)
    {
        var actual=Assert.IsType<DateTime>(Property(entity,property));Assert.Equal(DateTimeKind.Unspecified,actual.Kind);Assert.InRange(actual,before,DateTime.Now);
        var json=JsonSerializer.Serialize(actual,JsonDefaults.Options);Assert.Equal(JsonSerializer.Serialize(actual.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)),json);
    }
}
