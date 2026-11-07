using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;

namespace Metarill.Api.Tests;

public sealed class PlatformBootstrapSchemaTests
{
    /// <summary>运行真实平台显式初始化到预览结束，确认它向结构服务登记原目录及登录日志表。</summary>
    [Fact]
    public async Task Explicit_platform_bootstrap_previews_the_login_audit_table()
    {
        var directory = MetadataDirectory();
        var registry = new MetadataRegistry(Path.Combine(directory, "platform.json"), Path.Combine(directory, "demo.json"));
        var models = new ProjectPhysicalModels(MetadataRegistry.Load(Path.Combine(directory, "files.json")),
            MetadataRegistry.Load(Path.Combine(directory, "security.json")), registry);
        var provider = new PreviewProvider();
        var options = new DatabaseOptions { Provider = "mysql", ExpectedDatabase = "offline_platform" };
        using var session = new DatabaseSession(options, DispatchProxy.Create<IFreeSql, UnusedOrm>(), provider);
        var store = new SqlRowStore(session, models.Compose(registry.Platform));
        var bootstrap = new BootstrapService(session, new SchemaService(session), store, new MetadataRepository(store, registry), registry, models);

        await Assert.ThrowsAsync<StopBeforeApply>(() => bootstrap.InitializeAsync("synthetic-owner", "synthetic-password", options));

        Assert.DoesNotContain(registry.Platform.ConfigTables, table => table.TableName == "yz_manager_log");
        Assert.Equal(registry.Platform.ConfigTables.Select(table => table.TableName).Append("yz_manager_log").Order(StringComparer.Ordinal),
            provider.PreviewTables);
    }

    /// <summary>查找当前编译来源，固定测试使用同一份发布元数据。</summary>
    private static string MetadataDirectory([CallerFilePath] string file = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(file)!);
        while (!File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent ?? throw new InvalidOperationException("找不到测试源码。");
        return Path.Combine(directory.FullName, "backend", "metadata");
    }

    public class UnusedOrm : DispatchProxy
    {
        /// <summary>预览只读取结构，不能使用ORM读写数据。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException("不允许ORM调用。");
    }

    private sealed class StopBeforeApply : Exception;

    private sealed class PreviewProvider : IDatabaseProvider
    {
        internal List<string> PreviewTables { get; } = [];
        private bool opened;
        public string Name => "mysql";
        public DatabaseCapabilities Capabilities => new("mysql", true, false);
        public void Validate(DatabaseOptions options) { }
        public string QuoteIdentifier(string identifier) => throw new NotSupportedException();
        public string ParameterPlaceholder(string name) => throw new NotSupportedException();
        public IFreeSql CreateOrm(DatabaseOptions options) => throw new NotSupportedException();

        /// <summary>第一条连接只供真实结构预览；执行另开连接前立即停止。</summary>
        public DbConnection CreateConnection(DatabaseOptions options)
        {
            if (opened) throw new StopBeforeApply();
            opened = true;
            return new PreviewConnection(options.ExpectedDatabase, PreviewTables);
        }
    }

    private sealed class PreviewConnection(string database, List<string> tables) : DbConnection
    {
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => database;
        public override string DataSource => "offline";
        public override string ServerVersion => "8.4.11";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() { }
        public override void Close() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new PreviewCommand(this, tables);
    }

    private sealed class PreviewCommand(DbConnection connection, List<string> tables) : DbCommand
    {
        private readonly MySqlCommand parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new MySqlParameter();
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

        /// <summary>只允许连接身份和预览实际表名的查询，全部返回空库结果。</summary>
        public override object? ExecuteScalar()
        {
            if (CommandText == "SELECT DATABASE()") return DbConnection!.Database;
            if (CommandText == "SELECT @@server_uuid") return "offline-schema-test";
            Assert.Equal("SELECT TABLE_TYPE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table", CommandText);
            tables.Add(Assert.IsType<string>(parameters.Parameters["@table"].Value));
            return null;
        }
    }
}