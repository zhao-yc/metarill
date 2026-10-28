using FreeSql;
using MySqlConnector;
using System.Data.Common;

namespace Metarill.Data;

/// <summary>平台库或生成项目库的独立连接配置；不会触发结构同步。</summary>
public sealed record DatabaseOptions
{
    public string Provider { get; init; } = "mysql";
    public string ConnectionString { get; init; } = "";
    public string ExpectedDatabase { get; init; } = "";
}

/// <summary>区分规划中的品牌与已完成验收的实现，不能用驱动支持冒充产品支持。</summary>
public sealed record DatabaseCapabilities(string Provider, bool Implemented, bool TransactionalDdl);

public interface IDatabaseProvider
{
    string Name { get; }
    DatabaseCapabilities Capabilities { get; }
    IFreeSql CreateOrm(DatabaseOptions options);
    DbConnection CreateConnection(DatabaseOptions options);
    void Validate(DatabaseOptions options);
    string QuoteIdentifier(string identifier);
    string ParameterPlaceholder(string name);
    /// <summary>读取快照由适配器建立，未验收的提供程序不能静默降级。</summary>
    ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken cancellationToken=default)=>throw new NotSupportedException("当前数据库尚未实现一致读取事务。");
    /// <summary>加入外层事务前验证隔离能力，不擅自改变业务事务级别。</summary>
    Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken cancellationToken=default)=>throw new NotSupportedException("当前数据库尚未实现一致读取事务验证。");
    /// <summary>跨应用实例的按钮租约由适配器提供，未实现时明确拒绝，不用本机锁冒充。</summary>
    Task<IDatabaseExecutionLease?> TryAcquireExecutionLeaseAsync(DatabaseOptions options,string scope,TimeSpan lifetime,TimeProvider clock,CancellationToken cancellationToken=default)
        =>throw new NotSupportedException("当前数据库尚未实现跨实例按钮执行租约。");
    /// <summary>适配器显式提供事务锁定读取；未实现的数据库不能静默降级。</summary>
    string BuildSelectForUpdate(string selectSql) => throw new NotSupportedException("当前数据库尚未实现锁定读取。");
}

public static class DatabaseProviders
{
    public static IReadOnlyList<DatabaseCapabilities> Capabilities { get; } = Array.AsReadOnly(new[]
    {
        new DatabaseCapabilities("mysql", true, false), new("mariadb", false, false),
        new("postgresql", false, true), new("sqlserver", false, true),
        new("oracle", false, false), new("sqlite", false, true)
    });

    /// <summary>未实现的数据库明确失败，禁止误用 MySQL 方言。</summary>
    public static IDatabaseProvider Resolve(string provider) => provider?.ToLowerInvariant() switch
    {
        "mysql" => new MySqlDatabaseProvider(),
        _ => throw new NotSupportedException($"数据库 {provider} 尚未完成 Metarill 适配。")
    };
}

public sealed partial class MySqlDatabaseProvider : IDatabaseProvider
{
    public string Name => "mysql";
    /// <summary>显式RR避免部署库默认隔离级别改变后分批图读取混入多个版本。</summary>
    public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken cancellationToken=default)=>connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,cancellationToken);
    /// <summary>明确级别直接检查；本会话默认事务的Unspecified再查询同连接默认级别。</summary>
    public async Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken cancellationToken=default)
    {
        var isolation=transaction.IsolationLevel;
        if(isolation is System.Data.IsolationLevel.RepeatableRead or System.Data.IsolationLevel.Serializable)return;
        if(isolation!=System.Data.IsolationLevel.Unspecified)throw new InvalidOperationException("外层事务隔离级别不能保证固定图读快照。");
        await using var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT @@transaction_isolation";
        var actual=Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),System.Globalization.CultureInfo.InvariantCulture);
        if(actual is not("REPEATABLE-READ" or "SERIALIZABLE"))throw new InvalidOperationException("外层默认事务不能保证固定图读快照。");
    }
    /// <summary>按 MySQL 方言引用经过白名单校验的单段名称。</summary>
    public string QuoteIdentifier(string identifier) => Schema.MySqlSchemaPlanner.Quote(identifier);
    /// <summary>参数只接受安全名称，值始终通过 DbParameter 传递。</summary>
    public string ParameterPlaceholder(string name)
    {
        Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(name);
        return "@" + name;
    }
    /// <summary>仅为内部已参数化 SELECT 附加行锁，拒绝多语句与重复锁片段。</summary>
    public string BuildSelectForUpdate(string selectSql)
    {
        if (!selectSql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) || selectSql.Contains(';') || selectSql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("锁定读取必须是单条内部 SELECT。");
        return selectSql + " FOR UPDATE";
    }
    public DatabaseCapabilities Capabilities => DatabaseProviders.Capabilities[0];

    /// <summary>同时锁定预期库与连接串，防止初始化误落系统库或其他项目。</summary>
    public void Validate(DatabaseOptions options)
    {
        if (!string.Equals(options.Provider, Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("数据库提供程序与适配器不一致。");
        if (string.IsNullOrWhiteSpace(options.ExpectedDatabase)) throw new InvalidOperationException("必须明确配置 ExpectedDatabase。");
        Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(options.ExpectedDatabase);
        var builder = new MySqlConnectionStringBuilder(options.ConnectionString);
        if (string.IsNullOrWhiteSpace(builder.Server) || !string.Equals(builder.Database, options.ExpectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException("连接串数据库必须与 ExpectedDatabase 完全一致。");
        if (new[] { "mysql", "sys", "information_schema", "performance_schema" }.Contains(builder.Database, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("禁止将系统数据库作为 Metarill 目标库。");
        // DDL 和所有手写命令禁止本地文件导入；保留驱动其他正常连接参数。
        if (builder.AllowLoadLocalInfile) throw new InvalidOperationException("禁止开启本地文件导入。");
    }

    /// <summary>ORM 只负责持久化，启动和首次 CRUD 均禁止自动建表。</summary>
    public IFreeSql CreateOrm(DatabaseOptions options)
    {
        Validate(options);
        return new FreeSqlBuilder().UseConnectionString(DataType.MySql, options.ConnectionString)
            .UseAutoSyncStructure(false).UseNoneCommandParameter(false)
            .UseGenerateCommandParameterWithLambda(true).Build();
    }

    /// <summary>创建未打开的受目标库约束连接，由调用者释放。</summary>
    public DbConnection CreateConnection(DatabaseOptions options)
    {
        Validate(options);
        return new MySqlConnection(options.ConnectionString);
    }
}
