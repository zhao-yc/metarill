using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>页级导出仍使用原查询范围和列定义，文件与票据交给共享导出服务。</summary>
internal static class ExportRenderer
{
    /// <summary>保留旧 exportMethod，也支持明确的导出开关。</summary>
    public static bool Enabled(PageModel page) => page.Source.IsExport == 1 || Mode(page) is "list" or "edit";
    /// <summary>未指定时使用列表列，编辑导出明确使用详情列。</summary>
    private static string Mode(PageModel page) => page.Source.Extra.TryGetValue("exportMethod", out var mode) ? mode.ToString() : "";
    /// <summary>定制组件没有通用 Excel 表示，不能自动导出内部配置。</summary>
    private static IReadOnlyList<ColumnModel> Columns(PageModel page) => (Mode(page) == "edit" ? page.DetailColumns : page.ListColumns).Where(column => column.Source.ControlKind != "CustomControl").ToArray();
    /// <summary>沿用 initExport 返回票据，下载入口由宿主统一承接。</summary>
    public static string Controller(PageModel page) => !Enabled(page) ? "" : $$"""
    /// <summary>使用当前页导出权限生成有身份约束的下载票据。</summary>
    [HttpPost("initExport")]
    public async Task<R<string>> InitExport([FromBody] PageRequest request, [FromServices] Metarill.Core.Exports.IPageExportService exports, CancellationToken cancellationToken)
    {
        var actor = Actor("export");
        return R<string>.Success(await exports.CreateAsync(await service.Export{{page.Source.Id}}Async(request, actor, cancellationToken), actor, cancellationToken));
    }

""";
    /// <summary>先按页权限查询并固定字段投影，再批量补充关系名称。</summary>
    public static string Service(GenerationModel model, TableModel table, PageModel page)
    {
        if (!Enabled(page)) return "";
        var columns = Columns(page);
        var result = new StringBuilder().AppendLine("    /// <summary>导出复用列表范围和筛选，字段列表由元数据固定，不能来自客户端。</summary>")
            .AppendLine($"    public async Task<Metarill.Core.Exports.PageExportRequest> Export{page.Source.Id}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine($"        var query = Order{page.Source.Id}(Query{page.Source.Id}(request, actor), request);")
            .AppendLine($"        var entities = await query.ToListAsync(entity => new {table.Name}").AppendLine("        {");
        foreach (var column in columns.Concat(table.Columns.Where(column => column.IsId)).DistinctBy(column => column.Source.Id)) result.AppendLine($"            {column.Property} = entity.{column.Property},");
        result.AppendLine("        }, cancellationToken);");
        var relations = RelationCompilation.ForPage(model, page).Where(relation => columns.Contains(relation.SourceColumn)).ToArray();
        foreach (var relation in relations)
        {
            var name = relation.SourceColumn.Property;
            var sourceId = relation.SourceColumn.Type == "string?" ? $"long.Parse(entity.{name}!, CultureInfo.InvariantCulture)" : $"entity.{name}!.Value";
            result.AppendLine($"        var ids{name} = entities.Where(entity => entity.{name} is not null).Select(entity => {sourceId}).Distinct().ToArray();")
                .AppendLine($"        var query{name} = session.Select<{relation.Target.Name}>().Where(entity => ids{name}.Contains(entity.Id));");
            if (relation.Target.Columns.Any(column => column.Source.ColumnName == "delete_no")) result.AppendLine($"        query{name} = query{name}.Where(entity => entity.DeleteNo == null);");
            var project = relation.Target.Columns.SingleOrDefault(column => column.Source.ColumnName == "yz_project_id");
            if (project is not null && (!relation.Target.IsGlobal || relation.TargetPage.Scopes.Concat(relation.Scopes).Any(scope => scope.Source == "project")))
                result.AppendLine($"        query{name} = query{name}.Where(entity => entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
            foreach (var (column, source) in relation.TargetPage.Scopes.Concat(relation.Scopes).Where(scope => scope.Source == "parent"))
                result.AppendLine("        if (string.IsNullOrWhiteSpace(request.ParentId)) throw new ArgumentException(\"导出关系要求父记录上下文。\");")
                    .AppendLine($"        query{name} = query{name}.Where(entity => entity.{column.Property} == {ScopeValue(column, "request.ParentId")});");
            result.AppendLine($"        var rows{name} = await query{name}.ToListAsync(cancellationToken);")
                .AppendLine($"        var names{name} = rows{name}.ToDictionary(entity => entity.Id, entity => entity.{relation.NameColumn.Property});");
        }
        result.AppendLine("        var rows = entities.Select(entity => (IReadOnlyList<object?>)new object?[]").AppendLine("        {");
        foreach (var column in columns)
        {
            var relation = relations.SingleOrDefault(relation => relation.SourceColumn == column);
            result.AppendLine("            " + ValueExpression(column, relation) + ",");
        }
        result.AppendLine("        }).ToArray();")
            .AppendLine($"        return new({Literal(page.Source.Id)}, {Literal(page.Source.Name)}, [{string.Join(", ", columns.Select(column => Literal(string.IsNullOrWhiteSpace(column.Source.Name) ? column.JsonName : column.Source.Name)))}], rows);")
            .AppendLine("    }");
        return result.ToString();
    }
    /// <summary>关系导出名称，64位整数始终按文本输出避免 Excel 精度损失。</summary>
    private static string ValueExpression(ColumnModel column, RelationModel? relation)
    {
        var access = "entity." + column.Property;
        if (relation is not null)
        {
            var id = column.Type == "string?" ? $"long.Parse({access}, CultureInfo.InvariantCulture)" : access + ".Value";
            return $"{access} is null ? null : names{column.Property}.GetValueOrDefault({id}) ?? Convert.ToString({access}, CultureInfo.InvariantCulture)";
        }
        if (column.Type == "long") return access + ".ToString(CultureInfo.InvariantCulture)";
        if (column.Type == "long?") return access + "?.ToString(CultureInfo.InvariantCulture)";
        if (column.Source.ControlKind is "ConstList" or "ConstDownList")
        {
            var choices = column.Source.ControlDetail.Split(',').Select(value => value.Split('|', 2)).Where(pair => pair.Length == 2).GroupBy(pair => pair[1], StringComparer.Ordinal).Select(group => group.First()).ToArray();
            if (choices.Length == 0) return access;
            return access + " is null ? null : Convert.ToString(" + access + ", CultureInfo.InvariantCulture) switch { " + string.Join(", ", choices.Select(pair => Literal(pair[1]) + " => " + Literal(pair[0]))) + ", var raw => raw }";
        }
        return access;
    }
    /// <summary>关系范围只使用当前真实身份和已声明字段类型。</summary>
    private static string ScopeValue(ColumnModel column, string value) => column.Type switch { "string?" => value, "long?" or "long" => $"long.Parse({value}, CultureInfo.InvariantCulture)", "int?" => $"int.Parse({value}, CultureInfo.InvariantCulture)", _ => throw new InvalidOperationException("导出关系范围类型无效。") };
    /// <summary>正确转义生成的常量。</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
}
