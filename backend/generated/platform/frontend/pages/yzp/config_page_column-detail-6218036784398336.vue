<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="列名称" name="configColumnColumnName"><uni-easyinput :modelValue="entity.configColumnColumnName" @update:modelValue="change('configColumnColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="排序号" name="configColumnSortNo"><uni-easyinput :modelValue="entity.configColumnSortNo" @update:modelValue="change('configColumnSortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="着重显示" name="isImportant"><uni-data-select :modelValue="entity.isImportant" @update:modelValue="change('isImportant', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="列" name="configColumnId"><text>{{ entity.configColumnId }}</text><text>此控件待移植，当前只读</text></uni-forms-item>
      <uni-forms-item label="视图" name="configPageId"><uni-data-select :modelValue="entity.configPageId" @update:modelValue="change('configPageId', $event)" :localdata="options.configPageId || []" /></uni-forms-item>
      <uni-forms-item label="is_in_grid" name="isInGrid"><uni-data-select :modelValue="entity.isInGrid" @update:modelValue="change('isInGrid', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_in_filter" name="isInFilter"><uni-data-select :modelValue="entity.isInFilter" @update:modelValue="change('isInFilter', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="is_in_edit" name="isInEdit"><uni-data-select :modelValue="entity.isInEdit" @update:modelValue="change('isInEdit', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="只读" name="isReadOnly"><uni-data-select :modelValue="entity.isReadOnly" @update:modelValue="change('isReadOnly', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="width" name="width"><uni-easyinput :modelValue="entity.width" @update:modelValue="change('width', $event)" /></uni-forms-item>
      <uni-forms-item label="默认值" name="defaultValue"><uni-easyinput :modelValue="entity.defaultValue" @update:modelValue="change('defaultValue', $event)" /></uni-forms-item>
      <uni-forms-item label="匹配类型" name="matchType"><uni-easyinput :modelValue="entity.matchType" @update:modelValue="change('matchType', $event)" /></uni-forms-item>
      <uni-forms-item label="必填" name="isMust"><uni-data-select :modelValue="entity.isMust" @update:modelValue="change('isMust', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="控件明细" name="controlDetail"><yzp-control-detail-editor :entity="entity" :record-id="id" :readonly="false" @receive="applyCustom" /></uni-forms-item>
      <uni-forms-item label="新建初始值" name="initValue"><uni-easyinput :modelValue="entity.initValue" @update:modelValue="change('initValue', $event)" /></uni-forms-item>
      <uni-forms-item label="query_default_value" name="queryDefaultValue"><uni-easyinput :modelValue="entity.queryDefaultValue" @update:modelValue="change('queryDefaultValue', $event)" /></uni-forms-item>
      <uni-forms-item label="default_save_value" name="defaultSaveValue"><uni-easyinput :modelValue="entity.defaultSaveValue" @update:modelValue="change('defaultSaveValue', $event)" /></uni-forms-item>
      <uni-forms-item label="combine_cell" name="combineCell"><uni-easyinput :modelValue="entity.combineCell" @update:modelValue="change('combineCell', $event)" /></uni-forms-item>
      <uni-forms-item label="hint" name="hint"><uni-easyinput :modelValue="entity.hint" @update:modelValue="change('hint', $event)" /></uni-forms-item>
      <uni-forms-item label="is_print" name="isPrint"><uni-data-select :modelValue="entity.isPrint" @update:modelValue="change('isPrint', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="筛选框自动焦点" name="isAutoFocus"><uni-data-select :modelValue="entity.isAutoFocus" @update:modelValue="change('isAutoFocus', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="开发者备注" name="developerRemarks"><uni-easyinput :modelValue="entity.developerRemarks" @update:modelValue="change('developerRemarks', $event)" /></uni-forms-item>
      <uni-forms-item label="填写提示" name="description"><uni-easyinput :modelValue="entity.description" @update:modelValue="change('description', $event)" /></uni-forms-item>
      <uni-forms-item label="修改后自动保存" name="isAutoSaveAfterChange"><uni-data-select :modelValue="entity.isAutoSaveAfterChange" @update:modelValue="change('isAutoSaveAfterChange', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="config_column_name" name="configColumnName"><uni-easyinput :modelValue="entity.configColumnName" @update:modelValue="change('configColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="统计类型" name="sumKind"><uni-data-select :modelValue="entity.sumKind" @update:modelValue="change('sumKind', $event)" :localdata="[{&quot;text&quot;:&quot;\u4E0D\u7EDF\u8BA1&quot;,&quot;value&quot;:0},{&quot;text&quot;:&quot;\u5408\u8BA1&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;\u6982\u7387&quot;,&quot;value&quot;:2},{&quot;text&quot;:&quot;\u5E73\u5747\u6570&quot;,&quot;value&quot;:3}]" /></uni-forms-item>
      <uni-forms-item label="config_page_name" name="configPageName"><uni-easyinput :modelValue="entity.configPageName" @update:modelValue="change('configPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="link_config_table_id" name="linkConfigTableId"><uni-easyinput :modelValue="entity.linkConfigTableId" @update:modelValue="change('linkConfigTableId', $event)" /></uni-forms-item>
      <uni-forms-item label="link_config_table_name" name="linkConfigTableName"><uni-easyinput :modelValue="entity.linkConfigTableName" @update:modelValue="change('linkConfigTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="link_config_page_id" name="linkConfigPageId"><uni-easyinput :modelValue="entity.linkConfigPageId" @update:modelValue="change('linkConfigPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="link_config_page_name" name="linkConfigPageName"><uni-easyinput :modelValue="entity.linkConfigPageName" @update:modelValue="change('linkConfigPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="combine_super_column_id" name="combineSuperColumnId"><uni-easyinput :modelValue="entity.combineSuperColumnId" @update:modelValue="change('combineSuperColumnId', $event)" /></uni-forms-item>
      <uni-forms-item label="combine_super_column_name" name="combineSuperColumnName"><uni-easyinput :modelValue="entity.combineSuperColumnName" @update:modelValue="change('combineSuperColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="项目" name="yzProjectId"><text>{{ entity.yzProjectId }}</text></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["configColumnColumnName","configColumnSortNo","isImportant","name","configColumnId","configPageId","isInGrid","isInFilter","isInEdit","isReadOnly","width","defaultValue","matchType","isMust","controlDetail","initValue","queryDefaultValue","defaultSaveValue","combineCell","hint","isPrint","isAutoFocus","developerRemarks","description","isAutoSaveAfterChange","configColumnName","sumKind","configPageName","linkConfigTableId","linkConfigTableName","linkConfigPageId","linkConfigPageName","combineSuperColumnId","combineSuperColumnName","importNo"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {
      yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/findNameIdConfigPage1676901024972058625', method: 'POST', data: { parentId: this.parentId || null }, sendSessionId: true, success: res => { if (res.code === 200) this.options.configPageId = res.data; } });
    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_page_column/6218036784398336?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
