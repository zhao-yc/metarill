using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>明确关联在写入前核对目标记录；更新只检查实际提交的关系字段。</summary>
internal static class ForeignValidationRenderer
{
    /// <summary>绑定元数据声明的目标，未知外部控件不会按任意表名查询。</summary>
    public static TableModel? Target(GenerationModel model, ColumnModel column)
    {
        if (!string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)) return model.Tables.SingleOrDefault(table => table.Source.Id == column.Source.LinkConfigTableId);
        if (column.Source.ControlKind == "MultiCheckBoxDatabase") return model.Tables.SingleOrDefault(table => table.Source.TableName == column.Source.ControlDetail);
        if (column.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage" && !string.IsNullOrWhiteSpace(column.Source.ControlDetail))
        {
            using var detail = JsonDocument.Parse(column.Source.ControlDetail);
            if (detail.RootElement.TryGetProperty("tableId", out var tableId)) return model.Tables.SingleOrDefault(table => table.Source.Id == tableId.ToString());
            if (detail.RootElement.TryGetProperty("table", out var tableName)) return model.Tables.SingleOrDefault(table => table.Source.TableName == tableName.ToString());
            if (detail.RootElement.TryGetProperty("pageId", out var pageId)) return model.Tables.SingleOrDefault(table => table.Pages.Any(page => page.Source.Id == pageId.ToString()));
        }
        var suffix = column.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal) ? "_ids" : "_id";
        if (!column.Source.ColumnName.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var name = column.Source.ColumnName[..^suffix.Length];
        return model.Tables.SingleOrDefault(table => table.Source.TableName == name);
    }
    /// <summary>调用 Service 规则后的实体值必须仍指向当前项目有效记录。</summary>
    public static string Render(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder().AppendLine("    /// <summary>不接受跨项目或已删除的关联；省略字段不触发覆盖或清空。</summary>")
            .AppendLine($"    private async Task ValidateRelations{page.Source.Id}Async({table.Name} entity, RuntimeActor actor, bool creating, ISet<string> submitted, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        await Task.CompletedTask;");
        var number = 0;
        foreach (var column in table.Columns.Where(column => !column.IsSystem && (column.Type is "long?" or "string?")))
        {
            var target = Target(model, column);
            var knownRelation = column.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase" || !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId);
            if (target is null && !knownRelation) continue;
            number++;
            result.AppendLine($"        if ((creating || submitted.Contains({Literal(column.Source.ColumnName)})) && entity.{column.Property} is not null)").AppendLine("        {");
            if (target is null)
                result.AppendLine($"            throw new InvalidOperationException({Literal("关联目标尚未移植：" + column.Source.Name)});");
            else
            {
                if (column.IsJsonArray)
                    result.AppendLine($"            using var relation{number} = JsonDocument.Parse(entity.{column.Property});")
                        .AppendLine($"            if (relation{number}.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException(\"多值关联必须为数组。\");")
                        .AppendLine($"            var relationIds{number} = relation{number}.RootElement.EnumerateArray().Select(value => long.Parse(value.ToString(), CultureInfo.InvariantCulture)).Distinct().ToArray();");
                else
                    result.AppendLine($"            var relationIds{number} = new[] {{ {IdExpression(column)} }};");
                result.AppendLine($"            var relationQuery{number} = session.Select<{target.Name}>().Where(row => relationIds{number}.Contains(row.Id));");
                if (target.Columns.Any(candidate => candidate.Source.ColumnName == "delete_no")) result.AppendLine($"            relationQuery{number} = relationQuery{number}.Where(row => row.DeleteNo == null);");
                var project = target.Columns.SingleOrDefault(candidate => candidate.Source.ColumnName == "yz_project_id");
                if (project is not null && !target.IsGlobal)
                    result.AppendLine($"            relationQuery{number} = relationQuery{number}.Where(row => row.{project.Property} == {ProjectExpression(project)});");
                else if (project is null && !target.IsGlobal)
                    result.AppendLine($"            if (relationIds{number}.Length != 0) throw new InvalidOperationException(\"关联目标未声明项目归属，需要明确访问规则。\");");
                result.AppendLine($"            if (await relationQuery{number}.CountAsync(cancellationToken) != relationIds{number}.Length) throw new InvalidOperationException({Literal("关联记录不存在、已删除或不属于当前项目：" + column.Source.Name)});");
            }
            result.AppendLine("        }");
        }
        return result.AppendLine("    }").ToString();
    }
    /// <summary>关联主键只支持显式整数或整数文本。</summary>
    private static string IdExpression(ColumnModel column) => column.Type == "string?" ? $"long.Parse(entity.{column.Property}, CultureInfo.InvariantCulture)" : $"entity.{column.Property}.Value";
    /// <summary>目标项目键按其持久化类型匹配真实身份。</summary>
    private static string ProjectExpression(ColumnModel column) => column.Type == "string?" ? "actor.ProjectId" : "long.Parse(actor.ProjectId, CultureInfo.InvariantCulture)";
    /// <summary>对生成字符串进行正确转义。</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
}
