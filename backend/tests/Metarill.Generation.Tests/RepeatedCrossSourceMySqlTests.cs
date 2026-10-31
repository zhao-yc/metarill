using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实生成HTTP按固定层角色处理重复来源，表名或相同ID不代表同一显示角色。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RepeatedCrossSourceMySqlTests
{
    private const long Project=9007199254740993,First=9007199254745001;
    /// <summary>固定角色图3及physical名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_3_physical()=>Run(3,"physical");
    /// <summary>固定角色图3及derived名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_3_derived()=>Run(3,"derived");
    /// <summary>固定角色图3及mixed名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_3_mixed()=>Run(3,"mixed");
    /// <summary>固定角色图5及physical名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_5_physical()=>Run(5,"physical");
    /// <summary>固定角色图5及derived名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_5_derived()=>Run(5,"derived");
    /// <summary>固定角色图5及mixed名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_5_mixed()=>Run(5,"mixed");
    /// <summary>固定角色图6及physical名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_6_physical()=>Run(6,"physical");
    /// <summary>固定角色图6及derived名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_6_derived()=>Run(6,"derived");
    /// <summary>固定角色图6及mixed名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_6_mixed()=>Run(6,"mixed");
    /// <summary>固定角色图7及physical名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_7_physical()=>Run(7,"physical");
    /// <summary>固定角色图7及derived名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_7_derived()=>Run(7,"derived");
    /// <summary>固定角色图7及mixed名称，四种叶子／父键分别运行自有样本。</summary>
    [StandaloneExportMySqlFact] public Task Roles_7_mixed()=>Run(7,"mixed");
    /// <summary>只允许Server-200隔离业务库，实例pin核对后才创建本轮元数据表。</summary>
    private static async Task Run(int mask,string mode)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var o=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(o.ConnectionString);if(o.Provider!="mysql"||o.ExpectedDatabase!="metarill_business_test"||cs.Database!=o.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("重复来源实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(o.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==o.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");}foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"})await Exercise(o,mask,mode,type,parent);
    }
    /// <summary>每个来源节点分别更新、持锁及回滚；真实父字段与稀疏槽位独立，清理前核对结构及全量摘要。</summary>
    private static async Task Exercise(DatabaseOptions options,int mask,string mode,string type,string parentType)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_rrcs_"+nonce[..12];var roles=RepeatedCrossSourceTests.Roles(mask);var derived=mode!="physical";bool Joined(int index)=>roles.Contains(index)&&derived&&(mode!="mixed"||index!=roles[0]);var catalog=RepeatedCrossSourceTests.Catalog(prefix,mask,type,parentType,mode);var original=new[]{"country","area","place"};var source=prefix+"_source";var company=prefix+"_company";var targets=original.Select((name,index)=>roles.Contains(index)?source:prefix+"_"+name).ToArray();string ParentField(int index)=>prefix+"_"+original[index-1]+"_id";
        if(parentType=="string")foreach(var column in catalog.ConfigColumns.Where(x=>x.ConfigTableId=="2"&&roles.Where(i=>i>0).Select(ParentField).Contains(x.ColumnName)))column.ColumnLength="16";
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/repeated-cross-source/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-repeat-source-http");var directory=Path.Combine(boundary,nonce);var names=catalog.ConfigTables.Select(x=>x.TableName).Order(StringComparer.Ordinal).ToArray();var business=new Dictionary<long,string>();var companyNames=new Dictionary<long,string>();var foreign=new Dictionary<long,long>();var parents=new Dictionary<(long Node,int Role),long>();var observer=new Observer();var snapshot=new Snapshot(options,company);using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,catalog);WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,mask,mode,type,parentType,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var ddl=await schema.PreviewAsync(catalog);Assert.Equal(4-roles.Length+(derived?1:0),ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);var fields=(await schema.InspectAsync(catalog)).Tables.Single(x=>x.Name==source).Columns;Assert.DoesNotContain(fields,x=>x.Name is "country_name" or "area_name" or "place_name" or "display_label");
            object Key(long value)=>parentType=="string"?value.ToString(CultureInfo.InvariantCulture):value;
            async Task Seed(int role,long id,string label,long? parent=null)
            {
                var text=roles.Contains(role)&&derived?"业务"+label:label;var row=D("id",id,"name",text,"yz_project_id",Project,"status",1);business[id]=text;if(role>0){row[ParentField(role)]=Key(parent!.Value);parents[(id,role)]=parent.Value;}if(roles.Contains(role)){row["code"]="e";row["amount"]=1m;if(derived){var owner=Company(id);await store.InsertAsync(company,D("id",owner,"name",label,"yz_project_id",Project));row["company_id"]=owner;foreign[id]=owner;companyNames[owner]=label;}}await store.InsertAsync(targets[role],row);
            }
            for(var group=0;group<2;group++)for(var role=0;role<3;role++)await Seed(role,First+group*10+role,(group==0?"甲":"乙")+new[]{"根","区","城"}[role],role==0?null:First+group*10+role-1);
            if(derived)foreach(var role in roles)foreach(var offset in new[]{50,51}){var id=Company(First+offset+role*2);var name="派生"+role+(offset==50?"新名":"次名");await store.InsertAsync(company,D("id",id,"name",name,"yz_project_id",Project));companyNames[id]=name;}
            if(parentType=="string")foreach(var role in roles.Where(x=>x>0)){var id=LongParent(role);await Seed(role-1,id,"长"+role+"父",role==1?null:First+role-2);}
            long[] PathIds(){var ids=new long[3];var current=First+2;for(var role=2;role>=0;role--){ids[role]=current;if(role>0)current=parents[(current,role)];}return ids;}
            string NameAt(int role,long id)=>Joined(role)?companyNames[foreign[id]]:business[id];
            string Expected()=>string.Join("/",PathIds().Select((id,role)=>NameAt(role,id)));
            var model=GenerationModel.Create(catalog,"Example.RepeatedSourceProbe");var table=model.Tables.Single(x=>x.Source.Id=="2");new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var ownField=table.Columns.Single(x=>x.Source.ColumnName==original[roles[^1]]+"_id");var hooks=new StringBuilder().AppendLine("using System.Globalization;using Metarill.Core.Security;").AppendLine("namespace Example.RepeatedSourceProbe.Generated.Services;").AppendLine("public sealed partial class "+table.Name+"Service {").AppendLine("    // 测试受信钩子注入新自身或过长父键，外部请求不能选择私有标志。").AppendLine("    partial void BeforeSave(Example.RepeatedSourceProbe.Generated.Entities."+table.Name+" entity,RuntimeActor actor,bool creating,ISet<string> submitted) {");hooks.AppendLine("        if(creating&&entity.Name==\"新自身\"){entity."+ownField.Property+"="+(ownField.Type=="string?"?"entity.Id.ToString(CultureInfo.InvariantCulture)":"entity.Id")+";submitted.Add("+JsonSerializer.Serialize(ownField.Source.ColumnName)+");}");if(parentType=="string")foreach(var role in roles.Where(x=>x>0)){var parent=table.Columns.Single(x=>x.Source.ColumnName==ParentField(role));hooks.AppendLine("        if(!creating&&entity.Name=="+JsonSerializer.Serialize("截断"+role+"父键")+"){entity."+parent.Property+"="+JsonSerializer.Serialize(LongParent(role).ToString(CultureInfo.InvariantCulture))+";submitted.Add("+JsonSerializer.Serialize(parent.Source.ColumnName)+");}");}hooks.AppendLine("    }").AppendLine("}");await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),hooks.ToString());var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(979));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<IRuntimeAuthorization>(p=>new Auth(p.GetRequiredService<IHttpContextAccessor>()));assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(ex.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> PostRole(int role,string action,object body){var page=roles.Contains(role)?"201":(role+3).ToString(CultureInfo.InvariantCulture)+"01";return JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync("/api/admin/auto/"+targets[role]+"/"+page+"/"+action,body)).Content.ReadAsStringAsync());}
            Task<JsonElement> Post(string action,object body)=>PostRole(roles[0],action,body);
            async Task<string> State(){using var other=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(other,catalog).ReadIncludingDeletedAsync(source)).OrderBy(x=>SqlRowStore.Id(x)));}
            var leaf=(First+2).ToString(CultureInfo.InvariantCulture);var added=Data(await Post("add",new{name="新来源",placeId=leaf}));var addedId=added.GetProperty("id").GetString()!;Assert.Equal(Expected(),added.GetProperty("placeIdDisplayText").GetString());
            foreach(var role in roles)
            {
                var ownId=First+role;var own=ownId.ToString(CultureInfo.InvariantCulture);var changed=D("id",own,"name","显式"+role+"新名","placeId",leaf);if(derived)changed["companyId"]=Company(First+50+role*2).ToString(CultureInfo.InvariantCulture);var result=Data(await Post("update",changed));business[ownId]="显式"+role+"新名";if(derived)foreign[ownId]=Company(First+50+role*2);Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());
                result=Data(await Post("update",new{id=own,name="省略"+role+"改名"}));business[ownId]="省略"+role+"改名";Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());
                if(derived){result=Data(await Post("update",new{id=own,companyId=Company(First+51+role*2).ToString(CultureInfo.InvariantCulture)}));foreign[ownId]=Company(First+51+role*2);Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());if(Joined(role)){snapshot.Id=foreign[ownId];snapshot.Name="RR"+role+"源名";snapshot.Change=true;result=Data(await Post("update",new{id=own,placeId=leaf}));companyNames[foreign[ownId]]=snapshot.Name;Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());}}
                foreach(var bad in new object[]{new{id=own,status=0},new{id=own,amount=0.014m},new{id=own,creating=true},new{name="新自身"}}){var before=await State();var notifications=observer.Count;Assert.Equal(500,(await Post(bad.GetType().GetProperty("id") is null?"add":"update",bad)).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(notifications,observer.Count);}
                Assert.Equal(0.02m,Data(await Post("update",new{id=own,code="é",amount=0.015m})).GetProperty("amount").GetDecimal());
                if(role>0)
                {
                    var parent=table.Columns.Single(x=>x.Source.ColumnName==ParentField(role));result=Data(await Post("update",D("id",own,parent.JsonName,(First+10+role-1).ToString(CultureInfo.InvariantCulture),"placeId",leaf)));parents[(ownId,role)]=First+10+role-1;Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());result=Data(await Post("update",D("id",own,parent.JsonName,(First+role-1).ToString(CultureInfo.InvariantCulture))));parents[(ownId,role)]=First+role-1;Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());var before=await State();Assert.Equal(500,(await Post("update",D("id",own,parent.JsonName,"8999999999999999"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());
                    if(parentType=="string"){before=await State();var notifications=observer.Count;snapshot.NonStrict=true;try{var rejected=await Post("update",new{id=own,name="截断"+role+"父键"});Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Contains("结构外键在存储后",rejected.GetProperty("msg").GetString());Assert.Equal(before,await State());Assert.Equal(notifications,observer.Count);}finally{snapshot.NonStrict=false;}}
                }
                var preObserver=await State();Assert.Equal(500,(await Post("update",new{id=own,name="通知故障",placeId=leaf})).GetProperty("code").GetInt32());Assert.Equal(preObserver,await State());
                var marker="锁"+role+"持有";observer.Arm(marker);var hold=D("id",own,"name",marker);if(Joined(role))hold["companyId"]=foreign[ownId].ToString(CultureInfo.InvariantCulture);var pending=Post("update",hold);
                try
                {
                    await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));var lockedRole=Joined(role)?role:Enumerable.Range(0,3).First(x=>x!=role);var node=PathIds()[lockedRole];var nameTable=Joined(lockedRole)?company:targets[lockedRole];var key=Joined(lockedRole)?foreign[node]:node;await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var setup=independent.CreateCommand()){setup.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setup.ExecuteNonQueryAsync();}await using var tx=await independent.BeginTransactionAsync();await using var cmd=independent.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE `"+nameTable+"` SET name=name WHERE id=@id";cmd.Parameters.AddWithValue("id",key);Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>cmd.ExecuteNonQueryAsync())).Number);await tx.RollbackAsync();
                }
                finally{observer.Release.TrySetResult();await pending;observer.Disarm();}
                result=Data(await pending);business[ownId]=marker;Assert.Equal(Expected(),result.GetProperty("placeIdDisplayText").GetString());client.DefaultRequestHeaders.Add("denyTargets","1");var denied=await State();Assert.Equal(500,(await Post("update",new{id=own,placeId=leaf})).GetProperty("code").GetInt32());Assert.Equal(denied,await State());client.DefaultRequestHeaders.Remove("denyTargets");
            }
            // 原组件可只选根／中层；局部提交按字段存在性恢复旧路径，再编码最深选择。
            async Task StoredChoice(long id,int level,long selected)
            {
                using var other=new DatabaseSession(options);var raw=(await new SqlRowStore(other,catalog).ReadIncludingDeletedAsync(source)).Single(x=>SqlRowStore.Id(x)==id);for(var index=0;index<3;index++){var value=raw[original[index]+"_id"];if(index==level)Assert.Equal(selected.ToString(CultureInfo.InvariantCulture),Convert.ToString(value,CultureInfo.InvariantCulture));else Assert.Null(value);}
            }
            foreach(var role in roles)
            {
                var own=First+role;var text=own.ToString(CultureInfo.InvariantCulture);var path=PathIds();var value=Data(await Post("update",new{id=text,countryId=path[0].ToString(CultureInfo.InvariantCulture),areaId=(string?)null,placeId=(string?)null}));Assert.Equal(NameAt(0,path[0]),value.GetProperty("placeIdDisplayText").GetString());await StoredChoice(own,0,path[0]);value=Data(await Post("update",new{id=text,areaId=path[1].ToString(CultureInfo.InvariantCulture)}));Assert.Equal(string.Join("/",path.Take(2).Select((id,index)=>NameAt(index,id))),value.GetProperty("placeIdDisplayText").GetString());await StoredChoice(own,1,path[1]);value=Data(await Post("update",new{id=text,placeId=leaf}));Assert.Equal(Expected(),value.GetProperty("placeIdDisplayText").GetString());await StoredChoice(own,2,First+2);
            }
            var copied=Data(await Post("copy/"+addedId,new{}));Assert.NotEqual(addedId,copied.GetProperty("id").GetString());Assert.Equal(Expected(),copied.GetProperty("placeIdDisplayText").GetString());
            async Task PageMatchesCurrentRoot()
            {
                var page=Data(await Post("page",new{pageNo=2,pageSize=1,filters=new{placeId=new{countryId=PathIds()[0].ToString(CultureInfo.InvariantCulture),countryName="忽略客户端名称"}}}));Assert.Equal((long)roles.Length+2,Convert.ToInt64(page.GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Single(page.GetProperty("records").EnumerateArray());Assert.Equal(Expected(),page.GetProperty("records")[0].GetProperty("placeIdDisplayText").GetString());var empty=Data(await Post("page",new{pageNo=99,pageSize=1,filters=new{placeId=new{countryId=PathIds()[0].ToString(CultureInfo.InvariantCulture)}}}));Assert.Equal((long)roles.Length+2,Convert.ToInt64(empty.GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Empty(empty.GetProperty("records").EnumerateArray());
            }
            await PageMatchesCurrentRoot();
            if(roles.Contains(0)&&roles.Contains(1))
            {
                // 根／中使用同一行：中层父可为已有自身，根角色仍不读取父列。
                var node=First+1;var parent=table.Columns.Single(x=>x.Source.ColumnName==ParentField(1));Data(await Post("update",D("id",node.ToString(CultureInfo.InvariantCulture),parent.JsonName,node.ToString(CultureInfo.InvariantCulture))));parents[(node,1)]=node;var change=D("id",node.ToString(CultureInfo.InvariantCulture),"name","双角色中业务");if(derived)change["companyId"]=Company(First+52).ToString(CultureInfo.InvariantCulture);var value=Data(await Post("update",change));business[node]="双角色中业务";if(derived)foreign[node]=Company(First+52);Assert.Equal(Expected(),value.GetProperty("placeIdDisplayText").GetString());await PageMatchesCurrentRoot();Data(await Post("update",D("id",node.ToString(CultureInfo.InvariantCulture),parent.JsonName,First.ToString(CultureInfo.InvariantCulture))));parents[(node,1)]=First;
            }
            if(roles.Contains(0)&&roles.Contains(2))
            {
                // 同一实体可在固定根／末角色出现，两个角色的名称字段及父含义仍独立。
                var midId=First+1;var midModel=model.Tables.Single(x=>x.Source.TableName==targets[1]);var parent=midModel.Columns.Single(x=>x.Source.ColumnName==ParentField(1));Data(await PostRole(1,"update",D("id",midId.ToString(CultureInfo.InvariantCulture),parent.JsonName,leaf)));parents[(midId,1)]=First+2;Assert.Equal(First+2,PathIds()[0]);var body=D("id",leaf,"name","双角色业务");if(derived)body["companyId"]=Company(First+54).ToString(CultureInfo.InvariantCulture);var response=Data(await Post("update",body));business[First+2]="双角色业务";if(derived)foreign[First+2]=Company(First+54);Assert.Equal(Expected(),response.GetProperty("placeIdDisplayText").GetString());await PageMatchesCurrentRoot();Data(await PostRole(1,"update",D("id",midId.ToString(CultureInfo.InvariantCulture),parent.JsonName,First.ToString(CultureInfo.InvariantCulture))));parents[(midId,1)]=First;
            }
            if(roles.Contains(1)&&roles.Contains(2))
            {
                // 中／末及全三角色允许同一既有实体，名称和父语义仍按角色解释，固定层数不作自树递归。
                var node=First+2;var country=table.Columns.Single(x=>x.Source.ColumnName==ParentField(1));var area=table.Columns.Single(x=>x.Source.ColumnName==ParentField(2));
                foreach(var allSame in mask==7?new[]{false,true}:new[]{false})
                {
                    var rootId=allSame?node:First;Data(await Post("update",D("id",leaf,area.JsonName,leaf,country.JsonName,rootId.ToString(CultureInfo.InvariantCulture))));parents[(node,2)]=node;parents[(node,1)]=rootId;var label=allSame?"三角色业务":"双角色末业务";var change=D("id",leaf,"name",label);if(derived)change["companyId"]=Company(First+54).ToString(CultureInfo.InvariantCulture);var value=Data(await Post("update",change));business[node]=label;if(derived)foreign[node]=Company(First+54);Assert.Equal(Expected(),value.GetProperty("placeIdDisplayText").GetString());await PageMatchesCurrentRoot();Data(await Post("update",D("id",leaf,area.JsonName,(First+1).ToString(CultureInfo.InvariantCulture),country.JsonName,null)));parents[(node,2)]=First+1;parents.Remove((node,1));
                }
            }
            client.DefaultRequestHeaders.Add("denyTargets","1");foreach(var role in roles)Assert.Equal("",Data(await Post("update",new{id=(First+role).ToString(CultureInfo.InvariantCulture),countryId=(string?)null,areaId=(string?)null,placeId=(string?)null})).GetProperty("placeIdDisplayText").GetString());client.DefaultRequestHeaders.Remove("denyTargets");using(var independent=new DatabaseSession(options))foreach(var row in await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source))Assert.True(original.Count(key=>row.TryGetValue(key+"_id",out var value)&&value is not null&&Convert.ToString(value,CultureInfo.InvariantCulture)!="")<=1,"只允许一个稀疏ID位。");
            passed=true;await Receipt("verified",new{nonce,mask,mode,type,parentType,passed,realGeneratedHttp=true,realMySql=true,allSourceNodesUpdated=true,roleNamesIndependent=true,sameEntityAtRootAndLeaf=roles.Contains(0)&&roles.Contains(2),sameEntityAtRootAndMiddle=roles.Contains(0)&&roles.Contains(1),sameEntityAtMiddleAndLeaf=roles.Contains(1)&&roles.Contains(2),sameEntityAtAllRoles=mask==7,partialSelection=true,sparseStorage=true,currentLocks=true,rollback=true,formalWeb=false,browser=false});
        }
        catch(Exception ex){await Receipt("failure",new{nonce,mask,mode,type,parentType,error=ex.GetType().Name,ex.Message,ex.StackTrace});throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var state=await new SchemaService(cleanup).InspectAsync(catalog);if(state.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(catalog)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,catalog).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(state),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(state),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(catalog)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,catalog).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var c=cleanup.Provider.CreateConnection(options);await c.OpenAsync();foreach(var name in names){await using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var value=cmd.CreateParameter();value.ParameterName="name";value.Value=name;cmd.Parameters.Add(value);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value)+"\n");
    }
    /// <summary>过长父键指向已存在真实节点，截断后另一条有效路径也必须被存储复核拒绝。</summary>
    private static long LongParent(int role)=>long.Parse((First+role-1).ToString(CultureInfo.InvariantCulture)+"1",CultureInfo.InvariantCulture);
    /// <summary>来源FK和公司主键分开编码。</summary>
    private static long Company(long id)=>checked(id+100000L);
    /// <summary>只建立本次事务旧快照；独立改名只修改本nonce公司行。</summary>
    private sealed class Snapshot(DatabaseOptions options,string table):IRecordMutationGuard
    {
        internal bool Change,NonStrict;internal long Id;internal string Name="";
        public async Task BeforeMutationAsync(RecordMutationPreparation p,CancellationToken ct=default){if(!Change||p.Operation!=RecordMutationOperation.Update)return;Change=false;await using(var read=p.Transaction.Connection!.CreateCommand()){read.Transaction=p.Transaction;read.CommandText="SELECT name FROM `"+table+"` WHERE id=@id";var key=read.CreateParameter();key.ParameterName="id";key.Value=Id;read.Parameters.Add(key);await read.ExecuteScalarAsync(ct);}await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE `"+table+"` SET name=@name WHERE id=@id";cmd.Parameters.AddWithValue("id",Id);cmd.Parameters.AddWithValue("name",Name);Assert.Equal(1,await cmd.ExecuteNonQueryAsync(ct));}
        /// <summary>非严格模式限本次事务连接，释放后由驱动重置，不改服务器全局。</summary>
        public async Task BeforeWriteAsync(RecordMutationValidation p,CancellationToken ct=default){if(!NonStrict)return;await using var cmd=p.Transaction.Connection!.CreateCommand();cmd.Transaction=p.Transaction;cmd.CommandText="SET SESSION sql_mode=''";await cmd.ExecuteNonQueryAsync(ct);}
    }
    /// <summary>每个节点的通知暂停单独武装，不能重用上一次已完成的信号冒充当前锁。</summary>
    private sealed class Observer:IRecordMutationObserver
    {
        internal int Count;internal string? HoldName;internal TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm(string name){HoldName=name;Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);Release=new(TaskCreationOptions.RunContinuationsAsynchronously);}
        internal void Disarm()=>HoldName=null;
        public async Task BeforeCommitAsync(RecordMutationContext p,CancellationToken ct=default){Interlocked.Increment(ref Count);if(!p.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"通知故障"))throw new InvalidOperationException("测试通知故障");if(HoldName is not null&&Equals(name,HoldName)){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),ct);}}
    }
    /// <summary>同表全部角色可用同一页，头仅撤销list，不能改变真实身份／项目及update权限。</summary>
    private sealed class Auth(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(979,"重复来源实库",false,Project.ToString(CultureInfo.InvariantCulture),"5");
        public void RequirePagePermission(string page,string operation){if(operation=="list"&&accessor.HttpContext!.Request.Headers.ContainsKey("denyTargets"))throw new UnauthorizedAccessException("没有目标查询权限。");}
    }
    /// <summary>只有原R成功响应才可读取数据，不用空值隐藏HTTP异常。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.TryGetProperty("code",out var code),"响应不是原R合同。");Assert.Equal(200,code.GetInt32());return value.GetProperty("data");}
    /// <summary>字段来自固定模型，所有SQL业务值参数化。</summary>
    private static Dictionary<string,object?> D(params object?[] pairs){var d=new Dictionary<string,object?>();for(var i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i]!,pairs[i+1]);return d;}
    /// <summary>仅对已核对结构及完整行摘要的本次自有表执行清理。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=sql;await cmd.ExecuteNonQueryAsync();}
}
