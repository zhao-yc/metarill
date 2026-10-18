<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="父页面" name="parentConfigPageId"><button size="mini" @click="selectPage('parentConfigPageId', '/pages/yzp/config_page-list-1869016706289577986')">选择</button><text>{{ entity.parentConfigPageId }}</text></uni-forms-item>
      <uni-forms-item label="子页面" name="childConfigPageId"><button size="mini" @click="selectPage('childConfigPageId', '/pages/yzp/config_page-list-1869016706289577986')">选择</button><text>{{ entity.childConfigPageId }}</text></uni-forms-item>
      <uni-forms-item label="排序号" name="sortNo"><uni-easyinput :modelValue="entity.sortNo" @update:modelValue="change('sortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="PC端子页面打开方式" name="childPageOpenType"><uni-data-select :modelValue="entity.childPageOpenType" @update:modelValue="change('childPageOpenType', $event)" :localdata="[{&quot;text&quot;:&quot;\u65B0\u9875\u9762&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;\u5F39\u51FA\u5C42&quot;,&quot;value&quot;:2}]" /></uni-forms-item>
      <uni-forms-item label="parent_config_page_name" name="parentConfigPageName"><uni-easyinput :modelValue="entity.parentConfigPageName" @update:modelValue="change('parentConfigPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="child_config_page_name" name="childConfigPageName"><uni-easyinput :modelValue="entity.childConfigPageName" @update:modelValue="change('childConfigPageName', $event)" /></uni-forms-item>
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
const writableFields = ["parentConfigPageId","childConfigPageId","sortNo","childPageOpenType","parentConfigPageName","childConfigPageName","importNo"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_child/1679390851298631682/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {

    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/config_child/1679390851298631682/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_child/1679390851298631682/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_child/1679390851298631682?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
