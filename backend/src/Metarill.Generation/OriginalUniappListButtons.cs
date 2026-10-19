using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>恢复原列表导入导出、批量勾选与元数据按钮，不另建工具栏。</summary>
internal static partial class OriginalUniappListRenderer
{
    private sealed record ListButtons(string Markup, string Grid, string Methods, bool HasGrid, bool Batch);

    /// <summary>原按钮显示方案和确认文案属于页面合同，固定端点由后端再次鉴权。</summary>
    private static ListButtons Buttons(GenerationModel model, TableModel table, PageModel page, int kind)
    {
        var allowed = model.Catalog.ConfigButtons.Where(button => ObjText(button,"configPageId") == page.Source.Id).OrderBy(button => ObjInt(button,"sortNo")).ThenBy(button => ObjText(button,"id"),StringComparer.Ordinal).ToArray();
        var grid = allowed.Where(button => ObjText(button,"place") == "EditAndGrid" && ObjText(button,"visibleScheme","Normal") is "Normal" or "AlwaysVisible" or "VisibleWhenWritable" or "OnlyDeve").ToArray();
        var top = allowed.Where(button => ObjText(button,"place") == "List" && ObjText(button,"visibleScheme","Normal") is "Normal" or "AlwaysVisible").ToArray();
        var batch = kind == 2 || top.Any(button => ObjInt(button,"isBatchSelectButton") == 1);
        var markup = new StringBuilder(); var cells = new StringBuilder(); var methods = new StringBuilder();
        var endpoint = "/admin/auto/" + table.Source.TableName + "/" + page.Source.Id;
        var child = page.Scopes.Any(scope => scope.Source == "parent");
        if (ExportRenderer.Enabled(page))
        {
            markup.Append("<button size='mini' ").Append(child ? "" : "v-if=\"permissionVersion >= 0 && $yzp.hasPagePermission(page_sn, 'is_export')\" ").Append("@click='initExport'>导出</button>");
            methods.Append("initExport(){var that=this;yzp.request({url:").Append(J(endpoint+"/initExport")).Append(",data:{menuId:this.menu_id,filters:this.filters,parentId:this.parent_id},method:'POST',sendSessionId:true,success(res){if(res.code==200){var exportId=res.data;utils.openUrl(getApp().globalData.environment.base_url+'/admin/sys/export/export?exportId='+exportId);}else{uni.showModal({showCancel:false,content:res.msg});}}});},");
        }
        if (ImportRenderer.Enabled(page))
        {
            markup.Append("<button size='mini' @click='initImport'>导入</button>");
            methods.Append("initImport(){var that=this;yzp.chooseImportFile(function(importData){yzp.request({url:").Append(J(endpoint+"/importDatas"+page.Source.Id)).Append(",data:{parentId:that.parent_id,requestJson:importData},method:'POST',sendSessionId:true,success(res){if(res.code==200)that.notifyMenuMutation();yzp.importSuccessAction(res,that);}});});},");
        }
        foreach (var button in grid)
        {
            var id=ButtonId(button); var handler="customerButtonHandler"+id; var visible="customerButtonShow"+id;
            var config=J(new {buttonId=id,visibleScheme=ObjText(button,"visibleScheme","Normal"),visibleRuleJson=ObjText(button,"visibleRuleJson")});
            cells.Append('\n').Append(Link(ObjText(button,"name"),visible+"(item)",handler+"(item)",ButtonType(button)));
            methods.Append(visible).Append("(item){this.permissionVersion;return yzp.isButtonVisible(").Append(config).Append(",item,this.readOnlyCondition);},");
            methods.Append(handler).Append("(item){var that=this;this.clicked_id=item.id;").Append(ConfirmStart(button));
            methods.Append("yzp.request({url:'/admin/sys/config_button/execute',data:{buttonId:").Append(J(id)).Append(",entityId:item.id,visibleScheme:").Append(J(ObjText(button,"visibleScheme","Normal"))).Append(",visibleRuleJson:").Append(J(ObjText(button,"visibleRuleJson"))).Append("},sendSessionId:true,method:'POST',success(res){if(res.code==200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,").Append(B(ObjInt(button,"isAutoCloseAfterAction")==1)).Append(",'list');}});").Append(ConfirmEnd(button)).Append("},");
        }
        foreach (var button in top)
        {
            var id=ButtonId(button); var handler="customerButtonHandler"+id;
            markup.Append("<button size='mini' type='").Append(ButtonType(button)).Append("' v-if=\"permissionVersion >= 0 && $yzp.hasButtonPermission('").Append(id).Append("')\" @click='").Append(handler).Append("'>").Append(Html(ObjText(button,"name"))).Append("</button>");
            methods.Append(handler).Append("(){var that=this;");
            if (ObjInt(button,"isBatchSelectButton")==1) methods.Append("if(!that.selectedList.length){uni.showModal({showCancel:false,content:'请至少选择一条数据'});return;}");
            methods.Append(ConfirmStart(button));
            var target=ObjText(button,"sql");
            switch (ObjInt(button,"actionType"))
            {
                case 1:
                    methods.Append("yzp.request({url:").Append(J(target+"?parentId=")).Append("+that.parent_id,sendSessionId:true,method:'POST',");
                    if (ObjInt(button,"isBatchSelectButton")==1) methods.Append("data:that.selectedList,");
                    methods.Append("success(res){if(res.code==200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,false,'list');}});"); break;
                case 2: methods.Append("uni.navigateTo({url:").Append(J("/"+target)).Append(",success:()=>{project.").Append(handler).Append("(that.entity);}});"); break;
                case 3: methods.Append("window.open(").Append(target.StartsWith("http",StringComparison.Ordinal) ? J(target) : "location.href.split('pages')[0]+"+J(target)).Append(",'_blank');"); break;
            }
            methods.Append(ConfirmEnd(button)).Append("},");
        }
        if(batch && kind!=2) methods.Append(BatchMethods);
        return new(markup.ToString(),cells.ToString(),methods.ToString(),grid.Length>0,batch);
    }

