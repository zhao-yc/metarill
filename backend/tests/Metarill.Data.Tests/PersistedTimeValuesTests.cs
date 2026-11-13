using System.Data;
using System.Data.Common;
using Metarill.Core.Contracts;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>执行真实MySQL适配器方法，命令/记录流离线提供；不冒充真实写入或锁验收。</summary>
public sealed class PersistedTimeValuesTests
{
    /// <summary>同一事务一次SELECT固定列，只绑定long主键，保持微秒、负总小时和null。</summary>
    [Fact]
    public async Task Persisted_times_are_read_once_in_writer_transaction()
    {
        using var connection=new Connection([["12:34:56.123456",TimeSpan.FromHours(-30),DBNull.Value]]);connection.Open();using var transaction=connection.BeginTransaction();
        var values=await new MySqlDatabaseProvider().ReadPersistedTimeValuesAsync(transaction,"clock_record",9007199254740993L,["clock","duration","optional_clock"]);
        Assert.Equal(ClockTimeValue.ParseText("12:34:56.123456"),values[0]);Assert.Equal(TimeSpan.FromHours(-30),values[1]);Assert.Null(values[2]);
        var command=Assert.Single(connection.Commands);Assert.Same(transaction,command.Transaction);
        Assert.Equal("SELECT `clock`,`duration`,`optional_clock` FROM `clock_record` WHERE `id`=@persisted_time_id",command.CommandText);
        var parameter=Assert.Single(command.Parameters.Cast<DbParameter>());Assert.Equal(9007199254740993L,Assert.IsType<long>(parameter.Value));Assert.Equal("persisted_time_id",parameter.ParameterName);
        Assert.Equal(1,connection.Opens);Assert.Equal(1,connection.Executions);Assert.Equal(0,((Transaction)transaction).Commits);Assert.Equal(0,((Transaction)transaction).Rollbacks);
    }
    /// <summary>未知/重复列和非法表或主键在创建命令前拒绝，不能拼接SQL或误读其它记录。</summary>
    [Theory][InlineData("id")][InlineData("table")][InlineData("column")][InlineData("duplicate")][InlineData("empty")][InlineData("null")]
    public async Task Invalid_fixed_read_definition_does_not_execute(string kind)
    {
        using var connection=new Connection([]);connection.Open();using var transaction=connection.BeginTransaction();
        var id=kind=="id"?0:1;var table=kind=="table"?"clock_record;DROP TABLE x":"clock_record";
        IReadOnlyList<string> columns=kind switch{"column"=>["clock;DROP"],"duplicate"=>["clock","CLOCK"],"empty"=>[],"null"=>null!,_=>["clock"]};
        await Assert.ThrowsAnyAsync<Exception>(()=>new MySqlDatabaseProvider().ReadPersistedTimeValuesAsync(transaction,table,id,columns));
        Assert.Empty(connection.Commands);Assert.Equal(0,connection.Executions);
    }
    /// <summary>取消先于任何命令，底层不擅自开新连接或提交原事务。</summary>
    [Fact]
    public async Task Cancelled_read_does_not_execute()
    {
        using var connection=new Connection([]);connection.Open();using var transaction=connection.BeginTransaction();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new MySqlDatabaseProvider().ReadPersistedTimeValuesAsync(transaction,"clock_record",1,["clock"],new CancellationToken(true)));Assert.Empty(connection.Commands);
    }
    /// <summary>缺行、多行、缺列、类型/文本/范围损坏都明确失败；调用者继续负责事务回滚。</summary>
    [Theory][InlineData("missing")][InlineData("multiple")][InlineData("width")][InlineData("type")][InlineData("text")][InlineData("emptyText")][InlineData("range")]
    public async Task Invalid_physical_result_is_rejected(string kind)
    {
        object?[][] rows=kind switch{"missing"=>[],"multiple"=>[[TimeSpan.Zero],[TimeSpan.Zero]],"width"=>[[TimeSpan.Zero,TimeSpan.Zero]],"type"=>[[3]],"text"=>[["bad-time"]],"emptyText"=>[[""]],_=>[[TimeSpan.FromHours(839)]]};
        using var connection=new Connection(rows,kind=="width"?2:1);connection.Open();using var transaction=connection.BeginTransaction();
        await Assert.ThrowsAnyAsync<Exception>(()=>new MySqlDatabaseProvider().ReadPersistedTimeValuesAsync(transaction,"clock_record",1,["clock"]));Assert.Equal(1,connection.Executions);Assert.Equal(0,((Transaction)transaction).Commits);
    }
    /// <summary>连接只生成可观测命令，不访问网络或隐式开启附加事务。</summary>
    private sealed class Connection(object?[][] rows,int width=3):DbConnection
    {
        public List<Command> Commands{get;}=[];public int Opens,Executions;private ConnectionState state;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"8.4";public override ConnectionState State=>state;
        public override void Open(){Opens++;state=ConnectionState.Open;}public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string value)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this);
        protected override DbCommand CreateDbCommand(){var command=new Command(this);Commands.Add(command);return command;}
        /// <summary>固定结果流只允许读取，禁止测试替身替产品计算期望精度。</summary>
        public DbDataReader Execute()
        {
            Executions++;var table=new DataTable();for(var i=0;i<width;i++)table.Columns.Add("column"+i,typeof(object));foreach(var row in rows)table.Rows.Add(row);return table.CreateDataReader();
        }
    }
    /// <summary>记录器不会由被测回读方法提交或回滚。</summary>
    private sealed class Transaction(DbConnection connection):DbTransaction
    {
        public int Commits,Rollbacks;public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;protected override DbConnection DbConnection=>connection;
        public override void Commit()=>Commits++;public override void Rollback()=>Rollbacks++;
    }
    /// <summary>使用实际驱动参数容器，执行路径只能返回只读结果流。</summary>
    private sealed class Command(Connection owner):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}=owner;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;protected override DbParameter CreateDbParameter()=>new MySqlParameter();
        public override void Cancel(){}public override void Prepare(){}public override int ExecuteNonQuery()=>throw new NotSupportedException("回读禁止写入。");public override object ExecuteScalar()=>throw new NotSupportedException("回读禁止额外标量。");
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>owner.Execute();
    }
}
