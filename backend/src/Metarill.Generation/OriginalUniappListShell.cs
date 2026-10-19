using System.Text;

namespace Metarill.Generation;

/// <summary>原列表的组件生命周期、子表弹窗和权限入口。</summary>
internal static partial class OriginalUniappListRenderer
{
    /// <summary>后台固定端点复用原状态机；选择桥接只替换内部完整回读。</summary>
    private static string ScriptTop(TableModel table, PageModel page, string detail, bool importDetail, bool selection, bool child)
    {
        var endpoint = "/admin/auto/" + table.Source.TableName + "/" + page.Source.Id;
        var result = new StringBuilder("import utils from '@/common/utils';\nimport yzp from '@/common/yzp_runtime/yzp-runtime';\nimport project from '@/common/project';\nimport { menuBoolean } from '@/common/yzp_runtime/menuRuntime';\nimport { createListPageMixin } from '@/common/yzp_runtime/listPageRuntime';\n");
        if (importDetail) result.Append("import ").Append(detail).Append(" from '@/pages/yzp/").Append(table.Source.TableName).Append("-detail-").Append(page.Source.Id).AppendLine(".vue';");
        if (selection) result.AppendLine("import { createOriginalSelectionPageMixin } from '@/common/yzp_runtime/originalSelectionPageMixin';");
        result.Append("export default {mixins:[createListPageMixin({url:").Append(J(endpoint + "/page")).Append(",pageSize:").Append(page.Source.PageSize).Append(",request(options){return yzp.request(options);},onParentChanged(){");
        if (!selection) result.Append("if(Array.isArray(this.selectedList)){this.selectedList=[];this.isSelectAll=false;}");
        if (importDetail) result.Append("if(this.closeChildDetailPopup)this.closeChildDetailPopup();");
        result.Append("},onLoaded(){if(this.syncOriginalSelectionRows)this.syncOriginalSelectionRows();else if(this.selectedList){for(const item of this.list)item.isSelect=this.selectedList.some(row=>String(row.id)===String(item.id));this.checkIsAllSelect();}}");
        if (selection) result.Append(",additionalData(){return {selectedIds:this.showSelectedOnly?this.selectedDraftIds.slice():[]};},emptyResult(){return this.showSelectedOnly&&!this.selectedDraftIds.length;}");
        result.Append("}),");
        if (selection) result.Append("createOriginalSelectionPageMixin(").Append(J(endpoint)).Append(',').Append(B(child)).Append("),");
        result.Append("],props:{parent_id:{type:String},parent_page_sn:{type:String},is_son_page:{type:Boolean,default:false},child_page_open_type:{type:Number,default:1},parentReadOnlyCondition:{type:Boolean,default:false}},");
        if (importDetail) result.Append("components:{").Append(detail).Append("},");
        return result.ToString();
    }

    /// <summary>保留原子表详情弹窗的容器、props与onChange/onPopClose事件。</summary>
    private static string Popup(PageModel page, string detail) => "<yzp-popup ref=\"child_detail_popup\" type=\"center\" @change=\"onChildDetailPopupChange\"><view class=\"select-page-dialog-view\"><uni-section title='" + Html(page.Source.Name) + "' class='select-page-uni-section' type='line'><view class='section-view'><view class=\"child-detail-dialog-view\" :class=\"{ 'is-mobile': !$deviceInfo.is_pc }\"><" + detail + " v-if=\"childDetailPopupVisible\" :key=\"childDetailPopupKey\" :is_props=\"true\" :props_id=\"childDetailPopupId\" :props_parent_id=\"parent_id\" :props_parent_page_sn=\"parent_page_sn\" :parentReadOnlyCondition=\"readOnlyCondition\" @onChange=\"handleChildDetailChange\" @onPopClose=\"closeChildDetailPopup\"></" + detail + "></view></view></uni-section></view></yzp-popup>";

    private const string PopupMethods = """
    // 弹窗关闭先卸载子详情，重复打开不能沿用上个实体状态。
    openChildDetailPopup(id){this.childDetailPopupId=id||'';this.childDetailPopupKey++;this.childDetailPopupVisible=true;this.$nextTick(()=>this.$refs.child_detail_popup.open('center'));},
    closeChildDetailPopup(){this.childDetailPopupVisible=false;this.childDetailPopupId='';this.$refs.child_detail_popup?.close();},
    onChildDetailPopupChange(e){if(!e.show){this.childDetailPopupVisible=false;this.childDetailPopupId='';}},
    handleChildDetailChange(change){this.handleDetailChange(change);},
    """;

    /// <summary>取消排序保留原order:null，由后端恢复元数据默认排序。</summary>
    private static string SortMethods(PageModel page) => string.Concat(page.Sortable.Select(column => column.JsonName + "SortChange(e){this.orderBy.column=" + J(column.JsonName) + ";this.orderBy.order=e.order;this.refreshData();},\n"));

    private static string Link(string text, string visible, string click, string type) => "<view class='list-action-link" + (type == "warn" ? " list-action-link--warn" : "") + "'" + (visible.Length == 0 ? "" : " v-if=\"" + Html(visible) + "\"") + " @click='" + Html(click) + "'>" + Html(text) + "</view>";
}
