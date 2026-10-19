using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>固定一跳关系的真实源父解析、权限与最终值校验；不读取客户端formParameters推导归属。</summary>
internal static class BoundRelationValidationRenderer
{
    /// <summary>所有含显式声明的Service都需要宿主真实授权，不能仅依赖HTTP Controller。</summary>
    internal static bool RequiresAuthorization(TableModel table) => table.Columns.Any(FixedParentRelationCompilation.HasDeclaration);
    /// <summary>可见候选和隐藏字段最终校验共用同一固定定义，只按稳定列ID去重。</summary>
    private static RelationModel[] Relations(GenerationModel model, PageModel page) => RelationCompilation.ForPage(model, page)
        .Concat(RelationCompilation.ForPage(model, page, true)).Where(relation => relation.ParentBinding?.Binding is not null).DistinctBy(relation => relation.SourceColumn.Source.Id).ToArray();
    /// <summary>生成方法名只由已发布页面与字段ID构成，不包含请求输入。</summary>
    internal static string Suffix(PageModel page, RelationModel relation) => page.Source.Id + "_" + relation.SourceColumn.Source.Id;

    /// <summary>写入前先验目标页读取权限，派生阶段后再验最终FK；省略字段且父未变不读取历史名称。</summary>
    internal static string Call(GenerationModel model, TableModel table, PageModel page, bool creating, bool authorizationOnly)
    {
        var relations = RelationCompilation.ForPage(model, page, true); var result = new StringBuilder();
        foreach (var column in table.Columns.Where(FixedParentRelationCompilation.HasDeclaration))
        {
            var relation = relations.SingleOrDefault(candidate => candidate.SourceColumn.Source.Id == column.Source.Id);
            var binding = relation?.ParentBinding?.Binding;
            var trigger = creating ? "true" : "submitted.Contains(" + Literal(column.Source.ColumnName) + ")" + (binding is null ? "" : " || submitted.Contains(" + Literal(binding.SourceParentColumn.Source.ColumnName) + ")");
            var needsValue = !authorizationOnly || binding is null;
            result.AppendLine($"        if (({trigger})" + (needsValue ? $" && entity.{column.Property} is not null" : "") + ")");
            if (binding is null) result.AppendLine($"            throw new InvalidOperationException({Literal(relation?.ParentBinding?.Reason ?? "固定归属关系缺少已编译目标范围。")});");
            else result.AppendLine(authorizationOnly
                ? $"            AuthorizeBound{Suffix(page, relation!)}(actor, false);"
                : $"            await ValidateBound{Suffix(page, relation!)}Async(entity, actor, cancellationToken);");
        }
        return result.ToString();
    }

