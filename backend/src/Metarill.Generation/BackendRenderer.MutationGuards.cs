using System.Text;

namespace Metarill.Generation;

/// <summary>守卫只共享固定范围，不把页面可写白名单扩展成任意实体传输。</summary>
internal static partial class BackendRenderer
{
    private static readonly HashSet<string> PreparationScopeNames = new(StringComparer.Ordinal) { "yz_project_id", "yz_admin_type_id", "yz_role_id" };
    private static readonly HashSet<string> SecurityValueNames = new(StringComparer.Ordinal) { "yz_project_id", "yz_admin_type_id", "yz_role_id", "yz_role_ids", "is_super", "is_scope", "is_built_in" };

    /// <summary>只有已发布归属键进入准备阶段；省略字段保持不存在，显式 null 保留。</summary>
    internal static string PrepareCall(GenerationModel model, TableModel table, PageModel page, string operation, string targetId, string? request, string parentId = "parentId",string? businessButton=null)
    {
        var columns = SafeGuardScopes(model, page).Select(scope => scope.Column).Concat(table.Columns.Where(column => PreparationScopeNames.Contains(column.Source.ColumnName) && IsPhysicalId(column))).Where(column=>!NonRedundantCrossParentCompilation.IsSlot(model,table,column)).Distinct().ToArray();
        var result = new StringBuilder().AppendLine("        var mutationScopeValues = new Dictionary<string, object?>(StringComparer.Ordinal);");
        if (request is not null)
            foreach (var column in columns.Where(page.Writable.Contains))
                result.AppendLine($"        if ({request}.{column.Property}.IsSpecified) mutationScopeValues[{Literal(column.Source.ColumnName)}] = {request}.{column.Property}.Value;");
        foreach (var (column, source) in SafeGuardScopes(model, page))
        {
            if (source == "parent") result.AppendLine($"        if ({parentId} is not null) mutationScopeValues[{Literal(column.Source.ColumnName)}] = {ScopeValue(column, parentId)};");
            else if (source == "project") result.AppendLine($"        mutationScopeValues[{Literal(column.Source.ColumnName)}] = {ScopeValue(column, "actor.ProjectId")};");
        }
        return result.AppendLine($"        await PrepareMutationAsync({Literal(page.Source.Id)}, RecordMutationOperation.{operation}, actor, cancellationToken, {targetId}, mutationScopeValues{(businessButton is null?"":", "+businessButton)});").ToString();
    }

