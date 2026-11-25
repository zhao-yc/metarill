using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using static Metarill.Generation.OriginalDetailContext;

namespace Metarill.Generation;

/// <summary>保留原选择页按钮、弹层、props及事件；多选仅在内部桥接既有完整回读/迟到保护。</summary>
internal static class OriginalUniappDetailSelection
{
    /// <summary>目标页必须来自同一批已发布元数据，不能把字符串配置当作任意路由。</summary>
    internal static string Render(OriginalDetailContext c, OriginalDetailField f, string ro)
    {
        var functionSelection=c.Table.Source.IsFunctionTable==1&&f.Column.Source.ControlKind=="SelectPage"
            ?FunctionTableSelectionCompilation.Resolve(c.Model,c.Table,c.Page,f.Column):null;
        var definition=functionSelection?.Definition??SelectionControlDefinition.Resolve(f.Column,f.Settings);var pageId=definition.PageId;
        var target=functionSelection is {} knownTarget?(Table:knownTarget.Target,Page:knownTarget.Page):
            c.Model.Tables.SelectMany(table=>table.Pages.Select(page=>(Table:table,Page:page))).SingleOrDefault(item=>item.Page.Source.Id==pageId);
        if(target.Page is null)throw new InvalidOperationException("原选择页目标不存在："+pageId);
        if(target.Table.Source.IsFunctionTable==1)throw new InvalidOperationException("功能表没有可选择的记录："+pageId);
        var nameField=definition.NameField; var targetName=functionSelection?.Name??target.Table.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField||column.JsonName==nameField);
        if(targetName is null||!target.Page.Readable.Any(column=>column.Source.Id==targetName.Source.Id))throw new InvalidOperationException("原选择页名称列未开放读取。");
        var button=definition.ButtonText; var id=f.Column.Source.Id; var field=f.Column.JsonName; var display=c.DisplayField(f.Column,targetName.Source.ColumnName); var component="OriginalSelection"+id;
        c.Import(component,$"@/pages/yzp/{target.Table.Source.TableName}-list-{pageId}.vue");
        var multi=SelectionPageRenderer.Enabled(target.Page);
        if(multi)
        {
            var compiled=MultiSelectCompilation.Resolve(c.Model,c.Table,c.Page,f.Column);
            if(compiled.Definition is not {} known)throw new InvalidOperationException("原多选字段尚不能证明安全回读合同："+compiled.Reason);
            if(!c.Mixins.Any(item=>item.StartsWith("createMultiSelectFieldMixin(",StringComparison.Ordinal)))
            {
                c.Imports.AppendLine("import { createMultiSelectFieldMixin } from '@/common/yzp_runtime/multiSelectFieldMixin';");
                var all=MultiSelectCompilation.ForPage(c.Model,c.Table,c.Page);
                c.Mixins.Add("createMultiSelectFieldMixin("+Js(all.Select(item=>new {tableId=c.Table.Source.Id,columnId=item.Source.Source.Id,field=item.Source.JsonName,namesField=item.Names?.JsonName,nameField=item.Name.JsonName,int32Name=item.IntegerLabel&&item.Name.Type=="int?",sameParent=item.SameParent,targetParentField=item.TargetParentField,requiresTargetParent=item.TargetParents.Count>0,redundant=c.Table.Source.IsRongyu==1,writable=c.Page.Writable.Any(column=>column.Source.Id==item.Source.Source.Id),@ref="selectPageDialog"+item.Source.Source.Id}))+MultiSelectUiRenderer.TextParentArgument(all)+")");
            }
            display=known.Names?.JsonName??field;
            c.Methods.AppendLine($"openSelect{id}() {{ this.openMultiSelection({Js(field)}); }}, onPopClose{id}() {{ this.closeMultiSelection({Js(field)}); }}, onPopSelect{id}(rows) {{ this.permissionVersion++;if(!this.multiSelectionCurrent({Js(field)}))return;this.receiveMultiSelection({Js(field)},rows);if(!this.multiSelectionError)this.closeMultiSelection({Js(field)}); }},");
            c.Popups.AppendLine($"<yzp-popup ref=\"selectPageDialog{id}\" type=\"center\" @change=\"multiSelectionPopupChange('{field}', $event)\"><view class=\"select-page-dialog-view\"><{component} v-if=\"multiSelectionField === '{field}'\" :key=\"multiSelectionVersion\" ref='{id}' @onPopSelect=\"onPopSelect{id}\" @onPopClose=\"onPopClose{id}\" :selectedIds=\"entity.{field}\" :selectedNames=\"entity.{display}\" selected-name-field=\"{Html(targetName.JsonName)}\" :parent_id=\"multiTargetParent('{field}')\" :parentReadOnlyCondition=\"{ro} || !multiSelectionCurrent('{field}')\" /></view></yzp-popup>");
        }
        else
        {
            var targetParent=functionSelection is null?TargetParent(c,f.Column,target.Table,target.Page):"''";
            var capture=$"this._selection{id}=this.originalContext();";
            var release="";
            var popupReadOnly=ro;
            c.Data.Append("originalSelection").Append(id).Append(":false,");
            if(functionSelection?.Parent is {} parent)
            {
                // 弹层只在本次有效父范围打开；程序联动修改父值时拒绝旧范围回执。
                targetParent="originalSelectionParent"+id;
                c.Data.Append(targetParent).Append(":'',");
                c.Methods.AppendLine($"selectionCurrent{id}() {{ return this._selection{id}?.()===true; }},");
                popupReadOnly+=$" || !selectionCurrent{id}()";
                capture=$"const parent=String(this.entity.{parent.JsonName}??'');"+
                    "if(!/^[1-9][0-9]{0,18}$/.test(parent)||parent.length===19&&parent>'9223372036854775807'){uni.showModal({showCancel:false,content:'目标父范围尚未明确，请先选择或保存父记录。'});return;}"+
                    $"const current=this.originalContext();this.{targetParent}=parent;this._selection{id}=()=>current()&&parent===String(this.entity.{parent.JsonName}??'');";
                release=$"this.{targetParent}='';";
            }
            c.Methods.AppendLine($$$"""
openSelect{{{id}}}() { if(this.{{{ro}}})return; {{{capture}}}this.originalSelection{{{id}}}=true;this.$nextTick(()=>{if(this._selection{{{id}}}?.())this.$refs.selectPageDialog{{{id}}}.open();}); },
// 主动关闭与popup关闭事件分开，真实close会同步发change，不能回调自身形成递归。
onPopClose{{{id}}}(closePopup=true) { const opened=this.originalSelection{{{id}}};this.originalSelection{{{id}}}=false;this._selection{{{id}}}=null;{{{release}}}if(closePopup&&opened)this.$refs.selectPageDialog{{{id}}}?.close(); },
onPopSelect{{{id}}}(row) { this.permissionVersion++;if(this.{{{ro}}} || !this._selection{{{id}}}?.() || !row || !originalRecordId(row.id))return;this.entity.{{{field}}}=row.id;{{{(display==field?"":$"this.entity.{display}=row.{targetName.JsonName};")}}} this.onPopClose{{{id}}}(); },
""");
            c.Popups.AppendLine($"<yzp-popup ref=\"selectPageDialog{id}\" type=\"center\" @change=\"!$event.show && onPopClose{id}(false)\"><view class=\"select-page-dialog-view\"><{component} v-if=\"originalSelection{id}\" ref='{id}' @onPopSelect=\"onPopSelect{id}\" @onPopClose=\"onPopClose{id}\" :selectedIds=\"entity.{field}\" :selectedNames=\"entity.{display}\" selected-name-field=\"{Html(targetName.JsonName)}\" :parent_id=\"{targetParent}\" :parentReadOnlyCondition=\"{popupReadOnly}\" /></view></yzp-popup>");
        }
        var buttons=multi ? $"<view style='width: 100%;'><uni-easyinput disabled type='textarea' primaryColor=\"{c.PrimaryColor}\" :model-value='readonlyDisplayText(entity.{display})'/><button v-if='!{ro}' size='mini' @click='openSelect{id}()'>({{{{$yzp.relationArray(entity.{field}).length}}}}){Html(button)}</button></view>"
            : $"<view class='select-page-input-row'><uni-easyinput disabled primaryColor=\"{c.PrimaryColor}\" :model-value='readonlyDisplayText(entity.{display})'/><button v-if='!{ro}' size='mini' @click='openSelect{id}()'>{Html(button)}</button>{(!f.Required?$"<button v-if='entity.{field}' size='mini' @click=\"entity.{field}='';{(display==field?"":$"entity.{display}='';")}\">清除</button>":"")}</view>";
        return $"<uni-easyinput style='display: none;' v-model='entity.{field}' maxlength='{Html(f.Column.Source.ColumnLength)}' /><text v-if='{ro}'>{{{{readonlyDisplayText(entity.{display})}}}}</text><template v-if='!{ro}'>{buttons}</template>";
    }
    /// <summary>单选目标父沿用同父或唯一声明外键；缺少可读映射必须拒绝，不能空父退全量。</summary>
    private static string TargetParent(OriginalDetailContext c,ColumnModel foreign,TableModel targetTable,PageModel target)
    {
        // 非冗余单选复用该关系在当前页已经证明的父层位，不从稀疏字段的表关联猜测。
        var relation=c.Table.Source.IsRongyu==0
            ?RelationCompilation.ForPage(c.Model,c.Page,scopeSourceId:foreign.Source.Id).SingleOrDefault()
            :null;
        if((relation?.FixedParent??DerivedRelationNamesCompilation.SingleParent(c.Model,targetTable,target)) is {} fixedParent)
        {
            var sourceKey=relation?.FixedSourceParent?.Source??
                fixedParent.SingleSource(c.Model.Catalog,c.Table.Source,foreign.Source).Column;
            if(c.Page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==sourceKey.Id))return SameParent();
            var stored=c.Page.Readable.SingleOrDefault(column=>column.Source.Id==sourceKey.Id);
            if(stored is null)
                throw new InvalidOperationException($"页面 {c.Page.Source.Id} 的原单选缺少可读父字段 {sourceKey.ColumnName}。");
            return "entity."+stored.JsonName;
        }
        var parents=target.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.LinkConfigTableId).Distinct(StringComparer.Ordinal).ToArray();
        if(parents.Length==0)return "''";
        if(parents.Length!=1||string.IsNullOrWhiteSpace(parents[0]))throw new InvalidOperationException("原选择目标父缺少唯一声明关系。");
        var source=c.Page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.LinkConfigTableId).Distinct(StringComparer.Ordinal).ToArray();
        if(source.Length==1&&source[0]==parents[0])return SameParent();
        var links=c.Table.Columns.Where(column=>column.Source.LinkConfigTableId==parents[0]&&!column.IsJsonArray&&c.Page.Readable.Any(read=>read.Source.Id==column.Source.Id)).Take(2).ToArray();
        if(links.Length!=1)throw new InvalidOperationException("原选择目标父没有唯一可读来源字段。");
        return "entity."+links[0].JsonName;

        string SameParent()
        {
            // 原单选按主键打开目标页，文本级联业务键不可直接用作目标主键。
            if(CrossTableReadCompilation.TextSourceParent(c.Model,c.Table,c.Page) is not null&&
                SharedRedundantParentCompilation.CommonTextParent(c.Model,c.Table,c.Page,targetTable,target) is null&&
                NonRedundantCrossParentCompilation.CommonTextParent(c.Model,c.Table,c.Page,targetTable,target) is null)
                throw new NotSupportedException("原单选目标父尚未证明使用与来源相同的文本业务键。");
            return "parent_id";
        }
    }
}
