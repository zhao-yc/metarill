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
            "decimal" or "datetime" or "time" => "CASE WHEN INSTR(" + cast + ", '.') > 0 THEN TRIM(TRAILING '.' FROM TRIM(TRAILING '0' FROM " + cast + ")) ELSE " + cast + " END",
            _ => cast
        };
    }
}
