namespace Metarill.Data.Queries;

/// <summary>SQL 侧的标量名称与运行时文本保持同一布尔、十进制及日历格式。</summary>
internal static class MySqlMultiValueLabel
{
    /// <summary>表达式与类型只来自已编译元数据。null 随原查询保留。</summary>
    internal static string Text(string expression, string kind)
    {
        var cast = "CAST(" + expression + " AS CHAR)";
        return kind switch
        {
            "string" => expression,
            "bool" => "CASE WHEN " + expression + " IS NULL THEN NULL WHEN " + expression + " <> 0 THEN 'true' ELSE 'false' END",
            "double" => DoubleText(expression),
            "decimal" or "datetime" or "time" => "CASE WHEN INSTR(" + cast + ", '.') > 0 THEN TRIM(TRAILING '.' FROM TRIM(TRAILING '0' FROM " + cast + ")) ELSE " + cast + " END",
            _ => cast
        };
    }

    /// <summary>MySQL 的 DOUBLE 文本已保留往返精度。统一小数点和指数的呈现，供筛选和缓存共用。</summary>
    private static string DoubleText(string expression)
    {
        var raw="LOWER(CAST(ABS("+expression+") AS CHAR))";
        var mantissa="SUBSTRING_INDEX("+raw+",'e',1)";
        var rawDigits="REPLACE("+mantissa+",'.','')";
        var leadingTrimmed="TRIM(LEADING '0' FROM "+rawDigits+")";
        var digits="TRIM(TRAILING '0' FROM "+leadingTrimmed+")";
        var suppliedExponent="CASE WHEN INSTR("+raw+",'e')>0 THEN CAST(SUBSTRING_INDEX("+raw+",'e',-1) AS SIGNED) ELSE 0 END";
        var point="("+suppliedExponent+"+CAST(CHAR_LENGTH(SUBSTRING_INDEX("+mantissa+",'.',1)) AS SIGNED)"+
            "-CAST(CHAR_LENGTH("+rawDigits+") AS SIGNED)+CAST(CHAR_LENGTH("+leadingTrimmed+") AS SIGNED))";
        var sign="CASE WHEN "+expression+"<0 THEN '-' ELSE '' END";
        var scientific="CONCAT(LEFT("+digits+",1),CASE WHEN CHAR_LENGTH("+digits+")>1 THEN CONCAT('.',SUBSTRING("+
            digits+",2)) ELSE '' END,'e',"+point+"-1)";
        var plain="CASE WHEN "+point+"<=0 THEN CONCAT('0.',REPEAT('0',-"+point+"),"+digits+") WHEN "+point+
            ">=CHAR_LENGTH("+digits+") THEN RPAD("+digits+","+point+",'0') ELSE CONCAT(LEFT("+digits+","+point+
            "),'.',SUBSTRING("+digits+","+point+"+1)) END";
        return "CASE WHEN "+expression+" IS NULL THEN NULL WHEN "+expression+"=0 THEN '0' ELSE CONCAT("+sign+
            ",CASE WHEN "+point+"< -5 OR "+point+">21 THEN "+scientific+" ELSE "+plain+" END) END";
    }
}
