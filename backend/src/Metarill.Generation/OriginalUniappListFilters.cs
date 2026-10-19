using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>逐项沿用原筛选控件、默认值和选项请求，保留原字段命名。</summary>
internal static partial class OriginalUniappListRenderer
{
    private sealed record ListFilters(string Markup, string Data, string Defaults, string Methods, string Requests, string Reset, int Count, IReadOnlyList<string> OptionFields);

    /// <summary>原筛选只在提交时生效；不新增排序器、查询按钮或清除按钮。</summary>
    private static ListFilters Filters(GenerationModel model, TableModel table, PageModel page)
    {
        var markup = new StringBuilder(); var data = new StringBuilder(); var defaults = new StringBuilder();
        var methods = new StringBuilder(); var requests = new StringBuilder(); var reset = new StringBuilder();
        // 只下发本页实际生成的远程候选数组，退出清理不猜测实体属性。
        var optionFields = new List<string>();
        foreach (var column in page.Searchable)
        {
            var field = Field(model, page, column); var key = column.JsonName;
            var kind = Effective(column.Source, field, "controlKind", column.Source.ControlKind);
            var detail = Effective(column.Source, field, "controlDetail", column.Source.ControlDetail);
            var focus = Flag(field.Extra, "isAutoFocus") ? "focus" : "";
            var inputKey = kind is "SelectPage" or "MultiSelectPage" ? RelationName(table, column) : key;
            var color = Html(model.Catalog.PrimaryColor);
            markup.Append("<uni-forms-item label='").Append(Html(column.Source.Name)).AppendLine("'>");
            if (Text(column.Source.Extra, "filterControlKind") == "CheckBox")
                markup.Append($"<uni-data-checkbox multiple v-model='filters.{key}' :localdata='{key}Range' />");
            else switch (kind)
            {
                case "DateTime": case "date_time": markup.Append($"<uni-datetime-picker type='datetimerange' v-model='filters.{key}' />"); break;
                case "Date": case "date": markup.Append($"<uni-datetime-picker type='daterange' v-model='filters.{key}' />"); break;
                case "Month": case "month":
                    var date = IsDateColumn(column);
                    var display = date ? $"filters.{key} && filters.{key}.length > 0 ? filters.{key}[0].toString().substring(0, 7) : \"\"" : $"filters.{key} || \"\"";
                    markup.Append($"<picker mode='date' fields='month' :value='{display}' @change='{key}MonthFilterChange'><uni-easyinput primaryColor=\"{color}\" disabled class='can-read' :value='{display}' suffixIcon='bottom' /></picker>");
                    methods.Append(key).Append("MonthFilterChange(e){var value=e&&e.detail?e.detail.value:'';value=value?String(value).substring(0,7):'';");
                    if (date) methods.Append($"if(!value){{this.filters.{key}=[];return;}}var parts=value.split('-');var lastDay=new Date(Number(parts[0]),Number(parts[1]),0).getDate();this.filters.{key}=[value+'-01',value+'-'+String(lastDay).padStart(2,'0')];");
                    else methods.Append($"this.filters.{key}=value;");
                    methods.Append("},"); break;
                case "ConstList": markup.Append($"<uni-data-checkbox v-model='filters.{key}' :localdata='{key}Range' />"); break;
                case "ConstDownList": markup.Append($"<zxz-uni-data-select v-model='filters.{key}' :localdata='{key}Range' filterable />"); break;
                case "List": case "MultiCheckBoxDatabase":
                    var name = column.Source.ColumnName.Length >= 3 ? column.Source.ColumnName[..^3] : column.Source.ColumnName;
                    markup.Append($"<zxz-uni-data-select v-model='filters.{key}' :localdata='{name}_list' filterable dataKey='name' dataValue='id' />");
                    data.Append(name).Append("_list:[],"); optionFields.Add(name + "_list");
                    requests.Append(OriginalOptionsRequest(model, table, page, column, name)); break;
                case "CheckBox": markup.Append($"<uni-data-checkbox v-model='filters.{key}' :localdata='$checkboxRange' />"); break;
                case "CascadeTree":
                    // 后端级联能力仍由导出门禁检查；模板保持原组件，不能降级成普通输入框。
                    var definition = JsonValue(detail);
                    var prefix = "/admin/auto/" + table.Source.TableName + "/" + page.Source.Id;
                    var leaf = key.EndsWith("Id", StringComparison.Ordinal) ? key[..^2] : key;
                    markup.Append($"<yzp-cascade-tree :value='filters.{key}' :definition='{Html(definition)}' request-url='{prefix}/cascadeTreeOptions{column.Source.Id}' path-request-url='{prefix}/cascadeTreePath{column.Source.Id}' leaf-id-property='{key}' leaf-name-property='{leaf}Name' placeholder=\"请选择{Html(column.Source.Name)}\" primary-color=\"{color}\" @input='receiveCascadeTreeFilter{column.Source.Id}' />");
                    requests.Append($"if(!this.filters.{key})this.filters.{key}={{}};");
                    reset.Append($"this.filters.{key}={{}};");
                    methods.Append($"receiveCascadeTreeFilter{column.Source.Id}(value){{this.filters.{key}=Object.assign({{}},value||{{}});}},"); break;
                default: markup.Append($"<uni-easyinput primaryColor=\"{color}\" v-model='filters.{inputKey}' confirm-type='search' @confirm='filterList' {focus} />"); break;
            }
            markup.AppendLine("</uni-forms-item>");
            if (kind is "ConstList" or "ConstDownList" || Text(column.Source.Extra, "filterControlKind") == "CheckBox")
                data.Append(key).Append("Range:").Append(ConstantRange(column, detail)).Append(',');
            var value = Text(field.Extra, "queryDefaultValue");
            if (value.Length == 0 || kind == "CascadeTree") continue;
            if (kind is "Month" or "month") defaults.Append($"this.{key}MonthFilterChange({{detail:{{value:{(value == "today" ? "(function(){var now=new Date();return now.getFullYear()+'-'+String(now.getMonth()+1).padStart(2,'0');})()" : J(value))}}}}});");
            else if (kind is "DateTime" or "Date" or "date_time" or "date") { if (value == "today") defaults.Append($"this.filters.{key}=utils.getTodayDateAry();"); }
            else defaults.Append("this.filters.").Append(inputKey).Append('=').Append(column.Type is "int?" or "decimal?" && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ? value : J(value)).Append(';');
        }
        return new(markup.ToString(), data.ToString(), defaults.ToString(), methods.ToString(), requests.ToString(), reset.ToString(), page.Searchable.Count, optionFields.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>旧选项路径按来源列生成；空POST不改成候选分页协议。</summary>
    private static string OriginalOptionsRequest(GenerationModel model, TableModel table, PageModel page, ColumnModel column, string name)
    {
        var property = column.Property.EndsWith("Ids", StringComparison.Ordinal) ? column.Property[..^3] : column.Property.EndsWith("Id", StringComparison.Ordinal) ? column.Property[..^2] : column.Property;
        var relation=RelationCompilation.ForPage(model,page).SingleOrDefault(item=>item.SourceColumn.Source.Id==column.Source.Id);
        var context=relation is null?null:ControlRenderer.RelationContext(page,relation,true);
        if(context?.UnresolvedTarget==true)return "that."+name+"_list=[];";
        var sourceParent=page.Scopes.Any(scope=>scope.Source=="parent");
        var target=context?.TargetParentField is {} field?"that.filters."+field:context?.SameParent==true?"that.parent_id":null;
        var body=sourceParent?"parentId:that.parent_id,":"";
        var request="that.originalOptionRequest({url:"+J("/admin/auto/"+table.Source.TableName+"/"+page.Source.Id+"/findNameId"+property+column.Source.Id)
            +(sourceParent||target is not null?",data:{"+body+(target is null?"":"targetParentId:optionParent,")+"}":"")
            +(target is null?"":",isCurrent:()=>optionParent===String("+target+"??'')")
            +",sendSessionId:true,method:'POST',success:function(res){if(res.code==200){that."+name+"_list=res.data||[];}else{uni.showModal({showCancel:false,content:res.msg});}}});";
        if(target is not null)request="const optionParent=String("+target+"??'');if(!/^[1-9][0-9]{0,18}$/.test(optionParent)||optionParent.length===19&&optionParent>'9223372036854775807')return;"+request;
        if(sourceParent)request="if(!that.parent_id)return;"+request;
        return "that."+name+"_list=[];(()=>{"+request+"})();";
    }
    /// <summary>常量保留原text/value，长整数字符串不经过浮点转换。</summary>
    private static string ConstantRange(ColumnModel column, string detail) => J(detail.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(option =>
    {
        var parts = option.Split('|', 2); var value = parts.Length > 1 ? parts[1] : "";
        object typed = column.Type switch
        {
            "int?" when int.TryParse(value, out var number) => number,
            "decimal?" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            "long?" or "long" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number.ToString(CultureInfo.InvariantCulture),
            _ => value
        };
        return new { text = parts[0], value = typed };
    }));
    private static bool IsDateColumn(ColumnModel column) => column.Source.LogicalType is "datetime" or "date" || column.Source.ColumnType is "datetime" or "date";
    private static string JsonValue(string value) { try { using var document = JsonDocument.Parse(value); return document.RootElement.GetRawText(); } catch (JsonException) { return "{}"; } }
}