    /// <summary>原范围SQL仍完整保留；新提示只传稳定FK的物理ID，不能把名称或凭据当实体快照带出。</summary>
    private static IEnumerable<(ColumnModel Column, string Source)> SafeGuardScopes(GenerationModel model, PageModel page)
    {
        foreach (var scope in page.Scopes)
        {
            var column = scope.Column;
            if (GenerationMetadataPublisher.IsSensitiveKey(column.Source.ColumnName) || !IsPhysicalId(column)) continue;
            if (scope.Source == "project" && column.Source.ColumnName == "yz_project_id") { yield return scope; continue; }
            var table=model.Tables.Single(candidate=>candidate.Source.Id==page.Source.ConfigTableId);
            if (NonRedundantCrossParentCompilation.Managed(model,table,page,column)) continue;
            if (scope.Source != "parent" || !column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal)) continue;
            var parent = model.Tables.SingleOrDefault(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)
                ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName);
            if (parent?.Columns.SingleOrDefault(candidate => candidate.IsId)?.Type == "long") yield return scope;
        }
    }

    /// <summary>只把规范64位物理ID作为额外归属；普通字符串范围继续原查询，但不进入提示。</summary>
    private static bool IsPhysicalId(ColumnModel column) => column.Source.LogicalType is "id" or "int64" && column.Type is "long" or "long?";

    /// <summary>所有最终规则完成后才调用，任何拒绝先于本次对象持久化和观察者。</summary>
    private static string WriteCall(PageModel page, string operation, string original, string submitted = "submitted",string? businessButton=null) =>
        $"        await ValidateMutationAsync({Literal(page.Source.Id)}, RecordMutationOperation.{operation}, entity, actor, {original}, {submitted}, cancellationToken{(businessButton is null?"":", "+businessButton)});\n";

    /// <summary>准备在首个记录锁前；最终校验仍使用同一事务，默认实现不改变旧宿主规则。</summary>
    private static string Preparation(GenerationModel model, TableModel table)
    {
        var business=table.Pages.Any(page=>BusinessRecordButtons(model,page).Count>0);
        var columns = table.Columns.Where(column => SecurityValueNames.Contains(column.Source.ColumnName)).Concat(table.Pages.SelectMany(page => SafeGuardScopes(model, page).Select(scope => scope.Column))).Where(column=>!NonRedundantCrossParentCompilation.IsSlot(model,table,column)).Distinct().ToArray();
        var values = string.Join("\n", columns.Select(column => $"            [{Literal(column.Source.ColumnName)}] = entity.{column.Property},"));
        return $$"""
    /// <summary>窄字段快照不包含密码；原值在合并请求和手写钩子前读取。</summary>
    private IReadOnlyDictionary<string, object?> MutationScopeValues({{table.EntityType}} entity) => guards.Any()
        ? new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
{{values}}
        }) : EmptyMutationScope;
    private static readonly IReadOnlyDictionary<string, object?> EmptyMutationScope = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    /// <summary>身份先验证，归属值仅为提示，宿主必须在当前读中验证实际项目和后台。</summary>
    private async Task PrepareMutationAsync(string pageId, RecordMutationOperation operation, RuntimeActor actor, CancellationToken cancellationToken, long? targetId = null, IReadOnlyDictionary<string, object?>? scopeValues = null{{(business?", string? businessButtonId = null":"")}})
    {
        ValidateActor(actor);
        var transaction = session.CurrentTransaction ?? throw new InvalidOperationException("写入准备要求当前事务。");
        var preparation = new RecordMutationPreparation(pageId, "{{table.Source.TableName}}", operation, actor, transaction)
        {
            TargetId = targetId,
            ScopeValues = scopeValues is null ? EmptyMutationScope : new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(scopeValues, StringComparer.Ordinal)){{(business?",\n            BusinessButtonId = businessButtonId":"")}}
        };
        foreach (var guard in guards) await guard.BeforeMutationAsync(preparation, cancellationToken);
    }
    /// <summary>准备后的归属变化由宿主保守拒绝；不能在此补取逆序聚合锁。</summary>
    private async Task ValidateMutationAsync(string pageId, RecordMutationOperation operation, {{table.EntityType}} entity, RuntimeActor actor, IReadOnlyDictionary<string, object?>? original, ISet<string> submitted, CancellationToken cancellationToken{{(business?", string? businessButtonId = null":"")}})
    {
{{(TrustedSecurityServiceCompilation.Applies(model,table) ? "        SecurityRealmMutationGuard.RequirePrepared(guards,session.CurrentTransaction,\""+table.Source.TableName+"\",entity.Id,actor,operation is RecordMutationOperation.Create or RecordMutationOperation.Copy,operation==RecordMutationOperation.Delete);" : "        if (!guards.Any()) return;")}}
        var transaction = session.CurrentTransaction ?? throw new InvalidOperationException("写入复核要求当前事务。");
        var validation = new RecordMutationValidation(pageId, "{{table.Source.TableName}}", operation, entity.Id, actor,
            original ?? EmptyMutationScope, MutationScopeValues(entity), new HashSet<string>(submitted, StringComparer.Ordinal), transaction){{(business?" { BusinessButtonId = businessButtonId }":"")}};
        foreach (var guard in guards) await guard.BeforeWriteAsync(validation, cancellationToken);
    }

""";
    }
}