    private const string BatchMethods = """
    // 批量按钮沿用原行草稿，翻页后重标记，成功动作仍由后端再次验证每一行。
    checkIsAllSelect(){this.isSelectAll=this.list.length>0&&this.list.every(item=>item.isSelect===true);},
    selectAll(){this.isSelectAll=!this.isSelectAll;for(const item of this.list){item.isSelect=this.isSelectAll;this.selectedList=this.selectedList.filter(row=>row.id!==item.id);if(this.isSelectAll)this.selectedList.push(item);}},
    chkChange(index){var item=this.list[index];item.isSelect=!item.isSelect;if(item.isSelect)this.selectedList.push(item);else this.selectedList=this.selectedList.filter(row=>row.id!==item.id);this.checkIsAllSelect();},
    sureClose(){this.$emit('onPopClose');},
    sureSelect(){this.$emit('onPopSelect',this.selectedList);this.sureClose();},
    """;
    private static string ObjText(JsonElement value,string key,string fallback="")=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var item)&&item.ValueKind!=JsonValueKind.Null ? item.ToString() : fallback;
    private static int ObjInt(JsonElement value,string key)=>int.TryParse(ObjText(value,key),out var number)?number:0;
    /// <summary>按钮ID进入JS标识符前校验，名称与URL均走JSON编码。</summary>
    private static string ButtonId(JsonElement button)
    {
        var id=ObjText(button,"id");
        if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^[A-Za-z0-9_]+$"))throw new InvalidOperationException("原按钮标识包含不能生成方法名的字符。");
        return id;
    }
    private static string ButtonType(JsonElement button)=>ObjInt(button,"type")switch{1=>"primary",2=>"warn",_=>"default"};
    private static string ConfirmStart(JsonElement button)=>ObjInt(button,"isNeedConfirm")==1?"uni.showModal({content:"+J(ObjText(button,"confirmMsg").Length>0?ObjText(button,"confirmMsg"):"您确定"+ObjText(button,"name")+"吗？")+",success(modal){if(modal.confirm){":"";
    private static string ConfirmEnd(JsonElement button)=>ObjInt(button,"isNeedConfirm")==1?"}}});":"";
}
