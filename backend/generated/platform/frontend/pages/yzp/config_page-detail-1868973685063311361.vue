<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page detail-page uni-padding-wrap" :class="{ ispc: $deviceInfo?.is_pc, ismobile: !$deviceInfo?.is_pc }">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" :disabled="readOnlyCondition" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="表名" name="configTableTableName"><uni-easyinput :modelValue="entity.configTableTableName" :disabled="readOnlyCondition" @update:modelValue="change('configTableTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="order_by" name="orderBy"><uni-easyinput :modelValue="entity.orderBy" :disabled="readOnlyCondition" @update:modelValue="change('orderBy', $event)" /></uni-forms-item>
      <uni-forms-item label="Where" name="whereSql"><uni-easyinput type="textarea" :modelValue="entity.whereSql" :disabled="readOnlyCondition" @update:modelValue="change('whereSql', $event)" /></uni-forms-item>
      <uni-forms-item label="精确数据管理匹配字段" name="exactDataManageMatchField"><uni-easyinput :modelValue="entity.exactDataManageMatchField" :disabled="readOnlyCondition" @update:modelValue="change('exactDataManageMatchField', $event)" /></uni-forms-item>
      <uni-forms-item label="page_size" name="pageSize"><uni-easyinput :modelValue="entity.pageSize" :disabled="readOnlyCondition" @update:modelValue="change('pageSize', $event)" /></uni-forms-item>
      <uni-forms-item label="is_edit" name="isEdit"><uni-data-select :modelValue="entity.isEdit" :disabled="readOnlyCondition" @update:modelValue="change('isEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_new" name="isNew"><uni-data-select :modelValue="entity.isNew" :disabled="readOnlyCondition" @update:modelValue="change('isNew', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_update" name="isUpdate"><uni-data-select :modelValue="entity.isUpdate" :disabled="readOnlyCondition" @update:modelValue="change('isUpdate', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_delete" name="isDelete"><uni-data-select :modelValue="entity.isDelete" :disabled="readOnlyCondition" @update:modelValue="change('isDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_search" name="isSearch"><uni-data-select :modelValue="entity.isSearch" :disabled="readOnlyCondition" @update:modelValue="change('isSearch', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_sum" name="isSum"><uni-data-select :modelValue="entity.isSum" :disabled="readOnlyCondition" @update:modelValue="change('isSum', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_batch_delete" name="isBatchDelete"><uni-data-select :modelValue="entity.isBatchDelete" :disabled="readOnlyCondition" @update:modelValue="change('isBatchDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_import" name="isImport"><uni-data-select :modelValue="entity.isImport" :disabled="readOnlyCondition" @update:modelValue="change('isImport', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_export" name="isExport"><uni-data-select :modelValue="entity.isExport" :disabled="readOnlyCondition" @update:modelValue="change('isExport', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_multi_select" name="isMultiSelect"><uni-data-select :modelValue="entity.isMultiSelect" :disabled="readOnlyCondition" @update:modelValue="change('isMultiSelect', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_export_edit" name="isExportEdit"><uni-data-select :modelValue="entity.isExportEdit" :disabled="readOnlyCondition" @update:modelValue="change('isExportEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="export_method" name="exportMethod"><uni-easyinput :modelValue="entity.exportMethod" :disabled="readOnlyCondition" @update:modelValue="change('exportMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="复制方法" name="isCopy"><uni-data-select :modelValue="entity.isCopy" :disabled="readOnlyCondition" @update:modelValue="change('isCopy', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="read_only_condition" name="readOnlyCondition"><uni-easyinput :modelValue="entity.readOnlyCondition" :disabled="readOnlyCondition" @update:modelValue="change('readOnlyCondition', $event)" /></uni-forms-item>
      <uni-forms-item label="import_method" name="importMethod"><uni-easyinput :modelValue="entity.importMethod" :disabled="readOnlyCondition" @update:modelValue="change('importMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="button_visible" name="buttonVisible"><uni-easyinput :modelValue="entity.buttonVisible" :disabled="readOnlyCondition" @update:modelValue="change('buttonVisible', $event)" /></uni-forms-item>
      <uni-forms-item label="is_after_add" name="isAfterAdd"><uni-data-select :modelValue="entity.isAfterAdd" :disabled="readOnlyCondition" @update:modelValue="change('isAfterAdd', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_after_save" name="isAfterSave"><uni-data-select :modelValue="entity.isAfterSave" :disabled="readOnlyCondition" @update:modelValue="change('isAfterSave', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_before_save" name="isBeforeSave"><uni-data-select :modelValue="entity.isBeforeSave" :disabled="readOnlyCondition" @update:modelValue="change('isBeforeSave', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="子模块位置" name="childPlace"><uni-data-select :modelValue="entity.childPlace" :disabled="readOnlyCondition" @update:modelValue="change('childPlace', $event)" :localdata="[{&quot;text&quot;:&quot;\u4E00\u62D6N&quot;,&quot;value&quot;:&quot;OneWithMany&quot;},{&quot;text&quot;:&quot;\u6807\u7B7E\u663E\u793A&quot;,&quot;value&quot;:&quot;Tab&quot;},{&quot;text&quot;:&quot;\u65B0\u9875\u9762\u6253\u5F00&quot;,&quot;value&quot;:&quot;NewPage&quot;}]" /></uni-forms-item>
      <uni-forms-item label="is_after_delete" name="isAfterDelete"><uni-data-select :modelValue="entity.isAfterDelete" :disabled="readOnlyCondition" @update:modelValue="change('isAfterDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_repeat_pwd" name="isRepeatPwd"><uni-data-select :modelValue="entity.isRepeatPwd" :disabled="readOnlyCondition" @update:modelValue="change('isRepeatPwd', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="save_auto_close" name="saveAutoClose"><uni-easyinput :modelValue="entity.saveAutoClose" :disabled="readOnlyCondition" @update:modelValue="change('saveAutoClose', $event)" /></uni-forms-item>
      <uni-forms-item label="child_style" name="childStyle"><uni-easyinput :modelValue="entity.childStyle" :disabled="readOnlyCondition" @update:modelValue="change('childStyle', $event)" /></uni-forms-item>
      <uni-forms-item label="is_before_delete" name="isBeforeDelete"><uni-data-select :modelValue="entity.isBeforeDelete" :disabled="readOnlyCondition" @update:modelValue="change('isBeforeDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="config_table_name" name="configTableName"><uni-easyinput :modelValue="entity.configTableName" :disabled="readOnlyCondition" @update:modelValue="change('configTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="自动隐藏" name="isAutoHide"><uni-data-select :modelValue="entity.isAutoHide" :disabled="readOnlyCondition" @update:modelValue="change('isAutoHide', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="列表页编辑" name="isListEdit"><uni-data-select :modelValue="entity.isListEdit" :disabled="readOnlyCondition" @update:modelValue="change('isListEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_table_id" name="parentConfigTableId"><uni-easyinput :modelValue="entity.parentConfigTableId" :disabled="readOnlyCondition" @update:modelValue="change('parentConfigTableId', $event)" /></uni-forms-item>
      <uni-forms-item label="parent_config_table_name" name="parentConfigTableName"><uni-easyinput :modelValue="entity.parentConfigTableName" :disabled="readOnlyCondition" @update:modelValue="change('parentConfigTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="页面类型" name="type"><uni-data-select :modelValue="entity.type" :disabled="readOnlyCondition" @update:modelValue="change('type', $event)" :localdata="[{&quot;text&quot;:&quot;\u9ED8\u8BA4&quot;,&quot;value&quot;:0},{&quot;text&quot;:&quot;\u9009\u62E9\u9875&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;\u591A\u9009\u9875&quot;,&quot;value&quot;:2}]" /></uni-forms-item>
      <uni-forms-item label="列表页查询前" name="isBeforeQuery"><uni-data-select :modelValue="entity.isBeforeQuery" :disabled="readOnlyCondition" @update:modelValue="change('isBeforeQuery', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_page_id" name="parentConfigPageId"><uni-easyinput :modelValue="entity.parentConfigPageId" :disabled="readOnlyCondition" @update:modelValue="change('parentConfigPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="详情页查询前" name="isBeforeGet"><uni-data-select :modelValue="entity.isBeforeGet" :disabled="readOnlyCondition" @update:modelValue="change('isBeforeGet', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_page_name" name="parentConfigPageName"><uni-easyinput :modelValue="entity.parentConfigPageName" :disabled="readOnlyCondition" @update:modelValue="change('parentConfigPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="不生成详情页" name="isNotGenerateDetailPage"><uni-data-select :modelValue="entity.isNotGenerateDetailPage" :disabled="readOnlyCondition" @update:modelValue="change('isNotGenerateDetailPage', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ displayValue(entity, 'yzProjectName') }}</text></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" :disabled="readOnlyCondition" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="移动端列表页样式" name="mobileListPageStyleType"><uni-data-select :modelValue="entity.mobileListPageStyleType" :disabled="readOnlyCondition" @update:modelValue="change('mobileListPageStyleType', $event)" :localdata="[{&quot;text&quot;:&quot;\u4F20\u7EDF\u6837\u5F0F&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;Grid\u6837\u5F0F&quot;,&quot;value&quot;:2},{&quot;text&quot;:&quot;Grid\u5E26\u5934\u50CF&quot;,&quot;value&quot;:3}]" /></uni-forms-item>
      <uni-forms-item label="移动端详情页保存后自动返回列表页" name="isAutoBackOnMobileSave"><uni-data-select :modelValue="entity.isAutoBackOnMobileSave" :disabled="readOnlyCondition" @update:modelValue="change('isAutoBackOnMobileSave', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="PC详情页每行列数" name="cols"><uni-easyinput :modelValue="entity.cols" :disabled="readOnlyCondition" @update:modelValue="change('cols', $event)" /></uni-forms-item>
      <uni-forms-item label="仅更新编辑页字段" name="isUpdateEditColumnsOnly"><uni-data-select :modelValue="entity.isUpdateEditColumnsOnly" :disabled="readOnlyCondition" @update:modelValue="change('isUpdateEditColumnsOnly', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="详情页按钮位置" name="detailPageButtonsPosition"><uni-data-select :modelValue="entity.detailPageButtonsPosition" :disabled="readOnlyCondition" @update:modelValue="change('detailPageButtonsPosition', $event)" :localdata="[{&quot;text&quot;:&quot;\u56FA\u5B9A\u5E95\u90E8&quot;,&quot;value&quot;:0},{&quot;text&quot;:&quot;\u5B50\u8868\u4E0A\u9762&quot;,&quot;value&quot;:1}]" /></uni-forms-item>
      <uni-forms-item label="所属后台" name="yzAdminTypeId"><yzp-relation-select :modelValue="entity.yzAdminTypeId" endpoint="/admin/auto/config_page/1868973685063311361/findNameIdYzAdminType2200000000000000105" :parent-id="parentId" :require-parent="true" :require-target-parent="false" :same-parent="false" :readonly="readOnlyCondition" @update:modelValue="change('yzAdminTypeId', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="!readOnlyCondition && (id ? canSave : canCreate)" :disabled="saving" @click="save">保存</button>
    <button v-if="canCopyRecord" :disabled="saving" @click="copy">复制</button>
    <button v-if="canDeleteRecord" :disabled="saving" @click="remove">删除</button>
    <view v-if="childrenReady" class="yzp-children">
      <view><ChildList1868973687051411457 :key="id + ':1868973687051411457'" ref="child_1868973687051411457" :parent_id="id" parent_page_sn="1868973685063311361" :is_son_page="true" :child_page_open_type="1" :parentReadOnlyCondition="readOnlyCondition" /></view>
      <view><ChildList1868973687084965889 :key="id + ':1868973687084965889'" ref="child_1868973687084965889" :parent_id="id" parent_page_sn="1868973685063311361" :is_son_page="true" :child_page_open_type="1" :parentReadOnlyCondition="readOnlyCondition" /></view>
    </view>
    <view v-else class="uni-padding-wrap"><text>为避免数据丢失，请先保存当前页面的数据，再维护子表。</text></view>

  </view>
</template>
<script>
import yzp from '@/common/yzp';

import ChildList1868973687051411457 from '@/pages/yzp/config_page_column-list-6218036784398336.vue';
import ChildList1868973687084965889 from '@/pages/yzp/config_button-list-7097193256583168.vue';
const writableFields = ["name","configTableTableName","orderBy","whereSql","exactDataManageMatchField","pageSize","isEdit","isNew","isUpdate","isDelete","isSearch","isSum","isBatchDelete","isImport","isExport","isMultiSelect","isExportEdit","exportMethod","isCopy","readOnlyCondition","importMethod","buttonVisible","isAfterAdd","isAfterSave","isBeforeSave","childPlace","isAfterDelete","isRepeatPwd","saveAutoClose","childStyle","isBeforeDelete","configTableName","isAutoHide","isListEdit","parentConfigTableId","parentConfigTableName","type","isBeforeQuery","parentConfigPageId","isBeforeGet","parentConfigPageName","isNotGenerateDetailPage","importNo","mobileListPageStyleType","isAutoBackOnMobileSave","cols","isUpdateEditColumnsOnly","detailPageButtonsPosition","yzAdminTypeId"];
const displayFields = {"name":{"alias":null,"optionField":null,"multiNames":false},"configTableTableName":{"alias":null,"optionField":null,"multiNames":false},"orderBy":{"alias":null,"optionField":null,"multiNames":false},"whereSql":{"alias":null,"optionField":null,"multiNames":false},"exactDataManageMatchField":{"alias":null,"optionField":null,"multiNames":false},"pageSize":{"alias":null,"optionField":null,"multiNames":false},"isEdit":{"alias":null,"optionField":null,"multiNames":false},"isNew":{"alias":null,"optionField":null,"multiNames":false},"isUpdate":{"alias":null,"optionField":null,"multiNames":false},"isDelete":{"alias":null,"optionField":null,"multiNames":false},"isSearch":{"alias":null,"optionField":null,"multiNames":false},"isSum":{"alias":null,"optionField":null,"multiNames":false},"isBatchDelete":{"alias":null,"optionField":null,"multiNames":false},"isImport":{"alias":null,"optionField":null,"multiNames":false},"isExport":{"alias":null,"optionField":null,"multiNames":false},"isMultiSelect":{"alias":null,"optionField":null,"multiNames":false},"isExportEdit":{"alias":null,"optionField":null,"multiNames":false},"exportMethod":{"alias":null,"optionField":null,"multiNames":false},"isCopy":{"alias":null,"optionField":null,"multiNames":false},"readOnlyCondition":{"alias":null,"optionField":null,"multiNames":false},"importMethod":{"alias":null,"optionField":null,"multiNames":false},"buttonVisible":{"alias":null,"optionField":null,"multiNames":false},"isAfterAdd":{"alias":null,"optionField":null,"multiNames":false},"isAfterSave":{"alias":null,"optionField":null,"multiNames":false},"isBeforeSave":{"alias":null,"optionField":null,"multiNames":false},"childPlace":{"alias":"childPlaceShow","optionField":null,"multiNames":false},"isAfterDelete":{"alias":null,"optionField":null,"multiNames":false},"isRepeatPwd":{"alias":null,"optionField":null,"multiNames":false},"saveAutoClose":{"alias":null,"optionField":null,"multiNames":false},"childStyle":{"alias":null,"optionField":null,"multiNames":false},"isBeforeDelete":{"alias":null,"optionField":null,"multiNames":false},"configTableName":{"alias":null,"optionField":null,"multiNames":false},"isAutoHide":{"alias":null,"optionField":null,"multiNames":false},"isListEdit":{"alias":null,"optionField":null,"multiNames":false},"parentConfigTableId":{"alias":null,"optionField":null,"multiNames":false},"parentConfigTableName":{"alias":null,"optionField":null,"multiNames":false},"type":{"alias":"typeShow","optionField":null,"multiNames":false},"isBeforeQuery":{"alias":null,"optionField":null,"multiNames":false},"parentConfigPageId":{"alias":null,"optionField":null,"multiNames":false},"isBeforeGet":{"alias":null,"optionField":null,"multiNames":false},"parentConfigPageName":{"alias":null,"optionField":null,"multiNames":false},"isNotGenerateDetailPage":{"alias":null,"optionField":null,"multiNames":false},"yzProjectName":{"alias":null,"optionField":null,"multiNames":false},"importNo":{"alias":null,"optionField":null,"multiNames":false},"mobileListPageStyleType":{"alias":"mobileListPageStyleTypeShow","optionField":null,"multiNames":false},"isAutoBackOnMobileSave":{"alias":null,"optionField":null,"multiNames":false},"cols":{"alias":null,"optionField":null,"multiNames":false},"insertTime":{"alias":null,"optionField":null,"multiNames":false},"updateTime":{"alias":null,"optionField":null,"multiNames":false},"insertManagerId":{"alias":null,"optionField":null,"multiNames":false},"updateManagerId":{"alias":null,"optionField":null,"multiNames":false},"insertManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"updateManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"insertUserName":{"alias":null,"optionField":null,"multiNames":false},"isUpdateEditColumnsOnly":{"alias":null,"optionField":null,"multiNames":false},"updateUserName":{"alias":null,"optionField":null,"multiNames":false},"detailPageButtonsPosition":{"alias":"detailPageButtonsPositionShow","optionField":null,"multiNames":false},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId","multiNames":false},"id":{"alias":null,"optionField":null,"multiNames":false}};
const rtfFields = [];
const children = [{"id":"1868973687051411457","pageId":"6218036784398336","name":"\u9875\u9762\u5B57\u6BB5"},{"id":"1868973687084965889","pageId":"7097193256583168","name":"\u6309\u94AE"}];
const readonlyField = null;
/** 空父键不接受历史 undefined/null 字面量，字符串 ID 全程不转为浮点。 */
const recordId = value => typeof value === 'string' && !['undefined', 'null'].includes(value.trim()) ? value.trim() : '';
export default {
  mixins: [],
  components: { ChildList1868973687051411457, ChildList1868973687084965889 },
  props: { is_props: { type: Boolean, default: false }, props_id: { type: String, default: '' }, props_parent_id: { type: String, default: '' }, props_parent_page_sn: { type: String, default: '' }, parentReadOnlyCondition: { type: Boolean, default: false } },
  data() { return { id: '', parentId: '', parentPageId: '', routeReadOnly: false, recordReady: false, permissionsReady: false, permissionVersion: 0, currentChild: 0, children, canUpdate: true, entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: true, canDelete: true, canCopy: false }; },
  computed: {
    /** 子表继承父只读后仍检查自身当前操作权限；共享快照不代表继承操作授权。 */
    readOnlyCondition() { this.permissionVersion; return !this.permissionsReady || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.id ? !this.canUpdate : !this.canCreate) || readonlyField && this.entity[readonlyField]) || !yzp.hasPagePermission('1868973685063311361', this.id ? 'is_update' : 'is_new'); },
    /** 复制和删除分别检查自身操作授权，修改授权不会隐式开放其他动作。 */
    canCopyRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canCopy && yzp.hasPagePermission('1868973685063311361', 'is_all'); },
    canDeleteRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canDelete && yzp.hasPagePermission('1868973685063311361', 'is_delete'); },
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
      if (this.readOnlyCondition || !this.canCopy || !yzp.hasPagePermission('1868973685063311361', 'is_all') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('1868973685063311361', 'is_all') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/config_page/1868973685063311361/copy/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'POST', sendSessionId: true, isCurrent: current,
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
      this._loadRequest = yzp.request({ url: '/admin/auto/config_page/1868973685063311361/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
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
      this._writeRequest = yzp.request({ url: '/admin/auto/config_page/1868973685063311361/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200 && recordId(res.data?.id)) { this.id = res.data.id; this.entity = res.data; this.changes = {}; this.recordReady = true; this.emitDetailChange('save', res.data, id ? 'update' : 'add'); if (this.is_props) this.closeDetail(); else if (hadChildren) this.$nextTick(() => this.refreshSonsData()); uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
    },
    /** 删除仅通知当前列表，子表刷新不会写入或重载父实体。 */
    remove() {
      if (this.readOnlyCondition || !this.canDelete || !yzp.hasPagePermission('1868973685063311361', 'is_delete') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('1868973685063311361', 'is_delete') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/config_page/1868973685063311361?parentId=' + encodeURIComponent(parentId), method: 'DELETE', data: { id }, sendSessionId: true, isCurrent: current,
          success: res => { if (current() && res.code === 200) { this.recordReady = false; this.emitDetailChange('delete'); this.closeDetail(); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
      } });
    }
  }
};
</script>
