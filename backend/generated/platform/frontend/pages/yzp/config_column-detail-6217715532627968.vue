<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page detail-page uni-padding-wrap" :class="{ ispc: $deviceInfo?.is_pc, ismobile: !$deviceInfo?.is_pc }">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" :disabled="readOnlyCondition" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="关联表" name="joinTable"><uni-easyinput :modelValue="entity.joinTable" :disabled="readOnlyCondition" @update:modelValue="change('joinTable', $event)" /></uni-forms-item>
      <uni-forms-item label="is_hide_select_grid" name="isHideSelectGrid"><uni-easyinput :modelValue="entity.isHideSelectGrid" :disabled="readOnlyCondition" @update:modelValue="change('isHideSelectGrid', $event)" /></uni-forms-item>
      <uni-forms-item label="联接字段" name="isJoinColumn"><uni-data-select :modelValue="entity.isJoinColumn" :disabled="readOnlyCondition" @update:modelValue="change('isJoinColumn', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="数据类型" name="columnType"><uni-data-select :modelValue="entity.columnType" :disabled="readOnlyCondition" @update:modelValue="change('columnType', $event)" :localdata="[{&quot;text&quot;:&quot;\u5B57\u7B26&quot;,&quot;value&quot;:&quot;nvarchar&quot;},{&quot;text&quot;:&quot;\u6574\u6570&quot;,&quot;value&quot;:&quot;int&quot;},{&quot;text&quot;:&quot;\u5C0F\u6570&quot;,&quot;value&quot;:&quot;decimal&quot;},{&quot;text&quot;:&quot;\u65E5\u671F\u65F6\u95F4&quot;,&quot;value&quot;:&quot;datetime&quot;},{&quot;text&quot;:&quot;\u4E8C\u8FDB\u5236\u6570\u636E&quot;,&quot;value&quot;:&quot;varbinary&quot;},{&quot;text&quot;:&quot;Point&quot;,&quot;value&quot;:&quot;point&quot;}]" /></uni-forms-item>
      <uni-forms-item label="column_constrain" name="columnConstrain"><uni-easyinput :modelValue="entity.columnConstrain" :disabled="readOnlyCondition" @update:modelValue="change('columnConstrain', $event)" /></uni-forms-item>
      <uni-forms-item label="长度" name="columnLength"><uni-easyinput :modelValue="entity.columnLength" :disabled="readOnlyCondition" @update:modelValue="change('columnLength', $event)" /></uni-forms-item>
      <uni-forms-item label="列全名" name="columnFullName"><uni-easyinput :modelValue="entity.columnFullName" :disabled="readOnlyCondition" @update:modelValue="change('columnFullName', $event)" /></uni-forms-item>
      <uni-forms-item label="is_auto_post_back_query" name="isAutoPostBackQuery"><uni-data-select :modelValue="entity.isAutoPostBackQuery" :disabled="readOnlyCondition" @update:modelValue="change('isAutoPostBackQuery', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="保存选项" name="saveOption"><uni-easyinput :modelValue="entity.saveOption" :disabled="readOnlyCondition" @update:modelValue="change('saveOption', $event)" /></uni-forms-item>
      <uni-forms-item label="display_option" name="displayOption"><uni-easyinput :modelValue="entity.displayOption" :disabled="readOnlyCondition" @update:modelValue="change('displayOption', $event)" /></uni-forms-item>
      <uni-forms-item label="字段描述" name="description"><uni-easyinput :modelValue="entity.description" :disabled="readOnlyCondition" @update:modelValue="change('description', $event)" /></uni-forms-item>
      <uni-forms-item label="开发者备注" name="developerRemarks"><uni-easyinput :modelValue="entity.developerRemarks" :disabled="readOnlyCondition" @update:modelValue="change('developerRemarks', $event)" /></uni-forms-item>
      <uni-forms-item label="is_auto_update" name="isAutoUpdate"><uni-easyinput :modelValue="entity.isAutoUpdate" :disabled="readOnlyCondition" @update:modelValue="change('isAutoUpdate', $event)" /></uni-forms-item>
      <uni-forms-item label="复制" name="isCopy"><uni-easyinput :modelValue="entity.isCopy" :disabled="readOnlyCondition" @update:modelValue="change('isCopy', $event)" /></uni-forms-item>
      <uni-forms-item label="query_default_value" name="queryDefaultValue"><uni-easyinput :modelValue="entity.queryDefaultValue" :disabled="readOnlyCondition" @update:modelValue="change('queryDefaultValue', $event)" /></uni-forms-item>
      <uni-forms-item label="列名称" name="columnName"><uni-easyinput :modelValue="entity.columnName" :disabled="readOnlyCondition" @update:modelValue="change('columnName', $event)" /></uni-forms-item>
      <uni-forms-item label="匹配类型" name="matchType"><uni-data-select :modelValue="entity.matchType" :disabled="readOnlyCondition" @update:modelValue="change('matchType', $event)" :localdata="[{&quot;text&quot;:&quot;\u7CBE\u786E&quot;,&quot;value&quot;:&quot;P&quot;},{&quot;text&quot;:&quot;\u524D\u5339\u914D&quot;,&quot;value&quot;:&quot;L&quot;},{&quot;text&quot;:&quot;\u540E\u5339\u914D&quot;,&quot;value&quot;:&quot;R&quot;},{&quot;text&quot;:&quot;\u8303\u56F4&quot;,&quot;value&quot;:&quot;B&quot;},{&quot;text&quot;:&quot;\u591A\u884C&quot;,&quot;value&quot;:&quot;M&quot;},{&quot;text&quot;:&quot;\u5217\u8868\u8F93\u5165\u67E5\u8BE2&quot;,&quot;value&quot;:&quot;ListToText&quot;}]" /></uni-forms-item>
      <uni-forms-item label="width" name="width"><uni-easyinput :modelValue="entity.width" :disabled="readOnlyCondition" @update:modelValue="change('width', $event)" /></uni-forms-item>
      <uni-forms-item label="config_table_name" name="configTableName"><uni-easyinput :modelValue="entity.configTableName" :disabled="readOnlyCondition" @update:modelValue="change('configTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="is_in_grid" name="isInGrid"><uni-easyinput :modelValue="entity.isInGrid" :disabled="readOnlyCondition" @update:modelValue="change('isInGrid', $event)" /></uni-forms-item>
      <uni-forms-item label="is_in_filter" name="isInFilter"><uni-easyinput :modelValue="entity.isInFilter" :disabled="readOnlyCondition" @update:modelValue="change('isInFilter', $event)" /></uni-forms-item>
      <uni-forms-item label="is_in_edit" name="isInEdit"><uni-easyinput :modelValue="entity.isInEdit" :disabled="readOnlyCondition" @update:modelValue="change('isInEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="控件类型" name="controlKind"><uni-data-select :modelValue="entity.controlKind" :disabled="readOnlyCondition" @update:modelValue="change('controlKind', $event)" :localdata="[{&quot;text&quot;:&quot;--&quot;,&quot;value&quot;:&quot;&quot;},{&quot;text&quot;:&quot;\u4E0B\u62C9\u5217\u8868&quot;,&quot;value&quot;:&quot;List&quot;},{&quot;text&quot;:&quot;\u5E38\u91CF\u5217\u8868&quot;,&quot;value&quot;:&quot;ConstList&quot;},{&quot;text&quot;:&quot;\u5E38\u91CF\u4E0B\u62C9\u5217\u8868&quot;,&quot;value&quot;:&quot;ConstDownList&quot;},{&quot;text&quot;:&quot;\u590D\u9009\u6846&quot;,&quot;value&quot;:&quot;CheckBox&quot;},{&quot;text&quot;:&quot;\u590D\u9009\u6846-\u6570\u636E\u5E93&quot;,&quot;value&quot;:&quot;MultiCheckBoxDatabase&quot;},{&quot;text&quot;:&quot;\u591A\u884C\u8F93\u5165\u6846&quot;,&quot;value&quot;:&quot;TextArea&quot;},{&quot;text&quot;:&quot;\u5BCC\u6587\u672C&quot;,&quot;value&quot;:&quot;RTF&quot;},{&quot;text&quot;:&quot;\u6587\u4EF6\u4E0A\u4F20&quot;,&quot;value&quot;:&quot;Upload&quot;},{&quot;text&quot;:&quot;\u89C6\u9891&quot;,&quot;value&quot;:&quot;Video&quot;},{&quot;text&quot;:&quot;\u56FE\u7247&quot;,&quot;value&quot;:&quot;Image&quot;},{&quot;text&quot;:&quot;Base64\u56FE\u7247&quot;,&quot;value&quot;:&quot;Base64Image&quot;},{&quot;text&quot;:&quot;\u591A\u56FE\u7247&quot;,&quot;value&quot;:&quot;MultiImage&quot;},{&quot;text&quot;:&quot;\u7F51\u7EDC\u56FE\u7247&quot;,&quot;value&quot;:&quot;IntenetImage&quot;},{&quot;text&quot;:&quot;\u9009\u62E9\u9875&quot;,&quot;value&quot;:&quot;SelectPage&quot;},{&quot;text&quot;:&quot;\u591A\u9009\u62E9\u9875&quot;,&quot;value&quot;:&quot;MultiSelectPage&quot;},{&quot;text&quot;:&quot;\u65E5\u671F&quot;,&quot;value&quot;:&quot;date&quot;},{&quot;text&quot;:&quot;\u5E74\u6708&quot;,&quot;value&quot;:&quot;month&quot;},{&quot;text&quot;:&quot;\u65F6\u95F4&quot;,&quot;value&quot;:&quot;time&quot;},{&quot;text&quot;:&quot;\u65E5\u671F\u65F6\u95F4&quot;,&quot;value&quot;:&quot;date_time&quot;},{&quot;text&quot;:&quot;\u5B9A\u5236\u63A7\u4EF6&quot;,&quot;value&quot;:&quot;CustomControl&quot;},{&quot;text&quot;:&quot;\u7EA7\u8FDE\u9009\u62E9&quot;,&quot;value&quot;:&quot;CascadeTree&quot;},{&quot;text&quot;:&quot;\u6587\u4EF6\u5E93&quot;,&quot;value&quot;:&quot;FileLibrary&quot;}]" /></uni-forms-item>
      <uni-forms-item label="is_read_only" name="isReadOnly"><uni-easyinput :modelValue="entity.isReadOnly" :disabled="readOnlyCondition" @update:modelValue="change('isReadOnly', $event)" /></uni-forms-item>
      <uni-forms-item label="控件明细" name="controlDetail"><yzp-control-detail-editor :entity="entity" :record-id="id" :readonly="readOnlyCondition" @receive="applyCustom" /></uni-forms-item>
      <uni-forms-item label="is_must" name="isMust"><uni-easyinput :modelValue="entity.isMust" :disabled="readOnlyCondition" @update:modelValue="change('isMust', $event)" /></uni-forms-item>
      <uni-forms-item label="控件类型-筛选" name="filterControlKind"><uni-data-select :modelValue="entity.filterControlKind" :disabled="readOnlyCondition" @update:modelValue="change('filterControlKind', $event)" :localdata="[{&quot;text&quot;:&quot;--&quot;,&quot;value&quot;:&quot;&quot;},{&quot;text&quot;:&quot;\u4E0B\u62C9\u5217\u8868&quot;,&quot;value&quot;:&quot;List&quot;},{&quot;text&quot;:&quot;\u590D\u9009\u6846&quot;,&quot;value&quot;:&quot;CheckBox&quot;}]" /></uni-forms-item>
      <uni-forms-item label="link_config_table_name" name="linkConfigTableName"><uni-easyinput :modelValue="entity.linkConfigTableName" :disabled="readOnlyCondition" @update:modelValue="change('linkConfigTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="link_config_page_name" name="linkConfigPageName"><uni-easyinput :modelValue="entity.linkConfigPageName" :disabled="readOnlyCondition" @update:modelValue="change('linkConfigPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="排序号" name="sortNo"><uni-easyinput :modelValue="entity.sortNo" :disabled="readOnlyCondition" @update:modelValue="change('sortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="默认值" name="defaultValue"><uni-easyinput :modelValue="entity.defaultValue" :disabled="readOnlyCondition" @update:modelValue="change('defaultValue', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_id" name="yzProjectId"><text>{{ displayValue(entity, 'yzProjectId') }}</text></uni-forms-item>
      <uni-forms-item label="新建初始值" name="initValue"><uni-easyinput :modelValue="entity.initValue" :disabled="readOnlyCondition" @update:modelValue="change('initValue', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ displayValue(entity, 'yzProjectName') }}</text></uni-forms-item>
      <uni-forms-item label="is_print" name="isPrint"><uni-easyinput :modelValue="entity.isPrint" :disabled="readOnlyCondition" @update:modelValue="change('isPrint', $event)" /></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" :disabled="readOnlyCondition" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="control_width" name="controlWidth"><uni-easyinput :modelValue="entity.controlWidth" :disabled="readOnlyCondition" @update:modelValue="change('controlWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="col_span" name="colSpan"><uni-easyinput :modelValue="entity.colSpan" :disabled="readOnlyCondition" @update:modelValue="change('colSpan', $event)" /></uni-forms-item>
      <uni-forms-item label="align" name="align"><uni-easyinput :modelValue="entity.align" :disabled="readOnlyCondition" @update:modelValue="change('align', $event)" /></uni-forms-item>
      <uni-forms-item label="is_auto_post_back" name="isAutoPostBack"><uni-data-select :modelValue="entity.isAutoPostBack" :disabled="readOnlyCondition" @update:modelValue="change('isAutoPostBack', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="动态显示规则" name="visibleCondition"><uni-easyinput :modelValue="entity.visibleCondition" :disabled="readOnlyCondition" @update:modelValue="change('visibleCondition', $event)" /></uni-forms-item>
      <uni-forms-item label="开启排序" name="isSort"><uni-data-select :modelValue="entity.isSort" :disabled="readOnlyCondition" @update:modelValue="change('isSort', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="sum_kind" name="sumKind"><uni-easyinput :modelValue="entity.sumKind" :disabled="readOnlyCondition" @update:modelValue="change('sumKind', $event)" /></uni-forms-item>
      <uni-forms-item label="虚拟字段" name="isVirtualColumn"><uni-data-select :modelValue="entity.isVirtualColumn" :disabled="readOnlyCondition" @update:modelValue="change('isVirtualColumn', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="逻辑类型" name="logicalType"><uni-easyinput :modelValue="entity.logicalType" :disabled="readOnlyCondition" @update:modelValue="change('logicalType', $event)" /></uni-forms-item>
      <uni-forms-item label="移动端列表页占列数" name="mobileListPageColSpan"><uni-data-select :modelValue="entity.mobileListPageColSpan" :disabled="readOnlyCondition" @update:modelValue="change('mobileListPageColSpan', $event)" :localdata="[{&quot;text&quot;:&quot;1&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;2&quot;,&quot;value&quot;:2}]" /></uni-forms-item>
      <uni-forms-item label="数值精度" name="precision"><uni-easyinput :modelValue="entity.precision" :disabled="readOnlyCondition" @update:modelValue="change('precision', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库必填" name="isDbNotNull"><uni-data-select :modelValue="entity.isDbNotNull" :disabled="readOnlyCondition" @update:modelValue="change('isDbNotNull', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="小数位数" name="scale"><uni-easyinput :modelValue="entity.scale" :disabled="readOnlyCondition" @update:modelValue="change('scale', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="!readOnlyCondition && (id ? canSave : canCreate)" :disabled="saving" @click="save">保存</button>
    <button v-if="canCopyRecord" :disabled="saving" @click="copy">复制</button>
    <button v-if="canDeleteRecord" :disabled="saving" @click="remove">删除</button>


  </view>
</template>
<script>
import yzp from '@/common/yzp';


const writableFields = ["name","joinTable","isHideSelectGrid","isJoinColumn","columnType","columnConstrain","columnLength","columnFullName","isAutoPostBackQuery","saveOption","displayOption","description","developerRemarks","isAutoUpdate","isCopy","queryDefaultValue","columnName","matchType","width","configTableName","isInGrid","isInFilter","isInEdit","controlKind","isReadOnly","controlDetail","isMust","filterControlKind","linkConfigTableName","linkConfigPageName","sortNo","defaultValue","initValue","isPrint","importNo","controlWidth","colSpan","align","isAutoPostBack","visibleCondition","isSort","sumKind","isVirtualColumn","logicalType","mobileListPageColSpan","precision","isDbNotNull","scale"];
const displayFields = {"name":{"alias":null,"optionField":null,"multiNames":false},"joinTable":{"alias":null,"optionField":null,"multiNames":false},"isHideSelectGrid":{"alias":null,"optionField":null,"multiNames":false},"isJoinColumn":{"alias":null,"optionField":null,"multiNames":false},"columnType":{"alias":"columnTypeShow","optionField":null,"multiNames":false},"columnConstrain":{"alias":null,"optionField":null,"multiNames":false},"columnLength":{"alias":null,"optionField":null,"multiNames":false},"columnFullName":{"alias":null,"optionField":null,"multiNames":false},"isAutoPostBackQuery":{"alias":null,"optionField":null,"multiNames":false},"saveOption":{"alias":null,"optionField":null,"multiNames":false},"displayOption":{"alias":null,"optionField":null,"multiNames":false},"description":{"alias":null,"optionField":null,"multiNames":false},"developerRemarks":{"alias":null,"optionField":null,"multiNames":false},"isAutoUpdate":{"alias":null,"optionField":null,"multiNames":false},"isCopy":{"alias":null,"optionField":null,"multiNames":false},"queryDefaultValue":{"alias":null,"optionField":null,"multiNames":false},"columnName":{"alias":null,"optionField":null,"multiNames":false},"matchType":{"alias":"matchTypeShow","optionField":null,"multiNames":false},"width":{"alias":null,"optionField":null,"multiNames":false},"configTableName":{"alias":null,"optionField":null,"multiNames":false},"isInGrid":{"alias":null,"optionField":null,"multiNames":false},"isInFilter":{"alias":null,"optionField":null,"multiNames":false},"isInEdit":{"alias":null,"optionField":null,"multiNames":false},"controlKind":{"alias":"controlKindShow","optionField":null,"multiNames":false},"isReadOnly":{"alias":null,"optionField":null,"multiNames":false},"controlDetail":{"alias":null,"optionField":null,"multiNames":false},"isMust":{"alias":null,"optionField":null,"multiNames":false},"filterControlKind":{"alias":"filterControlKindShow","optionField":null,"multiNames":false},"linkConfigTableName":{"alias":null,"optionField":null,"multiNames":false},"linkConfigPageName":{"alias":null,"optionField":null,"multiNames":false},"sortNo":{"alias":null,"optionField":null,"multiNames":false},"defaultValue":{"alias":null,"optionField":null,"multiNames":false},"yzProjectId":{"alias":null,"optionField":null,"multiNames":false},"initValue":{"alias":null,"optionField":null,"multiNames":false},"yzProjectName":{"alias":null,"optionField":null,"multiNames":false},"isPrint":{"alias":null,"optionField":null,"multiNames":false},"importNo":{"alias":null,"optionField":null,"multiNames":false},"insertTime":{"alias":null,"optionField":null,"multiNames":false},"controlWidth":{"alias":null,"optionField":null,"multiNames":false},"updateTime":{"alias":null,"optionField":null,"multiNames":false},"colSpan":{"alias":null,"optionField":null,"multiNames":false},"insertManagerId":{"alias":null,"optionField":null,"multiNames":false},"align":{"alias":null,"optionField":null,"multiNames":false},"updateManagerId":{"alias":null,"optionField":null,"multiNames":false},"insertManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"isAutoPostBack":{"alias":null,"optionField":null,"multiNames":false},"updateManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"visibleCondition":{"alias":null,"optionField":null,"multiNames":false},"insertUserName":{"alias":null,"optionField":null,"multiNames":false},"updateUserName":{"alias":null,"optionField":null,"multiNames":false},"isSort":{"alias":null,"optionField":null,"multiNames":false},"sumKind":{"alias":null,"optionField":null,"multiNames":false},"isVirtualColumn":{"alias":null,"optionField":null,"multiNames":false},"logicalType":{"alias":null,"optionField":null,"multiNames":false},"mobileListPageColSpan":{"alias":"mobileListPageColSpanShow","optionField":null,"multiNames":false},"precision":{"alias":null,"optionField":null,"multiNames":false},"isDbNotNull":{"alias":null,"optionField":null,"multiNames":false},"scale":{"alias":null,"optionField":null,"multiNames":false},"id":{"alias":null,"optionField":null,"multiNames":false}};
const rtfFields = [];
const children = [];
const readonlyField = null;
/** 空父键不接受历史 undefined/null 字面量，字符串 ID 全程不转为浮点。 */
const recordId = value => typeof value === 'string' && !['undefined', 'null'].includes(value.trim()) ? value.trim() : '';
export default {
  mixins: [],
  components: {  },
  props: { is_props: { type: Boolean, default: false }, props_id: { type: String, default: '' }, props_parent_id: { type: String, default: '' }, props_parent_page_sn: { type: String, default: '' }, parentReadOnlyCondition: { type: Boolean, default: false } },
  data() { return { id: '', parentId: '', parentPageId: '', routeReadOnly: false, recordReady: false, permissionsReady: false, permissionVersion: 0, currentChild: 0, children, canUpdate: true, entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: true, canDelete: true, canCopy: false }; },
  computed: {
    /** 子表继承父只读后仍检查自身当前操作权限；共享快照不代表继承操作授权。 */
    readOnlyCondition() { this.permissionVersion; return !this.permissionsReady || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.id ? !this.canUpdate : !this.canCreate) || readonlyField && this.entity[readonlyField]) || !yzp.hasPagePermission('6217715532627968', this.id ? 'is_update' : 'is_new'); },
    /** 复制和删除分别检查自身操作授权，修改授权不会隐式开放其他动作。 */
    canCopyRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canCopy && yzp.hasPagePermission('6217715532627968', 'is_all'); },
    canDeleteRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canDelete && yzp.hasPagePermission('6217715532627968', 'is_delete'); },
    /** 只有已读取或写入成功的父记录可以挂载子页，手填路由 ID 不开放子页。 */
    childrenReady() { return this.recordReady && this.permissionsReady && Boolean(this.id) && String(this.entity.id) === this.id; }
  },
  mounted() { if (this.is_props) this.initializeDetail({ id: this.props_id, parentId: this.props_parent_id, parentPageId: this.props_parent_page_sn }); },
  onLoad(options) { if (!this.is_props) this.initializeDetail(options); },
  onUnload() { this.disposeDetail(); },
  beforeUnmount() { this.disposeDetail(); },
  methods: {
    /** 路由和弹层使用同一初始化入口；切换或卸载先取消旧记录回调。 */
    initializeDetail(options = {}) {
      this.disposeDetail(); this._alive = true; this._optionRequests = []; this._loadNo = 0;
      this.id = recordId(options.id); this.parentId = recordId(options.parentId || options.parent_id); this.parentPageId = recordId(options.parentPageId || options.parent_page_sn); this.routeReadOnly = options.parentReadOnly === '1';
      this.entity = {}; this.changes = {}; this.recordReady = false; this.saving = false; this.currentChild = 0; this.permissionsReady = false;
      this.initializeMultiSelections?.();
      const epoch = this._epoch, current = () => this._alive && epoch === this._epoch;
      const ready = () => { if (!current()) return; this.permissionsReady = true; this.permissionVersion++; this.loadOptions(); if (this.id) this.load(); };
      // 嵌入子页复用父页已刷新的权限快照，避免每个子模块重复请求。
      if (this.parentPageId) ready(); else yzp.refreshCurrentPermissions(ready, ready);
    },
    /** 取消读写及选项请求；迟到的弹层或导航回调不影响下一条父记录。 */
    disposeDetail() { this.disposeMultiSelections?.(); this._alive = false; this._epoch = (this._epoch || 0) + 1; this._loadRequest?.abort?.(); this._writeRequest?.abort?.(); (this._optionRequests || []).forEach(task => task?.abort?.()); },
    /** 页签只改变子模块显示，保留父表单脏字段及已挂载子列表状态。 */
    selectChild(index) { if (this.childrenReady && Number.isInteger(index) && index >= 0 && index < children.length) this.currentChild = index; },
    /** 父记录保存后仅重读各子列表，不向父表单回填或提交任何字段。 */
    refreshSonsData() { if (!this.childrenReady) return; for (const child of children) { const list = this.$refs['child_' + child.id]; list?.refreshData?.(); } },
    /** 弹层与新页面沿用原 onChange 事件，接收方只负责刷新自己的列表。 */
    emitDetailChange(action, entity, method) { const payload = action === 'save' && method === 'update' ? { action, method, entity } : action; if (this.is_props) this.$emit('onChange', payload); else this.getOpenerEventChannel?.()?.emit?.('onChange', payload); },
    /** 关闭弹层不操作导航栈，新页面沿用返回行为。 */
    closeDetail() { if (this.is_props) this.$emit('onPopClose'); else uni.navigateBack(); },
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() {
      if (this.readOnlyCondition || !this.canCopy || !yzp.hasPagePermission('6217715532627968', 'is_all') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('6217715532627968', 'is_all') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/config_column/6217715532627968/copy/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'POST', sendSessionId: true, isCurrent: current,
          success: res => { if (current() && res.code === 200 && recordId(res.data?.id)) { this.id = res.data.id; this.entity = res.data; this.changes = {}; this.recordReady = true; this.emitDetailChange('copy', res.data); if (this.is_props) this.closeDetail(); uni.showToast({ title: '复制成功', icon: 'success' }); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
      } });
    },
    /** 关系选项来自当前页已编译端点，旧身份和父记录的响应不回填。 */
    loadOptions() {
      const parentId = this.parentId || null, identity = this.currentIdentity();
      const current = () => this._alive && parentId === (this.parentId || null) && identity === this.currentIdentity();
      this.refreshRelationLabels?.();
    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页返回标识；路由保持原选择页协议。 */
    selectPage(field, path) { if (!this.readOnlyCondition && writableFields.includes(field)) uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 真实身份快照只用于隔离迟到回调，所有数据权限仍由后端核验。 */
    currentIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]); },
    /** 读取明细只接收服务端页级 VO，离开页面或父记录变化后忽略回调。 */
    load() {
      const id = this.id, parentId = this.parentId, identity = this.currentIdentity(), sequence = ++this._loadNo;
      const current = () => this._alive && sequence === this._loadNo && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      this._loadRequest?.abort?.();
      this._loadRequest = yzp.request({ url: '/admin/auto/config_column/6217715532627968/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200 && recordId(res.data?.id) === id) { this.entity = res.data; this.changes = {}; this.recordReady = true; } } });
    },
    /** 只记录用户修改且在白名单内的字段，原值不变的回调不会把 null 改为空字符串。 */
    change(field, value) { if (this.readOnlyCondition || !writableFields.includes(field) || JSON.stringify(this.entity[field]) === JSON.stringify(value)) return; this.entity[field] = value; this.changes[field] = value; },
    /** 消费真实常量展示别名和关系选项，缺失名称时仍显示原 ID。 */
    displayValue(row, field) {
      const definition = displayFields[field];
      // 多选名称仍按原逗号文本展示，HTTP数组与历史JSON文本都不回写实体。
      if (definition?.multiNames) { const value = definition.alias && row[definition.alias] != null ? row[definition.alias] : row[field]; try { const values = typeof value === 'string' ? JSON.parse(value) : value; if (Array.isArray(values)) return values.map(item => item == null ? '' : String(item)).join('，'); } catch {} return value ?? ''; }
      if (definition?.alias && row[definition.alias] !== undefined && row[definition.alias] !== null && row[definition.alias] !== '') return row[definition.alias];
      if (definition?.optionField && row[field] !== null && row[field] !== undefined) { const option = (this.options[definition.optionField] || []).find(item => String(item.value ?? item.id) === String(row[field])); if (option && (option.text ?? option.name) !== null && (option.text ?? option.name) !== undefined && (option.text ?? option.name) !== '') return option.text ?? option.name; }
      return row[field] ?? '';
    },
    /** 富文本沿用原编辑/预览页和事件协议；未修改、离开或切换记录的回传不产生写入。 */
    openRtf(field, action) {
      if (!rtfFields.includes(field) || !['edit', 'review'].includes(action) || action === 'edit' && (this.readOnlyCondition || !writableFields.includes(field))) return;
      const original = this.entity[field], text = original ?? '', id = this.id, parentId = this.parentId, identity = this.currentIdentity();
      const current = () => this._alive && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.navigateTo({ url: '/pages/main/rtf/rtf?action=' + action, events: { onReceiveRtf: value => { if (action === 'edit' && current() && typeof value === 'string' && value !== text) this.change(field, value); } }, success: result => { if (current()) result.eventChannel.emit('sendRtf', { data: text }); } });
    },
    /** 新增和修改均只发送元数据允许编辑的字段，保留省略、null、false和零。 */
    save() {
      if (this.saving || this.readOnlyCondition || !(this.id ? this.canSave : this.canCreate) || true && !this.parentId) return;
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      const id = this.id, parentId = this.parentId, identity = this.currentIdentity(), epoch = this._epoch, hadChildren = this.childrenReady;
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      this.saving = true;
      this._writeRequest = yzp.request({ url: '/admin/auto/config_column/6217715532627968/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200 && recordId(res.data?.id)) { this.id = res.data.id; this.entity = res.data; this.changes = {}; this.recordReady = true; this.emitDetailChange('save', res.data, id ? 'update' : 'add'); if (this.is_props) this.closeDetail(); else if (hadChildren) this.$nextTick(() => this.refreshSonsData()); uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
    },
    /** 删除仅通知当前列表，子表刷新不会写入或重载父实体。 */
    remove() {
      if (this.readOnlyCondition || !this.canDelete || !yzp.hasPagePermission('6217715532627968', 'is_delete') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('6217715532627968', 'is_delete') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/config_column/6217715532627968?parentId=' + encodeURIComponent(parentId), method: 'DELETE', data: { id }, sendSessionId: true, isCurrent: current,
          success: res => { if (current() && res.code === 200) { this.recordReady = false; this.emitDetailChange('delete'); this.closeDetail(); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
      } });
    }
  }
};
</script>
