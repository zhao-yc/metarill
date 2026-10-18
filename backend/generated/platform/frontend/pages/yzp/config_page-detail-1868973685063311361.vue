<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="表名" name="configTableTableName"><uni-easyinput :modelValue="entity.configTableTableName" @update:modelValue="change('configTableTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="order_by" name="orderBy"><uni-easyinput :modelValue="entity.orderBy" @update:modelValue="change('orderBy', $event)" /></uni-forms-item>
      <uni-forms-item label="Where" name="whereSql"><uni-easyinput type="textarea" :modelValue="entity.whereSql" @update:modelValue="change('whereSql', $event)" /></uni-forms-item>
      <uni-forms-item label="精确数据管理匹配字段" name="exactDataManageMatchField"><uni-easyinput :modelValue="entity.exactDataManageMatchField" @update:modelValue="change('exactDataManageMatchField', $event)" /></uni-forms-item>
      <uni-forms-item label="page_size" name="pageSize"><uni-easyinput :modelValue="entity.pageSize" @update:modelValue="change('pageSize', $event)" /></uni-forms-item>
      <uni-forms-item label="is_edit" name="isEdit"><uni-data-select :modelValue="entity.isEdit" @update:modelValue="change('isEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_new" name="isNew"><uni-data-select :modelValue="entity.isNew" @update:modelValue="change('isNew', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_update" name="isUpdate"><uni-data-select :modelValue="entity.isUpdate" @update:modelValue="change('isUpdate', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_delete" name="isDelete"><uni-data-select :modelValue="entity.isDelete" @update:modelValue="change('isDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_search" name="isSearch"><uni-data-select :modelValue="entity.isSearch" @update:modelValue="change('isSearch', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_sum" name="isSum"><uni-data-select :modelValue="entity.isSum" @update:modelValue="change('isSum', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_batch_delete" name="isBatchDelete"><uni-data-select :modelValue="entity.isBatchDelete" @update:modelValue="change('isBatchDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_import" name="isImport"><uni-data-select :modelValue="entity.isImport" @update:modelValue="change('isImport', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_export" name="isExport"><uni-data-select :modelValue="entity.isExport" @update:modelValue="change('isExport', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_multi_select" name="isMultiSelect"><uni-data-select :modelValue="entity.isMultiSelect" @update:modelValue="change('isMultiSelect', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_export_edit" name="isExportEdit"><uni-data-select :modelValue="entity.isExportEdit" @update:modelValue="change('isExportEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="export_method" name="exportMethod"><uni-easyinput :modelValue="entity.exportMethod" @update:modelValue="change('exportMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="复制方法" name="isCopy"><uni-data-select :modelValue="entity.isCopy" @update:modelValue="change('isCopy', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="read_only_condition" name="readOnlyCondition"><uni-easyinput :modelValue="entity.readOnlyCondition" @update:modelValue="change('readOnlyCondition', $event)" /></uni-forms-item>
      <uni-forms-item label="import_method" name="importMethod"><uni-easyinput :modelValue="entity.importMethod" @update:modelValue="change('importMethod', $event)" /></uni-forms-item>
      <uni-forms-item label="button_visible" name="buttonVisible"><uni-easyinput :modelValue="entity.buttonVisible" @update:modelValue="change('buttonVisible', $event)" /></uni-forms-item>
      <uni-forms-item label="is_after_add" name="isAfterAdd"><uni-data-select :modelValue="entity.isAfterAdd" @update:modelValue="change('isAfterAdd', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_after_save" name="isAfterSave"><uni-data-select :modelValue="entity.isAfterSave" @update:modelValue="change('isAfterSave', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_before_save" name="isBeforeSave"><uni-data-select :modelValue="entity.isBeforeSave" @update:modelValue="change('isBeforeSave', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="子模块位置" name="childPlace"><uni-data-select :modelValue="entity.childPlace" @update:modelValue="change('childPlace', $event)" :localdata="[{&quot;text&quot;:&quot;\u4E00\u62D6N&quot;,&quot;value&quot;:&quot;OneWithMany&quot;},{&quot;text&quot;:&quot;\u6807\u7B7E\u663E\u793A&quot;,&quot;value&quot;:&quot;Tab&quot;},{&quot;text&quot;:&quot;\u65B0\u9875\u9762\u6253\u5F00&quot;,&quot;value&quot;:&quot;NewPage&quot;}]" /></uni-forms-item>
      <uni-forms-item label="is_after_delete" name="isAfterDelete"><uni-data-select :modelValue="entity.isAfterDelete" @update:modelValue="change('isAfterDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_repeat_pwd" name="isRepeatPwd"><uni-data-select :modelValue="entity.isRepeatPwd" @update:modelValue="change('isRepeatPwd', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="save_auto_close" name="saveAutoClose"><uni-easyinput :modelValue="entity.saveAutoClose" @update:modelValue="change('saveAutoClose', $event)" /></uni-forms-item>
      <uni-forms-item label="child_style" name="childStyle"><uni-easyinput :modelValue="entity.childStyle" @update:modelValue="change('childStyle', $event)" /></uni-forms-item>
      <uni-forms-item label="is_before_delete" name="isBeforeDelete"><uni-data-select :modelValue="entity.isBeforeDelete" @update:modelValue="change('isBeforeDelete', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="config_table_name" name="configTableName"><uni-easyinput :modelValue="entity.configTableName" @update:modelValue="change('configTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="自动隐藏" name="isAutoHide"><uni-data-select :modelValue="entity.isAutoHide" @update:modelValue="change('isAutoHide', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="列表页编辑" name="isListEdit"><uni-data-select :modelValue="entity.isListEdit" @update:modelValue="change('isListEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_table_id" name="parentConfigTableId"><uni-easyinput :modelValue="entity.parentConfigTableId" @update:modelValue="change('parentConfigTableId', $event)" /></uni-forms-item>
      <uni-forms-item label="parent_config_table_name" name="parentConfigTableName"><uni-easyinput :modelValue="entity.parentConfigTableName" @update:modelValue="change('parentConfigTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="页面类型" name="type"><uni-data-select :modelValue="entity.type" @update:modelValue="change('type', $event)" :localdata="[{&quot;text&quot;:&quot;\u9ED8\u8BA4&quot;,&quot;value&quot;:0},{&quot;text&quot;:&quot;\u9009\u62E9\u9875&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;\u591A\u9009\u9875&quot;,&quot;value&quot;:2}]" /></uni-forms-item>
      <uni-forms-item label="列表页查询前" name="isBeforeQuery"><uni-data-select :modelValue="entity.isBeforeQuery" @update:modelValue="change('isBeforeQuery', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_page_id" name="parentConfigPageId"><uni-easyinput :modelValue="entity.parentConfigPageId" @update:modelValue="change('parentConfigPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="详情页查询前" name="isBeforeGet"><uni-data-select :modelValue="entity.isBeforeGet" @update:modelValue="change('isBeforeGet', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_page_name" name="parentConfigPageName"><uni-easyinput :modelValue="entity.parentConfigPageName" @update:modelValue="change('parentConfigPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="不生成详情页" name="isNotGenerateDetailPage"><uni-data-select :modelValue="entity.isNotGenerateDetailPage" @update:modelValue="change('isNotGenerateDetailPage', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ entity.yzProjectName }}</text></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="移动端列表页样式" name="mobileListPageStyleType"><uni-data-select :modelValue="entity.mobileListPageStyleType" @update:modelValue="change('mobileListPageStyleType', $event)" :localdata="[{&quot;text&quot;:&quot;\u4F20\u7EDF\u6837\u5F0F&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;Grid\u6837\u5F0F&quot;,&quot;value&quot;:2},{&quot;text&quot;:&quot;Grid\u5E26\u5934\u50CF&quot;,&quot;value&quot;:3}]" /></uni-forms-item>
      <uni-forms-item label="移动端详情页保存后自动返回列表页" name="isAutoBackOnMobileSave"><uni-data-select :modelValue="entity.isAutoBackOnMobileSave" @update:modelValue="change('isAutoBackOnMobileSave', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="PC详情页每行列数" name="cols"><uni-easyinput :modelValue="entity.cols" @update:modelValue="change('cols', $event)" /></uni-forms-item>
      <uni-forms-item label="仅更新编辑页字段" name="isUpdateEditColumnsOnly"><uni-data-select :modelValue="entity.isUpdateEditColumnsOnly" @update:modelValue="change('isUpdateEditColumnsOnly', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="详情页按钮位置" name="detailPageButtonsPosition"><uni-data-select :modelValue="entity.detailPageButtonsPosition" @update:modelValue="change('detailPageButtonsPosition', $event)" :localdata="[{&quot;text&quot;:&quot;\u56FA\u5B9A\u5E95\u90E8&quot;,&quot;value&quot;:0},{&quot;text&quot;:&quot;\u5B50\u8868\u4E0A\u9762&quot;,&quot;value&quot;:1}]" /></uni-forms-item>
      <uni-forms-item label="所属后台" name="yzAdminTypeId"><uni-data-select :modelValue="entity.yzAdminTypeId" @update:modelValue="change('yzAdminTypeId', $event)" :localdata="options.yzAdminTypeId || []" /></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","configTableTableName","orderBy","whereSql","exactDataManageMatchField","pageSize","isEdit","isNew","isUpdate","isDelete","isSearch","isSum","isBatchDelete","isImport","isExport","isMultiSelect","isExportEdit","exportMethod","isCopy","readOnlyCondition","importMethod","buttonVisible","isAfterAdd","isAfterSave","isBeforeSave","childPlace","isAfterDelete","isRepeatPwd","saveAutoClose","childStyle","isBeforeDelete","configTableName","isAutoHide","isListEdit","parentConfigTableId","parentConfigTableName","type","isBeforeQuery","parentConfigPageId","isBeforeGet","parentConfigPageName","isNotGenerateDetailPage","importNo","mobileListPageStyleType","isAutoBackOnMobileSave","cols","isUpdateEditColumnsOnly","detailPageButtonsPosition","yzAdminTypeId"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_page/1868973685063311361/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {
      yzp.request({ url: '/admin/auto/config_page/1868973685063311361/findNameIdYzAdminType2200000000000000105', method: 'POST', data: { parentId: this.parentId || null }, sendSessionId: true, success: res => { if (res.code === 200) this.options.yzAdminTypeId = res.data; } });
    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/config_page/1868973685063311361/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_page/1868973685063311361/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_page/1868973685063311361?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
