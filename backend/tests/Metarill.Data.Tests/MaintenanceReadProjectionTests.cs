using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>记录真实读取器生成的命令，禁止COUNT及无关数组补名；替身不承担真实MySQL锁与授权验收。</summary>
public sealed partial class MaintenanceReadProjectionTests
{
    /// <summary>可变调用集合不能修改投影，原计划与完整物理依赖保持不变。</summary>
    [Fact]public void Projection_is_immutable_and_does_not_reduce_scope_or_schema_proof()
    {
        var plan=Plan();var before=plan.Outputs.ToArray();var fields=new HashSet<string>(StringComparer.Ordinal){"single_name"};var projected=Project(plan,fields);fields.Clear();Assert.Equal(new[]{"id","single_name"},projected.Outputs);Assert.Equal(before,plan.Outputs);foreach(var field in new[]{"Scope","From","ProjectId","Expressions","Types","PhysicalReferences","SourceDependencies","Reverse","Crosses","Constants","MultiProjections"})Assert.Same(Read(plan,field),Read(projected,field));
    }
    /// <summary>未知输出、SQL形态及大小写伪装不能获得额外读取字段。</summary>
    [Theory][InlineData("missing")][InlineData("id;DROP TABLE")][InlineData("SINGLE_NAME")][InlineData("yz_project_id")]
    public void Unknown_output_cannot_expand_declared_cache_read(string field)=>Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Project(Plan(),new HashSet<string>{field})).InnerException);
    /// <summary>仅刷新标量时只发一条带当前锁的来源投影，不能触发COUNT或数组目标查询。</summary>
    [Fact]public async Task Scalar_refresh_does_not_query_unrelated_multiple_names_or_total()
    {
        var provider=new RecordingProvider(false);using var session=new DatabaseSession(Options,null,provider);var plan=Project(Plan(),new HashSet<string>{"single_name"});await session.InTransactionAsync(async()=>{var rows=await new MySqlNonRedundantReader(session).ReadCurrentRowsAsync(plan,[new("id",FixedReadComparison.In,new object?[]{101L})],[new("id")],128,projectId:ProjectId);Assert.Equal("当前名称",Assert.Single(rows)["single_name"]);Assert.Equal(new[]{"id","single_name"},rows[0].Keys);return true;});var sql=Assert.Single(provider.Commands);Assert.DoesNotContain("COUNT(",sql);Assert.DoesNotContain("metarill_multi_input",sql);Assert.DoesNotContain(" AS `shared_names`",sql);Assert.Contains("b.`yz_project_id`=@project_scope_number",sql);Assert.EndsWith(" FOR SHARE",sql);Assert.Equal(1,provider.Commits);
    }
    /// <summary>本次确实刷新数组时仍逐项补名，保留顺序和空位，而非为省查询丢弃所需目标。</summary>
    [Fact]public async Task Multiple_refresh_keeps_required_target_current_read()
    {
        var provider=new RecordingProvider(true);using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var rows=await new MySqlNonRedundantReader(session).ReadCurrentRowsAsync(Project(Plan(),new HashSet<string>{"shared_names"}),[new("id",FixedReadComparison.In,new object?[]{101L})],[new("id")],128,projectId:ProjectId);Assert.Equal(new string?[]{"甲",null,"乙"},JsonSerializer.Deserialize<string[]>((string)Assert.Single(rows)["shared_names"]!));Assert.DoesNotContain("single_name",rows[0].Keys);return true;});Assert.Equal(2,provider.Commands.Count);Assert.All(provider.Commands,sql=>{Assert.DoesNotContain("COUNT(",sql);Assert.EndsWith(" FOR SHARE",sql);});Assert.DoesNotContain(" LEFT JOIN ",provider.Commands[0]);Assert.Contains("multi_value.`id` IN",provider.Commands[1]);Assert.Equal(1,provider.Commits);
    }
    /// <summary>裁剪不产生普通快照降级，缺少真实调用事务时在开连接前拒绝。</summary>
    [Fact]public async Task Current_projection_requires_existing_transaction()
    {
        var provider=new RecordingProvider(false);using var session=new DatabaseSession(Options,null,provider);await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadCurrentRowsAsync(Project(Plan(),new HashSet<string>{"single_name"}),[],[new("id")],128));Assert.Empty(provider.Commands);Assert.Equal(0,provider.Opens);
    }
    private const string ProjectId="9007199254740993";
    private static DatabaseOptions Options=>new(){Provider="mysql",ExpectedDatabase="projection_offline",ConnectionString="Server=127.0.0.1;Port=1;Database=projection_offline;User ID=unused;Password=unused"};
    /// <summary>同一来源含标量与数据库复选，全局目标证明沿现有权威计划编译。</summary>
    private static MySqlNonRedundantReadPlan Plan()
    {
        var c=GlobalRelationCachePlanTests.Catalog("MultiCheckBoxDatabase",true);c.ConfigColumns.Add(new(){Id="17",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="single_id",LogicalType="int64",LinkConfigTableId="2",ControlKind="List",ControlDetail="{\"tableId\":\"2\",\"pageId\":\"21\",\"nameField\":\"name\"}"});c.ConfigColumns.Add(new(){Id="18",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="single_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("single.name")}});var catalog=RelationMaintenanceCatalog.Compile(c,[GlobalRelationCachePlanTests.Definition("MultiCheckBoxDatabase")]);var plans=(System.Collections.IEnumerable)Read(catalog,"Plans");var source=plans.Cast<object>().Single(x=>(string)Read(x,"Key")=="1");var table=Read(source,"Value");return (MySqlNonRedundantReadPlan)Read(table,"Read");
    }
    /// <summary>只读内部投影入口，不为测试向HTTP或公共API暴露动态字段集合。</summary>
    private static MySqlNonRedundantReadPlan Project(MySqlNonRedundantReadPlan plan,IReadOnlySet<string> fields)=>(MySqlNonRedundantReadPlan)typeof(MySqlNonRedundantReadPlan).GetMethod("ForCacheFields",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plan,[fields])!;
    /// <summary>观察已经编译的不可变计划，不重新解释元数据。</summary>
    private static object Read(object value,string property)=>value.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(value)!;
    /// <summary>只记录本测试读取语句，任何写入、COUNT或非当前读都失败。</summary>
    private sealed class RecordingProvider(bool multiple,string scalarName="single_name"):IDatabaseProvider
    {
        internal readonly List<string> Commands=[];internal int Commits,Opens;public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this);
        /// <summary>响应固定已知投影，未知SQL直接报错，避免宽松替身掩盖多余查询。</summary>
        internal DbDataReader Execute(string sql,DbTransaction? transaction)
        {
            Assert.NotNull(transaction);Assert.EndsWith(" FOR SHARE",sql);Assert.DoesNotContain("COUNT(",sql);Commands.Add(sql);var table=new DataTable();if(sql.StartsWith("SELECT multi_value.`id`,",StringComparison.Ordinal)){Assert.True(multiple);table.Columns.Add("id",typeof(long));table.Columns.Add("name",typeof(string));table.Rows.Add(1L,"甲");table.Rows.Add(2L,"乙");}else{Assert.StartsWith("SELECT b.`id` AS `id`,",sql);table.Columns.Add("id",typeof(long));if(multiple){Assert.Contains("NULL AS `shared_names`",sql);Assert.DoesNotContain(" AS `single_name`",sql);table.Columns.Add("shared_names",typeof(string));table.Columns.Add("metarill_multi_input_0",typeof(string));table.Rows.Add(101L,DBNull.Value,"[\"1\",\"3\",\"2\"]");}else{Assert.DoesNotContain("shared_names",sql);Assert.Contains(" AS `"+scalarName+"`",sql);table.Columns.Add(scalarName,typeof(string));table.Rows.Add(101L,"当前名称");}}return table.CreateDataReader();
        }
    }
    /// <summary>离线连接只用于形成真实事务对象，不向网络建连。</summary>
    private sealed class Connection(RecordingProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"projection_offline";public override string DataSource=>"offline";public override string ServerVersion=>"8.4";public override ConnectionState State=>state;public override void Open(){provider.Opens++;state=ConnectionState.Open;}public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this,provider);protected override DbCommand CreateDbCommand()=>new Command(this,provider);
    }
    /// <summary>命令必须沿同一事务，成功提交单独计数。</summary>
    private sealed class Transaction(DbConnection connection,RecordingProvider provider):DbTransaction{public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;protected override DbConnection DbConnection=>connection;public override void Commit()=>provider.Commits++;public override void Rollback(){}}
    /// <summary>参数使用驱动类型，读取仅交给固定记录器；禁止执行写入或标量COUNT。</summary>
    private sealed class Command(DbConnection connection,RecordingProvider provider):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override void Cancel(){}public override void Prepare(){}public override object? ExecuteScalar()=>throw new NotSupportedException("禁止多余COUNT");public override int ExecuteNonQuery()=>throw new NotSupportedException("投影读取禁止写入");protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Execute(CommandText,DbTransaction);
    }
}
