<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库连接字符串" name="mysqlConnStr"><uni-easyinput :modelValue="entity.mysqlConnStr" @update:modelValue="change('mysqlConnStr', $event)" /></uni-forms-item>
      <uni-forms-item label="缩略名" name="shortName"><uni-easyinput :modelValue="entity.shortName" @update:modelValue="change('shortName', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库名称" name="dbName"><uni-easyinput :modelValue="entity.dbName" @update:modelValue="change('dbName', $event)" /></uni-forms-item>
      <uni-forms-item label="api路径" name="contextRoot"><uni-easyinput :modelValue="entity.contextRoot" @update:modelValue="change('contextRoot', $event)" /></uni-forms-item>
      <uni-forms-item label="x5adminName" name="x5AdminName"><uni-easyinput :modelValue="entity.x5AdminName" @update:modelValue="change('x5AdminName', $event)" /></uni-forms-item>
      <uni-forms-item label="java项目包名" name="eclipseBaseName"><uni-easyinput :modelValue="entity.eclipseBaseName" @update:modelValue="change('eclipseBaseName', $event)" /></uni-forms-item>
      <uni-forms-item label="yzp版本" name="yzYzpVersionId"><text>{{ entity.yzYzpVersionId }}</text><text>此控件待移植，当前只读</text></uni-forms-item>
      <uni-forms-item label="数据库类型" name="dbType"><uni-data-select :modelValue="entity.dbType" @update:modelValue="change('dbType', $event)" :localdata="[{&quot;text&quot;:&quot;MicroSoftSQL&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;MySQL&quot;,&quot;value&quot;:2},{&quot;text&quot;:&quot;ORACLE&quot;,&quot;value&quot;:3}]" /></uni-forms-item>
      <uni-forms-item label="隐藏" name="isHide"><uni-easyinput :modelValue="entity.isHide" @update:modelValue="change('isHide', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库连接" name="dbUrl"><uni-easyinput :modelValue="entity.dbUrl" @update:modelValue="change('dbUrl', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库账号" name="dbUsername"><uni-easyinput :modelValue="entity.dbUsername" @update:modelValue="change('dbUsername', $event)" /></uni-forms-item>
      <uni-forms-item label="PrimaryColor" name="primaryColor"><uni-easyinput :modelValue="entity.primaryColor" @update:modelValue="change('primaryColor', $event)" /></uni-forms-item>
      <uni-forms-item label="简单项目" name="isSmall"><uni-data-select :modelValue="entity.isSmall" @update:modelValue="change('isSmall', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="项目" name="yzProjectId"><text>{{ entity.yzProjectId }}</text></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","mysqlConnStr","shortName","dbName","contextRoot","x5AdminName","eclipseBaseName","yzYzpVersionId","dbType","isHide","dbUrl","dbUsername","primaryColor","isSmall"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: true }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/yz_project/6186303424888832/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {

    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/yz_project/6186303424888832/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/yz_project/6186303424888832/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/yz_project/6186303424888832?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
