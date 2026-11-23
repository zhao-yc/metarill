using System.Globalization;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;

namespace Metarill.Generation;

/// <summary>逐项沿用原筛选控件、默认值和选项请求，保留原字段命名。</summary>
internal static partial class OriginalUniappListRenderer
{
    private sealed record CheckboxOptionScope(string Field, bool ParentScoped, IReadOnlyList<string> FormFields);
    private sealed record ListFilters(string Markup, string Data, string Defaults, string Methods, string Requests, string Reset, int Count, IReadOnlyList<string> OptionFields, IReadOnlyList<string> RemoteOptions, IReadOnlyList<CheckboxOptionScope> CheckboxOptions);

    /// <summary>原筛选只在提交时生效；不新增排序器、查询按钮或清除按钮。</summary>
    private static ListFilters Filters(GenerationModel model, TableModel table, PageModel page)
    {
        var markup = new StringBuilder(); var data = new StringBuilder(); var defaults = new StringBuilder();
        var methods = new StringBuilder(); var requests = new StringBuilder(); var reset = new StringBuilder();
        // 只下发本页实际生成的远程候选数组，退出清理不猜测实体属性。
        var optionFields = new List<string>();var remoteOptions = new List<string>();
        var checkboxOptions = new List<CheckboxOptionScope>();
        foreach (var column in page.Searchable)
        {
            var field = Field(model, page, column); var key = column.JsonName;
            var kind = Effective(column.Source, field, "controlKind", column.Source.ControlKind);
            var detail = Effective(column.Source, field, "controlDetail", column.Source.ControlDetail);
            var focus = Flag(field.Extra, "isAutoFocus") ? "focus" : "";
            var value = Text(field.Extra, "queryDefaultValue");
            markup.Append("<uni-forms-item label='").Append(Html(column.Source.Name)).AppendLine("'>");
            if (ControlRenderer.HasCheckboxArrayFilter(column))
            {
                markup.AppendLine($"<uni-data-checkbox multiple v-model='filters.{key}' :localdata='{key}Range' /></uni-forms-item>");
                var standardBoolean = kind == "CheckBox" && column.Type == "bool?" && string.IsNullOrWhiteSpace(detail);
                var range = kind switch
                {
                    "List" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CascadeTree" => "[]",
                    _ when standardBoolean => "this.$checkboxRange",
                    _ => ConstantRange(column, detail)
                };
                data.Append(key).Append("Range:").Append(range).Append(',');
                var scope = kind switch
                {
                    "List" when ControlRenderer.UsesScalarCheckboxFilter(column) =>
                        AppendListCheckboxFilterOptions(model, table, page, column, requests),
                    "MultiCheckBoxDatabase" when DatabaseCheckboxCompilation.Resolve(model, table, page, column).Definition is { } checkbox &&
                        (table.Source.IsRongyu==0||checkbox.SupportsRedundantArrayFilter) =>
                        AppendArrayCheckboxFilterOptions(model, table, page, column, checkbox.Endpoint,
                            checkbox.SourceParent is not null && checkbox.FormParentField is null, checkbox.Conditions, requests,
                            formParentField:checkbox.FormParentField),
                    "MultiSelectPage" when MultiSelectCompilation.CheckboxFilterOptions(model, table, page, column) is { } selection =>
                        AppendSelectionCheckboxFilterOptions(selection, requests),
                    _ => null
                };
                if (scope is not null)
                {
                    optionFields.Add(key + "Range");
                    checkboxOptions.Add(scope);
                }
                defaults.Append(CheckboxDefault(table,column,kind,value,standardBoolean,kind=="MultiCheckBoxDatabase"&&scope is not null));
                continue;
            }
            var inputKey = kind is "SelectPage" or "MultiSelectPage" ? RelationName(model, page, table, column) : key;
            var color = Html(model.Catalog.PrimaryColor);
            switch (kind)
            {
                case "DateTime": case "date_time": markup.Append($"<uni-datetime-picker type='datetimerange' v-model='filters.{key}' />"); break;
                case "Date": case "date": markup.Append($"<uni-datetime-picker type='daterange' v-model='filters.{key}' />"); break;
                case "Month": case "month":
                    var date = column.Type == "DateTime?";
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
                    var remote=kind=="List"?OriginalRemoteOptionsCompilation.Definition(model,page,column,name+"_list",true):null;
                    markup.Append($"<zxz-uni-data-select v-model='filters.{key}' :localdata='{name}_list' filterable dataKey='name' dataValue='id'{(remote is null?"":OriginalRemoteOptionsCompilation.Attributes(column))} />");
                    data.Append(name).Append("_list:[],");
                    // 原下拉仍提交单个 ID，候选和已选值随实际门店或父范围失效。
                    if(kind=="MultiCheckBoxDatabase"&&DatabaseCheckboxCompilation.Resolve(model,table,page,column).Definition is {} checkbox&&
                        (checkbox.FormParentField is not null||checkbox.HasStoredAccountScope||checkbox.HasStoredStaticScope||
                            checkbox.HasProjectStoredAccountScope||checkbox.ProjectFormScope))
                    {
                        optionFields.Add(name+"_list");
                        var scope=AppendArrayCheckboxFilterOptions(model,table,page,column,checkbox.Endpoint,
                            checkbox.SourceParent is not null&&checkbox.FormParentField is null,checkbox.Conditions,requests,
                            formParentField:checkbox.FormParentField,optionField:name+"_list");
                        if(scope is not null)checkboxOptions.Add(scope);
                    }
                    else if(remote is null){optionFields.Add(name+"_list");requests.Append(OriginalOptionsRequest(model,table,page,column,name));}
                    else {remoteOptions.Add(remote);requests.Append("that.loadOriginalRemoteOptions(").Append(J(column.Source.Id)).Append(");");} break;
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
            if (kind is "ConstList" or "ConstDownList")
                data.Append(key).Append("Range:").Append(ConstantRange(column, detail, scalar: true)).Append(',');
            if (value.Length == 0 || kind == "CascadeTree") continue;
            if (kind is "Month" or "month") defaults.Append($"this.{key}MonthFilterChange({{detail:{{value:{(value == "today" ? "(function(){var now=new Date();return now.getFullYear()+'-'+String(now.getMonth()+1).padStart(2,'0');})()" : J(value))}}}}});");
            else if (kind is "DateTime" or "Date" or "date_time" or "date") { if (value == "today") defaults.Append($"this.filters.{key}=utils.getTodayDateAry();"); }
            else
            {
                // 默认值与实际候选使用同一编码。单值控件的 v-model 接受字符串或数字。
                var literal = kind switch
                {
                    "ConstList" or "ConstDownList" => J(ConstantValue(column, value, scalar: true)),
                    "CheckBox" when column.Type == "bool?" => J(StandardCheckboxValue(ConstantValue(column, value))),
                    _ => column.Type is "int?" or "decimal?" && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ? value : J(value)
                };
                defaults.Append("this.filters.").Append(inputKey).Append('=').Append(literal).Append(';');
            }
        }
        return new(markup.ToString(), data.ToString(), defaults.ToString(), methods.ToString(), requests.ToString(), reset.ToString(), page.Searchable.Count, optionFields.Distinct(StringComparer.Ordinal).ToArray(), remoteOptions, checkboxOptions);
    }

    /// <summary>候选走目标多选页的授权分页；精确名称沿用原已选回读的文本合同。</summary>
    private static CheckboxOptionScope AppendSelectionCheckboxFilterOptions(MultiSelectModel selection, StringBuilder requests)
    {
        var field = selection.Source.JsonName;
        var targetParent = selection.TargetParentField;
        var parent = selection.SameParent ? "that.parent_id" : targetParent is null ? "null" : $"that.filters?.[{J(targetParent)}]";
        var options = J(new
        {
            field,
            url = $"/admin/auto/{selection.Target.Source.TableName}/{selection.TargetPage.Source.Id}",
            nameField = selection.Name.JsonName,
            int32Name = selection.IntegerLabel && selection.Name.Type == "int?",
            textNames = selection.TypedLabel && !selection.IntegerLabel,
            requiresParent = selection.TargetParents.Count > 0,
            limit = DatabaseCheckboxValue.MaximumItems
        });
        requests.AppendLine($"that.loadOriginalSelectionCheckboxOptions({options},{parent});");
        return new(field, selection.SameParent, targetParent is null ? [] : [targetParent]);
    }

    /// <summary>普通关联复用原数组端点。固定目录仍使用自己的已发布端点。</summary>
    private static CheckboxOptionScope? AppendListCheckboxFilterOptions(GenerationModel model, TableModel table, PageModel page,
        ColumnModel column, StringBuilder requests)
    {
        var catalog = FixedOptionCatalogCompilation.ForPage(model, page).SingleOrDefault(item => item.Source.Source.Id == column.Source.Id);
        if (catalog is not null)
            return AppendArrayCheckboxFilterOptions(model, table, page, column, catalog.Endpoint, false, null, requests);
        var relation = RelationCompilation.ForPage(model, page).SingleOrDefault(item => item.SourceColumn.Source.Id == column.Source.Id);
        if (relation is null) return null;
        var context = ControlRenderer.RelationContext(page, relation, true);
        if (context.UnresolvedTarget) return null;
        return AppendArrayCheckboxFilterOptions(model, table, page, column, relation.LegacyEndpoint,
            relation.SourceParents.Count > 0, relation.Conditions, requests, context);
    }

    /// <summary>数组候选只接收固定来源父级、目标父级和当前页已开放的表单标量。</summary>
    private static CheckboxOptionScope? AppendArrayCheckboxFilterOptions(GenerationModel model, TableModel table, PageModel page,
        ColumnModel column, string endpoint, bool requiresParent, RelationConditionModel? condition, StringBuilder requests,
        RelationUiContext? context = null, ColumnModel? formParentField=null, string? optionField=null)
    {
        var parentFields = requiresParent
            ? page.Scopes.Where(scope => scope.Source == "parent").Select(scope => scope.Column.Source.Id).ToHashSet(StringComparer.Ordinal)
            : [];
        var forms = condition?.Forms.Select(binding => binding.Column).DistinctBy(field => field.Source.Id).ToArray() ?? [];
        var sourceFilters = forms.Where(field => !parentFields.Contains(field.Source.Id)).ToArray();
        if (sourceFilters.Any(field => !ControlRenderer.HasScalarSourceFilter(model, page, field)))
            return null;

        var target = context?.TargetParentField;
        if (target is not null && !page.Searchable.Any(field => field.JsonName == target &&
            ControlRenderer.HasScalarSourceFilter(model, page, field)))
            return null;

        var bindings = new Dictionary<string,object?>
        {
            ["field"] = column.JsonName,
            ["url"] = $"/admin/auto/{table.Source.TableName}/{page.Source.Id}/{endpoint}",
            ["requiresParent"] = requiresParent,
            ["sameParent"] = context?.SameParent == true,
            ["targetParentField"] = target,
            ["table"] = GenerationModel.Camel(table.Source.TableName),
            ["formFields"] = forms.Select(field => new
            {
                name = field.JsonName,
                fromParent = parentFields.Contains(field.Source.Id),
                int64 = field.Type is "long" or "long?"
            }),
            ["limit"] = DatabaseCheckboxValue.MaximumItems
        };
        var dependencies = sourceFilters.Select(field => field.JsonName);
        if (target is not null) dependencies = dependencies.Append(target);
        if(formParentField is not null)
        {
            bindings["formParentField"]=formParentField.JsonName;
            dependencies=dependencies.Append(formParentField.JsonName);
        }
        if(optionField is not null)bindings["optionField"]=optionField;
        requests.AppendLine($"that.loadOriginalArrayCheckboxOptions({J(bindings)});");
        return new(column.JsonName, requiresParent, dependencies.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>显式复选与原组件使用同一源键和数组形状。日期预设从可选值中挑选，不生成区间。</summary>
    private static string CheckboxDefault(TableModel table,ColumnModel column,string kind,string value,bool standardBoolean,bool usesArrayCandidates)
    {
        if (value.Length == 0) return "";
        var key = column.JsonName;
        var month = kind is "month" or "Month";
        if (value == "today" && (month || column.Type == "DateTime?"))
        {
            var length = month ? 7 : 10;
            return $$$"""
                {
                  const dateKey = value => typeof value === 'string'
                    ? value.split(/[ T]/, 1)[0].split('-').map((part, index) => index ? part.padStart(2, '0') : part).join('-').slice(0, {{{length}}})
                    : '';
                  const today = dateKey(utils.getTodayDateAry()[0]);
                  this.filters.{{{key}}} = this.{{{key}}}Range.map(option => option.value).filter(value => today && dateKey(value) === today);
                }
                """;
        }

        if ((table.Source.IsRongyu==0||usesArrayCandidates)&&ControlRenderer.UsesMultiIdCheckboxFilter(column)&&value.TrimStart().StartsWith('['))
        {
            var ids = DatabaseCheckboxValue.Parse(value).Select(id => id.ToString(CultureInfo.InvariantCulture));
            return $"this.filters.{key}={J(ids)};";
        }

        var selected = ConstantValue(column, value);
        if (standardBoolean) selected = StandardCheckboxValue(selected);
        return $"this.filters.{key}={J(new[] { selected })};";
    }

    /// <summary>原全局“否／是”候选使用数字 0 和 1，恢复默认值时沿用该编码。</summary>
    private static object StandardCheckboxValue(object value) => value switch
    {
        bool boolean => boolean ? 1 : 0,
        "0" => 0,
        "1" => 1,
        _ => value
    };

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
    private static string ConstantRange(ColumnModel column, string detail, bool scalar = false) => J(detail.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(option =>
    {
        var parts = option.Split('|', 2); var value = parts.Length > 1 ? parts[1] : "";
        return new { text = parts[0], value = ConstantValue(column, value, scalar) };
    }));

    /// <summary>候选和默认值采用相同原始类型，避免复选框因数字与字符串不同而漏回显。</summary>
    private static object ConstantValue(ColumnModel column, string value, bool scalar = false) => column.Type switch
    {
        "bool?" when column.IsBooleanConstant && value.Length != 0 => BackendRenderer.ReadBooleanConstant(column, value) ? 1 : 0,
        "double?" when value.Length != 0 => BackendRenderer.ReadDoubleConstant(column, value),
        "bool?" when bool.TryParse(value, out var boolean) => scalar ? (object)(boolean ? 1 : 0) : boolean,
        "int?" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
        "decimal?" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
        "long?" or "long" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number.ToString(CultureInfo.InvariantCulture),
        _ => value
    };
    private static string JsonValue(string value) { try { using var document = JsonDocument.Parse(value); return document.RootElement.GetRawText(); } catch (JsonException) { return "{}"; } }
}
