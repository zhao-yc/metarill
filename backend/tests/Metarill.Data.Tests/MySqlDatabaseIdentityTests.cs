using System.Data;
using System.Data.Common;
using System.Text.Json;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>实际提供程序的同事务身份读取；离线记录流与显式真实只读连接分别列明。</summary>
public sealed class MySqlDatabaseIdentityTests
{
    /// <summary>按服务器大小写规则归一数据库名，固定同一事务一次读取，不取连接别名作为身份。</summary>
    [Theory][InlineData(0,"Db","Db","Db")][InlineData(1,"DB","db","db")][InlineData(2,"Db","DB","db")]
    public async Task Physical_identity_uses_actual_transaction_and_server_case_rules(int casing,string actual,string expected,string canonical)
    {
        using var connection=new Connection([["ABCDEF",actual,casing]]);connection.Open();using var transaction=connection.BeginTransaction();
        var result=await new MySqlDatabaseProvider().ReadPhysicalDatabaseIdentityAsync(Options(expected),transaction);
        Assert.Equal(new("mysql","abcdef",canonical),result);Assert.Equal("SELECT @@server_uuid,DATABASE(),@@lower_case_table_names",connection.Command!.CommandText);Assert.Same(transaction,connection.Command.Transaction);
    }
    /// <summary>空服务器、空库、未知大小写规则、错库及多行不能形成有效身份。</summary>
    [Theory][InlineData("server",null,0)][InlineData("", "Db",0)][InlineData("server","Other",0)][InlineData("server","Db",3)]
    public async Task Missing_or_wrong_database_identity_is_rejected(string server,string? database,int casing)
    {
        using var connection=new Connection([[server,database,casing]]);connection.Open();using var transaction=connection.BeginTransaction();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlDatabaseProvider().ReadPhysicalDatabaseIdentityAsync(Options("Db"),transaction));
    }
    /// <summary>真实Server-200只读核验需独立显式配置，不复用允许DDL的集成测试开关。</summary>
    [DatabaseIdentityReadOnlyFact]
    public async Task Server200_configured_development_database_bindings_are_verified_without_writes()
    {
        var file=Environment.GetEnvironmentVariable("METARILL_IDENTITY_READONLY_SETTINGS")!;var uuid=Environment.GetEnvironmentVariable("METARILL_IDENTITY_READONLY_SERVER_UUID")!;
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(file));var metarill=Property(settings.RootElement,"Metarill");
        var identities=new List<PhysicalDatabaseIdentity>();var expectedNames=new List<string>();
        foreach(var section in new[]{"Database","BusinessDatabase"})
        {
            var options=JsonSerializer.Deserialize<DatabaseOptions>(Property(metarill,section),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);
            Assert.Contains(parsed.Server,new[]{"192.168.31.200","deve-server.yinzon.com"});Assert.Contains(options.ExpectedDatabase,new[]{"metarill_test","metarill_business_test","metarill_dev","metarill_business_dev"});
            parsed.ConnectionTimeout=10;options=options with{ConnectionString=parsed.ConnectionString};
            expectedNames.Add(options.ExpectedDatabase);var provider=new MySqlDatabaseProvider();await using var connection=provider.CreateConnection(options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await connection.OpenAsync(timeout.Token);
            await using var transaction=await connection.BeginTransactionAsync(timeout.Token);
            try{var identity=await provider.ReadPhysicalDatabaseIdentityAsync(options,transaction,timeout.Token);Assert.Equal(uuid,identity.ServerId);identities.Add(identity);}
            finally{await transaction.RollbackAsync(CancellationToken.None);}
        }
        Assert.Equal(2,identities.Count);Assert.Equal(expectedNames[0]==expectedNames[1],identities[0]==identities[1]);
    }
    /// <summary>只按确定名称取私有配置，重复或缺失不猜测，不输出连接串。</summary>
    private static JsonElement Property(JsonElement value,string name)=>value.EnumerateObject().Single(field=>field.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
    private static DatabaseOptions Options(string database)=>new(){ExpectedDatabase=database,ConnectionString=new MySqlConnectionStringBuilder{Server="127.0.0.1",Port=1,Database=database,UserID="offline"}.ConnectionString};
    private sealed class Connection(object?[][] rows):DbConnection
    {
        private ConnectionState state;internal DbCommand? Command {get;private set;}
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"offline-alias";public override string ServerVersion=>"offline";public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string value)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this);
        protected override DbCommand CreateDbCommand()=>Command=new CommandReader(this,rows);
    }
    private sealed class Transaction(Connection connection):DbTransaction
    {
        protected override DbConnection DbConnection=>connection;public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;public override void Commit(){}public override void Rollback(){}
    }
    private sealed class CommandReader(Connection connection,object?[][] rows):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";
        public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}public override int ExecuteNonQuery()=>throw new NotSupportedException();public override object? ExecuteScalar()=>throw new NotSupportedException();protected override DbParameter CreateDbParameter()=>new MySqlParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Assert.Equal("SELECT @@server_uuid,DATABASE(),@@lower_case_table_names",CommandText);Assert.NotNull(DbTransaction);
            var table=new DataTable();for(var index=0;index<3;index++)table.Columns.Add("c"+index,typeof(object));
            foreach(var row in rows)table.Rows.Add(row.Select(value=>value??DBNull.Value).ToArray());return table.CreateDataReader();
        }
    }
}
public sealed class DatabaseIdentityReadOnlyFactAttribute:FactAttribute
{
    /// <summary>CI和普通回归默认跳过；两项显式变量齐备才读取指定私有连接，测试只发SELECT与事务清理。</summary>
    public DatabaseIdentityReadOnlyFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_IDENTITY_READONLY_SETTINGS"))||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_IDENTITY_READONLY_SERVER_UUID")))Skip="未显式配置Server-200物理身份只读验收。";
    }
}
