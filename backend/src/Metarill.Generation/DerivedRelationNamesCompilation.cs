using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

/// <summary>派生候选名称固定在目标R0的标量读计划中，不能用IsIgnore属性生成物理SQL。</summary>
internal static class DerivedRelationNamesCompilation
{
    /// <summary>冗余物理缓存继续沿原路径；只识别实际非物理名称。</summary>
    internal static bool Declared(GenerationModel model,RelationModel relation)=>!NonRedundantReadCompilation.Physical(model,relation.Target,relation.NameColumn);
    /// <summary>原单值控件的叶名精确范围需要固定树投影，即使候选页没有其它逻辑条件。</summary>
    internal static bool UsesSelfTreeExact(RelationModel relation)=>
        relation.SourceColumn.Source.ControlKind is "List" or "SelectPage"&&
        NonRedundantReadCompilation.SelfTreeExactColumn(relation.Target,relation.TargetPage) is not null;

    /// <summary>原单值控件按实际发布的目标页执行已声明的精确权限。</summary>
    internal static bool UsesExactSingle(RelationModel relation)=>
        relation.SourceColumn.Source.ControlKind is "List" or "SelectPage"&&
        (UsesExactSingleTarget(relation.Target,relation.TargetPage)||IsGlobalExactSingle(relation));

    /// <summary>隐藏单值外键也使用实际目标页范围，供写入登记复用。</summary>
    internal static bool UsesExactSingleTarget(TableModel target,PageModel page)=>
        !target.IsGlobal&&ExactDataManageCompilation.Declared(page);

    /// <summary>精确单选的 Excel 逻辑条件可以引入额外账号范围，仅由导入计划绑定。</summary>
    internal static bool UsesSeparateExactImportPlan(RelationModel relation)=>
        UsesExactSingle(relation)&&relation.ImportCondition is {HasLogicalFields:true};

    /// <summary>交互候选只纳入自己需要的逻辑投影。</summary>
    internal static RelationConditionModel? ProjectionCondition(RelationModel relation)=>
        relation.Conditions??(UsesExactSingle(relation)&&!UsesSeparateExactImportPlan(relation)?relation.ImportCondition:null);

    /// <summary>固定读取直接核对逻辑父、同父多范围及项目内目标引用的全局父页。</summary>
    internal static bool EmbedsParentScope(TableModel target,MultiTargetParentScope? parent)=>
        parent is {Logical:true} or {AdditionalTargets.Count:>0}||!target.IsGlobal&&parent is {Global:true};

    /// <summary>控件独立父字段由候选过滤器承接，字典页读计划只嵌入自己的父参数。</summary>
    internal static bool EmbedsParentScope(RelationModel relation)=>
        relation.ControlParentPageId is null&&EmbedsParentScope(relation.Target,relation.FixedParent);

    /// <summary>普通候选的逻辑条件来自发布页、父级或原控件查询。</summary>
    internal static bool UsesLogicalPage(RelationModel relation)=>
        EmbedsParentScope(relation)||FixedPageConditionCompilation.HasLogicalFields(relation.TargetPage)||
        relation.Conditions?.HasLogicalFields==true||UsesSelfTreeExact(relation);

    /// <summary>全局非冗余目标及冗余单选的账号范围来自实际发布页，生成目录须保留该页范围。</summary>
    internal static bool HasGlobalAccountScope(TableModel target,PageModel page)=>
        target.IsGlobal&&(target.Source.IsRongyu==0||page.Source.Extra.GetValueOrDefault("type").ToString()=="1")&&
        FixedPageConditionCompilation.HasAccountVariables(page);

