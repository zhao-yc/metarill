import yzp from '@/common/yzp_runtime/yzp-runtime';

/** 原下拉框仍消费原数组端点；内部隔离父/身份切换，退出只清理而不触发重新登录。 */
export function createOriginalListOptionsMixin({ targetParentFields = [], optionFields = [] } = {}) {
 return {
  created() {
   this._originalListAlive = true; this._originalOptionSequence = 0; this._originalOptionTasks = new Map()
   // 只清生成器声明的候选数组，不能遍历实体或根据字段后缀猜测业务数据。
   for (const field of targetParentFields) this.$watch(() => this.filters?.[field], () => {
    if (this._originalListAlive) this.reloadOriginalListOptions()
   }, { flush: 'sync' })
  },
  mounted() {
   this._originalOptionIdentity = () => {
    if (!this._originalListAlive) return
    const authenticated = this.hasOriginalListSession()
    // 选择页自己处理登录回读，不能因事件监听顺序清掉它刚恢复的同权请求。
    this.resetOriginalListContext(!authenticated)
    if (authenticated) { this.initLists(); this.refreshData() }
   }
   this._originalOptionLogout = () => { if (this._originalListAlive) this.resetOriginalListContext() }
   for (const event of ['onChangeProject', 'onLogin']) uni.$on(event, this._originalOptionIdentity)
   uni.$on('onLogout', this._originalOptionLogout)
  },
  onUnload() { this.disposeOriginalListOptions() },
  beforeUnmount() { this.disposeOriginalListOptions() },
  watch: { parent_id() { if (this._originalListAlive) this.reloadOriginalListOptions() } },
  methods: {
   /** 自动加载必须有当前会话；缺少会话只清空，不绕过公共请求的认证提示。 */
   hasOriginalListSession() { return !!uni.getStorageSync('loginStateId') },
   /** 捕获实际已存上下文，不从候选数据或URL建立权限。 */
   originalListIdentity() { return JSON.stringify([this.parent_id || '', !!this.is_son_page, uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]) },
   /** 固定候选数组先清空；登录和换父可以重新加载，退出时不能调用initLists。 */
   reloadOriginalListOptions() { this.clearOriginalListOptions(); if (this.hasOriginalListSession()) this.initLists() },
   /** 退出同步清旧数据和原选择/详情上下文，不销毁实例，之后登录仍能恢复。 */
   resetOriginalListContext(clearSelection = true) {
    this.clearOriginalListOptions(); this.resetListContext?.()
    if (clearSelection) this.clearSelectionContext?.()
    // 旧身份的确认意图始终失效，但有会话的身份事件不能取消新身份刚重建的回读。
    if (typeof this._originalConfirmPending === 'boolean') this._originalConfirmPending = false
    if (Array.isArray(this.selectedList)) this.selectedList = []
    if (typeof this.isSelectAll === 'boolean') this.isSelectAll = false
    if (typeof this.showSelectedOnly === 'boolean') this.showSelectedOnly = false
    this.closeChildDetailPopup?.()
   },
   /** 同一固定选项端点只接受最新回执，卸载和换父会先失效再取消。 */
   originalOptionRequest(options) {
    if (!this._originalListAlive || !this.hasOriginalListSession()) return
    const key = options.url, old = this._originalOptionTasks.get(key), sequence = ++this._originalOptionSequence, identity = this.originalListIdentity()
    this._originalOptionTasks.set(key, { sequence, task: null }); old?.task?.abort?.()
    const current = () => this._originalListAlive && this._originalOptionTasks.get(key)?.sequence === sequence && identity === this.originalListIdentity() && (typeof options.isCurrent !== 'function' || options.isCurrent())
    const task = yzp.request({ ...options, isCurrent: current, success: response => { if (current()) options.success?.(response) }, fail: error => { if (current()) options.fail?.(error) }, complete: value => { if (current()) options.complete?.(value) } })
    if (current()) this._originalOptionTasks.get(key).task = task
    return task
   },
   /** 先失效再取消，底层同步abort回调和取消异常都不能阻止其它任务清理。 */
   invalidateOriginalListOptions() { const old = [...this._originalOptionTasks.values()]; this._originalOptionTasks.clear(); this._originalOptionSequence++; for (const pending of old) { try { pending.task?.abort?.() } catch (_) { /* 回执时效不依赖取消成功。 */ } } },
   /** 字段清单来自当前页元数据编译，常量和用户筛选输入保持原样。 */
   clearOriginalListOptions() { this.invalidateOriginalListOptions(); for (const field of optionFields) this[field] = [] },
   /** 普通页与选择页均按原生命周期清理，不影响其他页面的事件订阅。 */
   disposeOriginalListOptions() { if (!this._originalListAlive) return; this._originalListAlive = false; this.invalidateOriginalListOptions(); for (const event of ['onChangeProject', 'onLogin']) uni.$off(event, this._originalOptionIdentity); uni.$off('onLogout', this._originalOptionLogout) }
  }
 }
}
