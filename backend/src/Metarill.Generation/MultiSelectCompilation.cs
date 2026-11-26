using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record MultiSelectModel(ColumnModel Source, TableModel Target, PageModel TargetPage, ColumnModel Name, ColumnModel? Names,
    bool SameParent, string? TargetParentField, IReadOnlyList<TableModel> TargetParents, string ButtonText)
{
    internal bool LogicalSourceParent { get; init; }
    internal ColumnModel? SharedTextParent { get; init; }
    internal ColumnModel? LogicalParentBusinessKey { get; init; }
    internal IReadOnlyList<ColumnModel> CompoundSourceDependencies { get; init; } = [];
    internal bool PublishedPhysicalSource { get; init; }
    internal bool UsesPageRead { get; init; }
    internal bool UsesTargetPageScope { get; init; }
    internal bool UsesNameOverride { get; init; }
    internal bool GlobalParent { get; init; }
    internal RelationConditionModel? ImportCondition { get; init; }
    internal ExactDataManageModel? ExactTarget { get; init; }
    internal bool UsesGlobalBusinessExactRead { get; init; }
    internal bool IntegerLabel => !Name.IsId&&Name.Type is ("int?" or "long?");
    internal bool TypedLabel => !Name.IsId&&Metarill.Core.Contracts.MultiValueLabel.IsTyped(Name.Source.LogicalType);
}
internal sealed record MultiSelectResult(MultiSelectModel? Definition, string? Reason);

/// <summary>编译可证明的冗余与非冗余多选；未覆盖的范围和派生名称不能静默标为支持。</summary>
internal static class MultiSelectCompilation
{
    /// <summary>辅助名称只读且使用HTTP数组，实体仍保持原JSON文本存储。</summary>
    internal static GenerationModel Prepare(GenerationModel model)
    {
        var definitions = model.Tables.ToDictionary(table => table.Source.Id, table => table.Pages
            .SelectMany(page => table.Source.IsFunctionTable == 1 ? ForPage(model, table, page) : ForService(model, table, page)).ToArray());
        return model with { Tables = model.Tables.Select(table =>
        {
            var actual = definitions[table.Source.Id];
            var names = actual.Where(definition => definition.Names is not null)
                .Select(definition => definition.Names!.Source.Id).ToHashSet(StringComparer.Ordinal);
            names.UnionWith(MultiNameWriteCompilation.ForTable(model,table).Select(binding=>binding.Cache.Source.Id));
            // 可发布全局候选与带真实项目归属的业务记录分别使用各自的查询范围。
            var scopedGlobal = table.HasScopedGlobalCache || table.IsGlobal && table.Source.IsRongyu == 1 &&
                actual.Any(definition => FixedPageConditionCompilation.HasStoredGlobalMultiScope(definition.Target, definition.TargetPage) ||
                    FixedPageConditionCompilation.HasGlobalProjectMultiScope(definition.Target, definition.TargetPage) ||
                    definition.TargetPage.Condition is {} account&&GlobalMultipleRelationTarget.IsNonRedundantAccountPage(
                        definition.Target.Source,definition.TargetPage.Source,account.Syntax,checkbox:false)||
                    UsesStaticGlobalDictionary(definition)||
                    definition.Target.IsGlobal&&
                    GlobalMultipleRelationTarget.RequireCached(model.Catalog,table.Source,definition.Source.Source,definition.Target.Source)
                        .UsesPhysicalParentPage);
            ColumnModel Fix(ColumnModel column) => names.Contains(column.Source.Id) ? column with { IsManagedMultiNames = true } : column;
            IReadOnlyList<ColumnModel> FixAll(IReadOnlyList<ColumnModel> columns) => columns.Select(Fix).ToArray();
            return table with { HasScopedGlobalCache = scopedGlobal, Columns = FixAll(table.Columns), Pages = table.Pages.Select(page => page with { Readable = FixAll(page.Readable), Writable = FixAll(page.Writable.Where(column => !names.Contains(column.Source.Id)).ToArray()), Searchable = FixAll(page.Searchable), Sortable = FixAll(page.Sortable), ListColumns = FixAll(page.ListColumns), DetailColumns = FixAll(page.DetailColumns) }).ToArray() };
        }).ToArray() };
    }

