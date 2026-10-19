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
    /// <summary>关系导出使用与控件相同的固定目标，不按客户端参数选择授权对象。</summary>
    private static RelationModel[] Relations(GenerationModel model, PageModel page) => RelationCompilation.ForPage(model, page).Where(relation => Columns(page).Contains(relation.SourceColumn)).ToArray();
    /// <summary>含关系名称的导出必须注入真实授权，独立宿主与平台使用同一Service注册合同。</summary>
    internal static bool RequiresAuthorization(GenerationModel model, TableModel table) => table.Pages.Any(page => Enabled(page) && Relations(model, page).Length > 0);
    /// <summary>沿用 initExport 返回票据，下载入口由宿主统一承接。</summary>
    public static string Controller(GenerationModel model, PageModel page) => !Enabled(page) ? "" : $$"""
    /// <summary>使用当前页导出权限生成原页面可使用的两分钟下载凭证。</summary>
    [HttpPost("initExport")]
    public async Task<R<string>> InitExport([FromBody] PageRequest request, [FromServices] Metarill.Core.Exports.IPageExportService exports, CancellationToken cancellationToken)
    {
        var actor = Actor("export");
{{string.Join("\n", Relations(model, page).Select(relation => relation.TargetPage.Source.Id).Distinct(StringComparer.Ordinal).Select(id => "        authorization.RequirePagePermission(" + Literal(id) + ", \"list\");"))}}
        return R<string>.Success(await exports.CreateDownloadAsync(actor, (maxRows, token) => service.Export{{page.Source.Id}}Async(request, actor, maxRows, token), cancellationToken));
    }

""";
    /// <summary>先按页权限查询并固定字段投影，再批量补充关系名称。</summary>
    public static string Service(GenerationModel model, TableModel table, PageModel page)
    {
        if (!Enabled(page)) return "";
        var columns = Columns(page); var relations = Relations(model, page);
        var contexts = relations.ToDictionary(relation => relation.SourceColumn.Source.Id, relation => ControlRenderer.RelationContext(page, relation, false));
        var dependencies = contexts.Values.Where(context => context.TargetParentField is not null).Select(context => table.Columns.Single(column => column.JsonName == context.TargetParentField));
        var result = new StringBuilder().AppendLine("    /// <summary>导出复用列表范围和固定列；关系名称另经真实授权和与候选相同的父范围读取。</summary>")
            .AppendLine($"    public async Task<Metarill.Core.Exports.PageExportRequest> Export{page.Source.Id}Async(PageRequest request, RuntimeActor actor, int maxRows, CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine("        if (maxRows is < 1 or > 1_048_575) throw new ArgumentOutOfRangeException(nameof(maxRows));");
        if (relations.Length > 0)
        {
            result.AppendLine("        var exportAuthorization = relationAuthorization ?? throw new UnauthorizedAccessException(\"关系导出需要宿主真实授权。\");")
                .AppendLine("        if (exportAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException(\"关系导出身份与当前真实身份不一致。\");")
                .AppendLine($"        exportAuthorization.RequirePagePermission({Literal(page.Source.Id)}, \"export\");");
            foreach (var targetPage in relations.Select(relation => relation.TargetPage.Source.Id).Distinct(StringComparer.Ordinal))
                result.AppendLine($"        exportAuthorization.RequirePagePermission({Literal(targetPage)}, \"list\");");
            result.AppendLine($"        await ValidateOptionsSourceParents{page.Source.Id}Async(request.ParentId, actor, cancellationToken);");
        }
        result.AppendLine($"        var query = Order{page.Source.Id}(Query{page.Source.Id}(request, actor), request);")
            .AppendLine($"        var entities = await query.Limit(maxRows + 1).ToListAsync(entity => new {table.Name}").AppendLine("        {");
        // 目标父依赖只在已有可读字段中取固定列，不因此增加导出的表头或VO字段。
        foreach (var column in columns.Concat(dependencies).Concat(table.Columns.Where(column => column.IsId)).DistinctBy(column => column.Source.Id)) result.AppendLine($"            {column.Property} = entity.{column.Property},");
        result.AppendLine("        }, cancellationToken);")
            .AppendLine("        if (entities.Count > maxRows) throw new InvalidOperationException(\"导出记录数量超过服务端上限，请缩小筛选范围后重试。\");");
        foreach (var relation in relations)
        {
            var name = relation.SourceColumn.Property; var context = contexts[relation.SourceColumn.Source.Id];
            var dependency = context.TargetParentField is null ? null : table.Columns.Single(column => column.JsonName == context.TargetParentField);
            var unsupported = context.UnresolvedTarget || dependency is not null && (dependency.IsJsonArray || dependency.Type is not ("string?" or "long?" or "long" or "int?"));
            var parent = dependency is null ? context.SameParent ? "request.ParentId" : "null" : dependency.Type == "string?" ? "entity." + dependency.Property : "Convert.ToString(entity." + dependency.Property + ", CultureInfo.InvariantCulture)";
            result.AppendLine($"        string? TargetParent{name}({table.Name} entity) => {parent};")
                .AppendLine($"        var names{name} = new Dictionary<(string? Parent, long Id), string?>();");
            if (unsupported)
            {
                // 合理但尚未形成唯一父映射的页面仍可生成，导出名称时明确拒绝，绝不读取宽范围目标。
                result.AppendLine($"        if (entities.Any(entity => entity.{name} is not null)) throw new InvalidOperationException(\"关联导出目标父范围无法唯一确定，请先配置可读的声明外键。\");");
                continue;
            }
            var sourceId = IdExpression(relation.SourceColumn, "entity");
            // 导出已验证来源export权限；固定绑定只需重新验证目标读取，不额外要求来源list权限。
            var scopeAuthorization = relation.ParentBinding?.Binding is not null ? ", false, false" : "";
            result.AppendLine("        // 按真实目标父分组隔离名称缓存；相同ID不能借用另一父范围的名称。")
                .AppendLine($"        foreach (var group{name} in entities.Where(entity => entity.{name} is not null).GroupBy(TargetParent{name}))")
                .AppendLine("        {")
                .AppendLine($"            var ids{name} = group{name}.Select(entity => {sourceId}).Distinct().ToArray();")
                .AppendLine($"            var query{name} = await OptionsScope{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(request.ParentId, group{name}.Key, actor, cancellationToken{scopeAuthorization});")
                .AppendLine($"            var rows{name} = await query{name}.Where(entity => ids{name}.Contains(entity.Id)).ToListAsync(entity => new {{ entity.Id, Name = entity.{relation.NameColumn.Property} }}, cancellationToken);")
                .AppendLine($"            foreach (var row in rows{name}) names{name}[(group{name}.Key, row.Id)] = row.Name;")
                .AppendLine("        }");
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
            var id = IdExpression(column, "entity");
            return $"{access} is null ? null : names{column.Property}.GetValueOrDefault((TargetParent{column.Property}(entity), {id})) ?? Convert.ToString({access}, CultureInfo.InvariantCulture)";
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
    /// <summary>关联ID以数据库原整数读取，不经过JavaScript浮点数或任意SQL转换。</summary>
    private static string IdExpression(ColumnModel column, string row) => column.Type switch
    {
        "string?" => $"long.Parse({row}.{column.Property}!, CultureInfo.InvariantCulture)",
        "long" => row + "." + column.Property,
        _ => row + "." + column.Property + "!.Value"
    };
    /// <summary>正确转义生成的常量。</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
}
