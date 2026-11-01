using System.Data;
using System.Data.Common;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>真实固定SQL构造与驱动结果解码，仅ADO行传输替代，不连接MySQL。</summary>
public sealed class DoubleStatisticsTests
{
    /// <summary>大测量值和极小非零值不转decimal；精确整数合计仍保留decimal。</summary>
    [Fact]
    public async Task Mixed_statistics_keep_double_and_exact_decimal_results_separate()
    {
        var provider=new AggregateProvider{Values=[1.25e100,-3.5e-200,9007199254740993m]};using var session=Session(provider);
        var result=await new MySqlNonRedundantReader(session).ReadStatisticsAsync(Plan(),[],["bill_amount","measurement","id"],projectId:"54");
        Assert.Equal(1,result.Total);Assert.Equal(1.25e100,result.DoubleSums["BILL_AMOUNT"]);Assert.Equal(-3.5e-200,result.DoubleSums["measurement"]);
        Assert.Equal(9007199254740993m,Assert.Single(result.Sums).Value);Assert.Equal("id",Assert.Single(result.Sums).Key);
        Assert.Throws<NotSupportedException>(()=>((IDictionary<string,double?>)result.DoubleSums).Add("rogue",1));
        var sql=Assert.Single(provider.Commands);Assert.Contains("SUM(",sql);Assert.Contains("LEFT JOIN",sql);Assert.DoesNotContain("LIMIT",sql);Assert.DoesNotContain("ORDER BY",sql);Assert.Contains("54",provider.Parameters.Values.Select(value=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)));
    }
    /// <summary>数据库空合计保留null，由原页脚按既有合同显示零，不借空集返回非有限值。</summary>
    [Fact]
    public async Task Empty_aggregate_results_preserve_nulls_in_both_numeric_families()
    {
        var provider=new AggregateProvider{Values=[null,null,null],Total=0};using var session=Session(provider);
        var result=await new MySqlNonRedundantReader(session).ReadStatisticsAsync(Plan(),[],["bill_amount","measurement","id"],projectId:"54");
        Assert.Equal(0,result.Total);Assert.Null(result.DoubleSums["bill_amount"]);Assert.Null(result.DoubleSums["measurement"]);Assert.Null(result.Sums["id"]);
    }
    /// <summary>驱动非有限值、文本溢出／下溢和非数值不能进入响应或被清空。</summary>
    [Theory][InlineData("NaN")][InlineData("Infinity")][InlineData("-Infinity")][InlineData("1e999")][InlineData("1e-999")][InlineData("wrong")]
    public async Task Invalid_driver_double_sum_is_rejected(string value)
    {
        var provider=new AggregateProvider{Values=[value]};using var session=Session(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadStatisticsAsync(Plan(),[],["bill_amount"],projectId:"54"));Assert.Single(provider.Commands);
    }
    /// <summary>重复统计和非法筛选在打开连接前拒绝，浮点支持不扩大动态SQL入口。</summary>
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Invalid_statistics_are_rejected_before_ado(bool duplicate)
    {
        var provider=new AggregateProvider{Values=[]};using var session=Session(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadStatisticsAsync(Plan(),duplicate?[]:[new("measurement",FixedReadComparison.Equal,"1e-999")],duplicate?["measurement","MEASUREMENT"]:["measurement"],projectId:"54"));Assert.Equal(0,provider.Connections);Assert.Empty(provider.Commands);
    }
    /// <summary>本页合计保持double、null按零；空页为零，不发明decimal精度。</summary>
    [Fact]
    public void Current_page_double_sums_preserve_large_small_and_empty_values()
    {
        Assert.Equal(2.5e100,LegacyStatisticsMath.SumDouble([1.25e100,null,1.25e100]));Assert.Equal(-7e-200,LegacyStatisticsMath.SumDouble([-3.5e-200,-3.5e-200]));Assert.Equal(0d,LegacyStatisticsMath.SumDouble([]));Assert.Equal(1.25e100,LegacyStatisticsMath.DoubleSumResult("1.25e100"));Assert.Null(LegacyStatisticsMath.DoubleSumResult(null));
    }
    /// <summary>本页输入和累加溢出明确失败，不能把Infinity送给JSON编码器。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Current_page_invalid_values_or_accumulation_overflow_are_rejected(bool overflow)=>Assert.Throws<InvalidOperationException>(()=>LegacyStatisticsMath.SumDouble(overflow?[double.MaxValue,double.MaxValue]:[double.NaN]));

    /// <summary>只在真实原目录增加物理测量值，派生金额路径及项目／软删除条件保持。</summary>
    private static MySqlNonRedundantReadPlan Plan()
    {
        var catalog=NonRedundantReadTests.Catalog("mt_double_statistics_root","mt_double_statistics_target");foreach(var field in catalog.ConfigColumns.Where(column=>column.ColumnName is "bill_amount" or "amount"))field.LogicalType="double";
        catalog.ConfigColumns.Add(new(){Id="900",ConfigTableId="1",ColumnName="measurement",LogicalType="double"});return MySqlNonRedundantReadCompiler.Compile(catalog,"1",["id","bill_amount","measurement"]);
    }
    /// <summary>真实ORM只提供类型上下文，实际连接始终来自下方受控ADO。</summary>
    private static DatabaseSession Session(AggregateProvider provider)=>new(new(){Provider="mysql",ExpectedDatabase="offline"},provider.CreateOrm(new()),provider);

    /// <summary>只返回预声明的SUM结果并记录SQL及绑定值，任何写入均拒绝。</summary>
    private sealed class AggregateProvider:IDatabaseProvider
    {
        internal object?[] Values=[];internal long Total=1;internal int Connections;internal List<string> Commands=[];internal Dictionary<string,object?> Parameters=[];
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);
        public void Validate(DatabaseOptions options){}
        public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);
        public string ParameterPlaceholder(string name)=>"@"+name;
        public IFreeSql CreateOrm(DatabaseOptions options)=>new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("禁止网络连接")).UseAutoSyncStructure(false).Build();
        public DbConnection CreateConnection(DatabaseOptions options){Connections++;return new Connection(this);}
    }
    /// <summary>读取连接仅支持本轮SELECT，不提供事务或网络。</summary>
    private sealed class Connection(AggregateProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string name)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>throw new NotSupportedException();protected override DbCommand CreateDbCommand()=>new Command(this,provider);
    }
    /// <summary>结果表交真实DbDataReader解析；驱动参数集合不插值进SQL。</summary>
    private sealed class Command(DbConnection connection,AggregateProvider provider):DbCommand
    {
        private readonly DbCommand parameters=new MySqlConnector.MySqlCommand();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}=CommandType.Text;public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>parameters.CreateParameter();public override int ExecuteNonQuery()=>throw new InvalidOperationException("禁止写入");public override object? ExecuteScalar()=>throw new InvalidOperationException("未声明标量查询");
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Assert.StartsWith("SELECT COUNT(*) AS total",CommandText);provider.Commands.Add(CommandText);provider.Parameters=Parameters.Cast<DbParameter>().ToDictionary(parameter=>parameter.ParameterName,parameter=>(object?)parameter.Value);
            var table=new DataTable();table.Columns.Add("total",typeof(long));for(var index=0;index<provider.Values.Length;index++)table.Columns.Add("sum_"+index,typeof(object));table.Rows.Add(new object?[]{provider.Total}.Concat(provider.Values.Select(value=>value??DBNull.Value)).ToArray());return table.CreateDataReader();
        }
        protected override void Dispose(bool disposing){if(disposing)parameters.Dispose();base.Dispose(disposing);}
    }
}
