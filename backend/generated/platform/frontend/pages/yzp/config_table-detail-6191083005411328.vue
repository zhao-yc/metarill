<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="表名" name="tableName"><uni-easyinput :modelValue="entity.tableName" @update:modelValue="change('tableName', $event)" /></uni-forms-item>
      <uni-forms-item label="存储模式" name="isRongyu"><yzp-table-storage-mode :entity="entity" :record-id="id" :readonly="false" @receive="applyCustom" /></uni-forms-item>
      <uni-forms-item label="功能表" name="isFunctionTable"><uni-data-select :modelValue="entity.isFunctionTable" @update:modelValue="change('isFunctionTable', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="sql" name="sql"><uni-easyinput :modelValue="entity.sql" @update:modelValue="change('sql', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库视图" name="isView"><uni-data-select :modelValue="entity.isView" @update:modelValue="change('isView', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="oracle_sql" name="oracleSql"><uni-easyinput :modelValue="entity.oracleSql" @update:modelValue="change('oracleSql', $event)" /></uni-forms-item>
      <uni-forms-item label="where_sql" name="whereSql"><uni-easyinput :modelValue="entity.whereSql" @update:modelValue="change('whereSql', $event)" /></uni-forms-item>
      <uni-forms-item label="order_by" name="orderBy"><uni-easyinput :modelValue="entity.orderBy" @update:modelValue="change('orderBy', $event)" /></uni-forms-item>
      <uni-forms-item label="group_by" name="groupBy"><uni-easyinput :modelValue="entity.groupBy" @update:modelValue="change('groupBy', $event)" /></uni-forms-item>
      <uni-forms-item label="page_size" name="pageSize"><uni-easyinput :modelValue="entity.pageSize" @update:modelValue="change('pageSize', $event)" /></uni-forms-item>
      <uni-forms-item label="is_edit" name="isEdit"><uni-easyinput :modelValue="entity.isEdit" @update:modelValue="change('isEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="is_new" name="isNew"><uni-easyinput :modelValue="entity.isNew" @update:modelValue="change('isNew', $event)" /></uni-forms-item>
      <uni-forms-item label="is_save" name="isSave"><uni-easyinput :modelValue="entity.isSave" @update:modelValue="change('isSave', $event)" /></uni-forms-item>
      <uni-forms-item label="is_delete" name="isDelete"><uni-easyinput :modelValue="entity.isDelete" @update:modelValue="change('isDelete', $event)" /></uni-forms-item>
      <uni-forms-item label="is_search" name="isSearch"><uni-easyinput :modelValue="entity.isSearch" @update:modelValue="change('isSearch', $event)" /></uni-forms-item>
      <uni-forms-item label="before_delete_sql" name="beforeDeleteSql"><uni-easyinput :modelValue="entity.beforeDeleteSql" @update:modelValue="change('beforeDeleteSql', $event)" /></uni-forms-item>
      <uni-forms-item label="after_save_sql" name="afterSaveSql"><uni-easyinput :modelValue="entity.afterSaveSql" @update:modelValue="change('afterSaveSql', $event)" /></uni-forms-item>
      <uni-forms-item label="after_import_sql" name="afterImportSql"><uni-easyinput :modelValue="entity.afterImportSql" @update:modelValue="change('afterImportSql', $event)" /></uni-forms-item>
      <uni-forms-item label="is_import" name="isImport"><uni-easyinput :modelValue="entity.isImport" @update:modelValue="change('isImport', $event)" /></uni-forms-item>
      <uni-forms-item label="is_export" name="isExport"><uni-easyinput :modelValue="entity.isExport" @update:modelValue="change('isExport', $event)" /></uni-forms-item>
      <uni-forms-item label="is_multi_select" name="isMultiSelect"><uni-easyinput :modelValue="entity.isMultiSelect" @update:modelValue="change('isMultiSelect', $event)" /></uni-forms-item>
      <uni-forms-item label="is_lazy_query" name="isLazyQuery"><uni-easyinput :modelValue="entity.isLazyQuery" @update:modelValue="change('isLazyQuery', $event)" /></uni-forms-item>
      <uni-forms-item label="grid_edit" name="gridEdit"><uni-easyinput :modelValue="entity.gridEdit" @update:modelValue="change('gridEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="title" name="title"><uni-easyinput :modelValue="entity.title" @update:modelValue="change('title', $event)" /></uni-forms-item>
      <uni-forms-item label="title_2" name="title2"><uni-easyinput :modelValue="entity.title2" @update:modelValue="change('title2', $event)" /></uni-forms-item>
      <uni-forms-item label="title_3" name="title3"><uni-easyinput :modelValue="entity.title3" @update:modelValue="change('title3', $event)" /></uni-forms-item>
      <uni-forms-item label="is_sum" name="isSum"><uni-easyinput :modelValue="entity.isSum" @update:modelValue="change('isSum', $event)" /></uni-forms-item>
      <uni-forms-item label="grid_width" name="gridWidth"><uni-easyinput :modelValue="entity.gridWidth" @update:modelValue="change('gridWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="is_print_preview" name="isPrintPreview"><uni-easyinput :modelValue="entity.isPrintPreview" @update:modelValue="change('isPrintPreview', $event)" /></uni-forms-item>
      <uni-forms-item label="is_batch_delete" name="isBatchDelete"><uni-easyinput :modelValue="entity.isBatchDelete" @update:modelValue="change('isBatchDelete', $event)" /></uni-forms-item>
      <uni-forms-item label="import_method" name="importMethod"><uni-easyinput :modelValue="entity.importMethod" @update:modelValue="change('importMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="read_only_condition" name="readOnlyCondition"><uni-easyinput :modelValue="entity.readOnlyCondition" @update:modelValue="change('readOnlyCondition', $event)" /></uni-forms-item>
      <uni-forms-item label="pop_window_width" name="popWindowWidth"><uni-easyinput :modelValue="entity.popWindowWidth" @update:modelValue="change('popWindowWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="pop_window_height" name="popWindowHeight"><uni-easyinput :modelValue="entity.popWindowHeight" @update:modelValue="change('popWindowHeight', $event)" /></uni-forms-item>
      <uni-forms-item label="is_export_edit" name="isExportEdit"><uni-easyinput :modelValue="entity.isExportEdit" @update:modelValue="change('isExportEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="is_no_save_and_close" name="isNoSaveAndClose"><uni-easyinput :modelValue="entity.isNoSaveAndClose" @update:modelValue="change('isNoSaveAndClose', $event)" /></uni-forms-item>
      <uni-forms-item label="is_update" name="isUpdate"><uni-easyinput :modelValue="entity.isUpdate" @update:modelValue="change('isUpdate', $event)" /></uni-forms-item>
      <uni-forms-item label="is_head_group" name="isHeadGroup"><uni-easyinput :modelValue="entity.isHeadGroup" @update:modelValue="change('isHeadGroup', $event)" /></uni-forms-item>
      <uni-forms-item label="is_modify_log" name="isModifyLog"><uni-easyinput :modelValue="entity.isModifyLog" @update:modelValue="change('isModifyLog', $event)" /></uni-forms-item>
      <uni-forms-item label="layout" name="layout"><uni-easyinput :modelValue="entity.layout" @update:modelValue="change('layout', $event)" /></uni-forms-item>
      <uni-forms-item label="conn_string" name="connString"><uni-easyinput :modelValue="entity.connString" @update:modelValue="change('connString', $event)" /></uni-forms-item>
      <uni-forms-item label="export_method" name="exportMethod"><uni-easyinput :modelValue="entity.exportMethod" @update:modelValue="change('exportMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="copy_method" name="copyMethod"><uni-easyinput :modelValue="entity.copyMethod" @update:modelValue="change('copyMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="sql_more" name="sqlMore"><uni-easyinput :modelValue="entity.sqlMore" @update:modelValue="change('sqlMore', $event)" /></uni-forms-item>
      <uni-forms-item label="authorize_scheme" name="authorizeScheme"><uni-easyinput :modelValue="entity.authorizeScheme" @update:modelValue="change('authorizeScheme', $event)" /></uni-forms-item>
      <uni-forms-item label="clear_method" name="clearMethod"><uni-easyinput :modelValue="entity.clearMethod" @update:modelValue="change('clearMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="is_raw" name="isRaw"><uni-easyinput :modelValue="entity.isRaw" @update:modelValue="change('isRaw', $event)" /></uni-forms-item>
      <uni-forms-item label="same_edit_code" name="sameEditCode"><uni-easyinput :modelValue="entity.sameEditCode" @update:modelValue="change('sameEditCode', $event)" /></uni-forms-item>
      <uni-forms-item label="sql_grid" name="sqlGrid"><uni-easyinput :modelValue="entity.sqlGrid" @update:modelValue="change('sqlGrid', $event)" /></uni-forms-item>
      <uni-forms-item label="data_src" name="dataSrc"><uni-easyinput :modelValue="entity.dataSrc" @update:modelValue="change('dataSrc', $event)" /></uni-forms-item>
      <uni-forms-item label="main_column" name="mainColumn"><uni-easyinput :modelValue="entity.mainColumn" @update:modelValue="change('mainColumn', $event)" /></uni-forms-item>
      <uni-forms-item label="batch_new_page_name" name="batchNewPageName"><uni-easyinput :modelValue="entity.batchNewPageName" @update:modelValue="change('batchNewPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_table_id" name="editTableId"><uni-easyinput :modelValue="entity.editTableId" @update:modelValue="change('editTableId', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_table_name" name="editTableName"><uni-easyinput :modelValue="entity.editTableName" @update:modelValue="change('editTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_page_id" name="editPageId"><uni-easyinput :modelValue="entity.editPageId" @update:modelValue="change('editPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_page_name" name="editPageName"><uni-easyinput :modelValue="entity.editPageName" @update:modelValue="change('editPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_key_column" name="editKeyColumn"><uni-easyinput :modelValue="entity.editKeyColumn" @update:modelValue="change('editKeyColumn', $event)" /></uni-forms-item>
      <uni-forms-item label="button_visible" name="buttonVisible"><uni-easyinput :modelValue="entity.buttonVisible" @update:modelValue="change('buttonVisible', $event)" /></uni-forms-item>
      <uni-forms-item label="auto_save" name="autoSave"><uni-easyinput :modelValue="entity.autoSave" @update:modelValue="change('autoSave', $event)" /></uni-forms-item>
      <uni-forms-item label="child_place" name="childPlace"><uni-easyinput :modelValue="entity.childPlace" @update:modelValue="change('childPlace', $event)" /></uni-forms-item>
      <uni-forms-item label="super_column_name" name="superColumnName"><uni-easyinput :modelValue="entity.superColumnName" @update:modelValue="change('superColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_page_url" name="editPageUrl"><uni-easyinput :modelValue="entity.editPageUrl" @update:modelValue="change('editPageUrl', $event)" /></uni-forms-item>
      <uni-forms-item label="display_option" name="displayOption"><uni-easyinput :modelValue="entity.displayOption" @update:modelValue="change('displayOption', $event)" /></uni-forms-item>
      <uni-forms-item label="bpm_step_kind" name="bpmStepKind"><uni-easyinput :modelValue="entity.bpmStepKind" @update:modelValue="change('bpmStepKind', $event)" /></uni-forms-item>
      <uni-forms-item label="document_ready" name="documentReady"><uni-easyinput :modelValue="entity.documentReady" @update:modelValue="change('documentReady', $event)" /></uni-forms-item>
      <uni-forms-item label="help" name="help"><uni-easyinput :modelValue="entity.help" @update:modelValue="change('help', $event)" /></uni-forms-item>
      <uni-forms-item label="edit_width" name="editWidth"><uni-easyinput :modelValue="entity.editWidth" @update:modelValue="change('editWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ displayValue(entity, 'yzProjectName') }}</text></uni-forms-item>
      <uni-forms-item label="is_platform" name="isPlatform"><uni-easyinput :modelValue="entity.isPlatform" @update:modelValue="change('isPlatform', $event)" /></uni-forms-item>
      <uni-forms-item label="is_sys" name="isSys"><uni-easyinput :modelValue="entity.isSys" @update:modelValue="change('isSys', $event)" /></uni-forms-item>
      <uni-forms-item label="is_repeat_pwd" name="isRepeatPwd"><uni-easyinput :modelValue="entity.isRepeatPwd" @update:modelValue="change('isRepeatPwd', $event)" /></uni-forms-item>
      <uni-forms-item label="所属后台" name="yzAdminTypeId"><zxz-uni-data-select filterable :modelValue="entity.yzAdminTypeId" @update:modelValue="change('yzAdminTypeId', $event)" :localdata="options.yzAdminTypeId || []" /></uni-forms-item>
      <uni-forms-item label="数据归属模式" name="scopeKind"><uni-easyinput :modelValue="entity.scopeKind" @update:modelValue="change('scopeKind', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","tableName","isRongyu","isFunctionTable","sql","isView","oracleSql","whereSql","orderBy","groupBy","pageSize","isEdit","isNew","isSave","isDelete","isSearch","beforeDeleteSql","afterSaveSql","afterImportSql","isImport","isExport","isMultiSelect","isLazyQuery","gridEdit","title","title2","title3","isSum","gridWidth","isPrintPreview","isBatchDelete","importMethod","readOnlyCondition","popWindowWidth","popWindowHeight","isExportEdit","isNoSaveAndClose","isUpdate","isHeadGroup","isModifyLog","layout","connString","exportMethod","importNo","copyMethod","sqlMore","authorizeScheme","clearMethod","isRaw","sameEditCode","sqlGrid","dataSrc","mainColumn","batchNewPageName","editTableId","editTableName","editPageId","editPageName","editKeyColumn","buttonVisible","autoSave","childPlace","superColumnName","editPageUrl","displayOption","bpmStepKind","documentReady","help","editWidth","isPlatform","isSys","isRepeatPwd","yzAdminTypeId","scopeKind"];
const displayFields = {"name":{"alias":null,"optionField":null},"tableName":{"alias":null,"optionField":null},"isRongyu":{"alias":null,"optionField":null},"isFunctionTable":{"alias":null,"optionField":null},"sql":{"alias":null,"optionField":null},"isView":{"alias":null,"optionField":null},"oracleSql":{"alias":null,"optionField":null},"whereSql":{"alias":null,"optionField":null},"orderBy":{"alias":null,"optionField":null},"groupBy":{"alias":null,"optionField":null},"pageSize":{"alias":null,"optionField":null},"isEdit":{"alias":null,"optionField":null},"isNew":{"alias":null,"optionField":null},"isSave":{"alias":null,"optionField":null},"isDelete":{"alias":null,"optionField":null},"isSearch":{"alias":null,"optionField":null},"beforeDeleteSql":{"alias":null,"optionField":null},"afterSaveSql":{"alias":null,"optionField":null},"afterImportSql":{"alias":null,"optionField":null},"isImport":{"alias":null,"optionField":null},"isExport":{"alias":null,"optionField":null},"isMultiSelect":{"alias":null,"optionField":null},"isLazyQuery":{"alias":null,"optionField":null},"gridEdit":{"alias":null,"optionField":null},"title":{"alias":null,"optionField":null},"title2":{"alias":null,"optionField":null},"title3":{"alias":null,"optionField":null},"isSum":{"alias":null,"optionField":null},"gridWidth":{"alias":null,"optionField":null},"isPrintPreview":{"alias":null,"optionField":null},"isBatchDelete":{"alias":null,"optionField":null},"importMethod":{"alias":null,"optionField":null},"readOnlyCondition":{"alias":null,"optionField":null},"popWindowWidth":{"alias":null,"optionField":null},"popWindowHeight":{"alias":null,"optionField":null},"isExportEdit":{"alias":null,"optionField":null},"isNoSaveAndClose":{"alias":null,"optionField":null},"isUpdate":{"alias":null,"optionField":null},"isHeadGroup":{"alias":null,"optionField":null},"isModifyLog":{"alias":null,"optionField":null},"layout":{"alias":null,"optionField":null},"connString":{"alias":null,"optionField":null},"exportMethod":{"alias":null,"optionField":null},"importNo":{"alias":null,"optionField":null},"insertTime":{"alias":null,"optionField":null},"updateTime":{"alias":null,"optionField":null},"insertManagerId":{"alias":null,"optionField":null},"insertUserName":{"alias":null,"optionField":null},"updateManagerId":{"alias":null,"optionField":null},"insertManagerRealName":{"alias":null,"optionField":null},"updateManagerRealName":{"alias":null,"optionField":null},"updateUserName":{"alias":null,"optionField":null},"copyMethod":{"alias":null,"optionField":null},"sqlMore":{"alias":null,"optionField":null},"authorizeScheme":{"alias":null,"optionField":null},"clearMethod":{"alias":null,"optionField":null},"isRaw":{"alias":null,"optionField":null},"sameEditCode":{"alias":null,"optionField":null},"sqlGrid":{"alias":null,"optionField":null},"dataSrc":{"alias":null,"optionField":null},"mainColumn":{"alias":null,"optionField":null},"batchNewPageName":{"alias":null,"optionField":null},"editTableId":{"alias":null,"optionField":null},"editTableName":{"alias":null,"optionField":null},"editPageId":{"alias":null,"optionField":null},"editPageName":{"alias":null,"optionField":null},"editKeyColumn":{"alias":null,"optionField":null},"buttonVisible":{"alias":null,"optionField":null},"autoSave":{"alias":null,"optionField":null},"childPlace":{"alias":null,"optionField":null},"superColumnName":{"alias":null,"optionField":null},"editPageUrl":{"alias":null,"optionField":null},"displayOption":{"alias":null,"optionField":null},"bpmStepKind":{"alias":null,"optionField":null},"documentReady":{"alias":null,"optionField":null},"help":{"alias":null,"optionField":null},"editWidth":{"alias":null,"optionField":null},"yzProjectName":{"alias":null,"optionField":null},"isPlatform":{"alias":null,"optionField":null},"isSys":{"alias":null,"optionField":null},"isRepeatPwd":{"alias":null,"optionField":null},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId"},"scopeKind":{"alias":null,"optionField":null},"id":{"alias":null,"optionField":null}};
const rtfFields = [];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this._alive = true; this._optionRequests = []; this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  onUnload() { this._alive = false; this._loadRequest?.abort?.(); this._optionRequests.forEach(task => task?.abort?.()); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_table/6191083005411328/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项来自当前页已编译端点，旧身份和父记录的响应不回填。 */
    loadOptions() {
      const parentId = this.parentId || null, identity = this.currentIdentity();
      const current = () => this._alive && parentId === (this.parentId || null) && identity === this.currentIdentity();
      this._optionRequests.push(yzp.request({ url: '/admin/auto/config_table/6191083005411328/findNameIdYzAdminType2200000000000000087', method: 'POST', data: { parentId }, sendSessionId: true, isCurrent: current, success: res => { if (current() && res.code === 200 && Array.isArray(res.data)) this.options.yzAdminTypeId = res.data; } }));
    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页返回标识；路由保持原选择页协议。 */
    selectPage(field, path) { if (writableFields.includes(field)) uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 真实身份快照只用于隔离迟到回调，所有数据权限仍由后端核验。 */
    currentIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]); },
    /** 读取明细只接收服务端页级 VO，离开页面或父记录变化后忽略回调。 */
    load() {
      const id = this.id, parentId = this.parentId, identity = this.currentIdentity();
      const current = () => this._alive && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      this._loadRequest = yzp.request({ url: '/admin/auto/config_table/6191083005411328/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200) { this.entity = res.data; this.changes = {}; } } });
    },
    /** 只记录用户修改且在白名单内的字段，原值不变的回调不会把 null 改为空字符串。 */
    change(field, value) { if (!writableFields.includes(field) || JSON.stringify(this.entity[field]) === JSON.stringify(value)) return; this.entity[field] = value; this.changes[field] = value; },
    /** 消费真实常量展示别名和关系选项，缺失名称时仍显示原 ID。 */
    displayValue(row, field) {
      const definition = displayFields[field];
      if (definition?.alias && row[definition.alias] !== undefined && row[definition.alias] !== null && row[definition.alias] !== '') return row[definition.alias];
      if (definition?.optionField && row[field] !== null && row[field] !== undefined) { const option = (this.options[definition.optionField] || []).find(item => String(item.value ?? item.id) === String(row[field])); if (option && (option.text ?? option.name) !== null && (option.text ?? option.name) !== undefined && (option.text ?? option.name) !== '') return option.text ?? option.name; }
      return row[field] ?? '';
    },
    /** 富文本沿用原编辑/预览页和事件协议；未修改、离开或切换记录的回传不产生写入。 */
    openRtf(field, action) {
      if (!rtfFields.includes(field) || !['edit', 'review'].includes(action) || action === 'edit' && !writableFields.includes(field)) return;
      const original = this.entity[field], text = original ?? '', id = this.id, parentId = this.parentId, identity = this.currentIdentity();
      const current = () => this._alive && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.navigateTo({ url: '/pages/main/rtf/rtf?action=' + action, events: { onReceiveRtf: value => { if (action === 'edit' && current() && typeof value === 'string' && value !== text) this.change(field, value); } }, success: result => { if (current()) result.eventChannel.emit('sendRtf', { data: text }); } });
    },
    /** 新增和修改均只发送元数据允许编辑的字段，保留省略、null、false和零。 */
    save() {
      if (this.saving) return;
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_table/6191083005411328/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_table/6191083005411328?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
