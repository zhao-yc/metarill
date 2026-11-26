using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
namespace Metarill.Generation;

internal sealed record RelationPropagationBinding(TableModel Source,ColumnModel Foreign,TableModel Target,ColumnModel Value,ColumnModel Cache,bool Multiple=true,string? CrossTableColumnId=null,string? CrossTableLevelKey=null,string? CrossTablePageId=null)
{
    internal IReadOnlyDictionary<string,string>? CrossTableGlobalPages{get;init;}
    internal string? SelfTreePageId{get;init;}
    internal string? CrossTableAccountPageId { get; init; }
    internal string? CrossTableRecursivePageId { get; init; }
    internal string? ScopedSinglePageId { get; init; }
    internal string? CheckboxTargetPageId { get; init; }
    internal string? CheckboxSourceParentId { get; init; }
    internal string? TargetKey { get; init; }
}
/// <summary>下发已证明的普通多值及跨表级联物理名称依赖，平台固定配置仍由其专用协调器维护。</summary>
internal static class RelationPropagationCompilation
{
    /// <summary>来自同一写入证明的根／页关系；R0实时名称不物化，未证明用途不冒充已支持传播。</summary>
    private static IReadOnlyList<RelationPropagationBinding> AllBindings(GenerationModel model)
    {
        var result=new List<RelationPropagationBinding>();
        // 视图的投影由数据库维护，不能登记为需要回填写入的物理名称副本。
        foreach(var table in model.Tables.Where(table=>table.Source.IsRongyu==1&&table.Source.IsView==0&&table.Source.IsFunctionTable==0))
        {
            result.AddRange(SelfTreePropagationCompilation.ForTable(model,table));
            foreach(var binding in MultiNameWriteCompilation.ForTable(model,table))
                result.Add(new(table,binding.Source,binding.Target,binding.Value,binding.Cache));
            foreach(var binding in DatabaseCheckboxCompilation.Caches(model,table))
                result.Add(new(table,binding.Source,binding.Target,binding.Value,binding.Cache){CheckboxTargetPageId=binding.CheckboxTargetPageId,CheckboxSourceParentId=binding.CheckboxSourceParentId});
            if(table.IsGlobal)
            {
                // 原下拉和单选共用已发布的页范围绑定，其余全局角色沿各自已证明的合约。
                result.AddRange(GlobalCachedSourceCompilation.ForModel(model)
                    .Where(binding=>binding.Source.Source.Id==table.Source.Id&&binding.ScopedSingle&&binding.Cache is not null)
                    .Select(binding=>new RelationPropagationBinding(table,binding.Foreign,binding.Target,binding.Name,binding.Cache!,false)
                        {ScopedSinglePageId=binding.Page.Source.Id}));
                continue;
            }
            result.AddRange(ScopedSingleBindings(model,table));


            // 完整写入链定义各级ID／名称归属；不按字段后缀猜目标，也不接受页间冲突定义。
            foreach(var page in table.Pages)foreach(var plan in CrossTableWriteCompilation.ForService(model,table,page))
            {
                var globalPages=plan.Read.Levels.Any(level=>level.Target.IsGlobal&&
                    (level.Target.Source.IsRongyu==0||FixedPageConditionCompilation.HasAccountVariables(level.Page)))||SharedCrossTableCompilation.ForSource(model,table,page,plan.Read.Source.Source.Id) is not null?GlobalPages(plan):null;
                foreach(var level in plan.Levels)
                {
                    var recursive=FixedPageConditionCompilation.HasLogicalFields(level.Read.Page);
                    result.Add(new(table,level.Id,level.Read.Target,level.Read.Name,level.Name,false,plan.Read.Source.Source.Id,level.Read.Key,level.Read.Target.IsGlobal?level.Read.Page.Source.Id:null)
                    {
                        CrossTableGlobalPages=globalPages,
                        TargetKey=level.Read.AlternateKey?.Source.ColumnName,
                        CrossTableAccountPageId=!recursive&&FixedPageConditionCompilation.HasAccountVariables(level.Read.Page)?level.Read.Page.Source.Id:null,
                        CrossTableRecursivePageId=recursive?level.Read.Page.Source.Id:null
                    });
                }
            }
        }
        foreach(var group in result.GroupBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)))
            if(group.Select(binding=>(binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Multiple,binding.CrossTableColumnId,binding.CrossTableLevelKey,binding.CrossTablePageId,binding.SelfTreePageId,binding.CrossTableAccountPageId,binding.CrossTableRecursivePageId,binding.ScopedSinglePageId,binding.CheckboxTargetPageId,binding.CheckboxSourceParentId,binding.TargetKey,PagesIdentity(binding.CrossTableGlobalPages))).Distinct().Count()!=1&&!group.All(binding=>SharedCrossTableCompilation.Equivalent(model,group.First(),binding)))throw new InvalidOperationException("同一名称缓存存在相互冲突的页面关系定义，未发布传播计划。");
        var bindings=result.GroupBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)).Select(group=>group.OrderByDescending(binding=>binding.CrossTableColumnId==binding.Foreign.Source.Id).ThenBy(binding=>binding.CrossTableColumnId,StringComparer.Ordinal).First()).OrderBy(binding=>binding.Source.Source.TableName,StringComparer.Ordinal).ThenBy(binding=>binding.Cache.Source.ColumnName,StringComparer.Ordinal).ToArray();
        var physical=bindings.Where(binding=>!Derived(model,binding)).ToArray();var derived=bindings.Where(binding=>Derived(model,binding)).ToArray();if(physical.Length>0)_=RelationMutationPlan.Compile(model.Catalog,Definitions(physical));if(derived.Length>0)DerivedCrossTableCaches.Validate(model.Catalog,Definitions(derived),Definitions(physical));return bindings;
    }
    /// <summary>从原单值外键登记名称副本，由实际固定目标页决定逻辑路径和账号归属。</summary>
    private static IEnumerable<RelationPropagationBinding> ScopedSingleBindings(GenerationModel model,TableModel table)
    {
        foreach(var foreign in table.Columns.Where(column=>!column.IsSystem&&column.Source.ControlKind is "List" or "SelectPage"))
        {
            var target=ForeignValidationRenderer.Target(model,foreign);
            if(target is null)continue;
            var cache=SelectionDisplayFieldCompilation.Resolve(table,foreign);
            if(cache.Source.Id==foreign.Source.Id)continue;
            (PageMetadata Page,ColumnMetadata Name) proof;
            if(target.IsGlobal)
            {
                var global=GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,foreign.Source,target.Source);
                // 物理全局字典的单选及固定下拉使用同一发布范围和来源行项目回填。
                if(!global.RequiresScopedCache||target.Source.IsRongyu!=0&&!global.UsesListPageScope&&
                    !global.ScopedAccount&&!global.FixedSelectionPage&&!global.ScopedPhysicalSelectionParent)continue;
                proof=(global.Page,global.Name);
            }
            else if(ProjectScopedSingleRelationTarget.Resolve(model.Catalog,table.Source,foreign.Source,target.Source) is {} project)
                proof=project;
            else continue;
            var value=target.Columns.Single(column=>column.Source.Id==proof.Name.Id);
            yield return new(table,foreign,target,value,cache,false){ScopedSinglePageId=proof.Page.Id};
        }
    }

    /// <summary>物理执行器只收到物理值；R0逻辑名称必须由完整维护及混合队列执行。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> Bindings(GenerationModel model)=>AllBindings(model).Where(binding=>!Derived(model,binding)).ToArray();
    /// <summary>派生层继续绑定原控制列和层，不按名称后缀猜测目标。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> DerivedBindings(GenerationModel model)=>AllBindings(model).Where(binding=>Derived(model,binding)).ToArray();
    /// <summary>以逻辑存储计划判断，不把R0表的真实物理名称误作派生。</summary>
    private static bool Derived(GenerationModel model,RelationPropagationBinding binding)=>
        binding.CrossTableAccountPageId is not null||binding.CrossTableRecursivePageId is not null||binding.ScopedSinglePageId is not null||binding.CheckboxSourceParentId is not null||
        SelfTreeCache.UsesWholePath(model.Catalog,binding.Source.Source,binding.SelfTreePageId)||
        binding.CheckboxTargetPageId is {} checkboxPage&&model.Catalog.ConfigPages.SingleOrDefault(page=>page.Id==checkboxPage) is {} published&&
            DatabaseCheckboxDefinition.Parse(binding.Foreign.Source.ControlDetail)
                .AdditionalPhysicalFormConditions(model.Catalog,binding.Target.Source,published) is not null||
        !NonRedundantReadCompilation.Physical(model,binding.Target,binding.Value)||
        binding.Multiple&&Metarill.Data.Queries.NonRedundantReads.HasScopedMultiPage(model.Catalog,binding.Target.Source,binding.Foreign.Source);
    /// <summary>只有静态绑定的精确来源才发布传播能力，不按后缀扩大范围。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,ColumnModel source)=>Bindings(model).Concat(DerivedBindings(model)).Any(binding=>binding.Source.Source.Id==table.Source.Id&&binding.Foreign.Source.Id==source.Source.Id);
    /// <summary>稳定ID是编译器与数据层之间唯一的关系输入，不传SQL或客户端路径。</summary>
    internal static RelationCacheDefinition[] Definitions(IReadOnlyList<RelationPropagationBinding> bindings)=>bindings.Select(binding=>new RelationCacheDefinition(binding.Source.Source.Id,binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Cache.Source.Id,binding.Multiple){CrossTableColumnId=binding.CrossTableColumnId,CrossTableLevelKey=binding.CrossTableLevelKey,CrossTablePageId=binding.CrossTablePageId,CrossTableGlobalPages=binding.CrossTableGlobalPages,SelfTreePageId=binding.SelfTreePageId,CrossTableAccountPageId=binding.CrossTableAccountPageId,CrossTableRecursivePageId=binding.CrossTableRecursivePageId,ScopedSinglePageId=binding.ScopedSinglePageId,CheckboxTargetPageId=binding.CheckboxTargetPageId,CheckboxSourceParentId=binding.CheckboxSourceParentId}).ToArray();
    /// <summary>派生级联或共享控制列携带完整全局页映射，供独立维护编译验证同一层链。</summary>
    internal static IReadOnlyDictionary<string,string>? GlobalPages(CrossTableWriteModel plan)
    {
        var levels=plan.Read.Levels.Where(level=>level.Target.IsGlobal).ToArray();return levels.Length==0?null:levels.ToDictionary(level=>level.Key,level=>level.Page.Source.Id,StringComparer.Ordinal);
    }
    /// <summary>只为实际消费的全局级联页发布常量条件，未使用的页面不参与缓存计划。</summary>
    internal static IReadOnlySet<string> PublishedCascadePages(GenerationModel model,IEnumerable<RelationCacheDefinition> definitions,
        out IReadOnlySet<string> staticMultiPages)
    {
        var r0Tables=model.Catalog.ConfigTables.Where(table=>table.IsRongyu==0).Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        var r0Pages=model.Catalog.ConfigPages.Where(page=>r0Tables.Contains(page.ConfigTableId)).Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        var typedNameColumns=model.Catalog.ConfigColumns.Where(column=>MultiValueLabel.IsTyped(column.LogicalType)).Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
        var pages=new HashSet<string>(StringComparer.Ordinal);
        var staticPages=new HashSet<string>(StringComparer.Ordinal);
        staticMultiPages=staticPages;
        foreach(var definition in definitions)
        {
            if(definition.CheckboxTargetPageId is {} checkboxPage)pages.Add(checkboxPage);
            if(definition.Multiple&&model.Catalog.ConfigColumns.Single(column=>column.Id==definition.ForeignColumnId) is
                {ControlKind:"MultiCheckBoxDatabase" or "MultiSelectPage"} multi&&
                model.Catalog.ConfigTables.Single(table=>table.Id==definition.TargetTableId) is {} target&&GlobalCrossTableTarget.Declared(target))
            {
                // 物理传播和派生回填在宿主重新装载后仍从实际全局多值发布页读取条件。
                var source=model.Catalog.ConfigTables.Single(table=>table.Id==definition.SourceTableId);
                var proof=GlobalMultipleRelationTarget.RequireCached(model.Catalog,source,multi,target);
                pages.Add(proof.Page.Id);
                if(proof.UsesProjectPage||GlobalCrossTableTarget.Declared(source)&&proof.UsesStoredStaticPage)
                    staticPages.Add(proof.Page.Id);
                if(proof.UsesPhysicalParentPage||proof.UsesNonRedundantAccountPage||proof.UsesProjectPage&&target.IsRongyu==0&&FixedPageConditionCompilation.HasAccountVariables(
                    model.Tables.Single(table=>table.Source.Id==target.Id).Pages.Single(page=>page.Source.Id==proof.Page.Id)))
                {
                    // 精简维护目录须保留每个来源页的物理归属证明，宿主重启后也按实际来源行重算。
                    pages.UnionWith(model.Catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id).Select(page=>page.Id));
                }
            }
            if(definition.SelfTreePageId is{} treePage)
            {
                pages.Add(treePage);
                var selected=model.Tables.Single(table=>table.Source.Id==definition.TargetTableId).Pages.Single(page=>page.Source.Id==treePage);
                if(FixedPageConditionCompilation.HasAccountVariables(selected))
                    pages.UnionWith(model.Catalog.ConfigPages.Where(page=>page.ConfigTableId==definition.SourceTableId).Select(page=>page.Id));
            }
            var scopedTree=SelfTreeCache.HasScopedPage(model.Catalog,definition.SelfTreePageId)?definition.SelfTreePageId:null;
            if((definition.ScopedSinglePageId??definition.CrossTableAccountPageId??definition.CrossTableRecursivePageId??scopedTree) is{} accountPage)
            {
                pages.Add(accountPage);
                pages.UnionWith(model.Catalog.ConfigPages.Where(page=>page.ConfigTableId==definition.SourceTableId).Select(page=>page.Id));
            }
            if(definition.CrossTablePageId is{} crossPage&&(r0Pages.Contains(crossPage)||typedNameColumns.Contains(definition.ValueColumnId)))pages.Add(crossPage);
            if(definition.CrossTableGlobalPages is{} mapping)pages.UnionWith(mapping.Values.Where(r0Pages.Contains));
        }
        // 发布目录需能重新证明项目内来源使用的父角色，并在名称回填时执行实际全局父页条件。
        foreach(var binding in GlobalCachedSourceCompilation.ForModel(model))
        {
            var sourcePages=GlobalCachedRelationSource.RowProjectSelfTreeParentPages(model.Catalog,binding.Source.Source,binding.Foreign.Source);
            if(sourcePages.Count==0)continue;
            pages.UnionWith(sourcePages.Select(page=>page.Id));
            pages.Add(binding.Page.Source.Id);
        }
        return pages;
    }
    /// <summary>冻结复选名称回填所需的目标、备用名称及详情开放的物理父字段。</summary>
    internal static IReadOnlySet<(string Page,string Column)> PublishedCheckboxProofFields(GenerationModel model,
        IEnumerable<RelationCacheDefinition> definitions)
    {
        var fields=new HashSet<(string,string)>();
        foreach(var definition in definitions)
        {
            if(!definition.Multiple)continue;
            var source=model.Tables.Single(table=>table.Source.Id==definition.SourceTableId);
            var relation=source.Columns.Single(column=>column.Source.Id==definition.ForeignColumnId);
            if(relation.Source.ControlKind!="MultiCheckBoxDatabase")continue;
            var shared=definition.CheckboxTargetPageId is not null;
            if(definition.CheckboxTargetPageId is {} targetPage)fields.Add((targetPage,definition.ValueColumnId));
            foreach(var page in source.Pages)
            {
                if(shared&&!fields.Add((page.Source.Id,definition.ForeignColumnId)))continue;
                var checkbox=DatabaseCheckboxCompilation.Resolve(model,source,page,relation,forService:true).Definition;
                if(checkbox is {ProjectFormScope:true,Conditions:{} condition})
                    fields.UnionWith(condition.Forms.Select(binding=>(page.Source.Id,binding.Column.Source.Id)));
                if(!page.Scopes.Any(scope=>scope.Source=="parent")&&checkbox is {FormParentField:{} formParent}&&
                    (checkbox.HasProjectStoredAccountScope||checkbox.HasStoredGlobalParentScope||
                     shared&&(checkbox.SharedLogicalParentScope||checkbox.ProjectFormScope)))
                    fields.Add((page.Source.Id,formParent.Source.Id));
            }
        }
        return fields;
    }

    /// <summary>维护和生成读取目录保留实际递归候选涉及的页面、控件和可见性声明。</summary>
    internal static IReadOnlySet<string> PublishedRecursiveTables(GenerationModel model,IEnumerable<(TableModel Table,PageModel Page)> roots)
    {
        var pending=new Queue<(TableModel Table,PageModel Page)>(roots);
        var tables=new HashSet<string>(StringComparer.Ordinal);
        var visited=new HashSet<string>(StringComparer.Ordinal);
        while(pending.TryDequeue(out var root))
        {
            if(!visited.Add(root.Page.Source.Id))continue;
            tables.Add(root.Table.Source.Id);
            if((MultiGlobalParentCompilation.SingleTarget(model,root.Table,root.Page)??
                MultiGlobalParentCompilation.PhysicalPageParent(model,root.Table,root.Page)) is {} parent)
            {
                var parentTable=model.Tables.Single(table=>table.Source.Id==parent.Parent.Id);
                var parentPageId=Metarill.Data.Queries.NonRedundantReads.MultiGlobalParentPageId(model.Catalog,root.Table.Source.Id,root.Page.Source.Id);
                pending.Enqueue((parentTable,parentTable.Pages.Single(page=>page.Source.Id==parentPageId)));
            }
            if(FixedPageConditionCompilation.HasSelfTreeFields(root.Page))
                foreach(var tree in NonRedundantReadCompilation.SelfTreeNameQueries(model,root.Table,root.Page)
                    .Where(tree=>root.Page.Condition!.SelfTreeFields.Contains(CascadeTreeViewRenderer.Name(root.Table,tree.Source)!.Source.ColumnName)))
                    pending.Enqueue((tree.Target,tree.TargetPage));
            foreach(var dependency in CrossTableWriteCompilation.ForService(model,root.Table,root.Page).SelectMany(path=>path.Read.Levels))
            {
                tables.Add(dependency.Target.Source.Id);
                if(FixedPageConditionCompilation.HasLogicalFields(dependency.Page))
                    pending.Enqueue((dependency.Target,dependency.Page));
            }
        }
        return tables;
    }
    /// <summary>精简维护目录与生成校验使用同一页范围，宿主可重新证明递归依赖。</summary>
    internal static MetadataCatalog PublishedMaintenanceMetadata(GenerationModel model,IReadOnlyList<RelationCacheDefinition> definitions)
    {
        var recursive=definitions.Select(item=>item.ScopedSinglePageId??item.CrossTableRecursivePageId??
            (SelfTreeCache.HasScopedPage(model.Catalog,item.SelfTreePageId)?item.SelfTreePageId:null)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        // 文本父证明还会验证两端完整级联结构，父层之后的页面也须保留其原可见字段和范围。
        foreach(var binding in definitions.Where(item=>item.Multiple))
        {
            var target=model.Catalog.ConfigTables.Single(table=>table.Id==binding.TargetTableId);
            var relation=model.Catalog.ConfigColumns.Single(column=>column.Id==binding.ForeignColumnId);
            if(Metarill.Data.Queries.MySqlNonRedundantReadCompiler.StoredTextMultiTargetPage(model.Catalog,target,relation) is not {} page)
                continue;
            recursive.Add(page.Id);
            recursive.UnionWith(model.Catalog.ConfigPages.Where(source=>source.ConfigTableId==binding.SourceTableId).Select(source=>source.Id));
        }
        var roots=model.Tables.SelectMany(table=>table.Pages.Where(page=>recursive.Contains(page.Source.Id)).Select(page=>(table,page)));
        var treePages=definitions.Where(item=>item.SelfTreePageId is not null&&SelfTreeCache.UsesWholePath(model.Catalog,
            model.Catalog.ConfigTables.Single(table=>table.Id==item.SourceTableId),item.SelfTreePageId)).Select(item=>item.SelfTreePageId!);
        // 维护目录保留来源页的选择控件覆盖，重新验证副本的固定实际目标。
        var scopedTables=PublishedRecursiveTables(model,roots).ToHashSet(StringComparer.Ordinal);
        var scalarReferences=DerivedCrossTableCaches.ScopedSingleReferencedTables(model.Catalog,definitions);
        scopedTables.UnionWith(model.Tables.Where(table=>scalarReferences.Contains(table.Source.TableName)).Select(table=>table.Source.Id));
        scopedTables.UnionWith(definitions.Where(definition=>definition.ScopedSinglePageId is not null&&
            (model.Tables.Any(table=>table.Source.Id==definition.TargetTableId&&!table.IsGlobal)||
                model.Catalog.ConfigColumns.Any(column=>column.Id==definition.ForeignColumnId&&column.ControlKind=="List")))
            .Select(definition=>definition.SourceTableId));
        scopedTables.UnionWith(definitions.Where(definition=>definition.CheckboxSourceParentId is not null)
            .Select(definition=>definition.SourceTableId));
        var pages=PublishedCascadePages(model,definitions,out var staticMultiPages);
        return GenerationMetadataPublisher.ForRelations(model.Catalog,pages,
            selfTreeNamePages:treePages,nestedTables:scopedTables,checkboxProofFields:PublishedCheckboxProofFields(model,definitions),
            staticMultiPages:staticMultiPages);
    }

    /// <summary>映射按稳定键比较，不把字典引用或插入顺序当作不同权限范围。</summary>
    internal static string PagesIdentity(IReadOnlyDictionary<string,string>? pages)=>pages is null?"":System.Text.Json.JsonSerializer.Serialize(pages.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToArray());

}
