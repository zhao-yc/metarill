using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>数据库复选派生名称在发布时固定，不替换原来源授权、父及表单范围。</summary>
internal static class DerivedCheckboxNamesCompilation
{
    /// <summary>只签发普通R0目标的单值名称，条件仍限物理标量；R1缓存同时取得完整维护证明。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,DatabaseCheckboxModel def)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||def.Target.Source.IsRongyu!=0||def.Target.Source.IsView!=0||def.Target.Source.IsFunctionTable!=0||def.Name.Type!="string?"&&!Metarill.Core.Contracts.MultiValueLabel.IsTyped(def.Name.Source.LogicalType)||def.Name.IsJsonArray)throw new InvalidOperationException("派生复选名称要求已证明来源与普通R0目标的固定单值名称。");
        var fields=def.Scopes.Select(scope=>scope.Column).Concat(def.Conditions?.Syntax.Fields.Select(field=>def.Target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,def.Target,column)&&
            (def.FunctionParentField is null||def.LogicalTargetPage is null||
             !NonRedundantCrossParentCompilation.Managed(model,def.Target,def.LogicalTargetPage,column))))throw new InvalidOperationException("派生复选的身份、父及附加条件必须为物理标量。");
        var pageFields=def.PhysicalTargetPage?.Condition?.Syntax.Fields??[];
        var dependencies=fields.Select(column=>column.Source.ColumnName)
            .Concat(def.Order.Select(item=>item.Column.Source.ColumnName)).Concat(pageFields)
            .Concat(new[]{"id",def.Name.Source.ColumnName})
            .Concat(def.ExactTarget is {} exact?[exact.Column.Source.ColumnName]:[])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan=ReadPlan(model,def,dependencies);
        if(plan.OutputTypes["id"]!="int64"||plan.OutputTypes[def.Name.Source.ColumnName]!=def.Name.Source.LogicalType)
            throw new InvalidOperationException("复选名称与固定物理终点类型不一致。");
        // R1不能只开放候选和写入：同一名称数组必须进入统一反向传播及原维护。
        if(owner.Source.IsRongyu==1&&owner.Source.IsFunctionTable==0)Metarill.Data.Relations.DerivedCrossTableCaches.Validate(model.Catalog,[new(owner.Source.Id,def.Source.Source.Id,def.Target.Source.Id,def.Name.Source.Id,def.Names.Source.Id,true)],[]);
        return dependencies;
    }

    /// <summary>合并候选显示字段与真实目标页的精确匹配依赖，统一验证和下发。</summary>
    private static NonRedundantReadPlan ReadPlan(GenerationModel model,DatabaseCheckboxModel def,IReadOnlyList<string> dependencies)
    {
        var field=def.Name.Source.ColumnName;
        string[] outputs=["id",field];
        if(UsesGlobalExactPage(def))
        {
            var published=def.NameTargetPage!;
            return NonRedundantReads.CompileGlobalExactSingleCandidate(model.Catalog,def.Target.Source.Id,published.Source.Id,
                outputs,dependencies,ExactNameTargets(model,def,def.ExactTarget!),null,[field]);
        }
        if(def.ExactTarget is not {} exact||!exact.Derived&&def.LogicalTargetPage is null)
            return NonRedundantReadCompilation.CompileMultiLabelPlan(model,def.Target,def.LogicalTargetPage??def.GlobalPage,
                def.NameTargetPage,field,outputs,dependencies);

        var page=def.NameTargetPage!;
        var targets=ExactNameTargets(model,def,exact);
        var bindings=NonRedundantCrossTableCompilation.Bindings(exact.CrossPlans??[]);
        if(HasExactLogicalParent(model,def))
            return NonRedundantReads.CompileExactCheckboxParentCandidate(model.Catalog,def.Target.Source.Id,page.Source.Id,
                outputs,dependencies,bindings,targets);
        if(def.LogicalTargetPage is not null||exact.SelfTree)
            return DerivedRelationNamesCompilation.CompileLogicalPage(model,def.Target,page,outputs,dependencies,
                singleTargets:targets,exact:exact);
        return NonRedundantReadCompilation.CompilePage(model,def.Target,outputs,dependencies,bindings,targets,[]);
    }

    /// <summary>实际生成静态计划与发布验证使用相同字段、关联和页面范围。</summary>
    internal static string PlanCall(GenerationModel model,DatabaseCheckboxModel def,IReadOnlyList<string> dependencies)
    {
        var field=def.Name.Source.ColumnName;
        static string Fields(IEnumerable<string> names)=>"new string[]{"+string.Join(",",names.Select(name=>JsonSerializer.Serialize(name)))+"}";
        if(UsesGlobalExactPage(def))
        {
            var published=def.NameTargetPage!;
            return "CompileGlobalExactSingleCandidate("+JsonSerializer.Serialize(def.Target.Source.Id)+","+
                JsonSerializer.Serialize(published.Source.Id)+","+Fields(["id",field])+","+Fields(dependencies)+
                NonRedundantReadCompilation.SingleTargetArgument(ExactNameTargets(model,def,def.ExactTarget!),"singleTargets")+
                ",listFields:"+Fields([field])+")";
        }
        if(def.ExactTarget is not {} exact||!exact.Derived&&def.LogicalTargetPage is null)
            return NonRedundantReadCompilation.MultiLabelPlanCall(model,def.Target,def.LogicalTargetPage??def.GlobalPage,
                def.NameTargetPage,def.Name,["id",field],dependencies);
        var page=def.NameTargetPage!;
        var targets=ExactNameTargets(model,def,exact);
        var args=Fields(["id",field])+","+Fields(dependencies);
        var crossBindings=NonRedundantCrossTableCompilation.BindingArgument(exact.CrossPlans??[]);
        var cross=crossBindings.Length==0?",Array.Empty<NonRedundantCrossTableScope>()":crossBindings;
        if(HasExactLogicalParent(model,def))
            return "CompileExactCheckboxParentCandidate("+JsonSerializer.Serialize(def.Target.Source.Id)+","+
                JsonSerializer.Serialize(page.Source.Id)+","+args+cross+NonRedundantReadCompilation.SingleTargetArgument(targets)+")";
        if(def.LogicalTargetPage is not null||exact.SelfTree)
            return DerivedRelationNamesCompilation.LogicalPagePlanStart(model,def.Target,page,exact:exact)+","+args+
                NonRedundantReadCompilation.SingleTargetArgument(targets,"singleTargets")+")";
        if(targets.Count==0&&crossBindings.Length==0)
            return "Compile("+JsonSerializer.Serialize(def.Target.Source.Id)+","+args+")";
        var start=targets.Count==0?"CompileCross":"CompileProjectPage";
        return start+"("+JsonSerializer.Serialize(def.Target.Source.Id)+","+args+cross+
            NonRedundantReadCompilation.SingleTargetArgument(targets)+")";
    }

    /// <summary>全局复选只沿明确绑定的精确页编译，角色值仍由请求绑定。</summary>
    internal static bool UsesGlobalExactPage(DatabaseCheckboxModel def)=>
        def.ExactTarget is not null&&def.NameTargetPage is {} page&&
        Metarill.Core.Metadata.GlobalCrossTableTarget.DeclaresExactPage(def.Target.Source,page.Source);

    /// <summary>复选独立父字段只对应当前候选页已证明的逻辑层位，不依赖页面属于哪种选择控件。</summary>
    internal static bool HasExactLogicalParent(GenerationModel model,DatabaseCheckboxModel def)=>
        def.ExactTarget is not null&&!UsesGlobalExactPage(def)&&def.LogicalTargetPage is {} page&&
        def.Scopes.Any(scope=>scope.Source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,def.Target,page,scope.Column));

    /// <summary>固定逻辑成员计划只解析字段，页面字面条件和当前账号在调用时显式附加。</summary>
    internal static PageModel? ExactMembershipPage(GenerationModel model,DatabaseCheckboxModel def)=>
        def.ExactTarget is {} exact&&!UsesGlobalExactPage(def)&&!HasExactLogicalParent(model,def)
            ?def.LogicalTargetPage??(exact.SelfTree&&def.DerivedName?def.NameTargetPage:null):null;

    /// <summary>授权和名称共用已发布的中间关系，冲突的绑定在生成时拒绝。</summary>
    private static IReadOnlyList<NonRedundantSingleTargetScope> ExactNameTargets(GenerationModel model,
        DatabaseCheckboxModel def,ExactDataManageModel exact)=>
        NonRedundantReadCompilation.MergeProjectTargets(NonRedundantReadCompilation.ProjectPathTargets(model,def.Target,
            def.NameTargetPage!,[def.Name.Source.ColumnName]).Concat(exact.SingleTargets));
}
