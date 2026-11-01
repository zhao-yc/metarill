using System.Data;
using FreeSql;
using FreeSql.Internal.CommonProvider;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>真实驱动命令的元数据／取消及ORM方言兼容；不连接网络或模拟持久化成功。</summary>
public sealed class MySqlFloatingCommandTests
{
    /// <summary>只包装驱动执行边界，原SQL、参数对象、连接及命令属性全部透传。</summary>
    [Fact]
    public void Wrapper_preserves_the_original_command_and_parameter_objects()
    {
        using var connection=new MySqlConnection();using var inner=new MySqlCommand("SELECT @value",connection);using var command=MySqlFloatingCommand.Wrap(inner);
        Assert.IsType<MySqlFloatingCommand>(command);Assert.Same(connection,command.Connection);
        var parameter=command.CreateParameter();parameter.ParameterName="value";parameter.Value=Math.ScaleB(1d,-958);command.Parameters.Add(parameter);
        Assert.Same(parameter,inner.Parameters[0]);Assert.Same(inner.Parameters,command.Parameters);Assert.Equal("SELECT @value",command.CommandText);
        command.CommandTimeout=7;command.CommandType=CommandType.Text;command.DesignTimeVisible=false;command.UpdatedRowSource=UpdateRowSource.None;
        Assert.Equal(7,inner.CommandTimeout);Assert.False(inner.DesignTimeVisible);Assert.Equal(UpdateRowSource.None,inner.UpdatedRowSource);
        command.CommandText="SELECT @value AS amount";Assert.Equal(command.CommandText,inner.CommandText);Assert.False(inner.IsPrepared);
    }
    /// <summary>异步释放同时完成真实驱动和包装组件收尾，不吞掉标准Disposed事件。</summary>
    [Fact]
    public async Task Async_disposal_releases_both_command_components()
    {
        var inner=new MySqlCommand();var command=MySqlFloatingCommand.Wrap(inner);var originalDisposed=0;var wrapperDisposed=0;
        inner.Disposed+=(_,_)=>originalDisposed++;command.Disposed+=(_,_)=>wrapperDisposed++;
        await command.DisposeAsync();Assert.Equal(1,originalDisposed);Assert.Equal(1,wrapperDisposed);
    }
    /// <summary>非法浮点在所有同步执行入口先拒绝，不依赖连接状态或数据库错误。</summary>
    [Theory][InlineData("scalar")][InlineData("reader")][InlineData("write")]
    public void Nonfinite_values_are_rejected_before_any_synchronous_execution(string kind)
    {
        using var inner=new MySqlCommand("SELECT @value");inner.Parameters.AddWithValue("value",double.NaN);using var command=MySqlFloatingCommand.Wrap(inner);
        var error=Assert.Throws<InvalidOperationException>(()=>{if(kind=="scalar")command.ExecuteScalar();else if(kind=="reader")command.ExecuteReader();else command.ExecuteNonQuery();});
        Assert.Equal("浮点数据库参数必须为有限值。",error.Message);Assert.Null(command.Connection);
    }
    /// <summary>异步写／读／标量同样拒绝非有限参数，不将它们格式化成文本交服务器猜测。</summary>
    [Theory][InlineData("scalar")][InlineData("reader")][InlineData("write")]
    public async Task Nonfinite_values_are_rejected_before_any_asynchronous_execution(string kind)
    {
        using var inner=new MySqlCommand("SELECT @value");inner.Parameters.AddWithValue("value",float.PositiveInfinity);await using var command=MySqlFloatingCommand.Wrap(inner);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(async()=>{if(kind=="scalar")await command.ExecuteScalarAsync();else if(kind=="reader")await command.ExecuteReaderAsync();else await command.ExecuteNonQueryAsync();});
        Assert.Equal("浮点数据库参数必须为有限值。",error.Message);Assert.Null(command.Connection);
    }
    /// <summary>取消在连接及预处理之前生效，不把已取消命令送至驱动。</summary>
    [Theory][InlineData("scalar")][InlineData("reader")][InlineData("write")]
    public async Task Cancellation_stops_execution_before_preparation(string kind)
    {
        using var inner=new MySqlCommand("SELECT @value");inner.Parameters.AddWithValue("value",1d);await using var command=MySqlFloatingCommand.Wrap(inner);using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>{if(kind=="scalar")await command.ExecuteScalarAsync(cancellation.Token);else if(kind=="reader")await command.ExecuteReaderAsync(cancellation.Token);else await command.ExecuteNonQueryAsync(cancellation.Token);});Assert.False(inner.IsPrepared);
    }
    /// <summary>公开ADO适配复用同一个主池、标识和方言参数，不建立第二个池或改转义规则。</summary>
    [Fact]
    public void Public_ado_adapter_reuses_the_pool_and_original_dialect()
    {
        using var orm=Original();var original=Assert.IsAssignableFrom<AdoProvider>(orm.Ado);var adapter=new FloatingMySqlAdo(original);
        try
        {
            Assert.Same(original.MasterPool,adapter.MasterPool);Assert.Equal(original.Identifier,adapter.Identifier);Assert.Equal(original.DataType,adapter.DataType);Assert.Empty(adapter.SlavePools);
            foreach(var value in new object[]{"引号'及\\路径",new byte[]{0,1,255},new DateTime(2026,11,2,3,4,5),TimeSpan.FromHours(26),9007199254740993L,0.1234567890123456789012345678m})
                Assert.Equal(original.AddslashesProcessParam(value,null,null),adapter.AddslashesProcessParam(value,null!,null!));
            var parameters=adapter.GetDbParamtersByObject("SELECT @id,@amount,@name",new{id=9007199254740993L,amount=1.25e100,name="中文'\\"});
            Assert.All(parameters,parameter=>Assert.IsType<MySqlParameter>(parameter));Assert.Contains(parameters,parameter=>parameter.Value is double number&&number==1.25e100);Assert.Contains(parameters,parameter=>parameter.Value is long id&&id==9007199254740993L);
            using var command=adapter.CreateCommand();Assert.IsType<MySqlFloatingCommand>(command);
        }
        finally{adapter.Dispose();}
    }
    /// <summary>生产提供程序仍生成与原FreeSql逐字相同的参数化SQL，并保持禁止自动同步。</summary>
    [Fact]
    public void Production_provider_keeps_the_original_query_and_write_sql()
    {
        using var original=Original();using var actual=new MySqlDatabaseProvider().CreateOrm(Options());var value=Math.ScaleB(1d,-958);
        Assert.Equal(original.Select<Measure>().Where(row=>row.Amount==value).ToSql(),actual.Select<Measure>().Where(row=>row.Amount==value).ToSql());
        var item=new Measure{Id=9007199254740993,Amount=value};Assert.Equal(original.Insert(item).ToSql(),actual.Insert(item).ToSql());Assert.Equal(original.Update<Measure>().Where(row=>row.Id==item.Id).Set(row=>row.Amount,value).ToSql(),actual.Update<Measure>().Where(row=>row.Id==item.Id).Set(row=>row.Amount,value).ToSql());
        Assert.IsType<FloatingMySqlAdo>(actual.Ado);Assert.False(actual.CodeFirst.IsAutoSyncStructure);actual.Dispose();actual.Dispose();
    }
    /// <summary>无网络的连接配置只用于SQL翻译，测试不执行这些命令。</summary>
    private static DatabaseOptions Options()=>new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Pooling=false;Connection Timeout=1"};
    private static IFreeSql Original()=>new FreeSqlBuilder().UseConnectionString(DataType.MySql,Options().ConnectionString).UseAutoSyncStructure(false).UseNoneCommandParameter(false).UseGenerateCommandParameterWithLambda(true).Build();
    [FreeSql.DataAnnotations.Table(Name="offline_float",DisableSyncStructure=true)]private sealed class Measure{[FreeSql.DataAnnotations.Column(IsPrimary=true)]public long Id{get;set;}public double? Amount{get;set;}}
}
