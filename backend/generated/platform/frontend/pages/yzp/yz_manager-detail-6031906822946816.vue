<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page detail-page uni-padding-wrap" :class="{ ispc: $deviceInfo?.is_pc, ismobile: !$deviceInfo?.is_pc }">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="用户名" name="name"><uni-easyinput :modelValue="entity.name" :disabled="readOnlyCondition" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="角色" name="yzRoleIds"><text>{{ entity.yzRoleIds }}</text><text>此控件待移植，当前只读</text></uni-forms-item>
      <uni-forms-item label="真实姓名" name="realName"><uni-easyinput :modelValue="entity.realName" :disabled="readOnlyCondition" @update:modelValue="change('realName', $event)" /></uni-forms-item>
      <uni-forms-item label="手机号" name="mobile"><uni-easyinput :modelValue="entity.mobile" :disabled="readOnlyCondition" @update:modelValue="change('mobile', $event)" /></uni-forms-item>
      <uni-forms-item label="锁定ip" name="lockIp"><uni-easyinput :modelValue="entity.lockIp" :disabled="readOnlyCondition" @update:modelValue="change('lockIp', $event)" /></uni-forms-item>
      <uni-forms-item label="登录时间" name="lastLoginTime"><uni-datetime-picker type="datetime" :modelValue="entity.lastLoginTime" :disabled="readOnlyCondition" @change="change('lastLoginTime', $event)" /></uni-forms-item>
      <uni-forms-item label="登录ip" name="loginIp"><uni-easyinput :modelValue="entity.loginIp" :disabled="readOnlyCondition" @update:modelValue="change('loginIp', $event)" /></uni-forms-item>
      <uni-forms-item label="邮箱" name="email"><uni-easyinput :modelValue="entity.email" :disabled="readOnlyCondition" @update:modelValue="change('email', $event)" /></uni-forms-item>
      <uni-forms-item label="电话" name="phone"><uni-easyinput :modelValue="entity.phone" :disabled="readOnlyCondition" @update:modelValue="change('phone', $event)" /></uni-forms-item>
      <uni-forms-item label="启用" name="isDisabled"><uni-data-select :modelValue="entity.isDisabled" :disabled="readOnlyCondition" @update:modelValue="change('isDisabled', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="备注" name="notes"><uni-easyinput :modelValue="entity.notes" :disabled="readOnlyCondition" @update:modelValue="change('notes', $event)" /></uni-forms-item>
      <uni-forms-item label="已修改密码" name="isEditPwd"><uni-data-select :modelValue="entity.isEditPwd" :disabled="readOnlyCondition" @update:modelValue="change('isEditPwd', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="修改密码时间" name="editPwdTime"><uni-datetime-picker type="datetime" :modelValue="entity.editPwdTime" :disabled="readOnlyCondition" @change="change('editPwdTime', $event)" /></uni-forms-item>
      <uni-forms-item label="openid" name="openid"><uni-easyinput :modelValue="entity.openid" :disabled="readOnlyCondition" @update:modelValue="change('openid', $event)" /></uni-forms-item>
      <uni-forms-item label="头像" name="avatar"><image v-if="entity.avatar" :src="entity.avatar" mode="aspectFit" /><uni-easyinput :modelValue="entity.avatar" :disabled="readOnlyCondition" @update:modelValue="change('avatar', $event)" /></uni-forms-item>
      <uni-forms-item label="钉钉用户ID" name="dingUserid"><uni-easyinput :modelValue="entity.dingUserid" :disabled="readOnlyCondition" @update:modelValue="change('dingUserid', $event)" /></uni-forms-item>
      <uni-forms-item label="钉钉unionid" name="dingUnionid"><uni-easyinput :modelValue="entity.dingUnionid" :disabled="readOnlyCondition" @update:modelValue="change('dingUnionid', $event)" /></uni-forms-item>
      <uni-forms-item label="登录时间起" name="loginRiseTime"><uni-datetime-picker type="datetime" :modelValue="entity.loginRiseTime" :disabled="readOnlyCondition" @change="change('loginRiseTime', $event)" /></uni-forms-item>
      <uni-forms-item label="登录时间止" name="loginStopTime"><uni-datetime-picker type="datetime" :modelValue="entity.loginStopTime" :disabled="readOnlyCondition" @change="change('loginStopTime', $event)" /></uni-forms-item>
      <uni-forms-item label="有效期" name="validityTime"><uni-datetime-picker type="datetime" :modelValue="entity.validityTime" :disabled="readOnlyCondition" @change="change('validityTime', $event)" /></uni-forms-item>
      <uni-forms-item label="注册时间" name="insertTime"><text>{{ displayValue(entity, 'insertTime') }}</text></uni-forms-item>
      <uni-forms-item label="后台类型" name="yzAdminTypeId"><yzp-relation-select :modelValue="entity.yzAdminTypeId" endpoint="/admin/auto/yz_manager/6031906822946816/findNameIdYzAdminType1679698329655750657" :parent-id="parentId" :require-parent="false" :require-target-parent="false" :same-parent="false" :readonly="readOnlyCondition" @update:modelValue="change('yzAdminTypeId', $event)" /></uni-forms-item>
      <uni-forms-item label="项目" name="yzProjectId"><text>{{ displayValue(entity, 'yzProjectId') }}</text></uni-forms-item>
    </uni-forms>
    <button v-if="!readOnlyCondition && (id ? canSave : canCreate)" :disabled="saving" @click="save">保存</button>
    <button v-if="canCopyRecord" :disabled="saving" @click="copy">复制</button>
    <button v-if="canDeleteRecord" :disabled="saving" @click="remove">删除</button>


  </view>
