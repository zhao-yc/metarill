import yzp from '@/common/yzp_runtime/yzp-runtime';
import { createRemoteRelationOptions } from '@/common/yzp_runtime/remoteRelationOptions';

/** 页级标签只按生成的固定关系读取当前记录 ID，避免每行请求和全量数组预取。 */
export function createRelationOptionsMixin(definitions) {
 return {
  mounted() {
   this._relationLabelStop = this.$watch(() => JSON.stringify((definitions || []).map(def => [this.parentId, ...(Array.isArray(this.records) ? this.records : [this.entity || {}]).map(row => [row[def.field], def.targetParentField ? row[def.targetParentField] : null])])), () => this.refreshRelationLabels())
   this._relationLabelIdentity = () => { this.clearRelationLabels(); this.refreshRelationLabels() }
   for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$on(event, this._relationLabelIdentity)
   this.refreshRelationLabels()
  },
  watch: { records() { this.refreshRelationLabels(true) }, entity() { this.refreshRelationLabels(true) } },
  onShow() { this.refreshRelationLabels() }, onUnload() { this.disposeRelationLabels() }, beforeUnmount() { this.disposeRelationLabels() }, beforeDestroy() { this.disposeRelationLabels() },
  methods: {
   /** 只读标签的身份快照不得替代服务端页面/项目授权。 */
   relationLabelIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}, this.parentId || null]) },
   /** 切换项目/父级先取消全部旧标签请求并清空名称，ID 由页面继续保存。 */
   clearRelationLabels() { for (const entry of this._relationLabels?.values?.() || []) entry.runtime?.dispose(); this._relationLabels = new Map(); this._relationLabelScopes = new Map(); for (const def of definitions || []) if (this.options) this.options[def.field] = [] },
   /** 汇总已授权投影，失败分组没有标签；绝不把 /selected 整体拒绝当成空选择成功。 */
   publishRelationLabels() {
    if (!this._alive || this._relationLabelsDisposed) return
    for (const def of definitions || []) {
     const rows = new Map(), scopes = this._relationLabelScopes?.get(def.field)
     // 页面原展示只按字段和ID查找；同ID跨目标父组时保守保留ID，不能混用任何组的名称。
     for (const entry of this._relationLabels?.values?.() || []) if (entry.field === def.field) for (const row of entry.runtime.getState().known) {
      const targets = scopes?.get(String(row.id)); if (targets?.size === 1 && targets.has(entry.targetKey)) rows.set(row.id, row)
     }
     this.options[def.field] = [...rows.values()]
    }
   },
   /** 按可证明的目标父上下文分组，每批最多200个当前页ID，不查任意字段。 */
   refreshRelationLabels(force = false) {
    if (!this._alive || this._relationLabelsDisposed) return
    const identity = this.relationLabelIdentity()
    if (this._relationLabelContext !== identity) { this.clearRelationLabels(); this._relationLabelContext = identity }
    this._relationLabels ||= new Map()
    const planned = new Map(), records = Array.isArray(this.records) ? this.records : [this.entity || {}]
    for (const def of definitions || []) {
     const groups = new Map()
     if (def.unresolvedTarget) continue
     for (const row of records) {
      const value = row[def.field]; if (value === null || value === undefined || value === '') continue
      const target = def.targetParentField ? row[def.targetParentField] : undefined, key = JSON.stringify(target === null || target === undefined ? null : String(target))
      if (!groups.has(key)) groups.set(key, { target, ids: [] })
      const values = Array.isArray(value) ? value : [value]; groups.get(key).ids.push(...values)
     }
     for (const group of groups.values()) { const ids = [...new Set(group.ids)]; for (let offset = 0; offset < ids.length; offset += 200) { const batch = ids.slice(offset, offset + 200), key = JSON.stringify([def.field, group.target ?? null, batch]); planned.set(key, { def, target: group.target, targetKey: group.target === null || group.target === undefined ? null : String(group.target), ids: batch }) } }
    }
    // 先登记完整计划，再启动请求，连同步回执也不能暂时发布跨组同ID的名称。
    this._relationLabelScopes = new Map()
    for (const plan of planned.values()) {
     if (!this._relationLabelScopes.has(plan.def.field)) this._relationLabelScopes.set(plan.def.field, new Map())
     const scopes = this._relationLabelScopes.get(plan.def.field)
     for (const id of plan.ids) { const key = String(id); if (!scopes.has(key)) scopes.set(key, new Set()); scopes.get(key).add(plan.targetKey) }
    }
    for (const [key, entry] of this._relationLabels) if (!planned.has(key)) { entry.runtime.dispose(); this._relationLabels.delete(key) }
    for (const [key, plan] of planned) {
     let entry = this._relationLabels.get(key)
     if (!entry) {
      entry = { field: plan.def.field, targetKey: plan.targetKey, runtime: null }; this._relationLabels.set(key, entry)
      entry.runtime = createRemoteRelationOptions({ request: options => yzp.request(options), getContext: () => ({ ...plan.def, targetParentId: plan.target, parentId: this.parentId, readonly: true, authenticated: !!uni.getStorageSync('loginStateId'), identity: this.relationLabelIdentity() }), onChange: () => this.publishRelationLabels() })
      entry.runtime.setValue(plan.ids)
     } else if (force) entry.runtime.refresh()
    }
    this.publishRelationLabels()
   },
   /** 生命周期清理幂等，旧请求和订阅不能回到新的父记录。 */
   disposeRelationLabels() { if (this._relationLabelsDisposed) return; this._relationLabelsDisposed = true; this._relationLabelStop?.(); for (const event of ['onChangeProject', 'onLogin', 'onLogout']) if (this._relationLabelIdentity) uni.$off(event, this._relationLabelIdentity); this.clearRelationLabels() }
  }
 }
}
