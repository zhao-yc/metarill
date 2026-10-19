import yzp from '@/common/yzp_runtime/yzp-runtime';

/** 旧选择页数组/JSON文本合同保持ID顺序；不把丢失精度的数字、对象或SQL当作ID。 */
function selectedIds(value) {
 const rows = value === null || value === undefined || value === '' ? [] : typeof value === 'string' ? JSON.parse(value) : value
 if (!Array.isArray(rows) || rows.length > 200) throw new Error('多选页最多200个已选项，已选值必须为数组。')
 return [...new Set(rows.map(id => {
  if (typeof id !== 'string' && (typeof id !== 'number' || !Number.isSafeInteger(id))) throw new Error('已选ID必须为完整正整数字符串。')
  const text = String(id)
  if (!/^[1-9][0-9]{0,18}$/.test(text) || text.length === 19 && text > '9223372036854775807') throw new Error('已选ID必须为完整正整数字符串。')
  return text
 }))]
}

/** 仅type=2生成页使用的选择草稿；端点来自固定生成页面，不写入业务实体。 */
export function createSelectionPageMixin(endpoint, requiresParent = false) {
 return {
  props: { selectedIds: { type: [Array, String], default: () => [] }, selectedNames: { type: [Array, String], default: () => [] }, selectedNameField: { type: String, default: 'name' } },
  data() { return { selectedDraftIds: [], selectedRows: [], showSelectedOnly: false, selectionBusy: false, selectionReady: false, selectionError: '' } },
  computed: {
   selectionCanConfirm() { return this._alive && !this.readOnlyCondition && !this.selectionBusy && this.selectionReady && !this.selectionError },
   selectionAllOnPage() { return this.records.length > 0 && this.records.every(row => this.selectionHas(row.id)) }
  },
  watch: { selectedIds: { deep: true, handler() { if (this._alive) { this.initializeSelection(); if (this.showSelectedOnly) this.loadPage(1) } } } },
  methods: {
   /** 初始值只信任ID，旧selectedNames不能绕过同权回读成为确认结果。 */
   initializeSelection() {
    this.disposeSelection(); this._selectionGeneration = (this._selectionGeneration || 0) + 1
    this.selectedRows = []; this.selectionReady = false; this.selectionError = ''; this.showSelectedOnly = false
    this._selectionIdentityHandler = () => { if (this._alive) { this.initializeList({ parentId: this.parentId }); this.loadPage(1) } }
    for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$on(event, this._selectionIdentityHandler)
    try { this.selectedDraftIds = selectedIds(this.selectedIds); this.readSelection(false) }
    catch (error) { this.selectedDraftIds = []; this.selectionError = error.message }
   },
   /** 身份、父范围和读取序号共同隔离迟到回执；取消不会回传任何选择。 */
   disposeSelection() {
    this._selectionGeneration = (this._selectionGeneration || 0) + 1; this._selectionRequest?.abort?.(); this._selectionRequest = null; this.selectionBusy = false
    for (const event of ['onChangeProject', 'onLogin', 'onLogout']) if (this._selectionIdentityHandler) uni.$off(event, this._selectionIdentityHandler)
    this._selectionIdentityHandler = null
   },
   /** 每次确认都读取全部已选行；任何缺失/越权/软删项整体报错并清空旧名称缓存。 */
   readSelection(confirm) {
    if (!this._alive || confirm && !this.selectionCanConfirm) return
    this._selectionRequest?.abort?.(); const generation = this._selectionGeneration = (this._selectionGeneration || 0) + 1
    const ids = this.selectedDraftIds.slice(), identity = this.currentIdentity(), parentId = this.parentId
    const current = () => this._alive && generation === this._selectionGeneration && identity === this.currentIdentity() && parentId === this.parentId
    const failure = message => { if (current()) { this.selectedRows = []; this.selectionReady = false; this.selectionError = message || '已选行读取失败，请重新确认选择。' } }
    if (requiresParent && !parentId) { failure('为避免数据丢失，请先保存当前页面的数据。'); return }
    this.selectionBusy = true; this.selectionReady = false; this.selectionError = ''; this.selectedRows = []
    const options = { url: endpoint + '/selection/selected', method: 'POST', sendSessionId: true, data: { parentId: parentId || null, selectedIds: ids }, isCurrent: current,
     success: response => {
      if (!current()) return
      if (response.code !== 200 || !Array.isArray(response.data)) { failure(response.msg); return }
      const rows = new Map(response.data.map(row => [String(row?.id), row]))
      if (rows.size !== ids.length || response.data.length !== ids.length || ids.some(id => !rows.has(id))) { failure('已选行发生变化，请重新确认选择。'); return }
      this.selectedRows = ids.map(id => ({ ...rows.get(id), id })); this.selectionReady = true; this.selectionError = ''
      if (confirm && !this.readOnlyCondition) {
       const result = this.selectedRows.map(row => ({ ...row }))
       this.$emit('onPopSelect', result)
       if (this._listRouted) this.getOpenerEventChannel?.().emit('selected', result)
       this.sureClose()
      }
     }, fail: () => failure('已选行读取失败，请重新确认选择。'), complete: () => { if (current()) this.selectionBusy = false } }
    try { this._selectionRequest = yzp.request(options); if (!this._selectionRequest && current()) { failure('登录已失效或读取未启动，请重新登录后确认选择。'); options.complete() } }
    catch (error) { failure(error.message); options.complete() }
   },
   /** 页面勾选只改本地草稿，不修改selectedIds/selectedNames或父实体。 */
   selectionHas(id) { return this.selectedDraftIds.includes(String(id)) },
   toggleSelection(row) {
    if (!this._alive || this.readOnlyCondition || this.selectionBusy || !this.records.includes(row)) return
    const id = String(row.id), ids = this.selectedDraftIds.slice(), index = ids.indexOf(id)
    if (index >= 0) ids.splice(index, 1)
    else { if (ids.length >= 200) { uni.showModal({ showCancel: false, content: '多选页最多选择200项，请减少选择。' }); return }; ids.push(id) }
    this.selectedDraftIds = ids; this.selectionError = ''; this.selectionReady = true; this.selectedRows = []
    if (this.showSelectedOnly) this.loadPage(1)
   },
   /** 全选仅作用于当前页，不把候选总数伪装成已全部选中。 */
   selectAll() {
    if (!this._alive || this.readOnlyCondition || this.selectionBusy) return
    const visible = this.records.map(row => String(row.id)), keep = this.selectedDraftIds.filter(id => !visible.includes(id))
    const ids = this.selectionAllOnPage ? keep : [...keep, ...visible.filter((id, index) => visible.indexOf(id) === index)]
    if (ids.length > 200) { uni.showModal({ showCancel: false, content: '多选页最多选择200项，请减少选择。' }); return }
    this.selectedDraftIds = ids; this.selectionError = ''; this.selectionReady = true; this.selectedRows = []
    if (this.showSelectedOnly) this.loadPage(1)
   },
   /** 空的仅看已选不能退化成普通全量查询；原分页、筛选和排序继续由页面维护。 */
   toggleSelectedOnly() {
    if (!this.showSelectedOnly && this.selectedDraftIds.length === 0) { uni.showModal({ showCancel: false, content: '当前没有已勾选数据' }); return }
    this.showSelectedOnly = !this.showSelectedOnly; this.loadPage(1)
   },
   /** 选好了通过同一页端点重新验证后才发旧onPopSelect row[]。 */
   sureSelect() { this.readSelection(true) },
   /** 关闭或取消先失效待确认请求，不发送选择事件或保存请求。 */
   sureClose() { this._selectionGeneration = (this._selectionGeneration || 0) + 1; this._selectionRequest?.abort?.(); this.selectionBusy = false; this.$emit('onPopClose'); if (this._listRouted) uni.navigateBack() }
  }
 }
}
