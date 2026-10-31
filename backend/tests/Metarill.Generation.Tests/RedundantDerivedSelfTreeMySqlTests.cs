using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Data.Relations;
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

/// <summary>真实生成HTTP及原维护接口验证派生自树／同目标多选，不将固定普通身份计为正式宿主验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RedundantDerivedSelfTreeMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    /// <summary>显式叶子缓存的四种来源／父键。</summary>
    [StandaloneExportMySqlFact]public Task Explicit_cache()=>Run("explicit");
    /// <summary>隐式辅助列由同一存储源头产生。</summary>
    [StandaloneExportMySqlFact]public Task Automatic_cache()=>Run("automatic");
    /// <summary>自定义逻辑caption仍维护原_name合同。</summary>
    [StandaloneExportMySqlFact]public Task Custom_caption()=>Run("caption");
    /// <summary>两层R0联接改变中间关联时仍传播完整闭包。</summary>
    [StandaloneExportMySqlFact]public Task Nested_derived_name()=>Run("nested");
    /// <summary>双树只刷新真正受影响的来源及缓存。</summary>
    [StandaloneExportMySqlFact]public Task Two_derived_trees()=>Run("double");
    /// <summary>同一派生目标的自树、多选与跨表同时参与原事务。</summary>
    [StandaloneExportMySqlFact]public Task Shared_target_multi_and_cross()=>Run("mixed");
    /// <summary>普通共享跨表五个父页保留独立派生自树。</summary>
    [StandaloneExportMySqlFact]public Task Shared_parent_pages()=>Run("shared");
    /// <summary>共享全局跨表不改变普通派生自树的项目范围。</summary>
    [StandaloneExportMySqlFact]public Task Global_shared_parent_pages()=>Run("globalShared");
    /// <summary>每个模型核对域名、库及实例，凭据只从私有配置读取。</summary>
    private static async Task Run(string shape)
    {
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=json.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("派生自树专项仅允许Server-200既定隔离业务库。");
        foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"}){await Pin(options);await Exercise(options,type,parent,shape);}
    }
    /// <summary>全部结构来自本轮目录；生成Controller、混合队列和原维护Business均为产品实现。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parent,string shape)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_rds_"+nonce[..12];var c=RedundantDerivedSelfTreeTests.Catalog(prefix,type,parent,shape);var source=prefix+"_source";var model=GenerationModel.Create(c,"Example.R1DerivedSelfProbe");var sourceModel=model.Tables.Single(t=>t.Source.Id=="2");var initialSelf=CascadeTreeWriteCompilation.ForService(model,sourceModel,sourceModel.Pages.Single(p=>p.Source.Id=="201")).OrderBy(x=>x.Read.Source.Source.Id,StringComparer.Ordinal).First();var cacheName=initialSelf.Name.Source.ColumnName;
        c.ConfigTables.Add(new(){Id="95",YzProjectId=c.YzProjectId,TableName=prefix+"_audit",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="95101",YzProjectId=c.YzProjectId,ConfigTableId="95",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="95102",YzProjectId=c.YzProjectId,ConfigTableId="95",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="95103",YzProjectId=c.YzProjectId,ConfigTableId="95",ColumnName="source_tree_name",LogicalType="string",ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("source."+cacheName)}},new(){Id="95104",YzProjectId=c.YzProjectId,ConfigTableId="95",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="95105",YzProjectId=c.YzProjectId,ConfigTableId="95",ColumnName="delete_no",LogicalType="datetime"}]);c.ConfigPages.Add(new(){Id="9501",YzProjectId=c.YzProjectId,YzAdminTypeId="5",ConfigTableId="95",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.AddRange([new(){Id="950195102",YzProjectId=c.YzProjectId,ConfigPageId="9501",ConfigColumnId="95102"},new(){Id="950195103",YzProjectId=c.YzProjectId,ConfigPageId="9501",ConfigColumnId="95103"}]);c.ConfigButtons.AddRange([JsonSerializer.SerializeToElement(new{id="91001",sql="/admin/sys/system_maintenance/tables"}),JsonSerializer.SerializeToElement(new{id="91002",sql="/admin/sys/system_maintenance/backfill_join_columns"})]);
        var terminalId=shape=="nested"?"5":shape is "mixed" or "shared" or "globalShared"?"90":"4";var terminalColumn=shape=="nested"?"label":"name";c.ConfigColumns.Single(x=>x.ConfigTableId==terminalId&&x.ColumnName==terminalColumn).ColumnLength="200";
        model=GenerationModel.Create(c,model.Namespace);sourceModel=model.Tables.Single(t=>t.Source.Id=="2");var page=sourceModel.Pages.Single(p=>p.Source.Id=="201");var self=CascadeTreeWriteCompilation.ForService(model,sourceModel,page).OrderBy(x=>x.Read.Source.Source.Id,StringComparer.Ordinal).First();var tree=self.Read.Target;var field=self.Read.Source;var cache=self.Name;var companyField=tree.Columns.Single(x=>x.Source.ColumnName=="company_id");var company=model.Tables.Single(t=>t.Source.Id==companyField.Source.LinkConfigTableId);var terminal=model.Tables.Single(t=>t.Source.Id==terminalId);var terminalPage=Assert.Single(terminal.Pages);var value=terminal.Columns.Single(x=>x.Source.ColumnName==terminalColumn);var mixed=shape=="mixed";var cross=CrossTableWriteCompilation.ForService(model,sourceModel,page);var audit=prefix+"_audit";var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/redundant-derived-self-tree/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-self-http");var directory=Path.Combine(boundary,nonce);using var session=new DatabaseSession(options);var store=new SqlRowStore(session,c);var schema=new SchemaService(session);var auth=new Auth();var observer=new Observer(source,audit,cache.Source.ColumnName,store,mixed);var snapshot=new Snapshot(options,c,terminal.Source.TableName,value.Source.ColumnName);WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,type,parent,shape,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var plan=await schema.PreviewAsync(c);Assert.Equal(names.Length,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);
            // 逻辑name不落树／中间表，真实名称只写已声明的物理终点。
            foreach(var (node,label) in new[]{(0,"根"),(1,"叶"),(2,"另一根"),(3,"改挂"),(4,"嵌套改挂")})
            {
                await store.InsertAsync(terminal.Source.TableName,D("id",Terminal(node),"yz_project_id",Project,value.Source.ColumnName,label));if(shape=="nested")await store.InsertAsync(company.Source.TableName,D("id",Company(node),"yz_project_id",Project,"brand_id",Terminal(node)));
                if(node<3)await store.InsertAsync(tree.Source.TableName,D("id",First+node,"yz_project_id",Project,"status",1,companyField.Source.ColumnName,Company(node),self.Read.Parent.Source.ColumnName,node==1?(self.Read.Parent.Type=="string?"?(object)Id(First):First):null));
            }
            var seeded=new HashSet<string>(StringComparer.Ordinal);long CrossId(string id)=>First+100+long.Parse(id,CultureInfo.InvariantCulture);
            foreach(var branch in cross)for(var index=0;index<branch.Levels.Count;index++){var level=branch.Levels[index].Read;if(!seeded.Add(level.Target.Source.Id))continue;var row=D("id",CrossId(level.Target.Source.Id),level.Name.Source.ColumnName,"跨表"+level.Target.Source.Id);if(level.Target.Columns.Any(x=>x.Source.ColumnName=="yz_project_id"))row["yz_project_id"]=level.Target.IsGlobal?Project+1:Project;if(level.Target.Columns.Any(x=>x.Source.ColumnName=="status"))row["status"]=1;if(level.Parent is{} pk){var up=CrossId(branch.Levels[index-1].Read.Target.Source.Id);row[pk.Source.ColumnName]=pk.Type=="string?"?Id(up):(object)up;}await store.InsertAsync(level.Target.Source.TableName,row);}
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var maintenance=(RelationMaintenanceCatalog)assembly.GetType(model.Namespace+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;Assert.Contains(cacheName,maintenance.MixedSourceColumns[source]);if(mixed)Assert.Contains("choice_names",maintenance.MixedSourceColumns[source]);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(850));builder.Services.AddSingleton<IRuntimeAuthorization>(auth);builder.Services.AddSingleton<ISecurityIdentityAccessor>(auth);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddSingleton(new SystemMaintenanceBusiness(session,maintenance,auth));assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddApplicationPart(typeof(SystemMaintenanceController).Assembly).AddJsonOptions(j=>j.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException or MySqlException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            // 真实生成HTTP和原维护Controller；维护动作不能被固定普通身份绕过按钮。
            async Task<JsonElement> Call(string table,string pageId,string action,object body,string? parentId=null,HttpMethod? method=null){using var request=new HttpRequestMessage(method??HttpMethod.Post,"/api/admin/auto/"+table+"/"+pageId+"/"+action+(parentId is null?"":"?parentId="+parentId)){Content=JsonContent.Create(body)};using var response=await client.SendAsync(request);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<JsonElement> Maintain()=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync("/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="2"})).Content.ReadAsStringAsync());
            async Task<string> State(){var rows=new Dictionary<string,object>();foreach(var table in c.ConfigTables)rows[table.TableName]=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x)).ToArray();return JsonSerializer.Serialize(rows);}
            Dictionary<string,object?> Selection(){var body=D("name","来源",field.JsonName,Id(First+1),cache.JsonName,"伪造缓存");foreach(var branch in cross)body[branch.Levels[^1].Id.JsonName]=Id(CrossId(branch.Levels[^1].Read.Target.Source.Id));if(sourceModel.Columns.Any(x=>x.Source.ColumnName=="owner_country_id"))body["ownerCountryId"]=Id(CrossId("3"));if(mixed){body["choiceIds"]=new[]{Id(First),Id(First+1)};body["choiceNames"]=new[]{"伪造"};}if(shape=="double")body["alternateId"]=Id(First);return body;}
            var added=Data(await Call(source,"201","add",Selection()));var id=added.GetProperty("id").GetString()!;var primary=long.Parse(id,CultureInfo.InvariantCulture);observer.Primary=primary;Assert.Equal("叶",added.GetProperty(cache.JsonName).GetString());Assert.Contains("根/叶",added.GetProperty(field.JsonName+"DisplayText").GetString());var consumers=new List<long>{primary};await store.InsertAsync(audit,D("id",First+800,"yz_project_id",Project,"source_id",primary,"source_tree_name","叶"));
            foreach(var parentPage in sourceModel.Pages.Where(p=>p.Scopes.Any(s=>s.Source=="parent"))){var scope=parentPage.Scopes.First(s=>s.Source=="parent");var level=cross.SelectMany(p=>p.Levels).First(x=>x.Id.Source.Id==scope.Column.Source.Id);var row=Data(await Call(source,parentPage.Source.Id,"add",Selection(),Id(CrossId(level.Read.Target.Source.Id))));consumers.Add(long.Parse(row.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));Assert.Equal("叶",row.GetProperty(cache.JsonName).GetString());}
            var copied=Data(await Call(source,"201","copy/"+id,new{}));var copy=long.Parse(copied.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);consumers.Add(copy);Assert.Equal("叶",copied.GetProperty(cache.JsonName).GetString());
            async Task Names(string? expected)
            {
                foreach(var key in consumers){var row=Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",key)));Assert.Equal(expected,row[cacheName]);Assert.Equal(Id(First+1),SqlRowStore.Text(row,field.Source.ColumnName));if(shape=="double")Assert.Equal("根",row["alternate_name"]);if(mixed){using var array=JsonDocument.Parse(SqlRowStore.Text(row,"choice_names"));var actual=array.RootElement.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.Null?null:v.GetString()).ToArray();Assert.Equal(new string?[]{"根",expected},actual);}}Assert.Equal(expected,Assert.Single(await store.ReadIncludingDeletedAsync(audit,D("id",First+800)))["source_tree_name"]);
            }
            // 原生SQL不发事件；省略FK仍保持缓存，路径为当前值，随后实际目标HTTP改名统一传播。
            await store.UpdateAsync(terminal.Source.TableName,Terminal(1),D(value.Source.ColumnName,"SQL叶"));var omitted=Data(await Call(source,"201","update",D("id",id,"name","省略关联",cache.JsonName,"伪造")));Assert.Equal("叶",omitted.GetProperty(cache.JsonName).GetString());Assert.Contains("SQL叶",omitted.GetProperty(field.JsonName+"DisplayText").GetString());
            var notifications=observer.Count;Data(await Call(terminal.Source.TableName,terminalPage.Source.Id,"update",D("id",Id(Terminal(1)),value.JsonName,"新叶")));Assert.Equal(notifications+1,observer.Count);await Names("新叶");
            Data(await Call(tree.Source.TableName,self.Read.TargetPage.Source.Id,"update",D("id",Id(First+1),companyField.JsonName,Id(Company(3)))));await Names("改挂");Data(await Call(terminal.Source.TableName,terminalPage.Source.Id,"update",D("id",Id(Terminal(1)),value.JsonName,"无关改名")));await Names("改挂");
            if(shape=="nested"){var brand=company.Columns.Single(x=>x.Source.ColumnName=="brand_id");Data(await Call(company.Source.TableName,Assert.Single(company.Pages).Source.Id,"update",D("id",Id(Company(3)),brand.JsonName,Id(Terminal(4)))));await Names("嵌套改挂");}
            Data(await Call(tree.Source.TableName,self.Read.TargetPage.Source.Id,"update",D("id",Id(First+1),companyField.JsonName,Id(Company(1)))));Data(await Call(terminal.Source.TableName,terminalPage.Source.Id,"update",D("id",Id(Terminal(1)),value.JsonName,"新叶")));await Names("新叶");
            snapshot.Enabled=true;var selected=D("id",id,field.JsonName,Id(First+1));if(mixed)selected["choiceIds"]=new[]{Id(First),Id(First+1)};var refreshed=Data(await Call(source,"201","update",selected));Assert.True(snapshot.SawOld&&snapshot.CommittedNew);Assert.Equal("RR暂存叶",refreshed.GetProperty(cache.JsonName).GetString());Assert.Equal("RR暂存叶",Assert.Single(await store.ReadAsync(audit,D("id",First+800)))["source_tree_name"]);foreach(var other in consumers.Where(key=>key!=primary))Assert.Equal("新叶",Assert.Single(await store.ReadAsync(source,D("id",other)))[cacheName]);Data(await Call(terminal.Source.TableName,terminalPage.Source.Id,"update",D("id",Id(Terminal(1)),value.JsonName,"RR新叶")));await Names("RR新叶");
            var before=await State();observer.SawPatched=false;var failed=await Call(terminal.Source.TableName,terminalPage.Source.Id,"update",D("id",Id(Terminal(1)),value.JsonName,"尾部失败"));Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.Contains("派生自树尾部失败",failed.GetProperty("msg").GetString());Assert.True(observer.SawPatched);Assert.Equal(before,await State());
            auth.DenyMaintenance=true;Assert.Equal(500,(await Maintain()).GetProperty("code").GetInt32());auth.DenyMaintenance=false;foreach(var key in consumers){await store.UpdateAsync(source,key,D(cacheName,"待回填"));if(mixed)await store.UpdateAsync(source,key,D("choice_names","[]"));}Assert.True(Data(await Maintain()).GetProperty("affectedRows").GetInt64()>0);await Names("RR新叶");Assert.Equal(0,Data(await Maintain()).GetProperty("affectedRows").GetInt64());
            var wide=(shape=="explicit"&&parent=="int64")||shape=="mixed"&&type=="int64"&&parent=="string";
            if(wide)
            {
                var original=Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",primary)));for(var index=0;index<131;index++){var row=new Dictionary<string,object?>(original){["id"]=First+1000+index,[cacheName]="跨批旧缓存"};await store.InsertAsync(source,row);consumers.Add(First+1000+index);}foreach(var key in consumers)await store.UpdateAsync(source,key,D(cacheName,"跨批旧缓存"));await store.UpdateAsync(terminal.Source.TableName,Terminal(2),D(value.Source.ColumnName,new string('长',90)));await store.UpdateAsync(source,copy,D(field.Source.ColumnName,type=="string"?(object)Id(First+2):First+2));before=await State();var overflow=await Maintain();Assert.Equal(500,overflow.GetProperty("code").GetInt32());Assert.Contains(cacheName,overflow.GetProperty("msg").GetString());Assert.Equal(before,await State());await store.UpdateAsync(terminal.Source.TableName,Terminal(2),D(value.Source.ColumnName,"另一根"));await store.UpdateAsync(source,copy,D(field.Source.ColumnName,type=="string"?(object)Id(First+1):First+1));Data(await Maintain());await Names("RR新叶");
            }
            // 同目标多选仍保留顺序、空数组与null；坏数组整笔拒绝，不能让新派生能力改变原写入合同。
            if(mixed)
            {
                Data(await Call(source,"201","update",D("id",id,"choiceIds",new[]{Id(First+1),Id(First)})));using(var a=JsonDocument.Parse(SqlRowStore.Text(Assert.Single(await store.ReadAsync(source,D("id",primary))),"choice_names")))Assert.Equal(new[]{"RR新叶","根"},a.RootElement.EnumerateArray().Select(x=>x.GetString()).ToArray());
                Data(await Call(source,"201","update",D("id",id,"choiceIds",Array.Empty<string>())));Assert.Equal("[]",SqlRowStore.Text(Assert.Single(await store.ReadAsync(source,D("id",primary))),"choice_names"));
                Data(await Call(source,"201","update",D("id",id,"choiceIds",null)));var empty=Assert.Single(await store.ReadAsync(source,D("id",primary)));Assert.Null(empty["choice_ids"]);Assert.Null(empty["choice_names"]);
                foreach(var bad in new[]{new[]{Id(First+1),Id(First+1)},new[]{Id(First+1)+" "},Enumerable.Repeat(Id(First+1),201).ToArray()}){before=await State();Assert.Equal(500,(await Call(source,"201","update",D("id",id,"choiceIds",bad))).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
                Data(await Call(source,"201","update",D("id",id,"choiceIds",new[]{Id(First),Id(First+1)})));await Names("RR新叶");
                // 缓存投影不能绕过旧预算合同：即使数组不含本次目标，来源历史超额仍使整笔变更及全量维护回滚。
                var unrelated=JsonSerializer.Serialize(Enumerable.Repeat(Id(First+2),201));await store.UpdateAsync(source,copy,D("choice_ids",unrelated,"choice_names","[\"保留旧值\"]"));before=await State();
                var budgetFailure=await Call(terminal.Source.TableName,terminalPage.Source.Id,"update",D("id",Id(Terminal(1)),value.JsonName,"超额应回滚"));Assert.Equal(500,budgetFailure.GetProperty("code").GetInt32());Assert.Contains("预算",budgetFailure.GetProperty("msg").GetString());Assert.Equal(before,await State());
                Assert.Equal(500,(await Maintain()).GetProperty("code").GetInt32());Assert.Equal(before,await State());await store.UpdateAsync(source,copy,D("choice_ids",JsonSerializer.Serialize(new[]{Id(First),Id(First+1)})));Data(await Maintain());await Names("RR新叶");
            }
            // 清空只影响指定自树；同目标数组和另一个自树仍沿原独立字段合同。
            Data(await Call(source,"201","update",D("id",id,field.JsonName,null)));var cleared=Assert.Single(await store.ReadAsync(source,D("id",primary)));Assert.Null(cleared[field.Source.ColumnName]);Assert.Null(cleared[cacheName]);if(mixed){using var array=JsonDocument.Parse(SqlRowStore.Text(cleared,"choice_names"));Assert.Equal(new[]{"根","RR新叶"},array.RootElement.EnumerateArray().Select(v=>v.GetString()).ToArray());}Data(await Call(source,"201","update",D("id",id,field.JsonName,Id(First+1))));await Names("RR新叶");
            Data(await Call(terminal.Source.TableName,terminalPage.Source.Id,"",new{id=Id(Terminal(1))},method:HttpMethod.Delete));await Names(null);passed=true;await Receipt("verified",new{nonce,type,parent,shape,passed,realGeneratedHttp=true,originalMaintenanceHttp=true,derivedSelf=true,sameTargetMulti=mixed,nested=shape=="nested",sourcePages=1+sourceModel.Pages.Count(p=>p.Scopes.Any(s=>s.Source=="parent")),consumers=consumers.Count,rrCurrentRead=true,targetAndDownstreamRollback=true,lateBackfillRollback=wide,repeatBackfillZero=true,observerOnlyTarget=true,formalIdentity=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parent,shape,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){await Pin(options);using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=command.CreateParameter();p.ParameterName="name";p.Value=name;command.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>终点／中间公司的自有固定ID不经过浮点数。</summary>
        long Terminal(int index)=>First+200+index;
        /// <summary>普通公司本身就是终点，嵌套时额外独立R0节点。</summary>
        long Company(int index)=>shape=="nested"?First+300+index:Terminal(index);
        /// <summary>意图、首因及清理只保留在本轮私有目录。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>目标提交前须看到自树、同目标多选和下游均已改写，然后准确制造尾部回滚。</summary>
    private sealed class Observer(string source,string audit,string cache,SqlRowStore store,bool multi):IRecordMutationObserver
    {
        internal long Primary;internal int Count;internal bool SawPatched;
        /// <summary>维护及消费者补丁不得递归进入业务观察器。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default){Count++;if(!c.SubmittedFields.Values.Any(v=>Equals(v,"尾部失败")))return;var row=Assert.Single(await store.ReadAsync(source,D("id",Primary)));SawPatched=Equals(row[cache],"尾部失败")&&Equals(Assert.Single(await store.ReadAsync(audit,D("id",First+800)))["source_tree_name"],"尾部失败");if(multi){using var values=JsonDocument.Parse(SqlRowStore.Text(row,"choice_names"));SawPatched&=values.RootElement.EnumerateArray().Any(v=>v.ValueKind==JsonValueKind.String&&v.GetString()=="尾部失败");}throw new InvalidOperationException("派生自树尾部失败");}
    }
    /// <summary>真实RR读旧终点后由独立连接改名，生成CurrentRows必须读到新值并持锁。</summary>
    private sealed class Snapshot(DatabaseOptions options,MetadataCatalog catalog,string terminal,string value):IRecordMutationGuard
    {
        internal bool Enabled,SawOld,CommittedNew;
        /// <summary>只在指定来源更新运行一次，不改变权限或提交字段。</summary>
        public async Task BeforeMutationAsync(RecordMutationPreparation p,CancellationToken token=default){if(!Enabled)return;Enabled=false;await using(var command=p.Transaction.Connection!.CreateCommand()){command.Transaction=p.Transaction;command.CommandText="SELECT @@transaction_isolation";Assert.Equal("REPEATABLE-READ",Convert.ToString(await command.ExecuteScalarAsync(token),CultureInfo.InvariantCulture));command.CommandText="SELECT `"+value+"` FROM `"+terminal+"` WHERE id=@id";var id=command.CreateParameter();id.ParameterName="id";id.Value=First+201;command.Parameters.Add(id);SawOld=Equals(await command.ExecuteScalarAsync(token),"新叶");}using var independent=new DatabaseSession(options);await new SqlRowStore(independent,catalog).UpdateAsync(terminal,First+201,D(value,"RR暂存叶"));CommittedNew=true;}
        /// <summary>实际目标读取和缓存预算由产品执行。</summary>
        public Task BeforeWriteAsync(RecordMutationValidation p,CancellationToken token=default)=>Task.CompletedTask;
    }
    /// <summary>固定普通Web形状身份，维护按钮可撤销，不代替真实宿主登录。</summary>
    private sealed class Auth:IRuntimeAuthorization,ISecurityIdentityAccessor
    {
        internal bool DenyMaintenance;
        /// <summary>项目和后台来自宿主固定上下文。</summary>
        public RuntimeActor RequireActor()=>new(997,"派生自树专项",false,Id(Project),"5");
        /// <summary>专项提供固定页动作许可，真实身份与撤权另验。</summary>
        public void RequirePagePermission(string page,string operation){}
        /// <summary>原维护Business仍核精确按钮ID和普通身份。</summary>
        public AuthenticatedIdentity RequireIdentity()=>new(RequireActor(),new HashSet<string>(),998,null){PrincipalProjectId=Id(Project),PrincipalAdminTypeId="5",ButtonIds=DenyMaintenance?new HashSet<string>():new HashSet<string>{"91001","91002"}};
    }
    /// <summary>建表与清理前均验证实例身份，不把配置中的域名当作服务器证明。</summary>
    private static async Task Pin(DatabaseOptions options){using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await command.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"派生自树实例pin不符。");}
    /// <summary>仅成功R响应才能继续断言data。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>预置字段保留真实驱动类型，通过参数化存储。</summary>
    private static Dictionary<string,object?> D(params object?[] values)=>Enumerable.Range(0,values.Length/2).ToDictionary(i=>(string)values[i*2]!,i=>values[i*2+1]);
    /// <summary>大整数ID始终按十进制文本传输。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>只清理固定目录证明的本轮独占表。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
}
