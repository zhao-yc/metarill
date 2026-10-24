using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Metarill.Generation.OriginalDetailContext;

namespace Metarill.Generation;

/// <summary>原v8详情模板的C#渲染入口；DOM与操作协议来自原GenerateDetail，仅增加必要身份/生命周期边界。</summary>
internal static class OriginalUniappDetailRenderer
{
    /// <summary>完整渲染单个原详情页，不写生成产物，不依赖旧Java服务或开发机绝对路径。</summary>
    internal static string Render(GenerationModel model, TableModel table, PageModel page, IReadOnlyList<ChildModel> children)
    {
        var c=new OriginalDetailContext(model,table,page,children); var layout=Layout(page);var tabs=Property(layout,"mode")=="tabs";
        var groups=layout.GetProperty("groups").EnumerateArray().Select(group=>Property(group,"key")).ToArray();
        var fields=page.DetailColumns.Select(c.Field).ToArray();
        if(tabs)fields=fields.OrderBy(field=>Array.IndexOf(groups,field.GroupKey)).ToArray();
        var inserted=false;
        foreach(var field in fields)
        {
            var group=groups.Contains(field.GroupKey,StringComparer.Ordinal)?field.GroupKey:"";
            if(tabs && group.Length>0 && !inserted){c.Fields.AppendLine(GroupTabs);inserted=true;}
            c.FieldsMetadata.Add(field with {GroupKey=group});OriginalUniappDetailFields.Render(c,field,group,tabs);
        }
        if(tabs)
        {
            c.Imports.AppendLine("import { createDetailLayoutMixin } from '@/common/yzp_runtime/detailLayoutRuntime';");
            c.Mixins.Insert(0,"createDetailLayoutMixin("+layout.GetRawText()+","+Js(c.FieldsMetadata.Select(field=>new{name=field.Column.JsonName,groupKey=field.GroupKey,visibleCondition=Text(field.Column.Source.Extra,"visibleCondition")}))+")");
        }
        var custom=OriginalUniappDetailButtons.Render(c);var position=Integer(page.Source.Extra,"detailPageButtonsPosition",0);if(position is not(0 or 1))throw new InvalidOperationException("原详情按钮位置仅支持0（固定底部）或1（子表前）。");
        c.Computed.AppendLine($$"""
parentId() { return this.parent_id || ''; },
originalChildrenReady() { return this.recordReady && this.permissionsReady && Boolean(this.id) && originalRecordId(this.entity.id)===this.id; },
readOnlyCondition() { this.permissionVersion;return !this.permissionsReady || Boolean(this.id && !this.recordReady) || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.page_is_function_table ? !this.page_is_update : this.id ? !this.page_is_update : !this.page_is_new)) || !yzp.hasPagePermission(this.page_sn,this.page_is_function_table || this.id ? 'is_update' : 'is_new'){{(ChildrenCompilation.ReadonlyField(page) is {} readOnly ? " || Boolean(this.entity."+readOnly+")" : "")}}; },
""");
        c.Data.Append($"page_is_update:{ChildrenCompilation.CanUpdate(page).ToString().ToLowerInvariant()},page_is_save:{ChildrenCompilation.CanUpdate(page).ToString().ToLowerInvariant()},page_is_delete:{(page.Source.IsDelete==1).ToString().ToLowerInvariant()},page_is_new:{(page.Source.IsNew==1).ToString().ToLowerInvariant()},page_is_copy:{(page.Source.IsCopy==1).ToString().ToLowerInvariant()},page_is_function_table:{(table.Source.IsFunctionTable==1).ToString().ToLowerInvariant()},detailGridColumns:{c.GridColumns},permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{{}},");
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
    /// <summary>直接移植原详情分组协议，关闭tabs仍保留定义；未知版本或重复key拒绝。</summary>
    private static JsonElement Layout(PageModel page)
    {
        var text=Text(page.Source.Extra,"detailLayout");var value=Object(text.Length==0?"{\"version\":1,\"mode\":\"none\",\"groups\":[]}":text,"布局");
        if(!value.TryGetProperty("version",out var version)||Integer(version,0)!=1||Property(value,"mode") is not("none" or "tabs")||!value.TryGetProperty("groups",out var groups)||groups.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("原详情布局必须包含version=1、mode和groups。");
        var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var group in groups.EnumerateArray()){var key=Property(group,"key");var name=Property(group,"name");if(!Regex.IsMatch(key,"^[A-Za-z][A-Za-z0-9_-]{0,63}$")||!keys.Add(key)||string.IsNullOrWhiteSpace(name)||name.Length>100)throw new InvalidOperationException("原详情分组key或名称无效。");}return value;
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