</template>
<script>
import yzp from '@/common/yzp';


const writableFields = ["name","yzRoleIds","realName","mobile","lockIp","lastLoginTime","loginIp","email","phone","isDisabled","notes","isEditPwd","editPwdTime","openid","avatar","dingUserid","dingUnionid","loginRiseTime","loginStopTime","validityTime","yzAdminTypeId"];
const displayFields = {"name":{"alias":null,"optionField":null,"multiNames":false},"yzRoleIds":{"alias":null,"optionField":null,"multiNames":false},"realName":{"alias":null,"optionField":null,"multiNames":false},"mobile":{"alias":null,"optionField":null,"multiNames":false},"lockIp":{"alias":null,"optionField":null,"multiNames":false},"lastLoginTime":{"alias":null,"optionField":null,"multiNames":false},"loginIp":{"alias":null,"optionField":null,"multiNames":false},"email":{"alias":null,"optionField":null,"multiNames":false},"phone":{"alias":null,"optionField":null,"multiNames":false},"isDisabled":{"alias":null,"optionField":null,"multiNames":false},"notes":{"alias":null,"optionField":null,"multiNames":false},"isEditPwd":{"alias":null,"optionField":null,"multiNames":false},"editPwdTime":{"alias":null,"optionField":null,"multiNames":false},"openid":{"alias":null,"optionField":null,"multiNames":false},"avatar":{"alias":null,"optionField":null,"multiNames":false},"dingUserid":{"alias":null,"optionField":null,"multiNames":false},"dingUnionid":{"alias":null,"optionField":null,"multiNames":false},"loginRiseTime":{"alias":null,"optionField":null,"multiNames":false},"loginStopTime":{"alias":null,"optionField":null,"multiNames":false},"validityTime":{"alias":null,"optionField":null,"multiNames":false},"insertTime":{"alias":null,"optionField":null,"multiNames":false},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId","multiNames":false},"updateTime":{"alias":null,"optionField":null,"multiNames":false},"insertManagerId":{"alias":null,"optionField":null,"multiNames":false},"updateManagerId":{"alias":null,"optionField":null,"multiNames":false},"insertUserName":{"alias":null,"optionField":null,"multiNames":false},"updateUserName":{"alias":null,"optionField":null,"multiNames":false},"insertManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"updateManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"yzProjectId":{"alias":null,"optionField":null,"multiNames":false},"id":{"alias":null,"optionField":null,"multiNames":false}};
const rtfFields = [];
const children = [];
const readonlyField = null;
/** 空父键不接受历史 undefined/null 字面量，字符串 ID 全程不转为浮点。 */
const recordId = value => typeof value === 'string' && !['undefined', 'null'].includes(value.trim()) ? value.trim() : '';
export default {
  mixins: [],
  components: {  },
  props: { is_props: { type: Boolean, default: false }, props_id: { type: String, default: '' }, props_parent_id: { type: String, default: '' }, props_parent_page_sn: { type: String, default: '' }, parentReadOnlyCondition: { type: Boolean, default: false } },
  data() { return { id: '', parentId: '', parentPageId: '', routeReadOnly: false, recordReady: false, permissionsReady: false, permissionVersion: 0, currentChild: 0, children, canUpdate: true, entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: false }; },
  computed: {
    /** 子表继承父只读后仍检查自身当前操作权限；共享快照不代表继承操作授权。 */
    readOnlyCondition() { this.permissionVersion; return !this.permissionsReady || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.id ? !this.canUpdate : !this.canCreate) || readonlyField && this.entity[readonlyField]) || !yzp.hasPagePermission('6031906822946816', this.id ? 'is_update' : 'is_new'); },
    /** 复制和删除分别检查自身操作授权，修改授权不会隐式开放其他动作。 */
    canCopyRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canCopy && yzp.hasPagePermission('6031906822946816', 'is_all'); },
    canDeleteRecord() { this.permissionVersion; return !this.readOnlyCondition && Boolean(this.id) && this.canDelete && yzp.hasPagePermission('6031906822946816', 'is_delete'); },
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
      if (this.readOnlyCondition || !this.canCopy || !yzp.hasPagePermission('6031906822946816', 'is_all') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('6031906822946816', 'is_all') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/copy/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'POST', sendSessionId: true, isCurrent: current,
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
      this._loadRequest = yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
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
      if (this.saving || this.readOnlyCondition || !(this.id ? this.canSave : this.canCreate) || false && !this.parentId) return;
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      const id = this.id, parentId = this.parentId, identity = this.currentIdentity(), epoch = this._epoch, hadChildren = this.childrenReady;
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      this.saving = true;
      this._writeRequest = yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true, isCurrent: current,
        success: res => { if (current() && res.code === 200 && recordId(res.data?.id)) { this.id = res.data.id; this.entity = res.data; this.changes = {}; this.recordReady = true; this.emitDetailChange('save', res.data, id ? 'update' : 'add'); if (this.is_props) this.closeDetail(); else if (hadChildren) this.$nextTick(() => this.refreshSonsData()); uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
    },
    /** 删除仅通知当前列表，子表刷新不会写入或重载父实体。 */
    remove() {
      if (this.readOnlyCondition || !this.canDelete || !yzp.hasPagePermission('6031906822946816', 'is_delete') || !this.id || this.saving) return;
      const id = this.id, parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && id === this.id && parentId === this.parentId && identity === this.currentIdentity();
      uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm || !current() || this.readOnlyCondition || !yzp.hasPagePermission('6031906822946816', 'is_delete') || this.saving) return; this.saving = true;
        this._writeRequest = yzp.request({ url: '/admin/auto/yz_manager/6031906822946816?parentId=' + encodeURIComponent(parentId), method: 'DELETE', data: { id }, sendSessionId: true, isCurrent: current,
          success: res => { if (current() && res.code === 200) { this.recordReady = false; this.emitDetailChange('delete'); this.closeDetail(); } }, complete: () => { if (this._alive && epoch === this._epoch) this.saving = false; } });
      } });
    }
  }
};
</script>
