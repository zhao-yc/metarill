using System.Data;
using System.Data.Common;
using System.Reflection;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Persistence;

namespace Metarill.Generation.Tests;

/// <summary>只供当前会话政策测试使用的本机ADO上下文；事务由真实DatabaseSession管理，任何SQL都硬拒。</summary>
internal sealed class SessionPolicyContext:IDisposable
{
    internal SessionPolicyProvider Provider{get;}
    internal DatabaseSession Session{get;}
    internal SqlRowStore Store{get;}
    internal SessionPolicyContext(bool disconnected=false)
    {
        Provider=new(disconnected);Session=new(new(){Provider="offline",ExpectedDatabase="offline",ConnectionString="offline"},Provider.Orm,Provider);Store=new(Session,new MetadataCatalog());
    }
    public void Dispose()=>Session.Dispose();
}

/// <summary>记录本地连接调用并禁止SQL；故障事务可仅用于验证Connection缺失分支，不模拟MySQL锁。</summary>
internal sealed class SessionPolicyProvider(bool disconnected):IDatabaseProvider
{
    internal int Opens,SqlCalls;
    internal IFreeSql Orm{get;}=DispatchProxy.Create<IFreeSql,CurrentSessionPolicyForbiddenOrm>();
    public string Name=>"offline";
    public DatabaseCapabilities Capabilities=>new("offline",true,false);
    public void Validate(DatabaseOptions options){}
    public IFreeSql CreateOrm(DatabaseOptions options)=>Orm;
    public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this,disconnected);
    public string QuoteIdentifier(string name)=>throw new NotSupportedException("政策测试禁止SQL。");
    public string ParameterPlaceholder(string name)=>throw new NotSupportedException("政策测试禁止SQL。");
    public string BuildSelectForUpdate(string sql)=>throw new NotSupportedException("政策测试禁止SQL。");
    private sealed class Connection(SessionPolicyProvider owner,bool missingConnection):DbConnection
    {
        private ConnectionState state;
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";
        public override string DataSource=>"offline";
        public override string ServerVersion=>"offline";
        public override ConnectionState State=>state;
        public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        public override void Open(){owner.Opens++;state=ConnectionState.Open;}
        public override void Close()=>state=ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)=>new Transaction(missingConnection?null:this);
        protected override DbCommand CreateDbCommand(){owner.SqlCalls++;throw new NotSupportedException("政策不能执行SQL或OAuth查询。");}
    }
    private sealed class Transaction(DbConnection? connection):DbTransaction
    {
        public override IsolationLevel IsolationLevel=>IsolationLevel.Unspecified;
        protected override DbConnection? DbConnection=>connection;
        public override void Commit(){}
        public override void Rollback(){}
    }
}

/// <summary>政策不能调用ORM；Dispose仅用于测试生命周期，不允许查询或写入。</summary>
public class CurrentSessionPolicyForbiddenOrm:DispatchProxy
{
    protected override object? Invoke(MethodInfo? method,object?[]? arguments)=>method!.Name=="Dispose"?null:throw new NotSupportedException("政策测试禁止ORM或联网。");
}
