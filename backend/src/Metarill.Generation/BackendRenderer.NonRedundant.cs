using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>非冗余读写分离：物理实体负责写入和当前锁，固定计划负责派生读取与回显。</summary>
internal static partial class BackendRenderer
{
    /// <summary>项目元数据仅嵌入和反序列化一次；各页只持有自己的不可变固定计划。</summary>
    private static string NonRedundantCatalog(GenerationModel model)
    {
        var candidateLevels=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>CrossTableWriteCompilation.ForService(model,table,page)))
            .SelectMany(plan=>plan.Read.Levels).ToArray();
        var accountPages=candidateLevels.Where(level=>FixedPageConditionCompilation.HasAccountVariables(level.Page))
            .Select(level=>level.Page.Source.Id).ToHashSet(StringComparer.Ordinal);
        // 保留参与递归范围证明的表及其候选页，避免无关页面条件进入精简运行目录。
        var scopedPages=candidateLevels.Where(level=>FixedPageConditionCompilation.HasLogicalFields(level.Page)).Select(level=>(level.Target,level.Page))
            .Concat(model.Tables.SelectMany(table=>table.Pages.Where(page=>NonRedundantReadCompilation.GlobalSelectionPage(table,page)&&
                (FixedPageConditionCompilation.HasSelfTreeFields(page)||CrossTableWriteCompilation.ForService(model,table,page).Count>0)).Select(page=>(table,page))));
        scopedPages=scopedPages.Concat(model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>CascadeTreeReadCompilation.ForService(model,table,page)))
            .Where(tree=>tree.ScopedCondition).Select(tree=>(tree.Target,tree.TargetPage)));
        scopedPages=scopedPages.Concat(model.Tables.SelectMany(table=>table.Pages
            .Where(page=>NonRedundantReadCompilation.GlobalAccountMultiPage(table,page)||
                NonRedundantReadCompilation.GlobalSingleParentPage(table,page)||
                FixedPageConditionCompilation.HasGlobalProjectSingleScope(table,page)||
                FixedPageConditionCompilation.HasGlobalListParentScope(table,page)||
                MultiGlobalParentCompilation.SingleTarget(model,table,page) is not null||
                MultiGlobalParentCompilation.PhysicalPageParent(model,table,page) is not null).Select(page=>(table,page))));
        var relations=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>RelationCompilation.ForPage(model,page)
            .Concat(RelationCompilation.ForPage(model,page,true,forExcel:true)))).ToArray();
        var globalExactPages=model.Tables.SelectMany(table=>table.Pages.Where(page=>
            GlobalCrossTableTarget.DeclaresExactPage(table.Source,page.Source)).Select(page=>(table,page))).ToArray();
        scopedPages=scopedPages.Concat(globalExactPages);
        var functionRelations = relations.Where(relation => relation.FunctionForm &&
            DerivedRelationNamesCompilation.UsesFixedRead(model, relation)).ToArray();
        var scopedRelations=relations.Where(relation=>relation.FunctionForm&&relation.Target.IsGlobal||
                DerivedRelationNamesCompilation.UsesLogicalPage(relation)||
                DerivedRelationNamesCompilation.UsesExactSingle(relation)||
                DerivedRelationNamesCompilation.HasGlobalAccountScope(relation.Target,relation.TargetPage)||
                NonRedundantReadCompilation.GlobalSingleParentPage(relation.Target,relation.TargetPage)||
                FixedPageConditionCompilation.HasGlobalProjectSingleScope(relation.Target,relation.TargetPage)||
                NonRedundantReadCompilation.UsesPageBoundGlobalList(relation)||
                DerivedRelationNamesCompilation.UsesGlobalPrimaryKeyDisplay(relation)||
                NonRedundantReadCompilation.UsesProjectSingleRead(relation)).ToArray();
        scopedPages=scopedPages.Concat(scopedRelations.Select(relation=>(relation.Target,relation.TargetPage)));
        // 固定单值读取在宿主重新装载时，从实际来源页重新验证物理或逻辑父键。
        var scopedOwners=scopedRelations.Where(relation=>relation.ScopedStoredSingle||relation.CompoundProjectSource||
            relation.ListPageOverride||relation.LogicalSourceParent||relation.CompoundGlobalSource||
            NonRedundantReadCompilation.UsesProjectControlRead(relation)||
            !relation.Target.IsGlobal&&relation.FixedParent is not null)
            .Select(relation=>relation.SourceColumn.Source.ConfigTableId).ToHashSet(StringComparer.Ordinal);
        scopedPages=scopedPages.Concat(model.Tables.Where(table=>scopedOwners.Contains(table.Source.Id))
            .SelectMany(table=>table.Pages.Select(page=>(table,page))));
        // 目标页自身没有叶名条件时，仍保留由下拉附加条件实际引用的树页。
        scopedPages=scopedPages.Concat(scopedRelations.Where(relation=>relation.Conditions?.SelfTreeFields.Count>0)
            .SelectMany(relation=>CascadeTreeReadCompilation.ForService(model,relation.Target,relation.TargetPage)
                .Where(tree=>relation.Conditions!.SelfTreeFields.Contains(
                    CascadeTreeViewRenderer.Name(relation.Target,tree.Source)!.Source.ColumnName))
                .Select(tree=>(tree.Target,tree.TargetPage))));
        var multiPageIds=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>
                NonRedundantReadCompilation.ProjectMultiTargets(model,table,page)))
            .SelectMany(scope=>new[]{scope.SourcePageId,scope.TargetPageId}).ToHashSet(StringComparer.Ordinal);
        scopedPages=scopedPages.Concat(model.Tables.SelectMany(table=>table.Pages.Where(page=>multiPageIds.Contains(page.Source.Id))
            .Select(page=>(table,page))));
        // Excel 单独声明的逻辑条件也需要保留实际目标页和自树候选页。
        var logicalMultiImports=model.Tables.SelectMany(table=>table.Pages.Where(ImportRenderer.Enabled)
            .SelectMany(page=>MultiSelectCompilation.ForPage(model,table,page))
            .Where(multi=>multi.ImportCondition?.HasLogicalFields==true)).DistinctBy(multi=>
                (multi.TargetPage.Source.Id,multi.Source.Source.Id,multi.ImportCondition!.Definition)).ToArray();
        var fixedSingleImports=relations.Where(relation=>relation.ImportCondition is not null&&relation.Target.Source.IsRongyu==0).ToArray();
        var importTargets=logicalMultiImports.Select(multi=>(multi.Target,multi.TargetPage,Condition:multi.ImportCondition!))
            .Concat(fixedSingleImports.Select(relation=>(relation.Target,relation.TargetPage,Condition:relation.ImportCondition!))).ToArray();
        scopedPages=scopedPages.Concat(importTargets.Select(item=>(item.Target,item.TargetPage)))
            .Concat(importTargets.SelectMany(item=>CascadeTreeReadCompilation.ForService(model,item.Target,item.TargetPage)
                .Where(tree=>CascadeTreeViewRenderer.Name(item.Target,tree.Source) is {} leaf&&item.Condition.SelfTreeFields.Contains(leaf.Source.ColumnName))
                .Select(tree=>(tree.Target,tree.TargetPage))));
        // 功能表的独立父字段只在表单中存在，目标页的固定范围仍需完整下发。
        var functionCheckboxes=model.Tables.Where(table=>table.Source.IsFunctionTable==1)
            .SelectMany(table=>table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForService(model,table,page))).ToArray();
        var scopedCheckboxes=model.Tables.Where(table=>table.Source.IsFunctionTable==0)
            .SelectMany(table=>table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForService(model,table,page)))
            .Where(checkbox=>checkbox.ExactTarget is not null||checkbox.SharedLogicalParentScope||checkbox.SharedDerivedPageScope||
                checkbox.DerivedName&&DerivedCheckboxNamesCompilation.UsesScopedCheckboxLabel(model,checkbox)).ToArray();
        var realtimeCheckboxes=model.Tables.Where(table=>table.Source.IsRongyu==0&&table.Source.IsFunctionTable==0)
            .SelectMany(table=>table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForService(model,table,page)))
            .Where(checkbox=>checkbox.DerivedName&&checkbox.NameTargetPage is not null).ToArray();
        // 隐藏的联接外键仍可影响已发布的多值名称，保留候选、业务回显及导入使用的实际页。
        static IEnumerable<string> NamePages(IEnumerable<Metarill.Data.Queries.NonRedundantSingleTargetScope> targets)=>
            targets.SelectMany(target=>new[]{target.PageId,target.SourcePageId,target.ControlSourcePageId}.OfType<string>()
                .Concat(NamePages(target.NestedTargets)));
        var multiSelections=model.Tables.Where(table=>table.Source.IsFunctionTable==0)
            .SelectMany(table=>table.Pages.SelectMany(page=>MultiSelectCompilation.ForService(model,table,page))).ToArray();
        // 即使名称是普通物理文本，业务回显和筛选也要保留全局多选页实际发布的范围。
        var globalMultiOwners=model.Tables.Where(table=>table.IsGlobal).Select(table=>table.Source.Id).ToHashSet(StringComparer.Ordinal);
        var staticMultiPages=multiSelections.Where(multi=>globalMultiOwners.Contains(multi.Source.Source.ConfigTableId)&&
                MultiSelectCompilation.UsesStaticGlobalDictionary(multi)||
                FixedPageConditionCompilation.HasGlobalProjectMultiScope(multi.Target,multi.TargetPage))
            .Select(multi=>multi.TargetPage.Source.Id)
            .ToHashSet(StringComparer.Ordinal);
        accountPages.UnionWith(multiSelections.Where(multi=>multi.Target.IsGlobal&&
                !string.IsNullOrWhiteSpace(multi.TargetPage.Source.WhereSql))
            .Select(multi=>multi.TargetPage.Source.Id));
        var multiNamePages=realtimeCheckboxes.SelectMany(checkbox=>NamePages(
            NonRedundantReadCompilation.ProjectPathTargets(model,checkbox.Target,checkbox.NameTargetPage!,
                [checkbox.Name.Source.ColumnName],multiValuePath:true)))
            .Concat(multiSelections.SelectMany(multi=>NamePages(DerivedMultiNamesCompilation.NameTargets(
                model,multi.Target,multi.TargetPage,multi.Name))))
            .Concat(logicalMultiImports.SelectMany(multi=>NamePages(DerivedMultiNamesCompilation.NameTargets(
                model,multi.Target,multi.TargetPage,multi.Name,multi.ImportCondition))))
            .ToHashSet(StringComparer.Ordinal);
        // 排序单值联接也需要固定中间页和字典条件，发布目录按实际依赖保留。
        var checkboxOrders=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForPage(model,table,page)))
            .Select(checkbox=>checkbox.CandidateOrderTargets).OfType<IReadOnlyList<Metarill.Data.Queries.NonRedundantSingleTargetScope>>().ToArray();
        var selfTrees=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>CascadeTreeReadCompilation.ForService(model,table,page))).ToArray();
        var candidateRelationPages=selfTrees.Where(tree=>tree.DerivedOrder)
            .SelectMany(tree=>NamePages(DerivedCascadeNamesCompilation.OrderTargets(model,tree)))
            .Concat(selfTrees.Where(tree=>tree.DerivedName).SelectMany(tree=>NamePages(
                CascadeTargetOrderCompilation.NameTargets(model,tree.Target,tree.TargetPage,tree.Name,tree.DerivedName))))
            .Concat(model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>CrossTableReadCompilation.ForService(model,table,page)))
                .SelectMany(plan=>plan.Levels).Where(level=>level.DerivedOrder).SelectMany(level=>NamePages(
                    CascadeTargetOrderCompilation.OrderTargets(model,level.Target,level.Page,level.Name,level.Order,level.DerivedOrder))))
            .Concat(checkboxOrders.SelectMany(NamePages))
            .Concat(relations.Where(relation => relation.IndependentOrder.Count > 0)
                .SelectMany(relation => NamePages(DerivedRelationNamesCompilation.CandidateOrderTargets(model, relation))))
            .ToHashSet(StringComparer.Ordinal);
        scopedPages=scopedPages.Concat(model.Tables.SelectMany(table=>table.Pages.Where(page=>candidateRelationPages.Contains(page.Source.Id))
            .Select(page=>(table,page))));
        foreach(var checkbox in realtimeCheckboxes.Where(checkbox=>checkbox.GlobalRealtimeNameOverride))
            multiNamePages.UnionWith(Metarill.Data.Queries.NonRedundantReads.PublishedGlobalCheckboxNamePages(model.Catalog,
                checkbox.Target.Source.Id,checkbox.NameTargetPage!.Source.Id,checkbox.Name.Source.ColumnName));
        scopedPages=scopedPages.Concat(model.Tables.SelectMany(table=>table.Pages.Where(page=>multiNamePages.Contains(page.Source.Id))
            .Select(page=>(table,page))));
        var publishedCheckboxes=functionCheckboxes.Concat(scopedCheckboxes).Concat(realtimeCheckboxes);
        var checkboxScopes=publishedCheckboxes.Where(checkbox=>checkbox.DerivedName&&
            (checkbox.GlobalRealtimeNameOverride||checkbox.SharedDerivedPageScope||checkbox.LogicalTargetPage is not null||
                checkbox.ExactTarget is not null)).ToArray();
        // 复选的物理目标页也可能绑定全局父，冻结实际目标及父页条件。
        foreach(var checkbox in publishedCheckboxes.Where(checkbox=>checkbox.GlobalParentPage is not null))
        {
            var targetPage=checkbox.GlobalParentPage!;
            var parent=checkbox.SourceParents.Single();
            var parentPageId=Metarill.Data.Queries.NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
                checkbox.Target.Source.Id,targetPage.Source.Id);
            scopedPages=scopedPages.Concat([(checkbox.Target,targetPage),(parent,parent.Pages.Single(page=>page.Source.Id==parentPageId))]);
        }
        accountPages.UnionWith(checkboxScopes.Select(checkbox=>(checkbox.LogicalTargetPage??checkbox.NameTargetPage)!.Source.Id));
        scopedPages=scopedPages.Concat(checkboxScopes.Select(checkbox=>(checkbox.Target,(checkbox.LogicalTargetPage??checkbox.NameTargetPage)!)));
        var exactMultiTargets=multiSelections
            .Where(multi=>multi.ExactTarget is not null&&(DerivedMultiNamesCompilation.UsesFixedRead(model,multi)||
                multi.ImportCondition?.HasLogicalFields==true))
            .Select(multi=>(multi.Target,multi.TargetPage)).DistinctBy(item=>item.TargetPage.Source.Id).ToArray();
        // 静态目录保留自树精确匹配页面，运行时从该页面重建已核对的叶名证明。
        var exactTreePages=model.Tables.SelectMany(table=>table.Pages
            .Where(page=>NonRedundantReadCompilation.SelfTreeExactColumn(table,page) is not null)
            .Select(page=>(table,page)));
        scopedPages=scopedPages.Concat(exactMultiTargets).Concat(exactTreePages);
        var hasExactCheckboxParent=checkboxScopes.Any(checkbox=>DerivedCheckboxNamesCompilation.HasExactLogicalParent(model,checkbox));
        var hasExactCheckboxMembership=checkboxScopes.Any(checkbox=>DerivedCheckboxNamesCompilation.ExactMembershipPage(model,checkbox) is not null);
        var nestedTables=RelationPropagationCompilation.PublishedRecursiveTables(model,scopedPages).ToHashSet(StringComparer.Ordinal);
        if(scopedCheckboxes.Where(checkbox=>checkbox.SharedLogicalParentScope||checkbox.SharedDerivedPageScope).Any(checkbox=>
        {
            if(checkbox.SharedDerivedPageScope||!NonRedundantReadCompilation.Physical(model,checkbox.Target,checkbox.Name))return true;
            var baseline=DatabaseCheckboxDefinition.Parse(checkbox.Source.Source.ControlDetail).NameField;
            return checkbox.Target.Columns.Any(column=>column.Source.ColumnName==baseline&&
                !NonRedundantReadCompilation.Physical(model,checkbox.Target,column));
        }))
        {
            // 保存同时读取表级和页级名称，两条联接引用的固定页都要保留到生成目录。
            var checkboxBindings=RelationPropagationCompilation.Definitions(RelationPropagationCompilation.DerivedBindings(model)
                .Where(binding=>binding.CheckboxTargetPageId is not null).ToArray());
            var references=Metarill.Data.Relations.DerivedCrossTableCaches.ScopedSingleReferencedTables(model.Catalog,checkboxBindings);
            nestedTables.UnionWith(model.Tables.Where(table=>references.Contains(table.Source.TableName)).Select(table=>table.Source.Id));
        }
        var recursiveCandidates=nestedTables.Count>0;
        var selfTreeNamePages=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>NonRedundantReadCompilation.SelfTreeNameQueries(model,table,page)))
            .Concat(model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>CascadeTreeReadCompilation.ForService(model,table,page)))
                .Where(tree=>tree.Target.IsGlobal))
            .Concat(model.Tables.Where(table=>table.IsGlobal&&table.Source.IsRongyu==0)
                .SelectMany(table=>table.Pages.SelectMany(page=>CascadeTreeReadCompilation.ForService(model,table,page))))
            .Select(tree=>tree.TargetPage.Source.Id).ToHashSet(StringComparer.Ordinal);
        var metadata=JsonSerializer.Serialize(GenerationMetadataPublisher.ForRelations(model.Catalog,accountPages,selfTreeNamePages,nestedTables,
            staticMultiPages:staticMultiPages),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var cross=model.Tables.Any(table=>table.Source.IsRongyu==0&&table.Pages.Any(page=>CrossTableWriteCompilation.ForService(model,table,page).Count>0));
        var helpers=cross?"    /// <summary>目标条件与页面一起冻结，静态初始化不读取后声明字段。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileCross(string tableId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[] bindings)=>Metarill.Data.Queries.NonRedundantReads.CompileWithCrossTables(Snapshot,tableId,outputs,bindings,dependencies,deferCascadeNames:true);\n    /// <summary>与统一物理结构共用稀疏选择编码器。</summary>\n    internal static Metarill.Core.Metadata.CrossTableSparseSelection CompileSelection(string tableId,string sourceId)=>Metarill.Core.Metadata.CrossTableSparseSelection.Compile(Snapshot.ConfigTables.Single(table=>table.Id==tableId),sourceId,Snapshot.ConfigColumns.Where(column=>column.ConfigTableId==tableId));\n":"";
        if(model.Tables.Any(table=>table.Source.IsRongyu==0&&table.Pages.Any(page=>SharedCrossTableCompilation.ForPage(model,table,page).Groups.Count>0)))helpers+="    /// <summary>共享R0由整组编译，成员不能分别打包覆盖。</summary>\n    internal static Metarill.Core.Metadata.SharedCrossTableSparseSelection CompileSharedSelection(string tableId,string sourceId)=>Metarill.Core.Metadata.SharedCrossTableSparseSelection.Compile(Snapshot.ConfigTables.Single(table=>table.Id==tableId),sourceId,Snapshot.ConfigColumns.Where(column=>column.ConfigTableId==tableId));\n";
        if(scopedRelations.Any(relation=>NonRedundantReadCompilation.UsesProjectSingleRead(relation)||
            NonRedundantReadCompilation.UsesPageBoundGlobalList(relation))||multiPageIds.Count>0||
            relations.Any(relation=>NonRedundantReadCompilation.PageSingleDisplayOverride(model,
                model.Tables.Single(table=>table.Source.Id==relation.SourceColumn.Source.ConfigTableId),
                relation.SourceColumn,relation.Target,relation.NameColumn) is not null)||
            relations.Any(relation=>DerivedRelationNamesCompilation.UsesExactSingle(relation)&&
                ExactDataManageCompilation.Resolve(model,relation.Target,relation.TargetPage)?.SingleTargets.Count>0))
            helpers+="    /// <summary>业务页实时关系名的目标及父范围由生成时固定。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileProjectPage(string tableId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[] cross,Metarill.Data.Queries.NonRedundantSingleTargetScope[] single,Metarill.Data.Queries.NonRedundantMultiTargetScope[]? multi=null)=>Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(Snapshot,tableId,outputs,dependencies,cross,single,multi);\n";
        if(relations.Any(relation=>!DerivedRelationNamesCompilation.UsesLogicalPage(relation)&&
            DerivedRelationNamesCompilation.ProjectCandidateTargets(model,relation).Count>0))
            helpers+="    /// <summary>派生候选沿实际中间页范围编译完整名称。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileProjectRelation(string tableId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[] single)=>Metarill.Data.Queries.NonRedundantReads.CompileProjectRelation(Snapshot,tableId,outputs,dependencies,single);\n";
        if(scopedRelations.Any(relation=>!relation.Target.IsGlobal&&DerivedRelationNamesCompilation.UsesLogicalPage(relation))||
            hasExactCheckboxMembership)helpers+="    /// <summary>单值候选、回读及保存共享发布页的逻辑条件。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileRelationMembership(string tableId,string pageId,string[] fields,string[] cross,string[] tree,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompilePageMembership(Snapshot,Snapshot.ConfigTables.Single(table=>table.Id==tableId),Snapshot.ConfigPages.Single(page=>page.Id==pageId),fields,cross.ToHashSet(StringComparer.Ordinal),tree.ToHashSet(StringComparer.Ordinal),outputs,dependencies,singleTargets);\n";
        if(scopedRelations.Any(relation=>(relation.Target.IsGlobal||DerivedRelationNamesCompilation.EmbedsParentScope(relation.Target,relation.FixedParent))&&relation.Conditions?.HasLogicalFields==true))
            helpers+="    /// <summary>全局下拉复用原发布页与附加逻辑字段的固定读取范围。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalListCandidate(string tableId,string pageId,string[] fields,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalListCandidate(Snapshot,tableId,pageId,fields,outputs,dependencies,singleTargets);\n";
        if (scopedRelations.Any(relation => DerivedRelationNamesCompilation.EmbedsParentScope(relation.Target,relation.FixedParent) &&
            ExactDataManageCompilation.Declared(relation.TargetPage)))
            helpers+="    /// <summary>按已发布目标页加载逻辑父级与精确匹配依赖。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileExactSingleParentCandidate(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[]? bindings=null,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null,string[]? listFields=null)=>Metarill.Data.Queries.NonRedundantReads.CompileExactSingleParentCandidate(Snapshot,tableId,pageId,outputs,dependencies,bindings,singleTargets,listFields);\n";
        if(hasExactCheckboxParent)
            helpers+="    /// <summary>功能表复选按实际目标页加载逻辑父级和精确匹配依赖。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileExactCheckboxParentCandidate(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[] bindings,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileExactCheckboxParentCandidate(Snapshot,tableId,pageId,outputs,dependencies,bindings,singleTargets);\n";
        if(recursiveCandidates||scopedRelations.Any(relation=>DerivedRelationNamesCompilation.EmbedsParentScope(relation.Target,relation.FixedParent)))helpers+="    /// <summary>候选通过发布目录保留实际依赖的逻辑级联条件。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileCrossPageCandidate(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[]? bindings=null,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(Snapshot,tableId,pageId,outputs,dependencies,bindings,singleTargets);\n";
        if(model.Tables.Any(table=>RuleRecordEntity(model,table)))helpers+="    /// <summary>规则实体保留根物理存储；父范围及条件使用同一冻结逻辑投影。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileRuleRecord(string tableId,string[] outputs,string[] storage,string[] dependencies,string[] rules,Metarill.Data.Queries.NonRedundantCrossTableScope[] bindings,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singles=null)=>Metarill.Data.Queries.NonRedundantReads.CompileRuleRecord(Snapshot,tableId,outputs,storage,bindings,dependencies,deferCascadeNames:true,logicalOutputs:rules,singleTargets:singles);\n";
        if(globalExactPages.Length>0)
            helpers+="    /// <summary>全局单选匹配列来自实际发布页，当前授权由生成服务绑定。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalExactSingleCandidate(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null,Metarill.Data.Queries.NonRedundantCrossTableScope[]? bindings=null,string[]? listFields=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalExactSingleCandidate(Snapshot,tableId,pageId,outputs,dependencies,singleTargets,bindings,listFields);\n";
        if(globalExactPages.Any(item=>ExactDataManageCompilation.Resolve(model,item.table,item.page) is {Derived:true}))
            helpers+="    /// <summary>写前权限联接从实际发布的单选页加载，并保留所依赖的中间页。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalExactSingleMatch(string tableId,string pageId,string field,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalExactSingleMatch(Snapshot,tableId,pageId,field,singleTargets);\n";
        if(model.Tables.Any(table=>table.Pages.Any(page=>NonRedundantReadCompilation.GlobalAccountMultiPage(table,page))))
            helpers+="    /// <summary>全局多选列表、统计和已选回读沿同一个带父参数的发布计划。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalAccountMultiPage(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[]? bindings=null,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalAccountMultiPage(Snapshot,tableId,pageId,outputs,dependencies,bindings??Array.Empty<Metarill.Data.Queries.NonRedundantCrossTableScope>(),singleTargets);\n";
        if(model.Tables.Any(table=>table.Pages.Any(page=>NonRedundantReadCompilation.GlobalSingleParentPage(table,page))))
            helpers+="    /// <summary>全局单选直接入口使用实际账号和父键限制。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalSingleParentPage(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[]? bindings=null,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalSingleParentPage(Snapshot,tableId,pageId,outputs,dependencies,bindings??Array.Empty<Metarill.Data.Queries.NonRedundantCrossTableScope>(),singleTargets);\n";
        if(model.Tables.Any(table=>table.Pages.Any(page=>NonRedundantReadCompilation.GlobalSelectionPage(table,page))))
            helpers+="    /// <summary>原候选页的自树投影由固定图补齐显示路径。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalPage(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalPage(Snapshot,tableId,pageId,outputs,dependencies,singleTargets);\n";
        if (model.Tables.Any(NonRedundantReadCompilation.HasGlobalRowProject) ||
            scopedRelations.Any(relation=>DerivedRelationNamesCompilation.HasGlobalAccountScope(relation.Target,relation.TargetPage)||
                NonRedundantReadCompilation.GlobalSingleParentPage(relation.Target,relation.TargetPage)||
                FixedPageConditionCompilation.HasGlobalProjectSingleScope(relation.Target,relation.TargetPage)||
                FixedPageConditionCompilation.HasGlobalListParentScope(relation.Target,relation.TargetPage)) ||
            functionRelations.Any(relation => DerivedRelationNamesCompilation.UsesGlobalPlan(model, relation)) ||
            scopedRelations.Any(DerivedRelationNamesCompilation.UsesGlobalPrimaryKeyDisplay) ||
            model.Tables.SelectMany(table => table.Pages).SelectMany(page => RelationCompilation.ForPage(model, page, true))
                .Any(relation => relation.Target.IsGlobal && DerivedRelationNamesCompilation.IsPhysicalTypedName(model, relation.Target, relation.NameColumn)))
            helpers+="    /// <summary>候选仅使用发布页的固定范围，普通记录仍按所属项目读写。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileGlobalCandidate(string tableId,string pageId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalCandidate(Snapshot,tableId,pageId,outputs,dependencies,singleTargets);\n";
        if (relations.Any(relation => !relation.Target.IsGlobal &&
            DerivedRelationNamesCompilation.UsesPhysicalPlan(model, relation) &&
            DerivedRelationNamesCompilation.UsesFixedRead(model, relation)))
            helpers+="    /// <summary>普通 R1 候选使用项目范围和实际物理列。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompilePhysicalRelation(string tableId,string[] outputs,string[] dependencies)=>Metarill.Data.Queries.NonRedundantReads.CompilePhysicalRelation(Snapshot,tableId,outputs,dependencies);\n";
        if (relations.Any(relation => DerivedRelationNamesCompilation.UsesPhysicalPlan(model, relation) &&
            DerivedRelationNamesCompilation.UsesFixedRead(model, relation) &&
            SharedRedundantParentCompilation.HasAccountPath(model, relation.Target, relation.TargetPage)))
            helpers+="    /// <summary>整数候选只能使用生成时已发布的物理父范围。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompilePhysicalRelation(string tableId,string[] outputs,string[] dependencies,Metarill.Data.Queries.RedundantSharedParentPlan[] parents)=>Metarill.Data.Queries.NonRedundantReads.CompilePhysicalRelation(Snapshot,tableId,outputs,dependencies,parents);\n";
        if(model.Tables.Any(table=>table.Pages.Any(page=>FixedPageConditionCompilation.HasMultiTargetScope(model,table,page))))
            helpers+="    /// <summary>多值控件的目标页与逻辑范围在发布时冻结。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileScopedMultiLabel(string tableId,string pageId,string field,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileScopedMultiLabel(Snapshot,tableId,pageId,field,outputs,dependencies,singleTargets);\n";
        if(functionRelations.Any(relation=>relation.FixedParent is {Global:true})||
            model.Tables.Any(table=>table.Pages.Any(page=>MultiGlobalParentCompilation.SingleTarget(model,table,page) is not null||
            MultiGlobalParentCompilation.PhysicalPageParent(model,table,page) is not null||
            MultiSelectCompilation.ForService(model,table,page).Any(multi=>multi.GlobalParent)||
            DatabaseCheckboxCompilation.ForService(model,table,page).Any(checkbox=>checkbox.GlobalParent))))
            helpers+="    /// <summary>校验实际多值目标页绑定的全局父。源码冻结后只接受规范ID。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileMultiGlobalParent(string tableId,string pageId)=>Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(Snapshot,tableId,pageId);\n";
        if(model.Tables.Any(table=>table.Pages.Any(page=>
            DatabaseCheckboxCompilation.ForService(model,table,page).Any(def=>DerivedCheckboxNamesCompilation.UsesScopedCheckboxLabel(model,def)))))
            helpers+="    /// <summary>原复选目标页的各父级与精确范围按实际发布页编译。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileScopedCheckboxLabel(string tableId,string pageId,string field,string[] outputs,string[] dependencies,bool exact,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileScopedCheckboxLabel(Snapshot,tableId,pageId,field,outputs,dependencies,exact,singleTargets);\n";
        if(exactMultiTargets.Length>0)
            helpers+="    /// <summary>实际多选页的逻辑范围和精确匹配投影共同冻结。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileExactMultiLabel(string tableId,string pageId,string field,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null,string[]? conditionFields=null)=>Metarill.Data.Queries.NonRedundantReads.CompileExactMultiLabel(Snapshot,tableId,pageId,field,outputs,dependencies,singleTargets,conditionFields);\n";
        if(logicalMultiImports.Length>0)
            helpers+="    /// <summary>Excel 名称匹配的附加逻辑字段沿实际候选页编译。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileMultiImportLabel(string tableId,string pageId,string field,string[] fields,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileMultiImportLabel(Snapshot,tableId,pageId,field,fields,singleTargets);\n";
        if(fixedSingleImports.Length>0)
            helpers+="    /// <summary>单选 Excel 匹配复用实际发布页和固定字段计划。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileSingleImportLabel(string tableId,string pageId,string field,string[] fields,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileSingleImportLabel(Snapshot,tableId,pageId,field,fields,singleTargets);\n";
        var derivedHelper=(checkboxOrders.Length>0||relations.Any(relation=>DerivedRelationNamesCompilation.UsesFixedRead(model,relation)||relation.IndependentOrder.Count>0)||model.Tables.Any(table=>table.Pages.Any(page=>MultiSelectCompilation.ForService(model,table,page).Any(multi=>DerivedMultiNamesCompilation.Declared(model,multi.Target,multi.Name))||DatabaseCheckboxCompilation.ForService(model,table,page).Any(def=>def.DerivedName)||CascadeTreeReadCompilation.ForService(model,table,page).Any(def=>def.UsesFixedRead)||CrossTableReadCompilation.ForService(model,table,page).Any(def=>def.Levels.Any(level=>level.Target.Source.IsRongyu==0)))))?"    /// <summary>派生候选不延迟级联名称，完整标量路径在发布时固定。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileRelation(string tableId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>singleTargets is null?Metarill.Data.Queries.NonRedundantReads.Compile(Snapshot,tableId,outputs,dependencies):Metarill.Data.Queries.NonRedundantReads.CompileProjectRelation(Snapshot,tableId,outputs,dependencies,singleTargets);\n":"";
        return $$"""

/// <summary>生成来源的私有只读快照，不提供外部取出或在线改写入口。</summary>
internal static class GeneratedNonRedundantReadCatalog
{
    private static readonly Metarill.Core.Metadata.MetadataCatalog Snapshot=System.Text.Json.JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>({{Literal(metadata)}},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    /// <summary>只在各页静态初始化时调用，普通请求执行已固定的SQL计划。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan Compile(string tableId,string[] outputs,string[] dependencies)=>Metarill.Data.Queries.NonRedundantReads.Compile(Snapshot,tableId,outputs,dependencies,deferCascadeNames:true);
    /// <summary>匹配字段只编译一次；主记录和实际关联来源由专用写入计划锁定。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileMatch(string tableId,string field)=>Metarill.Data.Queries.NonRedundantReads.CompileMatch(Snapshot,tableId,field);
    /// <summary>叶名精确匹配沿实际发布页及其完整自树路径。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileSelfTreeExactMatch(string tableId,string pageId)=>Metarill.Data.Queries.NonRedundantReads.CompileSelfTreeExactMatch(Snapshot,tableId,pageId);
    /// <summary>固定派生多值名称与原候选范围共用静态计划。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileMultiLabel(string tableId,string field,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantSingleTargetScope[]? singleTargets=null)=>Metarill.Data.Queries.NonRedundantReads.CompileMultiLabel(Snapshot,tableId,field,outputs,dependencies,singleTargets);
{{derivedHelper}}{{helpers}}}

""";
    }
    /// <summary>VO只声明原可读字段及其原自动显示别名，隐藏范围依赖不进入响应。</summary>
    private static string NonRedundantVo(GenerationModel model,TableModel table,PageModel page)
    {
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);var constants=page.Readable.Where(column=>column.Source.ControlKind is "ConstList" or "ConstDownList").ToArray();
        var result=new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Globalization;").AppendLine("using System.Text.Json;").AppendLine("using System.Text.Json.Serialization;").AppendLine("using Metarill.Data.Queries;").AppendLine($"using {model.Namespace}.Generated.Entities;").AppendLine($"namespace {model.Namespace}.Generated.VOs;").AppendLine($"public sealed partial class {page.Name}Vo").AppendLine("{");
        foreach(var output in plan.Outputs)
        {
            var column=page.Readable.SingleOrDefault(column=>column.Source.ColumnName==output.Storage);
            result.AppendLine($"    [JsonPropertyName({Literal(output.JsonName)})]");
            if(column is not null&&UsesBooleanOptionWireFormat(column))result.AppendLine("    [JsonConverter(typeof(Metarill.Core.Contracts.CheckboxBooleanJsonConverter))]");
            if (column?.IsMonthDate == true) result.AppendLine("    [JsonConverter(typeof(Metarill.Core.Contracts.OriginalMonthDateJsonConverter))]");
            if(output.Type=="TimeSpan?")result.AppendLine($"    [JsonConverter(typeof({TimeFieldCompilation.Converter(column)}))]");
            // 物理存储仍是字符串；原接口文件值保持JSON数组或null，不能向前端返回二次编码字符串。
            result.AppendLine($"    public {(output.IsJsonArray?"JsonElement?":output.Type)} {output.Property} {{get;{(table.Columns.Any(item=>item.Source.ColumnName==output.Storage&&item.IsManagedCascadeName)?"set":"init")};}}");
        }
        foreach(var column in constants)
        {if(plan.Outputs.Any(output=>output.Property.Equals(column.Property+"Show",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("常量显示别名冲突。");result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName+"Show")})] public string? {column.Property}Show {{get;init;}}");}
        result.Append(CascadeTreeViewRenderer.Vo(model,table,page));
        result.AppendLine("    /// <summary>固定SQL结果不扩充本页授权字段，驱动值按生成类型投影。</summary>").AppendLine($"    public static {page.Name}Vo FromReadRow(IReadOnlyDictionary<string,object?> row)=>new()").AppendLine("    {");
        foreach(var output in plan.Outputs)
        {
            var column=page.Readable.SingleOrDefault(column=>column.Source.ColumnName==output.Storage);
            var value=$"NonRedundantReads.Value<{output.Type}>(row,{Literal(output.Storage)})";
            if (output.IsJsonArray)
                value = column?.IsLegacyMedia == true ? $"Metarill.Core.Contracts.LegacyMediaValue.Project({value})"
                    : column is not null && DatabaseCheckboxCompilation.Generic(table,column) ? $"Metarill.Core.Contracts.DatabaseCheckboxValue.ProjectIds({value})"
                    : $"ReadFileValue(row,{Literal(output.Storage)})";
            result.AppendLine($"        {output.Property}={value},");
        }
        result.Append(NonRedundantCrossTableCompilation.VoAssignments(model,table,page));
        foreach (var column in constants)
        {
            var value = $"NonRedundantReads.Value<{column.Type}>(row,{Literal(column.Source.ColumnName)})";
            result.AppendLine($"        {column.Property}Show={ConstantDisplayExpression(column, value)},");
        }
        result.AppendLine("    };");
        if(plan.Outputs.Any(output=>output.IsJsonArray))result.AppendLine("    /// <summary>沿原VO恢复数据库JSON文本，null保持空值，避免二次编码文件ID数组。</summary>")
            .AppendLine("    private static JsonElement? ReadFileValue(IReadOnlyDictionary<string,object?> row,string storage)")
            .AppendLine("    {").AppendLine("        var json=NonRedundantReads.Value<string?>(row,storage);")
            .AppendLine("        return string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<JsonElement>(json);").AppendLine("    }");
        return result.AppendLine("}").ToString();
    }
    /// <summary>候选分页、统计和已选回读共享同一实际页读取计划及账号绑定。</summary>
    internal static string NonRedundantListReadPlan(GenerationModel model,TableModel table,PageModel page,NonRedundantPagePlan plan)
    {
        if(!NonRedundantReadCompilation.GlobalSelectionPage(table,page))
            return NonRedundantCrossTableCompilation.BoundPlan(model,table,page,"ReadPlan"+page.Source.Id,
                singleTargets:plan.SingleTargets.Count>0||plan.RequiresAccount);
        var bindAccount=plan.RequiresAccount||plan.SingleTargets.Count>0||CrossTableWriteCompilation.ForService(model,table,page).Count>0||
            FixedPageConditionCompilation.HasSelfTreeFields(page)||FixedPageConditionCompilation.HasAccountVariables(page)||
            NonRedundantReadCompilation.SelfTreeNameQueries(model,table,page).Any(tree=>FixedPageConditionCompilation.HasAccountVariables(tree.TargetPage));
        return "CandidateReadPlan"+page.Source.Id+(bindAccount?".BindAccount(actor)":"");
    }

    /// <summary>普通父范围沿物理键，稀疏跨表父范围沿固定逻辑链；公开读取与私有当前锁分开。</summary>
    private static string NonRedundantQuery(GenerationModel model,TableModel table,PageModel page)
    {
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);var suffix=page.Source.Id;var fixedQuery=FixedPageQueryCompilation.For(table,page);
        var globalParent=MultiGlobalParentCompilation.PhysicalScopeTarget(model,table,page) is not null;
        var snapshot=globalParent||CascadeTreeQueryCompilation.Snapshot(model,table,page);
        string ParentCheck(string key,string readLock="false")=>SelectionPageRenderer.Enabled(page)
            ?$"await ValidateSelectionParents{suffix}Async({key},actor,cancellationToken);"
            :globalParent?$"await ValidateParents{suffix}Async({key},actor,cancellationToken,locking:{readLock});":"";
        var globalCandidate=NonRedundantReadCompilation.GlobalSelectionPage(table,page);
        var readPlan=NonRedundantCrossTableCompilation.BoundPlan(model,table,page,"ReadPlan"+suffix,singleTargets:plan.SingleTargets.Count>0||plan.RequiresAccount);
        var crossCandidate=globalCandidate&&CrossTableWriteCompilation.ForService(model,table,page).Count>0;
        var listPlan=globalCandidate?NonRedundantListReadPlan(model,table,page,plan):readPlan;
        var listCondition=globalCandidate?"":FixedPageConditionCompilation.ReadArgument(page);
        var globalExact=GlobalCrossTableTarget.DeclaresExactPage(table.Source,page.Source);
        var detailUsesCandidate=globalExact||NonRedundantReadCompilation.GlobalSingleParentPage(table,page);
        var logicalCondition=FixedPageConditionCompilation.HasLogicalFields(page);
        var lockScope=NonRedundantCrossParentCompilation.Enabled(model,table,page)||logicalCondition;
        // 普通写范围默认维持真实事务锁；Agent原记录预检显式选择无锁派生匹配。
        var derivedExact=ExactDataManageCompilation.Resolve(model,table,page) is {Derived:true};
        var names=SelectionNameFilterCompilation.ForPage(model,table,page);var nameKeys=names.ConsumedKeys.Concat(names.Gaps.Select(gap=>gap.FilterKey)).ToHashSet(StringComparer.Ordinal);
        var outputs="new string[]{"+string.Join(",",plan.ReadOutputs.Select(Literal))+"}";var dependencies="new string[]{"+string.Join(",",plan.Dependencies.Select(Literal))+"}";
        var result=new StringBuilder().Append(FixedPageConditionCompilation.Declaration(page,"PageCondition"+suffix)).Append(NonRedundantCrossParentCompilation.Service(model,table,page));
        if(ExactDataManageCompilation.Resolve(model,table,page) is {Derived:true} exact)
            result.AppendLine("    /// <summary>匹配计划只含当前范围所需路径，避免锁住无关输出关系。</summary>")
                .AppendLine($"    internal static readonly NonRedundantReadPlan ExactReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{ExactDataManageCompilation.MatchPlanCall(table,page,exact)};");
        if(logicalCondition)
            result.AppendLine("    /// <summary>当前对象只锁定页面条件依赖的路径，不读取其它显示列。</summary>")
                .AppendLine($"    private static readonly NonRedundantReadPlan CrossConditionPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{(CrossTableWriteCompilation.ForService(model,table,page).Count>0?"CompileCross":"Compile")}({Literal(table.Source.Id)},new string[]{{\"id\"}},new string[]{{{string.Join(",",page.Condition!.Syntax.Fields.Select(Literal))}}}{NonRedundantCrossTableCompilation.BindingArgument(model,table,page)});");
        if(globalCandidate)
        {
            var compiler=globalExact?"CompileGlobalExactSingleCandidate":
                detailUsesCandidate?"CompileGlobalSingleParentPage":
                NonRedundantReadCompilation.GlobalAccountMultiPage(table,page)?"CompileGlobalAccountMultiPage":
                crossCandidate||FixedPageConditionCompilation.HasSelfTreeFields(page)?"CompileCrossPageCandidate":
                CascadeTreeReadCompilation.ForService(model,table,page).Count>0?"CompileGlobalPage":"CompileGlobalCandidate";
            var bindings=crossCandidate?NonRedundantCrossTableCompilation.BindingArgument(
                CrossTableWriteCompilation.ForService(model,table,page),globalExact?"bindings":null):"";
            var singles=NonRedundantReadCompilation.SingleTargetArgument(plan.SingleTargets,"singleTargets");
            result.AppendLine($"    private static readonly NonRedundantReadPlan CandidateReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{compiler}({Literal(table.Source.Id)},{Literal(suffix)},{outputs},{dependencies}{bindings}{singles});");
        }
        result.AppendLine("    /// <summary>生成校验和宿主静态初始化分别编译；普通请求不解释元数据。</summary>")
            .AppendLine($"    private static readonly NonRedundantReadPlan ReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{NonRedundantReadCompilation.PagePlanCall(model,table,page,plan,outputs,dependencies)};")
            .AppendLine(NonRedundantCrossParentCompilation.Enabled(model,table,page)?"    /// <summary>更新前按真实项目及物理／逻辑父范围锁定主记录。</summary>":"    /// <summary>更新前仅按真实物理项目/父范围锁定主记录。</summary>").AppendLine($"    private ISelect<{table.EntityType}> Scope{suffix}(RuntimeActor actor,string? parentId{ExactDataManageCompilation.Parameter(page,"detail")}{(lockScope?",bool locking=false":"")}{(derivedExact?",bool exactLocking=true":"")})").AppendLine("    {").AppendLine("        ValidateActor(actor);var query=mapper.Query();");
        var fixedTextParent=UsesFixedTextParent(model,table,page);
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {
            if(source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,table,page,column))continue;
            if(source=="parent"&&fixedTextParent&&column.Type=="string?")
                result.AppendLine($"        query=NonRedundantReads.ApplyTextParent(query,session,{Literal(column.Source.ColumnName)},parentId!);");
            else
            {
                if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");
                result.AppendLine($"        query=query.Where(entity=>entity.{column.Property}=={ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")});");
            }
        }
        if(NonRedundantCrossParentCompilation.Enabled(model,table,page))result.AppendLine($"        query=LogicalParentScope{suffix}(query,actor,parentId,locking);");
        result.Append(FixedPageConditionCompilation.Apply(page,"PageCondition"+suffix,"page_"+suffix,NonRedundantCrossTableCompilation.BoundPlan(model,table,page,"CrossConditionPlan"+suffix),"locking")).Append(ExactDataManageCompilation.Query(model,table,page,locking:derivedExact?"exactLocking":null)).AppendLine("        return query;").AppendLine("    }").AppendLine("    /// <summary>身份与父范围是服务附加的AND条件，请求筛选不能覆盖。</summary>").AppendLine($"    private {(ExactDataManageCompilation.Declared(page)?"":"static ")}List<FixedReadFilter> ReadFilters{suffix}(PageRequest request,RuntimeActor actor{ExactDataManageCompilation.Parameter(page,"list")}{(globalCandidate?",bool globalCandidate=false":"")})").AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);ValidateActor(actor);var result=new List<FixedReadFilter>();");
        if(NonRedundantCrossParentCompilation.Enabled(model,table,page))result.AppendLine($"        _=LogicalParentId{suffix}(request.ParentId);");
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {
            if(source=="parent"&&fixedTextParent&&column.Type=="string?")
                result.AppendLine($"        result.Add(FixedReadFilter.ForTextParent({Literal(column.Source.ColumnName)},request.ParentId!));");
            else
            {
                if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(request.ParentId))throw new ArgumentException(\"当前页面要求parentId。\");");
                result.AppendLine($"        {(globalCandidate&&source=="project"?"if(!globalCandidate)":"")}result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{ScopeValue(column,source=="project"?"actor.ProjectId":"request.ParentId!")}));");
            }
        }
        result.Append(ExactDataManageCompilation.ReadFilter(model,table,page));
        result.AppendLine("        foreach(var filter in request.Filters??throw new ArgumentException(\"filters不能为空。\"))").AppendLine("        {").AppendLine("            switch(filter.Key)").AppendLine("            {");
        // 名称辅助键在SQL分页/统计之前解析；不能把IsIgnore实体列当持久化列查询。
        foreach(var binding in names.Bindings)
        {
            var comparison = binding.Kind switch
            {
                SelectionNameFilterKind.JoinedScalarLike => "Contains",
                SelectionNameFilterKind.JoinedIntegerLike => "IntegerLabelContains",
                SelectionNameFilterKind.JoinedTypedLike => "TypedLabelContains",
                SelectionNameFilterKind.JoinedJsonArrayAnyLike => "JsonArrayAnyContains",
                _ => throw new InvalidOperationException("非冗余名称筛选缺少固定联接证明。")
            };
            result.AppendLine($"                case {Literal(binding.FilterKey)}:")
                .AppendLine("                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString()))break;")
                .AppendLine("                    if(filter.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException(\"名称筛选只接受文本。\");")
                .AppendLine($"                    result.Add(new({Literal(binding.StorageColumn.Source.ColumnName)},FixedReadComparison.{comparison},filter.Value.GetString()!));break;");
        }
        foreach(var group in names.Gaps.GroupBy(gap=>gap.FilterKey,StringComparer.Ordinal))
            result.AppendLine($"                case {Literal(group.Key)}:")
                .AppendLine("                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString()))break;")
                .AppendLine($"                    throw new ArgumentException({Literal(string.Join("；",group.Select(gap=>gap.Reason).Distinct(StringComparer.Ordinal)))});");
        foreach(var column in page.Searchable.Where(column=>!nameKeys.Contains(column.JsonName)))
        {
            var like=TextPatternColumn(model,page,column);
            var blankCheck=like&&!column.IsManagedMultiNames&&!column.IsJsonArray?"Metarill.Data.Queries.StringNameQuery.IsPatternBlank":"string.IsNullOrWhiteSpace";
            result.AppendLine($"                case {Literal(column.JsonName)}:").AppendLine("                {").AppendLine($"                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&{blankCheck}(filter.Value.GetString())||filter.Value.ValueKind==JsonValueKind.Array&&filter.Value.GetArrayLength()==0)break;");
            var multiIdCheckbox = ControlRenderer.UsesMultiIdCheckboxFilter(column);
            if (table.Source.IsRongyu == 0 && multiIdCheckbox)
                result.AppendLine("                    var selectedIds = Metarill.Core.Contracts.DatabaseCheckboxValue.ReadSelected(filter.Value);")
                    .AppendLine($"                    if (selectedIds.Length > 0) result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.JsonArrayHasAnyId,selectedIds));")
                    .AppendLine("                    break;");
            else if(DatabaseCheckboxCompilation.Generic(table,column)&&!multiIdCheckbox)result.AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.JsonArrayHasId,Metarill.Core.Contracts.DatabaseCheckboxValue.ReadId(filter.Value)));break;");
            else if(column.IsManagedMultiNames&&DatabaseCheckboxCompilation.ForService(model,table,page).Any(def=>def.Names?.Source.Id==column.Source.Id))result.AppendLine("                    if(filter.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException(\"复选名称筛选只接受文本。\");").AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.JsonArrayAnyContains,filter.Value.GetString()!));break;");
            else if(column.Source.ControlKind=="CascadeTree")result.AppendLine("                    break;");
            else if (column.Type == "DateTime?")
            {
                var dateArray = ControlRenderer.UsesScalarCheckboxFilter(column)
                    ? $"result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.In,{CheckboxDateValues}.Cast<object?>().ToArray()));"
                    : $"var range=PageDateRange.Parse(filter.Value);result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.GreaterOrEqual,range.Start));result.Add(new({Literal(column.Source.ColumnName)},range.EndExclusive?FixedReadComparison.Less:FixedReadComparison.LessOrEqual,range.End));";
                result.AppendLine($"                    if(filter.Value.ValueKind==JsonValueKind.Array){{{dateArray}break;}}")
                    .AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,DateTime.Parse(filter.Value.GetString()!,CultureInfo.InvariantCulture)));break;");
            }
            else
            {
                // 原数字筛选框提交文本；整数按精确值解析，保持选择弹层与普通列表的请求形状。
                var parse = column.Type switch
                {
                    "TimeSpan?" => $"{TimeFieldCompilation.Value(column)}.Parse(filter.Value)",
                    "int?" when !UsesBooleanOptionWireFormat(column) => "filter.Value.ValueKind==JsonValueKind.String?int.Parse(filter.Value.GetString()!,NumberStyles.Integer,CultureInfo.InvariantCulture):filter.Value.GetInt32()",
                    _ => CheckboxFilterBoolean(model, page, column) ? "CheckboxBooleanJsonConverter.Parse(filter.Value)" : $"JsonSerializer.Deserialize<{column.Type}>(filter.Value.GetRawText(),JsonOptions)"
                };
                var parseArray=column.Type=="TimeSpan?"?$"filter.Value.EnumerateArray().Select({TimeFieldCompilation.Value(column)}.Parse).Cast<object?>().ToArray()":CheckboxFilterBoolean(model, page, column)?"filter.Value.EnumerateArray().Select(CheckboxBooleanJsonConverter.Parse).Cast<object?>().ToArray()":$"JsonSerializer.Deserialize<{column.Type}[]>(filter.Value.GetRawText(),JsonOptions)!.Cast<object?>().ToArray()";
                result.AppendLine($"                    if(filter.Value.ValueKind==JsonValueKind.Array){{result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.In,{parseArray}));break;}}")
                    .AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.{(like?"LikePattern":"Equal")},{parse}));break;");
            }
            result.AppendLine("                }");
        }
        result.AppendLine("                default:throw new ArgumentException(\"页面未开放该筛选字段：\"+filter.Key);").AppendLine("            }").AppendLine("        }").AppendLine("        if(request.SelectedIds is null)throw new ArgumentException(\"selectedIds不能为空。\");")
            .AppendLine(SelectionPageRenderer.Enabled(page)?"        if(request.SelectedIds.Count>200||request.PageNo>1000000)throw new ArgumentException(\"多选页最多200个已选项，页码最多1000000。\");":"")
            .AppendLine("        if(request.SelectedIds.Count>0)result.Add(new(\"id\",FixedReadComparison.In,request.SelectedIds.Select(value=>(object?)long.Parse(value,CultureInfo.InvariantCulture)).ToArray()));")
            .AppendLine("        return result;").AppendLine("    }").AppendLine("    /// <summary>请求排序键仍由原页面开放列控制，默认排序保持元数据序列。</summary>").AppendLine($"    private static IReadOnlyList<FixedReadOrder> ReadOrder{suffix}(PageRequest request)").AppendLine("    {")
            .AppendLine("        if(request.OrderBy is not null&&!string.IsNullOrWhiteSpace(request.OrderBy.Order))").AppendLine("        {").AppendLine("            var descending=request.OrderBy.Order switch{\"descending\"=>true,\"ascending\"=>false,_=>throw new ArgumentException(\"排序方向无效。\")};")
            .AppendLine("            return (request.OrderBy.Column??request.OrderBy.Field) switch").AppendLine("            {");
        foreach(var column in page.Sortable)result.AppendLine($"                {Literal(column.JsonName)}=>new FixedReadOrder[]{{new({Literal(column.Source.ColumnName)},descending)}},");
        result.AppendLine("                _=>throw new ArgumentException(\"页面未开放该排序字段。\")").AppendLine("            };").AppendLine("        }")
            .AppendLine("        return new FixedReadOrder[]{"+string.Join(",",fixedQuery.DefaultOrder.Select(order=>$"new({Literal(order.Column.Source.ColumnName)},{order.Descending.ToString().ToLowerInvariant()})"))+"};").AppendLine("    }")
            .AppendLine("    /// <summary>固定SQL完成范围/筛选/排序/count/分页，再投影原VO。</summary>").AppendLine($"    public {(snapshot?"":"async ")}Task<PageResult<{page.Name}Vo>> Page{suffix}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken){(snapshot?$"=>{GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync<PageResult<{page.Name}Vo>>(async()=>":"")}").AppendLine("    {")
            .AppendLine("        if(request.PageNo<1||request.PageSize<0||request.PageSize>1000)throw new ArgumentException(\"分页参数无效，每页最多1000条。\");")
            .AppendLine($"        var unpaged=request.PageSize==0&&{(page.Source.PageSize==0).ToString().ToLowerInvariant()};")
            .AppendLine("        if(request.PageSize==0&&(!unpaged||request.PageNo!=1))throw new ArgumentException(\"仅已配置不分页的页面允许pageSize=0，且页码必须为1。\");")
            .AppendLine("        "+ParentCheck("request.ParentId"))
            .AppendLine($"        var rows=await new NonRedundantReader(session).PageAsync({listPlan},{NonRedundantFilterCall(model,table,page,globalCandidate:globalCandidate)},ReadOrder{suffix}(request),request.PageNo,request.PageSize,cancellationToken,unpaged,actor.ProjectId{listCondition});")
            .AppendLine($"        return new({NonRedundantProjectionCall(model,table,page,"rows.Records","false")},rows.Total,unpaged?0:(rows.Total+request.PageSize-1)/request.PageSize,request.PageNo,request.PageSize);").AppendLine(snapshot?"    },cancellationToken);":"    }")
            .AppendLine("    /// <summary>数量不读取明细，不依赖请求分页或排序。</summary>").AppendLine($"    public {(snapshot?"":"async ")}Task<long> Count{suffix}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken){(snapshot?$"=>{GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync<long>(async()=>":"")} {{ {ParentCheck("request.ParentId")} return (await new NonRedundantReader(session).StatisticsAsync({listPlan},{NonRedundantFilterCall(model,table,page,globalCandidate:globalCandidate)},Array.Empty<string>(),cancellationToken,actor.ProjectId{listCondition})).Total; }}{(snapshot?",cancellationToken);":"")}")
            .AppendLine("    /// <summary>详情及写后回显使用相同真实项目/父范围，不额外授予list权限。</summary>").AppendLine($"    public Task<{page.Name}Vo?> Detail{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken)=>{(snapshot?$"{GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync(()=>":"")}ReadDetail{suffix}Async(id,parentId,actor,cancellationToken,false){(snapshot?",cancellationToken)":"")};")
            .AppendLine("    /// <summary>私有写后当前读不暴露HTTP开关，防止旧事务快照覆盖已经验证的名称。</summary>")
            .AppendLine($"    private async Task<{page.Name}Vo?> ReadDetail{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken,bool currentRead)").AppendLine("    {")
            .AppendLine("        "+ParentCheck("parentId","currentRead"))
            .AppendLine($"        var filters=ReadFilters{suffix}(new PageRequest{{ParentId=parentId}},actor{ExactDataManageCompilation.Argument(page,"\"detail\"")}{(detailUsesCandidate?",globalCandidate:true":"")});")
            .AppendLine($"        var rows=await new NonRedundantReader(session).KeyRowsAsync({(detailUsesCandidate?listPlan:readPlan)},filters,[id],cancellationToken,projectId:actor.ProjectId{(detailUsesCandidate?"":FixedPageConditionCompilation.ReadArgument(page,true))},currentRead:currentRead);")
            .AppendLine($"        return rows.Count==0?null:{NonRedundantProjectionCall(model,table,page,"rows","currentRead",true)};").AppendLine("    }");
        if(ExactDataManageCompilation.Declared(page)||page.Condition is not null)
        {
            result.AppendLine("    /// <summary>只供已授权且已写入的对象回显；固定主键/项目/父与实际事务，不能由HTTP选择此入口。</summary>")
                .AppendLine($"    private async Task<{page.Name}Vo?> MutationResult{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine("        ValidateActor(actor);if(session.CurrentTransaction is null)throw new InvalidOperationException(\"写后回显必须使用实际写入事务。\");")
                .AppendLine("        var filters=new List<FixedReadFilter>();");
            foreach(var (column,source) in fixedQuery.IdentityScopes)
            {
                // 写后回显的窄查询也要核对已发布的文本父键，不能借排序规则命中近似记录。
                if(source=="parent"&&fixedTextParent&&column.Type=="string?")
                {
                    result.AppendLine($"        filters.Add(FixedReadFilter.ForTextParent({Literal(column.Source.ColumnName)},parentId!));");
                    continue;
                }
                if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");
                result.AppendLine($"        filters.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")}));");
            }
            result.AppendLine($"        var rows=await new NonRedundantReader(session).KeyRowsAsync({readPlan},filters,[id],cancellationToken,projectId:actor.ProjectId,currentRead:true);")
                .AppendLine($"        return rows.Count==0?null:{NonRedundantProjectionCall(model,table,page,"rows","true",true)};").AppendLine("    }");
        }
        result.Append(NonRedundantCascadeFilters(model,table,page)).Append(NonRedundantCascadeProjection(model,table,page));
        return result.ToString();
    }
    /// <summary>原map.statistics及百分比口径保持，统计谓词使用未分页的同一读取范围。</summary>
    internal static string NonRedundantStatistics(GenerationModel model,TableModel table,PageModel page)
    {
        var definitions=OriginalListStatisticsCompilation.ForPage(model,table,page);if(definitions is null)return "";
        var suffix=page.Source.Id;
        var globalCandidate=NonRedundantReadCompilation.GlobalSelectionPage(table,page);
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);
        var listPlan=NonRedundantListReadPlan(model,table,page,plan);
        var listCondition=globalCandidate?"":FixedPageConditionCompilation.ReadArgument(page);
        var result=new StringBuilder().AppendLine("    /// <summary>派生值在SQL完整范围内聚合，当前页统计仍折叠原VO。</summary>").AppendLine($"    internal {(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"Task":"async Task")}<Dictionary<string,object?>> Statistics{suffix}Async(PageRequest request,RuntimeActor actor,PageResult<{page.Name}Vo> page,CancellationToken cancellationToken){(CascadeTreeQueryCompilation.Snapshot(model,table,page)?$"=>{GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync<Dictionary<string,object?>>(async()=>":"")}").AppendLine("    {").AppendLine("        var statistics=new Dictionary<string,object?>(StringComparer.Ordinal);");
        if(definitions.Count==0)result.AppendLine("        await Task.CompletedTask;");
        var sums=definitions.Where(definition=>definition.Kind==1).ToArray();
        var batchSums=sums.Any(definition=>definition.Column.Type.TrimEnd('?')!="double");
        // 有精确数值时共用其基础聚合。仅有浮点时保留原分桶查询，避免额外扫描一次范围取计数。
        if(batchSums)
        {
            var sumFields=sums.Select(definition=>Literal(definition.Column.Source.ColumnName));
            result.AppendLine($"        var totals=await new NonRedundantReader(session).StatisticsAsync({listPlan},{NonRedundantFilterCall(model,table,page,globalCandidate:globalCandidate)},new string[]{{{string.Join(",",sumFields)}}},cancellationToken,actor.ProjectId{listCondition});");
        }
        foreach(var definition in definitions)
        {
            var column=definition.Column;var key=Literal(column.JsonName);var local="stat"+column.Property;
            if(definition.Kind==1&&column.Type.TrimEnd('?')=="double")
            {
                var field=Literal(column.Source.ColumnName);
                if(!batchSums)
                    result.AppendLine($"        var {local}=await new NonRedundantReader(session).StatisticsAsync({listPlan},{NonRedundantFilterCall(model,table,page,globalCandidate:globalCandidate)},new string[]{{{field}}},cancellationToken,actor.ProjectId{listCondition});");
                result.AppendLine($"        statistics[{key}]=new{{currentPage=LegacyStatisticsMath.SumDouble(page.Records.Select(row=>(double?)row.{column.Property})),total={(batchSums?"totals":local)}.DoubleSums[{field}]??0d}};");
            }
            else if(definition.Kind==1)
            {
                var current=column.Type.EndsWith('?')?"(decimal)(row."+column.Property+"??0)":"(decimal)row."+column.Property;
                result.AppendLine($"        statistics[{key}]=new{{currentPage=page.Records.Sum(row=>{current}),total=totals.Sums[{Literal(column.Source.ColumnName)}]??0m}};");
            }
            else
            {result.AppendLine($"        var {local}=new Dictionary<string,object?>();if(page.Records.Count>0){local}[\"currentPage\"]=LegacyStatisticsMath.Percentage(page.Records.LongCount(row=>row.{column.Property}==2),page.Records.Count);").AppendLine($"        if(page.Total>0){{var filters={NonRedundantFilterCall(model,table,page,globalCandidate:globalCandidate)};filters.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,2));var matches=await new NonRedundantReader(session).StatisticsAsync({listPlan},filters,Array.Empty<string>(),cancellationToken,actor.ProjectId{listCondition});{local}[\"total\"]=LegacyStatisticsMath.Percentage(matches.Total,page.Total);}}").AppendLine($"        statistics[{key}]={local};");}
        }
        return result.AppendLine("        return statistics;").AppendLine(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"    },cancellationToken);":"    }").ToString();
    }
}
