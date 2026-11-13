using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Metarill.Data.Schema;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

public sealed partial class MySqlFloatingParameterReadOnlyTests
{
    /// <summary>真实执行规划器生成的默认常量，固定二进制参数作独立参照；只查合成SELECT，不DDL。</summary>
    [FloatingParameterReadOnlyFact]
    public async Task Planned_double_literals_preserve_bits_in_real_mysql_synthetic_selects()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var token=timeout.Token;
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FLOAT_READONLY_SETTINGS")!,token));var options=JsonSerializer.Deserialize<DatabaseOptions>(Property(Property(settings.RootElement,"Metarill"),"BusinessDatabase"),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);Assert.False(parsed.UserID.Equals("root",StringComparison.OrdinalIgnoreCase));Assert.False(parsed.IgnorePrepare);var provider=new MySqlDatabaseProvider();await using var connection=provider.CreateConnection(options);await connection.OpenAsync(token);await Identity(connection,options,token);
        var values=new List<double>{0d,double.MaxValue,-double.MaxValue};for(var exponent=-1074;exponent<=1023;exponent++){var value=Math.ScaleB(1d,exponent);values.Add(value);values.Add(-value);}
        var random=new Random(71926);var bytes=new byte[8];for(var index=0;index<4096;index++){random.NextBytes(bytes);var value=BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes));if(double.IsFinite(value))values.Add(value);}
        using var orm=provider.CreateOrm(options);orm.Aop.CommandBefore+=(_,args)=>SelectOnly(args.Command.CommandText);var visited=new HashSet<int>();
        for(var offset=0;offset<values.Count;offset+=128)
        {
            var indices=Enumerable.Range(offset,Math.Min(128,values.Count-offset)).ToArray();var parameters=new List<DbParameter>();var statements=new List<string>();
            foreach(var index in indices)
            {
                var value=values[index];var column=MySqlSchemaPlanner.CompileColumn(new(){ColumnName="value",LogicalType="double",DefaultValue=value.ToString("G17",CultureInfo.InvariantCulture)});parameters.Add(new MySqlParameter("reference"+index,MySqlDbType.Double){Value=value});
                statements.Add("SELECT "+index+" AS `Number`,CAST(@reference"+index+" AS DOUBLE) AS `Input`,CAST("+column.DefaultSql+" AS DOUBLE) AS `Planned`");
            }
            // 显式使用已核验的同一连接，生产命令工厂自行准备；测试钩子不Prepare或替换值。
            var rows=await orm.Ado.QueryAsync<DefaultLiteralRow>(connection,null,CommandType.Text,string.Join(" UNION ALL ",statements),20,parameters.ToArray(),token);Assert.Equal(indices.Length,rows.Count);
            foreach(var row in rows){Assert.InRange(row.Number,offset,offset+indices.Length-1);Assert.True(visited.Add(row.Number));var expected=BitConverter.DoubleToInt64Bits(values[row.Number]);Assert.Equal(expected,BitConverter.DoubleToInt64Bits(row.Input));Assert.Equal(expected,BitConverter.DoubleToInt64Bits(row.Planned));}
        }
        Assert.Equal(values.Count,visited.Count);await Identity(connection,options,token);
    }
    /// <summary>仅映射合成结果，不是业务实体，也不参与结构同步。</summary>
    private sealed class DefaultLiteralRow{public int Number{get;set;}public double Input{get;set;}public double Planned{get;set;}}
}
