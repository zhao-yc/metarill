using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Metarill.Data.Schema;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>只在提供本轮独占范围和专用配置时运行；默认CI明确跳过真实数据库用例。</summary>
public sealed class MixedRelationReverseMySqlFactAttribute:FactAttribute
{
    /// <summary>发现阶段不连接数据库或读取配置正文。</summary>
    public MixedRelationReverseMySqlFactAttribute()
    {
        if(new[]{"METARILL_TEST_SETTINGS","METARILL_REVERSE_SCOPE"}.Any(key=>!File.Exists(Environment.GetEnvironmentVariable(key))))
            Skip="需要专用MySQL配置和本轮独占反查测试范围。";
    }
}

/// <summary>执行真实共享反查方法，验证精确字符串、主键分批、项目与删除范围以及无关目标并发。</summary>
public sealed class MixedRelationReverseMySqlTests
{
    private const long Target=9007199254740993;
    private sealed record Seed(long Id,long Project,string? Foreign,bool Deleted=false);

    /// <summary>索引仅通过元数据预览和显式安装；测试结束核对数据与结构后清理本轮两表。</summary>
    [MixedRelationReverseMySqlFact]
    public async Task Indexed_reverse_keeps_exact_ids_scope_paging_and_unrelated_target_locks()
    {
        var scopePath=Environment.GetEnvironmentVariable("METARILL_REVERSE_SCOPE")!;
        using var scope=JsonDocument.Parse(await File.ReadAllTextAsync(scopePath));var plan=scope.RootElement;
        var run=plan.GetProperty("runId").GetString()!;
        if(!Regex.IsMatch(run,@"\A[a-f0-9]{32}\z")||plan.GetProperty("authorizationStatus").GetString()!="approved"||plan.GetProperty("database").GetString()!="metarill_business_test")
            throw new UnauthorizedAccessException("本轮独占范围无效，未连接数据库。");
        var source="mt_rpf_"+run[..16]+"_source";var target="mt_rpf_"+run[..16]+"_target";
        Assert.Equal(new[]{source,target},plan.GetProperty("tables").EnumerateArray().Select(x=>x.GetString()));
        using var consumed=new FileStream(scopePath+".consumed",FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var address=new MySqlConnectionStringBuilder(options.ConnectionString);
        Assert.Equal("mysql",options.Provider);Assert.Equal("metarill_business_test",options.ExpectedDatabase);Assert.Equal(options.ExpectedDatabase,address.Database);Assert.Equal("deve-server.yinzon.com",address.Server);Assert.Equal(3306u,address.Port);
        using var session=new DatabaseSession(options);
        await using(var check=session.Provider.CreateConnection(options))
        {
            await check.OpenAsync();await using var query=check.CreateCommand();query.CommandText="SELECT @@hostname,@@server_uuid";
            await using var reader=await query.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
            Assert.Equal(plan.GetProperty("expectedHostname").GetString(),reader.GetString(0));Assert.Equal(plan.GetProperty("expectedServerUuid").GetString(),reader.GetString(1));
        }
        var metadata=RelationMaintenanceCatalogTests.Catalog();metadata.ConfigTables.Single(x=>x.Id=="source").TableName=source;metadata.ConfigTables.Single(x=>x.Id=="target").TableName=target;
        var foreign=metadata.ConfigColumns.Single(x=>x.Id=="foreign");foreign.LogicalType="string";foreign.ColumnLength="50";
        metadata.ConfigIndexes=[JsonSerializer.SerializeToElement(new{id="reverse_index",configTableId="source",indexName="ix_reverse_target",isUnique=0,isEnabled=1})];
        metadata.ConfigIndexColumns=new[]{"foreign","source_delete","source_id"}.Select((id,i)=>JsonSerializer.SerializeToElement(new{id="reverse_column_"+i,configIndexId="reverse_index",configColumnId=id,sortNo=i})).ToList();
        var schema=new SchemaService(session,expectedServerUuid:plan.GetProperty("expectedServerUuid").GetString());
        Assert.Empty((await schema.InspectAsync(metadata)).Tables);var preview=await schema.PreviewAsync(metadata);
        Assert.Equal(2,preview.Changes.Count);Assert.All(preview.Changes,x=>Assert.Equal("createTable",x.Kind));
        var seeds=Enumerable.Range(1,132).Select(i=>new Seed(i,54,Target.ToString(CultureInfo.InvariantCulture))).ToList();
        var exact=Target.ToString(CultureInfo.InvariantCulture);
        seeds.AddRange([new(200,54,exact+" "),new(201,54,"0"+exact),new(202,54,string.Concat(exact.Select(c=>(char)('０'+c-'0')))),new(203,54,(Target+1).ToString(CultureInfo.InvariantCulture)),new(204,54,null),new(205,54,""),new(206,55,exact),new(207,54,exact,true),new(208,54,"+"+exact),new(209,54,exact+"\u200b")]);
        seeds.AddRange(Enumerable.Range(300,12).Select(i=>new Seed(i,54,(9007199254750000L+i).ToString(CultureInfo.InvariantCulture))));
        var inserted=new List<Seed>();string? fingerprint=null;var installed=false;var cleaned=false;var passed=false;var explainKey="";
        try
        {
            Assert.True((await schema.ApplyAsync(preview)).Succeeded);installed=true;
            fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(metadata));
            await session.InTransactionAsync(async()=>
            {
                foreach(var row in seeds)
                {
                    await using var command=session.CurrentTransaction!.Connection!.CreateCommand();command.Transaction=session.CurrentTransaction;
                    command.CommandText="INSERT INTO `"+source+"` (`id`,`yz_project_id`,`target_id`,`delete_no`) VALUES (@id,@project,@foreign,@deleted)";
                    Add(command,"id",row.Id);Add(command,"project",row.Project);Add(command,"foreign",row.Foreign);Add(command,"deleted",row.Deleted?new DateTime(2000,1,1):null);
                    Assert.Equal(1,await command.ExecuteNonQueryAsync());
                }
                return true;
            });inserted.AddRange(seeds);
            var catalog=RelationMaintenanceCatalog.Compile(metadata);
            var consumer=Assert.Single(catalog.Mixed.Consumers);var reverse=Assert.Single(consumer.Read.Reverse);
            await VerifyReverseLayoutAsync(session,catalog,source,target);
            // 用过期模型模拟结构差异。仍在反查前拒绝，最终逐行核对合成数据保持。
            foreign.ColumnLength="64";
            try
            {
                var stale=RelationMaintenanceCatalog.Compile(metadata);
                var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync(()=>
                    new ProjectRelationMutationPropagator(session).AfterMutationAsync(stale,target,Target,"54",
                        new HashSet<string>(),allFields:true,refreshSource:false)));
                Assert.Contains(source+".target_id",error.Message);
            }
            finally{foreign.ColumnLength="50";}
            await session.InTransactionAsync(async()=>
            {
                var first=await Lookup(session,consumer.Read,reverse,Target,0);Assert.Equal(Enumerable.Range(1,128).Select(i=>(long)i),first);
                var second=await Lookup(session,consumer.Read,reverse,Target,first[^1]);Assert.Equal(new long[]{129,130,131,132},second);
                Assert.Empty(await Lookup(session,consumer.Read,reverse,Target,second[^1]));
                Assert.Equal(new long[]{206},await Lookup(session,consumer.Read,reverse,Target,0,"55"));
                Assert.Equal(new long[]{203},await Lookup(session,consumer.Read,reverse,Target+1,0));return true;
            });
            // 首事务的锁读已经完成；第二个无关新目标必须在它回滚前完成，不能只以连接存活证明并发。
            using var secondSession=new DatabaseSession(options);var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstTransaction=session.InTransactionAsync(async()=>
            {
                Assert.Empty(await Lookup(session,consumer.Read,reverse,905700000000000001,0));held.SetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(10));return true;
            });
            try
            {
                await held.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.False(firstTransaction.IsCompleted);
                await secondSession.InTransactionAsync(async()=>
                {
                    await using var mode=secondSession.CurrentTransaction!.Connection!.CreateCommand();mode.Transaction=secondSession.CurrentTransaction;
                    mode.CommandText="SELECT @@session.innodb_lock_wait_timeout";var timeout=Convert.ToInt32(await mode.ExecuteScalarAsync(),CultureInfo.InvariantCulture);
                    try
                    {
                        mode.CommandText="SELECT @@session.transaction_isolation";Assert.Equal("REPEATABLE-READ",await mode.ExecuteScalarAsync());
                        mode.CommandText="SET SESSION innodb_lock_wait_timeout=1";await mode.ExecuteNonQueryAsync();
                        Assert.Empty(await Lookup(secondSession,consumer.Read,reverse,905700000000000002,0));return true;
                    }
                    finally{mode.CommandText="SET SESSION innodb_lock_wait_timeout="+timeout.ToString(CultureInfo.InvariantCulture);await mode.ExecuteNonQueryAsync();}
                }).WaitAsync(TimeSpan.FromSeconds(5));Assert.False(firstTransaction.IsCompleted);
            }
            finally{release.TrySetResult();await firstTransaction;}
            await using(var connection=session.Provider.CreateConnection(options))
            {
                await connection.OpenAsync();await using var query=connection.CreateCommand();
                query.CommandText="EXPLAIN SELECT b.`id`"+reverse.From+" WHERE "+consumer.Read.Scope+" AND b.`id`>@after AND "+reverse.TargetPredicate+" ORDER BY b.`id` LIMIT 128 FOR UPDATE";
                Add(query,"after",0L);Add(query,"target_text","905700000000000001");Add(query,"target_id",905700000000000001L);Add(query,"project_scope_number",54L);Add(query,"project_scope_text","54");
                await using var reader=await query.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());explainKey=reader.GetString(reader.GetOrdinal("key"));Assert.Equal("ix_reverse_target",explainKey);
            }
            passed=true;
        }
        finally
        {
            if(installed)
            {
                Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(metadata)));
                await using var connection=session.Provider.CreateConnection(options);await connection.OpenAsync();
                await using(var query=connection.CreateCommand())
                {
                    query.CommandText="SELECT `id`,`yz_project_id`,`target_id`,`delete_no`,`target_name` FROM `"+source+"` ORDER BY `id`";
                    await using var reader=await query.ExecuteReaderAsync();var count=0;
                    while(await reader.ReadAsync())
                    {
                        var row=inserted.Single(x=>x.Id==reader.GetInt64(0));Assert.Equal(row.Project,reader.GetInt64(1));Assert.Equal(row.Foreign,reader.IsDBNull(2)?null:reader.GetString(2));Assert.Equal(row.Deleted,!reader.IsDBNull(3));Assert.True(reader.IsDBNull(4));count++;
                    }
                    Assert.Equal(inserted.Count,count);
                }
                await using(var empty=connection.CreateCommand()){empty.CommandText="SELECT COUNT(*) FROM `"+target+"`";Assert.Equal(0L,Convert.ToInt64(await empty.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}
                foreach(var table in new[]{source,target}){await using var drop=connection.CreateCommand();drop.CommandText="DROP TABLE `"+table+"`";await drop.ExecuteNonQueryAsync();}
                Assert.Empty((await schema.InspectAsync(metadata)).Tables);cleaned=true;
            }
            await File.WriteAllTextAsync(scopePath+".result.json",JsonSerializer.Serialize(new{runId=run,passed,installed,cleaned,rows=inserted.Count,paging=new[]{128,4,0},explainKey,unrelatedTargetsCompletedWhileHeld=passed},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    /// <summary>普通混合关系传播只发一条结构查询，且两表锁、反查和提交位于同一事务。</summary>
    private static async Task VerifyReverseLayoutAsync(DatabaseSession session,RelationMaintenanceCatalog catalog,string source,string target)
    {
        using var observation=DatabaseCommandObservation.Begin(new byte[32]);
        await session.InTransactionAsync(()=>new ProjectRelationMutationPropagator(session).AfterMutationAsync(
            catalog,target,905700000000000001L,"54",new HashSet<string>(),allFields:true,refreshSource:false));
        observation.Dispose();
        var snapshot=observation.Snapshot();
        Assert.True(snapshot.InternallyConsistent);
        var layout=Assert.Single(snapshot.Commands,command=>command.Sql.Contains("information_schema."));
        Assert.StartsWith("/* metarill:reverse-layout */",layout.Sql);
        Assert.Contains("FROM `"+source+"` LIMIT 0",layout.Sql);
        Assert.Contains("FROM `"+target+"` LIMIT 0",layout.Sql);
        var lookup=Assert.Single(snapshot.Commands,command=>command.Sql.StartsWith("SELECT b.`id`",StringComparison.Ordinal));
        Assert.EndsWith("LIMIT 128 FOR UPDATE",lookup.Sql);
        var identity=Assert.Single(snapshot.Commands,command=>command.Sql=="SELECT DATABASE(),@@SESSION.max_allowed_packet");
        var commit=Assert.Single(snapshot.Transactions,transaction=>transaction.Operation==DatabaseTransactionOperation.Commit);
        Assert.True(identity.Number<layout.Number&&layout.Number<lookup.Number);
        Assert.NotNull(commit.Transaction);
        Assert.Equal(commit.Transaction,identity.Transaction);
        Assert.Equal(commit.Transaction,layout.Transaction);
        Assert.Equal(commit.Transaction,lookup.Transaction);
        Assert.All(snapshot.Commands,command=>Assert.Equal(session.Options.ExpectedDatabase,command.Database));
    }

    /// <summary>反射仅访问现有私有执行方法，不为测试新增公共SQL或事务入口。</summary>
    private static Task<List<long>> Lookup(DatabaseSession session,MySqlNonRedundantReadPlan plan,MySqlReverseProjection reverse,long target,long after,string project="54")
        =>(Task<List<long>>)typeof(MySqlMixedRelationMutationExecutor).GetMethod("ReverseIds",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(new MySqlMixedRelationMutationExecutor(session),[plan,reverse,target,project,after,session.CurrentTransaction!,CancellationToken.None])!;
    /// <summary>所有值通过驱动参数；只有已验证的本轮标识符进入SQL结构。</summary>
    private static void Add(DbCommand command,string name,object? value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}
}
