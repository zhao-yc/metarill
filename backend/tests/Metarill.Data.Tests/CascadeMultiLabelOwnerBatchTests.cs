using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class CascadeMultiLabelOwnerBatchTests
{
    /// <summary>实际读取器处理同一已选 ID、两种来源账号及重现的同账号。连接只返回声明的离线行。</summary>
    [Fact]
    public async Task Same_member_is_read_separately_for_each_source_owner()
    {
        var catalog=CascadeMultiLabelReadTests.CrossCatalog("MultiSelectPage");
        catalog.ConfigTables.Single(table=>table.Id=="9").IsRongyu=1;
        CascadeMultiLabelReadTests.AddAccountScope(catalog,"9","21");
        var binding=new RelationCacheDefinition("9","93","1","19","94",true);
        var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]).Plans["9"].Read!;
        var provider=new FixtureProvider();
        using var orm=provider.CreateOrm(new());
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline"},orm,provider);

        var rows=await session.InTransactionAsync(()=>new MySqlNonRedundantReader(session)
            .ReadCurrentRowsAsync(plan,[],[new("id")],3));
        Assert.Equal(new[]{"[\"甲\"]","[null]","[\"甲\"]"},rows.Select(row=>Assert.IsType<string>(row["selected_country_names"])));
        Assert.Equal([OwnerA,OwnerB],provider.Owners.Select(value=>Assert.IsType<string>(value)));
        Assert.All(rows,row=>Assert.DoesNotContain(row.Keys,key=>key.StartsWith("metarill_multi_",StringComparison.Ordinal)));
    }

    /// <summary>复选从来源真实 bigint 字段读取两个相邻值；分组与 SQL 参数都保留其整数类型。</summary>
    [Fact]
    public async Task Checkbox_form_scope_binds_persisted_bigint_values_without_text_coercion()
    {
        var catalog=CascadeMultiLabelReadTests.CrossCatalog("MultiCheckBoxDatabase");
        var relation=catalog.ConfigColumns.Single(column=>column.Id=="93");
        relation.ControlDetail="""
            {"tableId":"1","pageId":"11","nameField":"country_name","whereSql":"record_owner_id=@formParameters.b185Consumer.recordOwnerId"}
            """;
        catalog.ConfigColumns.AddRange([
            new(){Id="95",ConfigTableId="9",ColumnName="record_owner_id",LogicalType="int64"},
            new(){Id="105",ConfigTableId="1",ColumnName="record_owner_id",LogicalType="int64"}]);
        catalog.ConfigPages.Add(new(){Id="99",ConfigTableId="9",WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigPageColumns.Add(new(){Id="995",ConfigPageId="99",ConfigColumnId="95"});
        var plan=NonRedundantReads.CompileProjectPage(catalog,"9",["id","selected_country_names","selected_ids"],[],[],[],
            [new("93","99","11",false,true)]).MySql;
        var provider=new FixtureProvider(typedOwner:true);
        using var orm=provider.CreateOrm(new());
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline"},orm,provider);

        var rows=await session.InTransactionAsync(()=>new MySqlNonRedundantReader(session)
            .ReadCurrentRowsAsync(plan,[],[new("id")],3));
        Assert.Equal(new[]{"[\"甲\"]","[null]","[\"甲\"]"},rows.Select(row=>Assert.IsType<string>(row["selected_country_names"])));
        Assert.Equal([long.Parse(OwnerA),long.Parse(OwnerB)],provider.Owners.Select(value=>Assert.IsType<long>(value)));
        Assert.All(rows,row=>Assert.DoesNotContain(row.Keys,key=>key.StartsWith("metarill_multi_",StringComparison.Ordinal)));
    }


    private const string OwnerA="9007199254740993";
    private const string OwnerB="9007199254740994";
    private const long Member=9007199254740995L;

    private sealed class FixtureProvider(bool typedOwner=false) : IDatabaseProvider
    {
        internal readonly List<object> Owners=[];
        internal bool TypedOwner {get;}=typedOwner;
        internal object OwnerValue(string owner)=>TypedOwner?(object)long.Parse(owner,System.Globalization.CultureInfo.InvariantCulture):owner;
        public string Name=>"mysql";
        public DatabaseCapabilities Capabilities=>new("mysql",true,false);
        public void Validate(DatabaseOptions options){}
        public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);
        public string ParameterPlaceholder(string name)=>"@"+name;
        public IFreeSql CreateOrm(DatabaseOptions options)=>new FreeSql.FreeSqlBuilder()
            .UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("离线夹具禁止使用数据库连接"))
            .UseAutoSyncStructure(false).Build();
        public DbConnection CreateConnection(DatabaseOptions options)=>new FixtureConnection(this);
    }

    private sealed class FixtureConnection(FixtureProvider provider) : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString {get;set;}="";
        public override string Database=>"offline";
        public override string DataSource=>"offline";
        public override string ServerVersion=>"offline";
        public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;
        public override void Close()=>state=ConnectionState.Closed;
        public override void ChangeDatabase(string database)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new FixtureTransaction(this);
        protected override DbCommand CreateDbCommand()=>new FixtureCommand(this,provider);
    }

    private sealed class FixtureTransaction(DbConnection connection) : DbTransaction
    {
        protected override DbConnection DbConnection=>connection;
        public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;
        public override void Commit(){}
        public override void Rollback(){}
    }

    private sealed class FixtureCommand(DbConnection connection,FixtureProvider provider) : DbCommand
    {
        private readonly DbCommand parameters=new MySqlConnector.MySqlCommand();
        [AllowNull] public override string CommandText {get;set;}="";
        public override CommandType CommandType {get;set;}=CommandType.Text;
        public override int CommandTimeout {get;set;}
        public override bool DesignTimeVisible {get;set;}
        public override UpdateRowSource UpdatedRowSource {get;set;}
        protected override DbConnection? DbConnection {get;set;}=connection;
        protected override DbTransaction? DbTransaction {get;set;}
        protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}
        public override void Prepare(){}
        public override int ExecuteNonQuery()=>throw new NotSupportedException();
        public override object? ExecuteScalar()=>throw new NotSupportedException();
        protected override DbParameter CreateDbParameter()=>parameters.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Assert.NotNull(Transaction);
            Assert.EndsWith(" FOR SHARE",CommandText);
            var rows=new DataTable();
            if(CommandText.StartsWith("SELECT multi_value.",StringComparison.Ordinal))
            {
                Assert.Contains("CROSS JOIN (SELECT @multi_owner_0 AS `record_owner_id`) `b`",CommandText);
                Assert.Contains("b.`record_owner_id`",CommandText);
                var owner=Parameters["multi_owner_0"].Value!;
                if(provider.TypedOwner)Assert.Contains("multi_value.`record_owner_id`=b.`record_owner_id`",CommandText);
                provider.Owners.Add(owner);
                Assert.Equal(Member,Parameters["multi_id_0"].Value);
                rows.Columns.Add("id",typeof(long));
                rows.Columns.Add("name",typeof(string));
                if(Equals(owner,provider.OwnerValue(OwnerA)))rows.Rows.Add(Member,"甲");
                else Assert.Equal(provider.OwnerValue(OwnerB),owner);
            }
            else
            {
                Assert.Contains("`b`.`record_owner_id` AS `metarill_multi_owner_1`",CommandText);
                rows.Columns.Add("id",typeof(long));
                rows.Columns.Add("selected_country_names",typeof(string));
                rows.Columns.Add("selected_ids",typeof(string));
                rows.Columns.Add("metarill_multi_input_0",typeof(string));
                rows.Columns.Add("metarill_multi_owner_1",provider.TypedOwner?typeof(long):typeof(string));
                const string selected="[\"9007199254740995\"]";
                rows.Rows.Add(1L,DBNull.Value,selected,selected,provider.OwnerValue(OwnerA));
                rows.Rows.Add(2L,DBNull.Value,selected,selected,provider.OwnerValue(OwnerB));
                rows.Rows.Add(3L,DBNull.Value,selected,selected,provider.OwnerValue(OwnerA));
            }
            return rows.CreateDataReader();
        }
    }
}
