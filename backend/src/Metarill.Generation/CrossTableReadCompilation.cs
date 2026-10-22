using System.Text.Json;
namespace Metarill.Generation;

internal sealed record CrossTableLevelModel(string Key,string Prefix,TableModel Target,PageModel Page,ColumnModel Name,ColumnModel? Parent,IReadOnlyList<(ColumnModel Column,bool Descending)> Order);
internal sealed record CrossTableReadModel(ColumnModel Source,IReadOnlyList<CrossTableLevelModel> Levels,string Joiner);
internal sealed record CrossTableReadResult(CrossTableReadModel? Definition,string? Reason);

/// <summary>把原跨表级联编译为有限强类型链；不推断动态SQL或模糊页面，不改变物理列。</summary>
internal static class CrossTableReadCompilation
{
    /// <summary>只有来源页已声明的可读/筛选控件才能增加原候选端点。</summary>
    internal static IReadOnlyList<CrossTableReadModel> ForPage(GenerationModel model,TableModel table,PageModel page)=>page.Readable.Concat(page.Searchable).DistinctBy(column=>column.Source.Id).Where(column=>column.Source.ControlKind=="CascadeTree").Select(column=>Resolve(model,table,page,column).Definition).OfType<CrossTableReadModel>().ToArray();
    /// <summary>固定读取计划在服务内共用，不因隐藏列增加外部端点。</summary>
    internal static IReadOnlyList<CrossTableReadModel> ForService(GenerationModel model,TableModel table,PageModel page)=>table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").Select(column=>Resolve(model,table,page,column).Definition).OfType<CrossTableReadModel>().ToArray();
    /// <summary>完整包继续报告未完成的保存、展示、筛选与辅助列；读接口存在不代表完整控件可发布。</summary>
    internal static string? CapabilityReason(GenerationModel model,TableModel table,PageModel page,ColumnModel column)=>Resolve(model,table,page,column).Definition is null?null:"CascadeTree跨表候选/路径已接线；已证明的各级保存及已存名称展示独立接线，筛选和导入导出及完整应用验收仍待完成。";
    /// <summary>逐级验证唯一可读目标页、父键指向、名称与稳定排序，任何一步不明就拒绝整个链。</summary>
    internal static CrossTableReadResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        CrossTableReadResult Gap(string reason)=>new(null,"跨表级联读取尚未支持："+reason);
        try
        {
            var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();using var json=JsonDocument.Parse(string.IsNullOrWhiteSpace(overlay)?source.Source.ControlDetail:overlay);var root=json.RootElement;Object(root);
            if(Text(root,"sourceMode","crossTable")!="crossTable")return Gap("不是crossTable模式。");
            if(table.Source.IsRongyu!=1||source.Type is not("string?" or "long?")||!source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)||source.IsSystem||GenerationModel.Enabled(source.Source.Extra,"isJoinColumn"))return Gap("来源须为普通冗余表的物理_id字段。");
            if(Extra(root))return Gap("额外范围或目标页映射不能忽略。");
            if(!root.TryGetProperty("levels",out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()<2)return Gap("至少需要2个固定层级。");
            var result=new List<CrossTableLevelModel>();var keys=new HashSet<string>(StringComparer.Ordinal);var prefixes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in entries.EnumerateArray())
            {
                Object(entry);if(Extra(entry))return Gap("层级额外范围或目标页映射不能忽略。");var nameOfTable=Text(entry,"table","");var target=model.Tables.SingleOrDefault(candidate=>candidate.Source.TableName==nameOfTable);
                if(target is null||target.Source.IsRongyu!=1||ManagerCredentialCompilation.IsTable(target)||TrustedSecurityServiceCompilation.Applies(model,target))return Gap("目标必须是已声明的普通冗余表。");
                var key=Text(entry,"key",nameOfTable);var prefix=Text(entry,"columnPrefix",nameOfTable);Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(prefix);
                if(!keys.Add(key)||!prefixes.Add(prefix))return Gap("层标识或字段前缀重复。");if(Text(entry,"idField","id")!="id")return Gap("主键只支持id。");
                bool Physical(ColumnModel column)=>!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")&&!column.IsJsonArray&&!ManagerCredentialCompilation.IsCredential(target,column);
                var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==Text(entry,"nameField","name"));if(name is null||name.Type!="string?"||!Physical(name))return Gap("名称必须为物理字符串。");
                ColumnModel? parent=null;
                if(result.Count==0&&!string.IsNullOrWhiteSpace(Text(entry,"parentField","")))Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(Text(entry,"parentField",""));
                if(result.Count>0)
                {
                    parent=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==Text(entry,"parentField",""));if(parent is null||parent.IsSystem||parent.Type is not("string?" or "long?")||!Physical(parent))return Gap("非首层必须有物理字符串/int64父键。");
                    if(!string.IsNullOrWhiteSpace(parent.Source.LinkConfigTableId)&&parent.Source.LinkConfigTableId!=result[^1].Target.Source.Id)return Gap("父键未指向上一层。");
                }
                if(!target.IsGlobal&&!target.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"))return Gap("目标缺少项目归属或明确全局声明。");
                var pages=target.Pages.Where(candidate=>!candidate.Scopes.Any(scope=>scope.Source=="parent")&&!ExactDataManageCompilation.Declared(candidate)&&candidate.Readable.Any(column=>column.Source.Id==name.Source.Id)&&(parent is null||candidate.Readable.Any(column=>column.Source.Id==parent.Source.Id))).ToArray();if(pages.Length!=1)return Gap("每级需要唯一且无目标父参数的可读页面。");
                var order=GenerationModel.ValidateOrderBy(Text(entry,"orderBy",name.Source.ColumnName+" asc"),target.Columns).ToList();if(order.Any(item=>!Physical(item.Column)))return Gap("排序必须是物理标量。");if(!order.Any(item=>item.Column.IsId))order.Add((target.Columns.Single(column=>column.IsId),false));result.Add(new(key,prefix,target,pages[0],name,parent,order));
            }
            if(result[^1].Prefix+"_id"!=source.Source.ColumnName)return Gap("末级前缀与来源叶子字段不一致。");
            if(!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId)&&source.Source.LinkConfigTableId!=result[^1].Target.Source.Id)return Gap("叶子关联表与末级不一致。");
            if(result.Select(level=>GenerationModel.Camel(level.Prefix+"_id")).Distinct(StringComparer.Ordinal).Count()!=result.Count)return Gap("层级JSON字段名冲突。");
            var joiner=Text(root,"displayJoiner","/");return new(new(source,result.ToArray(),joiner),null);
        }
        catch(Exception error)when(error is JsonException or ArgumentException or InvalidOperationException){return Gap("定义不能安全编译："+error.Message);}
    }
    /// <summary>不接受JSON最后值覆盖，结构与读取采用相同键解释。</summary>
    private static void Object(JsonElement value){if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw new ArgumentException("层级必须为无重复键对象。");}
    /// <summary>原标签与固定层级外观不进入SQL；额外查询定义明确拒绝。</summary>
    private static bool Extra(JsonElement value)=>new[]{"whereSql","where","sql","pageId","filterMatchMode"}.Any(key=>value.TryGetProperty(key,out _));
    /// <summary>空文本采用原默认，不把对象或数字转成SQL标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var field)||field.ValueKind==JsonValueKind.Null?fallback:field.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(field.GetString())?fallback:field.GetString()!:throw new ArgumentException("层级配置必须为文本："+key);
}
