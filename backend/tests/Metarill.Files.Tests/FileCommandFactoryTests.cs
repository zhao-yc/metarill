using System.Data;
using System.Data.Common;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>实际调用文件浏览Service，以明确替身验证适配器路由、原事务和连接所有权；不冒充实库权限验收。</summary>
public sealed class FileCommandFactoryTests
{
    /// <summary>无事务自管连接、有事务复用连接；成功、驱动异常和取消均只执行一次原查询。</summary>
    [Theory]
    [InlineData(false,0)][InlineData(true,0)][InlineData(false,1)][InlineData(true,1)][InlineData(false,2)][InlineData(true,2)]
    public async Task Browsing_uses_provider_factory_and_preserves_transaction_and_failure(bool transactional,int failure)
    {
        using var cancellation=new CancellationTokenSource();var expected=failure switch{1=>new InvalidOperationException("原驱动失败"),2=>(Exception)new OperationCanceledException(cancellation.Token),_=>null};
        var provider=new Provider{Failure=expected};var options=new DatabaseOptions{ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused",ExpectedDatabase="offline"};
        using var session=new DatabaseSession(options,null,provider);var catalog=new MetadataCatalog{YzProjectId="1"};
        var service=new FileLibraryService(new SqlRowStore(session,catalog),new FileMetadataContext([catalog]),new SnowflakeIdGenerator(29),new Authorization(),new FileLibraryOptions());
        async Task<int> Work()
        {
            var transaction=session.CurrentTransaction;
            if(expected is null)Assert.Empty(await service.ListAsync(42,cancellation.Token));
            else{var actual=await Record.ExceptionAsync(()=>service.ListAsync(42,cancellation.Token));Assert.Same(expected,actual);throw actual!;}
            Assert.Same(transaction,provider.Last!.Command!.Transaction);Assert.False(provider.Last.IsDisposed);return 1;
        }
        if(transactional)
        {
            if(expected is null)await session.InTransactionAsync(Work);
            else Assert.Same(expected,await Record.ExceptionAsync(()=>session.InTransactionAsync(Work)));
        }
        else if(expected is null)await service.ListAsync(42,cancellation.Token);
        else Assert.Same(expected,await Record.ExceptionAsync(()=>service.ListAsync(42,cancellation.Token)));
        var connection=Assert.IsType<Connection>(provider.Last);var command=Assert.IsType<Command>(connection.Command);
        Assert.Equal(1,provider.Commands);Assert.Equal(1,provider.Connections);Assert.Equal(1,connection.Opens);Assert.Equal(1,command.Executions);Assert.True(connection.IsDisposed);Assert.True(command.IsDisposed);
        Assert.Same(connection,command.Connection);Assert.Equal(transactional,command.Transaction is not null);
        Assert.Equal(transactional&&expected is null?1:0,connection.Commits);Assert.Equal(transactional&&expected is not null?1:0,connection.Rollbacks);
        Assert.Equal(42L,command.Parameters["browse_folder"].Value);Assert.Equal(1L,command.Parameters["browse_project"].Value);Assert.Equal("deleted",command.Parameters["deleted_status"].Value);
        Assert.Contains("`yz_file_library`",command.CommandText);Assert.Contains("`folder_id`=@browse_folder",command.CommandText);Assert.DoesNotContain("42",command.CommandText);Assert.Equal(cancellation.Token,command.Token);
    }
    /// <summary>只给固定身份；本用例没有任何文件行，不触发或替代来源权限判定。</summary>
    private sealed class Authorization:IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(2,"测试",false,"1","3");public void RequirePagePermission(string page,string operation)=>throw new UnauthorizedAccessException();
    }
    /// <summary>工厂计数发生在提供程序而非连接，漏接工厂会令本用例失败。</summary>
    private sealed class Provider:IDatabaseProvider
    {
        internal int Commands,Connections;internal Exception? Failure;internal Connection? Last;
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}
        public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options){Connections++;return Last=new(this);}
        public DbCommand CreateCommand(DbConnection connection){Commands++;return connection.CreateCommand();}
        public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;
    }
    /// <summary>替身连接只记录所有权，不联网；事务结束时保留身份供断言。</summary>
    private sealed class Connection(Provider owner):DbConnection
    {
        internal int Opens,Commits,Rollbacks;internal bool IsDisposed;internal Command? Command;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"fake";public override ConnectionState State=>ConnectionState.Open;
        public override void Open()=>Opens++;public override void Close(){}public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()=>Command=new(this,owner.Failure);
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this);
        protected override void Dispose(bool disposing){IsDisposed=true;base.Dispose(disposing);}
    }
    private sealed class Transaction(Connection connection):DbTransaction
    {
        public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>connection;
        public override void Commit()=>connection.Commits++;public override void Rollback()=>connection.Rollbacks++;
    }
    /// <summary>仅复用MySQL参数集合，实际执行返回空reader或原异常；不解释SQL、不读写文件。</summary>
    private sealed class Command(Connection connection,Exception? failure):DbCommand
    {
        private readonly MySqlCommand parameters=new();internal int Executions;internal bool IsDisposed;internal CancellationToken Token;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override int ExecuteNonQuery()=>throw new NotSupportedException();public override object? ExecuteScalar()=>throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>throw new NotSupportedException();
        /// <summary>异步执行保留传入取消标记及异常对象，供真实调用链断言。</summary>
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){Executions++;Token=token;return failure is null?Task.FromResult<DbDataReader>(new DataTable().CreateDataReader()):Task.FromException<DbDataReader>(failure);}
        protected override void Dispose(bool disposing){IsDisposed=true;if(disposing)parameters.Dispose();base.Dispose(disposing);}
    }
}
