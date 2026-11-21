using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>自树和跨表候选共用同一套联接排序证明，排序不会改变原名称或父级范围。</summary>
internal static class CascadeTargetOrderCompilation
{
    /// <summary>额外联接只允许使用非冗余目标页已开放的固定单值列。</summary>
    internal static bool CanUseJoin(GenerationModel model, TableModel target, PageModel page, ColumnModel column) =>
        target.Source.IsRongyu == 0 && !NonRedundantReadCompilation.Physical(model, target, column) &&
        column.Source.IsVirtualColumn == 0 && GenerationModel.Enabled(column.Source.Extra, "isJoinColumn") &&
        !column.IsJsonArray && !ExactDataManageValues.IsSensitiveField(column.Source.ColumnName) &&
        !ManagerCredentialCompilation.IsCredential(target, column) &&
        (column.Source.LogicalType == "string" || MultiValueLabel.IsTyped(column.Source.LogicalType)) &&
        page.Readable.Any(read => read.Source.Id == column.Source.Id);

    /// <summary>生成前验证名称与排序字段的类型。共用条件联接时，原名称及排序使用各自的查询。</summary>
    internal static (IReadOnlyList<string> Dependencies, IReadOnlyList<string> SortDependencies,
        IReadOnlyList<NonRedundantSingleTargetScope> Targets) Require(GenerationModel model, TableModel target,
        PageModel page, ColumnModel name, ColumnModel? parent, bool derivedName, bool derivedOrder,
        IReadOnlyList<(ColumnModel Column, bool Descending)> order, IEnumerable<ColumnModel> identity)
    {
        var orderFields = JoinedFields(model, target, name, order, derivedOrder);
        var targets = Targets(model, target, page, orderFields);
        var labelTargets = derivedName && targets.Count > 0
            ? NonRedundantReadCompilation.ProjectPathTargets(model, target, page,
                [name.Source.ColumnName], multiValuePath: true)
            : [];
        var separateOrder = targets.Any(candidate => labelTargets.Any(label => label.ColumnId == candidate.ColumnId));
        var identityFields = identity.Select(column => column.Source.ColumnName).Append("id");
        var labelOutputs = new[] { "id", name.Source.ColumnName }
            .Concat(parent is null ? [] : new[] { parent.Source.ColumnName }).ToArray();
        var labelDependencies = identityFields.Append(name.Source.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var orderDependencies = (separateOrder ? identityFields : labelDependencies)
            .Concat(order.Select(item => item.Column.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var orderOutputs = (separateOrder ? new[] { "id" } : labelOutputs)
            .Concat(orderFields.Select(column => column.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ordered = Compile(orderOutputs, orderDependencies, targets);
        var labels = separateOrder ? Compile(labelOutputs, labelDependencies, []) : ordered;
        if (labels.OutputTypes["id"] != "int64" || labels.OutputTypes[name.Source.ColumnName] != name.Source.LogicalType)
            throw new InvalidOperationException("级联名称未落到同类型的固定单值列。");
        if (orderFields.Any(column => ordered.OutputTypes[column.Source.ColumnName] != column.Source.LogicalType))
            throw new InvalidOperationException("级联排序列未落到同类型的固定单值列。");

        return separateOrder ? (labelDependencies, orderDependencies, targets) : (orderDependencies, [], targets);

        NonRedundantReadPlan Compile(string[] outputs, string[] dependencies, IReadOnlyList<NonRedundantSingleTargetScope> scopes)
        {
            if (FixedPageConditionCompilation.HasLogicalFields(page))
                return NonRedundantReads.CompileCrossPageCandidate(model.Catalog, target.Source.Id, page.Source.Id,
                    outputs, dependencies, null, scopes);
            if (NonRedundantReadCompilation.HasGlobalRowProject(target))
                return NonRedundantReads.CompileGlobalCandidate(model.Catalog, target.Source.Id, page.Source.Id,
                    outputs, dependencies, scopes);
            return scopes.Count > 0
                ? NonRedundantReads.CompileProjectRelation(model.Catalog, target.Source.Id, outputs, dependencies, scopes)
                : NonRedundantReads.Compile(model.Catalog, target.Source.Id, outputs, dependencies);
        }
    }

    /// <summary>下发排序所需的实际字典页和中间页，保留这些页的固定范围。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> OrderTargets(GenerationModel model, TableModel target,
        PageModel page, ColumnModel name, IReadOnlyList<(ColumnModel Column, bool Descending)> order, bool derivedOrder) =>
        Targets(model, target, page, JoinedFields(model, target, name, order, derivedOrder));

    private static IReadOnlyList<NonRedundantSingleTargetScope> Targets(GenerationModel model, TableModel target,
        PageModel page, IReadOnlyList<ColumnModel> columns) =>
        NonRedundantReadCompilation.ProjectPathTargets(model, target, page,
            columns.Select(column => column.Source.ColumnName), multiValuePath: true);

    private static ColumnModel[] JoinedFields(GenerationModel model, TableModel target, ColumnModel name,
        IReadOnlyList<(ColumnModel Column, bool Descending)> order, bool derivedOrder) =>
        !derivedOrder ? [] : order.Select(item => item.Column)
            .Where(column => column.Source.Id != name.Source.Id && !NonRedundantReadCompilation.Physical(model, target, column)).ToArray();
}
