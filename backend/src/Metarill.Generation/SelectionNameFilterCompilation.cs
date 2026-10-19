using System.Text.Json;

namespace Metarill.Generation;

/// <summary>仅描述实际可证明的名称存储匹配；JSON数组必须由已交付数据库方言按元素匹配。</summary>
internal enum SelectionNameFilterKind { ScalarLike, JsonArrayAnyLike }
internal sealed record SelectionNameFilterBinding(ColumnModel Source,ColumnModel StorageColumn,string FilterKey,SelectionNameFilterKind Kind);
internal sealed record SelectionNameFilterGap(ColumnModel Source,string FilterKey,string Reason);
internal sealed record SelectionNameFilterPlan(IReadOnlyList<SelectionNameFilterBinding> Bindings,IReadOnlyList<SelectionNameFilterGap> Gaps)
{
    /// <summary>接入Query时仅替换相同键的普通case，不能移除其他源ID筛选。</summary>
    internal IReadOnlySet<string> ConsumedKeys=>Bindings.Select(binding=>binding.FilterKey).ToHashSet(StringComparer.Ordinal);
}

/// <summary>原选择控件名称筛选的固定元数据编译；不接收请求表名/列名/SQL，不连接数据库。</summary>
internal static class SelectionNameFilterCompilation
{
    /// <summary>第一批只支持可读物理字符串名称、已证明的冗余Multi；缺口保留供能力门禁判断。</summary>
    internal static SelectionNameFilterPlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        if(page.Source.ConfigTableId!=table.Source.Id||!model.Tables.Any(candidate=>candidate.Source.Id==table.Source.Id&&candidate.Pages.Any(candidatePage=>candidatePage.Source.Id==page.Source.Id)))throw new ArgumentException("名称筛选页面不属于当前生成表。");
        var bindings=new Dictionary<string,SelectionNameFilterBinding>(StringComparer.Ordinal);var gaps=new List<SelectionNameFilterGap>();var ambiguous=new HashSet<string>(StringComparer.Ordinal);
        foreach(var source in page.Searchable.Where(column=>column.Source.ControlKind is "SelectPage" or "MultiSelectPage"))
        {
            var storage=SelectionDisplayFieldCompilation.Resolve(table,source);var key=storage.JsonName;
            void Gap(string reason)=>gaps.Add(new(source,key,"选择页名称筛选尚未支持："+reason));
            if(table.Source.IsRongyu!=1){Gap("非冗余名称读计划尚未交付。");continue;}
            if(storage.Source.Id==source.Source.Id){Gap("没有独立已声明名称列；ID筛选必须使用独立合同。");continue;}
            if(storage.Type!="string?"||storage.Source.IsVirtualColumn!=0||GenerationModel.Enabled(storage.Source.Extra,"isJoinColumn")||!page.Readable.Any(column=>column.Source.Id==storage.Source.Id)){Gap("辅助名称必须为本页可读的物理字符串列。");continue;}
            try
            {
                var settings=model.Catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id);
                var definition=SelectionControlDefinition.Resolve(source,settings);var baseline=SelectionControlDefinition.Resolve(source,null);
                if(definition.NameField!=baseline.NameField){Gap("页级nameField改变了稳定辅助列取值，不能按另一物理列静默筛选。");continue;}
                var target=model.Tables.SingleOrDefault(candidate=>candidate.Pages.Any(candidatePage=>candidatePage.Source.Id==definition.PageId));
                var targetPage=target?.Pages.Single(candidatePage=>candidatePage.Source.Id==definition.PageId);
                if(target is null||targetPage is null||!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId)&&source.Source.LinkConfigTableId!=target.Source.Id){Gap("目标页不存在或不属于声明目标表。");continue;}
                if(!target.IsGlobal&&!target.Columns.Any(column=>column.Source.ColumnName=="yz_project_id")){Gap("目标没有明确项目归属或全局声明。");continue;}
                var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==definition.NameField);
                if(name is null||name.Type!="string?"||!targetPage.Readable.Any(column=>column.Source.Id==name.Source.Id)){Gap("目标名称字段不是已开放读取的字符串。");continue;}
                SelectionNameFilterKind kind;
                if(source.Source.ControlKind=="MultiSelectPage")
                {
                    var known=MultiSelectCompilation.Resolve(model,table,page,source);
                    if(known.Definition is not {} multi||multi.Names?.Source.Id!=storage.Source.Id){Gap(known.Reason??"多选名称辅助列与稳定显示属性不一致。");continue;}
                    kind=SelectionNameFilterKind.JsonArrayAnyLike;
                }
                else
                {
                    if(storage.IsJsonArray||SelectionPageRenderer.Enabled(targetPage)){Gap("单选名称不是标量，或目标页面实际为多选。");continue;}
                    if(!string.IsNullOrWhiteSpace(definition.WhereSql)||!string.IsNullOrWhiteSpace(definition.OrderBy)){Gap("单选附加范围/排序尚无完整编译证明。");continue;}
                    kind=SelectionNameFilterKind.ScalarLike;
                }
                var binding=new SelectionNameFilterBinding(source,storage,key,kind);
                if(ambiguous.Contains(key)){Gap("多个来源控件声明了同一个名称筛选键。");continue;}
                if(bindings.Remove(key,out var previous)){gaps.Add(new(previous.Source,key,"选择页名称筛选尚未支持：多个来源控件声明了同一个名称筛选键。"));Gap("多个来源控件声明了同一个名称筛选键。");ambiguous.Add(key);continue;}
                bindings.Add(key,binding);
            }
            catch(Exception error)when(error is JsonException or InvalidOperationException or ArgumentException){Gap("配置无法安全编译："+error.Message);}
        }
        return new(bindings.Values.ToArray(),gaps.ToArray());
    }

    /// <summary>提供Query switch接线入口；JSON未提供真实方言分支时明确拒绝，绝不降级为序列化文本LIKE。</summary>
    internal static string RenderCases(SelectionNameFilterPlan plan,Func<SelectionNameFilterBinding,string>? renderJsonPredicate=null)
    {
        if(plan.Gaps.Count!=0)throw new InvalidOperationException(string.Join("；",plan.Gaps.Select(gap=>gap.Source.Source.ColumnName+":"+gap.Reason)));
        var result=new System.Text.StringBuilder();
        foreach(var binding in plan.Bindings)
        {
            var predicate=binding.Kind==SelectionNameFilterKind.ScalarLike?$"query = query.Where(entity => entity.{binding.StorageColumn.Property} != null && entity.{binding.StorageColumn.Property}.Contains(value));":renderJsonPredicate?.Invoke(binding)??throw new InvalidOperationException("JSON数组名称筛选尚未提供数据库方言实现。");
            if(string.IsNullOrWhiteSpace(predicate))throw new InvalidOperationException("名称筛选生成分支不能为空。");
            result.AppendLine("case "+JsonSerializer.Serialize(binding.FilterKey)+": {")
                .AppendLine("if (filter.Value.ValueKind == JsonValueKind.Null || filter.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(filter.Value.GetString())) break;")
                .AppendLine("if (filter.Value.ValueKind != JsonValueKind.String) throw new ArgumentException(\"名称筛选只接受文本。\");")
                .AppendLine("var value = filter.Value.GetString()!;").AppendLine(predicate).AppendLine("break; }");
        }
        return result.ToString();
    }
}
