<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="父菜单" name="parentMenuId"><uni-easyinput :modelValue="entity.parentMenuId" @update:modelValue="change('parentMenuId', $event)" /></uni-forms-item>
      <uni-forms-item label="项目" name="yzProjectId"><text>{{ entity.yzProjectId }}</text></uni-forms-item>
      <uni-forms-item label="所属后台" name="yzAdminTypeId"><uni-easyinput :modelValue="entity.yzAdminTypeId" @update:modelValue="change('yzAdminTypeId', $event)" /></uni-forms-item>
      <uni-forms-item label="菜单类型" name="kind"><uni-easyinput :modelValue="entity.kind" @update:modelValue="change('kind', $event)" /></uni-forms-item>
      <uni-forms-item label="目标页面" name="configPageId"><uni-easyinput :modelValue="entity.configPageId" @update:modelValue="change('configPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="自定义页面" name="configCustomPageId"><uni-easyinput :modelValue="entity.configCustomPageId" @update:modelValue="change('configCustomPageId', $event)" /></uni-forms-item>
      <uni-forms-item label="页面地址" name="navigateUrl"><uni-easyinput :modelValue="entity.navigateUrl" @update:modelValue="change('navigateUrl', $event)" /></uni-forms-item>
      <uni-forms-item label="图标" name="iconName"><uni-easyinput :modelValue="entity.iconName" @update:modelValue="change('iconName', $event)" /></uni-forms-item>
      <uni-forms-item label="排序" name="sortNo"><uni-easyinput :modelValue="entity.sortNo" @update:modelValue="change('sortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="隐藏" name="isHidden"><uni-easyinput :modelValue="entity.isHidden" @update:modelValue="change('isHidden', $event)" /></uni-forms-item>
      <uni-forms-item label="数量角标" name="isShowCount"><uni-easyinput :modelValue="entity.isShowCount" @update:modelValue="change('isShowCount', $event)" /></uni-forms-item>
      <uni-forms-item label="可见方案" name="visibleScheme"><uni-easyinput :modelValue="entity.visibleScheme" @update:modelValue="change('visibleScheme', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","parentMenuId","yzAdminTypeId","kind","configPageId","configCustomPageId","navigateUrl","iconName","sortNo","isHidden","isShowCount","visibleScheme"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: false, canDelete: false, canCopy: false }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_menu/2200000000000099000/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {

    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/config_menu/2200000000000099000/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_menu/2200000000000099000/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_menu/2200000000000099000?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
