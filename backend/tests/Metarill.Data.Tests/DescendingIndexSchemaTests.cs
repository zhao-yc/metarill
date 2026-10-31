using System.Data;
using System.Data.Common;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>真实结构服务配合只记录命令的离线连接；不把模拟反查当作MySQL已执行DDL。</summary>
public sealed class DescendingIndexSchemaTests
{
    /// <summary>混合方向按字段排序绑定，创建／增索引SQL一致；改旧索引方向仍要求独立迁移。</summary>
    [Fact]
    public void Mixed_directions_survive_compile_sql_diff_and_reverse_draft()
    {
        var desired=MySqlSchemaPlanner.Compile(Catalog(),"direction_offline");var table=Assert.Single(desired.Tables);var index=Assert.Single(table.Indexes!);
        Assert.Equal(new[]{"value","id"},index.Columns);Assert.Equal(new[]{"DESC","ASC"},index.Directions);
        Assert.Contains("INDEX `yzp_idx_value` (`value` DESC, `id`)",Assert.Single(MySqlSchemaPlanner.Compare(new(desired.Database,[]),desired)).Sql);
        var noIndex=desired with{Tables=[table with{Indexes=[]}]};Assert.Contains("ADD INDEX `yzp_idx_value` (`value` DESC, `id`)",Assert.Single(MySqlSchemaPlanner.Compare(noIndex,desired)).Sql);
        Assert.Empty(MySqlSchemaPlanner.Compare(desired,desired));Assert.Empty(MySqlSchemaPlanner.Compare(desired,noIndex));
        var ascending=desired with{Tables=[table with{Indexes=[index with{Directions=null}]}]};Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compare(ascending,desired));
        Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(desired),MySqlSchemaPlanner.Fingerprint(ascending));
        var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",desired,"source"),[]),"123");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));
        Assert.Equal(new[]{"DESC","ASC"},draft.LogicalMetadata.ConfigIndexColumns.Select(column=>column.GetProperty("sortDirection").GetString()));
        var roundtrip=MySqlSchemaPlanner.Compile(draft.LogicalMetadata,desired.Database);Assert.Empty(MySqlSchemaPlanner.Compare(desired,roundtrip));Assert.Empty(MySqlSchemaPlanner.Compare(roundtrip,desired));
    }
    /// <summary>省略和显式ASC必须保留旧JSON／指纹；未知或错位方向不能生成SQL或被摘要掩盖。</summary>
    [Fact]
    public void Ascending_legacy_shape_is_stable_and_invalid_direction_lists_fail_closed()
    {
        var index=new IndexSchema("yzp_idx_value",false,["value","id"]);Assert.Equal("{\"Name\":\"yzp_idx_value\",\"Unique\":false,\"Columns\":[\"value\",\"id\"]}",JsonSerializer.Serialize(index));
        DatabaseSchema Wrap(IndexSchema value)=>new("db",[new("record",[new("id","bigint",false,true),new("value","varchar(64)",true)],Indexes:[value])]);
        var original=Wrap(index);var explicitAsc=Wrap(index with{Directions=["ASC","asc"]});Assert.Equal(MySqlSchemaPlanner.Fingerprint(original),MySqlSchemaPlanner.Fingerprint(explicitAsc));Assert.Empty(MySqlSchemaPlanner.Compare(original,explicitAsc));
        foreach(var directions in new IReadOnlyList<string>[]{["DESC"],["ASC","DESC","ASC"],["ASC","DESC;DROP TABLE x"],["ASC",""]})
        {
            var invalid=Wrap(index with{Directions=directions});Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compare(new("db",[]),invalid));Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Fingerprint(invalid));
            var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",invalid,"source"),[]),"123");Assert.Contains(draft.Issues,issue=>issue.Code=="INDEX_UNREPRESENTABLE");
        }
    }
    /// <summary>读取真实服务的STATISTICS结果列，不能用SHOW CREATE字符串猜测ASC／DESC。</summary>
    [Fact]
    public async Task Snapshot_reads_each_collation_and_preview_detects_direction_conflict()
    {
        var provider=new RecordingProvider{TableExists=true,Directions=["D","A"]};using var session=Session(provider);var service=new SchemaService(session);
        var snapshot=await service.InspectAsync(Catalog());Assert.Equal(new[]{"DESC","ASC"},Assert.Single(snapshot.Tables[0].Indexes!).Directions);Assert.Empty(MySqlSchemaPlanner.Compare(snapshot,MySqlSchemaPlanner.Compile(Catalog(),"direction_offline")));
        provider.Directions=["A","A"];await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PreviewAsync(Catalog()));Assert.Equal(0,provider.Ddl);
        provider.Directions=["?","A"];await Assert.ThrowsAsync<NotSupportedException>(()=>service.InspectAsync(Catalog()));Assert.Equal(0,provider.Ddl);
    }
    /// <summary>执行真实特征查询：普通DESC可表示，但主键方向、前缀、隐藏和非BTREE仍保留阻断。</summary>
    [Fact]
    public async Task Desc_secondary_indexes_do_not_unblock_unrepresented_import_features()
    {
        var provider=new RecordingProvider();await using var connection=provider.CreateConnection(new());await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();var issues=new List<SchemaImportIssue>();
        var inspect=typeof(SchemaService).GetMethod("ReadImportFeaturesAsync",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        await (Task)inspect.Invoke(null,[connection,transaction,"record",issues,CancellationToken.None])!;
        Assert.DoesNotContain(issues,issue=>issue.Member=="yzp_idx_desc");Assert.Equal(new[]{"PRIMARY","prefix","hidden","hash","unknown"},issues.Select(issue=>issue.Member));Assert.All(issues,issue=>Assert.Equal("INDEX_FEATURE_UNSUPPORTED",issue.Code));Assert.Equal(0,provider.Ddl);
    }
    /// <summary>旧版、未知版本及MariaDB不能因语法可接受就宣称拥有MySQL物理降序。</summary>
    [Theory]
    [InlineData("5.7.44")][InlineData("8.0.0")][InlineData("8.0.10")][InlineData("8.0broken")][InlineData("10.11.8-MariaDB")][InlineData("8.0.43-MariaDB")][InlineData("unknown")]
    public async Task Unsupported_server_is_rejected_before_previewing_executable_desc_ddl(string version)
    {
        var provider=new RecordingProvider{Version=version};using var session=Session(provider);await Assert.ThrowsAsync<NotSupportedException>(()=>new SchemaService(session).PreviewAsync(Catalog()));Assert.Equal(0,provider.Ddl);Assert.Equal(0,provider.IndexReads);
    }
    /// <summary>支持的正式版本及标准发行后缀可预览；不能因版本字符串后缀误拒绝正常MySQL。</summary>
    [Theory]
    [InlineData("8.0.11")][InlineData("8.0.43-log")][InlineData("8.4.2")][InlineData("9.0.1")]
    public async Task Supported_server_can_preview_without_executing_ddl(string version)
    {
        var provider=new RecordingProvider{Version=version};using var session=Session(provider);Assert.Single((await new SchemaService(session).PreviewAsync(Catalog())).Changes);Assert.Equal(0,provider.Ddl);
    }
    /// <summary>即使SHOW CREATE文本没有变化，逐列方向改变也必须使已预览结构失效。</summary>
    [Fact]
    public async Task Direction_drift_after_preview_aborts_apply_before_any_ddl()
    {
        var provider=new RecordingProvider{TableExists=true};using var session=Session(provider);var service=new SchemaService(session);var plan=await service.PreviewAsync(Catalog());Assert.Empty(plan.Changes);
        provider.Directions=["A","A"];var error=await Assert.ThrowsAsync<SchemaApplyException>(()=>service.ApplyAsync(plan));Assert.Contains("结构在预览后发生变化",error.InnerException!.Message);Assert.Equal(0,error.Result.AppliedStatements);Assert.Equal(0,provider.Ddl);
    }
    /// <summary>预览时支持不代表执行时仍支持；重新连接后在结构锁和DDL之前拒绝版本变化。</summary>
    [Fact]
    public async Task Apply_rechecks_new_connection_version_and_records_zero_applied_statements()
    {
        var provider=new RecordingProvider();using var session=Session(provider);var service=new SchemaService(session);var plan=await service.PreviewAsync(Catalog());Assert.Single(plan.Changes);
        provider.Version="5.7.44";var error=await Assert.ThrowsAsync<SchemaApplyException>(()=>service.ApplyAsync(plan));Assert.IsType<NotSupportedException>(error.InnerException);Assert.Equal(0,error.Result.AppliedStatements);Assert.Equal(0,provider.Ddl);Assert.Equal(0,provider.Locks);Assert.False(Assert.Single(service.History).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ApplyAsync(plan));
    }
    /// <summary>原ASC模型仍可在原有服务器范围预览，不为新能力增加无关握手限制。</summary>
    [Fact]
    public async Task Existing_ascending_catalog_does_not_require_desc_server_capability()
    {
        var provider=new RecordingProvider{Version="5.7.44"};using var session=Session(provider);Assert.Single((await new SchemaService(session).PreviewAsync(Catalog("ASC"))).Changes);Assert.Equal(0,provider.Ddl);
    }
    /// <summary>维护方显式绑定的UUID必须在实际预览连接核对，不能只由调用方另开连接预检。</summary>
    [Fact]
    public async Task Explicit_instance_binding_rejects_preview_before_any_schema_lock_or_ddl()
    {
        var provider=new RecordingProvider();using var session=Session(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new SchemaService(session,expectedServerUuid:"approved-instance").PreviewAsync(Catalog()));Assert.Equal(0,provider.Ddl);Assert.Equal(0,provider.Locks);
    }
    /// <summary>正确绑定保持原预览结果；后续实际连接UUID改变时在结构锁/DDL之前停止。</summary>
    [Fact]
    public async Task Explicit_instance_binding_rechecks_every_apply_connection()
    {
        var provider=new RecordingProvider();using var session=Session(provider);var schema=new SchemaService(session,expectedServerUuid:"offline-instance");var plan=await schema.PreviewAsync(Catalog());Assert.Single(plan.Changes);
        provider.Uuid="other-instance";var error=await Assert.ThrowsAsync<SchemaApplyException>(()=>schema.ApplyAsync(plan));Assert.Contains("调用方审查范围",error.InnerException!.Message);Assert.Equal(0,error.Result.AppliedStatements);Assert.Equal(0,provider.Ddl);Assert.Equal(0,provider.Locks);
    }
    /// <summary>夹具只注入数据库协议，不替换SchemaService、规划器或反向编译器。</summary>
    private static DatabaseSession Session(RecordingProvider provider)=>new(new(){Provider="mysql",ConnectionString="Server=127.0.0.1;Port=1;Database=direction_offline;User ID=unused;Password=unused",ExpectedDatabase="direction_offline"},null,provider);
    /// <summary>原工作台排序与数组次序刻意不同，确保方向与实际索引字段绑定。</summary>
    private static MetadataCatalog Catalog(string first="DESC")=>new()
    {
        YzProjectId="123",ConfigTables=[new(){Id="table",TableName="record"}],ConfigColumns=[new(){Id="id",ConfigTableId="table",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="value",ConfigTableId="table",ColumnName="value",LogicalType="string",ColumnLength="64"}],
        ConfigIndexes=[JsonSerializer.SerializeToElement(new{id="index",configTableId="table",indexName="yzp_idx_value",isUnique=0})],
        ConfigIndexColumns=[JsonSerializer.SerializeToElement(new{configIndexId="index",configColumnId="id",sortNo=20,sortDirection="ASC"}),JsonSerializer.SerializeToElement(new{configIndexId="index",configColumnId="value",sortNo=10,sortDirection=first})]
    };
    private sealed class RecordingProvider:IDatabaseProvider
    {
        internal string Uuid {get;set;}="offline-instance";
        internal string Version="8.0.43";internal bool TableExists;internal string[] Directions=["D","A"];internal int Ddl,IndexReads,Locks;
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}
        public string QuoteIdentifier(string identifier)=>MySqlSchemaPlanner.Quote(identifier);public string ParameterPlaceholder(string name)=>"@"+name;
        public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this);
    }
    private sealed class Connection(RecordingProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"direction_offline";public override string DataSource=>"offline";public override string ServerVersion=>provider.Version;public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this);protected override DbCommand CreateDbCommand()=>new Command(this,provider);
    }
    private sealed class Transaction(DbConnection connection):DbTransaction
    {
        public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback(){}
    }
    private sealed class Command(DbConnection connection,RecordingProvider provider):DbCommand
    {
        private readonly DbCommand parameters=new MySqlConnector.MySqlCommand();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}=CommandType.Text;public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>parameters.CreateParameter();public override int ExecuteNonQuery(){provider.Ddl++;throw new InvalidOperationException("离线测试不可执行DDL。");}
        public override object? ExecuteScalar()
        {
            if(CommandText=="SELECT DATABASE()")return "direction_offline";if(CommandText=="SELECT @@server_uuid")return provider.Uuid;
            if(CommandText.StartsWith("SELECT TABLE_TYPE",StringComparison.Ordinal))return provider.TableExists?"BASE TABLE":null;if(CommandText.StartsWith("SELECT ENGINE",StringComparison.Ordinal))return "InnoDB";
            if(CommandText.Contains("GET_LOCK",StringComparison.Ordinal)){provider.Locks++;return 1;}if(CommandText.Contains("RELEASE_LOCK",StringComparison.Ordinal))return 1;throw new InvalidOperationException("未声明离线SQL。");
        }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var table=new DataTable();void Columns(params string[] names){foreach(var name in names)table.Columns.Add(name,typeof(object));}
            if(CommandText.StartsWith("SELECT COLUMN_NAME",StringComparison.Ordinal)){Columns("name","type","nullable","key","default","extra","logical");table.Rows.Add("id","bigint","NO","PRI",DBNull.Value,"","bigint");table.Rows.Add("value","varchar(64)","YES","",DBNull.Value,"","varchar");}
            else if(CommandText.StartsWith("SELECT INDEX_NAME,NON_UNIQUE",StringComparison.Ordinal)){provider.IndexReads++;Assert.Contains(",COLLATION FROM",CommandText);Columns("name","non_unique","column","direction");table.Rows.Add("yzp_idx_value",1,"value",provider.Directions[0]);table.Rows.Add("yzp_idx_value",1,"id",provider.Directions[1]);}
            else if(CommandText.StartsWith("SELECT INDEX_NAME,SUB_PART",StringComparison.Ordinal))
            {
                Columns("name","prefix","direction","type","visible");table.Rows.Add("yzp_idx_desc",DBNull.Value,"D","BTREE","YES");table.Rows.Add("PRIMARY",DBNull.Value,"D","BTREE","YES");table.Rows.Add("prefix",10,"D","BTREE","YES");table.Rows.Add("hidden",DBNull.Value,"D","BTREE","NO");table.Rows.Add("hash",DBNull.Value,DBNull.Value,"HASH","YES");table.Rows.Add("unknown",DBNull.Value,"Q","BTREE","YES");
            }
            else if(CommandText.StartsWith("SELECT CONSTRAINT_NAME",StringComparison.Ordinal)){Columns("name","type");}
            else if(CommandText.StartsWith("SHOW CREATE",StringComparison.Ordinal)){Columns("name","ddl");table.Rows.Add("record","CREATE TABLE record (id bigint, value varchar(64))");}
            else throw new InvalidOperationException("未声明离线反查SQL。");
            return table.CreateDataReader();
        }
        protected override void Dispose(bool disposing){if(disposing)parameters.Dispose();base.Dispose(disposing);}
    }
}
