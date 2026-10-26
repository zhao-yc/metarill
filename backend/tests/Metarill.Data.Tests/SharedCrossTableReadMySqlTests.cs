using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>共享非冗余读取的真实MySQL证明；不代替生成CRUD、正式身份或浏览器验收。</summary>
public sealed class SharedCrossTableReadMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    /// <summary>四种来源／父键交叉均直接执行SQL。</summary>
    [MySqlIntegrationFact]public async Task String_slots(){foreach(var parent in new[]{"string","int64"})await Exercise("string",parent);}
    [MySqlIntegrationFact]public async Task Integer_slots(){foreach(var parent in new[]{"string","int64"})await Exercise("int64",parent);}
    /// <summary>短控件自己的主叶从另一分支恢复，不能另存一份祖先。</summary>
    [MySqlIntegrationFact]public Task Short_string()=>Exercise("string","int64",true);
    [MySqlIntegrationFact]public Task Short_integer()=>Exercise("int64","string",true);
    /// <summary>独立R0固定派生目标沿现有路径读取真实终点。</summary>
    [MySqlIntegrationFact]public Task Derived_names()=>Exercise("string","int64",false,true);
    /// <summary>核对实例pin后才创建本轮元数据所属的随机样本。</summary>
    private static async Task Exercise(string sourceType,string parentType,bool shorter=false,bool derived=false)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||parsed.Database!=options.ExpectedDatabase||parsed.Server!="deve-server.yinzon.com"||parsed.Port!=3306)throw new InvalidOperationException("跨表读取只允许指定Server-200隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var identity=await command.ExecuteReaderAsync();if(!await identity.ReadAsync()||identity.GetString(0)!=options.ExpectedDatabase||identity.GetString(1)!=pin.RootElement.GetProperty("expectedHostname").GetString()||identity.GetString(2)!=pin.RootElement.GetProperty("expectedServerUuid").GetString())throw new InvalidOperationException("跨表读取实例pin不匹配，未创建样本。");}
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_scs_"+nonce[..12];var c=SharedCrossTableReadTests.Catalog(prefix,sourceType,parentType,shorter,derived);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();var source=prefix+"_source";var roles=new[]{"country","area","place","warehouse"};var evidence=Path.Combine(Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??Directory.GetCurrentDirectory(),".local/shared-cross-sparse/mysql-"+nonce);Directory.CreateDirectory(evidence);using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var reader=new MySqlNonRedundantReader(session);var scopes=SharedCrossTableReadTests.Scopes(c);var plan=MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",SharedCrossTableReadTests.Outputs(shorter),scopes);var group=SharedCrossTableSparseSelection.Compile(c.ConfigTables[0],"31",c.ConfigColumns.Where(x=>x.ConfigTableId=="1"));var created=false;var verified=false;string? fingerprint=null;
        object Key(long id)=>sourceType=="string"?Id(id):id;object Parent(long id)=>parentType=="string"?Id(id):id;long Node(int role,int batch=0)=>First+batch*10+role;long Company(int role,int batch=0)=>First+500+batch*10+role;long Record(int index)=>First+1000+index;
        await Receipt("intent",new{nonce,sourceType,parentType,shorter,derived,names,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(c));Assert.Empty((await schema.PreviewAsync(c)).Changes);Assert.DoesNotContain((await schema.InspectAsync(c)).Tables.Single(t=>t.Name==source).Columns,x=>x.Name.EndsWith("_name",StringComparison.Ordinal));
            for(var batch=0;batch<2;batch++)for(var role=0;role<4;role++)
            {
                var row=D("id",Node(role,batch),"yz_project_id",Project,"status",1);if(derived&&role<3){await store.InsertAsync(prefix+"_company",D("id",Company(role,batch),"name",new[]{"国","区","城"}[role]+batch,"yz_project_id",Project));row["company_id"]=Company(role,batch);}else row["name"]=new[]{"国","区","城","仓"}[role]+batch;if(role>0)row[role==1?"country_id":"area_id"]=Parent(Node(role==1?0:1,batch));await store.InsertAsync(prefix+"_"+roles[role],row);
            }
            for(var i=0;i<6;i++)await Insert(i);await Insert(6,true);await Insert(7,false,true);var before=await Snapshot();var all=await reader.ReadPageAsync(plan,[],[],1,20);Assert.Equal(6,all.Total);Assert.Equal(6,all.Records.Count);Assert.All(all.Records,row=>Assert.Equal(SharedCrossTableReadTests.Outputs(shorter).Order(StringComparer.Ordinal),row.Keys.Order(StringComparer.Ordinal)));
            var both=all.Records.Single(r=>Convert.ToInt64(r["id"],CultureInfo.InvariantCulture)==Record(3));Assert.Equal("国0",both["country_name"]);Assert.Equal("区0",both["area_name"]);Assert.Equal("仓0",both["warehouse_name"]);if(!shorter)Assert.Equal("城0",both["place_name"]);var warehouseOnly=all.Records.Single(r=>Convert.ToInt64(r["id"],CultureInfo.InvariantCulture)==Record(4));Assert.Equal(Id(Node(0,1)),Text(warehouseOnly,"country_id"));Assert.Equal("国1",warehouseOnly["country_name"]);Assert.Equal("区1",warehouseOnly["area_name"]);if(!shorter)Assert.Null(warehouseOnly["place_id"]);var empty=all.Records.Single(r=>Convert.ToInt64(r["id"],CultureInfo.InvariantCulture)==Record(0));foreach(var field in group.Slots.SelectMany(s=>new[]{s.ColumnName,s.ColumnName[..^3]+"_name"}))Assert.Null(empty[field]);
            var filters=new[]{new FixedReadFilter("country_id",FixedReadComparison.Equal,Key(Node(0)))};var first=await reader.ReadPageAsync(plan,filters,[new("id")],1,2);var second=await reader.ReadPageAsync(plan,filters,[new("id")],2,2);Assert.Equal(4,first.Total);Assert.Equal(new[]{Record(1),Record(2)},first.Records.Select(r=>Convert.ToInt64(r["id"],CultureInfo.InvariantCulture)));Assert.Equal(new[]{Record(3),Record(5)},second.Records.Select(r=>Convert.ToInt64(r["id"],CultureInfo.InvariantCulture)));Assert.Equal(3,(await reader.ReadPageAsync(plan,[new("area_id",FixedReadComparison.Equal,Key(Node(1)))],[],1,20)).Total);var stats=await reader.ReadStatisticsAsync(plan,filters,["amount"]);Assert.Equal(4,stats.Total);Assert.Equal(110m,stats.Sums["amount"]);Assert.Equal(4,(await reader.ReadRowsAsync(plan,filters,[],10)).Count);
            var neutral=NonRedundantReads.CompileWithCrossTables(c,"1",["id","country_id"],scopes);var parentQuery=NonRedundantCrossTableScopeSql.Apply(session.Select<ScopeRow>().AsTable((_,_)=>source).Where(r=>r.YzProjectId==Project&&r.DeleteNo==null),session,neutral,"country_id","shared_parent",Key(Node(0)),Id(Project));Assert.Equal(new[]{Record(1),Record(2),Record(3),Record(5)},await parentQuery.OrderBy(r=>r.Id).ToListAsync(r=>r.Id));
            // 坏历史存储分别覆盖物理冗余与实际路径冲突；COUNT、无COUNT、当前读和统计全部拒绝。
            foreach(var kind in shorter?new[]{"ancestor"}:new[]{"ancestor","conflict"})
            {
                var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>
                {
                    var patch=kind=="ancestor"?D("area_id",Key(Node(1))):D("warehouse_id",Key(Node(3,1)));Assert.Equal(1,await store.UpdateAsync(source,Record(3),patch));var one=new[]{new FixedReadFilter("id",FixedReadComparison.Equal,Record(3))};await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadPageAsync(plan,one,[],1,10));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadRowsAsync(plan,one,[],10));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadCurrentRowsAsync(plan,one,[],10));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadStatisticsAsync(plan,one,["amount"]));throw new InvalidOperationException("回滚共享坏值");
                }));Assert.Equal("回滚共享坏值",error.Message);Assert.Equal(before,await Snapshot());
            }
            // 短控件的地区是公共祖先，只有仓库一个独立末端；切换仓库应合法恢复新地区而非虚构冲突。
            if(shorter){var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.Equal(1,await store.UpdateAsync(source,Record(3),D("warehouse_id",Key(Node(3,1)))));var row=Assert.Single((await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.Equal,Record(3))],[],1,1)).Records);Assert.Equal("国1",row["country_name"]);Assert.Equal("区1",row["area_name"]);Assert.Equal(Id(Node(1,1)),Text(row,"area_id"));throw new InvalidOperationException("回滚短共享重选");}));Assert.Equal("回滚短共享重选",error.Message);Assert.Equal(before,await Snapshot());}
            // 已建立旧RR快照后，明确当前读仍取得外部提交的新终点名称；来源DML与验证失败整笔回滚。
            await using(var outside=new MySqlConnection(options.ConnectionString))
            {
                await outside.OpenAsync();async Task Rename(string text){await using var cmd=outside.CreateCommand();cmd.CommandText="UPDATE "+session.Provider.QuoteIdentifier(derived?prefix+"_company":prefix+"_country")+" SET name=@name WHERE id=@id";cmd.Parameters.AddWithValue("name",text);cmd.Parameters.AddWithValue("id",derived?Company(0):Node(0));Assert.Equal(1,await cmd.ExecuteNonQueryAsync());}
                try{var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{var one=new[]{new FixedReadFilter("id",FixedReadComparison.Equal,Record(3))};Assert.Equal("国0",Assert.Single((await reader.ReadPageAsync(plan,one,[],1,1)).Records)["country_name"]);await Rename("当前共享名称");Assert.Equal("当前共享名称",Assert.Single(await reader.ReadCurrentRowsAsync(plan,one,[],1))["country_name"]);var lockedId=Record(3);var locked=NonRedundantCrossTableScopeSql.Apply(session.Select<ScopeRow>().AsTable((_,_)=>source),session,neutral,"country_id","shared_locked",Key(Node(0)),Id(Project),true);Assert.NotNull(await locked.Where(r=>r.Id==lockedId).ForUpdate().FirstAsync());Assert.Equal(1,await store.UpdateAsync(source,Record(3),D("name","不得提交")));throw new InvalidOperationException("回滚共享当前读");}));Assert.Equal("回滚共享当前读",error.Message);}finally{await Rename("国0");}
            }
            Assert.Equal(before,await Snapshot());verified=true;await Receipt("verified",new{nonce,sourceType,parentType,shorter,derived,verified,sharedFrontiers=true,ancestorCoalesce=true,filterBeforePaging=true,statistics=true,noCountInvalidRejected=true,currentReadInvalidRejected=true,realOldRr=true,logicalParentScope=true,transactionRollback=true,foreignAndDeletedExcluded=true,generatedCrud=false,formalWeb=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,sourceType,parentType,shorter,derived,type=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0)
                {
                    Assert.True(names.All(n=>n.StartsWith(prefix+"_",StringComparison.Ordinal)));Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(actual));Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var hash=await Snapshot();await Receipt("cleanup-before",new{nonce,names,fingerprint,rowsSha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash)))});using var check=new DatabaseSession(options);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));Assert.Equal(hash,await Snapshot(check));await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await cmd.ExecuteNonQueryAsync();}
                }
                await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var cmd=independent.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0});
            }
        }
        /// <summary>真实数据只能走由统一物理字段计划约束的仓储，名称不写入R0来源。</summary>
        Task Insert(int index,bool foreign=false,bool deleted=false)
        {
            var depth=shorter?2:3;var a=index switch{0=>0,1=>1,2=>2,4=>0,_=>depth};var b=index is 3 or 4 or 6 or 7?3:0;var batch=index==4?1:0;var paths=new Dictionary<string,IReadOnlyList<long>>{{"31",new[]{Node(0),Node(1),Node(2)}.Take(a).ToArray()},{"41",new[]{Node(0,batch),Node(1,batch),Node(3,batch)}.Take(b).ToArray()}};var values=group.PackValidatedPaths(paths).ToDictionary();values["id"]=Record(index);values["name"]="来源"+index;values["amount"]=index*10m;values["yz_project_id"]=foreign?Project+1:Project;values["delete_no"]=deleted?new DateTime(2026,1,1):null;return store.InsertAsync(source,values);
        }
        /// <summary>同一表序和主键序比较整组状态，故障样本必须回滚后与原摘要一致。</summary>
        async Task<string> Snapshot(DatabaseSession? target=null){var rows=new List<object>();foreach(var name in names)rows.Add(new{name,rows=(await new SqlRowStore(target??session,c).ReadIncludingDeletedAsync(name)).OrderBy(r=>SqlRowStore.Id(r))});return JsonSerializer.Serialize(rows);}
        /// <summary>意图、首因及清理摘要只保存在私有目录。</summary>
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value)+"\n");
    }
    /// <summary>仅用于适配层父范围验证的最小映射，不是手写业务实体。</summary>
    [FreeSql.DataAnnotations.Table(DisableSyncStructure=true)]private sealed class ScopeRow
    {
        [FreeSql.DataAnnotations.Column(Name="id",IsPrimary=true)]public long Id{get;set;}
        [FreeSql.DataAnnotations.Column(Name="yz_project_id")]public long YzProjectId{get;set;}
        [FreeSql.DataAnnotations.Column(Name="delete_no")]public DateTime? DeleteNo{get;set;}
    }
    /// <summary>大整数始终使用不经浮点的十进制文本。</summary>
    private static string Id(long id)=>id.ToString(CultureInfo.InvariantCulture);
    /// <summary>两种ID物理类型用同一规范文本断言。</summary>
    private static string? Text(IReadOnlyDictionary<string,object?> row,string field)=>row[field] is{} v?Convert.ToString(v,CultureInfo.InvariantCulture):null;
    /// <summary>所有真实数据值均参数化，字典键由固定元数据验证。</summary>
    private static Dictionary<string,object?> D(params object?[] values){var d=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)d.Add((string)values[i]!,values[i+1]);return d;}
}
