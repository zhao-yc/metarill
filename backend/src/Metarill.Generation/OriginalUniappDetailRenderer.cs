using Metarill.Core.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Metarill.Generation.OriginalDetailContext;

namespace Metarill.Generation;

/// <summary>原v8详情模板的C#渲染入口；DOM与操作协议来自原GenerateDetail，仅增加必要身份/生命周期边界。</summary>
internal static class OriginalUniappDetailRenderer
{
    private const string ReadOnlyObjectPath="""
    // 只读取元数据声明的JSON对象自身属性，兼容旧接口的对象文本回显。
    originalReadOnlyPath(field, path) {
      let value = this.entity[field];
      if (typeof value === 'string') {
        try { value = JSON.parse(value); } catch { return undefined; }
      }
      for (const key of path) {
        if (!value || typeof value !== 'object' || Array.isArray(value) || !Object.prototype.hasOwnProperty.call(value, key)) return undefined;
        value = value[key];
      }
      return value;
    },
""";
    private const string ReadOnlyIntegerComparison="""
    // int64 响应使用字符串，避免 JS Number 舍入相邻ID。正负号、小数边界及 null 按服务端规则处理。
    originalReadOnlyCompare(operation, left, right) {
      const read = (value) => {
        if (value === null || value === '') value = '0';
        if (typeof value === 'number' && Number.isSafeInteger(value)) value = String(value);
        if (typeof value !== 'string' || !/^-?[0-9]+(?:\.[0-9]+)?$/.test(value)) throw new Error('只读条件的数字字段无效');
        const negative = value.startsWith('-');
        const [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.');
        const integer = whole.replace(/^0+/, '') || '0';
        const decimal = fraction.replace(/0+$/, '');
        return { integer, decimal, negative: negative && (integer !== '0' || decimal !== '') };
      };
      const equality = ['==', '===', '!=', '!=='].includes(operation);
      if (equality && (left === null || right === null)) return operation.startsWith('!') !== (left === right);
      const a = read(left), b = read(right);
      let order;
      if (a.negative !== b.negative) order = a.negative ? -1 : 1;
      else {
        const length = Math.max(a.decimal.length, b.decimal.length);
        const x = a.decimal.padEnd(length, '0'), y = b.decimal.padEnd(length, '0');
        order = a.integer.length - b.integer.length ||
          (a.integer === b.integer ? (x === y ? 0 : x < y ? -1 : 1) : a.integer < b.integer ? -1 : 1);
        if (a.negative) order = -order;
      }
      switch (operation) {
        case '==': case '===': return order === 0;
        case '!=': case '!==': return order !== 0;
        case '>': return order > 0;
        case '>=': return order >= 0;
        case '<': return order < 0;
        case '<=': return order <= 0;
        default: throw new Error('只读比较未发布');
      }
    },
""";
    private const string ReadOnlyBooleanOption = """
    // 原布尔选择可回显 0／1，条件只使用底层布尔值及明确的空值。
    originalReadOnlyBoolean(value) {
      if (value === true || value === 1 || value === '1') return true;
      if (value === false || value === 0 || value === '0') return false;
      if (value === null) return null;
      if (!this.id) {
        // 原新增页可用空字符串及文本配置值初始化表单。
        if (value === undefined || value === '') return null;
        if (value === 'true') return true;
        if (value === 'false') return false;
      }
      throw new Error('只读条件的布尔字段无效');
    },
""";
    private const string ReadOnlyEvaluationGuard = """
    // 已装载字段不能按原类型求值时，保持详情只读。
    originalCheckedReadOnly(check) {
      try { return Boolean(check()); } catch { return true; }
    },
""";
    /// <summary>完整渲染单个原详情页，不写生成产物，不依赖旧Java服务或开发机绝对路径。</summary>
    internal static string Render(GenerationModel model, TableModel table, PageModel page, IReadOnlyList<ChildModel> children)
    {
        var c=new OriginalDetailContext(model,table,page,children);var layout=Layout(page);var definition=DetailLayoutConfiguration.Parse(layout);
        var tabs=definition.Mode=="tabs";var groups=definition.Groups.Select(group=>group.Key).ToArray();
        // 隐藏字段与关闭布局也遵守同一引用规则，禁止生成时静默移到常显区。
        foreach(var field in model.Catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==page.Source.Id))
            definition.RequireGroup(Text(field.Extra,"detailGroupKey"));
        var fields=page.DetailColumns.Select(c.Field).ToArray();
        if(tabs)fields=fields.OrderBy(field=>Array.IndexOf(groups,field.GroupKey)).ToArray();
        var inserted=false;
        foreach(var field in fields)
        {
            var group=groups.Contains(field.GroupKey,StringComparer.Ordinal)?field.GroupKey:"";
            if(tabs && group.Length>0 && !inserted){c.Fields.AppendLine(GroupTabs);inserted=true;}
            c.FieldsMetadata.Add(field with {GroupKey=group});OriginalUniappDetailFields.Render(c,field,group,tabs);
        }
        if(c.RemoteOptions.Count>0)
        {
            c.Imports.AppendLine("import { createOriginalRemoteOptionsMixin } from '@/common/yzp_runtime/originalRemoteOptionsMixin';");
            c.Mixins.Add("createOriginalRemoteOptionsMixin(["+string.Join(',',c.RemoteOptions)+"])");
        }
        if(tabs)
        {
            c.Imports.AppendLine("import { createDetailLayoutMixin } from '@/common/yzp_runtime/detailLayoutRuntime';");
            var layoutFields = c.FieldsMetadata.Select(field =>
            {
                var rule = Text(field.Column.Source.Extra, "visibleCondition");
                var jsonFields = c.VisibilityJsonFields(rule);
                var options = new Dictionary<string, object> { ["name"] = field.Column.JsonName, ["groupKey"] = field.GroupKey, ["visibleCondition"] = rule };
                if (jsonFields.Length > 0) options.Add("jsonObjectFields", jsonFields);
                return options;
            });
            c.Mixins.Insert(0, "createDetailLayoutMixin(" + layout.GetRawText() + "," + Js(layoutFields) + ")");
        }
        var custom=OriginalUniappDetailButtons.Render(c);var position=Integer(page.Source.Extra,"detailPageButtonsPosition",0);if(position is not(0 or 1))throw new InvalidOperationException("原详情按钮位置仅支持0（固定底部）或1（子表前）。");
        var readOnly=PageReadOnlyCompilation.Compile(page);
        if(readOnly?.UsesObjectPaths==true)c.Methods.AppendLine(ReadOnlyObjectPath);
        if(readOnly?.UsesIntegerComparisons==true)c.Methods.AppendLine(ReadOnlyIntegerComparison);
        if (readOnly?.UsesBooleanOptions == true) c.Methods.AppendLine(ReadOnlyBooleanOption);
        if (readOnly is { UsesIntegerComparisons: true } or { UsesBooleanOptions: true })
            c.Methods.AppendLine(ReadOnlyEvaluationGuard);
        c.Computed.AppendLine($$"""
parentId() { return this.parent_id || ''; },
originalChildrenReady() { return this.recordReady && this.permissionsReady && Boolean(this.id) && originalRecordId(this.entity.id)===this.id; },
readOnlyCondition() { this.permissionVersion;return !this.permissionsReady || Boolean(this.id && !this.recordReady) || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.page_is_function_table ? !this.page_is_update : this.id ? !this.page_is_update : !this.page_is_new)) || !yzp.hasPagePermission(this.page_sn,this.page_is_function_table || this.id ? 'is_update' : 'is_new'){{(readOnly is not null ? " || Boolean("+readOnly.JavaScript+")" : "")}}; },
""");
        c.Data.Append($"page_is_update:{ChildrenCompilation.CanUpdate(page).ToString().ToLowerInvariant()},page_is_save:{ChildrenCompilation.CanUpdate(page).ToString().ToLowerInvariant()},page_is_delete:{(table.Source.IsFunctionTable==0&&page.Source.IsDelete==1).ToString().ToLowerInvariant()},page_is_new:{(table.Source.IsFunctionTable==0&&page.Source.IsNew==1).ToString().ToLowerInvariant()},page_is_copy:{(table.Source.IsFunctionTable==0&&page.Source.IsCopy==1).ToString().ToLowerInvariant()},page_is_function_table:{(table.Source.IsFunctionTable==1).ToString().ToLowerInvariant()},detailGridColumns:{c.GridColumns},permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{{}},");
        var childMarkup=Children(c);var values=new Dictionary<string,string>
        {
            ["##mobile_nav_bar##"]=$"<view v-if=\"!is_props\"><uni-nav-bar left-icon=\"left\" @clickLeft=\"$yzp.back\" fixed statusBar v-if='!readOnlyCondition' :border=\"false\" title=\"{Html(page.Source.Name)}\" @clickRight=\"showCustomerButtons\" :rightIcon=\"hasVisibleCustomerButtons ? 'more-filled' : ''\"></uni-nav-bar><uni-nav-bar left-icon=\"left\" @clickLeft=\"$yzp.back\" fixed statusBar v-else :border=\"false\" title=\"{Html(page.Source.Name)}\"></uni-nav-bar></view>",
            ["##edit_columns##"]=c.Fields.ToString(),["##child_tables##"]=c.Popups+childMarkup,
            ["##detail_page_top_buttons##"]=position==1?OriginalUniappDetailButtons.Container(custom,true,"!is_props"):"",
            ["##detail_page_embedded_top_buttons##"]=position==1?"<view v-if=\"is_props\">"+OriginalUniappDetailButtons.Container(custom,true)+"</view>":"",
            ["##detail_page_bottom_buttons##"]=position==0?"<view style=\"height:150px;\" v-if=\"!is_props\"></view><view>"+OriginalUniappDetailButtons.Container(custom,false)+"</view>":"",
            ["//##detail_layout_import##"]=c.Imports+"\nconst originalRecordId=value=>typeof value==='string' && !['null','undefined'].includes(value.trim())?value.trim():'';",
            ["//##detail_layout_mixin##"]=c.Mixins.Count==0?"":"mixins:["+string.Join(',',c.Mixins)+"],",
            ["//##import_child_tables_component##"]="",["//##defined_child_tables_component##"]=c.Components.ToString(),
            ["//##other_return_data##"]=c.Data.ToString(),["//##lists_list_and_index##"]="",["//##const_list_column_ranges##"]="",
            ["//##entity_columns##"]=c.Defaults.ToString(),["//##entity_column_readonly##"]=c.Readonly.ToString(),["//##entity_rules##"]=c.Rules.ToString(),
            ["//##watch_columns##"]=c.Watches.ToString(),["//##original_option_watchers##"]=c.OptionWatchers.ToString(),["//##computed_columns##"]=c.Computed.ToString(),["//##computed_buttons##"]="",
            ["//##customer_button_handlers##"]=c.Methods.ToString(),["//##rtf_edit_function##"]="",["//##img_component_events##"]="",["//##lists_select_change##"]="",
            ["//##init_lists##"]=c.Init.ToString(),["//##init_dynamic_lists##"]=c.DynamicInit.ToString(),["//##get_entity_img_to_ary##"]="",
            ["//##refresh_child_tables##"]=string.Join("\n",children.Select(child=>$"this.$refs.ChildList{child.Id}?.refreshData();")),
            ["//##insert_parent_id_value##"]=string.Join("\n",page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>$"that.entity.{scope.Column.JsonName}=that.parent_id;")),
            ["//##detail_layout_validation_error##"]=tabs?"this.revealDetailValidationError(err);":"",
            ["//##original_methods##"]=Runtime(c),["##table_name##"]=table.Source.TableName,["##table_camel_name##"]=GenerationModel.Camel(table.Source.TableName),["##page_sn##"]=page.Source.Id,
            ["##$uni-primary##"]=c.PrimaryColor,["##is_auto_back_on_mobile_save##"]=Enabled(page.Source.Extra,"isAutoBackOnMobileSave").ToString().ToLowerInvariant()
        };
        var result=Template("original-detail.vue.tpl");foreach(var (marker,value)in values)result=result.Replace(marker,value,StringComparison.Ordinal);
        if(Regex.IsMatch(result,"##[A-Za-z_$][A-Za-z0-9_$]*##"))throw new InvalidOperationException("原详情模板仍有未填充占位符。");return result;
    }
    /// <summary>读取原布局JSON且保留原模板文本，结构和引用统一交给共享规则验证。</summary>
    private static JsonElement Layout(PageModel page)
    {
        var text=Text(page.Source.Extra,"detailLayout");var value=Object(text.Length==0?"{\"version\":1,\"mode\":\"none\",\"groups\":[]}":text,"布局");
        return value;
    }
    /// <summary>原主子Tab/顺排结构，只有真实已读/已存父记录才能挂载；刷新子页不会写父表。</summary>
    private static string Children(OriginalDetailContext c)
    {
        if(c.Children.Count==0)return "";var tabs=ChildrenCompilation.Tabs(c.Page);var output=new StringBuilder("<view v-if=\"originalChildrenReady\">");
        if(tabs){c.Data.Append("sonItems:").Append(Js(c.Children.Select(child=>child.Page.Source.Name))).Append(",currentSonItem:0,");c.Methods.AppendLine("onClickSonItem(e) { if(this.currentSonItem!==e.currentIndex)this.currentSonItem=e.currentIndex; },");output.Append("<view class=\"yzp-child-tabs\"><scroll-view class=\"yzp-child-tabs__scroll\" scroll-x><view class=\"yzp-child-tabs__inner\"><view v-for=\"(item,index) in sonItems\" :key=\"index\" class=\"yzp-child-tabs__item\" :class=\"{'is-active':currentSonItem===index}\" @click=\"onClickSonItem({currentIndex:index})\"><text class=\"yzp-child-tabs__text\">{{item}}</text></view></view></scroll-view></view><view class=\"yzp-child-content\">");}
        for(var index=0;index<c.Children.Count;index++){var child=c.Children[index];c.Import(child.Component,$"@/pages/yzp/{child.Table.Source.TableName}-list-{child.Page.Source.Id}.vue");output.Append(tabs?$"<view v-show=\"currentSonItem === {index}\">":"").Append($"<{child.Component} :key=\"id + ':{child.Id}'\" :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='{child.OpenType}' ref='{child.Component}' />").Append(tabs?"</view>":"");}
        if(tabs)output.Append("</view>");return output.Append("</view><view v-else class='section-view'>为避免数据丢失，请先保存当前页面的数据，再维护子表。</view>").ToString();
    }
    /// <summary>原单表单标签模板，主表字段不因切组卸载。</summary>
    private const string GroupTabs="<view class='yzp-detail-groups' v-if='visibleDetailGroups.length'><scroll-view class='yzp-detail-groups__scroll' scroll-x :show-scrollbar='false' :scroll-into-view=\"'yzp-detail-group-' + activeDetailGroupKey\" scroll-with-animation><view class='yzp-detail-groups__inner' role='tablist'><view v-for='group in visibleDetailGroups' :key='group.key' :id=\"'yzp-detail-group-' + group.key\" class='yzp-detail-groups__item' :class=\"{'is-active':activeDetailGroupKey===group.key}\" role='tab' :aria-selected='activeDetailGroupKey===group.key' @click='selectDetailGroup(group.key)'>{{group.name}}</view></view></scroll-view></view>";
    /// <summary>嵌入原模板保持源码/CLI可搬移，不读取开发机器上的旧YZP文件。</summary>
    private static string Template(string name){var assembly=typeof(OriginalUniappDetailRenderer).Assembly;using var input=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(resource=>resource.EndsWith("."+name,StringComparison.Ordinal)))!;using var reader=new StreamReader(input);return reader.ReadToEnd();}
    /// <summary>请求边界保留原整entity协议，只隔离在途请求、身份变化及未保存父记录，不裁剪字段。</summary>
    private static string Runtime(OriginalDetailContext c)=>Template("original-detail.methods.js.tpl").Replace("##defaults##",c.Defaults.ToString(),StringComparison.Ordinal).Replace("##api##",Js(c.Api),StringComparison.Ordinal).Replace("##writable##",Js(c.Page.Writable.Select(column=>column.JsonName)),StringComparison.Ordinal);
}
