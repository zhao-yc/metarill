using System.Security;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>原目标列表组件嵌入弹层，字段定义只来源于固定元数据编译结果。</summary>
internal static class MultiSelectUiRenderer
{
    /// <summary>组件名使用稳定列ID，避免客户端路径和多个字段重名。</summary>
    private static string Component(MultiSelectModel def) => "MultiSelection" + def.Source.Source.Id;
    /// <summary>只导入本批实际生成的目标页，不使用客户端提供的路由。</summary>
    internal static string Imports(IReadOnlyList<MultiSelectModel> defs) => defs.Count == 0 ? "" : "import { createMultiSelectFieldMixin } from '@/common/yzp_runtime/multiSelectFieldMixin';\n" + string.Join("\n", defs.Select(def => $"import {Component(def)} from '@/pages/yzp/{def.Target.Source.TableName}-list-{def.TargetPage.Source.Id}.vue';"));
    /// <summary>原目标组件在源详情显式注册。</summary>
    internal static string Components(IReadOnlyList<MultiSelectModel> defs) => defs.Count == 0 ? "" : string.Join(", ", defs.Select(Component)) + ", ";
    /// <summary>字段和名称只由稳定来源表/列ID定位，不下发动态查询信息。</summary>
    internal static string Mixin(TableModel table, PageModel page, IReadOnlyList<MultiSelectModel> defs) => defs.Count == 0 ? "" : "createMultiSelectFieldMixin(" + JsonSerializer.Serialize(defs.Select(def => new { tableId = table.Source.Id, columnId = def.Source.Source.Id, field = def.Source.JsonName, namesField = def.Names?.JsonName, nameField = def.Name.JsonName, sameParent = def.SameParent, targetParentField = def.TargetParentField, requiresTargetParent = def.TargetParents.Count > 0, redundant = table.Source.IsRongyu == 1, writable = page.Writable.Any(column => column.Source.Id == def.Source.Source.Id), @ref = "multi_selection_" + def.Source.Source.Id })) + ")";
    /// <summary>保留原选择事件并沿用有限高度滚动容器；工具栏不能随长列表移出视口，关闭时卸载草稿。</summary>
    internal static string Popups(IReadOnlyList<MultiSelectModel> defs) => string.Join("\n", defs.Select(def => $"    <yzp-popup ref=\"multi_selection_{def.Source.Source.Id}\" type=\"center\" :mask-click=\"false\" @change=\"multiSelectionPopupChange('{def.Source.JsonName}', $event)\"><view class=\"select-page-dialog-view child-detail-dialog-view\"><{Component(def)} v-if=\"multiSelectionField === '{def.Source.JsonName}'\" :key=\"multiSelectionVersion\" :selectedIds=\"entity.{def.Source.JsonName}\" :selectedNames=\"entity.{def.Names?.JsonName ?? def.Source.JsonName}\" selected-name-field=\"{SecurityElement.Escape(def.Name.JsonName)}\" :parent_id=\"multiTargetParent('{def.Source.JsonName}')\" :parentReadOnlyCondition=\"readOnlyCondition || !multiSelectionCurrent('{def.Source.JsonName}')\" @onPopSelect=\"receiveMultiSelection('{def.Source.JsonName}', $event)\" @onPopClose=\"closeMultiSelection('{def.Source.JsonName}')\" /></view></yzp-popup>"));
}
