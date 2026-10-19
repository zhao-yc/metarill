<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="列名称" name="configColumnColumnName"><uni-easyinput :modelValue="entity.configColumnColumnName" @update:modelValue="change('configColumnColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="排序号" name="configColumnSortNo"><uni-easyinput :modelValue="entity.configColumnSortNo" @update:modelValue="change('configColumnSortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="着重显示" name="isImportant"><uni-data-select :modelValue="entity.isImportant" @update:modelValue="change('isImportant', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="列" name="configColumnId"><text>{{ entity.configColumnId }}</text><text>此关系控件待移植，当前只读</text></uni-forms-item>
      <uni-forms-item label="视图" name="configPageId"><zxz-uni-data-select filterable :modelValue="entity.configPageId" @update:modelValue="change('configPageId', $event)" :localdata="options.configPageId || []" /></uni-forms-item>
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
      <uni-forms-item label="项目" name="yzProjectId"><text>{{ displayValue(entity, 'yzProjectId') }}</text></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["configColumnColumnName","configColumnSortNo","isImportant","name","configColumnId","configPageId","isInGrid","isInFilter","isInEdit","isReadOnly","width","defaultValue","matchType","isMust","controlDetail","initValue","queryDefaultValue","defaultSaveValue","combineCell","hint","isPrint","isAutoFocus","developerRemarks","description","isAutoSaveAfterChange","configColumnName","sumKind","configPageName","linkConfigTableId","linkConfigTableName","linkConfigPageId","linkConfigPageName","combineSuperColumnId","combineSuperColumnName","importNo"];
const displayFields = {"configColumnColumnName":{"alias":null,"optionField":null},"configColumnSortNo":{"alias":null,"optionField":null},"isImportant":{"alias":null,"optionField":null},"name":{"alias":null,"optionField":null},"configColumnId":{"alias":null,"optionField":null},"configPageId":{"alias":"configPageName","optionField":"configPageId"},"isInGrid":{"alias":null,"optionField":null},"isInFilter":{"alias":null,"optionField":null},"isInEdit":{"alias":null,"optionField":null},"isReadOnly":{"alias":null,"optionField":null},"width":{"alias":null,"optionField":null},"defaultValue":{"alias":null,"optionField":null},"matchType":{"alias":null,"optionField":null},"isMust":{"alias":null,"optionField":null},"controlDetail":{"alias":null,"optionField":null},"initValue":{"alias":null,"optionField":null},"queryDefaultValue":{"alias":null,"optionField":null},"defaultSaveValue":{"alias":null,"optionField":null},"combineCell":{"alias":null,"optionField":null},"hint":{"alias":null,"optionField":null},"isPrint":{"alias":null,"optionField":null},"isAutoFocus":{"alias":null,"optionField":null},"developerRemarks":{"alias":null,"optionField":null},"description":{"alias":null,"optionField":null},"isAutoSaveAfterChange":{"alias":null,"optionField":null},"configColumnName":{"alias":null,"optionField":null},"sumKind":{"alias":"sumKindShow","optionField":null},"configPageName":{"alias":null,"optionField":null},"linkConfigTableId":{"alias":null,"optionField":null},"linkConfigTableName":{"alias":null,"optionField":null},"linkConfigPageId":{"alias":null,"optionField":null},"linkConfigPageName":{"alias":null,"optionField":null},"combineSuperColumnId":{"alias":null,"optionField":null},"combineSuperColumnName":{"alias":null,"optionField":null},"importNo":{"alias":null,"optionField":null},"insertTime":{"alias":null,"optionField":null},"updateTime":{"alias":null,"optionField":null},"insertManagerId":{"alias":null,"optionField":null},"updateManagerId":{"alias":null,"optionField":null},"insertManagerRealName":{"alias":null,"optionField":null},"updateManagerRealName":{"alias":null,"optionField":null},"insertUserName":{"alias":null,"optionField":null},"updateUserName":{"alias":null,"optionField":null},"yzProjectId":{"alias":null,"optionField":null},"id":{"alias":null,"optionField":null}};
const rtfFields = [];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this._alive = true; this._optionRequests = []; this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  onUnload() { this._alive = false; this._loadRequest?.abort?.(); this._optionRequests.forEach(task => task?.abort?.()); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项来自当前页已编译端点，旧身份和父记录的响应不回填。 */
    loadOptions() {
      const parentId = this.parentId || null, identity = this.currentIdentity();
      const current = () => this._alive && parentId === (this.parentId || null) && identity === this.currentIdentity();
      this._optionRequests.push(yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/findNameIdConfigPage1676901024972058625', method: 'POST', data: { parentId }, sendSessionId: true, isCurrent: current, success: res => { if (current() && res.code === 200 && Array.isArray(res.data)) this.options.configPageId = res.data; } }));
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
      this._loadRequest = yzp.request({ url: '/admin/auto/config_page_column/6218036784398336/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
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
