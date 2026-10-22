using System.Globalization;
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
            .AppendLine($"    private async Task ValidateRelations{page.Source.Id}Async({table.Name} entity, RuntimeActor actor, bool creating, ISet<string> submitted, CancellationToken cancellationToken{(CascadeTreeExcelRenderer.OperationAware(model,table,page)?", RecordMutationOperation? cascadeOperation = null":"")})")
            .AppendLine("    {").AppendLine("        await Task.CompletedTask;");
        var crossFields=CrossTableWriteCompilation.ForService(model,table,page).SelectMany(plan=>plan.Levels).SelectMany(level=>new[]{level.Id.Source.Id,level.Name.Source.Id}).ToHashSet(StringComparer.Ordinal);
        var derived = MaterializedDerivedWriteCompilation.ForTable(model, table).Where(binding=>!crossFields.Contains(binding.Storage.Source.Id)).ToArray();
        var cascadeNames=CascadeTreeWriteCompilation.ForService(model,table,page).Select(plan=>plan.Name.Source.Id).Concat(crossFields).ToHashSet(StringComparer.Ordinal);
        // 未证明的派生值不能借默认值、复制或手写钩子的submitted集合绕过严格DTO。
        foreach (var storage in table.Columns.Where(column => GenerationModel.Enabled(column.Source.Extra, "isJoinColumn") && !derived.Any(binding => binding.Storage.Source.Id == column.Source.Id)&&!cascadeNames.Contains(column.Source.Id)))
            result.AppendLine($"        if (creating ? entity.{storage.Property} is not null : submitted.Contains({Literal(storage.Source.ColumnName)})) throw new InvalidOperationException({Literal("物理派生写入尚未证明：" + storage.Source.ColumnName)});");
        foreach (var binding in derived)
            result.AppendLine($"        if (!creating && submitted.Contains({Literal(binding.Storage.Source.ColumnName)}) && !submitted.Contains({Literal(binding.ForeignKey.Source.ColumnName)})) throw new InvalidOperationException(\"派生字段只能随已声明关联重算。\");")
                .AppendLine($"        if ((creating || submitted.Contains({Literal(binding.ForeignKey.Source.ColumnName)})) && entity.{binding.ForeignKey.Property} is null) {{ entity.{binding.Storage.Property} = null; submitted.Add({Literal(binding.Storage.Source.ColumnName)}); }}");
        var constantRelations = RelationCompilation.ForPage(model, page).Where(relation => relation.Conditions is not null || relation.TargetPage.Condition is not null).ToDictionary(relation => relation.SourceColumn.Source.Id);
        var number = 0;
        foreach (var column in table.Columns.Where(column => FixedOptionCatalogCompilation.Resolve(model, page, column).Declared || !column.IsSystem && (column.Source.ControlKind=="CascadeTree" || column.Type is "long?" or "string?")))
        {
            var fixedCatalog = FixedOptionCatalogCompilation.Resolve(model, page, column);
            if (fixedCatalog.Declared)
            {
                result.AppendLine($"        if (creating || submitted.Contains({Literal(column.Source.ColumnName)}))");
                result.AppendLine(fixedCatalog.Definition is not null
                    ? $"            Metarill.Core.Metadata.TemplateVersionCatalog.RequireSupported(entity.{column.Property});"
                    : $"            throw new InvalidOperationException({Literal(fixedCatalog.Reason!)});");
                continue;
            }
            if(column.Source.ControlKind=="CascadeTree"){result.Append(CascadeTreeWriteRenderer.Validation(model,table,page,column));continue;}
            if(crossFields.Contains(column.Source.Id))continue;
            if (column.Source.ControlKind == "MultiSelectPage")
            {
                var multi = MultiSelectCompilation.Resolve(model, table, page, column, forService: true);
                if (multi.Definition is { } definition)
                {
                    var dependency = definition.TargetParentField is null ? null : table.Columns.Single(field => field.JsonName == definition.TargetParentField).Source.ColumnName;
                    var trigger = dependency is null ? "" : " || submitted.Contains(" + Literal(dependency) + ")";
                    result.AppendLine($"        if (creating || submitted.Contains({Literal(column.Source.ColumnName)}){trigger}) await ValidateMulti{page.Source.Id}_{column.Source.Id}Async(entity, actor, submitted, creating || submitted.Contains({Literal(column.Source.ColumnName)}), cancellationToken);");
                }
                else result.AppendLine($"        if ((creating || submitted.Contains({Literal(column.Source.ColumnName)})) && entity.{column.Property} is not null) throw new InvalidOperationException({Literal(multi.Reason!)});");
                continue;
            }
            if (column.Source.ControlKind == "MultiCheckBoxDatabase" && FixedRoleCheckboxCompilation.Resolve(model,table,page,column).Definition is not null)
            {
                result.AppendLine($"        if ((creating || submitted.Contains({Literal(column.Source.ColumnName)})) && entity.{column.Property} is not null)")
                    .AppendLine("        {")
                    .AppendLine($"            var roleIds = RoleCheckboxValue.Parse(entity.{column.Property}).Distinct().ToArray();")
                    .AppendLine($"            if (await RoleOptionsScope{page.Source.Id}(actor).Where(role => roleIds.Contains(role.Id)).CountAsync(cancellationToken) != roleIds.Length) throw new InvalidOperationException(\"角色不存在、已删除或不在当前工作区真实后台范围。\");")
                    .AppendLine("        }");
                continue;
            }
            if(table.Source.IsRongyu==0&&column.Source.ControlKind=="SelectPage")
            {
                result.Append(NonRedundantSelectionRenderer.Validate(model,table,page,column));continue;
            }
            var target = Target(model, column);
            var constantRelation = constantRelations.GetValueOrDefault(column.Source.Id);
            var knownRelation = column.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase" || !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId);
            if (target is null && !knownRelation) continue;
            number++;
            var constantDependency = constantRelation is null ? null : ControlRenderer.RelationContext(page,constantRelation,false).TargetParentField;
            var constantDependencyColumn = constantDependency is null ? null : table.Columns.Single(field=>field.JsonName==constantDependency);
            var constantTrigger = "creating || submitted.Contains("+Literal(column.Source.ColumnName)+")"+(constantDependencyColumn is null?"":" || submitted.Contains("+Literal(constantDependencyColumn.Source.ColumnName)+")");
            foreach(var dependent in (constantRelation?.Conditions?.Forms.Select(binding=>binding.Column)??[]).DistinctBy(field=>field.Source.Id))
                if(dependent.Source.Id!=column.Source.Id&&dependent.Source.Id!=constantDependencyColumn?.Source.Id)constantTrigger+=" || submitted.Contains("+Literal(dependent.Source.ColumnName)+")";
            result.AppendLine($"        if (({constantTrigger}) && entity.{column.Property} is not null)").AppendLine("        {");
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
                if (constantRelation is not null) result.Append(RelationConditionsCompilation.WriteQuery(model,table,page,constantRelation,number.ToString(CultureInfo.InvariantCulture)));
                else result.AppendLine($"            var relationQuery{number} = session.Select<{target.Name}>().Where(row => relationIds{number}.Contains(row.Id));");
                if (target.Columns.Any(candidate => candidate.Source.ColumnName == "delete_no")) result.AppendLine($"            relationQuery{number} = relationQuery{number}.Where(row => row.DeleteNo == null);");
                var project = target.Columns.SingleOrDefault(candidate => candidate.Source.ColumnName == "yz_project_id");
                if (project is not null && !target.IsGlobal)
                    result.AppendLine($"            relationQuery{number} = relationQuery{number}.Where(row => row.{project.Property} == {ProjectExpression(project)});");
                else if (project is null && !target.IsGlobal)
                    result.AppendLine($"            if (relationIds{number}.Length != 0) throw new InvalidOperationException(\"关联目标未声明项目归属，需要明确访问规则。\");");
                if (constantRelation is null)
                    result.AppendLine($"            if (await relationQuery{number}.CountAsync(cancellationToken) != relationIds{number}.Length) throw new InvalidOperationException({Literal("关联记录不存在、已删除或不属于当前项目：" + column.Source.Name)});");
                var assignments = derived.Where(binding => binding.ForeignKey.Source.Id == column.Source.Id).ToArray();
                if (constantRelation is not null && assignments.Length == 0)
                    result.AppendLine($"            var lockedRelation{number} = await relationQuery{number}.ForUpdate().ToListAsync(row => new {{ row.Id }}, cancellationToken);")
                        .AppendLine($"            if (lockedRelation{number}.Count != relationIds{number}.Length) throw new InvalidOperationException(\"关联记录已离开当前固定候选范围。\");");
                if (assignments.Length > 0)
                {
                    // 保留同项目活动COUNT预检，再加一次窄当前读；两个页面字段缓存共用此查询。
                    var fields = new[] { "Id" }.Concat(assignments.Select(binding => binding.Value.Property)).Distinct(StringComparer.Ordinal);
                    result.AppendLine($"            var derivedRows{number} = await relationQuery{number}.ForUpdate().ToListAsync(row => new {{ {string.Join(", ", fields.Select(field => "row." + field))} }}, cancellationToken);")
                        .AppendLine($"            if (derivedRows{number}.Count != 1 || derivedRows{number}[0].Id != relationIds{number}[0]) throw new InvalidOperationException(\"派生关联已变化、已删除或不属于当前项目。\");");
                    foreach (var binding in assignments)
                        result.AppendLine($"            entity.{binding.Storage.Property} = derivedRows{number}[0].{binding.Value.Property}; submitted.Add({Literal(binding.Storage.Source.ColumnName)});");
                }
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
