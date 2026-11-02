using System.Collections;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using FreeSql.Internal.CommonProvider;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>观察器沿实际DbCommand包装执行，网络／数据库为明确替身，不冒充真实MySQL或性能。</summary>
public sealed class DatabaseCommandObservationTests
{
    private static byte[] Key()=>Enumerable.Repeat((byte)7,32).ToArray();
    /// <summary>六执行入口各调用原驱动一次，返回原结果／reader，不消费结果游标。</summary>
    [Theory][InlineData(false,"scalar")][InlineData(false,"write")][InlineData(false,"reader")][InlineData(true,"scalar")][InlineData(true,"write")][InlineData(true,"reader")]
    public async Task All_six_entries_preserve_results_and_command_identity(bool asynchronous,string kind)
    {
        using var reader=new DataTable().CreateDataReader();using var original=new Command{Reader=reader};using var command=new MySqlFloatingCommand(original);var trace=DatabaseCommandObservation.Begin(Key());
        if(kind=="scalar")Assert.Equal("private-scalar-value",asynchronous?await command.ExecuteScalarAsync():command.ExecuteScalar());
        else if(kind=="write")Assert.Equal(9,asynchronous?await command.ExecuteNonQueryAsync():command.ExecuteNonQuery());
        else{Assert.Same(reader,asynchronous?await command.ExecuteReaderAsync():command.ExecuteReader());Assert.False(reader.IsClosed);}
        trace.Dispose();var value=trace.Snapshot();Assert.True(value.InternallyConsistent);Assert.Equal(1,original.Calls);Assert.Equal(1,value.Started);Assert.Equal(1,value.Completed);
        var entry=Assert.Single(value.Commands);Assert.True(entry.DriverCalled);Assert.True(entry.Succeeded);Assert.Equal("SELECT @value",entry.Sql);Assert.True(entry.StartedTimestamp<=entry.DriverTimestamp&&entry.DriverTimestamp<=entry.FinishedTimestamp);
        Assert.Equal(kind=="write"?9:null,entry.AffectedRows);Assert.DoesNotContain("private-scalar-value",JsonSerializer.Serialize(value));
    }
    /// <summary>未开启时连观察专用元数据都不访问；开启后读取失败只污染证据，原操作仍成功。</summary>
    [Fact]
    public void Disabled_path_and_metadata_failure_never_change_original_execution()
    {
        using var original=new Command{ThrowMetadata=true};using var command=new MySqlFloatingCommand(original);Assert.Equal(9,command.ExecuteNonQuery());
        var trace=DatabaseCommandObservation.Begin(Key());Assert.Equal(9,command.ExecuteNonQuery());trace.Dispose();Assert.Equal(2,original.Calls);
        Assert.Contains(DatabaseObservationProblem.MetadataFailure,trace.Snapshot().Problems);Assert.False(trace.Snapshot().InternallyConsistent);
    }
    /// <summary>只保存最终绑定值的HMAC与真实驱动类型，不保留可变参数／原值／标量结果。</summary>
    [Fact]
    public void Actual_parameter_types_and_keyed_values_are_immutable_and_not_plaintext()
    {
        const string secret="private-parameter-174385";using var original=new Command();var parameter=new MySqlParameter("value",secret);original.Parameters.Add(parameter);
        var bytes=Key();var trace=DatabaseCommandObservation.Begin(bytes);Array.Clear(bytes);using(var command=new MySqlFloatingCommand(original))Assert.Equal(9,command.ExecuteNonQuery());parameter.Value="changed-after-call";trace.Dispose();
        var entry=Assert.Single(trace.Snapshot().Commands);var captured=Assert.Single(entry.Parameters);Assert.Equal("String",captured.DbType);Assert.Equal("VarChar",captured.ProviderType);Assert.Equal(typeof(string).FullName,captured.ClrType);
        Assert.Equal(Convert.ToHexStringLower(HMACSHA256.HashData(Key(),Encoding.UTF8.GetBytes("string\0"+secret))),captured.ValueHash);
        var text=JsonSerializer.Serialize(trace.Snapshot());Assert.DoesNotContain(secret,text);Assert.DoesNotContain("changed-after-call",text);Assert.False(captured.ValueHash!.Contains(secret,StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(()=>((IList<DatabaseParameterObservation>)entry.Parameters).Clear());Assert.Throws<NotSupportedException>(()=>((IList<DatabaseCommandEntry>)trace.Snapshot().Commands).Clear());
    }
    /// <summary>精确大整数／decimal／浮点位型、null与空串区分；未知对象不得调用ToString。</summary>
    [Fact]
    public void Scalar_proofs_keep_precision_null_and_unsupported_values_distinct()
    {
        using var original=new Command();foreach(var value in new object?[]{9007199254740993L,0.1234567890123456789012345678m,Math.ScaleB(1d,-958),"",null,DBNull.Value,new byte[]{0,255},new Bomb()})original.Parameters.Add(new Parameter{ParameterName="p"+original.Parameters.Count,Value=value});
        var trace=DatabaseCommandObservation.Begin(Key());using(var command=new MySqlFloatingCommand(original))command.ExecuteNonQuery();trace.Dispose();var parameters=Assert.Single(trace.Snapshot().Commands).Parameters;
        Assert.NotEqual(parameters[0].ValueHash,parameters[1].ValueHash);Assert.NotEqual(parameters[3].ValueHash,parameters[4].ValueHash);Assert.Equal(parameters[4].ValueHash,parameters[5].ValueHash);Assert.Equal("double-bits",parameters[2].ValueKind);
        Assert.Null(parameters[7].ValueHash);Assert.Contains(DatabaseObservationProblem.UnsupportedValue,trace.Snapshot().Problems);Assert.False(trace.Snapshot().InternallyConsistent);Assert.Equal(1,original.Calls);
    }
    /// <summary>跨语言值证明使用无指数的精确decimal文本，相同数学值不依赖CLR尾零格式。</summary>
    [Fact]
    public void Decimal_value_proof_has_a_versioned_plain_canonical_form()
    {
        var values=new decimal[]{0.0000000000000000000000000001m,decimal.MaxValue,-0.0000000000000000000000000001m,1.00m,1m,0m};
        var texts=new[]{"0.0000000000000000000000000001","79228162514264337593543950335","-0.0000000000000000000000000001","1","1","0"};
        using var original=new Command();foreach(var value in values)original.Parameters.Add(new Parameter{ParameterName="p"+original.Parameters.Count,Value=value});
        var trace=DatabaseCommandObservation.Begin(Key());using(var command=new MySqlFloatingCommand(original))command.ExecuteNonQuery();trace.Dispose();var captured=Assert.Single(trace.Snapshot().Commands).Parameters;
        Assert.Equal("metarill-sql-values-v1",trace.Snapshot().ValueProofFormat);Assert.True(trace.Snapshot().InternallyConsistent);
        for(var i=0;i<texts.Length;i++)Assert.Equal(Convert.ToHexStringLower(HMACSHA256.HashData(Key(),Encoding.UTF8.GetBytes("decimal\0"+texts[i]))),captured[i].ValueHash);
    }
    /// <summary>取消／原浮点拒绝发生在驱动调用前，不能伪记为已执行SQL。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Preparation_failure_or_cancellation_is_recorded_without_driver_entry(bool canceled)
    {
        using var original=new MySqlCommand("SELECT @value");original.Parameters.AddWithValue("value",canceled?1d:double.NaN);using var command=MySqlFloatingCommand.Wrap(original);var trace=DatabaseCommandObservation.Begin(Key());
        if(canceled){using var token=new CancellationTokenSource();token.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>command.ExecuteScalarAsync(token.Token));}
        else Assert.Equal("浮点数据库参数必须为有限值。",Assert.Throws<InvalidOperationException>(()=>command.ExecuteScalar()).Message);
        trace.Dispose();var entry=Assert.Single(trace.Snapshot().Commands);Assert.False(entry.DriverCalled);Assert.False(entry.Succeeded);Assert.Equal(canceled,entry.Canceled);Assert.False(original.IsPrepared);
    }
    /// <summary>原异常对象和次数必须保持；证据不含异常正文。</summary>
    [Fact]
    public void Original_failure_is_not_replaced_or_retried()
    {
        var failure=new InvalidOperationException("private-error-65918");using var original=new Command{Failure=failure};using var command=new MySqlFloatingCommand(original);var trace=DatabaseCommandObservation.Begin(Key());Assert.Same(failure,Assert.Throws<InvalidOperationException>(()=>command.ExecuteNonQuery()));trace.Dispose();
        Assert.Equal(1,original.Calls);var entry=Assert.Single(trace.Snapshot().Commands);Assert.True(entry.DriverCalled);Assert.False(entry.Succeeded);Assert.Equal(failure.GetType().FullName,entry.ErrorType);Assert.DoesNotContain(failure.Message,JsonSerializer.Serialize(trace.Snapshot()));
    }
    /// <summary>超出有界预算不影响原执行，但明确拒绝完整性，不能丢样本后报告通过。</summary>
    [Fact]
    public void Overflow_preserves_execution_and_makes_the_capture_incomplete()
    {
        var trace=DatabaseCommandObservation.Begin(Key(),1);using var original=new Command();using var command=new MySqlFloatingCommand(original);Assert.Equal(9,command.ExecuteNonQuery());Assert.Equal(9,command.ExecuteNonQuery());trace.Dispose();
        Assert.Equal(2,original.Calls);Assert.Equal(2,trace.Snapshot().Completed);Assert.Single(trace.Snapshot().Commands);Assert.Contains(DatabaseObservationProblem.CommandLimit,trace.Snapshot().Problems);Assert.False(trace.Snapshot().InternallyConsistent);
    }
    /// <summary>元数据／值超限不改变原结果；敏感大值不进入摘要或证据。</summary>
    [Fact]
    public void Oversize_fields_are_explicitly_rejected_without_changing_the_command()
    {
        using var original=new Command{Text=new string('s',32769)};for(var i=0;i<257;i++)original.Parameters.Add(new Parameter{ParameterName="p"+i,Value=new string('x',16385)});
        var trace=DatabaseCommandObservation.Begin(Key());using(var command=new MySqlFloatingCommand(original))Assert.Equal(9,command.ExecuteNonQuery());trace.Dispose();
        Assert.Equal(32769,original.CommandText.Length);var value=trace.Snapshot();Assert.Equal(32768,Assert.Single(value.Commands).Sql.Length);Assert.Equal(256,value.Commands[0].Parameters.Count);
        foreach(var problem in new[]{DatabaseObservationProblem.SqlLength,DatabaseObservationProblem.ParameterLimit,DatabaseObservationProblem.ValueLimit})Assert.Contains(problem,value.Problems);Assert.False(value.InternallyConsistent);
    }
    /// <summary>并行调用链彼此隔离，await后上下文仍保持，不收编其它请求SQL。</summary>
    [Fact]
    public async Task Parallel_async_chains_are_isolated()
    {
        var values=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>Task.Run(async()=>
        {
            var trace=DatabaseCommandObservation.Begin(Key());await Task.Yield();using var original=new Command{Text="SELECT "+i};using(var command=new MySqlFloatingCommand(original))await command.ExecuteScalarAsync();trace.Dispose();return trace.Snapshot();
        })));
        Assert.Equal(8,values.Select(value=>Assert.Single(value.Commands).Sql).Distinct().Count());Assert.All(values,value=>Assert.True(value.InternallyConsistent));using var next=DatabaseCommandObservation.Begin(Key());
    }
    /// <summary>父scope结束后派生任务不能继续收集旧密钥证据，迟到执行只记不完整且仍执行原命令。</summary>
    [Fact]
    public async Task Closed_inherited_scope_is_not_reused_by_late_child()
    {
        var signal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var trace=DatabaseCommandObservation.Begin(Key());
        var task=Task.Run(async()=>{await signal.Task;using var original=new Command();using var command=new MySqlFloatingCommand(original);return command.ExecuteNonQuery();});
        trace.Dispose();signal.SetResult();Assert.Equal(9,await task);Assert.Empty(trace.Snapshot().Commands);Assert.Contains(DatabaseObservationProblem.LateCommand,trace.Snapshot().Problems);
    }
    /// <summary>关闭时原驱动仍在运行必须显式不完整；等待原结果不重试、不取消该命令。</summary>
    [Fact]
    public async Task Closing_while_a_driver_call_is_pending_never_claims_complete()
    {
        var gate=new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);using var original=new Command{ScalarCompletion=gate};using var command=new MySqlFloatingCommand(original);var trace=DatabaseCommandObservation.Begin(Key());
        var pending=command.ExecuteScalarAsync();Assert.False(pending.IsCompleted);trace.Dispose();Assert.Contains(DatabaseObservationProblem.ClosedWithPendingCommands,trace.Snapshot().Problems);
        gate.SetResult("original-result");Assert.Equal("original-result",await pending);Assert.Equal(1,original.Calls);Assert.False(trace.Snapshot().InternallyConsistent);Assert.Equal(1,trace.Snapshot().Completed);
    }
    /// <summary>派生异步上下文关闭后，父上下文可明确开始新一轮，不能被旧关闭引用永久阻塞。</summary>
    [Fact]
    public async Task A_closed_inherited_reference_does_not_block_a_new_explicit_scope()
    {
        var previous=DatabaseCommandObservation.Begin(Key());await Task.Run(previous.Dispose);Assert.True(previous.Snapshot().Closed);
        var next=DatabaseCommandObservation.Begin(Key());using(var original=new Command())using(var command=new MySqlFloatingCommand(original))Assert.Equal(9,command.ExecuteNonQuery());next.Dispose();
        Assert.Empty(previous.Snapshot().Commands);Assert.Single(next.Snapshot().Commands);Assert.True(next.Snapshot().InternallyConsistent);
    }
    /// <summary>嵌套观测被拒绝，避免外层静默少计内部命令；原根scope仍可正常使用。</summary>
    [Fact]
    public void Nested_scope_cannot_hide_commands_from_parent()
    {
        using var trace=DatabaseCommandObservation.Begin(Key());Assert.Throws<InvalidOperationException>(()=>DatabaseCommandObservation.Begin(Key()));using var original=new Command();using var command=new MySqlFloatingCommand(original);Assert.Equal(9,command.ExecuteNonQuery());Assert.Single(trace.Snapshot().Commands);
    }
    /// <summary>真正进入FreeSql命令工厂的驱动实例同样走观察；未连接故障不能伪记成功。</summary>
    [Fact]
    public void Actual_orm_factory_and_raw_mysql_commands_share_the_observation_entry()
    {
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Pooling=false;Connection Timeout=1"});var trace=DatabaseCommandObservation.Begin(Key());
        using(var orm=((AdoProvider)session.Orm.Ado).CreateCommand()){orm.CommandText="SELECT @id";orm.Parameters.Add(new MySqlParameter("id",9007199254740993L));Assert.ThrowsAny<Exception>(()=>{orm.ExecuteScalar();});}
        using(var raw=MySqlFloatingCommand.Wrap(new MySqlCommand("SELECT @id"))){raw.Parameters.Add(new MySqlParameter("id",9007199254740993L));Assert.ThrowsAny<Exception>(()=>{raw.ExecuteScalar();});}
        trace.Dispose();var values=trace.Snapshot().Commands;Assert.Equal(2,values.Count);Assert.All(values,value=>{Assert.True(value.DriverCalled);Assert.False(value.Succeeded);});Assert.Equal(values[0].Parameters,values[1].Parameters);
    }
    /// <summary>只验证失效输入，不创建scope、网络或数据库。</summary>
    [Fact]
    public void Invalid_key_and_limits_are_rejected()
    {
        Assert.Throws<ArgumentException>(()=>DatabaseCommandObservation.Begin(new byte[31]));Assert.Throws<ArgumentOutOfRangeException>(()=>DatabaseCommandObservation.Begin(Key(),0));Assert.Throws<ArgumentOutOfRangeException>(()=>DatabaseCommandObservation.Begin(Key(),65537));
    }
    /// <summary>只提供确定结果的命令，任何观测行为不能偷读结果或打开连接。</summary>
    internal sealed class Command:DbCommand
    {
        internal int Calls;internal Exception? Failure;internal bool ThrowMetadata;internal string Text="SELECT @value";internal DbDataReader? Reader;internal TaskCompletionSource<object?>? ScalarCompletion;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get=>ThrowMetadata?throw new InvalidOperationException("private-metadata-error"):Text;set=>Text=value??"";}
        public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}protected override DbTransaction? DbTransaction{get;set;}private readonly Parameters values=new();protected override DbParameterCollection DbParameterCollection=>values;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new Parameter();
        /// <summary>替身仅统计原调用并抛出指定原异常，不自行解释SQL。</summary>
        private void Enter(){Calls++;if(Failure is not null)throw Failure;}
        public override int ExecuteNonQuery(){Enter();return 9;}public override object? ExecuteScalar(){Enter();return "private-scalar-value";}
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){Enter();return Reader??new DataTable().CreateDataReader();}
        /// <summary>仅这一用例显式控制驱动返回，不改变其它默认同步／异步路径。</summary>
        public override Task<object?> ExecuteScalarAsync(CancellationToken token){if(ScalarCompletion is null)return base.ExecuteScalarAsync(token);Enter();return ScalarCompletion.Task;}
    }
    /// <summary>未知参数对象若被字符串化立刻失败，观察器只能报告不支持。</summary>
    private sealed class Bomb{public override string ToString()=>throw new InvalidOperationException("不能调用未知值的ToString。");}
    internal sealed class Parameter:DbParameter
    {
        public override DbType DbType{get;set;}public override ParameterDirection Direction{get;set;}=ParameterDirection.Input;public override bool IsNullable{get;set;}
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ParameterName{get;set;}="";[System.Diagnostics.CodeAnalysis.AllowNull]public override string SourceColumn{get;set;}="";public override object? Value{get;set;}public override bool SourceColumnNullMapping{get;set;}public override int Size{get;set;}public override void ResetDbType(){}
    }
    /// <summary>可同时存放真实MySQL参数和明确替身参数，不复制参数对象。</summary>
    private sealed class Parameters:DbParameterCollection
    {
        private readonly List<DbParameter> values=[];public override int Count=>values.Count;public override object SyncRoot=>values;
        public override int Add(object value){values.Add((DbParameter)value);return values.Count-1;}public override void AddRange(Array values){foreach(var value in values)Add(value!);}public override void Clear()=>values.Clear();public override bool Contains(object value)=>values.Contains((DbParameter)value);public override bool Contains(string value)=>IndexOf(value)>=0;public override void CopyTo(Array array,int index)=>((ICollection)values).CopyTo(array,index);public override IEnumerator GetEnumerator()=>values.GetEnumerator();public override int IndexOf(object value)=>values.IndexOf((DbParameter)value);public override int IndexOf(string name)=>values.FindIndex(value=>value.ParameterName==name);public override void Insert(int index,object value)=>values.Insert(index,(DbParameter)value);public override void Remove(object value)=>values.Remove((DbParameter)value);public override void RemoveAt(int index)=>values.RemoveAt(index);public override void RemoveAt(string name)=>RemoveAt(IndexOf(name));protected override DbParameter GetParameter(int index)=>values[index];protected override DbParameter GetParameter(string name)=>GetParameter(IndexOf(name));protected override void SetParameter(int index,DbParameter value)=>values[index]=value;protected override void SetParameter(string name,DbParameter value)=>SetParameter(IndexOf(name),value);
    }
}
