using Metarill.Core.Metadata;
using Metarill.Data.Relations;
namespace Metarill.Generation;

internal sealed record RelationPropagationBinding(TableModel Source,ColumnModel Foreign,TableModel Target,ColumnModel Value,ColumnModel Cache,bool Multiple=true,string? CrossTableColumnId=null,string? CrossTableLevelKey=null,string? CrossTablePageId=null);
/// <summary>下发已证明的普通多值及跨表级联物理名称依赖，平台固定配置仍由其专用协调器维护。</summary>
internal static class RelationPropagationCompilation
{
    /// <summary>来自同一写入证明的根／页关系；R0实时名称不物化，未证明用途不冒充已支持传播。</summary>
    private static IReadOnlyList<RelationPropagationBinding> AllBindings(GenerationModel model)
    {
        var result=new List<RelationPropagationBinding>();
        foreach(var table in model.Tables.Where(table=>table.Source.IsRongyu==1&&!table.IsGlobal))
        {
            foreach(var binding in MultiNameWriteCompilation.ForTable(model,table))result.Add(new(table,binding.Source,binding.Target,binding.Value,binding.Cache));
            foreach(var definition in table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForService(model,table,page)))result.Add(new(table,definition.Source,definition.Target,definition.Name,definition.Names));
            // 完整写入链定义各级ID／名称归属；不按字段后缀猜目标，也不接受页间冲突定义。
            foreach(var plan in table.Pages.SelectMany(page=>CrossTableWriteCompilation.ForService(model,table,page)))
                foreach(var level in plan.Levels)result.Add(new(table,level.Id,level.Read.Target,level.Read.Name,level.Name,false,plan.Read.Source.Source.Id,level.Read.Key,level.Read.Target.IsGlobal?level.Read.Page.Source.Id:null));
        }
        foreach(var group in result.GroupBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)))
            if(group.Select(binding=>(binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Multiple,binding.CrossTableColumnId,binding.CrossTableLevelKey,binding.CrossTablePageId)).Distinct().Count()!=1&&!group.All(binding=>SharedCrossTableCompilation.Equivalent(model,group.First(),binding)))throw new InvalidOperationException("同一名称缓存存在相互冲突的页面关系定义，未发布传播计划。");
        var bindings=result.GroupBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)).Select(group=>group.OrderByDescending(binding=>binding.CrossTableColumnId==binding.Foreign.Source.Id).ThenBy(binding=>binding.CrossTableColumnId,StringComparer.Ordinal).First()).OrderBy(binding=>binding.Source.Source.TableName,StringComparer.Ordinal).ThenBy(binding=>binding.Cache.Source.ColumnName,StringComparer.Ordinal).ToArray();
        var physical=bindings.Where(binding=>!Derived(model,binding)).ToArray();var derived=bindings.Where(binding=>Derived(model,binding)).ToArray();if(physical.Length>0)_=RelationMutationPlan.Compile(model.Catalog,Definitions(physical));if(derived.Length>0)DerivedCrossTableCaches.Validate(model.Catalog,Definitions(derived),Definitions(physical));return bindings;
    }
    /// <summary>物理执行器只收到物理值；R0逻辑名称必须由完整维护及混合队列执行。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> Bindings(GenerationModel model)=>AllBindings(model).Where(binding=>!Derived(model,binding)).ToArray();
    /// <summary>派生层继续绑定原控制列和层，不按名称后缀猜测目标。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> DerivedBindings(GenerationModel model)=>AllBindings(model).Where(binding=>Derived(model,binding)).ToArray();
    /// <summary>以逻辑存储计划判断，不把R0表的真实物理名称误作派生。</summary>
    private static bool Derived(GenerationModel model,RelationPropagationBinding binding)=>!NonRedundantReadCompilation.Physical(model,binding.Target,binding.Value);
    /// <summary>只有静态绑定的精确来源才发布传播能力，不按后缀扩大范围。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,ColumnModel source)=>Bindings(model).Concat(DerivedBindings(model)).Any(binding=>binding.Source.Source.Id==table.Source.Id&&binding.Foreign.Source.Id==source.Source.Id);
    /// <summary>稳定ID是编译器与数据层之间唯一的关系输入，不传SQL或客户端路径。</summary>
    internal static RelationCacheDefinition[] Definitions(IReadOnlyList<RelationPropagationBinding> bindings)=>bindings.Select(binding=>new RelationCacheDefinition(binding.Source.Source.Id,binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Cache.Source.Id,binding.Multiple){CrossTableColumnId=binding.CrossTableColumnId,CrossTableLevelKey=binding.CrossTableLevelKey,CrossTablePageId=binding.CrossTablePageId}).ToArray();
}