    /// <summary>单值候选父级来自实际发布页，项目内稀疏层位与显式全局父复用同一父键模型。</summary>
    internal static MultiTargetParentScope? SingleParent(GenerationModel model,TableModel target,PageModel page)=>
        GlobalCrossTableTarget.DeclaresExactSingle(target.Source,page.Source)
            ?GlobalCrossTableTarget.ExactSingleParent(model.Catalog,target.Source,page.Source)
            :target.IsGlobal?GlobalCrossTableTarget.SingleParent(model.Catalog,target.Source,page.Source)
                :ProjectScopedSingleRelationTarget.PageParent(model.Catalog,target.Source,page.Source);

    /// <summary>在生成时证明目标页所有逻辑成员，同时保留来源身份和实际父范围。</summary>
    internal static NonRedundantReadPlan CompileLogicalPage(GenerationModel model,TableModel target,PageModel page,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,RelationConditionModel? additional=null,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,ExactDataManageModel? exact=null)
    {
        var logicalParent=EmbedsParentScope(target,SingleParent(model,target,page));
        if(ExactDataManageCompilation.Declared(page)&&(exact is null||target.IsGlobal)||
            NonRedundantCrossParentCompilation.Enabled(model,target,page)&&!logicalParent)
            throw new NotSupportedException("单值关系的逻辑条件要求普通目标页及已证明的父范围。");
        if (logicalParent && exact is not null)
            return NonRedundantReads.CompileExactSingleParentCandidate(model.Catalog,target.Source.Id,page.Source.Id,outputs,dependencies,
                NonRedundantCrossTableCompilation.Bindings(exact.CrossPlans ?? []),singleTargets,
                additional is {HasLogicalFields:true}?additional.Syntax.Fields:null);
        if(target.IsGlobal||logicalParent)
            return additional is {HasLogicalFields:true}
                ?NonRedundantReads.CompileGlobalListCandidate(model.Catalog,target.Source.Id,page.Source.Id,additional.Syntax.Fields,outputs,dependencies,singleTargets)
                :NonRedundantReads.CompileCrossPageCandidate(model.Catalog,target.Source.Id,page.Source.Id,outputs,dependencies,null,singleTargets);
        var (fields,cross,tree)=LogicalMembership(page,additional,exact);
        return NonRedundantReads.CompilePageMembership(model.Catalog,target.Source,page.Source,fields,cross,tree,outputs,dependencies,singleTargets);
    }

    /// <summary>生成查询复用发布时核验的字段集合，调用方仅绑定身份和业务参数。</summary>
    internal static string LogicalPagePlanStart(GenerationModel model,TableModel target,PageModel page,
        RelationConditionModel? additional=null,ExactDataManageModel? exact=null)
    {
        var targetIds=JsonSerializer.Serialize(target.Source.Id)+","+JsonSerializer.Serialize(page.Source.Id);
        string Fields(IEnumerable<string> names)=>"new string[]{"+string.Join(",",names.Select(name=>JsonSerializer.Serialize(name)))+"}";
        var logicalParent = EmbedsParentScope(target,SingleParent(model,target,page));
        if (logicalParent && exact is not null) return "CompileExactSingleParentCandidate("+targetIds;
        if(target.IsGlobal||logicalParent)return additional is {HasLogicalFields:true}
            ?"CompileGlobalListCandidate("+targetIds+","+Fields(additional.Syntax.Fields)
            :"CompileCrossPageCandidate("+targetIds;
        var (fields,cross,tree)=LogicalMembership(page,additional,exact);
        return "CompileRelationMembership("+string.Join(",",targetIds,
            Fields(fields),Fields(cross.Order(StringComparer.Ordinal)),Fields(tree.Order(StringComparer.Ordinal)));
    }

