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
        if(source.Source.ControlKind is not("List" or "SelectPage"))return false;
        var target=ForeignValidationRenderer.Target(model,source);if(target is null)return false;
        var name="name";
        if(!string.IsNullOrWhiteSpace(source.Source.ControlDetail)){using var json=JsonDocument.Parse(source.Source.ControlDetail);if(json.RootElement.TryGetProperty("nameField",out var field)&&field.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(field.GetString()))name=field.GetString()!;}
        var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==name);
        return display is not null&&!NonRedundantReadCompilation.Physical(model,target,display);
    }
    /// <summary>type=1单选先独立证明目标，不反向调用关系编译；名称终点必须是非凭据单值字符串。</summary>
    internal static void RequireSelection(GenerationModel model,TableModel owner,TableModel target,PageModel page,ColumnModel name)
    {
        if(!SupportedOwner(model,owner)||target.Source.IsRongyu!=0||target.IsGlobal||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"||name.IsJsonArray||!page.Source.Extra.TryGetValue("type",out var kind)||kind.ToString()!="1"||ExactDataManageCompilation.Declared(page)||NonRedundantCrossParentCompilation.Enabled(model,target,page))throw new InvalidOperationException("派生单选名称要求普通R0或已发布R1物理来源、普通R0目标及原type=1固定单值页面。");
        if(page.Scopes.Select(scope=>scope.Column).Concat(page.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)))throw new InvalidOperationException("派生单选名称的目标身份、父及条件必须为物理标量。");
        var plan=NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,name.Source.ColumnName);
        if(plan.OutputTypes[name.Source.ColumnName]!="string")throw new InvalidOperationException("派生单选名称未落到固定字符串终点。");
    }
    /// <summary>普通目标及父条件必须完整证明，暂不把逻辑跨表父或平台固定归属当作普通物理范围。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,PageModel page,RelationModel relation)
    {
        var target=relation.Target;var name=relation.NameColumn;
        if(relation.SourceColumn.Source.ControlKind=="SelectPage")RequireSelection(model,owner,target,relation.TargetPage,name);
        if(!SupportedOwner(model,owner)||relation.SourceColumn.Source.ControlKind is not("List" or "SelectPage")||target.Source.IsRongyu!=0||target.IsGlobal||target.Source.IsFunctionTable!=0||target.Source.IsView!=0||name.Type!="string?"||name.IsJsonArray||relation.ParentBinding?.Declared==true||ExactDataManageCompilation.Declared(relation.TargetPage)||NonRedundantCrossParentCompilation.Enabled(model,target,relation.TargetPage))throw new InvalidOperationException("派生名称关系要求普通R0或已发布R1物理来源、普通R0目标及已证明的候选范围。");
        if(owner.Source.IsRongyu==1&&(!NonRedundantReadCompilation.Physical(model,owner,relation.SourceColumn)||GenerationModel.Enabled(relation.SourceColumn.Source.Extra,"isJoinColumn")||relation.SourceColumn.Source.IsVirtualColumn!=0||relation.SourceColumn.Source.LogicalType is not("int64" or "string")))throw new InvalidOperationException("R1派生候选必须使用本表真实单值外键。");
        var fields=relation.TargetPage.Scopes.Concat(relation.Scopes).Select(scope=>scope.Column)
            .Concat(relation.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[])
            .Concat(relation.Conditions?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>!NonRedundantReadCompilation.Physical(model,target,column)||column.IsJsonArray))throw new InvalidOperationException("派生名称候选的身份、父及附加范围必须为已发布物理标量。");
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(new[]{"id",name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan=NonRedundantReads.Compile(model.Catalog,target.Source.Id,["id",name.Source.ColumnName],dependencies);
        if(plan.OutputTypes["id"]!="int64"||plan.OutputTypes[name.Source.ColumnName]!="string")throw new InvalidOperationException("关系名称必须可编译为固定单值字符串，不能借级联或数组冒充标量。");
        return dependencies;
    }
    /// <summary>普通R0保持原合同；R1只为明确发布、有真实主键／项目的物理消费者开放同一固定候选。</summary>
    private static bool SupportedOwner(GenerationModel model,TableModel owner)
    {
        if(owner.Source.IsRongyu==0)return true;
        return owner.Source.IsRongyu==1&&!owner.IsGlobal&&owner.Source.IsView==0&&owner.Source.IsFunctionTable==0&&owner.Source.YzProjectId==model.Catalog.YzProjectId&&owner.Columns.Any(column=>column.IsId&&column.Source.LogicalType=="int64"&&NonRedundantReadCompilation.Physical(model,owner,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))&&owner.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"&&column.Source.LogicalType is "int64" or "string"&&NonRedundantReadCompilation.Physical(model,owner,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"));
    }

}
