using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Metarill.Generation;

internal sealed record RelationModel(ColumnModel SourceColumn, TableModel Target, PageModel TargetPage, ColumnModel NameColumn, string Endpoint,
    IReadOnlyList<(ColumnModel Column, string Source)> Scopes, IReadOnlyList<TableModel> SourceParents, IReadOnlyList<TableModel> TargetParents,
    FixedParentRelationResult? ParentBinding = null, IReadOnlyList<(ColumnModel Column, bool Descending)>? OptionsOrder = null,
    RelationConditionModel? Conditions = null, ColumnModel? FixedSourceParent = null, MultiTargetParentScope? FixedParent = null, bool LogicalSourceParent = false)
{
    internal bool ListPageOverride { get; init; }
    internal bool ScopedStoredSingle { get; init; }
    // 全局固定页要求当前读锁，但不进入项目内冗余下拉的名称投影路径。
    internal bool FixedGlobalSelection { get; init; }
    internal bool CompoundProjectSource { get; init; }
    internal bool CompoundProjectTarget { get; init; }
    internal bool CanonicalProjectParent { get; init; }
    internal IReadOnlyList<ColumnModel> IndependentOrder { get; init; } = [];
    internal RelationConditionModel? ImportCondition { get; init; }
    internal bool ExcelOnly { get; init; }
    internal bool CompoundGlobalSource { get; init; }
    internal bool FunctionForm { get; init; }
    internal bool ExcelTargetExact { get; init; }
    internal ColumnModel? SharedTextParent { get; init; }
    internal bool MixedStoredTextParent=>SharedTextParent is not null&&LogicalSourceParent&&Target.Source.IsRongyu==1;
    internal string ExcelScopeArgument => ExcelTargetExact ? ", excelScope: true" : "";
    /// <summary>原 V8 路由按来源字段去掉 Id/Ids 后缀，不能用目标表名替代业务别名。</summary>
    public string LegacyEndpoint
    {
        get
        {
            var name = SourceColumn.Property;
            if (name.EndsWith("Ids", StringComparison.Ordinal)) name = name[..^3];
            else if (name.EndsWith("Id", StringComparison.Ordinal)) name = name[..^2];
            return "findNameId" + name + SourceColumn.Source.Id;
        }
    }
}

