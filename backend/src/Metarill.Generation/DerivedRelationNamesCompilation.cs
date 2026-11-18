using System.Text.Json;
using Metarill.Data.Queries;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

/// <summary>派生候选名称固定在目标R0的标量读计划中，不能用IsIgnore属性生成物理SQL。</summary>
internal static class DerivedRelationNamesCompilation
{
    /// <summary>冗余物理缓存继续沿原路径；只识别实际非物理名称。</summary>
    internal static bool Declared(GenerationModel model,RelationModel relation)=>!NonRedundantReadCompilation.Physical(model,relation.Target,relation.NameColumn);
    /// <summary>物理整数和派生名称共用固定候选搜索、回显与工作簿。</summary>
    internal static bool UsesFixedRead(GenerationModel model, RelationModel relation) =>
        Declared(model, relation) || IsPhysicalInteger(model, relation.Target, relation.NameColumn);

    /// <summary>识别直接读取的整数业务编号，保留实际声明的存储类型。</summary>
    internal static bool IsPhysicalInteger(GenerationModel model, TableModel target, ColumnModel name) =>
        !name.IsJsonArray && name.Type is "int?" or "long?" && NonRedundantReadCompilation.Physical(model, target, name);

    /// <summary>识别固定整数终点；全局来源仍要求同目录固定目标，R1 另核验物理副本。</summary>
    internal static bool IsIntegerName(GenerationModel model, TableModel owner, TableModel target, ColumnModel name) =>
        (owner.IsGlobal ? owner.Source.IsRongyu is 0 or 1 && target.IsGlobal : SupportedOwner(model, owner)) &&
        !name.IsJsonArray && name.Type is "int?" or "long?" &&
        (target.Source.IsRongyu == 0 || target.Source.IsRongyu == 1 && IsPhysicalInteger(model, target, name));

    /// <summary>目标候选和来源筛选共用同一个名称终点类型。</summary>
    internal static string NameType(ColumnModel name) => name.Type switch
    {
        "int?" => "int32",
        "long?" => "int64",
        "string?" => "string",
        _ => throw new InvalidOperationException("关系名称不是已接通的标量类型。")
    };

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
        return !NonRedundantReadCompilation.Physical(model, target, display) || IsIntegerName(model, owner, target, display);
    }
    /// <summary>type=1 单选独立证明目标；字符串和整数都必须落到声明一致的非凭据标量。</summary>
    internal static void RequireSelection(GenerationModel model,TableModel owner,TableModel target,PageModel page,ColumnModel name)
    {
        if(!SupportedOwner(model,owner)||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"&&!IsIntegerName(model,owner,target,name)||name.IsJsonArray||!page.Source.Extra.TryGetValue("type",out var kind)||kind.ToString()!="1"||ExactDataManageCompilation.Declared(page)||NonRedundantCrossParentCompilation.Enabled(model,target,page))throw new InvalidOperationException("派生单选名称要求普通R0或已发布R1物理来源、普通R0目标及原type=1固定单值页面。");
        if(page.Scopes.Select(scope=>scope.Column).Concat(page.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)))throw new InvalidOperationException("派生单选名称的目标身份、父及条件必须为物理标量。");
        if (target.IsGlobal)
        {
            if (name.Type == "string?") GlobalCrossTableTarget.RequireSingleName(model.Catalog, target.Source, page.Source, name.Source);
            else GlobalCrossTableTarget.RequireSingle(model.Catalog, target.Source, page.Source);
        }
        // 全局候选按当前名称读取，不借用仅允许int64路径的精确管理匹配用途。
        var plan=target.IsGlobal?NonRedundantReads.Compile(model.Catalog,target.Source.Id,[name.Source.ColumnName]):NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,name.Source.ColumnName);
        if (plan.OutputTypes[name.Source.ColumnName] != NameType(name))
            throw new InvalidOperationException("派生单选名称未落到与声明一致的固定标量。");
    }
    /// <summary>普通目标及父条件必须完整证明，暂不把逻辑跨表父或平台固定归属当作普通物理范围。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,PageModel page,RelationModel relation)
    {
        var target=relation.Target;var name=relation.NameColumn;
        if (relation.SourceColumn.Source.ControlKind == "SelectPage" && Declared(model, relation))
            RequireSelection(model, owner, target, relation.TargetPage, name);
        var integerName = IsIntegerName(model, owner, target, name);
        var physicalInteger = IsPhysicalInteger(model, target, name);
        if(!(integerName && owner.IsGlobal) && !SupportedOwner(model,owner) ||
            relation.SourceColumn.Source.ControlKind is not("List" or "SelectPage")||
            target.Source.IsRongyu!=0&&!physicalInteger||target.Source.IsFunctionTable!=0||target.Source.IsView!=0||
            name.Type != "string?" && !integerName || name.IsJsonArray || relation.ParentBinding?.Declared==true||ExactDataManageCompilation.Declared(relation.TargetPage)||NonRedundantCrossParentCompilation.Enabled(model,target,relation.TargetPage))
            throw new InvalidOperationException("固定名称关系要求已发布来源、物理整数或已证明的派生目标及候选范围。");
        if (integerName && ForeignValidationRenderer.Target(model, relation.SourceColumn)?.Source.Id != target.Source.Id)
            throw new InvalidOperationException("整数名称候选必须与来源外键使用同一固定目标。");
        if(target.IsGlobal)
        {
            var proof=owner.Source.IsRongyu==1?GlobalSingleRelationTarget.RequireCached(model.Catalog,owner.Source,relation.SourceColumn.Source,target.Source):GlobalSingleRelationTarget.Require(model.Catalog,owner.Source,relation.SourceColumn.Source,target.Source);
            if(proof.Page.Id!=relation.TargetPage.Source.Id||proof.Name.Id!=name.Source.Id)throw new InvalidOperationException("全局派生候选与固定名称或目标页不一致。");
        }
        if(owner.Source.IsRongyu==1&&(!NonRedundantReadCompilation.Physical(model,owner,relation.SourceColumn)||GenerationModel.Enabled(relation.SourceColumn.Source.Extra,"isJoinColumn")||relation.SourceColumn.Source.IsVirtualColumn!=0||relation.SourceColumn.Source.LogicalType is not("int64" or "string")))throw new InvalidOperationException("R1派生候选必须使用本表真实单值外键。");
        if (integerName && owner.Source.IsRongyu == 1)
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
                throw new InvalidOperationException("R1 整数名称要求同类型的只读物理缓存及完整写入、传播和维护计划。");
        }
        var fields=relation.TargetPage.Scopes.Concat(relation.Scopes).Select(scope=>scope.Column)
            .Concat(relation.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[])
            .Concat(relation.Conditions?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>!NonRedundantReadCompilation.Physical(model,target,column)||column.IsJsonArray))throw new InvalidOperationException("派生名称候选的身份、父及附加范围必须为已发布物理标量。");
        var orderFields = relation.OptionsOrder?.Select(item => item.Column.Source.ColumnName) ?? [];
        var dependencies = fields.Select(column => column.Source.ColumnName).Concat(orderFields)
            .Concat(["id", name.Source.ColumnName]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputs = new[] { "id", name.Source.ColumnName };
        var plan = target.IsGlobal && physicalInteger || NonRedundantReadCompilation.HasGlobalRowProject(target)
            ? NonRedundantReads.CompileGlobalCandidate(model.Catalog, target.Source.Id, relation.TargetPage.Source.Id, outputs, dependencies)
            : physicalInteger && target.Source.IsRongyu == 1
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