    /// <summary>候选为只读；保存使用同一事务的当前读，项目准备锁已经在源记录锁之前获取。</summary>
    internal static string Service(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder();
        if (table.Columns.Any(FixedParentRelationCompilation.HasDeclaration))
            result.AppendLine($"    /// <summary>未知绑定在任何读取之前拒绝，BackendOnly仍保留可编译的明确失败入口。</summary>\n    private static void RejectBound{page.Source.Id}(string reason) => throw new InvalidOperationException(reason);");
        foreach (var relation in Relations(model, page))
        {
            var binding = relation.ParentBinding!.Binding!; var suffix = Suffix(page, relation);
            var sourceTerms = new List<string> { "parent.Id == sourceKey", $"parent.{binding.OwnerColumn.Property} == row.{binding.TargetParentColumn.Property}", $"parent.{Project(binding.SourceParent).Property} == {ProjectValue(binding.SourceParent)}" };
            if (FixedPageQueryCompilation.SoftDeleteColumn(binding.SourceParent) is {} sourceGuardDeleted) sourceTerms.Add($"parent.{sourceGuardDeleted.Property} == null");
            var ownerTerms = new List<string> { $"owner.Id == row.{binding.TargetParentColumn.Property}", $"owner.{Project(binding.TargetParent).Property} == {ProjectValue(binding.TargetParent)}" };
            if (FixedPageQueryCompilation.SoftDeleteColumn(binding.TargetParent) is {} ownerGuardDeleted) ownerTerms.Add($"owner.{ownerGuardDeleted.Property} == null");
            if (binding.SourceParent.Source.Id == "2076977530983813120")
                foreach (var flag in binding.TargetParent.Columns.Where(column => column.Source.ColumnName is "is_view" or "is_function_table"))
                    ownerTerms.Add($"(owner.{flag.Property} == null || owner.{flag.Property} != {(flag.Type == "bool?" ? "true" : "1")})");
            result.AppendLine($"    /// <summary>目标SQL再次关联真实源父与归属表，避免多语句读取间换表而返回旧范围候选。</summary>\n    private ISelect<{relation.Target.Name}> ApplyBoundScope{suffix}(ISelect<{relation.Target.Name}> query, long sourceKey, RuntimeActor actor)")
                .AppendLine("    {")
                .AppendLine($"        query = query.Where(row => session.Orm.Select<{binding.SourceParent.Name}>().Where(parent => {string.Join(" && ", sourceTerms)}).Any());")
                .AppendLine($"        return query.Where(row => session.Orm.Select<{binding.TargetParent.Name}>().Where(owner => {string.Join(" && ", ownerTerms)}).Any());")
                .AppendLine("    }")
                .AppendLine($"    /// <summary>来源读取与目标读取分别授权；写入已有来源操作门禁，不能把请求Actor当真实授权。</summary>\n    private void AuthorizeBound{suffix}(RuntimeActor actor, bool requireSourceList)")
                .AppendLine("    {")
                .AppendLine("        if (relationAuthorization is null || relationAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException(\"固定关系要求真实当前身份。\");")
                .AppendLine($"        if (requireSourceList) relationAuthorization.RequirePagePermission({Literal(page.Source.Id)}, \"list\");")
                .AppendLine($"        relationAuthorization.RequirePagePermission({Literal(relation.TargetPage.Source.Id)}, \"list\");")
                .AppendLine("    }")
                .AppendLine($"    /// <summary>固定源父只投影已声明owner字段；显式targetParentId仅作一致性检查，不能选择目标范围。</summary>\n    private async Task<long> ResolveBound{suffix}Async(string? parentId, string? targetParentId, RuntimeActor actor, bool locking, bool requireSourceList, CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine($"        AuthorizeBound{suffix}(actor, requireSourceList);")
                .AppendLine("        if (!long.TryParse(parentId, NumberStyles.None, CultureInfo.InvariantCulture, out var sourceKey) || sourceKey <= 0) throw new ArgumentException(\"固定关系要求有效来源parentId。\");")
                .AppendLine("        if (locking && session.CurrentTransaction is null) throw new InvalidOperationException(\"固定关系写入校验必须加入当前事务。\");")
                .AppendLine($"        var sourceQuery = session.Select<{binding.SourceParent.Name}>().Where(row => row.Id == sourceKey && row.{Project(binding.SourceParent).Property} == {ProjectValue(binding.SourceParent)});");
            if (FixedPageQueryCompilation.SoftDeleteColumn(binding.SourceParent) is {} sourceDeleted) result.AppendLine($"        sourceQuery = sourceQuery.Where(row => row.{sourceDeleted.Property} == null);");
            result.AppendLine("        if (locking) sourceQuery = sourceQuery.ForUpdate();")
                .AppendLine($"        var sources = await sourceQuery.Limit(1).ToListAsync(row => new {{ row.Id, OwnerId = row.{binding.OwnerColumn.Property} }}, cancellationToken);")
                .AppendLine("        var owner = sources.SingleOrDefault()?.OwnerId;")
                .AppendLine("        if (owner is not {} derived || derived <= 0) throw new InvalidOperationException(\"来源父不存在、已删除、跨项目或未声明有效归属。\");")
                .AppendLine($"        var ownerQuery = session.Select<{binding.TargetParent.Name}>().Where(row => row.Id == derived && row.{Project(binding.TargetParent).Property} == {ProjectValue(binding.TargetParent)});");
            if (FixedPageQueryCompilation.SoftDeleteColumn(binding.TargetParent) is {} ownerDeleted) result.AppendLine($"        ownerQuery = ownerQuery.Where(row => row.{ownerDeleted.Property} == null);");
            if (binding.SourceParent.Source.Id == "2076977530983813120")
                foreach (var flag in binding.TargetParent.Columns.Where(column => column.Source.ColumnName is "is_view" or "is_function_table"))
                    result.AppendLine($"        ownerQuery = ownerQuery.Where(row => row.{flag.Property} == null || row.{flag.Property} != {(flag.Type == "bool?" ? "true" : "1")});");
            result.AppendLine("        if (locking) ownerQuery = ownerQuery.ForUpdate();")
                .AppendLine("        if ((await ownerQuery.Limit(1).ToListAsync(row => row.Id, cancellationToken)).Count != 1) throw new InvalidOperationException(\"目标父不存在、已删除或不属于当前项目。\");")
                .AppendLine("        if (targetParentId is not null && (!long.TryParse(targetParentId, NumberStyles.None, CultureInfo.InvariantCulture, out var hinted) || hinted != derived)) throw new ArgumentException(\"targetParentId与服务器固定归属不一致。\");")
                .AppendLine("        return derived;")
                .AppendLine("    }")
                .AppendLine($"    /// <summary>最终实体的来源父和关联值必须处于同一固定归属；目标越界时整笔写入回滚。</summary>\n    private async Task ValidateBound{suffix}Async({table.Name} entity, RuntimeActor actor, CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine($"        var parentId = Convert.ToString(entity.{binding.SourceParentColumn.Property}, CultureInfo.InvariantCulture);")
                .AppendLine($"        var query = await OptionsScope{suffix}Async(parentId, null, actor, cancellationToken, true, false);")
                .AppendLine($"        var selectedId = {IdValue(relation.SourceColumn)};")
                .AppendLine("        var selected = await query.Where(row => row.Id == selectedId).ForUpdate().Limit(1).ToListAsync(row => row.Id, cancellationToken);")
                .AppendLine("        if (selected.Count != 1) throw new InvalidOperationException(\"关联字段不在来源父对应的目标范围内。\");")
                .AppendLine("    }");
        }
        return result.ToString();
    }
    /// <summary>父表项目键必须在编译阶段明确存在。</summary>
    private static ColumnModel Project(TableModel table) => table.Columns.Single(column => column.Source.ColumnName == "yz_project_id");
    /// <summary>按持久化类型绑定真实Actor项目值，保持参数化表达式。</summary>
    private static string ProjectValue(TableModel table) => Project(table).Type == "string?" ? "actor.ProjectId" : "long.Parse(actor.ProjectId, CultureInfo.InvariantCulture)";
    /// <summary>最终关联ID仅按整数或整数文本读取，不经JavaScript浮点数。</summary>
    private static string IdValue(ColumnModel column) => column.Type == "string?" ? $"long.Parse(entity.{column.Property}, NumberStyles.None, CultureInfo.InvariantCulture)" : $"entity.{column.Property}!.Value";
    /// <summary>元数据诊断消息只写作安全C#字面值。</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
}
