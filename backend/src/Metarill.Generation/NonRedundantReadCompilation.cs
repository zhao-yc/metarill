using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record NonRedundantOutput(string Storage,string Property,string JsonName,string Type,bool IsJsonArray=false);
internal sealed record NonRedundantPagePlan(IReadOnlyList<NonRedundantOutput> Outputs,IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> ReadOutputs,IReadOnlyList<NonRedundantSingleTargetScope> SingleTargets,
    IReadOnlyList<NonRedundantMultiTargetScope> MultiTargets,bool RequiresAccount);

/// <summary>生成侧提前证明单值／固定多选用途和物理写边界；数据库表达式由数据适配器编译。</summary>
internal static partial class NonRedundantReadCompilation
{
    /// <summary>声明全局的R0表仍可拥有实际行归属，两种用途必须分别生成。</summary>
    internal static bool HasGlobalRowProject(TableModel table)=>table.IsGlobal&&table.Source.IsRongyu==0&&table.Columns.Any(column=>column.Source.ColumnName=="yz_project_id");

    /// <summary>有行归属的全局选择页及独立物理父多选使用固定全局候选。</summary>
    internal static bool GlobalSelectionPage(TableModel table,PageModel page)=>
        HasGlobalRowProject(table)&&page.Source.Extra.GetValueOrDefault("type").ToString() is "1" or "2"||
        GlobalAccountMultiPage(table,page)||GlobalSingleParentPage(table,page)||
        table.Source.IsRongyu==0&&GlobalCrossTableTarget.DeclaresExactPage(table.Source,page.Source);

    /// <summary>原独立单选在分页和详情中使用已证明的父级及实际发布条件。</summary>
    internal static bool GlobalSingleParentPage(TableModel table,PageModel page)=>
        page.Source.Extra.GetValueOrDefault("type").ToString()=="1"&&table.IsGlobal&&table.Source.IsRongyu==0&&
        (page.Scopes.Any(scope=>scope.Source=="parent"&&CrossTableStorageColumns.IsLogicalPathField(
            table.Source,table.Columns.Select(column=>column.Source),scope.Column.Source.ColumnName))||
            GlobalPhysicalParentScope(table,page));

    /// <summary>全局多选的物理父范围和同父级联范围共用独立分页的固定候选计划。</summary>
    internal static bool GlobalAccountMultiPage(TableModel table,PageModel page)=>
        page.Source.Extra.GetValueOrDefault("type").ToString()=="2"&&
        (GlobalAccountParentScope(table,page)||table.IsGlobal&&table.Source.IsRongyu==0&&
            page.Scopes.Count(scope=>scope.Source=="parent")>1&&page.Scopes.Any(scope=>scope.Source=="parent"&&
                CrossTableStorageColumns.IsLogicalPathField(table.Source,table.Columns.Select(column=>column.Source),scope.Column.Source.ColumnName)));

    /// <summary>物理父范围由实际发布页决定，可以独立使用或叠加账号条件。</summary>
    private static bool GlobalAccountParentScope(TableModel table,PageModel page)=>
        GlobalPhysicalParentScope(table,page)&&
        (FixedPageConditionCompilation.HasAccountVariables(page)||page.Scopes.All(scope=>scope.Source is "parent" or "project"))&&
        !FixedPageConditionCompilation.HasLogicalFields(page);

    /// <summary>只识别页级实际物理父字段，稀疏逻辑父继续沿原级联证明。</summary>
    private static bool GlobalPhysicalParentScope(TableModel table,PageModel page)=>
        table.IsGlobal&&table.Source.IsRongyu==0&&page.Scopes.Any(scope=>scope.Source=="parent"&&
            !CrossTableStorageColumns.IsLogicalPathField(table.Source,table.Columns.Select(column=>column.Source),scope.Column.Source.ColumnName));

    /// <summary>候选、已选和Excel从同一发布页取得目标范围。</summary>
    internal static string RelationPlanStart(TableModel table,PageModel? page)=>
        HasGlobalRowProject(table)&&page is not null
            ?"CompileGlobalCandidate("+JsonSerializer.Serialize(table.Source.Id)+","+JsonSerializer.Serialize(page.Source.Id)
            :"CompileRelation("+JsonSerializer.Serialize(table.Source.Id);