/// <summary>关系选项只能编译已声明表、名称列和目标页面，禁止直接解释外部 SQL。</summary>
internal static class RelationCompilation
{
    /// <summary>把字段与页面覆盖合并后编译一次，三种候选入口共用同一范围。</summary>
    public static IReadOnlyList<RelationModel> ForPage(GenerationModel model, PageModel page, bool forService = false,
        string? scopeSourceId = null, bool forExcel = false)
    {
        var relations = new List<RelationModel>();
        // Java V8 在项目含非冗余实体时使用固定候选计划，纯冗余项目沿用旧实体查询。
        var fixedCandidateQuery = ProjectScopedSingleRelationTarget.UsesFixedListCandidates(model.Catalog);
        var owner=model.Tables.Single(table=>table.Source.Id==page.Source.ConfigTableId);
        var functionForm = owner.Source.IsFunctionTable == 1;
        // 普通冗余单选的弹窗直达目标页，Excel 仅生成私有名称范围，不增加候选路由。
        var imported = forExcel && ImportRenderer.Enabled(page) && ImportRenderer.Mode(page) != "custom" ? ImportRenderer.Columns(page) : [];
        bool ExcelSelection(ColumnModel column) => forExcel && owner.Source.IsRongyu == 1 && !column.IsSystem &&
            column.Source.ControlKind == "SelectPage" && (imported.Contains(column) || ExportRenderer.IncludesColumn(page, column));
        // 系统项目上下文保留既有List等接线，新缓存能力不额外追加隐藏候选或授权依赖。
        var candidates = forService && !functionForm ? owner.Columns.Where(column=>FixedParentRelationCompilation.HasDeclaration(column)||owner.Source.IsRongyu==0&&column.Source.ControlKind=="SelectPage"||!column.IsSystem&&(DerivedRelationNamesCompilation.DeclaredSource(model,column)||FixedListTarget(model,page,column)||FixedSelectionTarget(model,page,column,includeExact:true)||(column.Source.ControlKind is "List" or "SelectPage")&&ForeignValidationRenderer.Target(model,column)?.IsGlobal==true)||ExcelSelection(column)) : page.Readable.Concat(page.Writable)
            .Concat(page.Searchable.Where(column => column.Source.ControlKind == "List" && ControlRenderer.UsesScalarCheckboxFilter(column) &&
                SelectionNameFilterCompilation.EffectiveFilterKind(model, page, column) == "List"))
            .DistinctBy(x => x.Source.Id);
        if (scopeSourceId is not null) candidates = owner.Columns.Where(column => column.Source.Id == scopeSourceId);
        foreach (var source in candidates)
        {
            // 功能表单选直达实际目标页，各处无需为无实体表单生成持久外键的校验或额外候选入口。
            if(functionForm&&source.Source.ControlKind=="SelectPage")continue;
            var standard = source.Source.ControlKind == "List" || source.Source.ControlKind == "SelectPage" &&
                (owner.Source.IsRongyu == 0 || !source.IsSystem && (ForeignValidationRenderer.Target(model,source)?.IsGlobal == true ||
                    DerivedRelationNamesCompilation.DeclaredSource(model,source) || FixedSelectionTarget(model,page,source,includeExact:forService)));
            var excelSelection = ExcelSelection(source);
            var excelOnly = !standard && excelSelection;
            if ((!standard && !excelOnly) || FixedOptionCatalogCompilation.Resolve(model, page, source).Declared) continue;
            var field = model.Catalog.ConfigPageColumns.SingleOrDefault(candidate => candidate.ConfigPageId == page.Source.Id && candidate.ConfigColumnId == source.Source.Id);
            var root = Definition(source.Source.ControlDetail);
            var overlay = Definition(field?.Extra.GetValueOrDefault("controlDetail").ToString() ?? "");
            // 仅编译已知映射键，原元数据和未知合法键保持原样；页面非空配置覆盖字段定义。
            string Mapped(string key, string fallback = "")
            {
                var value = Text(overlay, key); if (string.IsNullOrWhiteSpace(value)) value = Text(root, key);
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            var tableId = Mapped("tableId", source.Source.LinkConfigTableId); var tableName = Mapped("table");
            // 非冗余单选的弹层、导入/导出与辅助接口必须使用同一已证明目标页。
            var selection=source.Source.ControlKind=="SelectPage"?NonRedundantSelectionCompilation.Resolve(model,owner,page,source):null;
            var target = selection?.Target ?? model.Tables.SingleOrDefault(t => !string.IsNullOrEmpty(tableId) ? t.Source.Id == tableId : t.Source.TableName == tableName);
            var functionPageId = functionForm ? Mapped("pageId", source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString()) : "";
            if (functionPageId.Length > 0)
            {
                var published = model.Tables.SingleOrDefault(table => table.Pages.Any(candidate => candidate.Source.Id == functionPageId));
                if (published is null || target is not null && target != published ||
                    tableId.Length > 0 && tableId != published.Source.Id ||
                    tableName.Length > 0 && tableName != published.Source.TableName ||
                    source.Source.LinkConfigTableId.Length > 0 && source.Source.LinkConfigTableId != published.Source.Id ||
                    source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString() is { Length: > 0 } linked && linked != functionPageId)
                    throw new InvalidOperationException("功能表下拉的已发布目标页与声明目标表或页面不一致。");
                target = published;
            }
            else if (source.Source.ControlKind == "List" && tableId.Length == 0 && tableName.Length == 0)
            {
                // 固定候选可通过表级的已发布页面证明实际实体，仍由项目内或全局绑定各自核验。
                var linkedTarget = ForeignValidationRenderer.Target(model, source);
                if (Mapped("pageId").Length == 0 ||
                    linkedTarget is not null && (UsesProjectListPage(model,linkedTarget,page,source) ||
                        linkedTarget.IsGlobal && fixedCandidateQuery))
                    target = linkedTarget;
            }
            if (target is null) continue; // 保留尚未移植的外部关系配置，能力清单明确列出未接入字段。
            if (target.Source.IsFunctionTable == 1)
                throw new InvalidOperationException("功能表没有可读取的候选记录：" + target.Source.TableName);
            if (!target.IsGlobal && !target.Columns.Any(column => column.Source.ColumnName == "yz_project_id"))
                throw new InvalidOperationException("关联目标未声明项目归属或明确的全局范围：" + target.Source.TableName);
            var name = selection?.Name ?? target.Columns.SingleOrDefault(c => c.Source.ColumnName == Mapped("nameField", "name"));
            if (name is null) continue;
            var typedName = DerivedRelationNamesCompilation.IsTypedName(model, owner, target, name);
            // 原整数下拉可展示真实主键。显示、候选和写入都沿外键已声明的实体及实际发布页。
            var listId = name.IsId && !functionForm && source.Source.ControlKind == "List" &&
                source.Type == "long?" && source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal) &&
                !owner.IsGlobal && name.Type == "long" &&
                NonRedundantReadCompilation.Physical(model,owner,source) && NonRedundantReadCompilation.Physical(model,target,name) &&
                ForeignValidationRenderer.Target(model,source)?.Source.Id == target.Source.Id &&
                (tableName.Length == 0 || tableName == target.Source.TableName);
            if (name.Type != "string?" && !typedName && selection?.Name.IsId!=true && !listId) continue;
            if (ManagerCredentialCompilation.IsCredential(target,name)) throw new InvalidOperationException("账号凭据不能作为关系名称、候选或导出读取列。");
            var targetPage = selection?.TargetPage ?? (functionPageId.Length > 0
                ? target.Pages.Single(candidate => candidate.Source.Id == functionPageId)
                : ListTargetPage(model, target, page, source));
            if (functionForm && target.IsGlobal && functionPageId.Length == 0)
                throw new InvalidOperationException("功能表下拉的全局目标需要显式发布页面。");
            var globalListPageScope = false;
            var globalFixedSelection = false;
            if(!functionForm&&target.IsGlobal&&(owner.Source.IsRongyu==0||!source.IsSystem))
            {
                var global=GlobalTargetForPage(model,owner,page,source,target);
                targetPage=target.Pages.Single(candidate=>candidate.Source.Id==global.Page.Id);
                globalListPageScope=global.UsesListPageScope;
                globalFixedSelection=global.FixedSelectionPage||global.ScopedPhysicalSelectionParent;
                if(name.Source.Id!=global.Name.Id&&!listId&&selection?.Name.IsId!=true)
                    throw new InvalidOperationException("全局关系页级名称覆盖与固定显示来源不一致。");
            }
            if(owner.IsGlobal&&!target.IsGlobal&&GlobalCachedSourceCompilation.ForModel(model)
                .SingleOrDefault(binding=>binding.Source.Source.Id==owner.Source.Id&&binding.Foreign.Source.Id==source.Source.Id) is {} parent)
            {
                targetPage=target.Pages.Single(candidate=>candidate.Source.Id==parent.Page.Source.Id);
                if(name.Source.Id!=parent.Name.Source.Id)throw new InvalidOperationException("普通父关系页级名称与发布来源不一致。");
            }
            if (targetPage is null) continue;
            var projectListPage = !functionForm && UsesProjectListPage(model,target,page,source);
            var listPageOverride = globalListPageScope || projectListPage && overlay.ContainsKey("pageId");
            if (listId && !target.IsGlobal && Mapped("pageId") is {Length:>0} declaredPage && declaredPage != targetPage.Source.Id)
                throw new InvalidOperationException("主键下拉的目标页声明与写入关系不一致。");
            if (!targetPage.Readable.Any(column => column.Source.Id == name.Source.Id)) throw new InvalidOperationException("关联名称列未在目标页开放读取。");
            // 原单选的附加条件已经逐项证明属于目标页，逻辑父和固定条件由同一页面计划执行。
            var fixedParent = functionForm
                ? FunctionTableSelectionCompilation.PublishedParent(model, target, targetPage)
                : DerivedRelationNamesCompilation.SingleParent(model,target,targetPage)??
                    PhysicalTargetForLogicalSource(model,owner,page,target,targetPage);
            var where = source.Source.ControlKind == "List"
                ? ProjectScopedSingleRelationTarget.ListWhereSql(model.Catalog,page.Source,source.Source)
                : selection is not null && (excelOnly || selection.ImportCondition is not null ||
                    fixedParent is { Logical:true } or { AdditionalTargets.Count:>0 } ||
                    FixedPageConditionCompilation.HasLogicalFields(targetPage)) ? "" : Mapped("whereSql");
            var (scopes, conditions) = RelationConditionsCompilation.Compile(model,owner,page,target,where,allowLogicalList:selection is null);
            if ((conditions is not null||FixedPageConditionCompilation.HasLogicalFields(targetPage))&&
                !functionForm && ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id)
                throw new InvalidOperationException("固定条件的候选必须与外键及显示来源的目标一致。");
            var sourceParents = page.Scopes.Where(scope => scope.Source == "parent").Select(scope => Parent(model, scope.Column, owner, page)).DistinctBy(parent => parent.Source.Id).ToArray();
            var accountParent=SharedRedundantParentCompilation.HasAccountPath(model,target,targetPage);
            var targetParents = targetPage.Scopes.Concat(scopes).Where(scope => scope.Source == "parent")
                .Select(scope => fixedParent?.HasTarget(scope.Column.Source.Id)==true
                    ?model.Tables.Single(table=>table.Source.Id==fixedParent.Parent.Id)
                    :accountParent?Parent(model,scope.Column,target,targetPage):Parent(model,scope.Column))
                .DistinctBy(parent => parent.Source.Id).ToArray();
            var binding = FixedParentRelationCompilation.Resolve(model, page, source, sourceParents, targetParents, targetPage.Scopes.Concat(scopes).ToArray());
            ColumnModel? sharedTextParent=null;
            // 即使实体相同，文本业务键也不能隐式代入关系目标的主键父范围。
            if(targetParents.Any(parent=>sourceParents.Any(sourceParent=>sourceParent.Source.Id==parent.Source.Id))&&
                CrossTableReadCompilation.TextSourceParent(model,owner,page) is not null)
            {
                sharedTextParent=SharedRedundantParentCompilation.CommonTextParent(model,owner,page,target,targetPage,allowSparseTarget:true)??
                    NonRedundantCrossParentCompilation.CommonTextParent(model,owner,page,target,targetPage,allowStoredTarget:true);
                if(sharedTextParent is null||binding.Declared||scopes.Any(scope=>scope.Source=="parent"&&scope.Column!=sharedTextParent)||
                    !NonRedundantReadCompilation.Physical(model,target,name)||name.Type!="string?"||
                    target.Source.IsRongyu!=0&&(ExactDataManageCompilation.Declared(targetPage)||FixedPageConditionCompilation.HasLogicalFields(targetPage))||
                    conditions?.HasLogicalFields==true)
                    throw new NotSupportedException("关联目标与来源共用实体时，必须证明其父键也使用同一文本业务键。");
                if(target.Source.IsRongyu==1)fixedParent=null;
                // 非冗余字典的页条件和精确匹配字段由同一个已发布的逻辑父计划验证。
                else if(fixedParent is not {Logical:true}||!fixedParent.HasTarget(sharedTextParent.Source.Id,logicalOnly:true))
                    throw new NotSupportedException("关联目标的文本父键必须沿目标页实际级联路径读取。");
            }
            IReadOnlyList<(ColumnModel Column, bool Descending)>? ordering = null;
            ColumnModel[] independentOrder = [];
            var usesFixedListOrder = fixedCandidateQuery && source.Source.ControlKind == "List";
            // 固定计划逐键合并。页面显式留空时回到目标页，不重新采用表级字段排序。
            var orderBy = usesFixedListOrder ? Text(overlay.ContainsKey("orderBy") ? overlay : root, "orderBy") : Mapped("orderBy");
            if (usesFixedListOrder && string.IsNullOrWhiteSpace(orderBy))
            {
                var linkedPage = Text(overlay.ContainsKey("pageId") ? overlay : root, "pageId");
                if (string.IsNullOrWhiteSpace(linkedPage))
                    linkedPage = source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
                // 自动寻找的目标页供权限和名称使用，只有原声明绑定的实际页面才能提供默认排序。
                if (linkedPage == targetPage.Source.Id)
                    orderBy = targetPage.Source.OrderBy;
            }
            // 单选弹层和辅助回读采用同一目标排序，包括稳定的主键次序。
            if (typedName && selection is not null)
                ordering = FixedPageQueryCompilation.For(target, targetPage).DefaultOrder;
            else if (!string.IsNullOrWhiteSpace(orderBy) && (binding.Binding is not null || source.Source.ControlKind == "List"))
            {
                try
                {
                    var order = GenerationModel.ValidateOrderBy(orderBy, target.Columns).ToList();
                    var joined = order.Select(item => item.Column).Where(column =>
                        !NonRedundantReadCompilation.Physical(model, target, column) &&
                        !(target.Source.IsRongyu == 0 && column.Source.Id == name.Source.Id)).DistinctBy(column => column.Source.Id).ToArray();
                    if (order.Any(item => item.Column.IsJsonArray || item.Column.Source.ControlKind is "Title" or "Tab" ||
                            ManagerCredentialCompilation.IsCredential(target, item.Column)) ||
                        joined.Length > 0 && (source.Source.ControlKind != "List" || binding.Declared ||
                            joined.Any(column => !CascadeTargetOrderCompilation.CanUseJoin(model, target, targetPage, column))))
                        throw new InvalidOperationException("候选排序须引用物理标量、当前显示名或目标页开放的固定单值联接。");
                    independentOrder = joined;

                    // 原排序不含主键时补稳定次序，避免同值候选在两页之间换位。
                    if (!order.Any(item => item.Column.IsId))
                        order.Add((target.Columns.Single(column => column.IsId), false));
                    ordering = order;
                }
                catch (InvalidOperationException) when (binding.Binding is not null)
                {
                    binding = new(true, null, "固定归属候选排序不能安全编译。");
                }
            }
            var scopedStoredList=target.Source.IsRongyu==1&&
                (globalListPageScope||projectListPage&&
                    ProjectScopedSingleRelationTarget.ListPageId(model.Catalog,page.Source,source.Source)==targetPage.Source.Id)&&
                (HasStoredListRange(targetPage)||conditions is not null||scopes.Any(scope=>scope.Source=="parent"));
            var selectionParent=selection is null?null:
                ProjectScopedSingleRelationTarget.PhysicalSelectionParent(model.Catalog,target.Source,targetPage.Source);
            var storedSelectionParent=target.Source.IsRongyu==1?selectionParent:null;
            // 业务读取按已发布单选页过滤已保存的名称，单选及 Excel 仍各自校验目标精确权限。
            var scopedStoredSelection=storedSelectionParent is not null||
                selection is not null&&owner.Source.IsRongyu==0&&!owner.IsGlobal&&!target.IsGlobal&&
                target.Source.IsRongyu==1&&targetPage.Condition is not null&&
                (!ExactDataManageCompilation.Declared(targetPage)||
                    ProjectScopedSingleRelationTarget.HasPhysicalSingleExact(model.Catalog,target.Source,targetPage.Source,"SelectPage"));
            var sourceKeys=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column)
                .DistinctBy(column=>column.Source.Id).ToArray();
            var projectParentScope=sharedTextParent is null&&!functionForm&&!binding.Declared&&!owner.IsGlobal&&!target.IsGlobal&&
                (selectionParent is not null||scopedStoredList||source.Source.ControlKind=="List"&&target.Source.IsRongyu==0);
            var sameParentTable=sourceParents is [var sourceParent]&&targetParents is [var targetParent]&&
                sourceParent.Source.Id==targetParent.Source.Id;
            var compoundSource=projectParentScope&&sameParentTable&&sourceKeys.Length>1&&
                sourceKeys.All(column=>NonRedundantReadCompilation.Physical(model,owner,column));
            var targetKeys=targetPage.Scopes.Concat(scopes).Where(scope=>scope.Source=="parent")
                .Select(scope=>scope.Column).DistinctBy(column=>column.Source.Id).Take(2).ToArray();
            // 目标复合范围及一对一的文本父键都使用实际发布字段，同父关系不依赖字符串隐式等值。
            var compoundTarget=projectParentScope&&targetParents.Length==1&&targetKeys.Length>1;
            var singleTextParent=projectParentScope&&sameParentTable&&sourceKeys is [var singleSource]&&targetKeys is [var singleTarget]&&
                (singleSource.Type=="string?"||singleTarget.Type=="string?")&&
                NonRedundantReadCompilation.Physical(model,owner,singleSource)&&NonRedundantReadCompilation.Physical(model,target,singleTarget);
            var publishedParent=selectionParent;
            if(publishedParent is null&&(compoundSource||compoundTarget||singleTextParent))
            {
                var fields=target.Columns.Select(column=>column.Source.ColumnName);
                var targetTerms=FixedConditionPlan.Compile(targetPage.Source.WhereSql,fields).Terms;
                var controlTerms=FixedConditionPlan.Compile(where,fields).Terms;
                publishedParent=MultiTargetParentScope.CompileListPage(model.Catalog,target.Source,targetTerms.Concat(controlTerms));
            }
            var compoundProjectSource=compoundSource?publishedParent?.SingleSource(model.Catalog,owner.Source,source.Source,page.Source):null;
            if(compoundSource&&compoundProjectSource is not {AdditionalColumns.Count:>0})
                throw new InvalidOperationException("原单值关联缺少已发布的来源复合物理父键。");
            var canonicalProjectParent=singleTextParent&&publishedParent is {Logical:false,Global:false}&&
                publishedParent.SingleSource(model.Catalog,owner.Source,source.Source,page.Source).Column.Id==sourceKeys[0].Source.Id;
            var parentProof=sharedTextParent is null?fixedParent??storedSelectionParent:fixedParent;
            var sourceBinding=compoundProjectSource??(parentProof is null||functionForm?null:
                owner.Source.IsRongyu==0&&(globalListPageScope||!owner.IsGlobal&&!target.IsGlobal)
                    ?parentProof.SingleSource(model.Catalog,owner.Source,source.Source,page.Source)
                    :parentProof.SingleSource(model.Catalog,owner.Source,source.Source));
            if(fixedParent is {Global:true}&&sourceBinding is {AdditionalColumns.Count:>0})
                NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,targetPage.Source.Id,sourceBinding);
            if (functionForm && targetParents.Length > 1)
                throw new InvalidOperationException("功能表下拉必须绑定唯一目标父范围。");
            var mixedStoredTextParent=sharedTextParent is not null&&owner.Source.IsRongyu==0&&target.Source.IsRongyu==1;
            var fixedSourceParent = functionForm && targetParents.Length == 1
                ? FunctionTableSelectionCompilation.ResolveParentField(page, source, targetParents[0].Source)
                : mixedStoredTextParent ? sourceKeys.Single()
                : sourceBinding is null ? null : owner.Columns.Single(column => column.Source.Id == sourceBinding.Column.Id);
            var relation=new RelationModel(source,target,targetPage,name,"findNameId"+target.Name+source.Source.Id,scopes,sourceParents,targetParents,binding,ordering,conditions,
                fixedSourceParent,fixedParent,mixedStoredTextParent||sourceBinding?.LogicalPages.Count>0)
            {
                ListPageOverride = listPageOverride,
                ScopedStoredSingle = sharedTextParent is null&&(scopedStoredList||scopedStoredSelection),
                SharedTextParent = sharedTextParent,
                FixedGlobalSelection = globalFixedSelection,
                CompoundProjectSource = compoundProjectSource is not null,
                CompoundProjectTarget = compoundTarget&&publishedParent is {Logical:false,Global:false,AdditionalTargets.Count:>0},
                CanonicalProjectParent = canonicalProjectParent,
                IndependentOrder = independentOrder,
                FunctionForm = functionForm,
                ImportCondition = selection?.ImportCondition,
                ExcelOnly = excelOnly,
                CompoundGlobalSource = fixedParent is {Global:true}&&sourceBinding is {AdditionalColumns.Count:>0},
                ExcelTargetExact = excelSelection && ExactDataManageCompilation.Declared(targetPage)
            };
            // 中间页的目标绑定只复用关系定义；外层计划随后编译完整的名称，避免孙级名称校验反复进入同一页。
            if (scopeSourceId is null && relation.IndependentOrder.Count > 0)
                DerivedRelationNamesCompilation.RequireCandidateOrder(model, relation);
            if(scopeSourceId is null&&(DerivedRelationNamesCompilation.UsesFixedRead(model,relation)||relation.IndependentOrder.Count>0))
                _=DerivedRelationNamesCompilation.Require(model,owner,page,relation);
            relations.Add(relation);
        }
        return relations;
    }

    /// <summary>同一关联的列表和详情从原外键展示主键，名称页继续使用表级辅助属性。</summary>
    internal static bool ListDisplaysId(GenerationModel model, PageModel page, ColumnModel source)
    {
        return source.Source.ControlKind == "List" && source.Type == "long?" && ListIdSetting(model,page,source) &&
            ForPage(model,page,scopeSourceId:source.Source.Id).SingleOrDefault()?.NameColumn.IsId == true;
    }

    /// <summary>列表和服务范围采用相同覆盖键，空页配置继续继承字段。</summary>
    private static bool ListIdSetting(GenerationModel model,PageModel page,ColumnModel source)
    {
        var field = model.Catalog.ConfigPageColumns.SingleOrDefault(item => item.ConfigPageId == page.Source.Id &&
            item.ConfigColumnId == source.Source.Id);
        var selected = Text(Definition(field?.Extra.GetValueOrDefault("controlDetail").ToString() ?? ""), "nameField");
        if (string.IsNullOrWhiteSpace(selected)) selected = Text(Definition(source.Source.ControlDetail), "nameField");
        return selected == "id";
    }

    /// <summary>同一业务父表允许中间页从级联取得父键，目标页使用该表的真实物理外键。</summary>
    private static MultiTargetParentScope? PhysicalTargetForLogicalSource(GenerationModel model,TableModel owner,PageModel sourcePage,
        TableModel target,PageModel targetPage)
    {
        if(owner.IsGlobal||target.IsGlobal||owner.Source.IsRongyu!=0||target.Source.IsRongyu!=0||
            targetPage.Scopes.Count(scope=>scope.Source=="parent")!=1)return null;
        var sourceParents=sourcePage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
        if(!sourceParents.Any(column=>NonRedundantCrossParentCompilation.Find(model,owner,sourcePage,column) is not null))
            return null;
        var condition=FixedConditionPlan.Compile(targetPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
        var parent=MultiTargetParentScope.Compile(model.Catalog,target.Source,condition);
        return parent is {Logical:false,Global:false}&&
            sourceParents.All(column=>Parent(model,column,owner,sourcePage).Source.Id==parent.Parent.Id)?parent:null;
    }

    /// <summary>实时下拉和单选按实际使用页证明派生名称；冗余副本保持全表唯一绑定。</summary>
    internal static GlobalSingleRelationPlan GlobalTargetForPage(GenerationModel model,TableModel owner,PageModel page,
        ColumnModel source,TableModel target)
    {
        if(owner.Source.IsRongyu==1)
            return GlobalSingleRelationTarget.RequireCached(model.Catalog,owner.Source,source.Source,target.Source);
        return GlobalSingleRelationTarget.Require(model.Catalog,owner.Source,source.Source,target.Source,page.Source);
    }

    /// <summary>有账号或逻辑范围的下拉即使隐藏，也须复核默认值和最终持久化值。</summary>
    private static bool FixedListTarget(GenerationModel model,PageModel page,ColumnModel source)
    {
        if(source.Source.ControlKind!="List"||ForeignValidationRenderer.Target(model,source) is not {} target)return false;
        // 已声明的非冗余目标页即使在隐藏入口也必须可解析，不能把失效绑定当作无范围。
        if (ListTargetPage(model,target,page,source,requireExplicit:UsesProjectListPage(model,target,page,source)) is not {} targetPage)
            return false;
        if(HasFixedTargetScope(target,targetPage)||DerivedRelationNamesCompilation.UsesExactSingleTarget(target,targetPage)||ListIdSetting(model,page,source))return true;
        var effective=ProjectScopedSingleRelationTarget.ListWhereSql(model.Catalog,page.Source,source.Source);
        var owner=model.Tables.Single(table=>table.Source.Id==page.Source.ConfigTableId);
        return SharedRedundantParentCompilation.CommonTextParent(model,owner,page,target,targetPage) is not null||
            target.Source.IsRongyu==1&&NonRedundantCrossParentCompilation.CommonTextParent(model,owner,page,target,targetPage,allowStoredTarget:true) is not null||
            target.Source.IsRongyu==1 && UsesProjectListPage(model,target,page,source) &&
                ProjectScopedSingleRelationTarget.ListPageId(model.Catalog,page.Source,source.Source)==targetPage.Source.Id &&
                (HasStoredListRange(targetPage) || !string.IsNullOrWhiteSpace(effective)) ||
            RelationConditionsCompilation.HasLogicalListFields(target,effective);
    }

    /// <summary>隐藏的单选必须沿实际固定目标页复核默认和提交值。</summary>
    private static bool FixedSelectionTarget(GenerationModel model,PageModel page,ColumnModel source,bool includeExact)
    {
        if(source.Source.ControlKind!="SelectPage"||ForeignValidationRenderer.Target(model,source) is not {} target)return false;
        var owner=model.Tables.Single(table=>table.Source.Id==page.Source.ConfigTableId);
        // 只有内部保存和选档需要追加精确权限专用的范围。普通弹窗仍直接打开实际单选页。
        bool HasScope(PageModel candidate)=>includeExact&&DerivedRelationNamesCompilation.UsesExactSingleTarget(target,candidate)||
            target.Source.IsRongyu==0&&(HasFixedTargetScope(target,candidate)||
                NonRedundantReadCompilation.SelfTreeExactColumn(target,candidate) is not null)||
            target.Source.IsRongyu==1&&(ProjectScopedSingleRelationTarget.PhysicalSelectionParent(model.Catalog,target.Source,candidate.Source) is not null||
                SharedRedundantParentCompilation.CommonTextParent(model,owner,page,target,candidate) is not null);
        if(!target.Pages.Any(HasScope))return false;
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==source.Source.Id);
        var definition=SelectionControlDefinition.Resolve(source,field);
        return target.Pages.Any(candidate=>candidate.Source.Id==definition.PageId&&HasScope(candidate));
    }

    /// <summary>目标页固定条件或父范围用于候选和内部最终值校验，包括原本隐藏的外键。</summary>
    internal static bool HasFixedTargetScope(TableModel target,PageModel targetPage)=>
        FixedPageConditionCompilation.HasLogicalFields(targetPage)||FixedPageConditionCompilation.HasAccountVariables(targetPage)||
        NonRedundantReadCompilation.GlobalSingleParentPage(target,targetPage)||HasProjectTargetScope(target,targetPage);

    /// <summary>项目内 R0 目标的额外固定范围也用于保护手写规则提交的外键与父键。</summary>
    internal static bool HasProjectTargetScope(TableModel target,PageModel targetPage)=>
        !target.IsGlobal&&target.Source.IsRongyu==0&&
        (targetPage.Condition is not null||targetPage.Scopes.Any(scope=>scope.Source=="parent"));

    /// <summary>主键下拉的目标发布页存在父范围时，写前复核及钩子使用同一条件。</summary>
    internal static bool HasFixedListIdParent(RelationModel relation) =>
        relation.SourceColumn.Source.ControlKind == "List" && relation.NameColumn.IsId && relation.TargetParents.Count > 0;

    /// <summary>运行时和私有写校验使用同一个原绑定页或原后台的无父目标页。</summary>
    internal static PageModel? ListTargetPage(GenerationModel model,TableModel target,PageModel page,ColumnModel source,
        bool requireExplicit=true)
    {
        var projectListPage = UsesProjectListPage(model,target,page,source);
        var explicitPage = projectListPage
            ? ProjectScopedSingleRelationTarget.ListPageId(model.Catalog,page.Source,source.Source)
            : source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        if (projectListPage && explicitPage.Length>0 &&
            ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id)
            throw new InvalidOperationException("下拉目标页必须使用原外键关联的同一实体。");
        return !string.IsNullOrWhiteSpace(explicitPage)
            ?target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==explicitPage)
                ??(requireExplicit?throw new InvalidOperationException("关联目标页不存在或不属于目标表。"):null)
            :target.Pages.FirstOrDefault(candidate=>candidate.Source.YzAdminTypeId==page.Source.YzAdminTypeId&&!candidate.Scopes.Any(scope=>scope.Source=="parent"))
                ??target.Pages.FirstOrDefault(candidate=>!candidate.Scopes.Any(scope=>scope.Source=="parent"));
    }

    /// <summary>固定冗余字典可使用页面常量、账号和父参数，原纯冗余项目不进入此范围。</summary>
    private static bool HasStoredListRange(PageModel page) =>
        page.Condition is not null || page.Scopes.Any(scope=>scope.Source=="parent");

    /// <summary>项目内固定下拉采用已发布页面，系统固定归属和全局关系沿原专用入口。</summary>
    private static bool UsesProjectListPage(GenerationModel model,TableModel target,PageModel page,ColumnModel source) =>
        source.Source.ControlKind=="List" && !target.IsGlobal && target.Source.IsView==0 && target.Source.IsFunctionTable==0 &&
        (target.Source.IsRongyu==0 || target.Source.IsRongyu==1 && ProjectScopedSingleRelationTarget.UsesFixedListCandidates(model.Catalog)) &&
        !FixedParentRelationCompilation.HasDeclaration(source) &&
        model.Tables.Any(owner=>owner.Source.Id==page.Source.ConfigTableId && !owner.IsGlobal && owner.Source.IsFunctionTable==0);

    /// <summary>父范围必须引用真实已声明的表与项目键，不能按请求选择父定义。</summary>
    private static TableModel Parent(GenerationModel model, ColumnModel column, TableModel? owner=null, PageModel? page=null)
    {
        // 隐式跨表父位沿已证明读取链解析，不能靠列名前缀猜目标表。
        var cross=owner is not null&&page is not null?NonRedundantCrossParentCompilation.Find(model,owner,page,column):null;
        var redundant=owner is not null&&page is not null?SharedRedundantParentCompilation.ForColumn(model,owner,page,column).FirstOrDefault():null;
        var parent = redundant?.Level.Target ?? (cross is{} path?path.Read.Levels[path.Index].Target:model.Tables.SingleOrDefault(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)
            ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName))
            ?? throw new InvalidOperationException("关联父范围缺少明确目标表：" + column.Source.ColumnName);
        if(parent.IsGlobal&&owner is not null&&page is not null&&GlobalCachedParentCompilation.ForColumn(model,owner,page,column)?.Target.Source.Id==parent.Source.Id)return parent;
        if(parent.IsGlobal&&(redundant is not null||cross is not null))return parent;
        if(parent.IsGlobal&&owner is not null&&page is not null&&MultiGlobalParentCompilation.PhysicalScopeTarget(model,owner,page) is not null)return parent;
        if(parent.IsGlobal&&owner is not null&&page is not null&&SharedGlobalParentCompilation.ForColumn(model,owner,page,column).Count>0)return parent;
        if (!parent.Columns.Any(candidate => candidate.Source.ColumnName == "yz_project_id")) throw new InvalidOperationException("关联父表未声明项目归属。");
        return parent;
    }

    /// <summary>只接受对象，重复键不得导致不同入口采用不同配置解释。</summary>
    private static JsonObject Definition(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        using var parsed = JsonDocument.Parse(text);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || parsed.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != parsed.RootElement.EnumerateObject().Count())
            throw new InvalidOperationException("关联控件明细必须为无重复键的对象。");
        return JsonNode.Parse(text)!.AsObject();
    }
    /// <summary>映射键只接受标量字符串/数字，不把复杂对象转成 SQL。</summary>
    private static string Text(JsonObject source, string key) => source[key] is null ? "" : source[key]!.GetValueKind() is JsonValueKind.String or JsonValueKind.Number
        ? source[key]!.ToString() : throw new InvalidOperationException("关联控件映射字段类型无效。");
}
