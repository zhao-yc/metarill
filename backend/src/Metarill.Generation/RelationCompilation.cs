using Metarill.Core.Metadata;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Metarill.Generation;

internal sealed record RelationModel(ColumnModel SourceColumn, TableModel Target, PageModel TargetPage, ColumnModel NameColumn, string Endpoint,
    IReadOnlyList<(ColumnModel Column, string Source)> Scopes, IReadOnlyList<TableModel> SourceParents, IReadOnlyList<TableModel> TargetParents,
    FixedParentRelationResult? ParentBinding = null, IReadOnlyList<(ColumnModel Column, bool Descending)>? OptionsOrder = null,
    RelationConditionModel? Conditions = null, ColumnModel? FixedSourceParent = null, MultiTargetParentScope? FixedParent = null, bool LogicalSourceParent = false)
{
    internal RelationConditionModel? ImportCondition { get; init; }
    internal bool ExcelOnly { get; init; }
    internal bool FunctionForm { get; init; }
    internal bool ExcelTargetExact { get; init; }
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
            else if (source.Source.ControlKind == "List" && tableId.Length == 0 && tableName.Length == 0 &&
                Mapped("pageId").Length == 0)
            {
                // 没有控件目标时，与实体校验、派生名称共同使用原关联页和旧表名。
                target = ForeignValidationRenderer.Target(model, source);
            }
            if (target is null) continue; // 保留尚未移植的外部关系配置，能力清单明确列出未接入字段。
            if (target.Source.IsFunctionTable == 1)
                throw new InvalidOperationException("功能表没有可读取的候选记录：" + target.Source.TableName);
            if (!target.IsGlobal && !target.Columns.Any(column => column.Source.ColumnName == "yz_project_id"))
                throw new InvalidOperationException("关联目标未声明项目归属或明确的全局范围：" + target.Source.TableName);
            var name = selection?.Name ?? target.Columns.SingleOrDefault(c => c.Source.ColumnName == Mapped("nameField", "name"));
            if (name is null) continue;
            var typedName = DerivedRelationNamesCompilation.IsTypedName(model, owner, target, name);
            if (name.Type != "string?" && !typedName) continue;
            if (ManagerCredentialCompilation.IsCredential(target,name)) throw new InvalidOperationException("账号凭据不能作为关系名称、候选或导出读取列。");
            var targetPage = selection?.TargetPage ?? (functionPageId.Length > 0
                ? target.Pages.Single(candidate => candidate.Source.Id == functionPageId)
                : ListTargetPage(target, page, source));
            if (functionForm && target.IsGlobal && functionPageId.Length == 0)
                throw new InvalidOperationException("功能表下拉的全局目标需要显式发布页面。");
            if(!functionForm&&target.IsGlobal&&(owner.Source.IsRongyu==0||!source.IsSystem))
            {
                var global=owner.Source.IsRongyu==1?Metarill.Core.Metadata.GlobalSingleRelationTarget.RequireCached(model.Catalog,owner.Source,source.Source,target.Source):Metarill.Core.Metadata.GlobalSingleRelationTarget.Require(model.Catalog,owner.Source,source.Source,target.Source);targetPage=target.Pages.Single(candidate=>candidate.Source.Id==global.Page.Id);if(name.Source.Id!=global.Name.Id)throw new InvalidOperationException("全局关系页级名称覆盖与固定显示来源不一致。");
            }
            if(owner.IsGlobal&&!target.IsGlobal&&GlobalCachedSourceCompilation.ForModel(model)
                .SingleOrDefault(binding=>binding.Source.Source.Id==owner.Source.Id&&binding.Foreign.Source.Id==source.Source.Id) is {} parent)
            {
                targetPage=target.Pages.Single(candidate=>candidate.Source.Id==parent.Page.Source.Id);
                if(name.Source.Id!=parent.Name.Source.Id)throw new InvalidOperationException("普通父关系页级名称与发布来源不一致。");
            }
            if (targetPage is null) continue;
            if (!targetPage.Readable.Any(column => column.Source.Id == name.Source.Id)) throw new InvalidOperationException("关联名称列未在目标页开放读取。");
            // 原单选的附加条件已经逐项证明属于目标页，逻辑父和固定条件由同一页面计划执行。
            var fixedParent = functionForm
                ? FunctionTableSelectionCompilation.PublishedParent(model, target, targetPage)
                : DerivedRelationNamesCompilation.SingleParent(model,target,targetPage)??
                    PhysicalTargetForLogicalSource(model,owner,page,target,targetPage);
            var where=selection is not null&&(excelOnly||selection.ImportCondition is not null||fixedParent is {Logical:true}||
                FixedPageConditionCompilation.HasLogicalFields(targetPage))?"":Mapped("whereSql");
            var (scopes, conditions) = RelationConditionsCompilation.Compile(model,owner,page,target,where,allowLogicalList:selection is null);
            if ((conditions is not null||FixedPageConditionCompilation.HasLogicalFields(targetPage))&&
                !functionForm && ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id)
                throw new InvalidOperationException("固定条件的候选必须与外键及显示来源的目标一致。");
            var sourceParents = page.Scopes.Where(scope => scope.Source == "parent").Select(scope => Parent(model, scope.Column, owner, page)).DistinctBy(parent => parent.Source.Id).ToArray();
            var accountParent=SharedRedundantParentCompilation.HasAccountPath(model,target,targetPage);
            var targetParents = targetPage.Scopes.Concat(scopes).Where(scope => scope.Source == "parent")
                .Select(scope => fixedParent?.Target.Id==scope.Column.Source.Id
                    ?model.Tables.Single(table=>table.Source.Id==fixedParent.Parent.Id)
                    :accountParent?Parent(model,scope.Column,target,targetPage):Parent(model,scope.Column))
                .DistinctBy(parent => parent.Source.Id).ToArray();
            var binding = FixedParentRelationCompilation.Resolve(model, page, source, sourceParents, targetParents, targetPage.Scopes.Concat(scopes).ToArray());
            IReadOnlyList<(ColumnModel Column, bool Descending)>? ordering = null;
            var orderBy = Mapped("orderBy");
            // 单选弹层和辅助回读采用同一目标排序，包括稳定的主键次序。
            if (typedName && selection is not null)
                ordering = FixedPageQueryCompilation.For(target, targetPage).DefaultOrder;
            else if (!string.IsNullOrWhiteSpace(orderBy) && (binding.Binding is not null ||
                source.Source.ControlKind == "List" && (typedName || NonRedundantReadCompilation.Physical(model, target, name))))
            {
                try
                {
                    // 普通候选按物理列排序，派生名称由固定 SQL 计划单独处理。
                    var order = GenerationModel.ValidateOrderBy(orderBy, target.Columns).ToList();
                    if (order.Any(item => item.Column.IsJsonArray ||
                        item.Column.Source.ControlKind is "Title" or "Tab" ||
                        ManagerCredentialCompilation.IsCredential(target, item.Column) ||
                        !NonRedundantReadCompilation.Physical(model, target, item.Column) && !(typedName && item.Column == name)))
                        throw new InvalidOperationException("候选排序只支持目标表的非凭据物理标量列及已证明的标量名称。");

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
            var sourceBinding=fixedParent is null||functionForm?null:
                !owner.IsGlobal&&!target.IsGlobal&&owner.Source.IsRongyu==0
                    ?fixedParent.SingleSource(model.Catalog,owner.Source,source.Source,page.Source)
                    :fixedParent.SingleSource(model.Catalog,owner.Source,source.Source);
            if (functionForm && targetParents.Length > 1)
                throw new InvalidOperationException("功能表下拉必须绑定唯一目标父范围。");
            var fixedSourceParent = functionForm && targetParents.Length == 1
                ? FunctionTableSelectionCompilation.ResolveParentField(page, source, targetParents[0].Source)
                : sourceBinding is null ? null : owner.Columns.Single(column => column.Source.Id == sourceBinding.Column.Id);
            var relation=new RelationModel(source,target,targetPage,name,"findNameId"+target.Name+source.Source.Id,scopes,sourceParents,targetParents,binding,ordering,conditions,
                fixedSourceParent,fixedParent,sourceBinding?.LogicalPages.Count>0)
            {
                FunctionForm = functionForm,
                ImportCondition = selection?.ImportCondition,
                ExcelOnly = excelOnly,
                ExcelTargetExact = excelSelection && ExactDataManageCompilation.Declared(targetPage)
            };
            // 中间页的目标绑定只复用关系定义；外层计划随后编译完整的名称，避免孙级名称校验反复进入同一页。
            if(scopeSourceId is null&&DerivedRelationNamesCompilation.UsesFixedRead(model,relation))
                _=DerivedRelationNamesCompilation.Require(model,owner,page,relation);
            relations.Add(relation);
        }
        return relations;
    }

    /// <summary>同一业务父表允许中间页从级联取得父键，目标页使用该表的真实物理外键。</summary>
    private static MultiTargetParentScope? PhysicalTargetForLogicalSource(GenerationModel model,TableModel owner,PageModel sourcePage,
        TableModel target,PageModel targetPage)
    {
        if(owner.IsGlobal||target.IsGlobal||owner.Source.IsRongyu!=0||target.Source.IsRongyu!=0||
            targetPage.Scopes.Count(scope=>scope.Source=="parent")!=1)return null;
        var sourceParents=sourcePage.Scopes.Where(scope=>scope.Source=="parent").Take(2).ToArray();
        if(sourceParents.Length!=1||NonRedundantCrossParentCompilation.Find(model,owner,sourcePage,sourceParents[0].Column) is not {} path)
            return null;
        var condition=FixedConditionPlan.Compile(targetPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
        var parent=MultiTargetParentScope.Compile(model.Catalog,target.Source,condition);
        return parent is {Logical:false,Global:false}&&parent.Parent.Id==path.Read.Levels[path.Index].Target.Source.Id?parent:null;
    }

    /// <summary>有账号或逻辑范围的下拉即使隐藏，也须复核默认值和最终持久化值。</summary>
    private static bool FixedListTarget(GenerationModel model,PageModel page,ColumnModel source)
    {
        if(source.Source.ControlKind!="List"||ForeignValidationRenderer.Target(model,source) is not {} target||
            ListTargetPage(target,page,source,requireExplicit:false) is not {} targetPage)return false;
        if(HasFixedTargetScope(target,targetPage)||DerivedRelationNamesCompilation.UsesExactSingleTarget(target,targetPage))return true;
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==source.Source.Id);
        var effective=Text(Definition(field?.Extra.GetValueOrDefault("controlDetail").ToString()??""),"whereSql");
        if(string.IsNullOrWhiteSpace(effective))effective=Text(Definition(source.Source.ControlDetail),"whereSql");
        return RelationConditionsCompilation.HasLogicalListFields(target,effective);
    }

    /// <summary>隐藏的单选必须沿实际固定目标页复核默认和提交值。</summary>
    private static bool FixedSelectionTarget(GenerationModel model,PageModel page,ColumnModel source,bool includeExact)
    {
        if(source.Source.ControlKind!="SelectPage"||ForeignValidationRenderer.Target(model,source) is not {} target||
            !target.Pages.Any(HasScope))return false;
        // 只有内部保存和选档需要追加精确权限专用的范围。普通弹窗仍直接打开实际单选页。
        bool HasScope(PageModel candidate)=>includeExact&&DerivedRelationNamesCompilation.UsesExactSingleTarget(target,candidate)||
            target.Source.IsRongyu==0&&(HasFixedTargetScope(target,candidate)||
                NonRedundantReadCompilation.SelfTreeExactColumn(target,candidate) is not null);
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==source.Source.Id);
        var definition=SelectionControlDefinition.Resolve(source,field);
        return target.Pages.Any(candidate=>candidate.Source.Id==definition.PageId&&HasScope(candidate));
    }

    /// <summary>目标页固定条件或父范围用于候选和内部最终值校验，包括原本隐藏的外键。</summary>
    internal static bool HasFixedTargetScope(TableModel target,PageModel targetPage)=>
        FixedPageConditionCompilation.HasLogicalFields(targetPage)||FixedPageConditionCompilation.HasAccountVariables(targetPage)||
        HasProjectTargetScope(target,targetPage);

    /// <summary>项目内 R0 目标的额外固定范围也用于保护手写规则提交的外键与父键。</summary>
    internal static bool HasProjectTargetScope(TableModel target,PageModel targetPage)=>
        !target.IsGlobal&&target.Source.IsRongyu==0&&
        (targetPage.Condition is not null||targetPage.Scopes.Any(scope=>scope.Source=="parent"));

    /// <summary>运行时和私有写校验使用同一个原绑定页或原后台的无父目标页。</summary>
    internal static PageModel? ListTargetPage(TableModel target,PageModel page,ColumnModel source,bool requireExplicit=true)
    {
        var explicitPage=source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        return !string.IsNullOrWhiteSpace(explicitPage)
            ?target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==explicitPage)
                ??(requireExplicit?throw new InvalidOperationException("关联目标页不存在或不属于目标表。"):null)
            :target.Pages.FirstOrDefault(candidate=>candidate.Source.YzAdminTypeId==page.Source.YzAdminTypeId&&!candidate.Scopes.Any(scope=>scope.Source=="parent"))
                ??target.Pages.FirstOrDefault(candidate=>!candidate.Scopes.Any(scope=>scope.Source=="parent"));
    }

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
