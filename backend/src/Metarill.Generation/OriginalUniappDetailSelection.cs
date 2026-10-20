using System.Text.Json;
using System.Text.RegularExpressions;
using static Metarill.Generation.OriginalDetailContext;

namespace Metarill.Generation;

/// <summary>保留原选择页按钮、弹层、props及事件；多选仅在内部桥接既有完整回读/迟到保护。</summary>
internal static class OriginalUniappDetailSelection
{
    /// <summary>目标页必须来自同一批已发布元数据，不能把字符串配置当作任意路由。</summary>
    internal static string Render(OriginalDetailContext c, OriginalDetailField f, string ro)
    {
        var definition=SelectionControlDefinition.Resolve(f.Column,f.Settings);var pageId=definition.PageId;
        var target=c.Model.Tables.SelectMany(table=>table.Pages.Select(page=>(Table:table,Page:page))).SingleOrDefault(item=>item.Page.Source.Id==pageId);
        if(target.Page is null)throw new InvalidOperationException("原选择页目标不存在："+pageId);
        var nameField=definition.NameField; var targetName=target.Table.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField||column.JsonName==nameField);
        if(targetName is null||!target.Page.Readable.Any(column=>column.Source.Id==targetName.Source.Id))throw new InvalidOperationException("原选择页名称列未开放读取。");
        var button=definition.ButtonText; var id=f.Column.Source.Id; var field=f.Column.JsonName; var display=c.DisplayField(f.Column,targetName.Source.ColumnName); var component="OriginalSelection"+id;
        c.Import(component,$"@/pages/yzp/{target.Table.Source.TableName}-list-{pageId}.vue");
        var multi=SelectionPageRenderer.Enabled(target.Page);var targetParent=TargetParent(c,target.Page);
        if(multi)
        {
            var compiled=MultiSelectCompilation.Resolve(c.Model,c.Table,c.Page,f.Column);
            if(compiled.Definition is not {} known)throw new InvalidOperationException("原多选字段尚不能证明安全回读合同："+compiled.Reason);
            if(!c.Mixins.Any(item=>item.StartsWith("createMultiSelectFieldMixin(",StringComparison.Ordinal)))
            {
                c.Imports.AppendLine("import { createMultiSelectFieldMixin } from '@/common/yzp_runtime/multiSelectFieldMixin';");
                var all=MultiSelectCompilation.ForPage(c.Model,c.Table,c.Page);
                c.Mixins.Add("createMultiSelectFieldMixin("+Js(all.Select(item=>new {tableId=c.Table.Source.Id,columnId=item.Source.Source.Id,field=item.Source.JsonName,namesField=item.Names?.JsonName,nameField=item.Name.JsonName,sameParent=item.SameParent,targetParentField=item.TargetParentField,requiresTargetParent=item.TargetParents.Count>0,redundant=c.Table.Source.IsRongyu==1,writable=c.Page.Writable.Any(column=>column.Source.Id==item.Source.Source.Id),@ref="selectPageDialog"+item.Source.Source.Id}))+ ")");
            }
            display=known.Names?.JsonName??field;
            c.Methods.AppendLine($"openSelect{id}() {{ this.openMultiSelection({Js(field)}); }}, onPopClose{id}() {{ this.closeMultiSelection({Js(field)}); }}, onPopSelect{id}(rows) {{ this.permissionVersion++;if(!this.multiSelectionCurrent({Js(field)}))return;this.receiveMultiSelection({Js(field)},rows);if(!this.multiSelectionError)this.closeMultiSelection({Js(field)}); }},");
            c.Popups.AppendLine($"<yzp-popup ref=\"selectPageDialog{id}\" type=\"center\" @change=\"multiSelectionPopupChange('{field}', $event)\"><view class=\"select-page-dialog-view\"><{component} v-if=\"multiSelectionField === '{field}'\" :key=\"multiSelectionVersion\" ref='{id}' @onPopSelect=\"onPopSelect{id}\" @onPopClose=\"onPopClose{id}\" :selectedIds=\"entity.{field}\" :selectedNames=\"entity.{display}\" selected-name-field=\"{Html(targetName.JsonName)}\" :parent_id=\"multiTargetParent('{field}')\" :parentReadOnlyCondition=\"{ro} || !multiSelectionCurrent('{field}')\" /></view></yzp-popup>");
        }
        else
        {
            c.Data.Append("originalSelection").Append(id).Append(":false,");
            c.Methods.AppendLine($$$"""
openSelect{{{id}}}() { if(this.{{{ro}}})return; this._selection{{{id}}}=this.originalContext();this.originalSelection{{{id}}}=true;this.$nextTick(()=>{if(this._selection{{{id}}}?.())this.$refs.selectPageDialog{{{id}}}.open();}); },
// 主动关闭与popup关闭事件分开，真实close会同步发change，不能回调自身形成递归。
onPopClose{{{id}}}(closePopup=true) { const opened=this.originalSelection{{{id}}};this.originalSelection{{{id}}}=false;this._selection{{{id}}}=null;if(closePopup&&opened)this.$refs.selectPageDialog{{{id}}}?.close(); },
onPopSelect{{{id}}}(row) { this.permissionVersion++;if(this.{{{ro}}} || !this._selection{{{id}}}?.() || !row || !originalRecordId(row.id))return;this.entity.{{{field}}}=row.id;{{{(display==field?"":$"this.entity.{display}=row.{targetName.JsonName};")}}} this.onPopClose{{{id}}}(); },
""");
            c.Popups.AppendLine($"<yzp-popup ref=\"selectPageDialog{id}\" type=\"center\" @change=\"!$event.show && onPopClose{id}(false)\"><view class=\"select-page-dialog-view\"><{component} v-if=\"originalSelection{id}\" ref='{id}' @onPopSelect=\"onPopSelect{id}\" @onPopClose=\"onPopClose{id}\" :selectedIds=\"entity.{field}\" :selectedNames=\"entity.{display}\" selected-name-field=\"{Html(targetName.JsonName)}\" :parent_id=\"{targetParent}\" :parentReadOnlyCondition=\"{ro}\" /></view></yzp-popup>");
        }
        var buttons=multi ? $"<view style='width: 100%;'><uni-easyinput disabled type='textarea' primaryColor=\"{c.PrimaryColor}\" :model-value='readonlyDisplayText(entity.{display})'/><button v-if='!{ro}' size='mini' @click='openSelect{id}()'>({{{{$yzp.relationArray(entity.{field}).length}}}}){Html(button)}</button></view>"
            : $"<view class='select-page-input-row'><uni-easyinput disabled primaryColor=\"{c.PrimaryColor}\" :model-value='readonlyDisplayText(entity.{display})'/><button v-if='!{ro}' size='mini' @click='openSelect{id}()'>{Html(button)}</button>{(!f.Required?$"<button v-if='entity.{field}' size='mini' @click=\"entity.{field}='';{(display==field?"":$"entity.{display}='';")}\">清除</button>":"")}</view>";
        return $"<uni-easyinput style='display: none;' v-model='entity.{field}' maxlength='{Html(f.Column.Source.ColumnLength)}' /><text v-if='{ro}'>{{{{readonlyDisplayText(entity.{display})}}}}</text><template v-if='!{ro}'>{buttons}</template>";
    }
    /// <summary>单选目标父沿用同父或唯一声明外键；缺少可读映射必须拒绝，不能空父退全量。</summary>
    private static string TargetParent(OriginalDetailContext c,PageModel target)
    {
        var parents=target.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.LinkConfigTableId).Distinct(StringComparer.Ordinal).ToArray();
        if(parents.Length==0)return "''";
        if(parents.Length!=1||string.IsNullOrWhiteSpace(parents[0]))throw new InvalidOperationException("原选择目标父缺少唯一声明关系。");
        var source=c.Page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.LinkConfigTableId).Distinct(StringComparer.Ordinal).ToArray();
        if(source.Length==1&&source[0]==parents[0])return "parent_id";
        var links=c.Table.Columns.Where(column=>column.Source.LinkConfigTableId==parents[0]&&!column.IsJsonArray&&c.Page.Readable.Any(read=>read.Source.Id==column.Source.Id)).Take(2).ToArray();
        if(links.Length!=1)throw new InvalidOperationException("原选择目标父没有唯一可读来源字段。");
        return "entity."+links[0].JsonName;
    }
}
