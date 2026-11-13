using System.Data.Common;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>命令工厂只包装原驱动，不负责打开连接、自动绑定事务或执行命令。</summary>
public sealed class DatabaseCommandFactoryTests
{
    /// <summary>真实驱动处于关闭状态时仍可建命令；执行异常来自原驱动且观察器保留失败。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task MySql_factory_preserves_connection_parameters_and_closed_driver_failure(bool observed)
    {
        IDatabaseProvider provider=new MySqlDatabaseProvider();using var connection=new MySqlConnection();
        using var command=provider.CreateCommand(connection);Assert.IsType<MySqlFloatingCommand>(command);
        Assert.Same(connection,command.Connection);Assert.Null(command.Transaction);Assert.Equal(System.Data.ConnectionState.Closed,connection.State);
        command.CommandText="SELECT @identity";command.CommandTimeout=17;
        var parameter=command.CreateParameter();parameter.ParameterName="identity";parameter.Value=9007199254740993L;command.Parameters.Add(parameter);
        using var trace=observed?DatabaseCommandObservation.Begin(Enumerable.Repeat((byte)13,32).ToArray()):null;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>command.ExecuteScalarAsync());trace?.Dispose();
        Assert.Same(parameter,command.Parameters[0]);Assert.Same(connection,command.Connection);Assert.Equal(17,command.CommandTimeout);
        Assert.Equal(System.Data.ConnectionState.Closed,connection.State);Assert.Equal("SELECT @identity",command.CommandText);
        if(trace is not null){var entry=Assert.Single(trace.Snapshot().Commands);Assert.False(entry.Succeeded);Assert.True(entry.DriverCalled);Assert.Equal("identity",Assert.Single(entry.Parameters).Name);Assert.True(trace.Snapshot().InternallyConsistent);}
    }
    /// <summary>既有自定义提供程序未覆盖工厂时保持原命令对象，不强迫MySQL包装或更换连接。</summary>
    [Fact]
    public void Default_factory_returns_original_custom_command_without_opening_connection()
    {
        IDatabaseProvider provider=new DefaultProvider();using var connection=new OriginalConnection();
        using var command=provider.CreateCommand(connection);Assert.Same(connection.Original,command);Assert.Same(connection,command.Connection);Assert.Equal(0,connection.Opens);
    }
    /// <summary>缺省提供程序仅测试新增接口的向后兼容，不实现未使用的网络能力。</summary>
    private sealed class DefaultProvider:IDatabaseProvider
    {
        public string Name=>"custom";public DatabaseCapabilities Capabilities=>new("custom",false,false);
        public IFreeSql CreateOrm(DatabaseOptions options)=>throw new NotSupportedException();public DbConnection CreateConnection(DatabaseOptions options)=>throw new NotSupportedException();
        public void Validate(DatabaseOptions options){}public string QuoteIdentifier(string value)=>value;public string ParameterPlaceholder(string value)=>"@"+value;
    }
    private sealed class OriginalConnection:DbConnection
    {
        internal readonly DatabaseCommandObservationTests.Command Original=new();internal int Opens;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"fake";public override System.Data.ConnectionState State=>System.Data.ConnectionState.Closed;
        public override void Open()=>Opens++;public override void Close(){}public override void ChangeDatabase(string value)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel level)=>throw new NotSupportedException();
        /// <summary>返回调用者创建的同一原命令并显式记录所属连接。</summary>
        protected override DbCommand CreateDbCommand(){Original.Connection=this;return Original;}
    }
}
