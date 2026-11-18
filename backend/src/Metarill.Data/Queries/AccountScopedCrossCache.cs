using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

/// <summary>冗余名称共享物理存储。回填时从当前来源行读取归属，避免使用发起维护的账号改写其它账号的缓存。</summary>
public sealed class AccountScopedCrossCache
{
    internal string PageId { get; }
    internal FixedConditionSqlPlan? Constants { get; }
    internal IReadOnlyList<(ColumnMetadata Target, ColumnMetadata Source)> Ownership { get; }

    private AccountScopedCrossCache(string pageId, FixedConditionSqlPlan? constants,
        IReadOnlyList<(ColumnMetadata Target, ColumnMetadata Source)> ownership)
    {
        PageId = pageId;
        Constants = constants;
        Ownership = ownership;
    }

    /// <summary>从全部已发布的来源页证明同一物理归属列，并将目标常量与账号等值分开编译。</summary>
    public static AccountScopedCrossCache Compile(MetadataCatalog catalog, TableMetadata source, TableMetadata target, PageMetadata targetPage)
    {
        return Compile(catalog,source,target,targetPage,recursive:false);
    }

    /// <summary>递归候选允许逻辑字段的常量条件，账号等值仍须对应来源行真实物理归属。</summary>
    internal static AccountScopedCrossCache CompileRecursive(MetadataCatalog catalog,TableMetadata source,TableMetadata target,PageMetadata targetPage)
        =>Compile(catalog,source,target,targetPage,recursive:true);

    private static AccountScopedCrossCache Compile(MetadataCatalog catalog,TableMetadata source,TableMetadata target,PageMetadata targetPage,bool recursive)
    {
        if (source.IsRongyu != 1 || targetPage.ConfigTableId != target.Id || GlobalCrossTableTarget.Declared(source) ||
            source.IsView != 0 || source.IsFunctionTable != 0 || target.IsView != 0 || target.IsFunctionTable != 0 ||
            targetPage.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length > 0)
            throw Invalid();

        var targets = Physical(catalog, target);
        var sources = Physical(catalog, source);
        var types=targets.ToDictionary(pair=>pair.Key,pair=>pair.Value.LogicalType,StringComparer.Ordinal);
        if(recursive)
        {
            var storage=MetadataStoragePolicy.ForTable(catalog,target);
            if(CrossTableParentCondition.Compile(catalog,target,targetPage,storage,allowLogical:true) is not {Allowed:true,HasLogical:true})
                throw Invalid();
            var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
            foreach(var column in columns.Where(column=>CrossTableStorageColumns.IsLogicalPathField(target,columns,column.ColumnName)))
                types.TryAdd(column.ColumnName,column.LogicalType);
        }
        var syntax = FixedConditionPlan.Compile(targetPage.WhereSql, types.Keys);
        var constants = new List<FixedConditionTerm>();
        var account = new List<(ColumnMetadata Target, string Variable)>();
        foreach (var term in syntax.Terms)
        {
            if (term.Values.All(value => value.Kind != FixedConditionValueKind.Variable))
            {
                constants.Add(term);
                continue;
            }
            var variable = Variable(term);
            if (term.Field == "yz_project_id" && variable.Equals("current_yz_project_id", StringComparison.OrdinalIgnoreCase) &&
                !GlobalCrossTableTarget.Declared(target)) continue;
            if (term.Field == "yz_project_id" || !AccountConditionVariables.IsPageField(variable, term.Field) ||
                !targets.TryGetValue(term.Field,out var targetOwner)||targetOwner.LogicalType is not ("int32" or "int64" or "string")) throw Invalid();
            account.Add((targetOwner, variable));
        }

        var pages = catalog.ConfigPages.Where(page => page.ConfigTableId == source.Id).ToArray();
        if ((!recursive&&account.Count==0)||account.Count>0&&pages.Length==0) throw Invalid();
        var ownership = new List<(ColumnMetadata, ColumnMetadata)>();
        var sourceFields = MetadataStoragePolicy.ForTable(catalog,source).PhysicalColumns.Select(column=>column.ColumnName).ToArray();
        var sourceTerms = account.Count==0?[]:pages.Select(page => FixedConditionPlan.Compile(page.WhereSql, sourceFields).Terms).ToArray();
        foreach (var (field, variable) in account)
        {
            var fields = sourceTerms.Select(terms => terms.Where(term => term.Field != "yz_project_id" &&
                term.Comparison == FixedConditionComparison.Equal && term.Values.Count == 1 &&
                term.Values[0].Kind == FixedConditionValueKind.Variable &&
                term.Values[0].Text.Equals(variable, StringComparison.OrdinalIgnoreCase)).Select(term => term.Field)
                .Distinct(StringComparer.Ordinal).ToArray()).ToArray();
            if (fields.Any(set => set.Length != 1) || fields.Select(set => set[0]).Distinct(StringComparer.Ordinal).Count() != 1)
                throw Invalid();
            if (!sources.TryGetValue(fields[0][0],out var owner) || !AccountConditionVariables.IsPageField(variable, owner.ColumnName) ||
                owner.LogicalType is not ("int32" or "int64" or "string")) throw Invalid();
            ownership.Add((field, owner));
        }

        var plan = constants.Count == 0 ? null : FixedConditionSqlPlan.Compile(
            FixedConditionPlan.Compile(string.Join(" AND ", constants.Select(term => term.ToDefinition())), types.Keys),types);
        return new(targetPage.Id, plan, ownership.AsReadOnly());
    }

    /// <summary>归属与常量都只能引用实际物理标量，防止使用名称缓存、凭据或级联辅助位作账号权威。</summary>
    private static Dictionary<string, ColumnMetadata> Physical(MetadataCatalog catalog, TableMetadata table)
    {
        var storage = MetadataStoragePolicy.ForTable(catalog, table);
        return storage.PhysicalColumns.Where(column => column.IsVirtualColumn == 0 &&
            column.ControlKind is not ("FileLibrary" or "CascadeTree" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList") &&
            column.LogicalType is not ("json" or "binary") && !ExactDataManageValues.IsSensitiveField(column.ColumnName) &&
            !(column.Extra.TryGetValue("isJoinColumn", out var join) && (join.ValueKind == System.Text.Json.JsonValueKind.True || join.ToString() == "1")))
            .ToDictionary(column => column.ColumnName, StringComparer.Ordinal);
    }

    private static string Variable(FixedConditionTerm term) =>
        term.Comparison == FixedConditionComparison.Equal && term.Values.Count == 1 &&
        term.Values[0].Kind == FixedConditionValueKind.Variable ? term.Values[0].Text : throw Invalid();

    private static InvalidOperationException Invalid() => new("账号过滤的跨表名称回填需要目标等值条件，以及所有来源页一致的物理账号归属。");
}
