using System.Data;
using System.Data.Common;
using System.Text.Json;
using FreeSql;
using FreeSql.DataAnnotations;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>独立只读开关，不复用允许建表／清理的实库开关；缺少任一固定实例输入即跳过。</summary>
public sealed class FloatingParameterReadOnlyFactAttribute:FactAttribute
{
    public FloatingParameterReadOnlyFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_ACKNOWLEDGEMENT")!="SELECT_ONLY"||
            !File.Exists(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_SETTINGS"))||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_HOSTNAME"))||
            !Guid.TryParse(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_SERVER_UUID"),out _))Skip="未显式配置浮点参数只读实例及SELECT_ONLY确认。";
    }
}

/// <summary>真实生产ORM与驱动只执行合成SELECT，不创建持久对象或读取现有业务行。</summary>
public sealed class MySqlFloatingParameterReadOnlyTests
{
    /// <summary>固定二进制位型、真实等值筛选、混合类型、六种执行入口及IgnorePrepare明确拒绝。</summary>
    [FloatingParameterReadOnlyFact]
    public async Task Production_commands_preserve_floating_parameters_without_database_writes()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var token=timeout.Token;
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_SETTINGS")!,token));
        var options=JsonSerializer.Deserialize<DatabaseOptions>(Property(Property(settings.RootElement,"Metarill"),"BusinessDatabase"),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);Assert.False(parsed.UserID.Equals("root",StringComparison.OrdinalIgnoreCase));Assert.False(parsed.IgnorePrepare);
        var provider=new MySqlDatabaseProvider();provider.Validate(options);await using var connection=provider.CreateConnection(options);await connection.OpenAsync(token);await Identity(connection,options,token);
        var values=new[]{Math.ScaleB(1d,-958),-Math.ScaleB(1d,-958),double.MaxValue,-double.MaxValue,double.Epsilon,-double.Epsilon,Math.BitIncrement(double.Epsilon),Math.ScaleB(1d,-1022),Math.BitDecrement(Math.ScaleB(1d,-1022)),Math.BitIncrement(1d),Math.BitDecrement(1d),1.25e100,-3.5e-200,0d};
        using var orm=provider.CreateOrm(options);orm.Aop.CommandBefore+=(_,args)=>SelectOnly(args.Command.CommandText);
        foreach(var value in values)
        {
            // 这里使用生产ADO命令工厂，不能在测试钩子中手动Prepare帮助它通过。
            var actual=await orm.Ado.QuerySingleAsync<double>(CommandType.Text,"SELECT CAST(@value AS DOUBLE)",[new MySqlParameter("value",MySqlDbType.Double){Value=value}],token);
            Assert.Equal(BitConverter.DoubleToInt64Bits(value),BitConverter.DoubleToInt64Bits(actual));
        }
        ISelect<Measure> Query()=>orm.Select<Measure>().AsTable((_,_)=>"(SELECT 1 id,POW(2,-958) amount)");var expected=Math.ScaleB(1d,-958);
        Assert.Equal(1,await Query().Where(row=>row.Amount==expected).CountAsync(token));Assert.Equal(0,await Query().Where(row=>row.Amount==Math.BitDecrement(expected)).CountAsync(token));
        using(var session=new DatabaseSession(options,orm))Assert.Equal(expected,await Metarill.Data.Queries.FloatingStatistics.SumAsync(session,Query().Where(row=>row.Amount==expected),"amount",token));
        // 有效原事务沿同一真实连接，ORM与基础设施包装均不能另开连接绕开它。
        await using(var transaction=await connection.BeginTransactionAsync(token))
        {
            try
            {
                Assert.Equal(1,await Query().WithTransaction(transaction).Where(row=>row.Amount==expected).CountAsync(token));
                foreach(var kind in new[]{"scalar","reader","write"})foreach(var asynchronous in new[]{false,true})
                {
                    using var inner=new MySqlCommand("SELECT CAST(@value AS DOUBLE)",(MySqlConnection)connection,(MySqlTransaction)transaction);inner.Parameters.AddWithValue("value",expected);await using var command=MySqlFloatingCommand.Wrap(inner);
                    if(kind=="scalar"){var value=asynchronous?await command.ExecuteScalarAsync(token):command.ExecuteScalar();Assert.Equal(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(Assert.IsType<double>(value)));}
                    else if(kind=="reader"){await using var reader=asynchronous?await command.ExecuteReaderAsync(token):command.ExecuteReader();Assert.True(await reader.ReadAsync(token));Assert.Equal(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(reader.GetDouble(0)));}
                    else Assert.Equal(-1,asynchronous?await command.ExecuteNonQueryAsync(token):command.ExecuteNonQuery());
                    Assert.True(inner.IsPrepared);Assert.Same(connection,command.Connection);Assert.Same(transaction,command.Transaction);
                }
            }
            finally{await transaction.RollbackAsync(CancellationToken.None);}
        }
        var moment=new DateTime(2026,11,2,3,4,5).AddTicks(1234560);var clock=TimeSpan.FromHours(26)+TimeSpan.FromTicks(1234560);var bytes=new byte[]{0,1,255};
        using(var inner=new MySqlCommand("SELECT CAST(@floating AS DOUBLE),CAST(@exact AS DECIMAL(30,0)),@name,CAST(@bytes AS BINARY),CAST(@moment AS DATETIME(6)),CAST(@clock AS TIME(6)),@empty",(MySqlConnection)connection))
        {
            inner.Parameters.AddWithValue("floating",expected);inner.Parameters.AddWithValue("exact",9007199254740993m);inner.Parameters.AddWithValue("name","中文'\\原值");inner.Parameters.AddWithValue("bytes",bytes);inner.Parameters.AddWithValue("moment",moment);inner.Parameters.AddWithValue("clock",clock);inner.Parameters.AddWithValue("empty",DBNull.Value);
            await using var command=MySqlFloatingCommand.Wrap(inner);await using var reader=await command.ExecuteReaderAsync(token);Assert.True(await reader.ReadAsync(token));Assert.Equal(expected,reader.GetDouble(0));Assert.Equal(9007199254740993m,reader.GetDecimal(1));Assert.Equal("中文'\\原值",reader.GetString(2));Assert.Equal(bytes,reader.GetFieldValue<byte[]>(3));Assert.Equal(moment,reader.GetDateTime(4));Assert.Equal(clock,reader.GetFieldValue<TimeSpan>(5));Assert.True(reader.IsDBNull(6));Assert.True(inner.IsPrepared);
        }
        using(var inner=new MySqlCommand("SELECT CAST(@exact AS DECIMAL(30,0))",(MySqlConnection)connection))
        {
            inner.Parameters.AddWithValue("exact",9007199254740993m);await using var command=MySqlFloatingCommand.Wrap(inner);Assert.Equal(9007199254740993m,Assert.IsType<decimal>(await command.ExecuteScalarAsync(token)));Assert.False(inner.IsPrepared);
        }
        var ignoring=new MySqlConnectionStringBuilder(options.ConnectionString){IgnorePrepare=true};await using(var ignoredConnection=new MySqlConnection(ignoring.ConnectionString))
        {
            await ignoredConnection.OpenAsync(token);using var inner=new MySqlCommand("SELECT CAST(@value AS DOUBLE)",ignoredConnection);inner.Parameters.AddWithValue("value",expected);await using var command=MySqlFloatingCommand.Wrap(inner);
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>command.ExecuteScalarAsync(token));Assert.Equal("浮点数据库参数需要启用MySQL预处理，不能忽略Prepare。",error.Message);Assert.False(inner.IsPrepared);
        }
        await Identity(connection,options,token);
    }
    /// <summary>首尾固定实例与库核验只读系统变量，不读取权限目录或SQL正文。</summary>
    private static async Task Identity(DbConnection connection,DatabaseOptions options,CancellationToken token)
    {
        await using var command=connection.CreateCommand();command.CommandText="SELECT @@hostname,@@server_uuid,DATABASE(),CURRENT_USER()";await using var rows=await command.ExecuteReaderAsync(token);Assert.True(await rows.ReadAsync(token));Assert.Equal(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_HOSTNAME"),rows.GetString(0));Assert.Equal(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_SERVER_UUID"),rows.GetString(1));Assert.Equal(options.ExpectedDatabase,rows.GetString(2));Assert.False(rows.GetString(3).StartsWith("root@",StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>测试中所有执行SQL必须为单条SELECT，禁止锁、文件及写入片段。</summary>
    private static void SelectOnly(string sql){Assert.StartsWith("SELECT",sql.Trim(),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain(';',sql);Assert.DoesNotContain("FOR UPDATE",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("OUTFILE",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("INTO ",sql,StringComparison.OrdinalIgnoreCase);}
    private static JsonElement Property(JsonElement value,string name)=>value.EnumerateObject().Single(property=>property.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
    [Table(Name="readonly_floating_parameter",DisableSyncStructure=true)]public sealed class Measure{[Column(Name="id",IsPrimary=true)]public long Id{get;set;}[Column(Name="amount")]public double Amount{get;set;}}
}
