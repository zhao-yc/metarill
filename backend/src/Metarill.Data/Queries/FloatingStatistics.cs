using System.Globalization;
using System.Numerics;
using System.Reflection;
using FreeSql;
using FreeSql.DataAnnotations;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>普通浮点统计使用固定物理列分桶，不使用会静默返回零的原生double SUM。</summary>
public static class FloatingStatistics
{
    /// <summary>来源query已经绑定页范围；这里只接收生成期固定字段，不接受客户端SQL或任意列。</summary>
    public static async Task<double?> SumAsync<T>(DatabaseSession session,ISelect<T> query,string column,CancellationToken cancellationToken=default)where T:class
    {
        ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(query);MetadataValidation.RequireIdentifier(column);
        if(session.Provider.Name!="mysql")throw new NotSupportedException("该数据库尚未完成浮点统计适配。");
        var member=typeof(T).GetProperties().SingleOrDefault(property=>property.GetCustomAttribute<ColumnAttribute>()?.Name==column);
        if(member is null||(Nullable.GetUnderlyingType(member.PropertyType)??member.PropertyType)!=typeof(double))throw new InvalidOperationException("浮点统计字段不是当前生成实体的固定double列。");
        // 不改已编译来源query的别名，避免破坏其中的项目／父范围谓词。
        var value=session.Provider.QuoteIdentifier(column);
        var rows=await query.OrderBy((string)null!).Offset(0).Limit(MySqlFloatingSum.MaximumBuckets+1)
            .GroupBy(MySqlFloatingSum.Exponent(value)).ToListAsync<FloatingSumRow>(MySqlFloatingSum.Fields(value),cancellationToken);
        return MySqlFloatingSum.Reduce(rows).Value;
    }
}

/// <summary>仅作受控聚合行映射，不是业务表或API响应。</summary>
internal sealed class FloatingSumRow
{
    public FloatingSumRow(){}
    public int? Exponent{get;set;}public string? Coefficient{get;set;}public long Rows{get;set;}public long Values{get;set;}
}
internal sealed record FloatingSumResult(long Rows,double? Value);

/// <summary>MySQL浮点指数范围固定，桶内系数以DECIMAL(65,0)求和后按文本返回。</summary>
internal static class MySqlFloatingSum
{
    internal const int MaximumBuckets=2047;
    /// <summary>多留一位有效整数位抵御LOG2在二次幂边界的舍入，最小单位夹在-1074。</summary>
    internal static string Exponent(string value)=>"CASE WHEN "+value+" IS NULL THEN NULL WHEN "+value+"=0 THEN -1074 ELSE GREATEST(-1074,CAST(FLOOR(LOG2(ABS("+value+"))) AS SIGNED)-53) END";
    /// <summary>系数最多约54位二进制整数；行数上限下的求和完全落在65位十进制内。</summary>
    internal static string Coefficient(string value)
    {
        var exponent=Exponent(value);return "CAST(SUM(CAST(("+value+")/CASE WHEN ("+exponent+")=-1074 THEN 4.9406564584124654e-324 ELSE POW(2,("+exponent+")) END AS DECIMAL(65,0))) AS CHAR)";
    }
    /// <summary>外层LIMIT只约束聚合桶，绝不截断参与统计的明细行。</summary>
    internal static string Fields(string value)=>Exponent(value)+" AS `Exponent`,"+Coefficient(value)+" AS `Coefficient`,COUNT(*) AS `Rows`,COUNT("+value+") AS `Values`";
    /// <summary>每桶计数、系数和指数均验证；空字段与零字段严格区分。</summary>
    internal static FloatingSumResult Reduce(IEnumerable<FloatingSumRow> rows)
    {
        var sum=new BinaryDoubleSum();long count=0;var buckets=new HashSet<int?>();
        foreach(var row in rows)
        {
            if(!buckets.Add(row.Exponent)||buckets.Count>MaximumBuckets||row.Rows<=0||row.Values<0||row.Values>row.Rows)throw new InvalidOperationException("浮点统计桶数量或计数无效。");
            count=checked(count+row.Rows);
            if(row.Values==0){if(row.Exponent is not null||row.Coefficient is not null)throw new InvalidOperationException("空值统计桶格式无效。");continue;}
            if(row.Values!=row.Rows||row.Exponent is not {} exponent||row.Coefficient is not {} coefficient||coefficient.Length>66
                ||!BigInteger.TryParse(coefficient,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var integer)||BigInteger.Abs(integer)>(BigInteger.One<<55)*row.Values)throw new InvalidOperationException("浮点统计桶系数与计数不符。");
            sum.AddBucket(exponent,coefficient);
        }
        return new(count,sum.Value());
    }
}
