using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>多选派生名称只在完整目标标量计划和物理范围均已证明时开放。</summary>
internal static class DerivedMultiNamesCompilation
{
    /// <summary>实际物理名称继续走原实现，不因联接标记猜测存储模式。</summary>
    internal static bool Declared(GenerationModel model,TableModel target,ColumnModel name)=>!NonRedundantReadCompilation.Physical(model,target,name);
    internal static bool UsesFixedRead(GenerationModel model,MultiSelectModel multi)=>
        Declared(model,multi.Target,multi.Name)||FixedPageConditionCompilation.HasMultiTargetScope(model,multi.Target,multi.TargetPage);

    /// <summary>发布时编译目标名称和范围，不能退化为查询IsIgnore实体属性。</summary>
    internal static void Require(GenerationModel model,TableModel owner,TableModel target,PageModel page,ColumnModel name,
        ExactDataManageModel? exact=null)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"&&!Metarill.Core.Contracts.MultiValueLabel.IsTyped(name.Source.LogicalType)||name.IsJsonArray||exact is {Derived:true})throw new InvalidOperationException("派生多选名称要求已证明来源、普通R0目标和固定单值名称。");
        var condition=page.Condition;
        if(page.Scopes.Select(scope=>scope.Column).Concat(condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[])
            .Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)&&
                condition?.CrossFields.Contains(column.Source.ColumnName)!=true&&condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true))
            throw new InvalidOperationException("派生多选名称的候选身份及条件缺少物理或逻辑路径证明。");
        // 全局名称路径已由多值固定目标证明，字符串键读取不能误套精确管理的int64专用门槛。
        var field=name.Source.ColumnName;
        var plan=NonRedundantReadCompilation.CompileMultiLabelPlan(model,target,page,page,field,[field],
            exact is null?[]:[exact.Column.Source.ColumnName]);
        if(plan.OutputTypes[name.Source.ColumnName]!=name.Source.LogicalType)throw new InvalidOperationException("派生多选名称与固定物理终点类型不一致。");
    }
    /// <summary>附加逻辑字段和显示名一同按实际候选页编译，先核对完整投影再允许包发布。</summary>
    internal static void ValidateLogicalImport(GenerationModel model,TableModel target,PageModel page,ColumnModel name,
        RelationConditionModel condition)
    {
        _=NonRedundantReads.CompileMultiImportLabel(model.Catalog,target.Source.Id,page.Source.Id,name.Source.ColumnName,
            condition.Syntax.Fields,ImportTargets(model,target,page,name,condition));
    }

    /// <summary>导入专用计划不进入普通多选、名称回显或保存校验。</summary>
    internal static string LogicalImportDeclaration(GenerationModel model,MultiSelectModel multi,string suffix)
    {
        if(multi.ImportCondition is not {HasLogicalFields:true} condition)return "";
        var ids=string.Join(",",new[]{multi.Target.Source.Id,multi.TargetPage.Source.Id,multi.Name.Source.ColumnName}
            .Select(value=>JsonSerializer.Serialize(value)));
        var fields="new string[]{"+string.Join(",",condition.Syntax.Fields.Select(field=>JsonSerializer.Serialize(field)))+"}";
        var targets=NonRedundantReadCompilation.SingleTargetArgument(ImportTargets(model,multi.Target,multi.TargetPage,multi.Name,condition));
        return $"    private static readonly NonRedundantReadPlan MultiImportReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileMultiImportLabel({ids},{fields}{targets});\n";
    }

    private static IReadOnlyList<NonRedundantSingleTargetScope> ImportTargets(GenerationModel model,TableModel target,PageModel page,
        ColumnModel name,RelationConditionModel condition)=>
        NonRedundantReadCompilation.ProjectPathTargets(model,target,page,condition.Syntax.Fields.Append(name.Source.ColumnName));

    /// <summary>原Excel按固定名称精确反查，读计划仅静态初始化时编译。</summary>
    internal static string Declaration(GenerationModel model,PageModel page,MultiSelectModel multi)
    {
        if(!UsesFixedRead(model,multi))return "";
        var target=multi.Target;
        var targetPage=multi.TargetPage;
        var dependencies=targetPage.Scopes.Select(scope=>scope.Column.Source.ColumnName)
            .Concat(FixedPageConditionCompilation.HasMultiTargetScope(model,target,targetPage)?[]:targetPage.Condition?.Syntax.Fields??[])
            .Concat(ImportRenderer.Enabled(page)&&multi.ImportCondition is {HasLogicalFields:false} condition?condition.Syntax.Fields:[])
            .Concat(multi.ExactTarget is {} exact?[exact.Column.Source.ColumnName]:[])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        string[] outputs=multi.Name.IsId?["id"]:["id",multi.Name.Source.ColumnName];
        var call=NonRedundantReadCompilation.MultiLabelPlanCall(model,target,targetPage,targetPage,multi.Name,outputs,dependencies);
        return "    /// <summary>多选导入名称的固定目标计划，普通请求只绑定值。</summary>\n"+
            $"    private static readonly NonRedundantReadPlan MultiDerivedPlan{page.Source.Id}_{multi.Source.Source.Id}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{call};\n";
    }
}
