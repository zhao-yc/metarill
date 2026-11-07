using ClosedXML.Excel;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Relations;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Maintenance;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原复选R1派生缓存、5000完整选择、来源条件和维护在真实生成HTTP及同一MySQL事务验证。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RedundantDerivedCheckboxMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001,Parent=9007199254759001;
    /// <summary>显式缓存与整数父的完整业务、跨批及预算回滚。</summary>
    [StandaloneExportMySqlFact]public Task Explicit_integer_parent()=>Run(false,"int64");
    /// <summary>显式缓存与字符串父保持原请求。</summary>
    [StandaloneExportMySqlFact]public Task Explicit_string_parent()=>Run(false,"string");
    /// <summary>自动缓存与整数父不依赖显式名称列。</summary>
    [StandaloneExportMySqlFact]public Task Automatic_integer_parent()=>Run(true,"int64");
    /// <summary>自动缓存与字符串父保持同一运行时权威。</summary>
    [StandaloneExportMySqlFact]public Task Automatic_string_parent()=>Run(true,"string");
    /// <summary>显式缓存经两个R0节点传播并切换中间关联。</summary>
    [StandaloneExportMySqlFact]public Task Nested_explicit_integer_parent()=>Run(false,"int64",true);
    /// <summary>自动缓存与字符串父同样证明两层名称及删除。</summary>
    [StandaloneExportMySqlFact]public Task Nested_automatic_string_parent()=>Run(true,"string",true);
    /// <summary>配置、域名、数据库和实例均验证，不接受命令行临时连接覆盖。</summary>
    private static async Task Run(bool automatic,string parent,bool nested=false)
    {
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=config.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Server!="deve-server.yinzon.com"||cs.Database!=options.ExpectedDatabase||cs.Port!=3306)throw new InvalidOperationException("R1派生复选专项只允许Server-200既定隔离业务库。");await Pin(options);await Exercise(options,automatic,parent,nested);
    }
    /// <summary>独占目录扩展物理审计缓存，不能把数组名称当作标量中间路径。</summary>
    private static async Task Exercise(DatabaseOptions options,bool automatic,string parent,bool nested)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_rcb_"+nonce[..12];var c=RedundantDerivedCheckboxTests.Catalog(prefix,automatic,parent,nested);c.ConfigColumns.Single(x=>x.Id=="25").DefaultValue=JsonSerializer.Serialize(new[]{Id(First+5002)});c.ConfigButtons.AddRange([JsonSerializer.SerializeToElement(new{id="91001",sql="/admin/sys/system_maintenance/tables"}),JsonSerializer.SerializeToElement(new{id="91002",sql="/admin/sys/system_maintenance/backfill_join_columns"})]);
        c.ConfigTables.Add(new(){Id="95",YzProjectId=c.YzProjectId,TableName=prefix+"_audit",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="95101",ConfigTableId="95",YzProjectId=c.YzProjectId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="95102",ConfigTableId="95",YzProjectId=c.YzProjectId,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4"},new(){Id="95103",ConfigTableId="95",YzProjectId=c.YzProjectId,ColumnName="company_name",LogicalType="string",ColumnLength="200",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}},new(){Id="95104",ConfigTableId="95",YzProjectId=c.YzProjectId,ColumnName="yz_project_id",LogicalType="int64"},new(){Id="95105",ConfigTableId="95",YzProjectId=c.YzProjectId,ColumnName="delete_no",LogicalType="datetime"}]);
        var source=prefix+"_source";var target=prefix+"_target";var company=prefix+"_company";var audit=prefix+"_audit";var terminal=nested?prefix+"_brand":company;var terminalColumn=nested?"label":"name";var terminalPage=nested?"601":"401";var names=c.ConfigTables.Select(x=>x.TableName).Order(StringComparer.Ordinal).ToArray();var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/redundant-derived-checkbox/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-checkbox-http");var directory=Path.Combine(boundary,nonce);using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var auth=new Auth();var observer=new TailObserver(source,audit,store);var guard=new Snapshot(options,c,terminal,terminalColumn);WebApplication? app=null;var created=false;var passed=false;var stage="意图";
        await Receipt("intent",new{nonce,automatic,parent,nested,names,database=options.ExpectedDatabase});
        try
        {
            stage="生成及显式结构";new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.R1CheckboxHttp");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var maintenance=(RelationMaintenanceCatalog)assembly.GetType("Example.R1CheckboxHttp.Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;Assert.Contains("target_names",maintenance.MixedSourceColumns[source]);Assert.Empty((await schema.InspectAsync(c)).Tables);var plan=await schema.PreviewAsync(c);Assert.Equal(nested?6:5,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);
            await Insert(session,prefix+"_parent",[Row("id",Parent,"yz_project_id",Project,"delete_no",null,"name","父甲"),Row("id",Parent+1,"yz_project_id",Project,"delete_no",null,"name","父乙")]);var companies=new List<Dictionary<string,object?>>();var targets=new List<Dictionary<string,object?>>();
            for(var index=0;index<5005;index++){companies.Add(Row("id",First+20000+index,"yz_project_id",Project,"delete_no",null,terminalColumn,index==0?"A_%'甲":index==1?"B乙":"X"+index.ToString("D5",CultureInfo.InvariantCulture)));targets.Add(Row("id",First+index,"yz_project_id",index==5002?Project+1:Project,"delete_no",index==5003?new DateTime(2026,1,1):null,"parent_id",ParentValue(index==5004?Parent+1:Parent),"company_id",First+20000+index,"segment",index==5001?2:1,"status",index==5000?0:1));}await Insert(session,terminal,companies);if(nested)await Insert(session,company,companies.Select(row=>Row("id",row["id"],"yz_project_id",Project,"delete_no",null,"brand_id",row["id"])).ToArray());await Insert(session,target,targets);await store.InsertAsync(audit,Row("id",First+30000,"company_id",First+20000,"company_name","A_%'甲","yz_project_id",Project));
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=100});var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(851));builder.Services.AddSingleton<IRuntimeAuthorization>(auth);builder.Services.AddSingleton<ISecurityIdentityAccessor>(auth);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(guard);builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddSingleton(new SystemMaintenanceBusiness(session,maintenance,auth));assembly.GetType("Example.R1CheckboxHttp.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddApplicationPart(typeof(SystemMaintenanceController).Assembly).AddJsonOptions(j=>j.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or JsonException or PageImportException or MySqlException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(60)};
            // 原业务、候选、Excel和维护Controller均实际调用；请求不提供SQL或数据库上下文。
            async Task<JsonElement> Post(string path,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(path,body)).Content.ReadAsStringAsync());
            Task<JsonElement> Action(string action,object body)=>Post("/api/admin/auto/"+source+"/201/"+action+"?parentId="+Parent,body);
            Task<JsonElement> Company(string name)=>Post("/api/admin/auto/"+terminal+"/"+terminalPage+"/update",Row("id",Id(First+20000),terminalColumn,name));
            Task<JsonElement> Maintain()=>Post("/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="2"});
            // 直接修改目标仅临时授该页update；复选候选和保存期间目标动作仍全部拒绝。
            async Task<JsonElement> ChangeLink(long key,long companyId){auth.TargetWrite=true;try{return await Post("/api/admin/auto/"+target+"/301/update?parentId="+Parent,new{id=Id(key),companyId=Id(companyId)});}finally{auth.TargetWrite=false;}}
            Task<JsonElement> Options(object? selected=null,int segment=1,long owner=Parent){var body=Row("parentId",Id(owner),"formParameters",new{segment,current_yz_project_id=Project+1});if(selected is not null)body.Add("id",selected);return Post("/api/admin/auto/"+source+"/201/findNameIdTarget25",body);}
            async Task<string> State(){var rows=new Dictionary<string,object>();foreach(var table in c.ConfigTables)rows[table.TableName]=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x)).ToArray();return JsonSerializer.Serialize(rows);}
            async Task Names(string[] expected,IEnumerable<long> ids){foreach(var key in ids)Assert.Equal(expected,JsonSerializer.Deserialize<string[]>(SqlRowStore.Text(Assert.Single(await store.ReadIncludingDeletedAsync(source,Row("id",key))),"target_names")));}
            async Task Audit(string? expected)=>Assert.Equal(expected,Assert.Single(await store.ReadIncludingDeletedAsync(audit,Row("id",First+30000)))["company_name"]);
            stage="原候选及来源动作";var all=Data(await Options());Assert.Equal(5000,all.GetArrayLength());Assert.Equal("X04999",all[0].GetProperty("name").GetString());Assert.Equal("A_%'甲",all[4999].GetProperty("text").GetString());Assert.Empty(Data(await Options(Array.Empty<string>())).EnumerateArray());Assert.Single(Data(await Options(new[]{Id(First)})).EnumerateArray());Assert.Single(Data(await Options(null,2)).EnumerateArray());Assert.Single(Data(await Options(null,1,Parent+1)).EnumerateArray());auth.Denied.UnionWith(["create","update","list"]);Assert.Equal(500,(await Options()).GetProperty("code").GetInt32());auth.Denied.Remove("create");Assert.Equal(5000,Data(await Options()).GetArrayLength());auth.Denied.Clear();
            stage="CRUD及范围反例";var selected=new[]{Id(First),Id(First+1)};var added=Data(await Action("add",new{name="复选派生来源",segment=1,targetIds=selected}));var id=added.GetProperty("id").GetString()!;var primary=long.Parse(id,CultureInfo.InvariantCulture);observer.Primary=primary;var consumers=new List<long>{primary};Assert.Equal(new[]{"A_%'甲","B乙"},Values(added));var echo=added.EnumerateObject().ToDictionary(x=>x.Name,x=>(object?)x.Value.Clone());echo["targetNames"]=new[]{"伪造"};Data(await Action("update",echo));var copy=Data(await Action("copy/"+id,new{}));var copyId=long.Parse(copy.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);consumers.Add(copyId);var before=await State();foreach(var bad in new[]{First+5000,First+5001,First+5002,First+5003,First+5004}){Assert.Equal(500,(await Action("update",new{id,targetIds=new[]{Id(bad)}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}Assert.Equal(500,(await Action("update",new{id,segment=2})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Post("/api/admin/auto/"+source+"/202/add?parentId="+Parent,new{name="复选隐藏默认",segment=1})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var template=Assert.Single(await store.ReadIncludingDeletedAsync(source,Row("id",primary)));foreach(var (key,other,deleted) in new[]{(First+31000,true,false),(First+31001,false,true)}){var row=new Dictionary<string,object?>(template){["id"]=key,["name"]="复选历史隔离",["yz_project_id"]=other?Project+1:Project,["delete_no"]=deleted?new DateTime(2026,1,1):null,["target_names"]="[\"历史\"]"};await store.InsertAsync(source,row);}
            stage="统一传播及旧RR";var count=observer.Count;Data(await Company("新甲"));Assert.Equal(count+1,observer.Count);await Names(["新甲","B乙"],consumers);await Audit("新甲");foreach(var key in new[]{First+31000,First+31001})Assert.Equal("[\"历史\"]",SqlRowStore.Text(Assert.Single(await store.ReadIncludingDeletedAsync(source,Row("id",key))),"target_names"));guard.Enabled=true;Assert.Equal(new[]{"RR当前甲","B乙"},Values(Data(await Action("update",new{id,targetIds=selected}))));Assert.True(guard.SawOld&&guard.CommittedNew);await Names(["新甲","B乙"],[copyId]);await Audit("新甲");Data(await Company("统一当前甲"));await Names(["统一当前甲","B乙"],consumers);await Audit("统一当前甲");
            stage="尾部失败整笔回滚";before=await State();observer.SawPatched=false;var failed=await Company("尾部失败");Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.Contains("复选尾部失败",failed.GetProperty("msg").GetString());Assert.True(observer.SawPatched);Assert.Equal(before,await State());
            stage="中间关联切换";Data(await ChangeLink(First,First+20002));await Names(["X00002","B乙"],consumers);if(nested){Data(await Post("/api/admin/auto/"+company+"/401/update",new{id=Id(First+20002),brandId=Id(First+20003)}));await Names(["X00003","B乙"],consumers);Data(await Post("/api/admin/auto/"+company+"/401/update",new{id=Id(First+20002),brandId=Id(First+20002)}));await Names(["X00002","B乙"],consumers);}Data(await Company("旧目标无关"));await Names(["X00002","B乙"],consumers);await Audit("旧目标无关");Data(await ChangeLink(First,First+20000));Data(await Company("统一当前甲"));await Names(["统一当前甲","B乙"],consumers);
            stage="原导入与真实XLSX";auth.Denied.UnionWith(["create","update","list"]);Data(await Action("importDatas201",new{parentId=Id(Parent),requestJson=new[]{ImportRow("复选派生导入",Id(First),Id(First+1))}}));before=await State();Assert.Equal(500,(await Action("importDatas201",new{parentId=Id(Parent),requestJson=new[]{ImportRow("复选整批首行",Id(First)),ImportRow("复选整批尾行",Id(First+999999))}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());auth.Denied.Clear();var ticket=Data(await Post("/api/admin/auto/"+source+"/201/initExport",new PageRequest{ParentId=Id(Parent),SelectedIds=[id]})).GetString()!;using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket))){Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var book=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(x=>x.GetString(),x=>x.Address.ColumnNumber);Assert.Equal(selected,JsonSerializer.Deserialize<string[]>(sheet.Row(2).Cell(headers["targetIds"]).GetString()));}
            stage="原维护及重复零";auth.DenyMaintenance=true;Assert.Equal(500,(await Maintain()).GetProperty("code").GetInt32());auth.DenyMaintenance=false;await store.UpdateAsync(terminal,First+20000,Row(terminalColumn,"SQL当前甲"));await Names(["统一当前甲","B乙"],consumers);Assert.True(Data(await Maintain()).GetProperty("affectedRows").GetInt64()>0);await Names(["SQL当前甲","B乙"],consumers);Assert.Equal(0,Data(await Maintain()).GetProperty("affectedRows").GetInt64());Data(await Company("统一当前甲"));await Audit("统一当前甲");
            stage="5000完整缓存及5001拒绝";var current=Data(await Options());var full=Data(await Action("add",new{name="复选完整5000",segment=1,targetIds=current.EnumerateArray().Select(x=>x.GetProperty("id").GetString()).ToArray()}));Assert.Equal(5000,Values(full).Length);Assert.Equal(current.EnumerateArray().Select(x=>x.GetProperty("name").GetString()),Values(full));before=await State();Assert.Equal(500,(await Action("update",new{id,targetIds=Enumerable.Repeat(Id(First),5001).ToArray()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());await store.UpdateAsync(target,First+5000,Row("status",1));Assert.Equal(500,(await Options()).GetProperty("code").GetInt32());Assert.Single(Data(await Options(new[]{Id(First)})).EnumerateArray());await store.UpdateAsync(target,First+5000,Row("status",0));
            var wide=!nested&&!automatic&&parent=="int64";if(wide)
            {
                stage="跨批历史预算回滚";for(var index=0;index<131;index++){var row=new Dictionary<string,object?>(template){["id"]=First+40000+index,["name"]="复选跨批"+index,["target_names"]="[\"待回填\"]"};await store.InsertAsync(source,row);}await store.UpdateAsync(source,copyId,Row("target_ids",JsonSerializer.Serialize(Enumerable.Repeat(Id(First),5001))));before=await State();failed=await Company("预算应回滚");Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.Contains("预算",failed.GetProperty("msg").GetString());Assert.Equal(before,await State());Assert.Equal(500,(await Maintain()).GetProperty("code").GetInt32());Assert.Equal(before,await State());await store.UpdateAsync(source,copyId,Row("target_ids",JsonSerializer.Serialize(selected)));Assert.True(Data(await Maintain()).GetProperty("affectedRows").GetInt64()>=131);await Names(["统一当前甲","B乙"],consumers);
            }
            stage="null空数组及删除";var cleared=Data(await Action("update",new{id,targetIds=(object?)null}));Assert.Equal(JsonValueKind.Null,cleared.GetProperty("targetIds").ValueKind);Assert.Null(Assert.Single(await store.ReadIncludingDeletedAsync(source,Row("id",primary)))["target_names"]);Data(await Action("update",new{id,targetIds=Array.Empty<string>()}));Assert.Equal("[]",SqlRowStore.Text(Assert.Single(await store.ReadIncludingDeletedAsync(source,Row("id",primary))),"target_names"));Data(await Action("update",new{id,targetIds=selected}));using(var request=new HttpRequestMessage(HttpMethod.Delete,"/api/admin/auto/"+terminal+"/"+terminalPage+"/"){Content=JsonContent.Create(new{id=Id(First+20000)})})Data(JsonSerializer.Deserialize<JsonElement>(await(await client.SendAsync(request)).Content.ReadAsStringAsync()));await Names([null!,"B乙"],consumers);await Audit(null);Assert.Empty((await schema.PreviewAsync(c)).Changes);passed=true;await Receipt("verified",new{nonce,automatic,parent,nested,passed,generatedHttp=true,originalComponent=true,actual5000Selection=true,currentRrRead=true,wholeImportRollback=true,actualXlsxDownload=true,renameAndLinkSwitch=true,observerOnlyTarget=true,originalMaintenanceHttp=true,repeatBackfillZero=true,wideBudgetRollback=wide,formalIdentity=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,automatic,parent,nested,stage,type=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){await Pin(options);using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names){await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var value=command.CreateParameter();value.ParameterName="name";value.Value=name;command.Parameters.Add(value);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>父字段按元数据持久化，接口仍保持字符串ID。</summary>
        object ParentValue(long value)=>parent=="string"?Id(value):value;
        /// <summary>首因、意图和独立清理仅写本轮私有目录。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>数组和物理审计均已更新后制造失败，验证原目标事务整体回滚。</summary>
    private sealed class TailObserver(string source,string audit,SqlRowStore store):IRecordMutationObserver
    {
        internal long Primary;internal int Count;internal bool SawPatched;
        /// <summary>消费者内部刷新不得递归进入Service观察器。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default){Count++;if(!c.SubmittedFields.Values.Any(v=>Equals(v,"尾部失败")))return;using var values=JsonDocument.Parse(SqlRowStore.Text(Assert.Single(await store.ReadAsync(source,Row("id",Primary))),"target_names"));SawPatched=values.RootElement.EnumerateArray().Any(v=>v.GetString()=="尾部失败")&&Equals(Assert.Single(await store.ReadAsync(audit,Row("id",First+30000)))["company_name"],"尾部失败");throw new InvalidOperationException("复选尾部失败");}
    }
    /// <summary>实际旧RR快照之后独立连接改名，显式复选重选须读到新联接名称。</summary>
    private sealed class Snapshot(DatabaseOptions options,MetadataCatalog catalog,string terminal,string terminalColumn):IRecordMutationGuard
    {
        internal bool Enabled,SawOld,CommittedNew;
        /// <summary>仅在指定保存执行一次，独立连接绕过事件不应改写其它消费者。</summary>
        public async Task BeforeMutationAsync(RecordMutationPreparation p,CancellationToken token=default){if(!Enabled)return;Enabled=false;await using(var command=p.Transaction.Connection!.CreateCommand()){command.Transaction=p.Transaction;command.CommandText="SELECT @@transaction_isolation";Assert.Equal("REPEATABLE-READ",Convert.ToString(await command.ExecuteScalarAsync(token),CultureInfo.InvariantCulture));command.CommandText="SELECT `"+terminalColumn+"` FROM `"+terminal+"` WHERE id=@id";var id=command.CreateParameter();id.ParameterName="id";id.Value=First+20000;command.Parameters.Add(id);SawOld=Equals(await command.ExecuteScalarAsync(token),"新甲");}using var other=new DatabaseSession(options);await new SqlRowStore(other,catalog).UpdateAsync(terminal,First+20000,Row(terminalColumn,"RR当前甲"));CommittedNew=true;}
        /// <summary>真实保存的条件、ID和缓存由产品继续校验。</summary>
        public Task BeforeWriteAsync(RecordMutationValidation p,CancellationToken token=default)=>Task.CompletedTask;
    }
    /// <summary>普通来源动作授权，目标列表故意拒绝；原维护按钮可精确撤销。</summary>
    private sealed class Auth:IRuntimeAuthorization,ISecurityIdentityAccessor
    {
        internal HashSet<string> Denied{get;}=new(StringComparer.Ordinal);internal bool DenyMaintenance,TargetWrite;
        /// <summary>项目身份来自宿主固定上下文。</summary>
        public RuntimeActor RequireActor()=>new(851,"R1派生复选专项",false,Id(Project),"5");
        /// <summary>复选只借用来源已授动作，不应调用目标列表权限。</summary>
        public void RequirePagePermission(string page,string operation){if(page=="301"&&!(TargetWrite&&operation=="update")||page=="201"&&Denied.Contains(operation))throw new UnauthorizedAccessException("复选固定测试减权。");}
        /// <summary>原维护业务继续检查普通身份和两个发布按钮。</summary>
        public AuthenticatedIdentity RequireIdentity()=>new(RequireActor(),new HashSet<string>(),852,null){PrincipalProjectId=Id(Project),PrincipalAdminTypeId="5",ButtonIds=DenyMaintenance?new HashSet<string>():new HashSet<string>{"91001","91002"}};
    }
    /// <summary>建表及清理前都复核真实实例，域名自身不当作实例证明。</summary>
    private static async Task Pin(DatabaseOptions options){using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"R1派生复选实例pin不符。");}
    /// <summary>原R1 Excel导入使用ID JSON数组与物理表单字段，不能套用R0名称数组。</summary>
    private static Dictionary<string,object?> ImportRow(string name,params string[] ids)=>Row("name",name,"segment",1,"targetIds",JsonSerializer.Serialize(ids));
    /// <summary>VO名称数组允许空值，null缓存和空数组由数据库独立核对。</summary>
    private static string?[] Values(JsonElement row)=>row.GetProperty("targetNames").ValueKind==JsonValueKind.Null?[]:row.GetProperty("targetNames").EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.Null?null:v.GetString()).ToArray();
    /// <summary>长ID从不经过浮点转换。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>种子只写已创建的本轮表，值全部参数化，批次不超过200行。</summary>
    private static async Task Insert(DatabaseSession session,string table,IReadOnlyList<Dictionary<string,object?>> rows)
    {
        foreach(var batch in rows.Chunk(200)){var columns=batch[0].Keys.ToArray();await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();var values=new List<string>();for(var index=0;index<batch.Length;index++){Assert.Equal(columns,batch[index].Keys);var marks=new List<string>();for(var field=0;field<columns.Length;field++){var name="p"+index+"_"+field;marks.Add("@"+name);var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=batch[index][columns[field]]??DBNull.Value;command.Parameters.Add(parameter);}values.Add("("+string.Join(',',marks)+")");}command.CommandText="INSERT INTO "+session.Provider.QuoteIdentifier(table)+"("+string.Join(',',columns.Select(session.Provider.QuoteIdentifier))+") VALUES "+string.Join(',',values);await command.ExecuteNonQueryAsync();}
    }
    /// <summary>只构造测试内固定字段。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>();for(var index=0;index<values.Length;index+=2)result.Add((string)values[index]!,values[index+1]);return result;}
    /// <summary>任何错误响应都不能被当作数据。</summary>
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.GetRawText());return response.GetProperty("data");}
}
