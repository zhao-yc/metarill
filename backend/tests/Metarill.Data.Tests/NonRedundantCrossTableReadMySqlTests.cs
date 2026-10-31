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

/// <summary>新跨表逻辑读取的真实四表证明；它不代表生成CRUD、HTTP或浏览器已经接通。</summary>
public sealed class NonRedundantCrossTableReadMySqlTests
{
    /// <summary>字符串来源ID位与两种目标父键均真实执行，长ID及跨表相同ID保持。</summary>
    [MySqlIntegrationFact]
    public async Task String_slots_restore_all_partial_paths_with_fixed_scopes_and_transactions()
    {foreach(var parent in new[]{"string","int64"})await Exercise("string",parent);}
    /// <summary>整数来源ID位共用同一投影、统计与历史存储校验，不依赖驱动转成浮点。</summary>
    [MySqlIntegrationFact]
    public async Task Integer_slots_restore_all_partial_paths_with_fixed_scopes_and_transactions()
    {foreach(var parent in new[]{"string","int64"})await Exercise("int64",parent);}

    /// <summary>只在严格实例pin的隔离库创建随机四表；失败保留原回执，清理必须结构与全量行摘要相符。</summary>
    private static async Task Exercise(string sourceType,string parentType)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||parsed.Database!=options.ExpectedDatabase||parsed.Server!="deve-server.yinzon.com"||parsed.Port!=3306)throw new InvalidOperationException("跨表读取只允许指定Server-200隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var identity=await command.ExecuteReaderAsync();if(!await identity.ReadAsync()||identity.GetString(0)!=options.ExpectedDatabase||identity.GetString(1)!=pin.RootElement.GetProperty("expectedHostname").GetString()||identity.GetString(2)!=pin.RootElement.GetProperty("expectedServerUuid").GetString())throw new InvalidOperationException("跨表读取实例pin不匹配，未创建样本。");}
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrcross_"+nonce[..12];var catalog=NonRedundantCrossTableReadTests.Catalog(prefix,sourceType,parentType,twoTrees:true);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var evidence=Path.Combine(Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??Directory.GetCurrentDirectory(),".local/nonredundant-cross-table-read/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var columns=new[]{"id","name","amount","country_id","country_name","area_id","area_name","place_id","place_name","owner_title","secondary_country_id","secondary_country_name","secondary_area_id","secondary_area_name","secondary_place_id","secondary_place_name"};var plan=MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"1",columns,crossTables:NonRedundantCrossTableReadTests.Scopes(catalog));var selection=CrossTableSparseSelection.Compile(catalog.ConfigTables[0],"15",catalog.ConfigColumns.Where(column=>column.ConfigTableId=="1"));
        var secondary=CrossTableSparseSelection.Compile(catalog.ConfigTables[0],"19",catalog.ConfigColumns.Where(column=>column.ConfigTableId=="1"));
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var store=new SqlRowStore(session,catalog);var reader=new MySqlNonRedundantReader(session);const long node=9007199254740993,other=9007199254740994,first=9007199254750000;var project=long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture);var created=false;var verified=false;string? fingerprint=null;string? rowsHash=null;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var preview=await schema.PreviewAsync(catalog);Assert.Equal(4,preview.Changes.Count);Assert.All(preview.Changes,change=>Assert.Equal("createTable",change.Kind));await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(preview)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            await session.InTransactionAsync(async()=>
            {
                await Target(1,node,"国%_!'中文",null);await Target(1,other,"乙国",null);await Target(2,node,"甲区",node);await Target(2,other,"乙区",other);await Target(3,node,"甲市",node);await Target(3,other,"乙市",other);
                await Target(3,1007,"已删市",node,deleted:true);await Target(3,1008,"其它项目市",node,foreign:true);await Target(3,1009,"坏父市",999);await Target(3,1010,"范围外市",node,enabled:false);
                await Source(0,[]);await Source(1,[node]);await Source(2,[other]);await Source(3,[node,node]);await Source(4,[node,node,node]);await Source(5,[other,other]);await Source(6,[other,other,other]);await Source(7,[node,node,999]);await Source(8,[node,node,1007]);await Source(9,[node,node,1008]);await Source(10,[node,999,1009]);await Source(11,[node,node,1010]);await Source(20,[node],deleted:true);await Source(21,[node,node,node],foreign:true);return true;
            });rowsHash=await RowsHash();
            var all=await reader.ReadPageAsync(plan,[],[],1,30);Assert.Equal(12,all.Total);Assert.Equal(12,all.Records.Count);Assert.All(all.Records,row=>Assert.Equal(columns.Order(),row.Keys.Order()));
            foreach(var (offset,depth,id) in new[]{(1,0,node),(2,0,other),(3,1,node),(4,2,node),(5,1,other),(6,2,other)})
            {
                var row=all.Records.Single(row=>Id(row)==first+offset);Assert.Equal(depth>=0?id.ToString(CultureInfo.InvariantCulture):null,Text(row,"country_id"));Assert.Equal(depth>=1?id.ToString(CultureInfo.InvariantCulture):null,Text(row,"area_id"));Assert.Equal(depth>=2?id.ToString(CultureInfo.InvariantCulture):null,Text(row,"place_id"));Assert.Equal(depth>=2?(id==node?"甲市":"乙市"):null,row["place_name"]);
                var physical=Assert.Single(await store.ReadAsync(names[0],new Dictionary<string,object?>{{"id",first+offset}}));var sparse=selection.ReadStored(selection.Slots.ToDictionary(slot=>slot.ColumnName,slot=>physical[slot.ColumnName]));Assert.Equal(new CrossTableSelectedNode(depth,id),sparse);
            }
            var clear=all.Records.Single(row=>Id(row)==first);Assert.All(new[]{"country_id","country_name","area_id","area_name","place_id","place_name"},field=>Assert.Null(clear[field]));Assert.Equal("国%_!'中文",clear["owner_title"]);Assert.All(all.Records,row=>Assert.Equal("乙国",row["secondary_country_name"]));
            foreach(var offset in new[]{7,8,9,10,11}){var row=all.Records.Single(row=>Id(row)==first+offset);Assert.Null(row["country_id"]);Assert.Null(row["area_id"]);Assert.All(new[]{"country_name","area_name","place_name"},field=>Assert.Null(row[field]));Assert.NotNull(row["place_id"]);}
            var roots=new[]{new FixedReadFilter("country_id",FixedReadComparison.Equal,sourceType=="string"?node.ToString(CultureInfo.InvariantCulture):(object)node)};var page=await reader.ReadPageAsync(plan,roots,[],2,1);Assert.Equal(3,page.Total);Assert.Equal(first+3,Id(Assert.Single(page.Records)));var empty=await reader.ReadPageAsync(plan,roots,[],99,1);Assert.Equal(3,empty.Total);Assert.Empty(empty.Records);
            var stats=await reader.ReadStatisticsAsync(plan,roots,["amount"]);Assert.Equal(3,stats.Total);Assert.Equal(80m,stats.Sums["amount"]);Assert.Null((await reader.ReadStatisticsAsync(plan,[new("id",FixedReadComparison.Equal,-1L)],["amount"])).Sums["amount"]);
            Assert.Equal(3,(await reader.ReadPageAsync(plan,[new("country_name",FixedReadComparison.Contains,"%_!")],[],1,30)).Total);Assert.Empty((await reader.ReadPageAsync(plan,[new("country_name",FixedReadComparison.Equal,"' OR 1=1 --")],[],1,30)).Records);
            var hidden=MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"1",["id","name"],dependencies:["country_name"],crossTables:NonRedundantCrossTableReadTests.Scopes(catalog));var hiddenRows=await reader.ReadPageAsync(hidden,[new("country_name",FixedReadComparison.Contains,"%_!")],[],1,30);Assert.Equal(3,hiddenRows.Total);Assert.All(hiddenRows.Records,row=>Assert.Equal(new[]{"id","name"},row.Keys));Assert.Single(await reader.ReadRowsAsync(plan,roots,[],1));Assert.Equal(12,(await reader.ReadPageAsync(plan,[],[],1,0,unpaged:true)).Records.Count);
            var runtime=await reader.ReadPageAsync(plan,[],[],1,30,projectId:(project+1).ToString(CultureInfo.InvariantCulture));Assert.Equal(first+21,Id(Assert.Single(runtime.Records)));Assert.Null(runtime.Records[0]["country_name"]);
            // 目标名称在当前事务内立即参与过滤，尾部故障使来源与目标一起回滚。
            var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.Equal(1,await store.UpdateAsync(names[1],node,new Dictionary<string,object?>{{"title","事务内改名"}}));Assert.Equal(3,(await reader.ReadPageAsync(plan,[new("country_name",FixedReadComparison.Equal,"事务内改名")],[],1,30)).Total);throw new InvalidOperationException("跨表读取尾部回滚");}));Assert.Equal("跨表读取尾部回滚",failure.Message);Assert.Equal(rowsHash,await RowsHash());
            // 固定目标条件真正参与祖先路径：整条路径不可见时仅保留来源所选ID，不泄漏祖先名称。
            await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.Equal(1,await store.UpdateAsync(names[1],node,new Dictionary<string,object?>{{"title",NonRedundantCrossTableReadTests.BlockedTitle}}));var row=Assert.Single((await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.Equal,first+4)],[],1,1)).Records);Assert.Null(row["country_id"]);Assert.Null(row["area_id"]);Assert.Null(row["country_name"]);Assert.Null(row["place_name"]);Assert.Equal(node.ToString(CultureInfo.InvariantCulture),Text(row,"place_id"));throw new InvalidOperationException("回滚目标范围样本");}));Assert.Equal(rowsHash,await RowsHash());
            await VerifyParentValues();await VerifyInvalidStorage();await VerifyCurrentRead();await VerifyLogicalParentScope();Assert.Equal(rowsHash,await RowsHash());
            using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reader.ReadPageAsync(plan,[],[],1,10,cancellation.Token));}
            var targetName=catalog.ConfigColumns.Single(column=>column.Id=="22");targetName.ColumnName="not_real_after_compile";try{Assert.Equal(12,(await reader.ReadPageAsync(plan,[],[],1,30)).Total);}finally{targetName.ColumnName="title";}
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);verified=true;await Receipt("verified",false);
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{nonce,sourceType,parentType,errorType=error.GetType().FullName,verified=false})+"\n");throw;}
        finally
        {
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);
                if(fingerprint is null||MySqlSchemaPlanner.Fingerprint(actual)!=fingerprint||rowsHash is null||await RowsHash()!=rowsHash||!names.SequenceEqual(new[]{prefix+"_source",prefix+"_country",prefix+"_area",prefix+"_place"}))throw new InvalidOperationException("跨表样本结构／行摘要不符，保留现场，未补清理。");
                await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}
                Assert.Empty((await inspection.InspectAsync(catalog)).Tables);await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
        }
        /// <summary>原字符串父键的前导零仍可回读；损坏或越界父键只隐藏派生路径，不改写来源选择。</summary>
        async Task VerifyParentValues()
        {
            var cases=parentType=="string"?new object[]{"0"+node,"bad","1.0","9223372036854775808","0"}:new object[]{-1L,0L};
            foreach(var value in cases)
            {
                var marker=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>
                {
                    Assert.Equal(1,await store.UpdateAsync(names[3],node,new Dictionary<string,object?>{{"area_id",value}}));var row=Assert.Single((await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.Equal,first+4)],[],1,1)).Records);
                    if(value is string text&&text=="0"+node){Assert.Equal("国%_!'中文",row["country_name"]);Assert.Equal(node.ToString(CultureInfo.InvariantCulture),Text(row,"area_id"));}else{Assert.Null(row["country_name"]);Assert.Null(row["area_id"]);Assert.Equal(node.ToString(CultureInfo.InvariantCulture),Text(row,"place_id"));}
                    Assert.Equal("乙国",row["secondary_country_name"]);throw new InvalidOperationException("回滚父键样本");
                }));Assert.Equal("回滚父键样本",marker.Message);Assert.Equal(rowsHash,await RowsHash());
            }
        }
        /// <summary>只验证历史坏值的读取拒绝，所有故意错误均加入事务并强制回滚。</summary>
        async Task VerifyInvalidStorage()
        {
            foreach(var kind in new[]{"multiple","invalid"})
            {
                var marker=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>
                {
                    var values=selection.PackValidatedPath([]).ToDictionary();values["country_id"]=sourceType=="string"?node.ToString(CultureInfo.InvariantCulture):(object)node;
                    if(kind=="multiple")values["place_id"]=sourceType=="string"?node.ToString(CultureInfo.InvariantCulture):(object)node;else values["country_id"]=sourceType=="string"?"0"+node:(object)(-1L);
                    Assert.Equal(1,await store.UpdateAsync(names[0],first,values));var filter=new[]{new FixedReadFilter("id",FixedReadComparison.Equal,first)};await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadPageAsync(plan,filter,[],1,10));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadRowsAsync(plan,filter,[],10));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadStatisticsAsync(plan,filter,["amount"]));throw new InvalidOperationException("故意回滚坏存储");
                }));Assert.Equal("故意回滚坏存储",marker.Message);Assert.Equal(rowsHash,await RowsHash());
            }
        }
        /// <summary>建立旧RR快照后，私有当前读的数量及明细必须同时看到外部已提交改名；结束后恢复原值。</summary>
        async Task VerifyCurrentRead()
        {
            var changed=false;await using var external=new MySqlConnection(options.ConnectionString);await external.OpenAsync();
            try
            {
                await session.InTransactionAsync(async()=>
                {
                    Assert.Equal(3,(await reader.ReadPageAsync(plan,[new("country_name",FixedReadComparison.Equal,"国%_!'中文")],[],1,30)).Total);
                    await using(var update=external.CreateCommand()){update.CommandText="UPDATE `"+names[1]+"` SET title=@title WHERE id=@id";update.Parameters.AddWithValue("title","RR后改名");update.Parameters.AddWithValue("id",node);Assert.Equal(1,await update.ExecuteNonQueryAsync());changed=true;}
                    Assert.Equal(3,(await reader.ReadPageAsync(plan,[new("country_name",FixedReadComparison.Equal,"国%_!'中文")],[],1,30)).Total);
                    var current=await reader.ReadPageAsync(plan,[new("country_name",FixedReadComparison.Equal,"RR后改名")],[],1,30,currentRead:true);Assert.Equal(3,current.Total);Assert.Equal(3,current.Records.Count);Assert.All(current.Records,row=>Assert.Equal("RR后改名",row["country_name"]));return true;
                });
            }
            finally{if(changed){await using var restore=external.CreateCommand();restore.CommandText="UPDATE `"+names[1]+"` SET title=@title WHERE id=@id";restore.Parameters.AddWithValue("title","国%_!'中文");restore.Parameters.AddWithValue("id",node);Assert.Equal(1,await restore.ExecuteNonQueryAsync());}}
        }
        /// <summary>逻辑父范围相关同一来源行，当前锁读覆盖整条选中路径，不能用历史快照或原始空位判归属。</summary>
        async Task VerifyLogicalParentScope()
        {
            NonRedundantReadPlan ScopePlan(params string[] fields)=>NonRedundantReads.CompileWithCrossTables(catalog,"1",["id"],NonRedundantCrossTableReadTests.Scopes(catalog),fields);
            var root=ScopePlan("country_id");var mid=ScopePlan("area_id");var leaf=ScopePlan("place_id");var both=ScopePlan("country_id","secondary_country_id");
            FreeSql.ISelect<ScopeRow> Query()=>session.Select<ScopeRow>().AsTable((_,_)=>names[0]).Where(row=>row.YzProjectId==project&&row.DeleteNo==null);
            object Value(long id)=>sourceType=="string"?id.ToString(CultureInfo.InvariantCulture):id;
            FreeSql.ISelect<ScopeRow> Match(NonRedundantReadPlan fixedPlan,string field,long id,bool locking=false)=>NonRedundantCrossTableScopeSql.Apply(Query(),session,fixedPlan,field,"scope_main",Value(id),catalog.YzProjectId,locking);
            Assert.Equal(new[]{first+1,first+3,first+4},(await Match(root,"country_id",node).OrderBy(row=>row.Id).ToListAsync()).Select(row=>row.Id));
            Assert.Equal(new[]{first+3,first+4},(await Match(mid,"area_id",node).OrderBy(row=>row.Id).ToListAsync()).Select(row=>row.Id));
            Assert.Equal(first+4,Assert.Single(await Match(leaf,"place_id",node).ToListAsync()).Id);
            var intersect=NonRedundantCrossTableScopeSql.Apply(Match(both,"country_id",node),session,both,"secondary_country_id","scope_other",Value(other),catalog.YzProjectId);
            Assert.Equal(3,await intersect.CountAsync());
            Assert.Empty(await NonRedundantCrossTableScopeSql.Apply(Match(both,"country_id",node),session,both,"secondary_country_id","scope_none",Value(node),catalog.YzProjectId).ToListAsync());
            // 原列表保留失效所选ID供回显；写范围不能凭这个原值放行无目标、已删、跨项目、坏父或固定条件外的路径。
            foreach(var id in new long[]{999,1007,1008,1009,1010})Assert.Empty(await Match(leaf,"place_id",id).ToListAsync());
            foreach(var values in new[]{new Dictionary<string,object?>{{"country_id",Value(node)},{"place_id",Value(node)}},new Dictionary<string,object?>{{"country_id",sourceType=="string"?(object)("0"+node):-1L}}})
            {
                var marker=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.Equal(1,await store.UpdateAsync(names[0],first,values));Assert.Empty(await Match(root,"country_id",node).Where(row=>row.Id==first).ToListAsync());throw new InvalidOperationException("回滚逻辑范围坏值");}));Assert.Equal("回滚逻辑范围坏值",marker.Message);
            }
            await using var outside=new MySqlConnection(options.ConnectionString);await outside.OpenAsync();await using(var setup=outside.CreateCommand()){setup.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setup.ExecuteNonQueryAsync();}
            // 同一写事务的相关子查询与外层来源都实际持锁；另一条路径仍可在独立事务修改并回滚。
            var beforeTransaction=Query();
            await session.InTransactionAsync(async()=>
            {
                Assert.Equal(first+4,(await NonRedundantCrossTableScopeSql.Apply(beforeTransaction,session,root,"country_id","prebuilt",Value(node),catalog.YzProjectId,true).Where(row=>row.Id==first+4).ForUpdate().FirstAsync())!.Id);
                for(var level=0;level<4;level++)
                {
                    await using var command=outside.CreateCommand();command.CommandText="UPDATE `"+names[level]+"` SET "+(level==0?"name":"title")+"=@name WHERE id=@id";command.Parameters.AddWithValue("name","锁期间禁止变化");command.Parameters.AddWithValue("id",level==0?first+4:node);var blocked=await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteNonQueryAsync());Assert.Equal(1205,blocked.Number);
                }
                await using var independent=await outside.BeginTransactionAsync();
                for(var level=1;level<4;level++){await using var command=outside.CreateCommand();command.Transaction=independent;command.CommandText="UPDATE `"+names[level]+"` SET title=@name WHERE id=@id";command.Parameters.AddWithValue("name","独立路径可修改");command.Parameters.AddWithValue("id",other);Assert.Equal(1,await command.ExecuteNonQueryAsync());}await independent.RollbackAsync();return true;
            });Assert.Equal(rowsHash,await RowsHash());
            // 先建旧RR快照，再从独立连接移动叶节点；逻辑锁范围必须按新父链决定当前归属。
            var moved=false;
            try
            {
                await session.InTransactionAsync(async()=>
                {
                    await using(var isolation=session.CurrentTransaction!.Connection!.CreateCommand()){isolation.Transaction=session.CurrentTransaction;isolation.CommandText="SELECT @@transaction_isolation";Assert.Equal("REPEATABLE-READ",await isolation.ExecuteScalarAsync());}
                    Assert.Equal(first+4,(await Match(root,"country_id",node).Where(row=>row.Id==first+4).FirstAsync())!.Id);
                    await Move(other);moved=true;
                    Assert.Equal(first+4,(await Match(root,"country_id",node).Where(row=>row.Id==first+4).FirstAsync())!.Id);
                    Assert.Null(await Match(root,"country_id",node,true).Where(row=>row.Id==first+4).ForUpdate().FirstAsync());
                    Assert.Equal(first+4,(await Match(root,"country_id",other,true).Where(row=>row.Id==first+4).ForUpdate().FirstAsync())!.Id);return true;
                });
            }
            finally{if(moved)await Move(node);}
            var rollback=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.NotNull(await Match(root,"country_id",node,true).Where(row=>row.Id==first+4).ForUpdate().FirstAsync());Assert.Equal(1,await store.UpdateAsync(names[0],first+4,new Dictionary<string,object?>{{"name","整笔回滚"}}));throw new InvalidOperationException("逻辑范围尾部故障");}));Assert.Equal("逻辑范围尾部故障",rollback.Message);Assert.Equal(rowsHash,await RowsHash());
            // 目标页常量同样限制直接选中的根，不只限制推导祖先；只作事务内变更并回滚。
            var hidden=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.Equal(1,await store.UpdateAsync(names[1],node,new Dictionary<string,object?>{{"title",NonRedundantCrossTableReadTests.BlockedTitle}}));Assert.Null(await Match(root,"country_id",node,true).Where(row=>row.Id==first+1).ForUpdate().FirstAsync());throw new InvalidOperationException("回滚逻辑范围隐藏根");}));Assert.Equal("回滚逻辑范围隐藏根",hidden.Message);Assert.Equal(rowsHash,await RowsHash());
            /// <summary>独立连接只改本轮叶节点父键；外层finally恢复，再参与严格数据摘要清理。</summary>
            async Task Move(long parent){await using var command=outside.CreateCommand();command.CommandText="UPDATE `"+names[3]+"` SET area_id=@parent WHERE id=@id";command.Parameters.AddWithValue("parent",parentType=="string"?parent.ToString(CultureInfo.InvariantCulture):(object)parent);command.Parameters.AddWithValue("id",node);Assert.Equal(1,await command.ExecuteNonQueryAsync());}
        }
        /// <summary>物理样本字段均由统一计划白名单约束，名称不写入R0来源。</summary>
        Task Source(int offset,long[] path,bool deleted=false,bool foreign=false)
        {var values=selection.PackValidatedPath(path).ToDictionary();foreach(var pair in secondary.PackValidatedPath(Enumerable.Repeat(other,offset%3+1).ToArray()))values.Add(pair.Key,pair.Value);values.Add("id",first+offset);values.Add("name","来源"+offset);values.Add("amount",offset*10m);values.Add("yz_project_id",foreign?project+1:project);values.Add("delete_no",deleted?new DateTime(2026,1,1):null);values.Add("owner_id",node);return store.InsertAsync(names[0],values);}
        /// <summary>按本轮已声明类型保存目标项目与父键，允许不同表同ID。</summary>
        Task Target(int level,long id,string title,long? parent,bool deleted=false,bool foreign=false,bool enabled=true)
        {var values=new Dictionary<string,object?>{{"id",id},{"title",title},{"is_enabled",enabled},{"yz_project_id",level==2?(foreign?project+1:project).ToString(CultureInfo.InvariantCulture):(object)(foreign?project+1:project)},{"delete_no",deleted?new DateTime(2026,1,1):null}};if(level>1)values.Add(level==2?"country_id":"area_id",parentType=="string"?parent?.ToString(CultureInfo.InvariantCulture):parent);return store.InsertAsync(names[level],values);}
        async Task<string> RowsHash(){var rows=new List<object>();foreach(var name in names)rows.Add((await store.ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)).ToArray());return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));}
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,sourceType,parentType,names,database=options.ExpectedDatabase,instanceVerified=true,fingerprint,rowsHash,verified,cleaned,independentTableAbsenceChecked=cleaned,twoIndependentTrees=true,logicalReadExecuted=verified,logicalParentScopeExecuted=verified,logicalParentCurrentLockExecuted=verified,generatedCrudExecuted=false,browserExecuted=false,grantAttempted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>适配层测试的最小来源映射；业务实体仍由元数据生成，不启用ORM结构同步。</summary>
    [FreeSql.DataAnnotations.Table(DisableSyncStructure=true)]
    private sealed class ScopeRow
    {
        [FreeSql.DataAnnotations.Column(Name="id",IsPrimary=true)]public long Id{get;set;}
        [FreeSql.DataAnnotations.Column(Name="yz_project_id")]public long YzProjectId{get;set;}
        [FreeSql.DataAnnotations.Column(Name="delete_no")]public DateTime? DeleteNo{get;set;}
    }
    /// <summary>驱动值保持原精确整数／字符串，不经过JSON浮点。</summary>
    private static long Id(IReadOnlyDictionary<string,object?> row)=>Convert.ToInt64(row["id"],CultureInfo.InvariantCulture);
    /// <summary>仅用于断言两种物理类型的同一逻辑ID。</summary>
    private static string? Text(IReadOnlyDictionary<string,object?> row,string field)=>row[field] is{} value?Convert.ToString(value,CultureInfo.InvariantCulture):null;
}
