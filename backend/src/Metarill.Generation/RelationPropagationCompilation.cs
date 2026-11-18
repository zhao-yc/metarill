using Metarill.Core.Metadata;
using Metarill.Data.Relations;
namespace Metarill.Generation;

internal sealed record RelationPropagationBinding(TableModel Source,ColumnModel Foreign,TableModel Target,ColumnModel Value,ColumnModel Cache,bool Multiple=true,string? CrossTableColumnId=null,string? CrossTableLevelKey=null,string? CrossTablePageId=null)
{
    internal IReadOnlyDictionary<string,string>? CrossTableGlobalPages{get;init;}
    internal string? SelfTreePageId{get;init;}
    internal string? CrossTableAccountPageId { get; init; }
    internal string? CrossTableRecursivePageId { get; init; }
}
/// <summary>下发已证明的普通多值及跨表级联物理名称依赖，平台固定配置仍由其专用协调器维护。</summary>
internal static class RelationPropagationCompilation
{
    /// <summary>来自同一写入证明的根／页关系；R0实时名称不物化，未证明用途不冒充已支持传播。</summary>
    private static IReadOnlyList<RelationPropagationBinding> AllBindings(GenerationModel model)
    {
        var result=new List<RelationPropagationBinding>();
        foreach(var table in model.Tables.Where(table=>table.Source.IsRongyu==1))
        {
            result.AddRange(SelfTreePropagationCompilation.ForTable(model,table));
            if(table.IsGlobal)continue; // 全局来源只接入已证明的自树定义。
            foreach(var binding in MultiNameWriteCompilation.ForTable(model,table))result.Add(new(table,binding.Source,binding.Target,binding.Value,binding.Cache));
            foreach(var definition in table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForService(model,table,page)))result.Add(new(table,definition.Source,definition.Target,definition.Name,definition.Names));
            // 完整写入链定义各级ID／名称归属；不按字段后缀猜目标，也不接受页间冲突定义。
            foreach(var page in table.Pages)foreach(var plan in CrossTableWriteCompilation.ForService(model,table,page))
            {
                var globalPages=plan.Read.Levels.Any(level=>level.Target.IsGlobal&&
                    (level.Target.Source.IsRongyu==0||FixedPageConditionCompilation.HasAccountVariables(level.Page)))||SharedCrossTableCompilation.ForSource(model,table,page,plan.Read.Source.Source.Id) is not null?GlobalPages(plan):null;
                foreach(var level in plan.Levels)
                {
                    var recursive=FixedPageConditionCompilation.HasCrossFields(level.Read.Page);
                    result.Add(new(table,level.Id,level.Read.Target,level.Read.Name,level.Name,false,plan.Read.Source.Source.Id,level.Read.Key,level.Read.Target.IsGlobal?level.Read.Page.Source.Id:null)
                    {
                        CrossTableGlobalPages=globalPages,
                        CrossTableAccountPageId=!recursive&&FixedPageConditionCompilation.HasAccountVariables(level.Read.Page)?level.Read.Page.Source.Id:null,
                        CrossTableRecursivePageId=recursive?level.Read.Page.Source.Id:null
                    });
                }
            }
        }
        foreach(var group in result.GroupBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)))
            if(group.Select(binding=>(binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Multiple,binding.CrossTableColumnId,binding.CrossTableLevelKey,binding.CrossTablePageId,binding.SelfTreePageId,binding.CrossTableAccountPageId,binding.CrossTableRecursivePageId,PagesIdentity(binding.CrossTableGlobalPages))).Distinct().Count()!=1&&!group.All(binding=>SharedCrossTableCompilation.Equivalent(model,group.First(),binding)))throw new InvalidOperationException("同一名称缓存存在相互冲突的页面关系定义，未发布传播计划。");
        var bindings=result.GroupBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)).Select(group=>group.OrderByDescending(binding=>binding.CrossTableColumnId==binding.Foreign.Source.Id).ThenBy(binding=>binding.CrossTableColumnId,StringComparer.Ordinal).First()).OrderBy(binding=>binding.Source.Source.TableName,StringComparer.Ordinal).ThenBy(binding=>binding.Cache.Source.ColumnName,StringComparer.Ordinal).ToArray();
        var physical=bindings.Where(binding=>!Derived(model,binding)).ToArray();var derived=bindings.Where(binding=>Derived(model,binding)).ToArray();if(physical.Length>0)_=RelationMutationPlan.Compile(model.Catalog,Definitions(physical));if(derived.Length>0)DerivedCrossTableCaches.Validate(model.Catalog,Definitions(derived),Definitions(physical));return bindings;
    }
    /// <summary>物理执行器只收到物理值；R0逻辑名称必须由完整维护及混合队列执行。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> Bindings(GenerationModel model)=>AllBindings(model).Where(binding=>!Derived(model,binding)).ToArray();
    /// <summary>派生层继续绑定原控制列和层，不按名称后缀猜测目标。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> DerivedBindings(GenerationModel model)=>AllBindings(model).Where(binding=>Derived(model,binding)).ToArray();
    /// <summary>以逻辑存储计划判断，不把R0表的真实物理名称误作派生。</summary>
    private static bool Derived(GenerationModel model,RelationPropagationBinding binding)=>
        binding.CrossTableAccountPageId is not null||binding.CrossTableRecursivePageId is not null||
        SelfTreeCache.UsesWholePath(model.Catalog,binding.Source.Source,binding.SelfTreePageId)||
        !NonRedundantReadCompilation.Physical(model,binding.Target,binding.Value)||
        binding.Multiple&&Metarill.Data.Queries.NonRedundantReads.HasScopedMultiPage(model.Catalog,binding.Target.Source,binding.Foreign.Source);
    /// <summary>只有静态绑定的精确来源才发布传播能力，不按后缀扩大范围。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,ColumnModel source)=>Bindings(model).Concat(DerivedBindings(model)).Any(binding=>binding.Source.Source.Id==table.Source.Id&&binding.Foreign.Source.Id==source.Source.Id);
    /// <summary>稳定ID是编译器与数据层之间唯一的关系输入，不传SQL或客户端路径。</summary>
    internal static RelationCacheDefinition[] Definitions(IReadOnlyList<RelationPropagationBinding> bindings)=>bindings.Select(binding=>new RelationCacheDefinition(binding.Source.Source.Id,binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Cache.Source.Id,binding.Multiple){CrossTableColumnId=binding.CrossTableColumnId,CrossTableLevelKey=binding.CrossTableLevelKey,CrossTablePageId=binding.CrossTablePageId,CrossTableGlobalPages=binding.CrossTableGlobalPages,SelfTreePageId=binding.SelfTreePageId,CrossTableAccountPageId=binding.CrossTableAccountPageId,CrossTableRecursivePageId=binding.CrossTableRecursivePageId}).ToArray();
    /// <summary>派生级联或共享控制列携带完整全局页映射，供独立维护编译验证同一层链。</summary>
    internal static IReadOnlyDictionary<string,string>? GlobalPages(CrossTableWriteModel plan)
    {
        var levels=plan.Read.Levels.Where(level=>level.Target.IsGlobal).ToArray();return levels.Length==0?null:levels.ToDictionary(level=>level.Key,level=>level.Page.Source.Id,StringComparer.Ordinal);
    }
    /// <summary>只为实际消费的全局级联页发布常量条件，未使用的页面不参与缓存计划。</summary>
    internal static IReadOnlySet<string> PublishedCascadePages(GenerationModel model,IEnumerable<RelationCacheDefinition> definitions)
    {
        var r0Tables=model.Catalog.ConfigTables.Where(table=>table.IsRongyu==0).Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        var r0Pages=model.Catalog.ConfigPages.Where(page=>r0Tables.Contains(page.ConfigTableId)).Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        var integerColumns=model.Catalog.ConfigColumns.Where(column=>column.LogicalType is "int32" or "int64").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
        var pages=new HashSet<string>(StringComparer.Ordinal);
        foreach(var definition in definitions)
        {
            if(definition.SelfTreePageId is{} treePage)pages.Add(treePage);
            var scopedTree=SelfTreeCache.HasScopedPage(model.Catalog,definition.SelfTreePageId)?definition.SelfTreePageId:null;
            if((definition.CrossTableAccountPageId??definition.CrossTableRecursivePageId??scopedTree) is{} accountPage)
            {
                pages.Add(accountPage);
                pages.UnionWith(model.Catalog.ConfigPages.Where(page=>page.ConfigTableId==definition.SourceTableId).Select(page=>page.Id));
            }
            if(definition.CrossTablePageId is{} crossPage&&(r0Pages.Contains(crossPage)||integerColumns.Contains(definition.ValueColumnId)))pages.Add(crossPage);
            if(definition.CrossTableGlobalPages is{} mapping)pages.UnionWith(mapping.Values.Where(r0Pages.Contains));
        }
        return pages;
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
            foreach(var dependency in CrossTableWriteCompilation.ForService(model,root.Table,root.Page).SelectMany(path=>path.Read.Levels))
            {
                tables.Add(dependency.Target.Source.Id);
                if(FixedPageConditionCompilation.HasCrossFields(dependency.Page))
                    pending.Enqueue((dependency.Target,dependency.Page));
            }
        }
        return tables;
    }
    /// <summary>精简维护目录与生成校验使用同一页范围，宿主可重新证明递归依赖。</summary>
    internal static MetadataCatalog PublishedMaintenanceMetadata(GenerationModel model,IReadOnlyList<RelationCacheDefinition> definitions)
    {
        var recursive=definitions.Select(item=>item.CrossTableRecursivePageId??
            (SelfTreeCache.HasScopedPage(model.Catalog,item.SelfTreePageId)?item.SelfTreePageId:null)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var roots=model.Tables.SelectMany(table=>table.Pages.Where(page=>recursive.Contains(page.Source.Id)).Select(page=>(table,page)));
        var treePages=definitions.Where(item=>item.SelfTreePageId is not null&&SelfTreeCache.UsesWholePath(model.Catalog,
            model.Catalog.ConfigTables.Single(table=>table.Id==item.SourceTableId),item.SelfTreePageId)).Select(item=>item.SelfTreePageId!);
        return GenerationMetadataPublisher.ForRelations(model.Catalog,PublishedCascadePages(model,definitions),
            selfTreeNamePages:treePages,nestedTables:PublishedRecursiveTables(model,roots));
    }

    /// <summary>映射按稳定键比较，不把字典引用或插入顺序当作不同权限范围。</summary>
    internal static string PagesIdentity(IReadOnlyDictionary<string,string>? pages)=>pages is null?"":System.Text.Json.JsonSerializer.Serialize(pages.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToArray());

}
