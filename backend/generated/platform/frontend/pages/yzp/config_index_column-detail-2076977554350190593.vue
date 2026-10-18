<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="所属索引" name="configIndexId"><uni-data-select :modelValue="entity.configIndexId" @update:modelValue="change('configIndexId', $event)" :localdata="options.configIndexId || []" /></uni-forms-item>
      <uni-forms-item label="配置字段" name="configColumnId"><text>{{ entity.configColumnId }}</text><text>此控件待移植，当前只读</text></uni-forms-item>
      <uni-forms-item label="物理列名" name="columnName"><uni-easyinput :modelValue="entity.columnName" @update:modelValue="change('columnName', $event)" /></uni-forms-item>
      <uni-forms-item label="字段顺序" name="sortNo"><uni-easyinput :modelValue="entity.sortNo" @update:modelValue="change('sortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="排序方向" name="sortDirection"><uni-data-select :modelValue="entity.sortDirection" @update:modelValue="change('sortDirection', $event)" :localdata="[{&quot;text&quot;:&quot;\u5347\u5E8F&quot;,&quot;value&quot;:&quot;ASC&quot;},{&quot;text&quot;:&quot;\u964D\u5E8F&quot;,&quot;value&quot;:&quot;DESC&quot;}]" /></uni-forms-item>
      <uni-forms-item label="config_index_index_name" name="configIndexIndexName"><uni-easyinput :modelValue="entity.configIndexIndexName" @update:modelValue="change('configIndexIndexName', $event)" /></uni-forms-item>
      <uni-forms-item label="config_column_name" name="configColumnName"><uni-easyinput :modelValue="entity.configColumnName" @update:modelValue="change('configColumnName', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ entity.yzProjectName }}</text></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["configIndexId","configColumnId","columnName","sortNo","sortDirection","configIndexIndexName","configColumnName","importNo"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: false, canDelete: true, canCopy: false }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_index_column/2076977554350190593/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {
      yzp.request({ url: '/admin/auto/config_index_column/2076977554350190593/findNameIdConfigIndex2076977805266128896', method: 'POST', data: { parentId: this.parentId || null }, sendSessionId: true, success: res => { if (res.code === 200) this.options.configIndexId = res.data; } });
    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/config_index_column/2076977554350190593/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_index_column/2076977554350190593/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_index_column/2076977554350190593?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
