using System.Text.Json;

namespace Metarill.Generation;

internal sealed record CascadeTreeReadModel(ColumnModel Source,TableModel Target,PageModel TargetPage,ColumnModel Name,ColumnModel Parent,string Joiner,IReadOnlyList<(ColumnModel Column,bool Descending)> Order);
internal sealed record CascadeTreeReadResult(CascadeTreeReadModel? Definition,string? Reason);

/// <summary>将原自关联级联定义编译为固定实体和可读页面；未覆盖场景保留完整能力门禁。</summary>
internal static class CascadeTreeReadCompilation
{
    /// <summary>候选接口只来自本页实际声明的级联字段，避免生成未开放入口。</summary>
    internal static IReadOnlyList<CascadeTreeReadModel> ForPage(GenerationModel model,TableModel table,PageModel page)=>page.Readable.Concat(page.Searchable).DistinctBy(column=>column.Source.Id).Where(column=>column.Source.ControlKind=="CascadeTree")
        .Select(column=>Resolve(model,table,page,column).Definition).OfType<CascadeTreeReadModel>().ToArray();

    /// <summary>目标页必须唯一且明确可读；模糊范围不能退化为表级宽查询。</summary>
    internal static CascadeTreeReadResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        CascadeTreeReadResult Gap(string text)=>new(null,"CascadeTree候选/路径尚未支持："+text);
        try
        {
            var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();
            using var doc=JsonDocument.Parse(string.IsNullOrWhiteSpace(overlay)?source.Source.ControlDetail:overlay);
            ValidateObject(doc.RootElement);var root=doc.RootElement;
            if(Text(root,"sourceMode","crossTable")!="selfTree")return Gap("跨表级联仍待接线。");
            if(!root.TryGetProperty("selfTree",out var tree))return Gap("缺少selfTree定义。");ValidateObject(tree);
            if(new[]{"whereSql","sql","where","pageId"}.Any(key=>root.TryGetProperty(key,out _)||tree.TryGetProperty(key,out _)))return Gap("额外查询范围或目标页映射尚未接线，不能忽略后回退宽查询。");
            if(table.Source.IsRongyu!=1 || page.Scopes.Any(scope=>scope.Source=="parent"))return Gap("非冗余或来源父范围仍待接线。");
            var target=model.Tables.SingleOrDefault(candidate=>candidate.Source.TableName==Text(tree,"table"));
            if(target is null || target.Source.IsRongyu!=1 || ManagerCredentialCompilation.IsTable(target) || TrustedSecurityServiceCompilation.Applies(model,target))return Gap("目标必须是已声明的普通冗余表。");
            if(Text(tree,"idField","id")!="id" || Text(tree,"rootMode","blank")!="blank")return Gap("只支持主键id与blank根模式。");
            if(Text(tree,"filterMatchMode","selfAndDescendants") is not("exact" or "selfAndDescendants"))return Gap("未知筛选模式。");
            var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==Text(tree,"nameField","name"));
            var parent=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==Text(tree,"parentField"));
            bool Physical(ColumnModel column)=>!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")&&!column.IsJsonArray&&!ManagerCredentialCompilation.IsCredential(target,column);
            if(name is null || parent is null || name.Source.Id==parent.Source.Id || name.Type!="string?" || parent.Type is not("string?" or "long?") || !Physical(name)||!Physical(parent)||parent.IsSystem)return Gap("名称/父字段必须为非凭据物理标量，父字段为字符串或int64。");
            if(!string.IsNullOrWhiteSpace(parent.Source.LinkConfigTableId)&&parent.Source.LinkConfigTableId!=target.Source.Id)return Gap("父字段未指向自身。");
            if(!target.IsGlobal&&!target.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"))return Gap("目标缺少项目归属或明确全局声明。");
            var candidates=target.Pages.Where(candidate=>!candidate.Scopes.Any(scope=>scope.Source=="parent")&&!ExactDataManageCompilation.Declared(candidate)&&candidate.Readable.Any(column=>column.Source.Id==name.Source.Id)&&candidate.Readable.Any(column=>column.Source.Id==parent.Source.Id)).ToArray();
            if(candidates.Length!=1)return Gap("目标缺少唯一、无父参数的可读页面。");
            var ordering=GenerationModel.ValidateOrderBy(Text(tree,"orderBy",name.Source.ColumnName+" asc"),target.Columns).ToList();
            if(ordering.Any(item=>!Physical(item.Column)))return Gap("排序字段不是可排序物理标量。");
            if(!ordering.Any(item=>item.Column.IsId))ordering.Add((target.Columns.Single(column=>column.IsId),false));
            var joiner=Text(root,"displayJoiner","/");if(joiner.Length>32)return Gap("路径分隔符过长。");
            return new(new(source,target,candidates[0],name,parent,joiner,ordering),null);
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or ArgumentException){return Gap("定义无法安全编译："+error.Message);}
    }
    /// <summary>重复键和非对象不得被JSON最后值覆盖规则静默接受。</summary>
    private static void ValidateObject(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw new InvalidOperationException("级联配置必须为无重复键对象。");
    }
    /// <summary>只解释已知文本配置，空文本继承原默认值。</summary>
    private static string Text(JsonElement value,string key,string fallback="")=>!value.TryGetProperty(key,out var field)||field.ValueKind==JsonValueKind.Null?fallback:field.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(field.GetString())?fallback:field.GetString()!:throw new InvalidOperationException("级联配置字段必须为文本："+key);
}
