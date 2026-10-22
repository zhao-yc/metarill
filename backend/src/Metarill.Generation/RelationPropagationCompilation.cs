using Metarill.Core.Metadata;
using Metarill.Data.Relations;
namespace Metarill.Generation;

internal sealed record RelationPropagationBinding(TableModel Source,ColumnModel Foreign,TableModel Target,ColumnModel Value,ColumnModel Cache);
/// <summary>先下发已证明的普通多值物理名称依赖，平台固定配置仍由其专用协调器维护。</summary>
internal static class RelationPropagationCompilation
{
    /// <summary>来自同一写入证明的根／页关系；R0实时名称不物化，未知／全局用途不冒充已支持传播。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> Bindings(GenerationModel model)
    {
        var result=new List<RelationPropagationBinding>();
        foreach(var table in model.Tables.Where(table=>table.Source.IsRongyu==1&&!table.IsGlobal))
        {
            foreach(var binding in MultiNameWriteCompilation.ForTable(model,table).Where(binding=>!binding.Target.IsGlobal))result.Add(new(table,binding.Source,binding.Target,binding.Value,binding.Cache));
            foreach(var definition in table.Pages.SelectMany(page=>DatabaseCheckboxCompilation.ForService(model,table,page)).Where(definition=>!definition.Target.IsGlobal))result.Add(new(table,definition.Source,definition.Target,definition.Name,definition.Names));
        }
        var bindings=result.DistinctBy(binding=>(binding.Source.Source.Id,binding.Cache.Source.Id)).OrderBy(binding=>binding.Source.Source.TableName,StringComparer.Ordinal).ThenBy(binding=>binding.Cache.Source.ColumnName,StringComparer.Ordinal).ToArray();
        if(bindings.Length>0)_=RelationMutationPlan.Compile(model.Catalog,Definitions(bindings));return bindings;
    }
    /// <summary>只有静态绑定的精确来源才发布传播能力，不按后缀扩大范围。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,ColumnModel source)=>Bindings(model).Any(binding=>binding.Source.Source.Id==table.Source.Id&&binding.Foreign.Source.Id==source.Source.Id);
    /// <summary>稳定ID是编译器与数据层之间唯一的关系输入，不传SQL或客户端路径。</summary>
    internal static RelationCacheDefinition[] Definitions(IReadOnlyList<RelationPropagationBinding> bindings)=>bindings.Select(binding=>new RelationCacheDefinition(binding.Source.Source.Id,binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Cache.Source.Id,true)).ToArray();
}
