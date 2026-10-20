using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Metarill.Generation.OriginalDetailContext;

namespace Metarill.Generation;

/// <summary>逐项移植原GenerateDetail字段分支；控件标签/props/events沿用v8，不依赖简化ControlRenderer。</summary>
internal static class OriginalUniappDetailFields
{
    /// <summary>输出原字段容器、默认值、校验、只读计算及事件，分组仅v-show不卸载表单项。</summary>
    internal static void Render(OriginalDetailContext c, OriginalDetailField f, string group, bool tabs)
    {
        var column = f.Column; var name = column.JsonName; var ro = name + "ReadOnly";
        var visible = Text(column.Source.Extra, "visibleCondition"); var description = Text(column.Source.Extra, "description");
        var span = f.Span > 1 ? $" style='grid-column: span {f.Span};'" : "";
        var grouped = tabs ? $" id='yzp-detail-field-{name}'" + (group.Length > 0 ? $" v-show='activeDetailGroupKey === {Html(Js(group))}'" : "") : "";
        c.Fields.AppendLine($"<uni-forms-item label='{Html(column.Source.Name)}' name='{name}' {(f.Required && !f.ReadOnly ? "required" : "")}{span}{grouped} v-if='!loadEntitySuccess || $yzp.isColumnVisible({{visibleCondition: {Html(Js(visible))}}}, entity)'>");
        if(f.Derived)
        {
            c.Readonly.Append(name).Append(":true,");c.Defaults.Append(name).Append(":null,");c.Computed.AppendLine($"{ro}() {{ return true; }},");
            c.Fields.AppendLine($"<text>{{{{readonlyDisplayText(entity.{name})}}}}</text></uni-forms-item>");return;
        }
        c.Readonly.Append(name).Append(':').Append(f.ReadOnly ? "true" : "false").Append(',');
        c.Computed.AppendLine($"{ro}() {{ return this.readOnlyCondition || this.entity_column_readonly.{name}; }},");
        c.Defaults.Append(name).Append(':').Append(Default(f)).Append(',');
        Rules(c, f); var content = Control(c, f, ro, description); c.Fields.AppendLine(content);
        if (description.Length > 0 && column.Source.ControlKind is not ("Image" or "MultiImage")) c.Fields.AppendLine($"<view class='input-tips'>{Html(description)}</view>");
        c.Fields.AppendLine("</uni-forms-item>");
        var watch = new StringBuilder();
        if (Enabled(column.Source.Extra, "isAutoPostBack")) watch.Append($"var parameters=uni.getStorageSync(this.formParametersName)||{{}}; parameters.{GenerationModel.Camel(c.Table.Source.TableName)}=parameters.{GenerationModel.Camel(c.Table.Source.TableName)}||{{}}; parameters.{GenerationModel.Camel(c.Table.Source.TableName)}.{name}=newVal; uni.setStorageSync(this.formParametersName,parameters); this.initDynamicLists();");
        if (Enabled(f.Settings.Extra, "isAutoSaveAfterChange")) watch.Append("if(this.loadEntitySuccess && JSON.stringify(newVal)!==JSON.stringify(oldVal)) this.saveEntity();");
        if (watch.Length > 0) c.Watches.AppendLine($"'entity.{name}'(newVal,oldVal) {{ if(!this._alive || this.readOnlyCondition) return; {watch} }},");
    }