    /// <summary>固定全局字典不读取动态项目或父参数，实际调用仍经过原目标页发布证明。</summary>
    internal static bool UsesStaticGlobalDictionary(MultiSelectModel definition) =>
        definition.TargetPage.Scopes.Count == 0 &&
        (definition.Target.Source.IsRongyu == 1 || !FixedPageConditionCompilation.HasLogicalFields(definition.TargetPage)) &&
        GlobalMultipleRelationTarget.IsStoredStaticPage(definition.Target.Source, definition.TargetPage.Source,
            definition.TargetPage.Condition?.Syntax);

    /// <summary>来源和目标均绑定稳定元数据ID，客户端不能替换表或页面。</summary>
    internal static IReadOnlyList<MultiSelectModel> ForPage(GenerationModel model, TableModel table, PageModel page) => page.Readable.Concat(page.Writable).DistinctBy(column => column.Source.Id)
        .Where(column => column.Source.ControlKind == "MultiSelectPage").Select(column => Resolve(model, table, page, column).Definition).OfType<MultiSelectModel>().ToArray();

    /// <summary>服务规则覆盖表级关系，隐藏字段仍需在复制或父依赖变化时验证；不会因此下发额外VO字段。</summary>
    internal static IReadOnlyList<MultiSelectModel> ForService(GenerationModel model, TableModel table, PageModel page) => table.Columns.Where(column => column.Source.ControlKind == "MultiSelectPage")
        .Select(column => Resolve(model, table, page, column, forService: true).Definition).OfType<MultiSelectModel>().ToArray();

    /// <summary>非冗余显式复选使用已发布多选页；独立父级必须在本列表能取得实际标量。</summary>
    internal static MultiSelectModel? CheckboxFilterOptions(GenerationModel model, TableModel table, PageModel page, ColumnModel source)
    {
        if (source.Source.ControlKind != "MultiSelectPage" ||
            !ControlRenderer.UsesMultiIdCheckboxFilter(source) ||
            SelectionNameFilterCompilation.EffectiveFilterKind(model, page, source) != "MultiSelectPage")
            return null;
        var definition = Resolve(model, table, page, source, forService: true).Definition;
        if(table.Source.IsRongyu!=0&&definition is not {SharedTextParent:not null,Target.Source.IsRongyu:0})
            return null;
        if (definition?.TargetParentField is {} field &&
            !page.Searchable.Any(column => column.JsonName == field && ControlRenderer.HasScalarSourceFilter(model, page, column)))
            return null;
        return definition;
    }

