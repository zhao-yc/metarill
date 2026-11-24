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

    /// <summary>已证明的独立父键单独校验，账号范围仍从实际来源行绑定。项目内固定单值副本允许纯常量或物理父级。</summary>
    internal static AccountScopedCrossCache Compile(MetadataCatalog catalog,TableMetadata source,TableMetadata target,
        PageMetadata targetPage,bool globalTreeSource,ColumnMetadata? multiParent=null,bool globalSingleSource=false,
        bool allowFixedOnly=false,bool separateExactScope=false,IReadOnlySet<string>? additionalParents=null)=>
        Compile(catalog,source,target,targetPage,recursive:false,multiParent:multiParent,globalTreeSource:globalTreeSource,
            globalSingleSource:globalSingleSource,allowFixedOnly:allowFixedOnly,separateExactScope:separateExactScope,
            additionalParents:additionalParents);

    /// <summary>全局物理多值字典的显式项目等值由固定查询作用域绑定，账号仍取实际来源行。</summary>
    internal static AccountScopedCrossCache CompileGlobalMulti(MetadataCatalog catalog,TableMetadata source,TableMetadata target,PageMetadata targetPage)=>
        Compile(catalog,source,target,targetPage,recursive:false,globalSingleSource:GlobalCrossTableTarget.Declared(source),globalMulti:true);

    /// <summary>递归候选允许逻辑字段的常量条件，账号等值仍须对应来源行真实物理归属。</summary>
    internal static AccountScopedCrossCache CompileRecursive(MetadataCatalog catalog,TableMetadata source,TableMetadata target,PageMetadata targetPage,
        ColumnMetadata? multiParent=null,bool globalTreeSource=false,IReadOnlySet<string>? selfTreeFields=null,
        bool globalSingleSource=false,bool separateExactScope=false,IReadOnlySet<string>? additionalParents=null)=>
        Compile(catalog,source,target,targetPage,recursive:true,multiParent:multiParent,globalTreeSource:globalTreeSource,
            selfTreeFields:selfTreeFields,globalSingleSource:globalSingleSource,separateExactScope:separateExactScope,
            additionalParents:additionalParents);

    private static AccountScopedCrossCache Compile(MetadataCatalog catalog, TableMetadata source, TableMetadata target,
        PageMetadata targetPage,bool recursive,ColumnMetadata? multiParent=null,bool globalTreeSource=false,
        IReadOnlySet<string>? selfTreeFields=null,bool globalSingleSource=false,bool allowFixedOnly=false,bool separateExactScope=false,
        IReadOnlySet<string>? additionalParents=null,bool globalMulti=false)
    {
        if(globalTreeSource)GlobalCachedRelationSource.RequireSelfTree(catalog,source);
        // 仅经发布证明的全局单值或物理账号多选缓存可绑定来源行；根查询另行约束行项目。
        if (source.IsRongyu != 1 || targetPage.ConfigTableId != target.Id || GlobalCrossTableTarget.Declared(source)&&!globalTreeSource&&!globalSingleSource ||
            source.IsView != 0 || source.IsFunctionTable != 0 || target.IsView != 0 || target.IsFunctionTable != 0 ||
            !separateExactScope&&targetPage.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0)
            throw Invalid();

        var targets = Physical(catalog, target);
        var sources = Physical(catalog, source);
        var types=targets.ToDictionary(pair=>pair.Key,pair=>pair.Value.LogicalType,StringComparer.Ordinal);
        if(recursive)
        {
            var storage=MetadataStoragePolicy.ForTable(catalog,target);
            if(CrossTableParentCondition.Compile(catalog,target,targetPage,storage,allowLogical:true,multiParent:multiParent,selfTreeFields:selfTreeFields,separateExactScope:separateExactScope,additionalParents:additionalParents) is not {Allowed:true,HasLogical:true})
                throw Invalid();
            var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
            foreach(var column in columns.Where(column=>CrossTableStorageColumns.IsLogicalPathField(target,columns,column.ColumnName)||
                selfTreeFields?.Contains(column.ColumnName)==true))
                types.TryAdd(column.ColumnName,column.LogicalType);
        }
        var syntax = FixedConditionPlan.Compile(targetPage.WhereSql, types.Keys);
        var constants = new List<FixedConditionTerm>();
        var account = new List<(ColumnMetadata Target, string Variable)>();
        var hasGlobalProject=false;
        foreach (var term in syntax.Terms)
        {
            if (term.Values.All(value => value.Kind != FixedConditionValueKind.Variable))
            {
                constants.Add(term);
                continue;
            }
            var variable = Variable(term);
            if((multiParent?.ColumnName==term.Field||additionalParents?.Contains(term.Field)==true)&&MultiTargetParentScope.IsParameter(variable))continue;
            if(GlobalMultipleRelationTarget.IsProjectScope(term)&&
                (!GlobalCrossTableTarget.Declared(target)||globalMulti&&target.IsRongyu is 0 or 1&&
                    targets.TryGetValue(term.Field,out var project)&&project.LogicalType is "int64" or "string"))
            {
                // 项目由候选与回填的固定作用域绑定，无须从来源页再推断门店或商户。
                hasGlobalProject=globalMulti;
                continue;
            }
            if (term.Field == "yz_project_id" || !AccountConditionVariables.IsPageField(variable, term.Field) ||
                selfTreeFields?.Contains(term.Field)==true||!targets.TryGetValue(term.Field,out var targetOwner)||targetOwner.LogicalType is not ("int32" or "int64" or "string")) throw Invalid();
            account.Add((targetOwner, variable));
        }

        var pages = catalog.ConfigPages.Where(page => page.ConfigTableId == source.Id).ToArray();
        if (account.Count==0 ? !recursive&&!allowFixedOnly&&!hasGlobalProject : pages.Length==0) throw Invalid();
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
