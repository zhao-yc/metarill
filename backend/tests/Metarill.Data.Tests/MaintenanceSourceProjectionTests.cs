using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Metarill.Data.Relations;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

public sealed partial class MaintenanceReadProjectionTests
{
    /// <summary>只返回受影响缓存，仍读取未选多值的预算ID，保留项目、软删、主键范围和行锁。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Source_lock_returns_only_selected_cache_with_original_scope(bool textProject)
    {
        var provider=new SourceProvider(["id","single_name","shared_ids"]);using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var rows=await LockedSources(session,SourceTable(textProject),new HashSet<string>{"single_name"},[101L],9);Assert.Equal(new[]{"id","single_name","shared_ids"},Assert.Single(rows).Keys);Assert.Equal(101L,rows[0]["id"]);Assert.DoesNotContain("shared_names",rows[0].Keys);return true;});var sql=Assert.Single(provider.Commands);Assert.Contains("WHERE `yz_project_id`=@project AND `delete_no` IS NULL AND `id`>@after AND `id` IN (@owned0)",sql);Assert.EndsWith("ORDER BY `id` LIMIT 128 FOR UPDATE",sql);Assert.Equal(textProject?(object)ProjectId:long.Parse(ProjectId),provider.Parameters[0]["project"]);Assert.Equal(9L,provider.Parameters[0]["after"]);Assert.Equal(101L,provider.Parameters[0]["owned0"]);Assert.Equal(1,provider.Commits);
    }
    /// <summary>整表维护及来源自身刷新继续默认完整缓存集合，不能因优化省略历史值比较。</summary>
    [Fact]public async Task Full_source_lock_keeps_all_caches_and_budget_inputs()
    {
        var provider=new SourceProvider(["id","shared_names","single_name","shared_ids"]);using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var rows=await LockedSources(session,SourceTable(),null);Assert.Equal(new[]{"id","shared_names","single_name","shared_ids"},Assert.Single(rows).Keys);return true;});Assert.DoesNotContain(" IN (",Assert.Single(provider.Commands));
    }
    /// <summary>空事件集合也不丢历史预算输入，真实调用方在空事件时直接跳过。
    /// </summary>
    [Fact]public async Task Empty_source_projection_keeps_id_and_array_budget_only()
    {
        var provider=new SourceProvider(["id","shared_ids"]);using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var rows=await LockedSources(session,SourceTable(),new HashSet<string>());Assert.Equal(new[]{"id","shared_ids"},Assert.Single(rows).Keys);return true;});Assert.Single(provider.Commands);
    }
    /// <summary>多值名称选择及全缓存选择保持编译字段顺序，不受调用集合顺序影响。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Source_projection_preserves_declared_order(bool all)
    {
        string[] expected=all?["id","shared_names","single_name","shared_ids"]:["id","shared_names","shared_ids"];var provider=new SourceProvider(expected);using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var selected=new HashSet<string>{"shared_names"};if(all){selected.Clear();selected.Add("single_name");selected.Add("shared_names");}var rows=await LockedSources(session,SourceTable(),selected);Assert.Equal(expected,Assert.Single(rows).Keys);return true;});
    }
    /// <summary>不允许将主键、项目字段、大小写伪装、未知字段或SQL片段作为缓存名。</summary>
    [Theory][InlineData("id")][InlineData("yz_project_id")][InlineData("SINGLE_NAME")][InlineData("missing")][InlineData("single_name;DROP TABLE")]
    public async Task Invalid_source_cache_projection_rejects_before_command(string field)
    {
        var provider=new SourceProvider([]);using var session=new DatabaseSession(Options,null,provider);await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync(async()=>{await LockedSources(session,SourceTable(),new HashSet<string>{field});return true;}));Assert.Empty(provider.Commands);Assert.Equal(0,provider.Commits);Assert.Equal(1,provider.Rollbacks);
    }
    /// <summary>执行中修改调用集合不能改变已形成的SQL列与行解码顺序。</summary>
    [Fact]public async Task Source_column_snapshot_survives_caller_set_mutation()
    {
        var selected=new HashSet<string>{"single_name"};var provider=new SourceProvider(["id","single_name","shared_ids"]){OnRead=selected.Clear};using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var rows=await LockedSources(session,SourceTable(),selected);Assert.Empty(selected);Assert.Equal(new[]{"id","single_name","shared_ids"},Assert.Single(rows).Keys);return true;});
    }
    /// <summary>保留真实数据库空值，不把未选择列伪造成null并带入写回比较。</summary>
    [Fact]public async Task Selected_null_is_distinct_from_absent_cache()
    {
        var provider=new SourceProvider(["id","single_name","shared_ids"]);provider.Values["single_name"]=null;using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var row=Assert.Single(await LockedSources(session,SourceTable(),new HashSet<string>{"single_name"}));Assert.True(row.ContainsKey("single_name"));Assert.Null(row["single_name"]);Assert.False(row.ContainsKey("shared_names"));return true;});
    }
    /// <summary>未选名称的历史多值预算仍检查；超额在当前读／写入前拒绝，合法上限继续执行。</summary>
    [Theory][InlineData(5000,false)][InlineData(5001,true)]
    public async Task Unselected_array_budget_is_never_bypassed(int count,bool reject)
    {
        var provider=new SourceProvider(["id","single_name","shared_ids"]){AllowCurrent=true};provider.Values["shared_ids"]=JsonSerializer.Serialize(Enumerable.Range(1,count).Select(x=>x.ToString()).ToArray());using var session=new DatabaseSession(Options,null,provider);
        async Task Execute(){await session.InTransactionAsync(async()=>{var table=SourceTable();var selected=new HashSet<string>{"single_name"};var rows=await LockedSources(session,table,selected,[101L]);var task=(Task)typeof(RelationMaintenanceExecutor).GetMethod("RefreshLockedAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(new RelationMaintenanceExecutor(session),[table,rows,ProjectId,selected,CancellationToken.None])!;await task;Assert.Empty(((System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!).Cast<object>());Assert.Equal(provider.Values["shared_ids"],rows[0]["shared_ids"]);return true;});}
        if(reject){var error=await Assert.ThrowsAsync<InvalidOperationException>(Execute);Assert.Contains("预算",error.Message);Assert.Single(provider.Commands);Assert.Equal(1,provider.Rollbacks);}else{await Execute();Assert.Equal(2,provider.Commands.Count);Assert.Equal(1,provider.Commits);Assert.DoesNotContain("shared_names",provider.Commands[1]);}Assert.All(provider.Commands,sql=>Assert.DoesNotContain("COUNT(",sql));
    }
    /// <summary>调用生产来源行锁方法，表对象只来自冻结维护目录。</summary>
    private static Task<List<Dictionary<string,object?>>> LockedSources(DatabaseSession session,object table,IReadOnlySet<string>? allowed,IReadOnlyList<long>? ids=null,long after=0)=>(Task<List<Dictionary<string,object?>>>)typeof(RelationMaintenanceExecutor).GetMethod("Sources",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(new RelationMaintenanceExecutor(session),[table,ProjectId,after,session.CurrentTransaction!,CancellationToken.None,ids,allowed])!;
    /// <summary>严格记录原始行锁SQL及参数，不连接网络；结果只含事先声明的列。</summary>
    private sealed class SourceProvider(string[] columns):IDatabaseProvider
    {
        internal readonly List<string> Commands=[];internal readonly List<Dictionary<string,object?>> Parameters=[];internal readonly Dictionary<string,object?> Values=new(){{"id",101L},{"single_name","当前名称"},{"shared_names",new string('名',200000)},{"shared_ids","[\"1\",\"2\"]"}};internal int Commits,Rollbacks;internal bool AllowCurrent;internal Action? OnRead;public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options)=>new SourceConnection(this);
        /// <summary>不接受宽SELECT、非当前读、额外查询或写入，防止替身掩盖回归。</summary>
        internal DbDataReader Execute(DbCommand command,DbTransaction? transaction)
        {
            Assert.NotNull(transaction);var sql=command.CommandText;Assert.DoesNotContain("COUNT(",sql);Commands.Add(sql);Parameters.Add(command.Parameters.Cast<DbParameter>().ToDictionary(x=>x.ParameterName,x=>(object?)x.Value));var data=new DataTable();if(sql.EndsWith(" FOR UPDATE",StringComparison.Ordinal)){Assert.StartsWith("SELECT "+string.Join(",",columns.Select(x=>"`"+x+"`"))+" FROM `global_cache_source` WHERE ",sql);OnRead?.Invoke();foreach(var name in columns)data.Columns.Add(name,Values[name]?.GetType()??typeof(string));data.Rows.Add(columns.Select(name=>Values[name]??DBNull.Value).ToArray());}else{Assert.True(AllowCurrent);Assert.EndsWith(" FOR SHARE",sql);Assert.StartsWith("SELECT b.`id` AS `id`,",sql);Assert.Contains(" AS `single_name`",sql);Assert.DoesNotContain("shared_names",sql);data.Columns.Add("id",typeof(long));data.Columns.Add("single_name",typeof(string));data.Rows.Add(101L,"当前名称");}return data.CreateDataReader();
        }
    }
    /// <summary>离线连接只创建同一事务，不执行真实数据库操作。</summary>
    private sealed class SourceConnection(SourceProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"projection_offline";public override string DataSource=>"offline";public override string ServerVersion=>"8.4";public override ConnectionState State=>state;public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new SourceTransaction(this,provider);protected override DbCommand CreateDbCommand()=>new SourceCommand(this,provider);
    }
    /// <summary>独立核验提交与回滚，异常不能被静默提交。</summary>
    private sealed class SourceTransaction(DbConnection connection,SourceProvider provider):DbTransaction{public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;protected override DbConnection DbConnection=>connection;public override void Commit()=>provider.Commits++;public override void Rollback()=>provider.Rollbacks++;}
    /// <summary>驱动参数容器配合固定读取器，禁止标量统计或任何DML。</summary>
    private sealed class SourceCommand(DbConnection connection,SourceProvider provider):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override void Cancel(){}public override void Prepare(){}public override object? ExecuteScalar()=>throw new NotSupportedException("来源锁读不执行统计");public override int ExecuteNonQuery()=>throw new NotSupportedException("离线锁读禁止写入");protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Execute(this,DbTransaction);
    }
}
