<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page detail-page uni-padding-wrap" :class="{ ispc: $deviceInfo?.is_pc, ismobile: !$deviceInfo?.is_pc }">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" :disabled="readOnlyCondition" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="表名" name="tableName"><uni-easyinput :modelValue="entity.tableName" :disabled="readOnlyCondition" @update:modelValue="change('tableName', $event)" /></uni-forms-item>
      <uni-forms-item label="存储模式" name="isRongyu"><yzp-table-storage-mode :entity="entity" :record-id="id" :readonly="readOnlyCondition" @receive="applyCustom" /></uni-forms-item>
      <uni-forms-item label="功能表" name="isFunctionTable"><uni-data-select :modelValue="entity.isFunctionTable" :disabled="readOnlyCondition" @update:modelValue="change('isFunctionTable', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="sql" name="sql"><uni-easyinput :modelValue="entity.sql" :disabled="readOnlyCondition" @update:modelValue="change('sql', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库视图" name="isView"><uni-data-select :modelValue="entity.isView" :disabled="readOnlyCondition" @update:modelValue="change('isView', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="oracle_sql" name="oracleSql"><uni-easyinput :modelValue="entity.oracleSql" :disabled="readOnlyCondition" @update:modelValue="change('oracleSql', $event)" /></uni-forms-item>
      <uni-forms-item label="where_sql" name="whereSql"><uni-easyinput :modelValue="entity.whereSql" :disabled="readOnlyCondition" @update:modelValue="change('whereSql', $event)" /></uni-forms-item>
      <uni-forms-item label="order_by" name="orderBy"><uni-easyinput :modelValue="entity.orderBy" :disabled="readOnlyCondition" @update:modelValue="change('orderBy', $event)" /></uni-forms-item>
      <uni-forms-item label="group_by" name="groupBy"><uni-easyinput :modelValue="entity.groupBy" :disabled="readOnlyCondition" @update:modelValue="change('groupBy', $event)" /></uni-forms-item>
      <uni-forms-item label="page_size" name="pageSize"><uni-easyinput :modelValue="entity.pageSize" :disabled="readOnlyCondition" @update:modelValue="change('pageSize', $event)" /></uni-forms-item>
      <uni-forms-item label="is_edit" name="isEdit"><uni-easyinput :modelValue="entity.isEdit" :disabled="readOnlyCondition" @update:modelValue="change('isEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="is_new" name="isNew"><uni-easyinput :modelValue="entity.isNew" :disabled="readOnlyCondition" @update:modelValue="change('isNew', $event)" /></uni-forms-item>
      <uni-forms-item label="is_save" name="isSave"><uni-easyinput :modelValue="entity.isSave" :disabled="readOnlyCondition" @update:modelValue="change('isSave', $event)" /></uni-forms-item>
      <uni-forms-item label="is_delete" name="isDelete"><uni-easyinput :modelValue="entity.isDelete" :disabled="readOnlyCondition" @update:modelValue="change('isDelete', $event)" /></uni-forms-item>
      <uni-forms-item label="is_search" name="isSearch"><uni-easyinput :modelValue="entity.isSearch" :disabled="readOnlyCondition" @update:modelValue="change('isSearch', $event)" /></uni-forms-item>
      <uni-forms-item label="before_delete_sql" name="beforeDeleteSql"><uni-easyinput :modelValue="entity.beforeDeleteSql" :disabled="readOnlyCondition" @update:modelValue="change('beforeDeleteSql', $event)" /></uni-forms-item>
      <uni-forms-item label="after_save_sql" name="afterSaveSql"><uni-easyinput :modelValue="entity.afterSaveSql" :disabled="readOnlyCondition" @update:modelValue="change('afterSaveSql', $event)" /></uni-forms-item>
      <uni-forms-item label="after_import_sql" name="afterImportSql"><uni-easyinput :modelValue="entity.afterImportSql" :disabled="readOnlyCondition" @update:modelValue="change('afterImportSql', $event)" /></uni-forms-item>
      <uni-forms-item label="is_import" name="isImport"><uni-easyinput :modelValue="entity.isImport" :disabled="readOnlyCondition" @update:modelValue="change('isImport', $event)" /></uni-forms-item>
      <uni-forms-item label="is_export" name="isExport"><uni-easyinput :modelValue="entity.isExport" :disabled="readOnlyCondition" @update:modelValue="change('isExport', $event)" /></uni-forms-item>
      <uni-forms-item label="is_multi_select" name="isMultiSelect"><uni-easyinput :modelValue="entity.isMultiSelect" :disabled="readOnlyCondition" @update:modelValue="change('isMultiSelect', $event)" /></uni-forms-item>
      <uni-forms-item label="is_lazy_query" name="isLazyQuery"><uni-easyinput :modelValue="entity.isLazyQuery" :disabled="readOnlyCondition" @update:modelValue="change('isLazyQuery', $event)" /></uni-forms-item>
      <uni-forms-item label="grid_edit" name="gridEdit"><uni-easyinput :modelValue="entity.gridEdit" :disabled="readOnlyCondition" @update:modelValue="change('gridEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="title" name="title"><uni-easyinput :modelValue="entity.title" :disabled="readOnlyCondition" @update:modelValue="change('title', $event)" /></uni-forms-item>
      <uni-forms-item label="title_2" name="title2"><uni-easyinput :modelValue="entity.title2" :disabled="readOnlyCondition" @update:modelValue="change('title2', $event)" /></uni-forms-item>
      <uni-forms-item label="title_3" name="title3"><uni-easyinput :modelValue="entity.title3" :disabled="readOnlyCondition" @update:modelValue="change('title3', $event)" /></uni-forms-item>
      <uni-forms-item label="is_sum" name="isSum"><uni-easyinput :modelValue="entity.isSum" :disabled="readOnlyCondition" @update:modelValue="change('isSum', $event)" /></uni-forms-item>
      <uni-forms-item label="grid_width" name="gridWidth"><uni-easyinput :modelValue="entity.gridWidth" :disabled="readOnlyCondition" @update:modelValue="change('gridWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="is_print_preview" name="isPrintPreview"><uni-easyinput :modelValue="entity.isPrintPreview" :disabled="readOnlyCondition" @update:modelValue="change('isPrintPreview', $event)" /></uni-forms-item>
      <uni-forms-item label="is_batch_delete" name="isBatchDelete"><uni-easyinput :modelValue="entity.isBatchDelete" :disabled="readOnlyCondition" @update:modelValue="change('isBatchDelete', $event)" /></uni-forms-item>
      <uni-forms-item label="import_method" name="importMethod"><uni-easyinput :modelValue="entity.importMethod" :disabled="readOnlyCondition" @update:modelValue="change('importMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="read_only_condition" name="readOnlyCondition"><uni-easyinput :modelValue="entity.readOnlyCondition" :disabled="readOnlyCondition" @update:modelValue="change('readOnlyCondition', $event)" /></uni-forms-item>
      <uni-forms-item label="pop_window_width" name="popWindowWidth"><uni-easyinput :modelValue="entity.popWindowWidth" :disabled="readOnlyCondition" @update:modelValue="change('popWindowWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="pop_window_height" name="popWindowHeight"><uni-easyinput :modelValue="entity.popWindowHeight" :disabled="readOnlyCondition" @update:modelValue="change('popWindowHeight', $event)" /></uni-forms-item>
      <uni-forms-item label="is_export_edit" name="isExportEdit"><uni-easyinput :modelValue="entity.isExportEdit" :disabled="readOnlyCondition" @update:modelValue="change('isExportEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="is_no_save_and_close" name="isNoSaveAndClose"><uni-easyinput :modelValue="entity.isNoSaveAndClose" :disabled="readOnlyCondition" @update:modelValue="change('isNoSaveAndClose', $event)" /></uni-forms-item>
      <uni-forms-item label="is_update" name="isUpdate"><uni-easyinput :modelValue="entity.isUpdate" :disabled="readOnlyCondition" @update:modelValue="change('isUpdate', $event)" /></uni-forms-item>
      <uni-forms-item label="is_head_group" name="isHeadGroup"><uni-easyinput :modelValue="entity.isHeadGroup" :disabled="readOnlyCondition" @update:modelValue="change('isHeadGroup', $event)" /></uni-forms-item>
      <uni-forms-item label="is_modify_log" name="isModifyLog"><uni-easyinput :modelValue="entity.isModifyLog" :disabled="readOnlyCondition" @update:modelValue="change('isModifyLog', $event)" /></uni-forms-item>
      <uni-forms-item label="layout" name="layout"><uni-easyinput :modelValue="entity.layout" :disabled="readOnlyCondition" @update:modelValue="change('layout', $event)" /></uni-forms-item>
      <uni-forms-item label="conn_string" name="connString"><uni-easyinput :modelValue="entity.connString" :disabled="readOnlyCondition" @update:modelValue="change('connString', $event)" /></uni-forms-item>
      <uni-forms-item label="export_method" name="exportMethod"><uni-easyinput :modelValue="entity.exportMethod" :disabled="readOnlyCondition" @update:modelValue="change('exportMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" :disabled="readOnlyCondition" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="copy_method" name="copyMethod"><uni-easyinput :modelValue="entity.copyMethod" :disabled="readOnlyCondition" @update:modelValue="change('copyMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="sql_more" name="sqlMore"><uni-easyinput :modelValue="entity.sqlMore" :disabled="readOnlyCondition" @update:modelValue="change('sqlMore', $event)" /></uni-forms-item>
      <uni-forms-item label="authorize_scheme" name="authorizeScheme"><uni-easyinput :modelValue="entity.authorizeScheme" :disabled="readOnlyCondition" @update:modelValue="change('authorizeScheme', $event)" /></uni-forms-item>
      <uni-forms-item label="clear_method" name="clearMethod"><uni-easyinput :modelValue="entity.clearMethod" :disabled="readOnlyCondition" @update:modelValue="change('clearMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="is_raw" name="isRaw"><uni-easyinput :modelValue="entity.isRaw" :disabled="readOnlyCondition" @update:modelValue="change('isRaw', $event)" /></uni-forms-item>
      <uni-forms-item label="same_edit_code" name="sameEditCode"><uni-easyinput :modelValue="entity.sameEditCode" :disabled="readOnlyCondition" @update:modelValue="change('sameEditCode', $event)" /></uni-forms-item>
      <uni-forms-item label="sql_grid" name="sqlGrid"><uni-easyinput :modelValue="entity.sqlGrid" :disabled="readOnlyCondition" @update:modelValue="change('sqlGrid', $event)" /></uni-forms-item>
      <uni-forms-item label="data_src" name="dataSrc"><uni-easyinput :modelValue="entity.dataSrc" :disabled="readOnlyCondition" @update:modelValue="change('dataSrc', $event)" /></uni-forms-item>
      <uni-forms-item label="main_column" name="mainColumn"><uni-easyinput :modelValue="entity.mainColumn" :disabled="readOnlyCondition" @update:modelValue="change('mainColumn', $event)" /></uni-forms-item>
      <uni-forms-item label="batch_new_page_name" name="batchNewPageName"><uni-easyinput :modelValue="entity.batchNewPageName" :disabled="readOnlyCondition" @update:modelValue="change('batchNewPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_table_id" name="editTableId"><uni-easyinput :modelValue="entity.editTableId" :disabled="readOnlyCondition" @update:modelValue="change('editTableId', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_table_name" name="editTableName"><uni-easyinput :modelValue="entity.editTableName" :disabled="readOnlyCondition" @update:modelValue="change('editTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_page_id" name="editPageId"><uni-easyinput :modelValue="entity.editPageId" :disabled="readOnlyCondition" @update:modelValue="change('editPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_page_name" name="editPageName"><uni-easyinput :modelValue="entity.editPageName" :disabled="readOnlyCondition" @update:modelValue="change('editPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_key_column" name="editKeyColumn"><uni-easyinput :modelValue="entity.editKeyColumn" :disabled="readOnlyCondition" @update:modelValue="change('editKeyColumn', $event)" /></uni-forms-item>
      <uni-forms-item label="button_visible" name="buttonVisible"><uni-easyinput :modelValue="entity.buttonVisible" :disabled="readOnlyCondition" @update:modelValue="change('buttonVisible', $event)" /></uni-forms-item>
      <uni-forms-item label="auto_save" name="autoSave"><uni-easyinput :modelValue="entity.autoSave" :disabled="readOnlyCondition" @update:modelValue="change('autoSave', $event)" /></uni-forms-item>
      <uni-forms-item label="child_place" name="childPlace"><uni-easyinput :modelValue="entity.childPlace" :disabled="readOnlyCondition" @update:modelValue="change('childPlace', $event)" /></uni-forms-item>
      <uni-forms-item label="super_column_name" name="superColumnName"><uni-easyinput :modelValue="entity.superColumnName" :disabled="readOnlyCondition" @update:modelValue="change('superColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_page_url" name="editPageUrl"><uni-easyinput :modelValue="entity.editPageUrl" :disabled="readOnlyCondition" @update:modelValue="change('editPageUrl', $event)" /></uni-forms-item>
      <uni-forms-item label="display_option" name="displayOption"><uni-easyinput :modelValue="entity.displayOption" :disabled="readOnlyCondition" @update:modelValue="change('displayOption', $event)" /></uni-forms-item>
      <uni-forms-item label="bpm_step_kind" name="bpmStepKind"><uni-easyinput :modelValue="entity.bpmStepKind" :disabled="readOnlyCondition" @update:modelValue="change('bpmStepKind', $event)" /></uni-forms-item>
      <uni-forms-item label="document_ready" name="documentReady"><uni-easyinput :modelValue="entity.documentReady" :disabled="readOnlyCondition" @update:modelValue="change('documentReady', $event)" /></uni-forms-item>
      <uni-forms-item label="help" name="help"><uni-easyinput :modelValue="entity.help" :disabled="readOnlyCondition" @update:modelValue="change('help', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_width" name="editWidth"><uni-easyinput :modelValue="entity.editWidth" :disabled="readOnlyCondition" @update:modelValue="change('editWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ displayValue(entity, 'yzProjectName') }}</text></uni-forms-item>
      <uni-forms-item label="is_platform" name="isPlatform"><uni-easyinput :modelValue="entity.isPlatform" :disabled="readOnlyCondition" @update:modelValue="change('isPlatform', $event)" /></uni-forms-item>
      <uni-forms-item label="is_sys" name="isSys"><uni-easyinput :modelValue="entity.isSys" :disabled="readOnlyCondition" @update:modelValue="change('isSys', $event)" /></uni-forms-item>
      <uni-forms-item label="is_repeat_pwd" name="isRepeatPwd"><uni-easyinput :modelValue="entity.isRepeatPwd" :disabled="readOnlyCondition" @update:modelValue="change('isRepeatPwd', $event)" /></uni-forms-item>
      <uni-forms-item label="所属后台" name="yzAdminTypeId"><yzp-relation-select :modelValue="entity.yzAdminTypeId" endpoint="/admin/auto/config_table/6191083005411328/findNameIdYzAdminType2200000000000000087" :parent-id="parentId" :require-parent="false" :require-target-parent="false" :same-parent="false" :readonly="readOnlyCondition" @update:modelValue="change('yzAdminTypeId', $event)" /></uni-forms-item>
      <uni-forms-item label="数据归属模式" name="scopeKind"><uni-easyinput :modelValue="entity.scopeKind" :disabled="readOnlyCondition" @update:modelValue="change('scopeKind', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="!readOnlyCondition && (id ? canSave : canCreate)" :disabled="saving" @click="save">保存</button>
    <button v-if="canCopyRecord" :disabled="saving" @click="copy">复制</button>
    <button v-if="canDeleteRecord" :disabled="saving" @click="remove">删除</button>
    <view v-if="childrenReady" class="yzp-children">
      <view><ChildList1868973890525487105 :key="id + ':1868973890525487105'" ref="child_1868973890525487105" :parent_id="id" parent_page_sn="6191083005411328" :is_son_page="true" :child_page_open_type="1" :parentReadOnlyCondition="readOnlyCondition" /></view>
      <view><ChildList1610249810450849794 :key="id + ':1610249810450849794'" ref="child_1610249810450849794" :parent_id="id" parent_page_sn="6191083005411328" :is_son_page="true" :child_page_open_type="1" :parentReadOnlyCondition="readOnlyCondition" /></view>
    </view>
    <view v-else class="uni-padding-wrap"><text>为避免数据丢失，请先保存当前页面的数据，再维护子表。</text></view>
  </view>
</template>
<script>
import yzp from '@/common/yzp';

import ChildList1868973890525487105 from '@/pages/yzp/config_page-list-1868973685063311361.vue';
import ChildList1610249810450849794 from '@/pages/yzp/config_column-list-6217715532627968.vue';
const writableFields = ["name","tableName","isRongyu","isFunctionTable","sql","isView","oracleSql","whereSql","orderBy","groupBy","pageSize","isEdit","isNew","isSave","isDelete","isSearch","beforeDeleteSql","afterSaveSql","afterImportSql","isImport","isExport","isMultiSelect","isLazyQuery","gridEdit","title","title2","title3","isSum","gridWidth","isPrintPreview","isBatchDelete","importMethod","readOnlyCondition","popWindowWidth","popWindowHeight","isExportEdit","isNoSaveAndClose","isUpdate","isHeadGroup","isModifyLog","layout","connString","exportMethod","importNo","copyMethod","sqlMore","authorizeScheme","clearMethod","isRaw","sameEditCode","sqlGrid","dataSrc","mainColumn","batchNewPageName","editTableId","editTableName","editPageId","editPageName","editKeyColumn","buttonVisible","autoSave","childPlace","superColumnName","editPageUrl","displayOption","bpmStepKind","documentReady","help","editWidth","isPlatform","isSys","isRepeatPwd","yzAdminTypeId","scopeKind"];
const displayFields = {"name":{"alias":null,"optionField":null},"tableName":{"alias":null,"optionField":null},"isRongyu":{"alias":null,"optionField":null},"isFunctionTable":{"alias":null,"optionField":null},"sql":{"alias":null,"optionField":null},"isView":{"alias":null,"optionField":null},"oracleSql":{"alias":null,"optionField":null},"whereSql":{"alias":null,"optionField":null},"orderBy":{"alias":null,"optionField":null},"groupBy":{"alias":null,"optionField":null},"pageSize":{"alias":null,"optionField":null},"isEdit":{"alias":null,"optionField":null},"isNew":{"alias":null,"optionField":null},"isSave":{"alias":null,"optionField":null},"isDelete":{"alias":null,"optionField":null},"isSearch":{"alias":null,"optionField":null},"beforeDeleteSql":{"alias":null,"optionField":null},"afterSaveSql":{"alias":null,"optionField":null},"afterImportSql":{"alias":null,"optionField":null},"isImport":{"alias":null,"optionField":null},"isExport":{"alias":null,"optionField":null},"isMultiSelect":{"alias":null,"optionField":null},"isLazyQuery":{"alias":null,"optionField":null},"gridEdit":{"alias":null,"optionField":null},"title":{"alias":null,"optionField":null},"title2":{"alias":null,"optionField":null},"title3":{"alias":null,"optionField":null},"isSum":{"alias":null,"optionField":null},"gridWidth":{"alias":null,"optionField":null},"isPrintPreview":{"alias":null,"optionField":null},"isBatchDelete":{"alias":null,"optionField":null},"importMethod":{"alias":null,"optionField":null},"readOnlyCondition":{"alias":null,"optionField":null},"popWindowWidth":{"alias":null,"optionField":null},"popWindowHeight":{"alias":null,"optionField":null},"isExportEdit":{"alias":null,"optionField":null},"isNoSaveAndClose":{"alias":null,"optionField":null},"isUpdate":{"alias":null,"optionField":null},"isHeadGroup":{"alias":null,"optionField":null},"isModifyLog":{"alias":null,"optionField":null},"layout":{"alias":null,"optionField":null},"connString":{"alias":null,"optionField":null},"exportMethod":{"alias":null,"optionField":null},"importNo":{"alias":null,"optionField":null},"insertTime":{"alias":null,"optionField":null},"updateTime":{"alias":null,"optionField":null},"insertManagerId":{"alias":null,"optionField":null},"insertUserName":{"alias":null,"optionField":null},"updateManagerId":{"alias":null,"optionField":null},"insertManagerRealName":{"alias":null,"optionField":null},"updateManagerRealName":{"alias":null,"optionField":null},"updateUserName":{"alias":null,"optionField":null},"copyMethod":{"alias":null,"optionField":null},"sqlMore":{"alias":null,"optionField":null},"authorizeScheme":{"alias":null,"optionField":null},"clearMethod":{"alias":null,"optionField":null},"isRaw":{"alias":null,"optionField":null},"sameEditCode":{"alias":null,"optionField":null},"sqlGrid":{"alias":null,"optionField":null},"dataSrc":{"alias":null,"optionField":null},"mainColumn":{"alias":null,"optionField":null},"batchNewPageName":{"alias":null,"optionField":null},"editTableId":{"alias":null,"optionField":null},"editTableName":{"alias":null,"optionField":null},"editPageId":{"alias":null,"optionField":null},"editPageName":{"alias":null,"optionField":null},"editKeyColumn":{"alias":null,"optionField":null},"buttonVisible":{"alias":null,"optionField":null},"autoSave":{"alias":null,"optionField":null},"childPlace":{"alias":null,"optionField":null},"superColumnName":{"alias":null,"optionField":null},"editPageUrl":{"alias":null,"optionField":null},"displayOption":{"alias":null,"optionField":null},"bpmStepKind":{"alias":null,"optionField":null},"documentReady":{"alias":null,"optionField":null},"help":{"alias":null,"optionField":null},"editWidth":{"alias":null,"optionField":null},"yzProjectName":{"alias":null,"optionField":null},"isPlatform":{"alias":null,"optionField":null},"isSys":{"alias":null,"optionField":null},"isRepeatPwd":{"alias":null,"optionField":null},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId"},"scopeKind":{"alias":null,"optionField":null},"id":{"alias":null,"optionField":null}};
const rtfFields = [];
const children = [{"id":"1868973890525487105","pageId":"1868973685063311361","name":"\u914D\u7F6E\u9875\u9762"},{"id":"1610249810450849794","pageId":"6217715532627968","name":"\u5217"}];
const readonlyField = null;
/** 空父键不接受历史 undefined/null 字面量，字符串 ID 全程不转为浮点。 */
const recordId = value => typeof value === 'string' && !['undefined', 'null'].includes(value.trim()) ? value.trim() : '';
export default {
  mixins: [],
  components: { ChildList1868973890525487105, ChildList1610249810450849794 },
  props: { is_props: { type: Boolean, default: false }, props_id: { type: String, default: '' }, props_parent_id: { type: String, default: '' }, props_parent_page_sn: { type: String, default: '' }, parentReadOnlyCondition: { type: Boolean, default: false } },
  data() { return { id: '', parentId: '', parentPageId: '', routeReadOnly: false, recordReady: false, permissionsReady: false, permissionVersion: 0, currentChild: 0, children, canUpdate: true, entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: false }; },
  computed: {
    /** 子表继承父只读后仍检查自身当前操作权限；共享快照不代表继承操作授权。 */
    readOnlyCondition() { this.permissionVersion; return !this.permissionsReady || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.id ? !this.canUpdate : !this.canCreate) || readonlyField && this.entity[readonlyField]) || !yzp.hasPagePermission('6191083005411328', this.id ? 'is_update' : 'is_new'); },
    /** 复制和删除分别检查自身操作授权，修改授权不会隐式开放其他动作。 */
    canCopyRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canCopy && yzp.hasPagePermission('6191083005411328', 'is_all'); },
    canDeleteRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canDelete && yzp.hasPagePermission('6191083005411328', 'is_delete'); },
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
      const epoch = this._epoch, current = () => this._alive && epoch === this._epoch;
      const ready = () => { if (!current()) return; this.permissionsReady = true; this.permissionVersion++; this.loadOptions(); if (this.id) this.load(); };
      // 嵌入子页复用父页已刷新的权限快照，避免每个子模块重复请求。
      if (this.parentPageId) ready(); else yzp.refreshCurrentPermissions(ready, ready);
    },
    /** 取消读写及选项请求；迟到的弹层或导航回调不影响下一条父记录。 */
    disposeDetail() { this._alive = false; this._epoch = (this._epoch || 0) + 1; this._loadRequest?.abort?.(); this._writeRequest?.abort?.(); (this._optionRequests || []).forEach(task => task?.abort?.()); },
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
      if (this.readOnlyCondition || !this.canCopy || !yzp.hasPagePermission('6191083005411328', 'is_all') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('6191083005411328', 'is_all') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/config_table/6191083005411328/copy/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'POST', sendSessionId: true, isCurrent: current,
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
      this._loadRequest = yzp.request({ url: '/admin/auto/config_table/6191083005411328/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200 && recordId(res.data?.id) === id) { this.entity = res.data; this.changes = {}; this.recordReady = true; } } });
    },
    /** 只记录用户修改且在白名单内的字段，原值不变的回调不会把 null 改为空字符串。 */
    change(field, value) { if (this.readOnlyCondition || !writableFields.includes(field) || JSON.stringify(this.entity[field]) === JSON.stringify(value)) return; this.entity[field] = value; this.changes[field] = value; },
    /** 消费真实常量展示别名和关系选项，缺失名称时仍显示原 ID。 */
    displayValue(row, field) {
      const definition = displayFields[field];
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
      if (this.saving || this.readOnlyCondition || !(this.id ? this.canSave : this.canCreate) || false && !this.parentId) return;
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      const id = this.id, parentId = this.parentId, identity = this.currentIdentity(), epoch = this._epoch, hadChildren = this.childrenReady;
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      this.saving = true;
      this._writeRequest = yzp.request({ url: '/admin/auto/config_table/6191083005411328/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200 && recordId(res.data?.id)) { this.id = res.data.id; this.entity = res.data; this.changes = {}; this.recordReady = true; this.emitDetailChange('save', res.data, id ? 'update' : 'add'); if (this.is_props) this.closeDetail(); else if (hadChildren) this.$nextTick(() => this.refreshSonsData()); uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
    },
    /** 删除仅通知当前列表，子表刷新不会写入或重载父实体。 */
    remove() {
      if (this.readOnlyCondition || !this.canDelete || !yzp.hasPagePermission('6191083005411328', 'is_delete') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('6191083005411328', 'is_delete') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/config_table/6191083005411328?parentId=' + encodeURIComponent(parentId), method: 'DELETE', data: { id }, sendSessionId: true, isCurrent: current,
          success: res => { if (current() && res.code === 200) { this.recordReady = false; this.emitDetailChange('delete'); this.closeDetail(); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
      } });
    }
  }
};
</script>
