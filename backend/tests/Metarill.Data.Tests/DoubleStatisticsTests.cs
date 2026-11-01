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
        var sql=Assert.Single(provider.Commands);Assert.Contains("SUM(",sql);Assert.Contains("LEFT JOIN",sql);Assert.Contains("LIMIT 2048",sql);Assert.Contains("DECIMAL(65,0)",sql);Assert.DoesNotContain("ORDER BY",sql);Assert.Contains("54",provider.Parameters.Values.Select(value=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)));
    }
    /// <summary>数据库空合计保留null，由原页脚按既有合同显示零，不借空集返回非有限值。</summary>
    [Fact]
    public async Task Empty_aggregate_results_preserve_nulls_in_both_numeric_families()
    {
        var provider=new AggregateProvider{Values=[null,null,null],Total=0};using var session=Session(provider);
        var result=await new MySqlNonRedundantReader(session).ReadStatisticsAsync(Plan(),[],["bill_amount","measurement","id"],projectId:"54");
        Assert.Equal(0,result.Total);Assert.Null(result.DoubleSums["bill_amount"]);Assert.Null(result.DoubleSums["measurement"]);Assert.Null(result.Sums["id"]);
    }
    /// <summary>驱动返回非法整数系数时不能进入响应或被清空。</summary>
    [Theory][InlineData("NaN")][InlineData("Infinity")][InlineData("-Infinity")][InlineData("1e999")][InlineData("1e-999")][InlineData("wrong")]
    public async Task Invalid_driver_bucket_coefficient_is_rejected(string value)
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
    /// <summary>错误计数、重复桶、越界指数或不完整混合范围不得伪造合法零合计。</summary>
    [Theory]
    [InlineData("rows")][InlineData("values")][InlineData("duplicate")][InlineData("exponent")][InlineData("coefficient")][InlineData("scope")][InlineData("null-coefficient")][InlineData("field")]
    public async Task Malformed_bucket_or_mixed_scope_is_rejected(string defect)
    {
        var provider=new AggregateProvider{Values=[1.25e100,-3.5e-200,9007199254740993m],Defect=defect};using var session=Session(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadStatisticsAsync(Plan(),[],["bill_amount","measurement","id"],projectId:"54"));Assert.Single(provider.Commands);
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
        internal object?[] Values=[];internal string? Defect;internal long Total=1;internal int Connections;internal List<string> Commands=[];internal Dictionary<string,object?> Parameters=[];
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
            Assert.StartsWith("SELECT",CommandText);Assert.Contains("field_no",CommandText);Assert.Contains("DECIMAL(65,0)",CommandText);provider.Commands.Add(CommandText);provider.Parameters=Parameters.Cast<DbParameter>().ToDictionary(parameter=>parameter.ParameterName,parameter=>(object?)parameter.Value);
            var mixed=provider.Values.Length==3;var floating=mixed?2:1;var exact=mixed?1:0;
            var table=new DataTable();table.Columns.Add("field_no",typeof(int));table.Columns.Add("exponent",typeof(object));table.Columns.Add("coefficient",typeof(string));table.Columns.Add("rows",typeof(long));table.Columns.Add("values",typeof(long));if(mixed)table.Columns.Add("exact",typeof(object));table.Columns.Add("cross",typeof(int));
            if(mixed)table.Rows.Add(-1,DBNull.Value,DBNull.Value,provider.Total,0L,provider.Values[2]??DBNull.Value,0);
            if(provider.Total>0)for(var index=0;index<floating;index++)
            {
                object exponent=DBNull.Value,coefficient=DBNull.Value;var value=provider.Values[index];
                if(value is string invalid){exponent=0;coefficient=invalid;}
                else if(value is double number)
                {
                    // 传输夹具按独立IEEE位型提供整数桶，不使用产品SQL或归并器伪造正确性。
                    var bits=BitConverter.DoubleToInt64Bits(number);var stored=(int)((bits>>52)&0x7ff);var mantissa=bits&0x000f_ffff_ffff_ffffL;if(stored!=0)mantissa|=1L<<52;exponent=stored==0?-1074:stored-1075;coefficient=(bits<0?-new System.Numerics.BigInteger(mantissa):new System.Numerics.BigInteger(mantissa)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                var row=new List<object?>{index,exponent,coefficient,provider.Total,value is null?0L:provider.Total};if(exact>0)row.Add(DBNull.Value);row.Add(0);table.Rows.Add(row.ToArray());
            }
            if(provider.Defect is {} defect)
            {
                var row=table.Rows.Cast<DataRow>().First(value=>(int)value[0]>=0);
                switch(defect)
                {
                    case "rows":row[3]=0L;break;
                    case "values":row[4]=(long)row[3]+1;break;
                    case "duplicate":table.Rows.Add((object[])row.ItemArray.Clone());break;
                    case "exponent":row[1]=972;break;
                    case "coefficient":row[2]="36028797018963969";break;
                    case "scope":table.Rows[0][3]=2L;break;
                    case "null-coefficient":row[2]=DBNull.Value;break;
                    case "field":row[0]=199;break;
                }
            }
            return table.CreateDataReader();
        }
        protected override void Dispose(bool disposing){if(disposing)parameters.Dispose();base.Dispose(disposing);}
    }
}
