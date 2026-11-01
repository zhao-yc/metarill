using System.Data;
using System.Data.Common;
using FreeSql.Internal.CommonProvider;
using FreeSql.Internal.Model;
using FreeSql.Internal.ObjectPool;
using FreeSql.MySql;
using MySqlConnector;

namespace Metarill.Data;

/// <summary>只在执行含浮点参数的MySQL命令时准备，防止文本协议将有限double解析到相邻值。</summary>
internal sealed class MySqlFloatingCommand(DbCommand inner) : DbCommand
{
    /// <summary>保留真实连接和事务，只包装MySQL命令；其它提供程序继续原路径。</summary>
    internal static DbCommand Wrap(DbCommand command) => command is MySqlCommand ? new MySqlFloatingCommand(command) : command;
    /// <summary>参数绑定完成且连接已就绪后才准备；显式忽略Prepare不能静默退回不精确的文本传输。</summary>
    private bool NeedsPreparation()
    {
        if(inner is not MySqlCommand command)return false;
        var floating=false;
        foreach(DbParameter parameter in command.Parameters)
        {
            if(parameter.Value is double number){if(!double.IsFinite(number))throw new InvalidOperationException("浮点数据库参数必须为有限值。");floating=true;}
            else if(parameter.Value is float single){if(!float.IsFinite(single))throw new InvalidOperationException("浮点数据库参数必须为有限值。");floating=true;}
        }
        return floating&&!command.IsPrepared;
    }
    /// <summary>同步ORM入口在同一原连接准备，不获取额外连接或修改SQL／参数。</summary>
    private void PrepareFloating()
    {
        if(!NeedsPreparation())return;inner.Prepare();RequirePrepared();
    }
    /// <summary>异步入口沿原取消令牌准备，失败不执行原命令。</summary>
    private async Task PrepareFloatingAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!NeedsPreparation())return;
        await inner.PrepareAsync(token);RequirePrepared();
    }
    /// <summary>驱动配置若使Prepare成为空操作，明确拒绝，而不是记录假成功。</summary>
    private void RequirePrepared(){if(inner is MySqlCommand{IsPrepared:false})throw new InvalidOperationException("浮点数据库参数需要启用MySQL预处理，不能忽略Prepare。");}
    [System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get=>inner.CommandText;set=>inner.CommandText=value;}
    public override int CommandTimeout{get=>inner.CommandTimeout;set=>inner.CommandTimeout=value;}
    public override CommandType CommandType{get=>inner.CommandType;set=>inner.CommandType=value;}
    public override bool DesignTimeVisible{get=>inner.DesignTimeVisible;set=>inner.DesignTimeVisible=value;}
    public override UpdateRowSource UpdatedRowSource{get=>inner.UpdatedRowSource;set=>inner.UpdatedRowSource=value;}
    protected override DbConnection? DbConnection{get=>inner.Connection;set=>inner.Connection=value;}
    protected override DbTransaction? DbTransaction{get=>inner.Transaction;set=>inner.Transaction=value;}
    protected override DbParameterCollection DbParameterCollection=>inner.Parameters;
    protected override DbParameter CreateDbParameter()=>inner.CreateParameter();
    /// <summary>取消、显式准备及释放均交真实驱动，保留原资源所有权。</summary>
    public override void Cancel()=>inner.Cancel();
    public override void Prepare()=>inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken=default)=>inner.PrepareAsync(cancellationToken);
    public override int ExecuteNonQuery(){PrepareFloating();return inner.ExecuteNonQuery();}
    public override object? ExecuteScalar(){PrepareFloating();return inner.ExecuteScalar();}
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){PrepareFloating();return inner.ExecuteReader(behavior);}
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken){await PrepareFloatingAsync(cancellationToken);return await inner.ExecuteNonQueryAsync(cancellationToken);}
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken){await PrepareFloatingAsync(cancellationToken);return await inner.ExecuteScalarAsync(cancellationToken);}
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken cancellationToken){await PrepareFloatingAsync(cancellationToken);return await inner.ExecuteReaderAsync(behavior,cancellationToken);}
    protected override void Dispose(bool disposing){try{if(disposing)inner.Dispose();}finally{base.Dispose(disposing);}}
    public override async ValueTask DisposeAsync(){try{await inner.DisposeAsync();}finally{base.Dispose(true);GC.SuppressFinalize(this);}}
}

/// <summary>复用现有FreeSql单主库池及方言转换，只替换公开命令工厂。</summary>
internal sealed class FloatingMySqlAdo : AdoProvider
{
    private readonly AdoProvider original;
    /// <summary>当前适配器仅配置单主库；不重建池，也不声称实现未验收的读写分离。</summary>
    internal FloatingMySqlAdo(AdoProvider source):base(source.DataType,source.ConnectionString,source.SlaveConnectionStrings)
    {
        if(source.DataType!=FreeSql.DataType.MySql||source.SlavePools.Count!=0)throw new NotSupportedException("浮点命令适配只支持当前单主库MySQL提供程序。");
        original=source;_util=source._util;MasterPool=source.MasterPool;Identifier=source.Identifier;
    }
    /// <summary>原ADO继续提供驱动命令，包装层不生成或改写SQL。</summary>
    public override DbCommand CreateCommand()=>MySqlFloatingCommand.Wrap(original.CreateCommand());
    /// <summary>驱动参数创建与原常量转义均委托固定方言，避免改变非浮点类型。</summary>
    public override DbParameter[] GetDbParamtersByObject(string sql,object obj)=>original.GetDbParamtersByObject(sql,obj);
    public override object AddslashesProcessParam(object param,Type mapType,ColumnInfo mapColumn)=>original.AddslashesProcessParam(param,mapType,mapColumn);
    /// <summary>归还同一个原池，保留驱动对异常连接的处理。</summary>
    public override void ReturnConnection(IObjectPool<DbConnection> pool,FreeSql.Internal.ObjectPool.Object<DbConnection> connection,Exception error)=>original.ReturnConnection(pool,connection,error);
}

/// <summary>通过FreeSql公开提供程序入口安装命令工厂，不反射私有字段或在AOP中抢开连接。</summary>
internal sealed class FloatingMySqlProvider<TMark> : MySqlProvider<TMark>
{
    private AdoProvider? original;private int disposed;
    /// <summary>保留原ADO及唯一连接池，适配器与原对象在同一提供程序生命周期收尾。</summary>
    public FloatingMySqlProvider(string master,string[] slaves,Func<DbConnection>? factory=null):base(master,slaves,factory)
    {
        original=Ado as AdoProvider??throw new InvalidOperationException("当前FreeSql提供程序缺少预期ADO扩展点。");
        Ado=new FloatingMySqlAdo(original);
    }
    /// <summary>两份ADO状态都释放；同一个原连接池遵循既有幂等释放合同，不遗留基类资源。</summary>
    public override void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        try{base.Dispose();}finally{original?.Dispose();}
    }
}
