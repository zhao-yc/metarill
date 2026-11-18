using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
namespace Metarill.Generation;

internal sealed record GlobalCachedSourceBinding(TableModel Source,ColumnModel Foreign,TableModel Target,PageModel Page,ColumnModel Name,ColumnModel Cache,bool ScopedSingle=false);

/// <summary>全局R1下拉及符合固定范围的原单选共用缓存发布证明，服务使用编译好的目录。</summary>
internal static class GlobalCachedSourceCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,IReadOnlyList<GlobalCachedSourceBinding>> Bindings=new();
    /// <summary>来源/目标/名称/副本必须同时成立；不从无项目字段猜权限，不悄悄舍弃缺失副本。</summary>
    internal static IReadOnlyList<GlobalCachedSourceBinding> ForModel(GenerationModel model)=>Bindings.GetValue(model,Compile);
    /// <summary>单值控件的隐式名称加入响应字段，页面控件及可写列继续取原配置。</summary>
    internal static GenerationModel AddReadAliases(GenerationModel model)
    {
        var bindings=ForModel(model);if(bindings.Count==0)return model;
        var byTable=bindings.ToLookup(binding=>binding.Source.Source.Id,StringComparer.Ordinal);
        return model with{Tables=model.Tables.Select(table=>table with
        {
            HasScopedGlobalCache=byTable[table.Source.Id].Any(binding=>binding.ScopedSingle),
            Pages=table.Pages.Select(page=>page with
            {
                Readable=page.Readable.Concat(byTable[table.Source.Id]
                    .Where(binding=>page.Readable.Any(column=>column.Source.Id==binding.Foreign.Source.Id))
                    .Select(binding=>binding.Cache)).DistinctBy(column=>column.Source.Id).ToArray()
            }).ToArray()
        }).ToArray()};
    }
    /// <summary>维护和混合传播必须实际登记所有名称副本，不能只让元数据入口放行后发布半条链路。</summary>
    internal static void RequireComplete(GenerationModel model,RelationMaintenanceCatalog catalog)
    {
        foreach(var binding in ForModel(model))
            if(!catalog.MixedSourceColumns.GetValueOrDefault(binding.Source.Source.TableName,[]).Contains(binding.Cache.Source.ColumnName,StringComparer.Ordinal))throw new NotSupportedException("全局List来源尚未形成完整保存/传播/维护计划："+binding.Source.Source.TableName+"."+binding.Cache.Source.ColumnName);
    }
    /// <summary>完整目录的全局来源存在时，所有生成服务均固定Actor项目，目标事件也不能切换消费者目录。</summary>
    internal static bool BindsActor(GenerationModel model)=>ForModel(model).Count>0||
        model.Tables.Any(table=>table.IsGlobal&&(table.Source.IsRongyu==0||table.Columns.Any(column=>
            column.Source.ControlKind=="CascadeTree"&&!NonRedundantCrossTableSubset.Declared(column))));
    /// <summary>快照获取连接前先核验原发布身份；普通目录保持原表达式和事务语义。</summary>
    internal static string ReadSession(GenerationModel model)=>BindsActor(model)?"PublishedReadSession(actor)":"session";
    /// <summary>只为固定发布身份的生成服务添加轻量守卫，不重写正文或元数据字面量。</summary>
    internal static string ReadSessionDeclaration(GenerationModel model)=>BindsActor(model)?"    /// <summary>先核验发布身份再获取读快照，不向错误项目打开数据库连接。</summary>\n    private DatabaseSession PublishedReadSession(RuntimeActor actor) { ValidateActor(actor); return session; }\n":"";
    /// <summary>来源字段不接受改变目标含义的页级覆盖，所有页面共享同一物理名称副本。</summary>
    private static IReadOnlyList<GlobalCachedSourceBinding> Compile(GenerationModel model)
    {
        var result=new List<GlobalCachedSourceBinding>();
        foreach(var table in model.Tables.Where(table=>table.IsGlobal&&table.Source.IsRongyu==1))
        foreach(var foreign in table.Columns.Where(column=>!column.IsSystem&&column.Source.ControlKind is "List" or "SelectPage"))
        {
            var rowProject=table.Columns.Any(column=>column.Source.ColumnName=="yz_project_id");
            var target=ForeignValidationRenderer.Target(model,foreign);
            if(target is null||!target.IsGlobal&&(!rowProject||
                GlobalCachedRelationSource.RowProjectSelfTreeParentPages(model.Catalog,table.Source,foreign.Source).Count==0))continue;
            var single=target.IsGlobal?GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,foreign.Source,target.Source)
                :GlobalSingleRelationTarget.RequireProjectParent(model.Catalog,table.Source,foreign.Source,target.Source);
            var cache=table.Columns.SingleOrDefault(column=>column.Source.ColumnName==foreign.Source.ColumnName[..^3]+"_"+single.Name.ColumnName)??throw new NotSupportedException("全局List来源必须声明原物理名称副本。");
            var proof=GlobalCachedRelationSource.RequireCache(model.Catalog,table.Source,cache.Source);
            foreach(var field in model.Catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId==foreign.Source.Id))
                RequireSameTarget(foreign,field,proof);
            result.Add(new(table,foreign,target,target.Pages.Single(page=>page.Source.Id==proof.Page.Id),target.Columns.Single(column=>column.Source.Id==proof.Name.Id),cache,proof.ScopedSingle));
        }
        return result;
    }

    /// <summary>单选沿用原页面文案继承。后端固定页、名称、筛选和排序仍使用同一副本声明。</summary>
    private static void RequireSameTarget(ColumnModel foreign,PageColumnMetadata field,GlobalCachedRelationBinding binding)
    {
        var overlay=field.Extra.GetValueOrDefault("controlDetail").ToString();
        if(string.IsNullOrWhiteSpace(overlay)||
            JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(foreign.Source.ControlDetail)))return;

        if(foreign.Source.ControlKind=="SelectPage")
        {
            var baseline=SelectionControlDefinition.Resolve(foreign,null);
            var effective=SelectionControlDefinition.Resolve(foreign,field);
            using var document=JsonDocument.Parse(overlay);
            var definition=document.RootElement;
            bool Matches(string key,string expected)=>!definition.TryGetProperty(key,out var value)||
                value.ValueKind==JsonValueKind.Null||
                value.ValueKind is JsonValueKind.String or JsonValueKind.Number&&
                (string.IsNullOrWhiteSpace(value.ToString())||value.ToString()==expected);

            if(baseline==(effective with{ButtonText=baseline.ButtonText})&&
                Matches("tableId",binding.Target.Id)&&Matches("table",binding.Target.TableName))return;
        }

        throw new NotSupportedException("全局单值来源页覆盖不能改变固定关系定义。");
    }
}
