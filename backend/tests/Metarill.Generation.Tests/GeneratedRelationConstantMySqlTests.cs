using System.Globalization;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Data.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class GeneratedRelationOptionsMySqlTests
{
    /// <summary>真实生成编译和HTTP/MySQL覆盖常量候选、默认值、钩子、复制、导入与事务回滚，保持旧接口和前端。</summary>
    [StandaloneExportMySqlFact]
    public Task Constant_relation_conditions_share_candidate_selected_write_copy_and_import_scope()=>RunListConditions(false);
    /// <summary>原表单槽、最终实体、默认及覆盖导入依赖和真实Excel名称缓存共用固定计划。</summary>
    [StandaloneExportMySqlFact]
    public Task Form_relation_conditions_share_real_entity_import_and_export_context()=>RunListConditions(true);
    /// <summary>非冗余源表使用同一真实表单、导入、导出和锁定链路，不以独立编译代替数据库验证。</summary>
    [StandaloneExportMySqlFact]
    public Task Nonredundant_form_relation_conditions_share_real_entity_import_and_export_context()=>RunListConditions(true,true);
    /// <summary>两类条件分别独占样本，继承原常量、当前读和并发验收，失败回执不改写。</summary>
    private async Task RunListConditions(bool form,bool nonRedundant=false)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var connection=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||connection.Database!=options.ExpectedDatabase||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("固定条件只允许核实后的隔离业务库。");
        var repository=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少验收仓库路径。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少隔离实例pin。")));
        await using(var pinned=new MySqlConnection(options.ConnectionString))
        {
            await pinned.OpenAsync();await using var command=pinned.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||reader.GetString(0)!=options.ExpectedDatabase||reader.GetString(1)!=pin.RootElement.GetProperty("expectedHostname").GetString()||reader.GetString(2)!=pin.RootElement.GetProperty("expectedServerUuid").GetString())throw new InvalidOperationException("隔离实例pin不符。");
        }
        var nonce=Guid.NewGuid().ToString("N");var prefix=(form?"mt_rf_":"mt_rc_")+nonce[..12];var catalog=RelationOptionsContractTests.Catalog(prefix);
        if(nonRedundant)catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        var source=catalog.ConfigPages.Single(page=>page.Id=="201");source.IsCopy=1;source.IsImport=1;
        catalog.ConfigColumns.Single(column=>column.Id=="22").Name="记录名";
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="25");foreign.Name="目标";foreign.DefaultValue=(First+2).ToString(CultureInfo.InvariantCulture);
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",whereSql=$"parent_id=@parentId AND yz_project_id=@current_yz_project_id AND alias='A AND B, C' AND name LIKE '允许%' AND id IN ({First},{First+1},{First+2},{First+3}) AND delete_no IS NULL"});
        // 独立来源页没有来源父，目标父来自真实可编辑外键；依赖变化必须重验旧的省略FK。
        catalog.ConfigPages.Add(new(){Id="202",ConfigTableId="2",WhereSql="yz_project_id=@current_yz_project_id",IsImport=1});
        catalog.ConfigPageColumns.AddRange([new(){Id="20222",ConfigPageId="202",ConfigColumnId="22"},new(){Id="20225",ConfigPageId="202",ConfigColumnId="25"},new(){Id="20226",ConfigPageId="202",ConfigColumnId="26"}]);
        catalog.ConfigColumns.Single(column=>column.Id=="26").Name="父ID";
        foreign.SortNo=-5;catalog.ConfigColumns.Single(column=>column.Id=="26").SortNo=5;
        var sourceCamel=Pascal(prefix+"_source");sourceCamel=char.ToLowerInvariant(sourceCamel[0])+sourceCamel[1..];
        if(form)
        {
            catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-10;
            catalog.ConfigColumns.AddRange([new(){Id="27",ConfigTableId="2",ColumnName="allowed",LogicalType="string",Name="分类",DefaultValue="A AND B, C"},new(){Id="28",ConfigTableId="2",ColumnName="minimum",LogicalType="int64",Name="最小ID",DefaultValue=First.ToString(CultureInfo.InvariantCulture)}]);
            foreach(var id in new[]{"27","28"})catalog.ConfigColumns.Single(column=>column.Id==id).Extra["isAutoPostBack"]=JsonSerializer.SerializeToElement(1);
            foreach(var pageId in new[]{"203","204"})
            {
                var page=new Metarill.Core.Metadata.PageMetadata{Id=pageId,ConfigTableId="2",Name="表单联动",WhereSql=source.WhereSql,IsImport=1,IsCopy=1,IsExport=pageId=="203"?1:0};
                if(pageId=="204")page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(page);
                foreach(var fieldId in new[]{"22","25","27","28"})catalog.ConfigPageColumns.Add(new(){Id=pageId+fieldId,ConfigPageId=pageId,ConfigColumnId=fieldId,IsShow=fieldId is "27" or "28"?0:1});
                catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==pageId&&field.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql=$"parent_id=@parentId AND yz_project_id=@current_yz_project_id AND alias=@formParameters.{sourceCamel}.allowed AND id>=@formParameters.minimum AND name LIKE '允许%'"}));
            }
        }
        var evidence=Path.Combine(repository,(form?".local/form-conditions/form-mysql-":".local/fixed-conditions/constant-mysql-")+nonce);Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,names=catalog.ConfigTables.Select(table=>table.TableName),instancePinned=true,verified=false}));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-relation-constant-mysql");var directory=Path.Combine(boundary,nonce);
        WebApplication? app=null;var created=false;var verified=false;
        try
        {
            using var session=new DatabaseSession(options);var schema=new SchemaService(session);Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var plan=await schema.PreviewAsync(catalog);Assert.Equal(3,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await InsertParents(session,prefix+"_parent");
            await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(prefix+"_target")+" (id,name,yz_project_id,parent_id,alias) VALUES (@a,'允许甲',@project,@parent,@alias),(@b,'允许乙',@project,@parent,@alias),(@c,'允许排除',@project,@parent,'别的分类'),(@d,'不允许',@project,@parent,@alias)",("a",First),("b",First+1),("c",First+2),("d",First+3),("project",Project),("parent",Parent),("alias","A AND B, C"));
            // 已存在的失败关系只用于验证复制必须重新检查，不能因为原记录可读就绕过候选。
            await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(prefix+"_source")+" (id,name,yz_project_id,parent_id,target_id) VALUES (@id,'旧不合法关系',@project,@parent,@target)",("id",First+9000),("project",Project),("parent",Parent),("target",First+2));
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ConstantMySql");
            var formHook=form?"if(entity.Name==\"联动钩子\"||entity.Name==\"联动登记\"){entity.Allowed=\"别的分类\";submittedFields.Add(\"allowed\");} if(entity.Name==\"联动未登记\"){entity.Allowed=\"别的分类\";} if(entity.Name==\"联动未登记外键\"){entity.TargetId="+(First+2).ToString(CultureInfo.InvariantCulture)+"L;}":"";
            await File.WriteAllTextAsync(Path.Combine(directory,"ConstantHook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.ConstantMySql.Generated.Services;
public partial class {{Pascal(prefix+"_source")}}Service
{
    /// <summary>验收手写钩子改写最终关联后，仍须经过生成Service的统一范围校验。</summary>
    partial void BeforeSave(Example.ConstantMySql.Generated.Entities.{{Pascal(prefix+"_source")}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
        {
        if(entity.Name=="钩子拒绝")
        {
            entity.TargetId={{First+2}}L;submittedFields.Add("target_id");
        }
        {{formHook}}
    }
}
""");
            var assembly=await BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            var snapshotGuard=new ConstantSnapshotGuard(options,prefix);var pauseObserver=new ConstantPauseObserver();
            builder.Services.AddSingleton<IRecordMutationGuard>(snapshotGuard);builder.Services.AddSingleton<IRecordMutationObserver>(pauseObserver);
            var exports=new ExcelPageExportService(new ExportOptions{RootDirectory=Path.Combine(directory,"private-exports"),MaxRows=100});builder.Services.AddSingleton<IPageExportService>(exports);
            builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(859));
            builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ConstantAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.ConstantMySql.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration=>configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>
            {
                try{await next();}
                catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or PageImportException)
                {await context.Response.WriteAsJsonAsync(R<string>.Error("固定条件验收拒绝"));}
            });app.MapControllers();await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(20)};
            var endpoint="/api/admin/auto/"+prefix+"_source/201";var candidate=endpoint+"/findNameIdTarget25";
            var legacy=Data(await Send(candidate,new{parentId=Parent.ToString(),formParameters=new{current_yz_project_id=(Project+1).ToString(),sql="1=1"}})).EnumerateArray().ToArray();
            Assert.Equal(new[]{First.ToString(),(First+1).ToString()},legacy.Select(Id).Order());
            var paged=Data(await Send(candidate+"/page",new{parentId=Parent.ToString(),pageNo=1,pageSize=1}));Assert.Equal("2",paged.GetProperty("total").ToString());Assert.Single(paged.GetProperty("records").EnumerateArray());
            Assert.Equal("1",Data(await Send(candidate+"/page",new{parentId=Parent.ToString(),keyword="允许乙",pageSize=10})).GetProperty("total").ToString());
            foreach(var selectedRoute in new[]{candidate,candidate+"/selected"})
            {
                /// <summary>旧只读回读和专用已选端点仅提供原固定ID合同。</summary>
                object Good(long id)=>selectedRoute==candidate?new{parentId=Parent.ToString(),id=id.ToString()}:(object)new{parentId=Parent.ToString(),selectedIds=new[]{id.ToString()}};
                Assert.Single(Data(await Send(selectedRoute,Good(First))).EnumerateArray());
                Assert.Equal(500,(await Send(selectedRoute,Good(First+2))).GetProperty("code").GetInt32());
            }
            // 普通新增/更新/导入只有本页动作；不额外索取目标列表维护权限。
            client.DefaultRequestHeaders.Add("denyTarget","1");
            var before=await Count();var good=Data(await Send(endpoint+"/add?parentId="+Parent,new{name="保存合法",targetId=First.ToString()}));var id=good.GetProperty("id").GetString()!;
            Assert.Equal(before+1,await Count());
            foreach(var bad in new object[]{new{name="默认拒绝"},new{name="显式拒绝",targetId=(First+2).ToString()},new{name="钩子拒绝",targetId=First.ToString()}})
                Assert.Equal(500,(await Send(endpoint+"/add?parentId="+Parent,bad)).GetProperty("code").GetInt32());
            Assert.Equal(before+1,await Count());
            Assert.Equal(500,(await Send(endpoint+"/update?parentId="+Parent,new{id,name="被回滚",targetId=(First+2).ToString()})).GetProperty("code").GetInt32());
            Assert.Equal("保存合法",Data(await Get(endpoint+"/detail/"+id+"?parentId="+Parent)).GetProperty("name").GetString());
            Assert.Equal(500,(await Send(endpoint+"/copy/"+(First+9000)+"?parentId="+Parent,new{})).GetProperty("code").GetInt32());Assert.Equal(before+1,await Count());
            Data(await Send(endpoint+"/copy/"+id+"?parentId="+Parent,new{}));Assert.Equal(before+2,await Count());
            var accepted=await Send(endpoint+"/importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{new Dictionary<string,object?>{{"记录名","导入合法"},{"目标","允许乙"}}}});Assert.Equal(200,accepted.GetProperty("code").GetInt32());
            var imported=await Count();Assert.Equal(before+3,imported);
            var rejected=await Send(endpoint+"/importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{new Dictionary<string,object?>{{"记录名","应整批回滚"},{"目标","允许甲"}},new Dictionary<string,object?>{{"记录名","后行拒绝"},{"目标","允许排除"}}}});Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Equal(imported,await Count());
            var independentPage="/api/admin/auto/"+prefix+"_source/202";
            var independentRow=Data(await Send(independentPage+"/add",new{name="独立父合法",parentId=Parent.ToString(),targetId=First.ToString()}));
            var independentId=independentRow.GetProperty("id").GetString();
            Assert.Equal(500,(await Send(independentPage+"/update",new{id=independentId,parentId=(Parent+1).ToString()})).GetProperty("code").GetInt32());
            Assert.Equal(Parent.ToString(),Data(await Get(independentPage+"/detail/"+independentId)).GetProperty("parentId").GetString());
            // Excel中先给关系、后给父，仍应由固定依赖拓扑先解析父字段。
            Assert.Equal(200,(await Send(independentPage+"/importDatas202",new{requestJson=new[]{new Dictionary<string,object?>{{"目标","允许乙"},{"父ID",Parent.ToString()},{"记录名","乱序合法导入"}}}})).GetProperty("code").GetInt32());
            var independentCount=await Count();Assert.Equal(500,(await Send(independentPage+"/importDatas202",new{requestJson=new[]{new Dictionary<string,object?>{{"目标","允许乙"},{"父ID",(Parent+1).ToString()},{"记录名","错误父导入"}}}})).GetProperty("code").GetInt32());Assert.Equal(independentCount,await Count());
            client.DefaultRequestHeaders.Remove("denyTarget");
            if(form)
            {
                var formPage="/api/admin/auto/"+prefix+"_source/203";var formOptions=formPage+"/findNameIdTarget25";
                var values=new Dictionary<string,object?>{{sourceCamel,new{allowed="A AND B, C"}},{"minimum",First.ToString()},{"current_yz_project_id",(Project+1).ToString()},{"sql","1=1"}};
                var candidates=Data(await Send(formOptions,new{parentId=Parent.ToString(),formParameters=values})).EnumerateArray().ToArray();Assert.Equal(new[]{First.ToString(),(First+1).ToString()},candidates.Select(Id).Order());
                Assert.Equal("2",Data(await Send(formOptions+"/page",new{parentId=Parent.ToString(),formParameters=values,pageSize=1})).GetProperty("total").ToString());
                foreach(var originalSlot in new object[]{new Dictionary<string,object?>{{sourceCamel,new{allowed="A AND B, C",minimum=First.ToString()}}},new{allowed="A AND B, C",minimum=First.ToString()}})
                    Assert.Equal(2,Data(await Send(formOptions,new{parentId=Parent.ToString(),formParameters=originalSlot})).GetArrayLength());
                Assert.Single(Data(await Send(formOptions,new{parentId=Parent.ToString(),formParameters=values,id=First.ToString()})).EnumerateArray());
                Assert.Equal(500,(await Send(formOptions+"/selected",new{parentId=Parent.ToString(),formParameters=values,selectedIds=new[]{(First+2).ToString()}})).GetProperty("code").GetInt32());
                foreach(var invalid in new object?[]{null,new{},new Dictionary<string,object?>{{sourceCamel,new{allowed=new[]{"A"}}},{"minimum",First.ToString()}},new Dictionary<string,object?>{{sourceCamel,new{allowed="A AND B, C"}},{"minimum",JsonSerializer.Deserialize<JsonElement>("1e-100")}}})
                    Assert.Equal(500,(await Send(formOptions,new{parentId=Parent.ToString(),formParameters=invalid})).GetProperty("code").GetInt32());
                var injection=new Dictionary<string,object?>{{sourceCamel,new{allowed="%' OR 1=1 --"}},{"minimum",First.ToString()}};
                Assert.Empty(Data(await Send(formOptions,new{parentId=Parent.ToString(),formParameters=injection})).EnumerateArray());
                var main=Data(await Send(formPage+"/add?parentId="+Parent,new{name="联动甲",targetId=First.ToString(),allowed="A AND B, C",minimum=First.ToString()}));var mainId=main.GetProperty("id").GetString();
                // 未登记的手写变化不持久化，不能拿它验证另一字段后留下不匹配的实际记录。
                Assert.Equal(500,(await Send(formPage+"/update?parentId="+Parent,new{id=mainId,name="联动未登记",targetId=(First+2).ToString()})).GetProperty("code").GetInt32());
                Assert.Equal(500,(await Send(formPage+"/update?parentId="+Parent,new{id=mainId,name="联动未登记外键",allowed="别的分类"})).GetProperty("code").GetInt32());
                foreach(var change in new object[]{new{id=mainId,allowed="别的分类"},new{id=mainId,minimum=(First+1).ToString()}})Assert.Equal(500,(await Send(formPage+"/update?parentId="+Parent,change)).GetProperty("code").GetInt32());
                var unchanged=Data(await Get(formPage+"/detail/"+mainId+"?parentId="+Parent));Assert.Equal("A AND B, C",unchanged.GetProperty("allowed").GetString());Assert.Equal(First.ToString(),unchanged.GetProperty("minimum").GetString());
                Data(await Send(formPage+"/update?parentId="+Parent,new{id=mainId,name="联动登记",targetId=(First+2).ToString()}));
                Assert.Equal("别的分类",Data(await Get(formPage+"/detail/"+mainId+"?parentId="+Parent)).GetProperty("allowed").GetString());
                Data(await Send(formPage+"/update?parentId="+Parent,new{id=mainId,name="联动甲",targetId=First.ToString(),allowed="A AND B, C"}));
                Data(await Send(formPage+"/add?parentId="+Parent,new{name="联动默认",targetId=First.ToString()}));
                Data(await Send(formPage+"/add?parentId="+Parent,new{name="联动乙",targetId=(First+2).ToString(),allowed="别的分类"}));
                var countBeforeHook=await Count();Assert.Equal(500,(await Send(formPage+"/add?parentId="+Parent,new{name="联动钩子",targetId=First.ToString()})).GetProperty("code").GetInt32());Assert.Equal(countBeforeHook,await Count());
                var shuffled=new[]{new Dictionary<string,object?>{{"目标","允许乙"},{"最小ID",First.ToString()},{"分类","A AND B, C"},{"记录名","乱序联动导入"}}};
                Assert.Equal(200,(await Send(formPage+"/importDatas203",new{parentId=Parent.ToString(),requestJson=shuffled})).GetProperty("code").GetInt32());
                var importCount=await Count();Assert.Equal(500,(await Send(formPage+"/importDatas203",new{parentId=Parent.ToString(),requestJson=new[]{new Dictionary<string,object?>{{"记录名","首行需回滚"},{"目标","允许甲"},{"分类","A AND B, C"}},new Dictionary<string,object?>{{"目标","允许排除"},{"分类","A AND B, C"},{"记录名","后行越界"}}}})).GetProperty("code").GetInt32());Assert.Equal(importCount,await Count());
                var overwrite="/api/admin/auto/"+prefix+"_source/204/importDatas204";
                Assert.Equal(200,(await Send(overwrite,new{parentId=Parent.ToString(),requestJson=new[]{new Dictionary<string,object?>{{"记录名","联动甲"},{"目标","允许乙"}}}})).GetProperty("code").GetInt32());
                Assert.Equal(500,(await Send(overwrite,new{parentId=Parent.ToString(),requestJson=new[]{new Dictionary<string,object?>{{"记录名","联动甲"},{"分类","别的分类"}}}})).GetProperty("code").GetInt32());
                Data(await Send(formPage+"/copy/"+mainId+"?parentId="+Parent,new{}));
                // 已存在的不同联动上下文共享同一FK，导出不能借用另一组的名称缓存。
                await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(prefix+"_source")+" (id,name,yz_project_id,parent_id,target_id,allowed,minimum) VALUES (@id,'导出隔离坏',@project,@parent,@target,'别的分类',@minimum)",("id",First+9100),("project",Project),("parent",Parent),("target",First),("minimum",First));
                var ticket=Data(await Send(formPage+"/initExport",new{parentId=Parent.ToString()})).GetString()!;var download=await exports.OpenDownloadAsync(ticket);
                using(download.Stream)using(var workbook=new XLWorkbook(download.Stream))
                {
                    var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.DoesNotContain("分类",headers.Keys);Assert.DoesNotContain("最小ID",headers.Keys);
                    var names=sheet.RowsUsed().Skip(1).ToDictionary(row=>row.Cell(headers["记录名"]).GetString()+"#"+row.RowNumber(),row=>row.Cell(headers["目标"]).GetString());
                    Assert.Equal(First.ToString(),names.Single(pair=>pair.Key.StartsWith("导出隔离坏#",StringComparison.Ordinal)).Value);
                    Assert.Equal("允许甲",names.Single(pair=>pair.Key.StartsWith("联动默认#",StringComparison.Ordinal)).Value);Assert.Equal("允许排除",names.Single(pair=>pair.Key.StartsWith("联动乙#",StringComparison.Ordinal)).Value);
                }
            }
            // RR已建立旧快照后，候选变为合法应按当前读成功；父被删除则必须按当前读拒绝。
            await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(prefix+"_target")+" SET alias='快照旧值' WHERE id=@id",("id",First));
            snapshotGuard.Mode="candidate-current";Data(await Send(endpoint+"/add?parentId="+Parent,new{name="RR当前合法",targetId=First.ToString()}));
            var beforeParent=await Count();snapshotGuard.Mode="parent-current";
            try{Assert.Equal(500,(await Send(endpoint+"/add?parentId="+Parent,new{name="RR父删除拒绝",targetId=First.ToString()})).GetProperty("code").GetInt32());Assert.Equal(beforeParent,await Count());}
            finally{await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(prefix+"_parent")+" SET delete_no=NULL WHERE id=@id",("id",Parent));}
            // 只操作本测试精确随机表，使用普通连接的行锁等待证明，无需任何全局观测授权。
            var pending=Send(endpoint+"/add?parentId="+Parent,new{name="并发持锁",targetId=First.ToString()});
            try
            {
                await pauseObserver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using var contender=new MySqlConnection(options.ConnectionString);await contender.OpenAsync();
                await using var command=contender.CreateCommand();command.CommandText="SET SESSION innodb_lock_wait_timeout=1";await command.ExecuteNonQueryAsync();
                foreach(var table in new[]{prefix+"_target",prefix+"_parent"})
                {
                    command.CommandText="UPDATE "+session.Provider.QuoteIdentifier(table)+" SET name=name WHERE id=@id";command.Parameters.Clear();command.Parameters.AddWithValue("id",table.EndsWith("_target",StringComparison.Ordinal)?First:Parent);
                    var locked=await Assert.ThrowsAsync<MySqlException>(async()=>{await command.ExecuteNonQueryAsync();});Assert.Equal(1205,locked.Number);
                }
                command.Parameters.Clear();command.Parameters.AddWithValue("id",First+1);command.CommandText="UPDATE "+session.Provider.QuoteIdentifier(prefix+"_target")+" SET alias=alias WHERE id=@id";await command.ExecuteNonQueryAsync();
            }
            finally{pauseObserver.Release.TrySetResult();Data(await pending);}
            // 提交后原目标可以立即修改，证明锁没有泄漏到后续请求。
            await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(prefix+"_target")+" SET alias='A AND B, C' WHERE id=@id",("id",First));
            // 修改候选分类后，分页/已选仍读取同一常量计划，不能从先前选中状态放宽。
            await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(prefix+"_target")+" SET alias='已退出' WHERE id=@id",("id",First));
            Assert.Equal("1",Data(await Send(candidate+"/page",new{parentId=Parent.ToString(),pageSize=10})).GetProperty("total").ToString());
            Assert.Equal(500,(await Send(candidate+"/selected",new{parentId=Parent.ToString(),selectedIds=new[]{First.ToString()}})).GetProperty("code").GetInt32());
            verified=true;
            await File.WriteAllTextAsync(Path.Combine(evidence,"receipt.json"),JsonSerializer.Serialize(new{nonce,verified=true,generatedCompiled=true,httpExecuted=true,mysqlExecuted=true,constantConditions=true,formConditions=form,nonRedundant,unmarkedHookChecked=form,registeredHookChecked=form,realExcelExecuted=form,defaultAndHookChecked=true,parentDependencyUpdateChecked=true,parentDependencyImportChecked=true,copyChecked=true,importBatchRollback=true,repeatableReadCurrentChecked=true,independentLockWaitChecked=true,browserExecuted=false,deviceExecuted=false}));

            /// <summary>所有读取/写入经过真实HTTP入口，失败时保留TRX而不回显数据库凭据。</summary>
            async Task<JsonElement> Send(string path,object body){using var response=await client.PostAsJsonAsync(path,body);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>();}
            /// <summary>详情沿用原GET路由，不能以直接数据库读取替代页面协议。</summary>
            async Task<JsonElement> Get(string path)=>await (await client.GetAsync(path)).Content.ReadFromJsonAsync<JsonElement>();
            /// <summary>独立连接计数确认失败没有产生部分业务写入。</summary>
            async Task<long> Count(){await using var db=new MySqlConnection(options.ConnectionString);await db.OpenAsync();await using var cmd=db.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM "+session.Provider.QuoteIdentifier(prefix+"_source");return Convert.ToInt64(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
        }
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);foreach(var table in catalog.ConfigTables)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(table.TableName));
                await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();
                foreach(var table in catalog.ConfigTables){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";command.Parameters.AddWithValue("name",table.TableName);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}
                await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names=catalog.ConfigTables.Select(table=>table.TableName),verified,independentAbsenceChecked=true}));
            }
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>在真实业务事务中制造旧一致性快照，再由独立连接改当前值，不使用全局观测权限。</summary>
    private sealed class ConstantSnapshotGuard(DatabaseOptions options,string prefix):IRecordMutationGuard
    {
        public string? Mode{get;set;}
        /// <summary>准备入口先建立原事务快照，再改变当前候选或父状态。</summary>
        public async Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)
        {
            var mode=Mode;if(mode is null||preparation.Operation!=RecordMutationOperation.Create)return;Mode=null;
            await using var read=preparation.Transaction.Connection!.CreateCommand();read.Transaction=preparation.Transaction;read.CommandText="SELECT @@transaction_isolation";
            Assert.Equal("REPEATABLE-READ",Convert.ToString(await read.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture));
            var target="`"+prefix+"_target`";var parent="`"+prefix+"_parent`";
            read.CommandText="SELECT alias FROM "+target+" WHERE id="+First.ToString(CultureInfo.InvariantCulture);await read.ExecuteScalarAsync(cancellationToken);
            await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync(cancellationToken);await using var write=independent.CreateCommand();
            write.CommandText=mode=="candidate-current"?"UPDATE "+target+" SET alias='A AND B, C' WHERE id=@id":"UPDATE "+parent+" SET delete_no=UTC_TIMESTAMP() WHERE id=@id";
            write.Parameters.AddWithValue("id",mode=="candidate-current"?First:Parent);Assert.Equal(1,await write.ExecuteNonQueryAsync(cancellationToken));
        }
    }

    /// <summary>在生成Service已完成校验而事务未提交时暂停，让独立普通连接验证窄行锁与提交释放。</summary>
    private sealed class ConstantPauseObserver:IRecordMutationObserver
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>仅暂停指定自有样本，超时和取消均结束等待，不阻塞无关操作。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
        {
            if(!context.SubmittedFields.TryGetValue("name",out var name)||!Equals(name,"并发持锁"))return;
            Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(15),cancellationToken);
        }
    }

    /// <summary>当前身份由测试宿主固定；可单独拒绝目标列表以检验写入没有新增权限门槛。</summary>
    private sealed class ConstantAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>不从客户端表单读取身份或项目。</summary>
        public RuntimeActor RequireActor()=>new(859,"固定条件验收",false,Project.ToString(),"1");
        /// <summary>只在指定测试头下拒绝目标页读取，其它当前动作保持合法。</summary>
        public void RequirePagePermission(string pageId,string operation)
        {if(pageId=="301"&&accessor.HttpContext!.Request.Headers.ContainsKey("denyTarget"))throw new UnauthorizedAccessException("目标列表拒权");}
    }
}
