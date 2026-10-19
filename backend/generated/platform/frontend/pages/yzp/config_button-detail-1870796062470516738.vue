<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="行为类型" name="actionType"><uni-data-select :modelValue="entity.actionType" @update:modelValue="change('actionType', $event)" :localdata="[{&quot;text&quot;:&quot;\u8C03\u63A5\u53E3&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;\u9875\u9762\u8DF3\u8F6C&quot;,&quot;value&quot;:2},{&quot;text&quot;:&quot;\u9875\u9762\u8DF3\u8F6C(\u65B0\u9875\u9762)&quot;,&quot;value&quot;:3},{&quot;text&quot;:&quot;\u81EA\u5B9A\u4E49\u65B9\u6CD5&quot;,&quot;value&quot;:4}]" /></uni-forms-item>
      <uni-forms-item label="sql" name="sql"><uni-easyinput :modelValue="entity.sql" @update:modelValue="change('sql', $event)" /></uni-forms-item>
      <uni-forms-item label="创建时间" name="insertTime"><text>{{ displayValue(entity, 'insertTime') }}</text></uni-forms-item>
      <uni-forms-item label="位置" name="place"><uni-data-select :modelValue="entity.place" @update:modelValue="change('place', $event)" :localdata="[{&quot;text&quot;:&quot;\u7F16\u8F91\u754C\u9762&quot;,&quot;value&quot;:&quot;Edit&quot;},{&quot;text&quot;:&quot;\u5217\u8868\u4E0A&quot;,&quot;value&quot;:&quot;List&quot;},{&quot;text&quot;:&quot;\u7F16\u8F91\u754C\u9762\u53CA\u8868\u683C&quot;,&quot;value&quot;:&quot;EditAndGrid&quot;},{&quot;text&quot;:&quot;\u8868\u683C&quot;,&quot;value&quot;:&quot;Grid&quot;}]" /></uni-forms-item>
      <uni-forms-item label="视图" name="configPageId"><button size="mini" @click="selectPage('configPageId', '/pages/yzp/config_page-list-1869016706289577986')">选择</button><text>{{ displayValue(entity, 'configPageId') }}</text></uni-forms-item>
      <uni-forms-item label="显示方案" name="visibleScheme"><uni-data-select :modelValue="entity.visibleScheme" @update:modelValue="change('visibleScheme', $event)" :localdata="[{&quot;text&quot;:&quot;\u663E\u793A&quot;,&quot;value&quot;:&quot;Normal&quot;},{&quot;text&quot;:&quot;\u9690\u85CF&quot;,&quot;value&quot;:&quot;Hidden&quot;},{&quot;text&quot;:&quot;\u603B\u662F\u53EF\u89C1&quot;,&quot;value&quot;:&quot;AlwaysVisible&quot;},{&quot;text&quot;:&quot;\u53EF\u5199\u663E\u793A\u53EA\u8BFB\u9690\u85CF&quot;,&quot;value&quot;:&quot;VisibleWhenWritable&quot;},{&quot;text&quot;:&quot;\u4EC5\u5F00\u53D1\u7528\u6237\u53EF\u89C1&quot;,&quot;value&quot;:&quot;OnlyDeve&quot;}]" /></uni-forms-item>
      <uni-forms-item label="动态显示规则" name="visibleRuleJson"><yzp-button-visible-rule-editor :entity="entity" :record-id="id" :readonly="false" @receive="applyCustom" /></uni-forms-item>
      <uni-forms-item label="排序号" name="sortNo"><uni-easyinput :modelValue="entity.sortNo" @update:modelValue="change('sortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="保存选项" name="saveOption"><uni-data-select :modelValue="entity.saveOption" @update:modelValue="change('saveOption', $event)" :localdata="[{&quot;text&quot;:&quot;\u4FDD\u5B58&quot;,&quot;value&quot;:&quot;Save&quot;},{&quot;text&quot;:&quot;\u4E0D\u4FDD\u5B58&quot;,&quot;value&quot;:&quot;NotSave&quot;}]" /></uni-forms-item>
      <uni-forms-item label="is_need_confirm" name="isNeedConfirm"><uni-data-select :modelValue="entity.isNeedConfirm" @update:modelValue="change('isNeedConfirm', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="config_table_name" name="configTableName"><uni-easyinput :modelValue="entity.configTableName" @update:modelValue="change('configTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="config_page_name" name="configPageName"><uni-easyinput :modelValue="entity.configPageName" @update:modelValue="change('configPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="confirm_msg" name="confirmMsg"><uni-easyinput :modelValue="entity.confirmMsg" @update:modelValue="change('confirmMsg', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ displayValue(entity, 'yzProjectName') }}</text></uni-forms-item>
      <uni-forms-item label="类型" name="type"><uni-data-select :modelValue="entity.type" @update:modelValue="change('type', $event)" :localdata="[{&quot;text&quot;:&quot;default&quot;,&quot;value&quot;:0},{&quot;text&quot;:&quot;primary&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;warn&quot;,&quot;value&quot;:2}]" /></uni-forms-item>
      <uni-forms-item label="多选按钮" name="isBatchSelectButton"><uni-data-select :modelValue="entity.isBatchSelectButton" @update:modelValue="change('isBatchSelectButton', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="执行后自动关闭" name="isAutoCloseAfterAction"><uni-data-select :modelValue="entity.isAutoCloseAfterAction" @update:modelValue="change('isAutoCloseAfterAction', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","actionType","sql","place","configPageId","visibleScheme","visibleRuleJson","sortNo","saveOption","isNeedConfirm","configTableName","configPageName","importNo","confirmMsg","type","isBatchSelectButton","isAutoCloseAfterAction"];
const displayFields = {"name":{"alias":null,"optionField":null},"actionType":{"alias":"actionTypeShow","optionField":null},"sql":{"alias":null,"optionField":null},"insertTime":{"alias":null,"optionField":null},"place":{"alias":"placeShow","optionField":null},"configPageId":{"alias":null,"optionField":null},"visibleScheme":{"alias":"visibleSchemeShow","optionField":null},"visibleRuleJson":{"alias":null,"optionField":null},"sortNo":{"alias":null,"optionField":null},"saveOption":{"alias":"saveOptionShow","optionField":null},"isNeedConfirm":{"alias":null,"optionField":null},"configTableName":{"alias":null,"optionField":null},"configPageName":{"alias":null,"optionField":null},"importNo":{"alias":null,"optionField":null},"updateTime":{"alias":null,"optionField":null},"insertManagerId":{"alias":null,"optionField":null},"insertUserName":{"alias":null,"optionField":null},"updateManagerId":{"alias":null,"optionField":null},"insertManagerRealName":{"alias":null,"optionField":null},"updateManagerRealName":{"alias":null,"optionField":null},"updateUserName":{"alias":null,"optionField":null},"confirmMsg":{"alias":null,"optionField":null},"yzProjectName":{"alias":null,"optionField":null},"type":{"alias":"typeShow","optionField":null},"isBatchSelectButton":{"alias":null,"optionField":null},"isAutoCloseAfterAction":{"alias":null,"optionField":null},"id":{"alias":null,"optionField":null}};
const rtfFields = [];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: true, canSave: true, canDelete: true, canCopy: true }; },
  onLoad(options) { this._alive = true; this._optionRequests = []; this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  onUnload() { this._alive = false; this._loadRequest?.abort?.(); this._optionRequests.forEach(task => task?.abort?.()); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_button/1870796062470516738/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项来自当前页已编译端点，旧身份和父记录的响应不回填。 */
    loadOptions() {
      const parentId = this.parentId || null, identity = this.currentIdentity();
      const current = () => this._alive && parentId === (this.parentId || null) && identity === this.currentIdentity();

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
      this._loadRequest = yzp.request({ url: '/admin/auto/config_button/1870796062470516738/detail/' + encodeURIComponent(id) + '?parentId=' + encodeURIComponent(parentId), method: 'GET', sendSessionId: true, isCurrent: current,
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
      yzp.request({ url: '/admin/auto/config_button/1870796062470516738/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_button/1870796062470516738?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
