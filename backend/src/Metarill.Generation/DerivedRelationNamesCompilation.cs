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
    /// <summary>普通目标页的级联条件在原候选和当前读中重用同一发布范围。</summary>
    internal static bool UsesLogicalPage(RelationModel relation)=>
        FixedPageConditionCompilation.HasLogicalFields(relation.TargetPage)||relation.Conditions?.HasLogicalFields==true;

    /// <summary>全局非冗余目标的账号范围来自实际发布页，仅绑定认证的账号值。</summary>
    internal static bool HasGlobalAccountScope(TableModel target,PageModel page)=>
        target.IsGlobal&&target.Source.IsRongyu==0&&FixedPageConditionCompilation.HasAccountVariables(page);

    /// <summary>在生成时证明目标页所有逻辑成员，同时保留原来源身份与物理父范围。</summary>
    internal static NonRedundantReadPlan CompileLogicalPage(GenerationModel model,TableModel target,PageModel page,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,RelationConditionModel? additional=null)
    {
        if(ExactDataManageCompilation.Declared(page)||NonRedundantCrossParentCompilation.Enabled(model,target,page))
            throw new NotSupportedException("单值关系的逻辑条件要求普通目标页及已证明的物理父范围。");
        if(target.IsGlobal)
            return additional is {HasLogicalFields:true}
                ?NonRedundantReads.CompileGlobalListCandidate(model.Catalog,target.Source.Id,page.Source.Id,additional.Syntax.Fields,outputs,dependencies)
                :NonRedundantReads.CompileCrossPageCandidate(model.Catalog,target.Source.Id,page.Source.Id,outputs,dependencies);
        var (fields,cross,tree)=LogicalMembership(page,additional);
        return NonRedundantReads.CompilePageMembership(model.Catalog,target.Source,page.Source,fields,cross,tree,outputs,dependencies);
    }

    /// <summary>生成查询复用发布时核验的字段集合，调用方仅绑定身份和业务参数。</summary>
    internal static string LogicalPagePlanStart(TableModel target,PageModel page,RelationConditionModel? additional=null)
    {
        var targetIds=JsonSerializer.Serialize(target.Source.Id)+","+JsonSerializer.Serialize(page.Source.Id);
        string Fields(IEnumerable<string> names)=>"new string[]{"+string.Join(",",names.Select(name=>JsonSerializer.Serialize(name)))+"}";
        if(target.IsGlobal)return additional is {HasLogicalFields:true}
            ?"CompileGlobalListCandidate("+targetIds+","+Fields(additional.Syntax.Fields)
            :"CompileCrossPageCandidate("+targetIds;
        var (fields,cross,tree)=LogicalMembership(page,additional);
        return "CompileRelationMembership("+string.Join(",",targetIds,
            Fields(fields),Fields(cross.Order(StringComparer.Ordinal)),Fields(tree.Order(StringComparer.Ordinal)));
    }

    /// <summary>固定页面与下拉附加条件共用一份完整路径和最小字段集合。</summary>
    private static (IReadOnlyList<string> Fields,IReadOnlySet<string> Cross,IReadOnlySet<string> Tree) LogicalMembership(
        PageModel page,RelationConditionModel? additional)=>
        ((page.Condition?.Syntax.Fields??[]).Concat(additional is {HasLogicalFields:true}?additional.Syntax.Fields:[])
            .Distinct(StringComparer.Ordinal).ToArray(),
        (page.Condition?.CrossFields??Enumerable.Empty<string>()).Concat(additional?.CrossFields??Enumerable.Empty<string>())
            .ToHashSet(StringComparer.Ordinal),
        (page.Condition?.SelfTreeFields??Enumerable.Empty<string>()).Concat(additional?.SelfTreeFields??Enumerable.Empty<string>())
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>有类型的物理名称与派生名称共用候选搜索、回显和工作簿范围。</summary>
    internal static bool UsesFixedRead(GenerationModel model, RelationModel relation) =>
        Declared(model, relation) || IsPhysicalTypedName(model, relation.Target, relation.NameColumn);

    /// <summary>物理名称仍使用真实声明类型作为 SQL 投影和精确反查的参数类型。</summary>
    internal static bool IsPhysicalTypedName(GenerationModel model, TableModel target, ColumnModel name) =>
        IsTypedLabel(name) && NonRedundantReadCompilation.Physical(model, target, name);

    /// <summary>只接受已接通的业务标量，主键继续按原独立标识合同处理。</summary>
    internal static bool IsTypedLabel(ColumnModel name) =>
        !name.IsId && !name.IsJsonArray && MultiValueLabel.IsTyped(name.Source.LogicalType);

    /// <summary>全局来源要求同目录固定目标，R1 另核验物理副本。</summary>
    internal static bool IsTypedName(GenerationModel model, TableModel owner, TableModel target, ColumnModel name) =>
        (owner.IsGlobal ? owner.Source.IsRongyu is 0 or 1 && target.IsGlobal : SupportedOwner(model, owner)) &&
        IsTypedLabel(name) &&
        (target.Source.IsRongyu == 0 || target.Source.IsRongyu == 1 && IsPhysicalTypedName(model, target, name));

    /// <summary>日期与日期时间有相同的 CLR 类型，终点校验直接比较声明的逻辑类型。</summary>
    internal static string NameType(ColumnModel name) =>
        name.Type == "string?" ? "string" : IsTypedLabel(name) ? name.Source.LogicalType :
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
        if(!SupportedOwner(model,owner)||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"&&!IsTypedName(model,owner,target,name)||name.IsJsonArray||!page.Source.Extra.TryGetValue("type",out var kind)||kind.ToString()!="1"||ExactDataManageCompilation.Declared(page)||NonRedundantCrossParentCompilation.Enabled(model,target,page))throw new InvalidOperationException("派生单选名称要求普通R0或已发布R1物理来源、普通R0目标及原type=1固定单值页面。");
        if(page.Scopes.Any(scope=>scope.Column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,scope.Column)))
            throw new InvalidOperationException("派生单选名称的目标身份与父字段必须为物理标量。");
        if (target.IsGlobal)
        {
            if(FixedPageConditionCompilation.HasSelfTreeFields(page))
                GlobalCrossTableTarget.RequireScopedSelfTreeName(model.Catalog,target.Source,page.Source,name.Source);
            else if(FixedPageConditionCompilation.HasCrossFields(page)||HasGlobalAccountScope(target,page))
                GlobalCrossTableTarget.RequireCrossTableCandidate(model.Catalog,target.Source,page.Source,name.Source);
            else if (name.Type == "string?") GlobalCrossTableTarget.RequireSingleName(model.Catalog, target.Source, page.Source, name.Source);
            else GlobalCrossTableTarget.RequireSingle(model.Catalog, target.Source, page.Source);
        }
        // 全局候选按当前名称读取，不借用仅允许int64路径的精确管理匹配用途。
        var plan=FixedPageConditionCompilation.HasLogicalFields(page)
            ?CompileLogicalPage(model,target,page,[name.Source.ColumnName],[name.Source.ColumnName])
            :HasGlobalAccountScope(target,page)
                ?NonRedundantReads.CompileGlobalCandidate(model.Catalog,target.Source.Id,page.Source.Id,[name.Source.ColumnName])
                :target.IsGlobal?NonRedundantReads.Compile(model.Catalog,target.Source.Id,[name.Source.ColumnName])
                    :NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,name.Source.ColumnName);
        if (plan.OutputTypes[name.Source.ColumnName] != NameType(name))
            throw new InvalidOperationException("派生单选名称未落到与声明一致的固定标量。");
    }
    /// <summary>普通目标及父条件必须完整证明，暂不把逻辑跨表父或平台固定归属当作普通物理范围。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,PageModel page,RelationModel relation)
    {
        var target=relation.Target;var name=relation.NameColumn;
        if (relation.SourceColumn.Source.ControlKind == "SelectPage" && Declared(model, relation))
            RequireSelection(model, owner, target, relation.TargetPage, name);
        var typedName = IsTypedName(model, owner, target, name);
        var physicalTypedName = IsPhysicalTypedName(model, target, name);
        if(!(typedName && owner.IsGlobal) && !SupportedOwner(model,owner) ||
            relation.SourceColumn.Source.ControlKind is not("List" or "SelectPage")||
            target.Source.IsRongyu!=0&&!physicalTypedName||target.Source.IsFunctionTable!=0||target.Source.IsView!=0||
            name.Type != "string?" && !typedName || name.IsJsonArray || relation.ParentBinding?.Declared==true||ExactDataManageCompilation.Declared(relation.TargetPage)||NonRedundantCrossParentCompilation.Enabled(model,target,relation.TargetPage))
            throw new InvalidOperationException("固定名称关系要求已发布来源、物理标量或已证明的派生目标及候选范围。");
        if (typedName && ForeignValidationRenderer.Target(model, relation.SourceColumn)?.Source.Id != target.Source.Id)
            throw new InvalidOperationException("名称候选必须与来源外键使用同一固定目标。");
        if(target.IsGlobal)
        {
            var proof=owner.Source.IsRongyu==1?GlobalSingleRelationTarget.RequireCached(model.Catalog,owner.Source,relation.SourceColumn.Source,target.Source):GlobalSingleRelationTarget.Require(model.Catalog,owner.Source,relation.SourceColumn.Source,target.Source);
            if(proof.Page.Id!=relation.TargetPage.Source.Id||proof.Name.Id!=name.Source.Id)throw new InvalidOperationException("全局派生候选与固定名称或目标页不一致。");
        }
        if(owner.Source.IsRongyu==1&&(!NonRedundantReadCompilation.Physical(model,owner,relation.SourceColumn)||GenerationModel.Enabled(relation.SourceColumn.Source.Extra,"isJoinColumn")||relation.SourceColumn.Source.IsVirtualColumn!=0||relation.SourceColumn.Source.LogicalType is not("int64" or "string")))throw new InvalidOperationException("R1派生候选必须使用本表真实单值外键。");
        if (typedName && owner.Source.IsRongyu == 1)
        {
            var cache = SelectionDisplayFieldCompilation.Resolve(owner, relation.SourceColumn);
            // 全局来源沿用发布证明中的两种原外键路径写法。
            var sameDisplay = owner.IsGlobal
                ? GlobalCachedSourceCompilation.ForModel(model).Any(binding =>
                    binding.Foreign.Source.Id == relation.SourceColumn.Source.Id && binding.Cache.Source.Id == cache.Source.Id)
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
        if(fields.Any(column=>!NonRedundantReadCompilation.Physical(model,target,column)||column.IsJsonArray))throw new InvalidOperationException("派生名称候选的身份、父及附加范围必须为已发布物理标量。");
        var orderFields = relation.OptionsOrder?.Select(item => item.Column.Source.ColumnName) ?? [];
        var dependencies = fields.Select(column => column.Source.ColumnName).Concat(orderFields)
            .Concat(["id", name.Source.ColumnName]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputs = new[] { "id", name.Source.ColumnName };
        var plan = UsesLogicalPage(relation)
            ? CompileLogicalPage(model,target,relation.TargetPage,outputs,dependencies,relation.Conditions)
            : target.IsGlobal && physicalTypedName || NonRedundantReadCompilation.HasGlobalRowProject(target) ||
                HasGlobalAccountScope(target,relation.TargetPage)
            ? NonRedundantReads.CompileGlobalCandidate(model.Catalog, target.Source.Id, relation.TargetPage.Source.Id, outputs, dependencies)
            : physicalTypedName && target.Source.IsRongyu == 1
                ? NonRedundantReads.CompilePhysicalRelation(model.Catalog,target.Source.Id,outputs,dependencies,
                    SharedRedundantParentCompilation.AccountPlans(model,target,relation.TargetPage))
                : NonRedundantReads.Compile(model.Catalog, target.Source.Id, outputs, dependencies);
        if (plan.OutputTypes["id"] != "int64" || plan.OutputTypes[name.Source.ColumnName] != NameType(name))
            throw new InvalidOperationException("关系名称必须编译为与声明一致的固定标量。");
        return dependencies;
    }
    /// <summary>普通R0保持原合同；R1只为明确发布、有真实主键／项目的物理消费者开放同一固定候选。</summary>
    private static bool SupportedOwner(GenerationModel model,TableModel owner)
    {
        if(owner.Source.IsRongyu==0)return true;
        return owner.Source.IsRongyu==1&&!owner.IsGlobal&&owner.Source.IsView==0&&owner.Source.IsFunctionTable==0&&owner.Source.YzProjectId==model.Catalog.YzProjectId&&owner.Columns.Any(column=>column.IsId&&column.Source.LogicalType=="int64"&&NonRedundantReadCompilation.Physical(model,owner,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))&&owner.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"&&column.Source.LogicalType is "int64" or "string"&&NonRedundantReadCompilation.Physical(model,owner,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"));
    }

}
