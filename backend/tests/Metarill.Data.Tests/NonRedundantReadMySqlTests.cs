using System.Globalization;
using FreeSql.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>固定单值计划的真实参数化SQL测试，不替代生成页面HTTP或浏览器验收。</summary>
public sealed class NonRedundantReadMySqlTests
{
    /// <summary>正式结构预览与应用后，验证数据库内过滤/排序/分页、目标范围及事务读取，再核对并精确清理自有表。</summary>
    [MySqlIntegrationFact]
    public async Task Single_value_plan_executes_scoped_joins_filters_count_paging_and_transaction_reads()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var builder=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||builder.Database!="metarill_business_test"||builder.Server!="deve-server.yinzon.com"||builder.Port!=3306)throw new InvalidOperationException("固定读取实库测试只允许指定隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var connection=new MySqlConnection(options.ConnectionString))
        {await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await command.ExecuteReaderAsync();Assert.True(await r.ReadAsync()&&r.GetString(0)==options.ExpectedDatabase&&r.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例身份不匹配，未创建任何样本。");}
        var nonce=Guid.NewGuid().ToString("N");var catalog=NonRedundantReadTests.Catalog("mt_nrr_root_"+nonce[..12],"mt_nrr_party_"+nonce[..12]);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var evidence=Path.Combine(Root(),".local/nonredundant-read/mysql-"+nonce);Directory.CreateDirectory(evidence);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var store=new SqlRowStore(session,catalog);var reader=new MySqlNonRedundantReader(session);var plan=NonRedundantReadTests.Plan(catalog);
        const long first=9007199254741001,second=9007199254741002;var project=long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture);var created=false;var verified=false;string? fingerprint=null;string? rowsHash=null;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var changes=await schema.PreviewAsync(catalog);Assert.Equal(2,changes.Changes.Count);Assert.All(changes.Changes,change=>Assert.Equal("createTable",change.Kind));await Receipt("intent",false);
            created=true;var applied=await schema.ApplyAsync(changes);Assert.True(applied.Succeeded);Assert.Equal(2,applied.AppliedStatements);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            await session.InTransactionAsync(async()=>
            {
                await Party(101,"A%_!'中文",102);await Party(102,"B候选",null);await Party(103,"删除目标",null,deleted:true);await Party(104,"其它项目目标",null,foreign:true);
                await Source(first,"第一条",101,102,second);await Source(second,"第二条",102,101,null);await Source(first+2,"已删除源",101,null,null,deleted:true);await Source(first+3,"其它项目源",101,null,null,foreign:true);await Source(first+4,"不可见目标",103,104,null);await Source(first+5,"缺失目标",999,null,null);return true;
            });
            rowsHash=await RowsHash();
            var statistics=await reader.ReadStatisticsAsync(plan,[],["bill_amount"]);Assert.Equal(4,statistics.Total);Assert.Equal(31m,statistics.Sums["bill_amount"]);
            var all=await reader.ReadPageAsync(plan,[],[],1,20);Assert.Equal(4,all.Total);Assert.Equal(4,all.Records.Count);Assert.All(all.Records,row=>Assert.Equal(7,row.Count));
            var bounded=await reader.ReadRowsAsync(plan,[new("bill_name",FixedReadComparison.IsNotNull)],[new("bill_name")],1);Assert.Equal(first,Convert.ToInt64(Assert.Single(bounded)["id"],CultureInfo.InvariantCulture));
            var unpaged=await reader.ReadPageAsync(plan,[],[],1,0,unpaged:true);Assert.Equal(4,unpaged.Records.Count);
            var hiddenPlan=MySqlNonRedundantReadCompiler.Compile(catalog,"1",["id","name"],["bill_name"]);var hiddenOutput=await reader.ReadPageAsync(hiddenPlan,[new("bill_name",FixedReadComparison.Contains,"%_!")],[],1,10);Assert.Equal(2,Assert.Single(hiddenOutput.Records).Count);
            var selected=await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.In,new object?[]{first.ToString(CultureInfo.InvariantCulture),null})],[],1,10);Assert.Equal(first,Convert.ToInt64(Assert.Single(selected.Records)["id"],CultureInfo.InvariantCulture));Assert.Empty((await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.In,Array.Empty<object?>())],[],1,10)).Records);
            var runtimeProject=await reader.ReadPageAsync(plan,[],[],1,10,projectId:(project+1).ToString(CultureInfo.InvariantCulture));Assert.Equal(first+3,Convert.ToInt64(Assert.Single(runtimeProject.Records)["id"],CultureInfo.InvariantCulture));Assert.Null(runtimeProject.Records[0]["bill_name"]);
            var one=Assert.Single((await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.Equal,first.ToString(CultureInfo.InvariantCulture))],[],1,10)).Records);Assert.Equal("A%_!'中文",one["bill_name"]);Assert.Equal("B候选",one["ship_name"]);Assert.Equal("B候选",one["grand_label"]);Assert.Equal("第二条",one["parent_name"]);Assert.Equal(first,Convert.ToInt64(one["id"],CultureInfo.InvariantCulture));
            var restricted=await reader.ReadPageAsync(plan,[new("bill_name",FixedReadComparison.IsNotNull)],[new("bill_name")],2,1);Assert.Equal(2,restricted.Total);Assert.Equal(second,Convert.ToInt64(Assert.Single(restricted.Records)["id"],CultureInfo.InvariantCulture));
            var literal=await reader.ReadPageAsync(plan,[new("bill_name",FixedReadComparison.Contains,"%_!")],[],1,10);Assert.Equal(1,literal.Total);var filteredStatistics=await reader.ReadStatisticsAsync(plan,[new("bill_name",FixedReadComparison.Contains,"%_!")],["bill_amount"]);Assert.Equal(1,filteredStatistics.Total);Assert.Equal(10.25m,filteredStatistics.Sums["bill_amount"]);Assert.Equal(first,Convert.ToInt64(Assert.Single(literal.Records)["id"],CultureInfo.InvariantCulture));
            Assert.Empty((await reader.ReadPageAsync(plan,[new("bill_name",FixedReadComparison.Equal,"' OR 1=1 --")],[],1,10)).Records);
            var hidden=await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.Equal,first+4)],[],1,10);var invisible=Assert.Single(hidden.Records);Assert.Null(invisible["bill_name"]);Assert.Null(invisible["ship_name"]);
            var missing=Assert.Single((await reader.ReadPageAsync(plan,[new("id",FixedReadComparison.Equal,first+5)],[],1,10)).Records);Assert.Null(missing["bill_name"]);
            var emptyPage=await reader.ReadPageAsync(plan,[new("bill_name",FixedReadComparison.IsNotNull)],[],99,1);Assert.Equal(2,emptyPage.Total);Assert.Empty(emptyPage.Records);
            // 目标名称在当前事务内变化即参与过滤，尾部故障后正式事务回滚。
            await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>
            {Assert.Equal(1,await store.UpdateAsync(names[1],101,new Dictionary<string,object?>{{"name","事务内名称"},{"amount",30.25m}}));var current=await reader.ReadPageAsync(plan,[new("bill_name",FixedReadComparison.Equal,"事务内名称")],[],1,10);Assert.Equal(first,Convert.ToInt64(Assert.Single(current.Records)["id"],CultureInfo.InvariantCulture));Assert.Equal(51m,(await reader.ReadStatisticsAsync(plan,[],["bill_amount"])).Sums["bill_amount"]);throw new InvalidOperationException("读取事务尾部测试故障。");}));
            Assert.Equal(rowsHash,await RowsHash());Assert.Equal(31m,(await reader.ReadStatisticsAsync(plan,[],["bill_amount"])).Sums["bill_amount"]);Assert.Null((await reader.ReadStatisticsAsync(plan,[new("id",FixedReadComparison.Equal,-1L)],["bill_amount"])).Sums["bill_amount"]);Assert.Equal(1,(await reader.ReadPageAsync(plan,[new("bill_name",FixedReadComparison.Contains,"%_!")],[],1,10)).Total);
            await VerifyDerivedLocksAsync(options,catalog,first,second);
            // 编译结果不继续持有可变元数据：修改源名称不能改变已发布的SQL。
            var sourceColumn=catalog.ConfigColumns.Single(column=>column.Id=="22");sourceColumn.ColumnName="not_a_real_column";
            try{Assert.Equal(4,(await reader.ReadPageAsync(plan,[],[],1,20)).Total);}finally{sourceColumn.ColumnName="name";}
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog)));verified=true;await Receipt("verified",false);
        }
        finally
        {
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);
                if(actual.Tables.Count>0)
                {
                    if(fingerprint is null||MySqlSchemaPlanner.Fingerprint(actual)!=fingerprint||rowsHash is null||await RowsHash()!=rowsHash||names[0]!="mt_nrr_root_"+nonce[..12]||names[1]!="mt_nrr_party_"+nonce[..12])throw new InvalidOperationException("自有结构或行摘要不匹配，保留样本现场。");
                    await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}
                }
                // 新会话及独立连接复核精确两表已不存在，不将本用例核验移作其它批次的清理证明。
                Assert.Empty((await inspection.InspectAsync(catalog)).Tables);await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
        }
        Task Party(long id,string name,long? parent,bool deleted=false,bool foreign=false)=>store.InsertAsync(names[1],new Dictionary<string,object?>{{"id",id},{"name",name},{"amount",id==101?10.25m:id==102?20.75m:999m},{"parent_id",parent},{"yz_project_id",foreign?project+1:project},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        Task Source(long id,string name,long? bill,long? ship,long? parent,bool deleted=false,bool foreign=false)=>store.InsertAsync(names[0],new Dictionary<string,object?>{{"id",id},{"name",name},{"bill_id",bill},{"ship_id",ship},{"parent_id",parent},{"yz_project_id",foreign?project+1:project},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        async Task<string> RowsHash(){var rows=new List<object>();foreach(var name in names)rows.Add((await store.ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)).ToArray());return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));}
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,names,instanceVerified=true,fingerprint,rowsHash,verified,cleaned,independentTableAbsenceChecked=cleaned,generatedHttpExecuted=false,browserExecuted=false,grantAttempted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>双连接证明关联锁、无关目标可写、提交释放及RR旧快照不能保留已失效匹配；不申请观测权限。</summary>
    private static async Task VerifyDerivedLocksAsync(DatabaseOptions options,MetadataCatalog catalog,long first,long second)
    {
        using var session=new DatabaseSession(options);var store=new SqlRowStore(session,catalog);var party=catalog.ConfigTables.Single(table=>table.Id=="2").TableName;var source=catalog.ConfigTables.Single(table=>table.Id=="1").TableName;
        var project=long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture);var plan=NonRedundantReads.CompileMatch(catalog,"1","bill_name");
        var originals=(await store.ReadAsync(party)).Where(row=>SqlRowStore.Id(row) is 101 or 102).ToDictionary(row=>SqlRowStore.Id(row),row=>row["name"]);
        await using var other=new MySqlConnection(options.ConnectionString);await other.OpenAsync();
        await using(var setup=other.CreateCommand()){setup.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setup.ExecuteNonQueryAsync();}
        try
        {
            Assert.Throws<InvalidOperationException>(()=>ExactDataManageSql.ApplyDerived(Query(),session,plan,"bill_name","mt_lock",[originals[101]],catalog.YzProjectId));
            await session.InTransactionAsync(async()=>
            {
                var query=ExactDataManageSql.ApplyDerived(Query(),session,plan,"bill_name","mt_lock",[originals[101]],catalog.YzProjectId).Where(row=>row.Id==first).ForUpdate();
                var sql=query.ToSql();Assert.DoesNotContain((string)originals[101]!,sql);Assert.Contains("EXISTS",sql,StringComparison.OrdinalIgnoreCase);
                Assert.Equal(first,(await query.FirstAsync())!.Id);
                // 匹配计划只锁bill来源，无关目标必须真实写成功。
                Assert.Equal(1,await Rename(102,"独立目标仍可修改"));
                var blocked=await Assert.ThrowsAsync<MySqlException>(()=>Rename(101,"锁期间不得修改"));Assert.Equal(1205,blocked.Number);
                Assert.Null(await ExactDataManageSql.ApplyDerived(Query(),session,plan,"bill_name","mt_lock",["' OR 1=1 --"],catalog.YzProjectId).Where(row=>row.Id==second).ForUpdate().FirstAsync());
                Assert.Null(await ExactDataManageSql.ApplyDerived(Query(),session,plan,"bill_name","mt_lock",[],catalog.YzProjectId).Where(row=>row.Id==first).ForUpdate().FirstAsync());return true;
            });
            // 跨两级的匹配必须锁住每一级真实目标，不能只锁终点或首个外键。
            var chained=NonRedundantReads.CompileMatch(catalog,"1","grand_label");
            await session.InTransactionAsync(async()=>
            {
                Assert.Equal(first,(await ExactDataManageSql.ApplyDerived(Query(),session,chained,"grand_label","mt_chain",["独立目标仍可修改"],catalog.YzProjectId).Where(row=>row.Id==first).ForUpdate().FirstAsync())!.Id);
                foreach(var target in new long[]{101,102}){var blocked=await Assert.ThrowsAsync<MySqlException>(()=>Rename(target,"链路锁期间不得修改"));Assert.Equal(1205,blocked.Number);}return true;
            });
            Assert.Equal(1,await Rename(101,"提交后关联已改变"));
            await session.InTransactionAsync(async()=>
            {
                await using(var isolation=session.CurrentTransaction!.Connection!.CreateCommand()){isolation.Transaction=session.CurrentTransaction;isolation.CommandText="SELECT @@transaction_isolation";Assert.Equal("REPEATABLE-READ",await isolation.ExecuteScalarAsync());}
                var old=await new NonRedundantReader(session).PageAsync(plan,[new("id",FixedReadComparison.Equal,first)],[],1,1);Assert.Equal("提交后关联已改变",Assert.Single(old.Records)["bill_name"]);
                Assert.Equal(1,await Rename(101,"快照后新匹配"));
                Assert.Null(await ExactDataManageSql.ApplyDerived(Query(),session,plan,"bill_name","mt_lock",["提交后关联已改变"],catalog.YzProjectId).Where(row=>row.Id==first).ForUpdate().FirstAsync());
                Assert.Equal(first,(await ExactDataManageSql.ApplyDerived(Query(),session,plan,"bill_name","mt_lock",["快照后新匹配"],catalog.YzProjectId).Where(row=>row.Id==first).ForUpdate().FirstAsync())!.Id);return true;
            });
        }
        finally{foreach(var row in originals)Assert.Equal(1,await Rename(row.Key,(string)row.Value!));}
        /// <summary>外层保留真实项目及固定主键；随机表名只来自本用例元数据。</summary>
        FreeSql.ISelect<MatchingRoot> Query()=>session.Select<MatchingRoot>().AsTable((_,_)=>source).Where(row=>row.YzProjectId==project);
        /// <summary>独立连接只更新本轮自有两条目标，不读取其它库。</summary>
        async Task<int> Rename(long id,string name){await using var command=other.CreateCommand();command.CommandText="UPDATE "+session.Provider.QuoteIdentifier(party)+" SET name=@name WHERE id=@id";command.Parameters.AddWithValue("name",name);command.Parameters.AddWithValue("id",id);return await command.ExecuteNonQueryAsync();}
    }
    /// <summary>仅用于适配层外层映射，业务实体仍由元数据生成。</summary>
    [Table(DisableSyncStructure=true)]
    private sealed class MatchingRoot
    {
        [Column(Name="id",IsPrimary=true)] public long Id{get;set;}
        [Column(Name="yz_project_id")] public long YzProjectId{get;set;}
    }
    /// <summary>按测试源码所在clone写入私有收据，不输出凭据。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
