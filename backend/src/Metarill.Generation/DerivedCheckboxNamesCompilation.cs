using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>数据库复选派生名称在发布时固定，不替换原来源授权、父及表单范围。</summary>
internal static class DerivedCheckboxNamesCompilation
{
    /// <summary>只签发普通R0目标的单值名称，条件仍限物理标量；R1缓存同时取得完整维护证明。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,DatabaseCheckboxModel def,
        IReadOnlyList<ColumnModel>? cachedLabels=null)
    {
        if(def.Names is null)throw new InvalidOperationException("按主键显示的复选不需要派生名称计划。");
        if(owner.Source.IsRongyu is not(0 or 1)||def.Target.Source.IsRongyu!=0||def.Target.Source.IsView!=0||def.Target.Source.IsFunctionTable!=0||def.Name.Type!="string?"&&!Metarill.Core.Contracts.MultiValueLabel.IsTyped(def.Name.Source.LogicalType)||def.Name.IsJsonArray)throw new InvalidOperationException("派生复选名称要求已证明来源与普通R0目标的固定单值名称。");
        var fields=def.Scopes.Select(scope=>scope.Column).Concat(def.Conditions?.Syntax.Fields.Select(field=>def.Target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,def.Target,column)&&
            (!def.SharedLogicalParentScope&&!def.RealtimeLogicalParentScope&&!def.CompoundTargetParents&&def.FunctionParentField is null||def.LogicalTargetPage is null||
             !NonRedundantCrossParentCompilation.Managed(model,def.Target,def.LogicalTargetPage,column))))throw new InvalidOperationException("派生复选的身份、父及附加条件必须为物理标量。");
        var pageFields=def.PhysicalTargetPage?.Condition?.Syntax.Fields??[];
        var outputs=OutputFields(def,cachedLabels);
        var dependencies=fields.Select(column=>column.Source.ColumnName)
            .Concat(def.CandidateOrderTargets is null?def.Order.Select(item=>item.Column.Source.ColumnName):[]).Concat(pageFields)
            .Concat(outputs)
            .Concat(def.ExactTarget is {} exact?[exact.Column.Source.ColumnName]:[])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan=ReadPlan(model,def,outputs,dependencies);
        if(plan.OutputTypes["id"]!="int64"||plan.OutputTypes[def.Name.Source.ColumnName]!=def.Name.Source.LogicalType||
            cachedLabels?.Any(label=>plan.OutputTypes[label.Source.ColumnName]!=label.Source.LogicalType)==true)
            throw new InvalidOperationException("复选名称与固定物理终点类型不一致。");
        // 冗余来源的每份名称数组都须进入原维护及反向传播。
        if(owner.Source.IsRongyu==1&&owner.Source.IsFunctionTable==0)
        {
            var cache=new Metarill.Data.Relations.RelationCacheDefinition(owner.Source.Id,def.Source.Source.Id,
                def.Target.Source.Id,def.Name.Source.Id,def.Names.Source.Id,true)
            {
                CheckboxTargetPageId=def.SharedLogicalParentScope||def.SharedDerivedPageScope||def.ProjectFormScope?ScopedCheckboxPage(def).Source.Id:null,
                CheckboxSourceParentId=def.SharedDerivedPageScope||def.ProjectFormScope?DatabaseCheckboxCompilation.FixedCacheParent(def):null
            };
            // 无父级的固定常量页沿用原物理传播，具有持久父级的副本统一验证实际父范围。
            if(def.SharedDerivedPageScope&&!def.SharedAccountScope&&!def.ProjectFormScope&&cache.CheckboxSourceParentId is null&&
                NonRedundantReadCompilation.Physical(model,def.Target,def.Name))
                _=Metarill.Data.Relations.RelationMutationPlan.Compile(model.Catalog,[cache]);
            else Metarill.Data.Relations.DerivedCrossTableCaches.Validate(model.Catalog,[cache],[]);
        }
        return dependencies;
    }

    /// <summary>合并候选显示字段与真实目标页的精确匹配依赖，统一验证和下发。</summary>
    private static NonRedundantReadPlan ReadPlan(GenerationModel model,DatabaseCheckboxModel def,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
    {
        var field=def.Name.Source.ColumnName;
        if(UsesScopedCheckboxLabel(model,def))
            return NonRedundantReads.CompileScopedCheckboxLabel(model.Catalog,def.Target.Source.Id,ScopedCheckboxPage(def).Source.Id,
                field,outputs,dependencies,def.ExactTarget is not null,ScopedNameTargets(model,def,outputs));
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
    internal static string PlanCall(GenerationModel model,DatabaseCheckboxModel def,IReadOnlyList<string> dependencies,
        IReadOnlyList<ColumnModel> cachedLabels)
    {
        var field=def.Name.Source.ColumnName;
        var outputs=OutputFields(def,cachedLabels);
        static string Fields(IEnumerable<string> names)=>"new string[]{"+string.Join(",",names.Select(name=>JsonSerializer.Serialize(name)))+"}";
        if(UsesScopedCheckboxLabel(model,def))
            return "CompileScopedCheckboxLabel("+JsonSerializer.Serialize(def.Target.Source.Id)+","+
                JsonSerializer.Serialize(ScopedCheckboxPage(def).Source.Id)+","+JsonSerializer.Serialize(field)+","+
                Fields(outputs)+","+Fields(dependencies)+","+(def.ExactTarget is null?"false":"true")+
                NonRedundantReadCompilation.SingleTargetArgument(ScopedNameTargets(model,def,outputs))+")";
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

    /// <summary>候选显示字段与写入需要的各名称共用同一投影。</summary>
    private static string[] OutputFields(DatabaseCheckboxModel def,IReadOnlyList<ColumnModel>? cachedLabels)=>
        new[]{"id",def.Name.Source.ColumnName}.Concat(cachedLabels?.Select(label=>label.Source.ColumnName)??[])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>全局复选只沿明确绑定的精确页编译，角色值仍由请求绑定。</summary>
    internal static bool UsesGlobalExactPage(DatabaseCheckboxModel def)=>
        def.ExactTarget is not null&&def.NameTargetPage is {} page&&
        Metarill.Core.Metadata.GlobalCrossTableTarget.DeclaresExactPage(def.Target.Source,page.Source);

    /// <summary>复选独立父字段只对应当前候选页已证明的逻辑层位，不依赖页面属于哪种选择控件。</summary>
    internal static bool HasExactLogicalParent(GenerationModel model,DatabaseCheckboxModel def)=>
        def.ExactTarget is not null&&!UsesGlobalExactPage(def)&&def.LogicalTargetPage is {} page&&
        def.Scopes.Any(scope=>scope.Source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,def.Target,page,scope.Column));

    /// <summary>实际复选页承担多父条件，也承担单物理全局父叠加精确匹配的范围。</summary>
    internal static bool UsesScopedCheckboxLabel(GenerationModel model,DatabaseCheckboxModel def)=>
        !UsesGlobalExactPage(def)&&(def.GlobalRealtimeNameOverride||def.SharedDerivedPageScope||def.SharedLogicalParentScope||def.ProjectFormScope||
            def.RealtimeLogicalParentScope||def.CompoundTargetParents||
            def.LogicalTargetPage?.Scopes.Any(scope=>scope.Source=="parent"&&
                Metarill.Core.Metadata.MultiTargetParentScope.IsCheckboxOnlyParentField(scope.Column.Source))==true||
            def.GlobalParent&&def.LogicalTargetPage is not null&&def.ExactTarget is not null&&!HasExactLogicalParent(model,def));

    /// <summary>全局常量字典与逻辑范围共用实际发布页，始终从复选定义取页。</summary>
    private static PageModel ScopedCheckboxPage(DatabaseCheckboxModel def)=>def.LogicalTargetPage??def.NameTargetPage!;

    /// <summary>固定逻辑成员计划只解析字段，页面字面条件和当前账号在调用时显式附加。</summary>
    internal static PageModel? ExactMembershipPage(GenerationModel model,DatabaseCheckboxModel def)=>
        !UsesScopedCheckboxLabel(model,def)&&def.ExactTarget is {} exact&&!UsesGlobalExactPage(def)&&!HasExactLogicalParent(model,def)
            ?def.LogicalTargetPage??(exact.SelfTree&&def.DerivedName?def.NameTargetPage:null):null;

    /// <summary>固定范围和精确字段沿用同一实际目标页的联接。</summary>
    private static IReadOnlyList<NonRedundantSingleTargetScope> ScopedNameTargets(GenerationModel model,DatabaseCheckboxModel def,
        IReadOnlyList<string> outputs)=>
        def.ExactTarget is {} exact?ExactNameTargets(model,def,exact):
            NonRedundantReadCompilation.ProjectPathTargets(model,def.Target,ScopedCheckboxPage(def),outputs,multiValuePath:true);

    /// <summary>授权和名称共用已发布的中间关系，冲突的绑定在生成时拒绝。</summary>
    private static IReadOnlyList<NonRedundantSingleTargetScope> ExactNameTargets(GenerationModel model,
        DatabaseCheckboxModel def,ExactDataManageModel exact)=>
        NonRedundantReadCompilation.MergeProjectTargets(NonRedundantReadCompilation.ProjectPathTargets(model,def.Target,
            def.NameTargetPage!,[def.Name.Source.ColumnName],multiValuePath:true).Concat(exact.SingleTargets));
}
