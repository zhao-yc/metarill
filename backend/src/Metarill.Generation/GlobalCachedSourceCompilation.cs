using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
namespace Metarill.Generation;

internal sealed record GlobalCachedSourceBinding(TableModel Source,ColumnModel Foreign,TableModel Target,PageModel Page,ColumnModel Name,ColumnModel Cache);

/// <summary>只接原全局R1物理List缓存；生成期一次证明，不在请求内解析元数据。</summary>
internal static class GlobalCachedSourceCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,IReadOnlyList<GlobalCachedSourceBinding>> Bindings=new();
    /// <summary>来源/目标/名称/副本必须同时成立；不从无项目字段猜权限，不悄悄舍弃缺失副本。</summary>
    internal static IReadOnlyList<GlobalCachedSourceBinding> ForModel(GenerationModel model)=>Bindings.GetValue(model,Compile);
    /// <summary>原List隐式名称属于响应字段，不成为额外页面控件或可写字段。</summary>
    internal static GenerationModel AddReadAliases(GenerationModel model)
    {
        var bindings=ForModel(model);if(bindings.Count==0)return model;
        return model with{Tables=model.Tables.Select(table=>table with{Pages=table.Pages.Select(page=>page with{Readable=page.Readable.Concat(bindings.Where(binding=>binding.Source.Source.Id==table.Source.Id&&page.Readable.Any(column=>column.Source.Id==binding.Foreign.Source.Id)).Select(binding=>binding.Cache)).DistinctBy(column=>column.Source.Id).ToArray()}).ToArray()}).ToArray()};
    }
    /// <summary>维护和混合传播必须实际登记所有名称副本，不能只让元数据入口放行后发布半条链路。</summary>
    internal static void RequireComplete(GenerationModel model,RelationMaintenanceCatalog catalog)
    {
        foreach(var binding in ForModel(model))
            if(!catalog.MixedSourceColumns.GetValueOrDefault(binding.Source.Source.TableName,[]).Contains(binding.Cache.Source.ColumnName,StringComparer.Ordinal))throw new NotSupportedException("全局List来源尚未形成完整保存/传播/维护计划："+binding.Source.Source.TableName+"."+binding.Cache.Source.ColumnName);
    }
    /// <summary>完整目录的全局来源存在时，所有生成服务均固定Actor项目，目标事件也不能切换消费者目录。</summary>
    internal static bool BindsActor(GenerationModel model)=>ForModel(model).Count>0;
    /// <summary>来源字段不接受改变目标含义的页级覆盖，所有页面共享同一物理名称副本。</summary>
    private static IReadOnlyList<GlobalCachedSourceBinding> Compile(GenerationModel model)
    {
        var result=new List<GlobalCachedSourceBinding>();
        foreach(var table in model.Tables.Where(table=>table.IsGlobal&&table.Source.IsRongyu==1&&!table.Columns.Any(column=>column.Source.ColumnName=="yz_project_id")))
        foreach(var foreign in table.Columns.Where(column=>!column.IsSystem&&column.Source.ControlKind=="List"))
        {
            var target=ForeignValidationRenderer.Target(model,foreign);if(target is null||!target.IsGlobal)continue;
            var single=GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,foreign.Source,target.Source);
            var cache=table.Columns.SingleOrDefault(column=>column.Source.ColumnName==foreign.Source.ColumnName[..^3]+"_"+single.Name.ColumnName)??throw new NotSupportedException("全局List来源必须声明原物理名称副本。");
            var proof=GlobalCachedRelationSource.RequireCache(model.Catalog,table.Source,cache.Source);
            foreach(var field in model.Catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId==foreign.Source.Id))
                if(field.Extra.TryGetValue("controlDetail",out var overlay)&&!string.IsNullOrWhiteSpace(overlay.ToString())&&!JsonNode.DeepEquals(JsonNode.Parse(overlay.ToString()),JsonNode.Parse(foreign.Source.ControlDetail)))throw new NotSupportedException("全局List来源页覆盖不能替换固定关系定义。");
            result.Add(new(table,foreign,target,target.Pages.Single(page=>page.Source.Id==proof.Page.Id),target.Columns.Single(column=>column.Source.Id==proof.Name.Id),cache));
        }
        return result;
    }
}
