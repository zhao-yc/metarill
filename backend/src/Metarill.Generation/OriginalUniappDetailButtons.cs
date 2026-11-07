using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Metarill.Generation.OriginalDetailContext;

namespace Metarill.Generation;

/// <summary>原详情自定义按钮、先保存、确认、三类动作与移动端更多菜单，元数据不决定后端授权。</summary>
internal static class OriginalUniappDetailButtons
{
    /// <summary>生成原按钮插槽并保留按sortNo显示顺序，隐藏按钮不生成可见入口。</summary>
    internal static string Render(OriginalDetailContext c)
    {
        var buttons=c.Model.Catalog.ConfigButtons.Where(value=>value.ValueKind==JsonValueKind.Object && Property(value,"configPageId")==c.Page.Source.Id && Property(value,"place") is "Edit" or "EditAndGrid" && Property(value,"visibleScheme","Normal") is "Normal" or "AlwaysVisible" or "VisibleWhenWritable" or "OnlyDeve")
            .OrderBy(value=>value.TryGetProperty("sortNo",out var order)?Integer(order,0):0).ThenBy(value=>Property(value,"id"),StringComparer.Ordinal).ToArray();
        var output=new StringBuilder(); var descriptors=new List<object>();
        if(c.Page.Source.IsCopy==1)output.AppendLine("<button type='primary' size='mini' v-if=\"page_is_copy && !readOnlyCondition && $yzp.hasPagePermission(page_sn,'is_all')\" @click='copyEntity()'>复制</button>");
        foreach(var button in buttons)
        {
            var id=Property(button,"id"); if(!Regex.IsMatch(id,"^[A-Za-z0-9_]+$"))throw new InvalidOperationException("原详情按钮标识无效。");
            var name=Property(button,"name"); var action=button.TryGetProperty("actionType",out var type)?Integer(type,1):1; var save=Property(button,"saveOption","NotSave");
            if(action is <1 or >3)throw new InvalidOperationException("原GenerateDetail没有该按钮动作的实现，不能输出无效按钮："+id+"/"+action);
            if(save is not ("Save" or "NotSave"))throw new InvalidOperationException("原详情按钮保存选项无效。");
            var style=button.TryGetProperty("type",out var t)?Integer(t,0):0;if(style is <0 or >2)throw new InvalidOperationException("原详情按钮样式无效。");
            var config=new {buttonId=id,visibleScheme=Property(button,"visibleScheme","Normal"),visibleRuleJson=Property(button,"visibleRuleJson")};
            c.Computed.AppendLine($"customerButtonShow{id}() {{ this.permissionVersion;return yzp.isButtonVisible({Js(config)},this.entity,this.readOnlyCondition); }},");
            output.AppendLine($"<button size='mini' type='{(style==1?"primary":style==2?"warn":"default")}' v-if='customerButtonShow{id}' @click='customerButtonHandler{id}'>{Html(name)}</button>");
            c.Methods.AppendLine($"customerButtonHandler{id}() {{ this.runOriginalButton({Js(id)}); }},");
            descriptors.Add(new {id,name,action,save,confirm=button.TryGetProperty("isNeedConfirm",out var confirm)&&Integer(confirm,0)==1,confirmMsg=Property(button,"confirmMsg","您确定"+name+"吗？"),target=Property(button,"sql"),close=button.TryGetProperty("isAutoCloseAfterAction",out var close)&&Integer(close,0)==1});
        }
        c.Data.Append("originalButtons:").Append(Js(descriptors)).Append(',');
        c.Computed.AppendLine("hasVisibleCustomerButtons() { this.permissionVersion;return this.originalButtons.some(button=>this['customerButtonShow'+button.id]); },");
        c.Methods.AppendLine("showCustomerButtons() { const current=this.originalContext(),actions=this.originalButtons.filter(button=>this['customerButtonShow'+button.id]);if(!actions.length){uni.showToast({title:'暂无可用操作',icon:'none'});return;}uni.showActionSheet({itemList:actions.map(button=>button.name),success:res=>{if(current()&&actions[res.tapIndex])this['customerButtonHandler'+actions[res.tapIndex].id]();}}); },");
        return output.ToString();
    }
    /// <summary>原表单卡片页脚/弹窗底栏/固定底栏布局，关闭入口始终存在。</summary>
    internal static string Container(string custom, bool beforeChildren, string visible="") => "<view"+(visible.Length>0?$" v-if=\"{visible}\"":"")+" class='bottom-buttons"+(beforeChildren?" is-before-child":"")+"' :class=\"{ 'is-embedded': is_props }\">"
        +"<button type='warn' size=\"mini\" v-if=\"!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')\" @click='deleteEntity()'>删除</button>"
        +"<button type='primary' size=\"mini\" v-if=\"!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')\" @click='saveEntity()'>保存</button>"
        +"<button type='default' size=\"mini\" @click=\"closeDetail()\">关闭</button>"+custom+"</view>";
}
