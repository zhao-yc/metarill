import yzp from '@/common/yzp_runtime/yzp-runtime';
import { createRemoteRelationOptions } from '@/common/yzp_runtime/remoteRelationOptions';

/** 为原zxz绑定固定候选端点；只管理视图状态，不修改实体、筛选值或页面布局。 */
export function createOriginalRemoteOptionsMixin(definitions = [], owner = 'entity') {
 const initial = () => ({ records: [], selected: [], loading: false, hasMore: true, blocked: false, message: '', revision: 0 })
 return {
  data() { return { originalRemoteOptions: Object.fromEntries(definitions.map(definition => [definition.id, initial()])) } },
  created() {
   this._originalRemoteAlive = true; this._originalRemoteControllers = new Map(); this._originalRemoteOpened = new Set(); this._originalRemoteMessages = new Map()
   for (const definition of definitions) {
    const controller = createRemoteRelationOptions({ request: options => yzp.request(options), getContext: () => this.originalRemoteContext(definition), onChange: state => {
     if (!this._originalRemoteAlive) return
     const revision = this.originalRemoteOptions[definition.id].revision
     this.originalRemoteOptions[definition.id] = { ...state, revision }; this[definition.list] = state.records
     // 复用原错误弹窗；后台初始化不弹窗，不让失败回执清空已选值。
     if (!state.message) this._originalRemoteMessages.delete(definition.id)
     else if (this._originalRemoteOpened.has(definition.id) && this.originalRemoteContext(definition).authenticated && this._originalRemoteMessages.get(definition.id) !== state.message) {
      this._originalRemoteMessages.set(definition.id, state.message); uni.showModal({ showCancel: false, content: state.message })
     }
    } })
    this._originalRemoteControllers.set(definition.id, controller)
    this.$watch(() => this[owner]?.[definition.field], value => { if (this._originalRemoteAlive) controller.setValue(value) }, { deep: true, flush: 'sync' })
    this.$watch(() => JSON.stringify(this.originalRemoteContext(definition)), () => {
     if (!this._originalRemoteAlive) return
     this.originalRemoteOptions[definition.id].revision++; this._originalRemoteOpened.delete(definition.id); this._originalRemoteMessages.delete(definition.id)
     controller.setValue(this[owner]?.[definition.field])
    }, { flush: 'sync' })
   }
  },
  mounted() {
   this._originalRemoteIdentity = () => {
    if (!this._originalRemoteAlive) return
    for (const definition of definitions) { this.originalRemoteOptions[definition.id].revision++; this._originalRemoteOpened.delete(definition.id); this._originalRemoteMessages.delete(definition.id); this._originalRemoteControllers.get(definition.id).refresh() }
   }
   for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$on(event, this._originalRemoteIdentity)
  },
  onUnload() { this.disposeOriginalRemoteOptions() },
  beforeUnmount() { this.disposeOriginalRemoteOptions() },
  methods: {
   /** 所有范围均由生成时声明，身份仅用于隔离迟到响应，不能替代服务端权限。 */
   originalRemoteContext(definition) {
    const login = uni.getStorageSync('loginStateId'), permissions = this.permissionsReady, record = this.recordReady, id = this.id
    const ready = owner !== 'entity' || !!(this._alive && permissions && (!id || record))
    return { endpoint: definition.endpoint, parentId: this.parent_id || null, targetParentId: definition.targetParentField ? this[owner]?.[definition.targetParentField] ?? '' : definition.sameParent ? this.parent_id || null : undefined, requireParent: definition.requireParent, requireTargetParent: definition.requireTargetParent, sameParent: definition.sameParent, readonly: !!(definition.readonlyField && this[definition.readonlyField]), authenticated: !!login && ready && this._originalRemoteAlive, identity: JSON.stringify([login || '', uni.getStorageSync('sysParams') || {}, this.parent_id || '', owner === 'entity' ? [this.id || '', this._detailEpoch || 0, ready] : !!this.is_son_page]) }
   },
   /** 初始化只回读已选；展开后才分页，不预先拉完整候选集合。 */
   loadOriginalRemoteOptions(id) { const definition = definitions.find(item => item.id === id); if (this._originalRemoteAlive && definition) this._originalRemoteControllers.get(id).setValue(this[owner]?.[definition.field]) },
   /** 原展开、搜索和底部事件保持在同一个选择器内。 */
   openOriginalRemoteOptions(id) { if (!this._originalRemoteAlive) return; this.loadOriginalRemoteOptions(id); this._originalRemoteOpened.add(id); this._originalRemoteControllers.get(id)?.open() },
   searchOriginalRemoteOptions(id, value) { if (this._originalRemoteAlive) this._originalRemoteControllers.get(id)?.search(value) },
   moreOriginalRemoteOptions(id) { if (this._originalRemoteAlive) this._originalRemoteControllers.get(id)?.more() },
   /** 先失效再取消，卸载、身份与父范围改变不能让旧标签回填。 */
   disposeOriginalRemoteOptions() { if (!this._originalRemoteAlive) return; this._originalRemoteAlive = false; for (const controller of this._originalRemoteControllers.values()) controller.dispose(); this._originalRemoteControllers.clear(); for (const event of ['onChangeProject', 'onLogin', 'onLogout']) if (this._originalRemoteIdentity) uni.$off(event, this._originalRemoteIdentity) }
  }
 }
}
