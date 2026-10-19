import yzp from '@/common/yzp_runtime/yzp-runtime';

/** 原下拉框仍消费原数组端点；只在内部隔离父/身份切换和迟到回执。 */
export function createOriginalListOptionsMixin() {
 return {
  created() { this._originalListAlive = true; this._originalOptionSequence = 0; this._originalOptionTasks = new Map() },
  mounted() {
   this._originalOptionIdentity = () => { if (this._originalListAlive) { this.invalidateOriginalListOptions(); this.initLists(); this.refreshData() } }
   for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$on(event, this._originalOptionIdentity)
  },
  onUnload() { this.disposeOriginalListOptions() },
  beforeUnmount() { this.disposeOriginalListOptions() },
  watch: { parent_id() { if (this._originalListAlive) { this.invalidateOriginalListOptions(); this.initLists() } } },
  methods: {
   /** 捕获实际已存上下文，不从候选数据或URL建立权限。 */
   originalListIdentity() { return JSON.stringify([this.parent_id || '', !!this.is_son_page, uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]) },
   /** 同一固定选项端点只接受最新回执，卸载和换父会先失效再取消。 */
   originalOptionRequest(options) {
    if (!this._originalListAlive) return
    const key = options.url, old = this._originalOptionTasks.get(key), sequence = ++this._originalOptionSequence, identity = this.originalListIdentity()
    this._originalOptionTasks.set(key, { sequence, task: null }); old?.task?.abort?.()
    const current = () => this._originalListAlive && this._originalOptionTasks.get(key)?.sequence === sequence && identity === this.originalListIdentity()
    const task = yzp.request({ ...options, isCurrent: current, success: response => { if (current()) options.success?.(response) }, fail: error => { if (current()) options.fail?.(error) }, complete: value => { if (current()) options.complete?.(value) } })
    if (current()) this._originalOptionTasks.get(key).task = task
    return task
   },
   /** 保留选项数据的响应式数组由原initLists重新赋值，旧任务不回填。 */
   invalidateOriginalListOptions() { const old = [...this._originalOptionTasks.values()]; this._originalOptionTasks.clear(); for (const pending of old) pending.task?.abort?.() },
   /** 普通页与选择页均按原生命周期清理，不影响其他页面的事件订阅。 */
   disposeOriginalListOptions() { if (!this._originalListAlive) return; this._originalListAlive = false; this.invalidateOriginalListOptions(); for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$off(event, this._originalOptionIdentity) }
  }
 }
}
