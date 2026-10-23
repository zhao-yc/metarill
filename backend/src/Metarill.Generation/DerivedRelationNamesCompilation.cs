using System.Text.Json;
using Metarill.Data.Queries;
namespace Metarill.Generation;

/// <summary>派生候选名称固定在目标R0的标量读计划中，不能用IsIgnore属性生成物理SQL。</summary>
internal static class DerivedRelationNamesCompilation
{
    /// <summary>冗余物理缓存继续沿原路径；只识别实际非物理名称。</summary>
    internal static bool Declared(GenerationModel model,RelationModel relation)=>!NonRedundantReadCompilation.Physical(model,relation.Target,relation.NameColumn);
    /// <summary>隐藏字段也保留表级派生目标证明，仅用于内部最终值校验，不新增公开路由。</summary>
    internal static bool DeclaredSource(GenerationModel model,ColumnModel source)
    {
        if(source.Source.ControlKind!="List")return false;
        var target=ForeignValidationRenderer.Target(model,source);if(target is null)return false;
        var name="name";
        if(!string.IsNullOrWhiteSpace(source.Source.ControlDetail)){using var json=JsonDocument.Parse(source.Source.ControlDetail);if(json.RootElement.TryGetProperty("nameField",out var field)&&field.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(field.GetString()))name=field.GetString()!;}
        var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==name);
        return display is not null&&!NonRedundantReadCompilation.Physical(model,target,display);
    }
    /// <summary>普通目标及父条件必须完整证明，暂不把逻辑跨表父或平台固定归属当作普通物理范围。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,PageModel page,RelationModel relation)
    {
        var target=relation.Target;var name=relation.NameColumn;
        if(owner.Source.IsRongyu!=0||relation.SourceColumn.Source.ControlKind!="List"||target.Source.IsRongyu!=0||target.IsGlobal||target.Source.IsFunctionTable!=0||target.Source.IsView!=0||name.Type!="string?"||name.IsJsonArray||relation.ParentBinding?.Declared==true||ExactDataManageCompilation.Declared(relation.TargetPage)||NonRedundantCrossParentCompilation.Enabled(model,target,relation.TargetPage))throw new InvalidOperationException("派生名称关系要求R0来源List、普通R0目标及已证明的物理候选范围。");
        var fields=relation.TargetPage.Scopes.Concat(relation.Scopes).Select(scope=>scope.Column)
            .Concat(relation.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[])
            .Concat(relation.Conditions?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>!NonRedundantReadCompilation.Physical(model,target,column)||column.IsJsonArray))throw new InvalidOperationException("派生名称候选的身份、父及附加范围必须为已发布物理标量。");
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(new[]{"id",name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan=NonRedundantReads.Compile(model.Catalog,target.Source.Id,["id",name.Source.ColumnName],dependencies);
        if(plan.OutputTypes["id"]!="int64"||plan.OutputTypes[name.Source.ColumnName]!="string")throw new InvalidOperationException("关系名称必须可编译为固定单值字符串，不能借级联或数组冒充标量。");
        return dependencies;
    }
}
