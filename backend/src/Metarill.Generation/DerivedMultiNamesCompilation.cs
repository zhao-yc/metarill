using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>多选派生名称只在完整目标标量计划和物理范围均已证明时开放。</summary>
internal static class DerivedMultiNamesCompilation
{
    /// <summary>实际物理名称继续走原实现，不因联接标记猜测存储模式。</summary>
    internal static bool Declared(GenerationModel model,TableModel target,ColumnModel name)=>!NonRedundantReadCompilation.Physical(model,target,name);
    internal static bool UsesFixedRead(GenerationModel model,MultiSelectModel multi)=>
        (multi.SharedTextParent is null||multi.Target.Source.IsRongyu==0)&&
        (Declared(model,multi.Target,multi.Name)||FixedPageConditionCompilation.HasMultiTargetScope(model,multi.Target,multi.TargetPage));

    /// <summary>发布时编译目标名称和范围，不能退化为查询IsIgnore实体属性。</summary>
    internal static void Require(GenerationModel model,TableModel owner,TableModel target,PageModel page,ColumnModel name,
        ExactDataManageModel? exact=null)
    {
        var scopedPhysicalTarget=target.Source.IsRongyu==1&&!target.IsGlobal&&
            NonRedundantReads.IsProjectFixedMultiSelectTarget(model.Catalog,target.Source,page.Source);
        if(owner.Source.IsRongyu is not(0 or 1)||target.Source.IsRongyu!=0&&!scopedPhysicalTarget&&
            !FixedPageConditionCompilation.HasStoredGlobalMultiScope(target,page)||
            target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"&&
            !Metarill.Core.Contracts.MultiValueLabel.IsTyped(name.Source.LogicalType)||name.IsJsonArray)
            throw new InvalidOperationException("多选名称要求已证明的来源、目标页面及固定单值名称。");
        var condition=page.Condition;
        if(page.Scopes.Select(scope=>scope.Column).Concat(condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[])
            .Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)&&
                condition?.CrossFields.Contains(column.Source.ColumnName)!=true&&condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true&&
                !NonRedundantCrossParentCompilation.Managed(model,target,page,column)))
            throw new InvalidOperationException("派生多选名称的候选身份及条件缺少物理或逻辑路径证明。");
        // 全局名称路径已由多值固定目标证明，字符串键读取不能误套精确管理的int64专用门槛。
        var field=name.Source.ColumnName;
        var plan=exact is null
            ?NonRedundantReadCompilation.CompileMultiLabelPlan(model,target,page,page,field,[field],[])
            :NonRedundantReads.CompileExactMultiLabel(model.Catalog,target.Source.Id,page.Source.Id,field,[field],[],
                ExactTargets(model,target,page,name,exact));
        if(plan.OutputTypes[name.Source.ColumnName]!=name.Source.LogicalType)throw new InvalidOperationException("派生多选名称与固定物理终点类型不一致。");
    }
    /// <summary>附加逻辑字段和显示名一同按实际候选页编译，先核对完整投影再允许包发布。</summary>
    internal static void ValidateLogicalImport(GenerationModel model,TableModel target,PageModel page,ColumnModel name,
        RelationConditionModel condition,ExactDataManageModel? exact)
    {
        var field=name.Source.ColumnName;
        _=exact is null
            ?NonRedundantReads.CompileMultiImportLabel(model.Catalog,target.Source.Id,page.Source.Id,field,
                condition.Syntax.Fields,NameTargets(model,target,page,name,condition))
            :NonRedundantReads.CompileExactMultiLabel(model.Catalog,target.Source.Id,page.Source.Id,field,
                name.IsId?["id"]:["id",field],[],ExactTargets(model,target,page,name,exact,condition),condition.Syntax.Fields);
    }

    /// <summary>导入专用计划不进入普通多选、名称回显或保存校验。</summary>
    internal static string LogicalImportDeclaration(GenerationModel model,MultiSelectModel multi,string suffix)
    {
        if(multi.ImportCondition is not {HasLogicalFields:true} condition)return "";
        if(multi.ExactTarget is not null)
            return $"    private static readonly NonRedundantReadPlan MultiImportReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{ExactPlanCall(model,multi,multi.Name,[],condition)};\n";
        var ids=string.Join(",",new[]{multi.Target.Source.Id,multi.TargetPage.Source.Id,multi.Name.Source.ColumnName}
            .Select(value=>JsonSerializer.Serialize(value)));
        var fields="new string[]{"+string.Join(",",condition.Syntax.Fields.Select(field=>JsonSerializer.Serialize(field)))+"}";
        var targets=NonRedundantReadCompilation.SingleTargetArgument(NameTargets(model,multi.Target,multi.TargetPage,multi.Name,condition));
        return $"    private static readonly NonRedundantReadPlan MultiImportReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileMultiImportLabel({ids},{fields}{targets});\n";
    }

    /// <summary>名称及原导入依赖共用实际页面的物理字典范围，发布目录据此保留对应路径。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> NameTargets(GenerationModel model,TableModel target,PageModel page,
        ColumnModel name,RelationConditionModel? condition=null)=>
        NonRedundantReadCompilation.ProjectPathTargets(model,target,page,
            (condition?.Syntax.Fields??[]).Append(name.Source.ColumnName),multiValuePath:true);

    /// <summary>名称、精确匹配和导入附加条件共享实际页的联接绑定。</summary>
    private static IReadOnlyList<NonRedundantSingleTargetScope> ExactTargets(GenerationModel model,TableModel target,
        PageModel page,ColumnModel name,ExactDataManageModel exact,RelationConditionModel? condition=null)=>
        NonRedundantReadCompilation.MergeProjectTargets(NameTargets(model,target,page,name,condition).Concat(exact.SingleTargets));

    /// <summary>生成端只固定目标页和投影字段，权限集合由真实当前请求提供。</summary>
    internal static string ExactPlanCall(GenerationModel model,MultiSelectModel multi,ColumnModel name,
        IEnumerable<string> dependencies,RelationConditionModel? condition=null)
    {
        static string Fields(IEnumerable<string> values)=>"new string[]{"+string.Join(",",values.Select(value=>JsonSerializer.Serialize(value)))+"}";
        var exact=multi.ExactTarget??throw new InvalidOperationException("精确多选读取缺少目标页权限证明。");
        var field=name.Source.ColumnName;
        var targets=ExactTargets(model,multi.Target,multi.TargetPage,name,exact,condition);
        var additional=condition is null?"":", conditionFields: "+Fields(condition.Syntax.Fields);
        return $"CompileExactMultiLabel({JsonSerializer.Serialize(multi.Target.Source.Id)},{JsonSerializer.Serialize(multi.TargetPage.Source.Id)},"+
            $"{JsonSerializer.Serialize(field)},{Fields(name.IsId?["id"]:["id",field])},{Fields(dependencies)}"+
            NonRedundantReadCompilation.SingleTargetArgument(targets)+additional+")";
    }

    /// <summary>原Excel按固定名称精确反查，读计划仅静态初始化时编译。</summary>
    internal static string Declaration(GenerationModel model,PageModel page,MultiSelectModel multi)
    {
        if(!UsesFixedRead(model,multi))return "";
        var target=multi.Target;
        var targetPage=multi.TargetPage;
        var dependencies=targetPage.Scopes.Select(scope=>scope.Column.Source.ColumnName)
            .Concat(FixedPageConditionCompilation.HasMultiTargetScope(model,target,targetPage)?[]:targetPage.Condition?.Syntax.Fields??[])
            .Concat(ImportRenderer.Enabled(page)&&multi.ImportCondition is {HasLogicalFields:false} condition?condition.Syntax.Fields:[])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        string[] outputs=multi.Name.IsId?["id"]:["id",multi.Name.Source.ColumnName];
        var call=multi.ExactTarget is null
            ?NonRedundantReadCompilation.MultiLabelPlanCall(model,target,targetPage,targetPage,multi.Name,outputs,dependencies)
            :ExactPlanCall(model,multi,multi.Name,dependencies);
        return "    /// <summary>多选导入名称的固定目标计划，普通请求只绑定值。</summary>\n"+
            $"    private static readonly NonRedundantReadPlan MultiDerivedPlan{page.Source.Id}_{multi.Source.Source.Id}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{call};\n";
    }
}
