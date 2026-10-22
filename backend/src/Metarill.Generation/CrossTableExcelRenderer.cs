using System.Text.Json;
namespace Metarill.Generation;

/// <summary>跨表Excel先接通无歧义的空单元格清空，非空路径语义待用户决定。</summary>
internal static class CrossTableExcelRenderer
{
    /// <summary>不把未确认的末级回退或新路径格式偷偷作为可用导入。</summary>
    internal static string Import(PageModel page,ColumnModel source)=>$"    /// <summary>空单元格按原清空处理；非空跨表路径策略仍待确认。</summary>\n    private Task<JsonElement> ImportCascade{page.Source.Id}_{source.Source.Id}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)\n    {{\n        cancellationToken.ThrowIfCancellationRequested();if(session.CurrentTransaction is null)throw new InvalidOperationException(\"跨表导入要求来源事务。\");\n        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return Task.FromResult(JsonSerializer.SerializeToElement<object?>(null));\n        throw new NotSupportedException(\"非空跨表路径导入策略尚待确认，未写入。\");\n    }}\n";
    /// <summary>单元格明确清空时准备全部级ID；显式同一行祖先字段保留并与JSON顺序无关。</summary>
    internal static string PrepareIds(CrossTableWriteModel plan,IReadOnlyList<ColumnModel> columns)
    {
        return string.Concat(plan.Levels.Where(level=>level.Id.Source.Id!=plan.Read.Source.Source.Id).Select(level=>
        {
            var declared=columns.SingleOrDefault(column=>column.Source.Id==level.Id.Source.Id);var absent=declared is null?"":"if(!row.TryGetProperty("+Literal(ImportRenderer.Header(declared))+",out _))";
            return $"                        {absent}values[{Literal(level.Id.JsonName)}]=JsonSerializer.SerializeToElement<object?>(null);\n";
        }));
    }
    /// <summary>字段/标题只作为字符串字面量，不进入动态代码。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