    /// <summary>多值候选和 Excel 名称匹配从实际发布页沿单值路径收集范围。</summary>
    internal static string MultiLabelPlanCall(GenerationModel model,TableModel table,PageModel? scopePage,PageModel? namePage,
        ColumnModel name,IEnumerable<string> outputs,IEnumerable<string> dependencies)
    {
        static string Fields(IEnumerable<string> values)=>"new string[]{"+string.Join(",",values.Select(value=>JsonSerializer.Serialize(value)))+"}";
        var field=name.Source.ColumnName;
        var start=scopePage is not null&&FixedPageConditionCompilation.HasMultiTargetScope(model,table,scopePage)
            ?"CompileScopedMultiLabel("+JsonSerializer.Serialize(table.Source.Id)+","+JsonSerializer.Serialize(scopePage.Source.Id)+","+JsonSerializer.Serialize(field)
            :table.IsGlobal?RelationPlanStart(table,scopePage):"CompileMultiLabel("+JsonSerializer.Serialize(table.Source.Id)+","+JsonSerializer.Serialize(field);
        return start+","+Fields(outputs)+","+Fields(dependencies)+
            SingleTargetArgument(MultiLabelTargets(model,table,namePage,field))+")";
    }

    /// <summary>验证使用与下发代码相同的目标页、显示名称和依赖。</summary>
    internal static NonRedundantReadPlan CompileMultiLabelPlan(GenerationModel model,TableModel table,PageModel? scopePage,
        PageModel? namePage,string field,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
    {
        var id=table.Source.Id;
        var targets=MultiLabelTargets(model,table,namePage,field);
        if(scopePage is not null&&FixedPageConditionCompilation.HasMultiTargetScope(model,table,scopePage))
            return NonRedundantReads.CompileScopedMultiLabel(model.Catalog,id,scopePage.Source.Id,field,outputs,dependencies,targets);
        if(!table.IsGlobal)
            return NonRedundantReads.CompileMultiLabel(model.Catalog,id,field,outputs,dependencies,targets);
        if(scopePage is not null&&HasGlobalRowProject(table))
            return NonRedundantReads.CompileGlobalCandidate(model.Catalog,id,scopePage.Source.Id,outputs,dependencies,targets);
        return targets.Count>0?NonRedundantReads.CompileProjectRelation(model.Catalog,id,outputs,dependencies,targets)
            :NonRedundantReads.Compile(model.Catalog,id,outputs,dependencies);
    }

    private static IReadOnlyList<NonRedundantSingleTargetScope> MultiLabelTargets(GenerationModel model,TableModel table,
        PageModel? page,string field)=>page is null?[]:ProjectPathTargets(model,table,page,[field],multiValuePath:true);

    /// <summary>没有派生关系的原纯物理非冗余页继续使用既有强类型路径。</summary>
    internal static bool Requires(GenerationModel model,TableModel table)=>HasGlobalRowProject(table)||
        table.Source.IsRongyu==0&&table.Pages.Any(page=>GlobalCrossTableTarget.DeclaresExactMulti(table.Source,page.Source))||
        table.Pages.Any(page=>GlobalAccountMultiPage(table,page)||GlobalSingleParentPage(table,page)||
            table.Source.IsRongyu==0&&MultiGlobalParentCompilation.PhysicalSingleTarget(model,table,page) is not null)||table.Source.IsRongyu==0&&model.Catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Source.Id&&column.IsVirtualColumn==0&&(!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column)||MetadataStoragePolicy.IsRelationColumn(column)));
    /// <summary>所有实体写入复用同一存储计划，不把读取派生属性持久化。</summary>
    internal static bool Physical(GenerationModel model,TableModel table,ColumnModel column)=>MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column.Source);
    /// <summary>仅识别本页声明的非冗余叶名，不从请求或显示列推断精确范围。</summary>
    internal static ColumnModel? SelfTreeExactColumn(TableModel table,PageModel page)
    {
        var column=ExactDataManageCompilation.MatchColumn(table,page);
        return column is not null&&CascadeTreeStorageColumns.IsLogicalNameField(table.Source,
            table.Columns.Select(item=>item.Source),column.Source.ColumnName)?column:null;
    }

    /// <summary>固定叶名与目标页一起发布；仅显示、顺序等范围不变的页面覆盖可以继续使用表级读取定义。</summary>
    internal static IReadOnlyList<CascadeTreeReadModel> SelfTreeNameQueries(GenerationModel model, TableModel table, PageModel page)
    {
        if (table.Source.IsRongyu != 0) return [];
        var fields = page.Searchable.Concat(page.Sortable)
            .Concat(FixedPageQueryCompilation.For(table, page).DefaultOrder.Select(order => order.Column))
            .Select(column => column.Source.ColumnName).Concat(page.Condition is { } condition ? condition.SelfTreeFields : Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var exactName=SelfTreeExactColumn(table,page)?.Source.ColumnName;
        if(exactName is not null)fields.Add(exactName);
        var result = new List<CascadeTreeReadModel>();
        foreach (var tree in CascadeTreeReadCompilation.ForService(model, table, page))
        {
            if (CascadeTreeViewRenderer.Name(table, tree.Source) is not { } name || !fields.Contains(name.Source.ColumnName))
                continue;
            var baseline = CascadeTreeReadCompilation.Resolve(model, table, page, tree.Source, usePageDetail: false).Definition;
            if (baseline is null || baseline.TargetPage.Source.Id != tree.TargetPage.Source.Id ||
                baseline.Parent.Source.Id != tree.Parent.Source.Id || baseline.Name.Source.Id != tree.Name.Source.Id)
                throw new InvalidOperationException("自树名称查询必须与页面实际目标页、父字段及名称字段一致。");
            if(name.Source.ColumnName==exactName||page.Condition?.SelfTreeFields.Contains(name.Source.ColumnName)==true)
            {
                var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==tree.Source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();
                if(!CrossTableStorageColumns.KeepsDefinition(tree.Source.Source.ControlDetail,overlay))
                    throw new InvalidOperationException("自树固定页面条件必须沿用表级级联定义。");
            }
            result.Add(tree);
        }
        return result;
    }

    /// <summary>只编译已经接通的标量用途；完整包还须通过全部控件、范围及独立宿主验证。</summary>
    internal static NonRedundantPagePlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        // 固定候选约束实际发布页，普通业务页可使用自己的项目条件。
        if(GlobalCrossTableTarget.DeclaresExactPage(table.Source,page.Source))
            _=GlobalCrossTableTarget.ExactPageParent(model.Catalog,table.Source,page.Source);
        else if(HasGlobalRowProject(table))GlobalDerivedRelationSource.RequireTable(model.Catalog,table.Source);
        else if(table.IsGlobal&&GlobalCrossTableTarget.SingleParent(model.Catalog,table.Source,page.Source) is null)
        {
            if(GlobalAccountParentScope(table,page))
                GlobalCrossTableTarget.RequireMultiCandidate(model.Catalog,table.Source,page.Source);
            else if(FixedPageConditionCompilation.HasSelfTreeFields(page))
                GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(model.Catalog,table.Source,page.Source,
                    allowMultiParent:FixedPageConditionCompilation.HasMultiTargetScope(model,table,page));
            else if(table.Columns.Any(NonRedundantCrossTableSubset.Declared))
            {
                if(FixedPageConditionCompilation.HasMultiTargetScope(model,table,page)&&page.Scopes.Any(scope=>scope.Source=="parent"))
                    GlobalCrossTableTarget.RequireMultiCandidate(model.Catalog,table.Source,page.Source);
                else GlobalCrossTableTarget.RequireCrossTableCandidate(model.Catalog,table.Source,page.Source);
            }
            else GlobalCrossTableTarget.RequireAccountCandidate(model.Catalog,table.Source,page.Source);
        }
        // 导入沿用标准Service写事务，仅开放本页真实物理可写列，派生读取不参与覆盖匹配。
        if(ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>!Physical(model,table,column)))throw new InvalidOperationException("非冗余导入只能写入物理列。");
        // 物理文件ID数组沿用原FileLibrary事务。布局控件可以展示，但不能同时配置为联接字段。
        if (table.Columns.Any(column => (column.Source.ControlKind is "Title" or "Tab") && !column.IsLayoutOnly))
            throw new InvalidOperationException("非冗余标题和标签字段必须是纯布局字段。");
        // 旧媒体描述与文件库ID都来自物理JSON文本；各自的VO负责恢复数组。
        if (table.Columns.Any(column => column.IsJsonArray &&
            (column.IsLegacyMedia || column.Source.ControlKind == "FileLibrary"
                ? !Physical(model, table, column)
                : column.Source.ControlKind is not ("MultiSelectPage" or "MultiCheckBoxDatabase") && !column.IsManagedMultiNames)))
            throw new InvalidOperationException("非冗余多值或派生文件读取仍待接线。");
        if (page.Searchable.Any(column =>
            column.Source.LogicalType is not ("string" or "int64" or "int32" or "decimal" or "double" or
                "bool" or "datetime" or "date" or "time" or "binary" or "json") ||
            column.Source.LogicalType == "json" && (column.IsJsonArray || !Physical(model, table, column))))
            throw new InvalidOperationException("非冗余筛选字段类型尚未接线。JSON 文本筛选须使用普通物理列。");
        if(page.Scopes.Any(scope=>!Physical(model,table,scope.Column)))throw new InvalidOperationException("非冗余父范围必须绑定物理外键。");
        if(ManagerCredentialCompilation.IsTable(table)||FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null||FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null||FixedOptionCatalogCompilation.ForPage(model,page).Count!=0)throw new InvalidOperationException("非冗余固定安全模块及目录读取尚需独立验收。");
        foreach(var checkbox in table.Columns.Where(column=>column.Source.ControlKind=="MultiCheckBoxDatabase"))
            if(DatabaseCheckboxCompilation.Resolve(model,table,page,checkbox).Definition is null)throw new InvalidOperationException("非冗余数据库复选缺少已证明的字段与候选范围。");
        foreach(var multi in table.Columns.Where(column=>column.Source.ControlKind=="MultiSelectPage"))
            if(MultiSelectCompilation.Resolve(model,table,page,multi,forService:true).Definition is null)throw new InvalidOperationException("非冗余多选缺少已证明的字段与候选范围。");
        foreach(var tree in table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree"))
        {
            if(CrossTableWriteCompilation.Resolve(model,table,page,tree).Definition is{} cross){NonRedundantCrossTableCompilation.Require(model,table,page,cross.Read);continue;}
            if(CascadeTreeWriteCompilation.Resolve(model,table,page,tree).Definition is null)throw new InvalidOperationException("非冗余级联要求已证明的selfTree读写计划。");
        }
        var crossPlans=CrossTableWriteCompilation.ForService(model,table,page);
        var crossIds=crossPlans.SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var relation in table.Columns.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is "CascadeTree" or "MultiCheckBoxDatabase"||crossIds.Contains(relation.Source.Id))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联写入缺少已接通的明确目标。");ValidateTargetDeclarations(model,relation,target);
            if(target.IsGlobal&&page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==relation.Source.Id)&&
                MultiGlobalParentCompilation.PhysicalScopeTarget(model,table,page) is {} parentPage)
            {
                _=NonRedundantReads.MultiGlobalParentPageId(model.Catalog,parentPage.Source.ConfigTableId,parentPage.Source.Id);
                continue;
            }
            if(target.IsGlobal&&relation.Source.ControlKind is not("List" or "SelectPage")&&
                MultiGlobalParentCompilation.SingleParentBinding(model,table,page,relation) is not null)
                continue;
            if(target.IsGlobal){if(relation.Source.ControlKind=="MultiSelectPage")_=GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);else _=RelationCompilation.GlobalTargetForPage(model,table,page,relation,target);}
        }
        foreach(var selection in table.Columns.Where(column=>column.Source.ControlKind=="SelectPage"))NonRedundantSelectionCompilation.Resolve(model,table,page,selection);
        var outputs=page.Readable.Select(column=>new NonRedundantOutput(column.Source.ColumnName,column.Property,column.JsonName,column.Type,column.IsJsonArray)).ToList();
        foreach(var name in CascadeTreeViewRenderer.ImplicitNames(model,table,page))outputs.Add(new(name.Source.ColumnName,name.Property,name.JsonName,name.Type));
        foreach(var multi in MultiSelectCompilation.ForPage(model,table,page).Where(multi=>multi.Names is not null))
            if(!outputs.Any(output=>output.Storage==multi.Names!.Source.ColumnName))outputs.Add(new(multi.Names!.Source.ColumnName,multi.Names.Property,multi.Names.JsonName,"string?",true));
        foreach(var checkbox in DatabaseCheckboxCompilation.ForPage(model,table,page).Where(def=>def.Names is not null&&page.Readable.Any(field=>field.Source.Id==def.Source.Source.Id)))
            if(!outputs.Any(output=>output.Storage==checkbox.Names!.Source.ColumnName))outputs.Add(new(checkbox.Names!.Source.ColumnName,checkbox.Names.Property,checkbox.Names.JsonName,"string?",true));
        foreach(var relation in page.Readable.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is not("List" or "SelectPage"))continue;
            var pageField=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==relation.Source.Id);
            if(relation.Source.ControlKind!="SelectPage"&&pageField?.Extra.TryGetValue("controlDetail",out var detail)==true&&!string.IsNullOrWhiteSpace(detail.ToString())&&HasEffectiveOverride(relation.Source.ControlDetail,detail.ToString())&&!ProvenOptionsOverride(model,page,relation,detail.ToString()))throw new InvalidOperationException("非冗余页级关系控件覆盖尚需接线。");
            var nameField=BaselineSingleName(relation);
            if(nameField=="id"||!relation.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联目标缺少已接通的固定候选来源。");
            ValidateTargetDeclarations(model,relation,target);
            if(NonRedundantSelectionCompilation.DisplaysId(model,page,relation)||RelationCompilation.ListDisplaysId(model,page,relation))continue;
            var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField);
            if(display is null)throw new InvalidOperationException("未知名称候选仍待完整接线。");
            if(target.IsGlobal)_=RelationCompilation.GlobalTargetForPage(model,table,page,relation,target);
            if(!Physical(model,target,display))
            {
                var candidate=RelationCompilation.ForPage(model,page).SingleOrDefault(candidate=>candidate.SourceColumn.Source.Id==relation.Source.Id)??throw new InvalidOperationException("派生名称缺少固定候选关系。");
                _=DerivedRelationNamesCompilation.Require(model,table,page,candidate);
            }
            var alias=relation.Source.ColumnName[..^3]+"_"+nameField;MetadataValidation.RequireIdentifier(alias);
            if(!outputs.Any(output=>output.Storage.Equals(alias,StringComparison.OrdinalIgnoreCase)))outputs.Add(new(alias,GenerationModel.Pascal(alias),GenerationModel.Camel(alias),""));
        }
        var selectionNames=SelectionNameFilterCompilation.ForPage(model,table,page);
        var dependencies=page.Searchable.Concat(page.Sortable).Concat(ExactDataManageCompilation.Resolve(model,table,page) is {} exact ? new[]{exact.Column} : Array.Empty<ColumnModel>()).Concat(FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>scope.Column)).Concat(FixedPageQueryCompilation.For(table,page).DefaultOrder.Select(item=>item.Column)).Select(column=>column.Source.ColumnName).Concat(selectionNames.Bindings.Select(binding=>binding.StorageColumn.Source.ColumnName)).Concat(page.Condition?.Syntax.Fields??Array.Empty<string>()).Concat(CrossTableFilterCompilation.ForPage(model,table,page).SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // 跨表层名和显式开放的自树叶名都在分页前按实际路径求值；其余级联名称不能作为查询依赖。
        var queryNames=crossPlans.SelectMany(plan=>plan.Levels).Select(level=>level.Name.Source.Id)
            .Concat(SelfTreeNameQueries(model,table,page).Select(tree=>CascadeTreeViewRenderer.Name(table,tree.Source)!.Source.Id))
            .ToHashSet(StringComparer.Ordinal);
        if(CascadeTreeWriteCompilation.ManagedNames(table).Any(name=>!queryNames.Contains(name.Source.Id)&&dependencies.Contains(name.Source.ColumnName,StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("非冗余级联名称的筛选/排序/匹配用途缺少完整路径证明。");
        // 图只取来源已授权行的所选ID；隐藏ID为私有投影依赖，不加入VO或页面字段。
        var readOutputs=outputs.Select(output=>output.Storage).Concat(CascadeTreeViewRenderer.ForPage(model,table,page).Select(plan=>plan.Source.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var singleTargets=ProjectSingleTargets(model,table,page,readOutputs.Concat(dependencies));
        var crossBindings=NonRedundantCrossTableCompilation.Bindings(crossPlans);
        var multiTargets=ProjectMultiTargets(model,table,page);
        var compiled=CompilePage(model,table,readOutputs,dependencies,crossBindings,singleTargets,multiTargets);
        var account=compiled.RequiresAccount;
        if(GlobalSelectionPage(table,page))
        {
            var candidate=GlobalCrossTableTarget.DeclaresExactPage(table.Source,page.Source)
                ?NonRedundantReads.CompileGlobalExactSingleCandidate(model.Catalog,table.Source.Id,page.Source.Id,
                    readOutputs,dependencies,singleTargets,crossBindings,null)
                :GlobalAccountMultiPage(table,page)
                ?NonRedundantReads.CompileGlobalAccountMultiPage(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,crossBindings,singleTargets)
                :GlobalSingleParentPage(table,page)
                    ?NonRedundantReads.CompileGlobalSingleParentPage(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,crossBindings,singleTargets)
                :crossPlans.Count>0
                ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,crossBindings,singleTargets)
                :FixedPageConditionCompilation.HasSelfTreeFields(page)
                    ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,null,singleTargets)
                    :CascadeTreeReadCompilation.ForService(model,table,page).Count>0
                        ?NonRedundantReads.CompileGlobalPage(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,singleTargets)
                        :NonRedundantReads.CompileGlobalCandidate(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,singleTargets);
            account|=candidate.RequiresAccount;
        }
        outputs=outputs.Select(output=>output.Type.Length>0?output:output with{Type=compiled.OutputTypes[output.Storage] switch{"string"=>"string?","int64"=>"long?","int32"=>"int?","decimal"=>"decimal?","double"=>"double?","bool"=>"bool?","datetime" or "date"=>"DateTime?","time"=>"TimeSpan?",_=>throw new InvalidOperationException("自动显示字段类型尚未接线。")}}).ToList();
        if(outputs.Select(output=>output.Property).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count)throw new InvalidOperationException("派生显示属性冲突。");
        return new(outputs,dependencies,readOutputs,singleTargets,multiTargets,account);
    }

    /// <summary>业务页单值读取使用实际项目内非冗余目标以及已证明的父范围。</summary>
    internal static bool UsesProjectSingleRead(RelationModel relation)=>
        relation.ParentBinding?.Declared!=true && (relation.ScopedStoredSingle ||
            relation.Target.Source.IsRongyu==0 && (UsesProjectControlRead(relation)||!relation.Target.IsGlobal&&
                (relation.TargetPage.Condition is not null||relation.TargetPage.Scopes.Any(scope=>scope.Source=="parent"))));

    /// <summary>即使没有附加筛选，发布后的 SQL 也须沿实际来源页验证全局目标。</summary>
    internal static bool UsesPageBoundGlobalList(RelationModel relation)=>relation.Target.IsGlobal&&relation.ListPageOverride;

    /// <summary>原下拉可以仅在控件中限定目标，读取名称时也要保留该范围。</summary>
    internal static bool UsesProjectControlRead(RelationModel relation)=>
        relation.SourceColumn.Source.ControlKind=="List"&&
        (relation.Conditions is not null||relation.Scopes.Any(scope=>scope.Source=="parent"||
            relation.Target.IsGlobal&&scope.Source=="project"));

    /// <summary>同一来源表的各页分别固定实际目标页，物理父键按本页同父或表单关系证明。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> ProjectSingleTargets(GenerationModel model,TableModel table,PageModel page,
        IEnumerable<string> fields)
    {
        if(table.Source.IsRongyu!=0)return [];
        var direct=RelationCompilation.ForPage(model,page).Concat(RelationCompilation.ForPage(model,page,true))
            .DistinctBy(relation=>relation.SourceColumn.Source.Id)
            .Where(relation=>!table.IsGlobal||relation.Target.IsGlobal)
            .Select(relation=>PageTarget(model,table,page,relation))
            .OfType<NonRedundantSingleTargetScope>();
        return MergeProjectTargets(direct.Concat(ProjectPathTargets(model,table,page,fields)));
    }

    /// <summary>页级名称沿用相同的物理外键和原候选条件。纯名称变化不引入额外范围。</summary>
    private static NonRedundantSingleTargetScope? PageTarget(GenerationModel model,TableModel table,PageModel page,RelationModel relation)
    {
        var display=PageSingleDisplayOverride(model,table,relation.SourceColumn,relation.Target,relation.NameColumn);
        var scoped=UsesProjectSingleRead(relation);
        if(scoped||UsesPageBoundGlobalList(relation))
            return ProjectTarget(model,table,page,relation) with
                {ApplyPageCondition=scoped,DisplayNameColumnId=display is null?null:relation.NameColumn.Source.Id};
        return display is null?null:new(relation.SourceColumn.Source.Id,relation.TargetPage.Source.Id)
            {ApplyPageCondition=false,DisplayNameColumnId=relation.NameColumn.Source.Id};
    }

    /// <summary>按原稳定辅助属性覆盖同一目标的文本值，显式联接必须指向同一外键和表级名称。</summary>
    internal static ColumnModel? PageSingleDisplayOverride(GenerationModel model,TableModel table,ColumnModel source,TableModel target,ColumnModel name)
    {
        if(source.Source.ControlKind is not ("List" or "SelectPage")||table.Source.IsRongyu!=0||table.IsGlobal||target.IsGlobal||
            source.IsSystem||!Physical(model,table,source)||ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id||
            name.Type!="string?"||name.IsJsonArray||name.Source.ControlKind=="FileLibrary"||!Physical(model,target,name)||
            Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(name.Source.ColumnName))
            return null;
        var baseline=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==BaselineSingleName(source));
        if(baseline is null||baseline==name||baseline.Type!="string?"||!Physical(model,target,baseline)||
            Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(baseline.Source.ColumnName))return null;
        var automatic=SelectionDisplayFieldCompilation.AutomaticSingle(table,source,baseline);
        if(automatic is not null)return automatic;

        var declared=SelectionDisplayFieldCompilation.Resolve(table,source);
        return !declared.IsJsonArray&&MetadataStoragePolicy.IsTextSingleDisplayJoin(source.Source,declared.Source,baseline.Source.ColumnName)
            ?declared:null;
    }

    /// <summary>原响应属性从表级名称推导，页面覆盖不能改变 JSON 协议。</summary>
    private static string BaselineSingleName(ColumnModel relation)
    {
        if(string.IsNullOrWhiteSpace(relation.Source.ControlDetail))return "name";
        using var definition=JsonDocument.Parse(relation.Source.ControlDetail);
        return definition.RootElement.TryGetProperty("nameField",out var name)?name.GetString()??"name":"name";
    }

    /// <summary>固定父键从所在页面的同父或独立表单字段证明。</summary>
    private static NonRedundantSingleTargetScope ProjectTarget(GenerationModel model,TableModel table,PageModel page,RelationModel relation)
    {
        ColumnModel? parent=null;
        if(relation.TargetPage.Scopes.Concat(relation.Scopes).Any(scope=>scope.Source=="parent"))
        {
            var context=ControlRenderer.RelationContext(page,relation,false);
            parent=context.SameParent?relation.FixedSourceParent??page.Scopes.Single(scope=>scope.Source=="parent").Column:
                table.Columns.SingleOrDefault(column=>column.JsonName==context.TargetParentField);
            if(parent is null||(!Physical(model,table,parent)&&
                !(relation.LogicalSourceParent&&NonRedundantCrossParentCompilation.Managed(model,table,page,parent)))||
                parent.Type is not("string?" or "long?" or "long"))
                throw new NotSupportedException("项目内实时名称缺少当前页已证明的来源父键。");
        }
        return new(relation.SourceColumn.Source.Id,relation.TargetPage.Source.Id,parent?.Source.Id)
        {
            SourcePageId=relation.ScopedStoredSingle||relation.CompoundProjectSource||UsesPageBoundGlobalList(relation)||
                relation.FixedParent is not null||relation.Target.IsGlobal&&parent is not null?page.Source.Id:null,
            ControlSourcePageId=relation.ScopedStoredSingle||UsesProjectControlRead(relation)?page.Source.Id:null
        };
    }

    /// <summary>多值字段按当前页冻结目标，原复选回显与名称筛选套用已发布目标页范围。</summary>
    internal static IReadOnlyList<NonRedundantMultiTargetScope> ProjectMultiTargets(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Source.IsRongyu!=0)return [];
        var multi=table.IsGlobal?Array.Empty<MultiSelectModel>():MultiSelectCompilation.ForService(model,table,page);
        return multi.Where(def=>(def.UsesPageRead||def.UsesNameOverride)&&def.Names is not null)
            .Select(def=>new NonRedundantMultiTargetScope(def.Source.Source.Id,page.Source.Id,def.TargetPage.Source.Id,def.UsesTargetPageScope))
            .Concat(DatabaseCheckboxCompilation.ForService(model,table,page).Where(def=>def.UsesPageRead||def.UsesControlRead||def.UsesNameOverride)
                .Select(def=>new NonRedundantMultiTargetScope(def.Source.Source.Id,page.Source.Id,def.NameTargetPage?.Source.Id,
                    def.LogicalTargetPage is not null||def.PhysicalTargetPage is not null||def.Target.IsGlobal,def.UsesControlRead))).ToArray();
    }

    /// <summary>生成验证与导出窄投影使用完全相同的各页固定目标。</summary>
    internal static NonRedundantReadPlan CompilePage(GenerationModel model,TableModel table,IReadOnlyList<string> outputs,
        IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> crosses,
        IReadOnlyList<NonRedundantSingleTargetScope> singles,IReadOnlyList<NonRedundantMultiTargetScope> multis)=>
        singles.Count==0&&multis.Count==0?NonRedundantReads.CompileWithCrossTables(model.Catalog,table.Source.Id,outputs,crosses,dependencies,deferCascadeNames:true):
            NonRedundantReads.CompileProjectPage(model.Catalog,table.Source.Id,outputs,dependencies,crosses,singles,multis);

    /// <summary>列表、详情与导出下发同一固定页绑定，旧的无条件生成保持原调用。</summary>
    internal static string PagePlanCall(GenerationModel model,TableModel table,PageModel page,
        NonRedundantPagePlan plan,string outputs,string dependencies)
    {
        var arguments=JsonSerializer.Serialize(table.Source.Id)+","+outputs+","+dependencies;
        var cross=NonRedundantCrossTableCompilation.BindingArgument(model,table,page);
        if(plan.SingleTargets.Count==0&&plan.MultiTargets.Count==0)
            return (cross.Length==0?"Compile":"CompileCross")+"("+arguments+cross+")";
        var singles=plan.SingleTargets.Count==0?",Array.Empty<NonRedundantSingleTargetScope>()":SingleTargetArgument(plan.SingleTargets);
        var multis=plan.MultiTargets.Count==0?"":",new NonRedundantMultiTargetScope[]{"+string.Join(",",plan.MultiTargets.Select(scope=>
            $"new({JsonSerializer.Serialize(scope.ColumnId)},{JsonSerializer.Serialize(scope.SourcePageId)},{JsonSerializer.Serialize(scope.TargetPageId)},{(scope.ApplyPageScope?"true":"false")}{(scope.ApplyControlScope?",true":"")})"))+"}";
        return "CompileProjectPage("+arguments+(cross.Length==0?",Array.Empty<NonRedundantCrossTableScope>()":cross)+singles+multis+")";
    }

    /// <summary>业务页和记录规则的生成代码复用同一目标页与物理父键字面量。</summary>
    internal static string SingleTargetArgument(IReadOnlyList<NonRedundantSingleTargetScope> scopes,string? parameter=null)
    {
        if(scopes.Count==0)return "";
        return ","+(parameter is null?"":parameter+":")+SingleTargetArray(scopes);
    }

    /// <summary>读写共用同一关联目标；相互冲突的有效声明不能按不同优先级串表。</summary>
    private static void ValidateTargetDeclarations(GenerationModel model,ColumnModel relation,TableModel target)
    {
        // 明确关联的常量控件从表或页声明确定目标，控件明细保存选项文本。
        if (relation.Source.ControlKind is ("ConstList" or "ConstDownList") && MetadataStoragePolicy.IsMultiIdRelation(relation.Source))
            return;
        if(string.IsNullOrWhiteSpace(relation.Source.ControlDetail))return;
        using var document=JsonDocument.Parse(relation.Source.ControlDetail);var definition=document.RootElement;
        if(definition.TryGetProperty("tableId",out var id)&&model.Tables.SingleOrDefault(table=>table.Source.Id==id.ToString()) is {} byId&&byId.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联读取和写入目标声明不一致。");
        if(definition.TryGetProperty("table",out var name)&&model.Tables.SingleOrDefault(table=>table.Source.TableName==name.ToString()) is {} byName&&byName.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联表名和ID声明不一致。");
        if(definition.TryGetProperty("pageId",out var page)&&model.Tables.SingleOrDefault(table=>table.Pages.Any(item=>item.Source.Id==page.ToString())) is {} byPage&&byPage.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联页面和表声明不一致。");
    }
    /// <summary>沿原逐键覆盖语义比较有效JSON；空覆盖、属性顺序及格式差异不改变读取计划。</summary>
    private static bool HasEffectiveOverride(string source,string overlay)
    {
        try
        {
            var original=JsonNode.Parse(string.IsNullOrWhiteSpace(source)?"{}":source) as JsonObject??throw new InvalidOperationException("关系定义必须是JSON对象。");
            var changes=JsonNode.Parse(overlay) as JsonObject??throw new InvalidOperationException("页级关系定义必须是JSON对象。");var effective=original.DeepClone().AsObject();
            foreach(var pair in changes)effective[pair.Key]=pair.Value?.DeepClone();return !JsonNode.DeepEquals(original,effective);
        }
        catch(Exception error)when(error is JsonException or ArgumentException){throw new InvalidOperationException("关系覆盖必须是有效无重复键的JSON对象。",error);}
    }
    /// <summary>只允许已编译的条件、排序及同一目标的自动显示值覆盖。</summary>
    private static bool ProvenOptionsOverride(GenerationModel model, PageModel page, ColumnModel source, string overlay)
    {
        var original = JsonNode.Parse(string.IsNullOrWhiteSpace(source.Source.ControlDetail) ? "{}" : source.Source.ControlDetail) as JsonObject
            ?? throw new InvalidOperationException("关系定义必须是JSON对象。");
        var changes = JsonNode.Parse(overlay) as JsonObject
            ?? throw new InvalidOperationException("页级关系定义必须是JSON对象。");
        var effective = original.DeepClone().AsObject();
        foreach (var pair in changes)
            effective[pair.Key] = pair.Value?.DeepClone();

        var conditionsChanged = !JsonNode.DeepEquals(original["whereSql"], effective["whereSql"]);
        var orderChanged = !JsonNode.DeepEquals(original["orderBy"], effective["orderBy"]);
        // 合法的空覆盖可以由实际目标页继承，目标页也未设置时继续使用固定默认次序。
        var clearedListOrder = source.Source.ControlKind == "List" &&
            (effective["orderBy"] is null || effective["orderBy"] is JsonValue value &&
                value.TryGetValue<string>(out var orderText) && string.IsNullOrWhiteSpace(orderText));
        original.Remove("whereSql");
        effective.Remove("whereSql");
        original.Remove("orderBy");
        effective.Remove("orderBy");
        var pageChanged = source.Source.ControlKind=="List" && !JsonNode.DeepEquals(original["pageId"], effective["pageId"]);
        if (source.Source.ControlKind=="List")
        {
            original.Remove("pageId");
            effective.Remove("pageId");
        }
        var nameChanged = !JsonNode.DeepEquals(original["nameField"], effective["nameField"]);
        original.Remove("nameField");
        effective.Remove("nameField");
        if (!JsonNode.DeepEquals(original, effective))
            return false;

        var compiled = RelationCompilation.ForPage(model, page)
            .SingleOrDefault(relation => relation.SourceColumn.Source.Id == source.Source.Id);
        var owner=model.Tables.Single(table=>table.Source.Id==page.Source.ConfigTableId);
        return compiled is not null &&
            compiled.Target.Source.Id == ForeignValidationRenderer.Target(model, source)?.Source.Id &&
            (!nameChanged || compiled.NameColumn.IsId || PageSingleDisplayOverride(model,owner,source,compiled.Target,compiled.NameColumn) is not null) &&
            (!pageChanged || compiled.ListPageOverride) &&
            (!conditionsChanged || compiled.Conditions is not null || compiled.Scopes.Count>0 ||
                source.Source.ControlKind=="List" && string.IsNullOrWhiteSpace(
                    ProjectScopedSingleRelationTarget.ListWhereSql(model.Catalog,page.Source,source.Source))) &&
            (!orderChanged || compiled.OptionsOrder is not null || clearedListOrder);
    }
    /// <summary>没有页面的依赖表仍必须先验证物理结构；本入口不写任何生成文件。</summary>
    internal static void Validate(GenerationModel model,TableModel table)
    {try{foreach(var page in table.Pages)ForPage(model,table,page);}catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException){throw new InvalidOperationException("非冗余派生字段读取批次未完成："+error.Message,error);}}
}