    /// <summary>原详情默认值按字段明细保留；数组控件首次新增使用数组，读取已有null不在此处改写。</summary>
    private static string Default(OriginalDetailField f)
    {
        if (f.Column.Source.ControlKind is "Image" or "MultiImage" or "Upload" or "Video" or "FileLibrary" or "MultiCheckBoxDatabase" or "MultiSelectPage") return "[]";
        if (f.Column.Type is "long" or "long?" or "int?" or "decimal?" or "double?" && !string.IsNullOrWhiteSpace(f.Default))
        {
            if (!decimal.TryParse(f.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw new InvalidOperationException("原详情数值默认值无效：" + f.Column.Source.ColumnName);
            return f.Column.Type is "long" or "long?" ? Js(f.Default) : f.Default;
        }
        return Js(f.Default);
    }
    /// <summary>恢复原必填、长度、整数和小数校验，字段只读不产生必填规则。</summary>
    private static void Rules(OriginalDetailContext c, OriginalDetailField f)
    {
        var rules = new List<object>();
        if (f.Required && !f.ReadOnly) rules.Add(new { required = true, errorMessage = f.Column.Source.Name + "为必填项" });
        if (f.Column.Type == "string?" && f.Column.Source.ControlKind is not ("List" or "CascadeTree") && int.TryParse(f.Column.Source.ColumnLength, out var maximum) && maximum > 0) rules.Add(new { maxLength = maximum, errorMessage = "长度需在{maxLength}个字符以内" });
        else if (f.Column.Type is "long" or "long?" or "int?") rules.Add(new { pattern = "^-?\\d+$", errorMessage = "只能输入整数" });
        else if (f.Column.Type is "decimal?" or "double?") rules.Add(new { format = "number", errorMessage = "只能输入数字" });
        c.Rules.Append(f.Column.JsonName).Append(":{rules:").Append(Js(rules)).Append("},");
    }
    /// <summary>原只读选择显示为文本，编辑态才挂载原选择控件。</summary>
    private static string Choice(string ro, string display, string control) => $"<text v-if='{ro}'>{{{{{display}}}}}</text><template v-if='!{ro}'>{control}</template>";
    private static string Hidden(OriginalDetailField f) => $"<uni-easyinput style='display: none;' v-model='entity.{f.Column.JsonName}' maxlength='{Html(f.Column.Source.ColumnLength)}' />";

    /// <summary>保留每个原控件分支，未知控件明确拒绝，不以只读占位冒充完成。</summary>
    private static string Control(OriginalDetailContext c, OriginalDetailField f, string ro, string description)
    {
        var column = f.Column; var name = column.JsonName; var kind = column.Source.ControlKind; var primary = c.PrimaryColor;
        switch (kind)
        {
            case "date_time": return $"<uni-datetime-picker type='datetime' v-if='!{ro}' v-model='entity.{name}' /><text v-if='{ro}'>{{{{entity.{name}}}}}</text>";
            case "date": return $"<uni-datetime-picker type='date' :disabled='{ro}' v-model='entity.{name}' />";
            case "month":
                c.Methods.AppendLine($"{name}MonthChange(e) {{ const value=e?.detail?.value; this.entity.{name}=value ? String(value).slice(0,7)+'-01' : ''; }},");
                return $"<picker mode='date' fields='month' :disabled='{ro}' :value='entity.{name} ? String(entity.{name}).substring(0,7) : \"\"' @change='{name}MonthChange'><uni-easyinput :disabled='{ro}' :model-value='entity.{name} ? String(entity.{name}).substring(0,7) : \"\"' primaryColor=\"{primary}\" /></picker>";
            case "time": return $"<picker mode='time' :disabled='{ro}' :value='entity.{name}' @change='entity.{name}=$event.detail.value'><uni-easyinput :disabled='{ro}' :model-value='entity.{name}' /></picker>";
            case "ConstList": case "ConstDownList":
                var choices = f.Detail.Split(',').Select(token => token.Split('|', 2)).Select(pair => new { text = pair[0], value = ConstValue(column, pair.Length == 2 ? pair[1] : pair[0]) }).ToArray();
                c.Data.Append(name).Append("Range:").Append(Js(choices)).Append(',');
                return Choice(ro, $"entity.{name}Show", kind == "ConstList" ? $"<uni-data-checkbox :disabled='{ro}' v-model='entity.{name}' :localdata='{name}Range' />" : $"<zxz-uni-data-select v-model='entity.{name}' :localdata='{name}Range' filterable :disabled='{ro}' />");
            case "CheckBox": return Choice(ro, $"(($checkboxRange || [])[entity.{name}==null?0:entity.{name}] || {{}}).text || ''", $"<uni-data-checkbox :disabled='{ro}' v-model='entity.{name}' :localdata='$checkboxRange' />");
            case "List":
                var list = Stem(column) + "_list"; var handler = FixedOptionCatalogCompilation.Resolve(c.Model,c.Page,column).Definition is not null ? Stem(column) + "SelectChange" : name + "DetailChange"; var display = c.DisplayField(column, string.IsNullOrWhiteSpace(f.Detail) || !f.Detail.TrimStart().StartsWith("{",StringComparison.Ordinal) ? "name" : Property(Object(f.Detail,"关联"),"nameField","name"));
                c.Data.Append(list).Append(":[],"); InitList(c, f, list, false);
                c.Methods.AppendLine($"{handler}(item) {{ if(this.{ro}) return; this.entity.{name}=item ? item.id : ''; {(display == name ? "" : $"this.entity.{display}=item ? item.name : '';")} }},");
                return Choice(ro, $"readonlyDisplayText(entity.{display})", $"<zxz-uni-data-select v-model='entity.{name}' :localdata='{list}' filterable :disabled='{ro}' dataKey='name' dataValue='id' @change='{handler}' />");
            case "MultiCheckBoxDatabase":
                c.Data.Append(name).Append("List:[],").Append(name).Append("SelectAll:[],"); InitList(c, f, name + "List", true);
                c.Methods.AppendLine($$$"""
{{{name}}}SelectAllChange() { if(this.{{{ro}}}) return; this.entity.{{{name}}}=this.{{{name}}}SelectAll==1 ? this.{{{name}}}List.map(item=>item.id) : []; },
{{{name}}}Change() { this.{{{name}}}SelectAll=JSON.stringify(this.{{{name}}}List.map(item=>String(item.id)).sort())===JSON.stringify(yzp.relationArray(this.entity.{{{name}}}).map(String).sort()) ? [1] : []; },
""");
                return Choice(ro, $"readonlyDisplayText(entity.{c.DisplayField(column)})", $"<view><uni-data-checkbox multiple v-model='{name}SelectAll' :disabled='{ro}' :localdata='$multiCheckboxRange' @change='{name}SelectAllChange' /></view><uni-data-checkbox multiple v-model='entity.{name}' :disabled='{ro}' :localdata='{name}List' @change='{name}Change' />") + Hidden(f);
            case "TextArea": return $"<uni-easyinput primaryColor=\"{primary}\" :disabled='{ro}' type='textarea' maxlength='{(column.Source.ColumnLength == "max" ? "-1" : Html(column.Source.ColumnLength))}' v-model='entity.{name}' />";
            case "CustomControl":
                if (!Regex.IsMatch(f.Detail, "^yzp-[a-z0-9]+(?:-[a-z0-9]+)*$")) throw new InvalidOperationException("原详情自定义组件名称无效。");
                c.Methods.AppendLine($"receive{column.Source.Id}(newEntity) {{ if(this.{ro}) return; this.entity=newEntity; }},");
                return $"<{f.Detail}{(f.Detail == "yzp-table-storage-mode" ? "" : $" v-if='!{ro}'")} :entity=\"entity\" :record-id=\"id\" :readonly=\"{ro}\" @receive=\"receive{column.Source.Id}\" />";
            case "FileLibrary": return FileLibrary(c, f, ro);
            case "IntenetImage": return $"<image class='internet-img' mode='widthFix' :src='entity.{name}'></image>";
            case "Base64Image": return $"<image class='base64img' mode='widthFix' :src='entity.{name}'></image>" + Hidden(f);
            case "Image": case "MultiImage": case "Upload": case "Video": return Upload(c, f, ro, description);
            case "SelectPage": case "MultiSelectPage": return OriginalUniappDetailSelection.Render(c, f, ro);
            case "CascadeTree": return Cascade(c, f, ro);
            case "RTF":
                c.Methods.AppendLine($$$"""
{{{name}}}RtfDo(action) { if(!['edit','review'].includes(action) || action==='edit' && this.{{{ro}}}) return; const current=this.originalContext(), original=this.entity.{{{name}}}, text=original??'', that=this; uni.navigateTo({url:'/pages/main/rtf/rtf?action='+action,events:{onReceiveRtf(rtf){that.permissionVersion++;if(action==='edit' && !that.{{{ro}}} && current() && typeof rtf==='string' && rtf!==text) that.entity.{{{name}}}=rtf;}},success(result){if(current())result.eventChannel.emit('sendRtf',{data:text});}}); },
""");
                return Hidden(f) + $"<view class='rtf-buttons'><button size='mini' @click=\"{name}RtfDo('review')\">预览</button><button size='mini' v-if='!{ro}' @click=\"{name}RtfDo('edit')\">编辑</button></view><view class='rtf-review' v-html='entity.{name}'></view>";
            case "":
                return $"<uni-easyinput primaryColor=\"{primary}\" v-if='!{ro}' v-model='entity.{name}' {(column.Type == "string?" ? $"maxlength='{(column.Source.ColumnLength == "max" ? "-1" : Html(column.Source.ColumnLength))}'" : "")} {(column.Type is "int?" or "decimal?" or "double?" ? "type='number'" : "")} {(Enabled(c.Page.Source.Extra, "isListEdit") ? "@blur='saveEntity()'" : "")} /><text v-if='{ro}'>{{{{entity.{name}}}}}</text>";
            default: throw new InvalidOperationException("原详情尚无该控件的完整移植分支：" + kind);
        }
    }
    /// <summary>下拉仍调用原findNameId与formParameters协议；只为迟到响应增加组件范围核验。</summary>
    private static void InitList(OriginalDetailContext c, OriginalDetailField f, string list, bool checkbox)
    {
        var column = f.Column; var endpoint = c.Api + "/findNameId" + GenerationModel.Pascal(Stem(column)) + column.Source.Id;
        var relation=checkbox?null:RelationCompilation.ForPage(c.Model,c.Page).SingleOrDefault(item=>item.SourceColumn.Source.Id==column.Source.Id);
        var context=relation is null?null:ControlRenderer.RelationContext(c.Page,relation,false);
        var target=context?.TargetParentField is {} parent?"that.entity."+parent:context?.SameParent==true?"that.parent_id":null;
        var load = context?.UnresolvedTarget==true
            ? "that."+list+"=[];uni.showModal({showCancel:false,content:'关联目标父范围尚未明确，不能加载候选项'});"
            : "(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["+Js(column.Source.Id)+"]||0)+1;that._originalOptions["+Js(column.Source.Id)+"]=optionSequence;const optionParent="+(target is null?"null":"String("+target+"??'')")+";that.originalRequest({url:"+Js(endpoint)+",isCurrent:()=>optionSequence===that._originalOptions["+Js(column.Source.Id)+"]&&"+(target is null?"true":"optionParent===String("+target+"??'')")+",data:{parentId:that.parent_id,"+(target is null?"":"targetParentId:optionParent,")+"formParameters:uni.getStorageSync(that.formParametersName)"+(f.ReadOnly?",id:that.entity."+column.JsonName:",...(that.readOnlyCondition?{id:that.entity."+column.JsonName+"}:{})")+"},method:'POST',sendSessionId:true,success(res){if(res.code===200){that."+list+"=res.data||[];"+(checkbox?"that."+column.JsonName+"Change();":"")+"}else uni.showModal({showCancel:false,content:res.msg});}});})();";
        var method="loadOriginalOptions"+column.Source.Id;
        // 独立父字段即使没有开启旧联动标志，也必须失效原候选缓存；实体的已选ID/名称不被清空。
        if(context?.TargetParentField is {} targetField)
        {
            var key=Js(column.Source.Id);
            c.OptionWatchers.AppendLine($$$"""
this.$watch(()=>this.entity.{{{targetField}}},()=>{this.{{{list}}}=[];this._originalOptions=this._originalOptions||{};const sequence=(this._originalOptions[{{{key}}}]||0)+1;this._originalOptions[{{{key}}}]=sequence;const current=this.originalContext(),parent=this.entity.{{{targetField}}};this.$nextTick(()=>{if(current()&&parent===this.entity.{{{targetField}}}&&sequence===this._originalOptions[{{{key}}}])this.{{{method}}}();});},{flush:'sync'});
""");
            var guard="if(!that._alive||!that.permissionsReady||that.id&&!that.recordReady||!/^[1-9][0-9]{0,18}$/.test(optionParent)||optionParent.length===19&&optionParent>'9223372036854775807')return;";
            load=load.Replace(";that.originalRequest({", ";"+guard+"that.originalRequest({",StringComparison.Ordinal);
        }
        c.Methods.AppendLine($"{method}() {{const that=this;that.{list}=[];{load}}},");
        if (Enabled(column.Source.Extra, "isAutoPostBackQuery")) c.DynamicInit.AppendLine($"that.{method}();"); else c.Init.AppendLine($"that.{method}();");
    }
    /// <summary>沿用现有DTO/VO常量类型；长整数保留字符串，小数按decimal解析，不经JavaScript整数转换。</summary>
    private static object? ConstValue(ColumnModel column, string value) => column.Type switch
    {
        "string?" => value,
        "long" or "long?" => value.Length==0?null:long.Parse(value,NumberStyles.Integer,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        "int?" => value.Length==0?null:int.Parse(value,NumberStyles.Integer,CultureInfo.InvariantCulture),
        "decimal?" => value.Length==0?null:decimal.Parse(value,NumberStyles.Number,CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("原详情常量值与字段类型不符："+column.Source.ColumnName)
    };
    private static string Stem(ColumnModel column) => column.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal) ? column.Source.ColumnName[..^4] : column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal) ? column.Source.ColumnName[..^3] : column.Source.ColumnName;

    /// <summary>文件库沿用原v-model、上下文和全部控件props，默认保护访问与可信后端一致。</summary>
    private static string FileLibrary(OriginalDetailContext c, OriginalDetailField f, string ro)
    {
        var d = Object(f.Detail, "文件库"); var access = Property(d, "accessMode", "PROTECTED"); if (access is not ("PUBLIC" or "PROTECTED")) throw new InvalidOperationException("文件库访问模式无效。");
        var maximum = d.TryGetProperty("maxCount", out var count) ? Integer(count, 9) : 9; var size = d.TryGetProperty("maxSizeMb", out var bytes) ? Integer(bytes, 50) : 50;
        var accept = Property(d, "accept", "image,file,video"); if (maximum is < 1 or > 100 || size is < 1 or > 10240 || accept.Split(',').Any(item => item is not ("image" or "file" or "video"))) throw new InvalidOperationException("文件库配置无效。");
        var extensions = d.TryGetProperty("extensions", out var exts) && exts.ValueKind != JsonValueKind.Null ? exts.GetRawText() : "[]";
        return $"<yzp-file-library v-model='entity.{f.Column.JsonName}' config-column-id='{f.Column.Source.Id}' page-id='{c.Page.Source.Id}' table-id='{c.Table.Source.Id}' :record-id='id' :parent-id='parent_id' :parent-page-sn='parent_page_sn' access-mode='{access}' accept='{Html(accept)}' :max-count='{maximum}' :max-size-mb='{Math.Min(size,100)}' :extensions='{Html(extensions)}' :allow-library='{(!d.TryGetProperty("allowLibrary", out var allow) || allow.ValueKind != JsonValueKind.False).ToString().ToLowerInvariant()}' :crop-images='{(d.TryGetProperty("cropImages", out var crop) && crop.ValueKind == JsonValueKind.True).ToString().ToLowerInvariant()}' :readonly='{ro}' primary-color=\"{c.PrimaryColor}\" />";
    }
    /// <summary>原文件选择仍顺序上传并可显式裁切；旧回调不能写入已离开的详情。</summary>
    private static string Upload(OriginalDetailContext c, OriginalDetailField f, string ro, string description)
    {
        var name=f.Column.JsonName; var kind=f.Column.Source.ControlKind; var limit=9; var crop=false;
        if (kind is "Image" or "MultiImage") { var detail=Object(f.Detail,"图片"); if(detail.TryGetProperty("limit",out var value))limit=Integer(value,9); crop=detail.TryGetProperty("cropImages",out var option)&&option.ValueKind==JsonValueKind.True; }
        else if (!string.IsNullOrWhiteSpace(f.Detail) && !int.TryParse(f.Detail,out limit)) throw new InvalidOperationException("原附件/视频数量必须为整数。");
        if(limit is < 1 or > 100)throw new InvalidOperationException("原上传数量上限无效。");
        c.Methods.AppendLine($$$"""
{{{name}}}FileSelect(e) { if(this.{{{ro}}})return; const that=this,current=this.originalContext(); yzp.uploadSelectedFiles(e,{cropImages:{{{crop.ToString().ToLowerInvariant()}}}},function(uploadedResults){that.permissionVersion++;if(!current() || that.{{{ro}}} || !uploadedResults?.length)return; const old=Array.isArray(that.entity.{{{name}}})?that.entity.{{{name}}}.slice():[];that.entity.{{{name}}}=old.concat(uploadedResults);}); },
""");
        var media = kind == "Video" ? "file-mediatype='video' file-extname='mp4'" : kind == "Upload" ? "file-mediatype='all'" : ":imageStyles='$imageStyles'";
        return $"<uni-file-picker {media} :disabled='{ro}' {(kind == "Image" ? "return-type='object'" : $"limit='{limit}'")} :readonly='{ro}' v-model='entity.{name}' title='{Html(description)}' @select='{name}FileSelect'></uni-file-picker>" + Hidden(f);
    }
    /// <summary>级联原定义和事件直接移植，依赖原固定级联/路径端点，未知定义不生成空壳。</summary>
    private static string Cascade(OriginalDetailContext c, OriginalDetailField f, string ro)
    {
        var d=Object(f.Detail,"级联"); var name=f.Column.JsonName; var mode=Property(d,"sourceMode","crossTable");
        if(mode is not ("selfTree" or "crossTable"))throw new InvalidOperationException("原级联sourceMode无效。");
        var leafName=GenerationModel.Camel(Stem(f.Column)+"_name");
        if(leafName==name)throw new InvalidOperationException("原级联叶子ID和名称不能使用同一属性。");
        c.Defaults.Append(leafName).Append(":'',");
        var display=$"readonlyDisplayText(entity.{leafName})";
        if(mode=="crossTable")
        {
            if(!d.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array||levels.GetArrayLength()==0)throw new InvalidOperationException("原跨表级联必须包含levels。");
            var names=new List<string>(); foreach(var level in levels.EnumerateArray()) { var prefix=Property(level,"columnPrefix",Property(level,"table")); var id=Property(level,"idPropertyName",GenerationModel.Camel(prefix+"_id")); var label=Property(level,"namePropertyName",GenerationModel.Camel(prefix+"_name")); if(!Regex.IsMatch(id,"^[A-Za-z_][A-Za-z0-9_]*$")||!Regex.IsMatch(label,"^[A-Za-z_][A-Za-z0-9_]*$"))throw new InvalidOperationException("级联需要已经解析的属性定义。"); c.Defaults.Append(id).Append(":'',").Append(label).Append(":'',"); names.Add("entity."+label); } display=$"readonlyDisplayText([{string.Join(',',names)}], {Html(Js(Property(d,"displayJoiner","/")))})";
        }
        c.Methods.AppendLine($"receiveCascadeTree{f.Column.Source.Id}(value) {{ if(this.{ro})return; this.entity=Object.assign({{}},this.entity,value||{{}}); }},");
        return Choice(ro,display,$"<yzp-cascade-tree :value='entity' :definition='{Html(d.GetRawText())}' request-url='{c.Api}/cascadeTreeOptions{f.Column.Source.Id}' path-request-url='{c.Api}/cascadeTreePath{f.Column.Source.Id}' leaf-id-property='{name}' leaf-name-property='{leafName}' placeholder=\"请选择{Html(f.Column.Source.Name)}\" :readonly='{ro}' primary-color=\"{c.PrimaryColor}\" @input='receiveCascadeTree{f.Column.Source.Id}' />");
    }
}