    /// <summary>固定条件及精确权限共用真实发布路径。权限字段只扩充依赖，授权值仍由当前账号提供。</summary>
    private static (IReadOnlyList<string> Fields,IReadOnlySet<string> Cross,IReadOnlySet<string> Tree) LogicalMembership(
        PageModel page,RelationConditionModel? additional,ExactDataManageModel? exact)
    {
        var cross = (page.Condition?.CrossFields ?? Enumerable.Empty<string>())
            .Concat(additional?.CrossFields ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        if (exact?.CrossPlans is not null) cross.Add(exact.Column.Source.ColumnName);
        var tree = (page.Condition?.SelfTreeFields ?? Enumerable.Empty<string>())
            .Concat(additional?.SelfTreeFields ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        if (exact is {SelfTree:true}) tree.Add(exact.Column.Source.ColumnName);
        var fields = (page.Condition?.Syntax.Fields ?? []).Concat(additional is { HasLogicalFields: true } ? additional.Syntax.Fields : [])
            .Concat(cross).Concat(tree).Distinct(StringComparer.Ordinal).ToArray();
        return (fields, cross, tree);
    }

    /// <summary>名称投影和目标逻辑条件共用固定读取，物理名称也执行同样的精确权限。</summary>
    internal static bool UsesFixedRead(GenerationModel model, RelationModel relation) =>
        relation.FixedParent is {AdditionalTargets.Count:>0} || UsesExactSingle(relation) || UsesSelfTreeExact(relation) || Declared(model, relation) ||
        IsPhysicalTypedName(model, relation.Target, relation.NameColumn) || relation.NameColumn.IsId ||
        relation.Conditions is {JoinedFields.Count:>0} || FixedPageConditionCompilation.HasJoinedFields(relation.TargetPage) ||
        relation.FunctionForm && (relation.Target.IsGlobal || relation.TargetParents.Count > 0 ||
            ExactDataManageCompilation.Declared(relation.TargetPage) ||
            RelationCompilation.HasFixedTargetScope(relation.Target, relation.TargetPage) || UsesLogicalPage(relation));

    /// <summary>全局下拉和弹窗单选从实际发布页取得候选，主键显示值直接复用真实 ID。</summary>
    internal static bool UsesGlobalPrimaryKeyDisplay(RelationModel relation)=>
        relation.Target.IsGlobal&&relation.SourceColumn.Source.ControlKind is ("List" or "SelectPage")&&relation.NameColumn.IsId;

    /// <summary>读取用途与下发产物共用实际全局发布页。</summary>
    internal static bool UsesGlobalPlan(GenerationModel model, RelationModel relation) =>
        UsesGlobalPrimaryKeyDisplay(relation) ||
        relation.Target.IsGlobal && (relation.FunctionForm || IsPhysicalTypedName(model, relation.Target, relation.NameColumn)) ||
        NonRedundantReadCompilation.HasGlobalRowProject(relation.Target) || HasGlobalAccountScope(relation.Target, relation.TargetPage) ||
        NonRedundantReadCompilation.GlobalSingleParentPage(relation.Target,relation.TargetPage)||
        FixedPageConditionCompilation.HasGlobalProjectSingleScope(relation.Target,relation.TargetPage)||
        FixedPageConditionCompilation.HasGlobalListParentScope(relation.Target,relation.TargetPage);

    /// <summary>普通冗余目标的固定候选只能读取物理列及已发布的父范围。</summary>
    internal static bool UsesPhysicalPlan(GenerationModel model, RelationModel relation) =>
        relation.Target.Source.IsRongyu == 1 && (relation.FunctionForm || UsesExactSingle(relation) ||
            relation.NameColumn.IsId || IsPhysicalTypedName(model, relation.Target, relation.NameColumn));

    /// <summary>仅实际字典页及该控件的固定条件可以将物理名称副本列纳入依赖。</summary>
    internal static string[] StoredConditionCacheFields(RelationModel relation)
    {
        if(relation.SharedTextParent is null||relation.Target.Source.IsRongyu!=1)return [];
        var controlFields=relation.Conditions?.Syntax.Fields??(UsesExactSingle(relation)?relation.ImportCondition?.Syntax.Fields:null)??[];
        var fields=(relation.TargetPage.Condition?.Syntax.Fields??[]).Concat(controlFields).ToHashSet(StringComparer.Ordinal);
        return relation.Target.Columns.Where(column=>fields.Contains(column.Source.ColumnName)&&
            GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")).Select(column=>column.Source.ColumnName).ToArray();
    }

    /// <summary>物理名称仍使用真实声明类型作为 SQL 投影和精确反查的参数类型。</summary>
    internal static bool IsPhysicalTypedName(GenerationModel model, TableModel target, ColumnModel name) =>
        IsTypedLabel(name) && NonRedundantReadCompilation.Physical(model, target, name);

    /// <summary>只接受已接通的业务标量，主键继续按原独立标识合同处理。</summary>
    internal static bool IsTypedLabel(ColumnModel name) =>
        !name.IsId && !name.IsJsonArray && MultiValueLabel.IsTyped(name.Source.LogicalType);

    /// <summary>全局来源要求同目录固定目标，R1 另核验物理副本。</summary>
    internal static bool IsTypedName(GenerationModel model, TableModel owner, TableModel target, ColumnModel name) =>
        (owner.Source.IsFunctionTable == 1 ||
            (owner.IsGlobal ? owner.Source.IsRongyu is 0 or 1 && target.IsGlobal : SupportedOwner(model, owner))) &&
        IsTypedLabel(name) &&
        (target.Source.IsRongyu == 0 || target.Source.IsRongyu == 1 && IsPhysicalTypedName(model, target, name));

    /// <summary>日期与日期时间有相同的 CLR 类型，终点校验直接比较声明的逻辑类型。</summary>
    internal static string NameType(ColumnModel name) =>
        name.Type == "string?" ? "string" : name.IsId||IsTypedLabel(name) ? name.Source.LogicalType :
        throw new InvalidOperationException("关系名称不是已接通的标量类型。");

    /// <summary>隐藏字段也保留表级派生目标证明，仅用于内部最终值校验，不新增公开路由。</summary>
    internal static bool DeclaredSource(GenerationModel model,ColumnModel source)
    {
        if(source.Source.ControlKind is not("List" or "SelectPage"))return false;
        var target=ForeignValidationRenderer.Target(model,source);if(target is null)return false;
        var name="name";
        if(!string.IsNullOrWhiteSpace(source.Source.ControlDetail)){using var json=JsonDocument.Parse(source.Source.ControlDetail);if(json.RootElement.TryGetProperty("nameField",out var field)&&field.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(field.GetString()))name=field.GetString()!;}
        var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==name);
        if (display is null) return false;
        var owner = model.Tables.Single(table => table.Source.Id == source.Source.ConfigTableId);
        return !NonRedundantReadCompilation.Physical(model, target, display) || IsTypedName(model, owner, target, display);
    }
    /// <summary>type=1 单选独立证明目标；名称必须落到与声明类型一致的非凭据标量。</summary>
    internal static void RequireSelection(GenerationModel model,TableModel owner,TableModel target,PageModel page,ColumnModel name)
    {
        var globalExact=GlobalCrossTableTarget.DeclaresExactSingle(target.Source,page.Source);
        var fixedParent=SingleParent(model,target,page);
        var globalCached=owner.IsGlobal&&owner.Source.IsRongyu==1&&GlobalCachedSourceCompilation.ForModel(model)
            .Any(binding=>binding.Source.Source.Id==owner.Source.Id&&binding.ScopedSingle&&binding.Page.Source.Id==page.Source.Id);
        if(!(globalCached||SupportedOwner(model,owner))||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"&&!IsTypedName(model,owner,target,name)||name.IsJsonArray||!page.Source.Extra.TryGetValue("type",out var kind)||kind.ToString()!="1"||ExactDataManageCompilation.Declared(page)&&owner.IsGlobal&&!globalExact||NonRedundantCrossParentCompilation.Enabled(model,target,page)&&fixedParent is not {Logical:true})throw new InvalidOperationException("派生单选名称要求普通R0或已发布R1物理来源、普通R0目标及原type=1固定单值页面。");
        if(page.Scopes.Any(scope=>scope.Column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,scope.Column)&&
            fixedParent?.HasTarget(scope.Column.Source.Id,logicalOnly:true)!=true))
            throw new InvalidOperationException("派生单选名称的目标身份与父字段必须为物理标量。");
        // 物理父范围已经由候选证明；名称继续通过下面的固定投影。
        var globalJoin=target.IsGlobal&&FixedPageConditionCompilation.HasJoinedFields(page);
        var globalProject=FixedPageConditionCompilation.HasGlobalProjectSingleScope(target,page)||globalJoin&&
            GlobalCrossTableTarget.IsProjectSinglePage(model.Catalog,target.Source,page.Source);
        if(globalExact)GlobalCrossTableTarget.RequireExactSingleCandidate(model.Catalog,target.Source,page.Source);
        else if(globalProject)GlobalCrossTableTarget.RequireProjectSingleCandidate(model.Catalog,target.Source,page.Source);
        else if (target.IsGlobal&&fixedParent is null)
        {
            if(globalJoin)GlobalCrossTableTarget.RequireJoinedSingleCandidate(model.Catalog,target.Source,page.Source);
            else if(FixedPageConditionCompilation.HasSelfTreeFields(page))
                GlobalCrossTableTarget.RequireScopedSelfTreeName(model.Catalog,target.Source,page.Source,name.Source);
            else if(FixedPageConditionCompilation.HasCrossFields(page)||HasGlobalAccountScope(target,page))
                GlobalCrossTableTarget.RequireCrossTableCandidate(model.Catalog,target.Source,page.Source,name.Source);
            else if (name.Type == "string?") GlobalCrossTableTarget.RequireSingleName(model.Catalog, target.Source, page.Source, name.Source);
            else GlobalCrossTableTarget.RequireSingle(model.Catalog, target.Source, page.Source);
        }
        // 项目内和全局单选均从实际候选页证明下游字典，保留各段的固定条件。
        var outputs=new[]{name.Source.ColumnName};
        var exact=ExactDataManageCompilation.Resolve(model,target,page);
        var targets=NonRedundantReadCompilation.ProjectPathTargets(model,target,page,outputs);
        var plan=globalExact
            ?NonRedundantReads.CompileGlobalExactSingleCandidate(model.Catalog,target.Source.Id,page.Source.Id,
                outputs,[exact!.Column.Source.ColumnName],
                NonRedundantReadCompilation.MergeProjectTargets(targets.Concat(exact.SingleTargets)))
            :EmbedsParentScope(target,fixedParent)||FixedPageConditionCompilation.HasLogicalFields(page)||exact?.SelfTree==true
            ?CompileLogicalPage(model,target,page,outputs,outputs,singleTargets:targets,exact:exact)
            :globalProject||HasGlobalAccountScope(target,page)||NonRedundantReadCompilation.GlobalSingleParentPage(target,page)
                ?NonRedundantReads.CompileGlobalCandidate(model.Catalog,target.Source.Id,page.Source.Id,outputs,[],targets)
                :target.IsGlobal?NonRedundantReads.CompileProjectRelation(model.Catalog,target.Source.Id,outputs,[],targets)
                    :NonRedundantReads.CompilePublishedScalarMatch(model.Catalog,target.Source.Id,page.Source.Id,name.Source.ColumnName);
        if (plan.OutputTypes[name.Source.ColumnName] != NameType(name))
            throw new InvalidOperationException("派生单选名称未落到与声明一致的固定标量。");
    }
    /// <summary>普通目标及父条件必须完整证明，暂不把逻辑跨表父或平台固定归属当作普通物理范围。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,PageModel page,RelationModel relation)
    {
        var target=relation.Target;var name=relation.NameColumn;
        var exact = CandidateExact(model, owner, relation);
        if (relation.SourceColumn.Source.ControlKind == "SelectPage" && Declared(model, relation))
            RequireSelection(model, owner, target, relation.TargetPage, name);
        var typedName = IsTypedName(model, owner, target, name);
        var scopedGlobalCache=owner.IsGlobal&&owner.Source.IsRongyu==1&&target.IsGlobal&&
            GlobalCachedSourceCompilation.ForModel(model).Any(binding=>binding.ScopedSingle&&binding.Foreign.Source.Id==relation.SourceColumn.Source.Id);
        if(!(relation.FunctionForm||scopedGlobalCache||typedName&&owner.IsGlobal)&&!SupportedOwner(model,owner)||
            relation.SourceColumn.Source.ControlKind is not("List" or "SelectPage")||
            target.Source.IsRongyu!=0&&!UsesPhysicalPlan(model,relation)||target.Source.IsFunctionTable!=0||target.Source.IsView!=0||
            name.Type != "string?" && !typedName && !name.IsId || name.IsJsonArray || relation.ParentBinding?.Declared==true||NonRedundantCrossParentCompilation.Enabled(model,target,relation.TargetPage)&&relation.FixedParent is not {Logical:true})
            throw new InvalidOperationException("固定名称关系要求已发布来源、物理标量或已证明的派生目标及候选范围。");
        if ((typedName||UsesExactSingle(relation)) && !relation.FunctionForm &&
            ForeignValidationRenderer.Target(model, relation.SourceColumn)?.Source.Id != target.Source.Id)
            throw new InvalidOperationException("名称候选必须与来源外键使用同一固定目标。");
        if(target.IsGlobal&&!relation.FunctionForm)
        {
            var proof=RelationCompilation.GlobalTargetForPage(model,owner,page,relation.SourceColumn,target);
            if(proof.Page.Id!=relation.TargetPage.Source.Id||proof.Name.Id!=name.Source.Id&&!UsesGlobalPrimaryKeyDisplay(relation))
                throw new InvalidOperationException("全局派生候选与固定名称或目标页不一致。");
        }
        if(!relation.FunctionForm&&owner.Source.IsRongyu==1&&(!NonRedundantReadCompilation.Physical(model,owner,relation.SourceColumn)||GenerationModel.Enabled(relation.SourceColumn.Source.Extra,"isJoinColumn")||relation.SourceColumn.Source.IsVirtualColumn!=0||relation.SourceColumn.Source.LogicalType is not("int64" or "string")))throw new InvalidOperationException("R1派生候选必须使用本表真实单值外键。");
        if (typedName && owner.Source.IsRongyu == 1 && !relation.FunctionForm)
        {
            var cache = SelectionDisplayFieldCompilation.Resolve(owner, relation.SourceColumn);
            // 全局来源沿用发布证明中的两种原外键路径写法。
            var sameDisplay = owner.IsGlobal
                ? GlobalCachedSourceCompilation.ForModel(model).Any(binding =>
                    binding.Foreign.Source.Id == relation.SourceColumn.Source.Id && binding.Cache?.Source.Id == cache.Source.Id)
                : NonRedundantSelectionCompilation.IsDisplay(relation.SourceColumn, cache, name);
            if (!sameDisplay || !NonRedundantReadCompilation.Physical(model, owner, cache) ||
                page.Readable.Contains(relation.SourceColumn) && !page.Readable.Contains(cache) ||
                !MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns.GetValueOrDefault(owner.Source.TableName, [])
                    .Contains(cache.Source.ColumnName, StringComparer.Ordinal))
                throw new InvalidOperationException("R1 标量名称要求同类型的只读物理缓存及完整写入、传播和维护计划。");
        }
        var fields=relation.TargetPage.Scopes.Concat(relation.Scopes).Select(scope=>scope.Column)
            .Concat(UsesLogicalPage(relation)?[]:relation.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[])
            .Concat(relation.Conditions is {HasLogicalFields:false} conditions
                ?conditions.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field)):[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)&&
            relation.FixedParent?.HasTarget(column.Source.Id,logicalOnly:true)!=true))throw new InvalidOperationException("派生名称候选的身份、父及附加范围必须为已发布物理标量。");
        // 只有原物理及名称排序进入原候选范围，额外字典使用独立的标量计划。
        var orderFields = relation.OptionsOrder?.Where(item => !relation.IndependentOrder.Contains(item.Column))
            .Select(item => item.Column.Source.ColumnName) ?? [];
        var dependencies = fields.Select(column => column.Source.ColumnName).Concat(orderFields)
            .Concat(UsesExactSingle(relation)&&!UsesSeparateExactImportPlan(relation)?relation.ImportCondition?.Syntax.Fields??[]:[])
            .Concat(["id", name.Source.ColumnName]).Concat(exact is null ? [] : new[] { exact.Column.Source.ColumnName })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputs = name.IsId?new[]{"id"}:new[]{"id",name.Source.ColumnName};
        var projectTargets=ProjectCandidateTargets(model,relation,exact);
        var conditionCaches=StoredConditionCacheFields(relation);
        var plan = IsGlobalExactSingle(relation)
            ? NonRedundantReads.CompileGlobalExactSingleCandidate(model.Catalog,target.Source.Id,relation.TargetPage.Source.Id,
                outputs,dependencies,projectTargets,null,
                ProjectionCondition(relation) is {HasLogicalFields:true} globalCondition?globalCondition.Syntax.Fields:null)
            : UsesLogicalPage(relation)
            ? CompileLogicalPage(model,target,relation.TargetPage,outputs,dependencies,ProjectionCondition(relation),projectTargets,exact)
            : exact is not null && (exact.CrossPlans is not null || exact.SingleTargets.Count > 0)
            ? NonRedundantReadCompilation.CompilePage(model, target, outputs, dependencies,
                NonRedundantCrossTableCompilation.Bindings(exact.CrossPlans ?? []), projectTargets, [])
            : UsesGlobalPlan(model, relation)
            ? NonRedundantReads.CompileGlobalCandidate(model.Catalog, target.Source.Id, relation.TargetPage.Source.Id, outputs, dependencies, projectTargets)
            : UsesPhysicalPlan(model, relation)
                ? conditionCaches.Length>0
                    ? NonRedundantReads.CompileStoredSingleRelation(model.Catalog,target.Source.Id,relation.TargetPage.Source.Id,
                        outputs,dependencies,conditionCaches,SharedRedundantParentCompilation.AccountPlans(model,target,relation.TargetPage))
                    : NonRedundantReads.CompilePhysicalRelation(model.Catalog,target.Source.Id,outputs,dependencies,
                        SharedRedundantParentCompilation.AccountPlans(model,target,relation.TargetPage))
                : projectTargets.Count>0
                ? NonRedundantReads.CompileProjectRelation(model.Catalog,target.Source.Id,outputs,dependencies,projectTargets)
                : NonRedundantReads.Compile(model.Catalog, target.Source.Id, outputs, dependencies);
        if (plan.OutputTypes["id"] != "int64" || plan.OutputTypes[name.Source.ColumnName] != NameType(name))
            throw new InvalidOperationException("关系名称必须编译为与声明一致的固定标量。");
        if(relation.SharedTextParent is not null&&Declared(model,relation))
            plan.RequireScalarLabelOrigin(name.Source.ColumnName);
        return dependencies;
    }
    /// <summary>全局下拉及单选从实际目标页编译精确候选；弹窗仍绑定原单选页。</summary>
    internal static bool IsGlobalExactSingle(RelationModel relation)=>
        GlobalCrossTableTarget.DeclaresExactSingle(relation.Target.Source,relation.TargetPage.Source)&&
        (relation.SourceColumn.Source.ControlKind=="List"||relation.SourceColumn.Source.ControlKind=="SelectPage"&&
            relation.TargetPage.Source.Extra.GetValueOrDefault("type").ToString()=="1");

    /// <summary>原单值候选沿目标列表授权，匹配字段及投影由实际目标页证明。</summary>
    internal static ExactDataManageModel? CandidateExact(GenerationModel model, TableModel owner, RelationModel relation)
    {
        if (!ExactDataManageCompilation.Declared(relation.TargetPage)) return null;
        if(!UsesExactSingle(relation)||!relation.FunctionForm&&owner.IsGlobal&&!IsGlobalExactSingle(relation))
            throw new NotSupportedException("当前派生名称目标的精确范围缺少固定候选证明。");

        return ExactDataManageCompilation.Resolve(model, relation.Target, relation.TargetPage);
    }

    /// <summary>按实际发布页冻结排序路径，名称仍使用各自的联接和范围。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> CandidateOrderTargets(GenerationModel model, RelationModel relation) =>
        NonRedundantReadCompilation.ProjectPathTargets(model, relation.Target, relation.TargetPage,
            relation.IndependentOrder.Select(column => column.Source.ColumnName), multiValuePath: true);

    /// <summary>只允许编译能落到预期类型的单值排序投影。</summary>
    internal static void RequireCandidateOrder(GenerationModel model, RelationModel relation)
    {
        var outputs = new[] { "id" }.Concat(relation.IndependentOrder.Select(column => column.Source.ColumnName)).ToArray();
        var ranking = NonRedundantReads.CompileProjectRelation(model.Catalog, relation.Target.Source.Id, outputs, [],
            CandidateOrderTargets(model, relation));
        if (ranking.OutputTypes["id"] != "int64" || relation.IndependentOrder.Any(column =>
            ranking.OutputTypes[column.Source.ColumnName] != column.Source.LogicalType))
            throw new InvalidOperationException("下拉排序必须落到同类型的固定单值列。");
    }

    /// <summary>名称和权限匹配字段使用同一份固定中间页，避免借用名称相同的其他目标。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> ProjectCandidateTargets(GenerationModel model,
        RelationModel relation, ExactDataManageModel? exact = null, bool forImport = false)
    {
        var nameTargets = NonRedundantReadCompilation.ProjectPathTargets(model, relation.Target, relation.TargetPage,
            [relation.NameColumn.Source.ColumnName]);
        var fields=UsesExactSingle(relation)&&(forImport||!UsesSeparateExactImportPlan(relation))
            ?relation.ImportCondition?.Syntax.Fields??[]:[];
        var conditionTargets=NonRedundantReadCompilation.ProjectPathTargets(model,relation.Target,relation.TargetPage,
            fields.Concat(relation.Conditions?.JoinedFields??Enumerable.Empty<string>())
                .Concat(relation.TargetPage.Condition?.JoinedFields??Enumerable.Empty<string>()));
        return NonRedundantReadCompilation.MergeProjectTargets(nameTargets.Concat(conditionTargets).Concat(exact?.SingleTargets??[]));
    }

    /// <summary>普通R0保持原合同；R1只为明确发布、有真实主键／项目的物理消费者开放同一固定候选。</summary>
    private static bool SupportedOwner(GenerationModel model,TableModel owner)
    {
        if(owner.Source.IsRongyu==0)return true;
        return owner.Source.IsRongyu==1&&!owner.IsGlobal&&owner.Source.IsView==0&&owner.Source.IsFunctionTable==0&&owner.Source.YzProjectId==model.Catalog.YzProjectId&&owner.Columns.Any(column=>column.IsId&&column.Source.LogicalType=="int64"&&NonRedundantReadCompilation.Physical(model,owner,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))&&owner.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"&&column.Source.LogicalType is "int64" or "string"&&NonRedundantReadCompilation.Physical(model,owner,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"));
    }

}
