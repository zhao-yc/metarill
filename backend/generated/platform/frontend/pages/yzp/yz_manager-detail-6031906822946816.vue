<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="用户名" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="角色" name="yzRoleIds"><text>{{ entity.yzRoleIds }}</text><text>此控件待移植，当前只读</text></uni-forms-item>
      <uni-forms-item label="真实姓名" name="realName"><uni-easyinput :modelValue="entity.realName" @update:modelValue="change('realName', $event)" /></uni-forms-item>
      <uni-forms-item label="手机号" name="mobile"><uni-easyinput :modelValue="entity.mobile" @update:modelValue="change('mobile', $event)" /></uni-forms-item>
      <uni-forms-item label="锁定ip" name="lockIp"><uni-easyinput :modelValue="entity.lockIp" @update:modelValue="change('lockIp', $event)" /></uni-forms-item>
      <uni-forms-item label="登录时间" name="lastLoginTime"><uni-datetime-picker type="datetime" :modelValue="entity.lastLoginTime" @change="change('lastLoginTime', $event)" /></uni-forms-item>
      <uni-forms-item label="登录ip" name="loginIp"><uni-easyinput :modelValue="entity.loginIp" @update:modelValue="change('loginIp', $event)" /></uni-forms-item>
      <uni-forms-item label="邮箱" name="email"><uni-easyinput :modelValue="entity.email" @update:modelValue="change('email', $event)" /></uni-forms-item>
      <uni-forms-item label="电话" name="phone"><uni-easyinput :modelValue="entity.phone" @update:modelValue="change('phone', $event)" /></uni-forms-item>
      <uni-forms-item label="启用" name="isDisabled"><uni-data-select :modelValue="entity.isDisabled" @update:modelValue="change('isDisabled', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="备注" name="notes"><uni-easyinput :modelValue="entity.notes" @update:modelValue="change('notes', $event)" /></uni-forms-item>
      <uni-forms-item label="已修改密码" name="isEditPwd"><uni-data-select :modelValue="entity.isEditPwd" @update:modelValue="change('isEditPwd', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="修改密码时间" name="editPwdTime"><uni-datetime-picker type="datetime" :modelValue="entity.editPwdTime" @change="change('editPwdTime', $event)" /></uni-forms-item>
      <uni-forms-item label="openid" name="openid"><uni-easyinput :modelValue="entity.openid" @update:modelValue="change('openid', $event)" /></uni-forms-item>
      <uni-forms-item label="头像" name="avatar"><image v-if="entity.avatar" :src="entity.avatar" mode="aspectFit" /><uni-easyinput :modelValue="entity.avatar" @update:modelValue="change('avatar', $event)" /></uni-forms-item>
      <uni-forms-item label="钉钉用户ID" name="dingUserid"><uni-easyinput :modelValue="entity.dingUserid" @update:modelValue="change('dingUserid', $event)" /></uni-forms-item>
      <uni-forms-item label="钉钉unionid" name="dingUnionid"><uni-easyinput :modelValue="entity.dingUnionid" @update:modelValue="change('dingUnionid', $event)" /></uni-forms-item>
      <uni-forms-item label="登录时间起" name="loginRiseTime"><uni-datetime-picker type="datetime" :modelValue="entity.loginRiseTime" @change="change('loginRiseTime', $event)" /></uni-forms-item>
      <uni-forms-item label="登录时间止" name="loginStopTime"><uni-datetime-picker type="datetime" :modelValue="entity.loginStopTime" @change="change('loginStopTime', $event)" /></uni-forms-item>
      <uni-forms-item label="有效期" name="validityTime"><uni-datetime-picker type="datetime" :modelValue="entity.validityTime" @change="change('validityTime', $event)" /></uni-forms-item>
      <uni-forms-item label="注册时间" name="insertTime"><text>{{ displayValue(entity, 'insertTime') }}</text></uni-forms-item>
      <uni-forms-item label="后台类型" name="yzAdminTypeId"><zxz-uni-data-select filterable :modelValue="entity.yzAdminTypeId" @update:modelValue="change('yzAdminTypeId', $event)" :localdata="options.yzAdminTypeId || []" /></uni-forms-item>
      <uni-forms-item label="项目" name="yzProjectId"><text>{{ displayValue(entity, 'yzProjectId') }}</text></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","yzRoleIds","realName","mobile","lockIp","lastLoginTime","loginIp","email","phone","isDisabled","notes","isEditPwd","editPwdTime","openid","avatar","dingUserid","dingUnionid","loginRiseTime","loginStopTime","validityTime","yzAdminTypeId"];
const displayFields = {"name":{"alias":null,"optionField":null},"yzRoleIds":{"alias":null,"optionField":null},"realName":{"alias":null,"optionField":null},"mobile":{"alias":null,"optionField":null},"lockIp":{"alias":null,"optionField":null},"lastLoginTime":{"alias":null,"optionField":null},"loginIp":{"alias":null,"optionField":null},"email":{"alias":null,"optionField":null},"phone":{"alias":null,"optionField":null},"isDisabled":{"alias":null,"optionField":null},"notes":{"alias":null,"optionField":null},"isEditPwd":{"alias":null,"optionField":null},"editPwdTime":{"alias":null,"optionField":null},"openid":{"alias":null,"optionField":null},"avatar":{"alias":null,"optionField":null},"dingUserid":{"alias":null,"optionField":null},"dingUnionid":{"alias":null,"optionField":null},"loginRiseTime":{"alias":null,"optionField":null},"loginStopTime":{"alias":null,"optionField":null},"validityTime":{"alias":null,"optionField":null},"insertTime":{"alias":null,"optionField":null},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId"},"updateTime":{"alias":null,"optionField":null},"insertManagerId":{"alias":null,"optionField":null},"updateManagerId":{"alias":null,"optionField":null},"insertUserName":{"alias":null,"optionField":null},"updateUserName":{"alias":null,"optionField":null},"insertManagerRealName":{"alias":null,"optionField":null},"updateManagerRealName":{"alias":null,"optionField":null},"yzProjectId":{"alias":null,"optionField":null},"id":{"alias":null,"optionField":null}};
const rtfFields = [];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this._alive = true; this._optionRequests = []; this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  onUnload() { this._alive = false; this._loadRequest?.abort?.(); this._optionRequests.forEach(task => task?.abort?.()); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项来自当前页已编译端点，旧身份和父记录的响应不回填。 */
    loadOptions() {
      const parentId = this.parentId || null, identity = this.currentIdentity();
      const current = () => this._alive && parentId === (this.parentId || null) && identity === this.currentIdentity();
      this._optionRequests.push(yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/findNameIdYzAdminType1679698329655750657', method: 'POST', data: { parentId }, sendSessionId: true, isCurrent: current, success: res => { if (current() && res.code === 200 && Array.isArray(res.data)) this.options.yzAdminTypeId = res.data; } }));
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
      this._loadRequest = yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
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
      yzp.request({ url: '/admin/auto/yz_manager/6031906822946816/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/yz_manager/6031906822946816?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
