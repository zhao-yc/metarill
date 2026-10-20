using System.Text.Json;

namespace Metarill.Generation;

/// <summary>仅描述实际可证明的名称存储匹配；JSON数组必须由已交付数据库方言按元素匹配。</summary>
internal enum SelectionNameFilterKind { ScalarLike, JsonArrayAnyLike, JoinedScalarLike }
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
    /// <summary>冗余物理/数组名称与已证明的非冗余单值联接共用原键；缺口保留供完整包门禁判断。</summary>
    internal static SelectionNameFilterPlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        if(page.Source.ConfigTableId!=table.Source.Id||!model.Tables.Any(candidate=>candidate.Source.Id==table.Source.Id&&candidate.Pages.Any(candidatePage=>candidatePage.Source.Id==page.Source.Id)))throw new ArgumentException("名称筛选页面不属于当前生成表。");
        var bindings=new Dictionary<string,SelectionNameFilterBinding>(StringComparer.Ordinal);var gaps=new List<SelectionNameFilterGap>();var ambiguous=new HashSet<string>(StringComparer.Ordinal);
        foreach(var source in page.Searchable.Where(column=>UsesNameInput(model,page,column)))
        {
            var storage=SelectionDisplayFieldCompilation.Resolve(table,source);var key=storage.JsonName;
            void Gap(string reason)=>gaps.Add(new(source,key,"选择页名称筛选尚未支持："+reason));
            if(EffectiveFilterKind(model,page,source)!=source.Source.ControlKind){Gap("页级controlKind改变了名称筛选的存储种类，尚未建立完整证明。");continue;}
            if(table.Source.IsRongyu is not(0 or 1)){Gap("未识别的存储模式不能建立名称谓词。");continue;}
            if(storage.Source.Id==source.Source.Id){Gap("没有独立已声明名称列；ID筛选必须使用独立合同。");continue;}
            if(storage.Type!="string?"||storage.Source.IsVirtualColumn!=0||!page.Readable.Any(column=>column.Source.Id==storage.Source.Id)||table.Source.IsRongyu==1&&GenerationModel.Enabled(storage.Source.Extra,"isJoinColumn")){Gap("辅助名称必须为本页可读的已证明字符串列。");continue;}
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
                if(table.Source.IsRongyu==0)
                {
                    var selection=NonRedundantSelectionCompilation.Resolve(model,table,page,source);
                    if(!NonRedundantSelectionCompilation.IsDisplay(source,storage,selection)){Gap("非冗余名称列没有绑定同一单值外键和目标名称。");continue;}
                    var read=Metarill.Data.Queries.NonRedundantReads.Compile(model.Catalog,table.Source.Id,[storage.Source.ColumnName]);
                    if(read.OutputTypes[storage.Source.ColumnName]!="string"){Gap("非冗余名称计划不是字符串。");continue;}
                    kind=SelectionNameFilterKind.JoinedScalarLike;
                }
                else if(source.Source.ControlKind=="MultiSelectPage")
                {
                    var known=MultiSelectCompilation.Resolve(model,table,page,source);
                    if(known.Definition is not {} multi||multi.Names?.Source.Id!=storage.Source.Id){Gap(known.Reason??"多选名称辅助列与稳定显示属性不一致。");continue;}
                    kind=SelectionNameFilterKind.JsonArrayAnyLike;
                }
                else
                {
                    if(storage.IsJsonArray||SelectionPageRenderer.Enabled(targetPage)){Gap("单选名称不是标量，或目标页面实际为多选。");continue;}
                    if(!FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(target,targetPage),definition,out var reason)){Gap(reason!);continue;}
                    kind=SelectionNameFilterKind.ScalarLike;
                }
                var binding=new SelectionNameFilterBinding(source,storage,key,kind);
                if(ambiguous.Contains(key)){Gap("多个来源控件声明了同一个名称筛选键。");continue;}
                if(bindings.Remove(key,out var previous)){gaps.Add(new(previous.Source,key,"选择页名称筛选尚未支持：多个来源控件声明了同一个名称筛选键。"));Gap("多个来源控件声明了同一个名称筛选键。");ambiguous.Add(key);continue;}
                bindings.Add(key,binding);
            }
            catch(Exception error)when(error is JsonException or InvalidOperationException or ArgumentException){Gap("配置无法安全编译："+error.Message);}
        }
        // 同一HTTP键不能同时代表另一字段筛选或缺口来源；移出绑定后固定拒绝，避免覆盖普通case或依顺序选中。
        foreach(var binding in bindings.Values.ToArray())
        {
            var reason=page.Searchable.Any(column=>column.JsonName==binding.FilterKey)
                ? "名称筛选键与页面已开放的字段筛选冲突。"
                : gaps.Any(gap=>gap.FilterKey==binding.FilterKey) ? "同名名称筛选键存在无法编译的来源。" : null;
            if(reason is null)continue;
            bindings.Remove(binding.FilterKey);gaps.Add(new(binding.Source,binding.FilterKey,"选择页名称筛选尚未支持："+reason));
        }
        return new(bindings.Values.ToArray(),gaps.ToArray());
    }

    /// <summary>与原ListFilters的控件覆盖优先级相同，空配置继承来源；不修改元数据或详情控件。</summary>
    internal static string EffectiveFilterKind(GenerationModel model,PageModel page,ColumnModel column)
    {
        var field=model.Catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id);
        return field.Extra.TryGetValue("controlKind",out var value)&&value.ValueKind!=JsonValueKind.Null&&value.ToString().Length>0?value.ToString():column.Source.ControlKind;
    }
    /// <summary>原多选复选筛选优先使用源ID键，即使基础控件为选择页也不提交名称键。</summary>
    internal static bool UsesNameInput(GenerationModel model,PageModel page,ColumnModel column)
        => !CheckboxPriority(column)&&EffectiveFilterKind(model,page,column) is "SelectPage" or "MultiSelectPage";
    /// <summary>选择页覆盖为原文本筛选时沿用已存在的源ID合同，不开放新的名称入口。</summary>
    internal static bool UsesSourceTextOverride(GenerationModel model,PageModel page,ColumnModel column)
        => !CheckboxPriority(column)&&column.Source.ControlKind is "SelectPage" or "MultiSelectPage" && EffectiveFilterKind(model,page,column) is "Text" or "TextArea" or "RTF";
    /// <summary>原复选数组分支优先于页级文本覆盖，两种筛选判定必须共用此门禁。</summary>
    private static bool CheckboxPriority(ColumnModel column)=>column.Source.Extra.TryGetValue("filterControlKind",out var value)&&value.ToString()=="CheckBox";

    /// <summary>提供Query switch接线入口；JSON未提供真实方言分支时明确拒绝，绝不降级为序列化文本LIKE。</summary>
    internal static string RenderCases(SelectionNameFilterPlan plan,Func<SelectionNameFilterBinding,string>? renderJsonPredicate=null)
    {
        if(plan.Gaps.Count!=0)throw new InvalidOperationException(string.Join("；",plan.Gaps.Select(gap=>gap.Source.Source.ColumnName+":"+gap.Reason)));
        var result=new System.Text.StringBuilder();
        foreach(var binding in plan.Bindings)
        {
            if(binding.Kind==SelectionNameFilterKind.JoinedScalarLike)throw new InvalidOperationException("非冗余名称谓词必须使用固定读取计划，不能查询忽略持久化的实体列。");
            var predicate=binding.Kind==SelectionNameFilterKind.ScalarLike?$"query = Metarill.Data.Queries.StringNameQuery.ApplyLike(session, query, entity => entity.{binding.StorageColumn.Property}, value);":renderJsonPredicate?.Invoke(binding)??throw new InvalidOperationException("JSON数组名称筛选尚未提供数据库方言实现。");
            if(string.IsNullOrWhiteSpace(predicate))throw new InvalidOperationException("名称筛选生成分支不能为空。");
            result.AppendLine("case "+JsonSerializer.Serialize(binding.FilterKey)+": {")
                .AppendLine("if (filter.Value.ValueKind == JsonValueKind.Null || filter.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(filter.Value.GetString())) break;")
                .AppendLine("if (filter.Value.ValueKind != JsonValueKind.String) throw new ArgumentException(\"名称筛选只接受文本。\");")
                .AppendLine("var value = filter.Value.GetString()!;").AppendLine(predicate).AppendLine("break; }");
        }
        return result.ToString();
    }
}