    /// <summary>额外SQL或未知父映射返回明确缺口，保留原配置而不扩大候选范围。</summary>
    internal static MultiSelectResult Resolve(GenerationModel model, TableModel table, PageModel page, ColumnModel source, bool forService = false, bool rootDefinition = false)
    {
        MultiSelectResult Gap(string reason) => new(null, "MultiSelectPage尚未支持：" + reason);
        var functionForm = table.Source.IsFunctionTable == 1;
        if (table.Source.IsRongyu is not (0 or 1)) return Gap("未识别的多选存储模式。");
        if (source.Type != "string?" || !source.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal)) return Gap("字段必须为已声明的字符串_ids存储。");
        if (!forService && !page.Readable.Any(column => column.Source.Id == source.Source.Id)) return Gap("来源页未开放ID字段，无法形成完整字段合同。");
        try
        {
            var root = Read(source.Source.ControlDetail);
            var field = rootDefinition ? null : model.Catalog.ConfigPageColumns.SingleOrDefault(candidate => candidate.ConfigPageId == page.Source.Id && candidate.ConfigColumnId == source.Source.Id);
            var overlay = Read(field?.Extra.GetValueOrDefault("controlDetail").ToString() ?? "");
            string Value(string key, string fallback = "") { var value = Scalar(overlay, key); if (string.IsNullOrWhiteSpace(value)) value = Scalar(root, key); return string.IsNullOrWhiteSpace(value) ? fallback : value; }
            var pageId = Value("pageId", source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString());
            var target = model.Tables.SingleOrDefault(candidate => candidate.Pages.Any(targetPage => targetPage.Source.Id == pageId));
            var targetPage = target?.Pages.Single(candidate => candidate.Source.Id == pageId);
            if (target is null || target.Source.IsFunctionTable == 1 || targetPage is null || !SelectionPageRenderer.Enabled(targetPage)) return Gap("目标必须是已声明的type=2多选页面。");
            if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) && source.Source.LinkConfigTableId != target.Source.Id) return Gap("目标页与声明关联表不一致。");
            if (!target.IsGlobal && !target.Columns.Any(column => column.Source.ColumnName == "yz_project_id")) return Gap("目标未声明项目归属或明确全局范围。");
            var name = target.Columns.SingleOrDefault(column => column.Source.ColumnName == Value("nameField", "name"));
            // R1全局名称副本必须通过完整发布证明，普通物理Join与任意缓存继续拒绝。
            var globalCachedName=name is not null&&target.IsGlobal&&NonRedundantReadCompilation.Physical(model,target,name)&&
                GlobalPhysicalRelationValue.IsPublishedScalar(model.Catalog,MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,name.Source);
            var scalarLabel=name is not null&&SupportsTypedLabel(model,table,source,target,name);
            if (name is null || GenerationModel.Enabled(name.Source.Extra,"isJoinColumn")&&!DerivedMultiNamesCompilation.Declared(model,target,name)&&!globalCachedName || name.IsJsonArray || name.Type != "string?" && !name.IsId&&!scalarLabel || !targetPage.Readable.Any(column => column.Source.Id == name.Source.Id)) return Gap("名称字段不在目标页可读投影或类型未支持。");
            if (ManagerCredentialCompilation.IsCredential(target,name)) return Gap("账号凭据不能作为多选名称读取列。");
            var sharedTextParent=SharedRedundantParentCompilation.CommonTextParent(model,table,page,target,targetPage,allowSparseTarget:true)??
                NonRedundantCrossParentCompilation.CommonTextParent(model,table,page,target,targetPage,allowStoredTarget:true);
            var globalBusinessExactRead=table.IsGlobal&&table.Source.IsRongyu==0&&target.Source.IsRongyu==0&&
                GlobalCrossTableTarget.DeclaresExactMulti(target.Source,targetPage.Source);
            var scoped=(sharedTextParent is null||target.Source.IsRongyu==0)&&
                (globalBusinessExactRead||FixedPageConditionCompilation.HasMultiTargetScope(model,target,targetPage));
            var derived=DerivedMultiNamesCompilation.Declared(model,target,name);
            // 功能表确认直接使用目标页的查询范围；业务表的写入和导入也需要同一精确范围。
            var exactTarget=functionForm?null:ExactDataManageCompilation.Resolve(model,target,targetPage);
            // 文本父身份独立于名称联接，非冗余目标的名称终点由固定读取计划复核。
            if(sharedTextParent is not null&&(name.Type!="string?"&&!scalarLabel||
                target.Source.IsRongyu!=0&&(derived||FixedPageConditionCompilation.HasLogicalFields(targetPage)||
                    exactTarget is not null&&!ExactDataManageCompilation.IsDirectPhysicalSource(exactTarget))))
                return Gap("共用文本父键的多选需要已证明的标量名称和发布页面条件。");

            if(!functionForm&&(scoped||derived))DerivedMultiNamesCompilation.Require(model,table,target,targetPage,name,exactTarget);
            var fixedPage=Scalar(root,"pageId");
            if(fixedPage.Length==0)fixedPage=source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
            var pageOverride=pageId!=fixedPage;
            if(sharedTextParent is not null&&pageOverride)return Gap("共用文本父键的多选需沿用表级候选页。");
            // 实时名称绑定实际候选页。普通备用页仍须证明同项目，但无需套用默认字典的固定条件。
            var projectOnlyOverride=pageOverride&&table.Source.IsRongyu==0&&!table.IsGlobal&&
                Metarill.Data.Queries.NonRedundantReads.IsProjectOnlyStoredMultiSelectPage(model.Catalog,target.Source,targetPage.Source);
            // 非冗余来源的备用全局字典页使用自己的查询和范围，持久名称副本仍使用表级页。
            var globalPageRead=pageOverride&&table.Source.IsRongyu==0&&target.IsGlobal;
            var pageRead=globalPageRead||table.Source.IsRongyu==0&&!table.IsGlobal&&!target.IsGlobal&&
                (target.Source.IsRongyu==0||projectOnlyOverride||
                    Metarill.Data.Queries.NonRedundantReads.IsProjectFixedMultiSelectTarget(model.Catalog,target.Source,targetPage.Source));
            if(!functionForm&&pageOverride&&!pageRead&&(scoped||target.Pages.Any(candidate=>candidate.Source.Id==fixedPage&&FixedPageConditionCompilation.HasMultiTargetScope(model,target,candidate))))
                return Gap("限定范围的目标页必须与表级读取和缓存绑定一致。");
            var query=FixedPageQueryCompilation.For(target,targetPage);
            var definition=new SelectionControlDefinition(pageId,name.Source.ColumnName,Value("buttonText"),Value("whereSql"),"");
            RelationConditionModel? importCondition=null;
            if(!FixedPageQueryCompilation.TryProveSelection(model.Catalog,query,definition,out _))
            {
                if(functionForm)return Gap("额外控件范围需要固定来源字段读取协议，不能使用宽范围目标页代替。");
                if(ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id)
                    return Gap("导入附加条件的目标页必须与实际存储外键一致。");
                // 原 Excel 反查叠加控件条件；普通候选和名称缓存只使用已发布的目标页范围。
                var condition=RelationConditionsCompilation.SelectionImport(model,table,page,query,definition.WhereSql);
                if(condition.HasLogicalFields)DerivedMultiNamesCompilation.ValidateLogicalImport(model,target,targetPage,name,condition,exactTarget);
                importCondition=condition;
            }
            var ordering = Value("orderBy");
            if (!string.IsNullOrWhiteSpace(ordering) && !GenerationModel.ValidateOrderBy(ordering, target.Columns).Select(item => (item.Column.Source.Id, item.Descending)).SequenceEqual(GenerationModel.ValidateOrderBy(targetPage.Source.OrderBy, target.Columns).Select(item => (item.Column.Source.Id, item.Descending)))) return Gap("控件排序与目标页不一致。");
            TableModel Parent(ColumnModel column) => model.Tables.Single(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId) ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName);
            // 非冗余跨表祖先列按固定层位归属实际父表，列前缀与真实表名可以不同。
            TableModel SourceParent(ColumnModel column) =>
                NonRedundantCrossParentCompilation.Find(model, table, page, column) is { } path
                    ? path.Read.Levels[path.Index].Target
                    : SharedRedundantParentCompilation.ForColumn(model,table,page,column).FirstOrDefault()?.Level.Target ?? Parent(column);
            var sourceScopes=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
            var sourceParents=sourceScopes.Select(column=>SourceParent(column).Source.Id).Distinct().Order().ToArray();
            var compoundLogical=!functionForm&&sourceScopes.Length>1&&sourceParents.Length==1&&
                sourceScopes.Any(column=>NonRedundantCrossParentCompilation.Managed(model,table,page,column));
            MultiTargetParentScope? scopedParent=null;
            if((scoped||compoundLogical)&&targetPage.Scopes.Any(scope=>scope.Source=="parent"))
            {
                var syntax=FixedConditionPlan.Compile(targetPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                scopedParent=target.IsGlobal?GlobalCrossTableTarget.StoredMultiParent(model.Catalog,target.Source,targetPage.Source,allowExact:exactTarget is not null)??
                    MultiTargetParentScope.CompileGlobalPage(model.Catalog,target.Source,syntax):
                    MultiTargetParentScope.CompileSelectionPage(model.Catalog,target.Source,syntax,allowGlobalParent:true,
                        allowStoredPhysicalParents:target.Source.IsRongyu==1&&
                            Metarill.Data.Queries.NonRedundantReads.IsProjectFixedMultiSelectTarget(model.Catalog,target.Source,targetPage.Source));
            }
            if(functionForm)scopedParent??=MultiGlobalParentCompilation.PhysicalPageParent(model,target,targetPage);
            var targetParents = scopedParent is not null?[model.Tables.Single(parent=>parent.Source.Id==scopedParent.Parent.Id)]:
                targetPage.Scopes.Where(scope => scope.Source == "parent").Select(scope => scope.Column==sharedTextParent&&target.Source.IsRongyu==1
                    ?SharedRedundantParentCompilation.ForColumn(model,target,targetPage,scope.Column).Single().Level.Target:Parent(scope.Column))
                    .DistinctBy(parent => parent.Source.Id).ToArray();
            if (scopedParent?.Global!=true&&targetParents.Any(parent => !parent.Columns.Any(column => column.Source.ColumnName == "yz_project_id"))) return Gap("目标父表未声明项目归属。");
            if(scopedParent?.Global==true)_=Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.Id,targetPage.Source.Id);
            // 来源父参数进入固定字典时，存在性校验必须使用同一实际整数业务键。
            var logicalParentBusinessKey=scopedParent is {Global:false,Logical:true}&&
                scopedParent.LogicalAlternateKey(model.Catalog,target.Source) is {LogicalType:"int64"} key
                ?targetParents.Single().Columns.Single(column=>column.Source.Id==key.Id):null;
            var same = targetParents.Length > 0 && sourceParents.SequenceEqual(targetParents.Select(parent => parent.Source.Id).Order());
            if(same&&sharedTextParent is null&&CrossTableReadCompilation.TextSourceParent(model,table,page) is not null)
                return Gap("多选目标父尚未证明使用与来源相同的文本业务键。");
            string? targetField = null;
            if (targetParents.Length > 1) return Gap("多个目标父定义未形成唯一合同。");
            if (targetParents.Length == 1 && !same)
            {
                var fields = table.Columns.Where(column => column.Source.Id != source.Source.Id && column.Source.LinkConfigTableId == targetParents[0].Source.Id && !column.IsJsonArray && column.Type is "string?" or "long?").Take(2).ToArray();
                if (fields.Length != 1) return Gap("目标父缺少唯一可读的声明外键。");
                targetField = fields[0].JsonName;
                if (!forService && !page.Readable.Any(column => column.Source.Id == fields[0].Source.Id)) return Gap("来源页未开放目标父字段，前端不能猜测隐藏父值。");
            }
            // 普通非冗余来源按实际页面校验父键，全局关系及冗余缓存继续按其固定范围证明。
            var usesPageRead=pageRead&&(pageOverride||targetParents.Length>0||!scoped&&targetPage.Condition is not null);
            var usesTargetPageScope=usesPageRead&&!projectOnlyOverride;
            var usesNameOverride=false;
            // 混合存储由来源页还原父键，候选目标继续比较实际物理键列。
            var logicalSourceParent=sharedTextParent is not null&&table.Source.IsRongyu==0&&target.Source.IsRongyu==1;
            var publishedPhysicalSource=false;
            IReadOnlyList<ColumnModel> compoundSourceDependencies=[];
            if(scopedParent is not null && !functionForm)
            {
                var owner=usesPageRead?scopedParent.Source(model.Catalog,table.Source,source.Source,page.Source)
                    :scopedParent.Source(model.Catalog,table.Source,source.Source);
                var actual=same?sourceScopes.SingleOrDefault(column=>column.Source.Id==owner.Column.Id):
                    table.Columns.Single(column=>column.JsonName==targetField);
                logicalSourceParent=owner.LogicalPages.Count>0;
                if(actual?.Source.Id!=owner.Column.Id||logicalSourceParent&&(!same||!NonRedundantCrossParentCompilation.Managed(model,table,page,actual)))
                    return Gap("目标页父字段须与来源记录的固定父键一致。");
                publishedPhysicalSource=same&&!scopedParent.Global&&!logicalSourceParent&&!table.IsGlobal&&!target.IsGlobal&&
                    (usesPageRead||owner.AdditionalColumns.Count>0);
                if(owner.AdditionalColumns.Count>0)
                {
                    if(scopedParent.Global)
                        Metarill.Data.Queries.NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,targetPage.Source.Id,owner);
                    compoundSourceDependencies=NonRedundantCrossParentCompilation.Dependencies(model,table,page,sourceScopes);
                }
            }
            if (functionForm)
                return new(new(source, target, targetPage, name, name.IsId ? null : SelectionDisplayFieldCompilation.FunctionFormNames(table, source, name), same,
                    targetField, targetParents, Value("buttonText", "选择")){GlobalParent=scopedParent?.Global==true}, null);

            ColumnModel? names = null;
            if (!name.IsId)
            {
                var nameField = name.Source.ColumnName; var auxiliary = source.Source.ColumnName[..^4] + "_" + (nameField.EndsWith('s') ? nameField : nameField + "s");
                names = table.Source.IsRongyu==0 ? SelectionDisplayFieldCompilation.Resolve(table,source) : table.Columns.SingleOrDefault(column => column.Source.ColumnName == auxiliary && column.Type == "string?" && !column.IsSystem);
                if(names is not null&&table.Source.IsRongyu==0&&(names.Source.Id==source.Source.Id||names.Type!="string?"||!GenerationModel.Enabled(names.Source.Extra,"isJoinColumn")))return Gap("非冗余名称必须为只读联接或自动派生属性。");
                if (names is null) return Gap((table.Source.IsRongyu==0?"非冗余名称联接列尚未明确声明：":"冗余名称辅助列尚未明确声明：") + auxiliary);
                if(table.Source.IsRongyu==1&&!MultiNameStorageCompilation.Supports(model,table,source,name,names))return Gap("冗余名称缓存没有绑定同一来源ID和目标标量路径。");
                if (!forService && table.Source.IsRongyu==1 && !MultiNameStorageCompilation.IsImplicit(model,names) && !page.Readable.Any(column => column.Source.Id == names.Source.Id)) return Gap("冗余名称辅助列未在来源页开放读取。");
            }
            if(table.Source.IsRongyu==0)
            {
                var baselineName=Scalar(root,"nameField");if(string.IsNullOrWhiteSpace(baselineName))baselineName="name";
                if(ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id)
                    return Gap("页级目标与实际存储外键不一致。");
                var changedName=baselineName!=name.Source.ColumnName;
                if(changedName&&(table.IsGlobal||target.IsGlobal))
                    return Gap("全局多选页级名称必须沿用发布的读取合同。");
                // 以 ID 显示时使用原 ID 数组，不要求页面另有名称列，也不登记名称覆盖。
                usesNameOverride=changedName&&!name.IsId;
                if(usesNameOverride)
                {
                    var baseline=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==baselineName);
                    if(names is null||baseline is null||
                        !PhysicalTextLabel(model,target,baseline)||!PhysicalTextLabel(model,target,name))
                        return Gap("页级名称必须为同一项目内目标的物理文本，沿用表级名称数组。");
                }
                if(!NonRedundantReadCompilation.Physical(model,table,source))return Gap("非冗余多选要求物理ID和标量名称。");
                if(target.IsGlobal)
                {
                    var proof=globalPageRead
                        ?GlobalMultipleRelationTarget.RequirePage(model.Catalog,table.Source,source.Source,target.Source,page.Source)
                        :GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,source.Source,target.Source);
                    if(proof.Page.Id!=targetPage.Source.Id||proof.Name.Id!=name.Source.Id)
                        return Gap("全局多选页级目标和名称声明不一致。");
                }
                if(targetField is not null&&!NonRedundantReadCompilation.Physical(model,table,table.Columns.Single(column=>column.JsonName==targetField)))return Gap("非冗余目标父必须为物理外键。");
                if(names is not null)
                {
                    if(usesPageRead||usesNameOverride)
                        Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(model.Catalog,table.Source.Id,[names.Source.ColumnName],
                            [],[],[],[new(source.Source.Id,page.Source.Id,targetPage.Source.Id,usesTargetPageScope)]);
                    else
                        Metarill.Data.Queries.NonRedundantReads.Compile(model.Catalog,table.Source.Id,[names.Source.ColumnName]);
                }
            }
            if(table.Source.IsRongyu==1&&target.IsGlobal){var proof=Metarill.Core.Metadata.GlobalMultipleRelationTarget.RequireCached(model.Catalog,table.Source,source.Source,target.Source);if(proof.Page.Id!=targetPage.Source.Id||proof.Name.Id!=name.Source.Id)return Gap("全局多选缓存与固定目标页／名称声明不一致。");}
            // R1派生数组不能只证明候选：同一根定义还须取得数据层缓存证明，Full报告与实际发布保持一致。
            if(table.Source.IsRongyu==1&&names is not null&&(scoped||derived))Metarill.Data.Relations.DerivedCrossTableCaches.Validate(model.Catalog,[new(table.Source.Id,source.Source.Id,target.Source.Id,name.Source.Id,names.Source.Id,true)],[]);
            return new(new(source, target, targetPage, name, names, same, targetField, targetParents, Value("buttonText", "选择"))
                {
                    LogicalSourceParent=logicalSourceParent,
                    SharedTextParent=sharedTextParent,
                    LogicalParentBusinessKey=logicalParentBusinessKey,
                    CompoundSourceDependencies=compoundSourceDependencies,
                    PublishedPhysicalSource=publishedPhysicalSource,
                    GlobalParent=scopedParent?.Global==true,
                    UsesPageRead=usesPageRead,
                    UsesTargetPageScope=usesTargetPageScope,
                    UsesNameOverride=usesNameOverride,
                    ImportCondition=importCondition,
                    ExactTarget=exactTarget,
                    UsesGlobalBusinessExactRead=globalBusinessExactRead
                }, null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or NotSupportedException) { return Gap("配置或范围无法安全编译：" + error.Message); }
    }

    /// <summary>页级显示只覆盖普通物理文本，保留现有数组协议及索引范围。</summary>
    internal static bool PhysicalTextLabel(GenerationModel model,TableModel target,ColumnModel label)=>
        label.Type=="string?"&&!label.IsJsonArray&&label.Source.ControlKind!="FileLibrary"&&
        !Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(label.Source.ColumnName)&&
        NonRedundantReadCompilation.Physical(model,target,label);

    /// <summary>生成与数据层使用同一标量终点证明，范围仍由固定目标页证明。</summary>
    internal static bool SupportsTypedLabel(GenerationModel model,TableModel table,ColumnModel source,TableModel target,ColumnModel name)
    {
        if(name.IsId||!Metarill.Core.Contracts.MultiValueLabel.IsTyped(name.Source.LogicalType))return false;
        var storage=MetadataStoragePolicy.ForTable(model.Catalog,target.Source);
        return MultiNameStorageColumns.IsPhysicalScalarLabel(table.Source,source.Source,target.Source,name.Source,storage)||
            MultiNameStorageColumns.IsDerivedScalarLabel(target.Source,name.Source,storage)||
            GlobalPhysicalRelationValue.IsPublishedTypedCache(model.Catalog,storage,target.Source,name.Source);
    }

    /// <summary>重复键、数组配置或复杂映射值必须拒绝，未知合法键保持在原元数据。</summary>
    private static JsonObject Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != doc.RootElement.EnumerateObject().Count()) throw new InvalidOperationException("控件明细必须为无重复键对象。");
        return JsonNode.Parse(text)!.AsObject();
    }
    /// <summary>只解释原协议中的标量配置，不执行脚本或SQL。</summary>
    private static string Scalar(JsonObject source, string key) => source[key] is null ? "" : source[key]!.GetValueKind() is JsonValueKind.String or JsonValueKind.Number ? source[key]!.ToString() : throw new InvalidOperationException("控件映射字段必须为标量。");
}
