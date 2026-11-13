<template>
 <view>
  <zxz-uni-data-select :key="selectorKey" v-bind="$attrs" remote filterable :modelValue="modelValue ?? ''" :multiple="multiple"
   :disabled="readonly || disabled || state.blocked" :localdata="state.records" :remote-selected="state.selected"
   :remote-loading="state.loading" :remote-has-more="state.hasMore" @open="open" @inputChange="search" @loadMore="more"
   @update:modelValue="change" @change="selected" />
  <text v-if="state.message" class="yzp-relation-select__message">{{ state.message }}</text>
 </view>
</template>
<script>
import yzp from '@/common/yzp_runtime/yzp-runtime';
import { createRemoteRelationOptions } from '@/common/yzp_runtime/remoteRelationOptions';
/** 原选择器的远程外壳，网络状态不能直接改写或清空实体。 */
export default {
 name: 'YzpRelationSelect', inheritAttrs: false,
 props: { modelValue: { type: [String, Number, Array], default: '' }, endpoint: { type: String, required: true }, parentId: { type: [String, Number], default: undefined }, targetParentId: { type: [String, Number], default: undefined }, requireParent: Boolean, requireTargetParent: Boolean, sameParent: Boolean, multiple: Boolean, readonly: Boolean, disabled: Boolean },
 emits: ['update:modelValue', 'change', 'options-change'],
 data() { return { state: { records: [], selected: [], loading: false, hasMore: true, blocked: false, message: '' }, selectorKey: 0, identityVersion: 0 } },
 computed: { scopeKey() { this.identityVersion; return JSON.stringify(this.context()) } },
 watch: { modelValue: { deep: true, handler(value) { this._relation?.setValue(value) } }, scopeKey(key) { if (this._relation && key !== this._relationScope) { this._relationScope = key; this.selectorKey++; this._relation.refresh() } } },
 mounted() {
  this._relation = createRemoteRelationOptions({ request: options => yzp.request(options), getContext: () => this.context(), onChange: state => { this.state = state; this.$emit('options-change', state.known) } })
  this._relationScope = JSON.stringify(this.context()); this._relation.setValue(this.modelValue)
  this._identityChanged = () => { this.identityVersion++; const key = JSON.stringify(this.context()); if (key !== this._relationScope) { this._relationScope = key; this.selectorKey++; this._relation?.refresh() } }
  for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$on(event, this._identityChanged)
 },
 beforeUnmount() { this.dispose() }, beforeDestroy() { this.dispose() },
 methods: {
  /** 身份仅用于迟到隔离，父键由生成时可证明的字段上下文提供。 */
  context() { const login = uni.getStorageSync('loginStateId'); return { endpoint: this.endpoint, parentId: this.parentId, targetParentId: this.targetParentId, requireParent: this.requireParent, requireTargetParent: this.requireTargetParent, sameParent: this.sameParent, readonly: this.readonly || this.disabled, authenticated: !!login, identity: JSON.stringify([login || '', uni.getStorageSync('sysParams') || {}]) } },
  /** 单选清空转为明确 null，多选返回副本；只响应用户操作。 */
  change(value) { if (this.readonly || this.disabled || this.state.blocked) return; const next = !this.multiple && value === '' ? null : Array.isArray(value) ? value.slice() : value; this._relation?.setValue(next); this.$emit('update:modelValue', next) },
  /** 对象回调仍受只读和上下文门禁。 */
  selected(value) { if (!this.readonly && !this.disabled && !this.state.blocked) this.$emit('change', value) },
  /** 保留原下拉展开、搜索与底部加载交互。 */
  open() { this._relation?.open() },
  search(value) { this._relation?.search(value) },
  more() { this._relation?.more() },
  /** 释放事件、计时器及两类请求，离开后不再通知页面。 */
  dispose() { this._relation?.dispose(); this._relation = null; for (const event of ['onChangeProject', 'onLogin', 'onLogout']) if (this._identityChanged) uni.$off(event, this._identityChanged) }
 }
}
</script>
<style scoped>
.yzp-relation-select__message { display: block; color: #909399; font-size: 12px; margin-top: 4px; }
</style>
