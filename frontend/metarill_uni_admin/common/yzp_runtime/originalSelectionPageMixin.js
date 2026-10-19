import { createSelectionPageMixin } from '@/common/yzp_runtime/selectionPageMixin';

/** 原列表只保留selectedList/isSelectAll/chkChange接口；内部桥接已验证的完整同权回读，不改变组件和工具栏。 */
export function createOriginalSelectionPageMixin(endpoint, requiresParent = false) {
 return {
  mixins: [createSelectionPageMixin(endpoint, requiresParent)],
  data() { return { selectedList: [], isSelectAll: false } },
  computed: {
   records() { return this.list },
   parentId() { return this.parent_id }
  },
  created() { this._alive = true },
  mounted() { this.initializeSelection() },
  onUnload() { this.disposeOriginalSelection() },
  beforeUnmount() { this.disposeOriginalSelection() },
  watch: {
   parent_id() { if (this._alive) { this.initializeSelection(); this.refreshData() } },
   selectedNames: { deep: true, handler() { if (this._alive) this.initializeSelection() } },
   selectedNameField() { if (this._alive) this.initializeSelection() },
   selectionError(value) { if (value && this._originalConfirmPending) { this._originalConfirmPending = false; uni.showModal({ showCancel: false, content: value }) } },
   selectedDraftIds: { deep: true, handler() { this.syncOriginalSelectionRows() } },
   selectedRows: { deep: true, handler() { this.syncOriginalSelectionRows() } },
   list: { deep: true, handler() { this.syncOriginalSelectionRows() } }
  },
  methods: {
   /** 兼容回读运行时；身份来自现有登录上下文，父字段不由URL替换目标表。 */
   currentIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]) },
   initializeList() { this.initializeSelection(); this.refreshData() },
   loadPage(pageNo = 1) { if (pageNo === 1) this.refreshData(); else this.paginationChange({ current: pageNo }) },
   /** 保留原row.isSelect与selectedList名称，仅展示当前范围的真实行或原ID，不借用上一父范围的缓存。 */
   syncOriginalSelectionRows() {
    const rows = new Map([...(this.selectedRows || []), ...(this.list || [])].map(row => [String(row.id), row]))
    this.selectedList = this.selectedDraftIds.map(id => ({ ...(rows.get(id) || {}), id }))
    for (const row of this.list || []) row.isSelect = this.selectionHas(row.id)
    this.isSelectAll = this.selectionAllOnPage
   },
   /** 原确认按钮保持位置和外观；失败用原弹窗反馈，并允许再次同权核验。 */
   sureSelect() {
    if (!this._alive || this.readOnlyCondition || this.selectionBusy) return
    this._originalConfirmPending = true; this.selectionError = ''; this.selectionReady = true
    this.readSelection(true)
   },
   chkChange(index) { if (this.list[index]) this.toggleSelection(this.list[index]) },
   checkIsAllSelect() { this.syncOriginalSelectionRows() },
   syncSelectedRelations() { this.initializeSelection() },
   /** 关闭和卸载先使旧请求失效；不会写回父实体或修改已保存字段。 */
   disposeOriginalSelection() { this._alive = false; this.disposeSelection(); this.selectedList = [] }
  }
 }
}
