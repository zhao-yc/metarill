import yzp from '@/common/yzp_runtime/yzp-runtime';

/** ID 保留服务端十进制字符串，候选和父级都不能经过浮点近似。 */
function isSelectionCheckboxId(value) {
 return typeof value === 'string' && /^[1-9][0-9]{0,18}$/.test(value) &&
  (value.length < 19 || value <= '9223372036854775807')
}

/** 父字段使用当前标量，安全整数可以转为接口要求的字符串。 */
function checkboxParentId(value) {
 const text = Number.isSafeInteger(value) ? String(value) : value
 return isSelectionCheckboxId(text) ? text : null
}

/** 原选项复用发布路由；父级和身份变化立即废弃请求。 */
export function createOriginalListOptionsMixin({ targetParentFields = [], optionFields = [], checkboxOptionScopes = [] } = {}) {
 return {
  created() {
   this._originalListAlive = true; this._originalOptionSequence = 0; this._originalOptionTasks = new Map()
   // 只清生成器声明的候选数组，不能遍历实体或根据字段后缀猜测业务数据。
   const dependencies = new Set([...targetParentFields, ...checkboxOptionScopes.flatMap(scope => scope.formFields)])
   for (const field of dependencies) this.$watch(() => this.filters?.[field], () => {
    if (!this._originalListAlive) return
    this.clearScopedCheckboxFilters(scope => scope.formFields.includes(field))
    this.reloadOriginalListOptions()
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
  watch: {
   parent_id() {
    if (!this._originalListAlive) return
    this.clearScopedCheckboxFilters(scope => scope.parentScoped)
    this.reloadOriginalListOptions()
   }
  },
  methods: {
   /** 自动加载必须有当前会话；缺少会话只清空，不绕过公共请求的认证提示。 */
   hasOriginalListSession() { return !!uni.getStorageSync('loginStateId') },
   /** 捕获实际已存上下文，不从候选数据或URL建立权限。 */
   originalListIdentity() { return JSON.stringify([this.parent_id || '', !!this.is_son_page, uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]) },
   /** 固定候选数组先清空；登录和换父可以重新加载，退出时不能调用initLists。 */
   reloadOriginalListOptions() { this.clearOriginalListOptions(); if (this.hasOriginalListSession()) this.initLists() },
   /** 退出同步清旧数据和原选择/详情上下文，不销毁实例，之后登录仍能恢复。 */
   resetOriginalListContext(clearSelection = true) {
    this.clearScopedCheckboxFilters(() => true)
    this.clearOriginalListOptions(); this.resetListContext?.()
    if (clearSelection) this.clearSelectionContext?.()
    // 旧身份的确认意图始终失效，但有会话的身份事件不能取消新身份刚重建的回读。
    if (typeof this._originalConfirmPending === 'boolean') this._originalConfirmPending = false
    if (Array.isArray(this.selectedList)) this.selectedList = []
    if (typeof this.isSelectAll === 'boolean') this.isSelectAll = false
    if (typeof this.showSelectedOnly === 'boolean') this.showSelectedOnly = false
    this.closeChildDetailPopup?.()
   },
   /** 原数组候选按发布范围查询，所有合法值到齐后再更新复选框。 */
   loadOriginalArrayCheckboxOptions(definition) {
    const { field, url, requiresParent, sameParent, targetParentField, table, formFields, limit } = definition
    const range = field + 'Range', data = {}
    if (requiresParent) {
     data.parentId = checkboxParentId(this.parent_id)
     if (data.parentId === null) return
    }
    if (sameParent || targetParentField) {
     data.targetParentId = sameParent ? data.parentId : checkboxParentId(this.filters?.[targetParentField])
     if (data.targetParentId == null) return
    }
    if (formFields.length > 0) {
     const values = {}
     for (const binding of formFields) {
      const value = binding.fromParent ? data.parentId : this.filters?.[binding.name]
      const valid = value === null || typeof value === 'boolean' || typeof value === 'string' && value.trim() ||
       typeof value === 'number' && Number.isFinite(value) && (!binding.int64 || Number.isSafeInteger(value))
      if (!valid) return
      values[binding.name] = value
     }
     data.formParameters = { [table]: values }
    }
    return this.originalOptionRequest({
     url, data, method: 'POST', sendSessionId: true,
     success: res => {
      if (res.code !== 200) {
       this[range] = []
       uni.showModal({showCancel: false, content: res.msg})
       return
      }
      const items = res.data, ids = new Set()
      if (!Array.isArray(items) || items.length > limit || items.some(item => {
       if (!isSelectionCheckboxId(item?.value) || item.id !== item.value ||
           !(item.text === null || typeof item.text === 'string') || ids.has(item.value)) return true
       ids.add(item.value)
       return false
      })) {
       this[range] = []
       uni.showModal({showCancel: false, content: '关联候选响应无效或超过' + limit + '条。'})
       return
      }
      this[range] = items.map(({text, value}) => ({text: text ?? '', value}))
     }
    })
   },
   /** 逐页取齐已发布的多选候选；高精度名称分批走原回读，只发布完整的一组结果。 */
   loadOriginalSelectionCheckboxOptions(definition, parent) {
    const { field, url, nameField, int32Name, textNames, requiresParent, limit } = definition
    const key = 'selection-checkbox:' + field, range = field + 'Range'
    const parentId = checkboxParentId(parent)
    if (requiresParent && parentId === null) return
    const scope = requiresParent ? { parentId } : {}
    const pageSize = 1000, labelsBatchSize = 200
    const ids = new Set(), options = []
    let total

    const fail = (message = '多选候选响应不完整，请重试。') => {
     this[range] = []
     uni.showModal({showCancel: false, content: message})
    }
    const send = (path, data, accept) => this.originalOptionRequest({
     url: url + path, method: 'POST', sendSessionId: true, data,
     success(res) { if (res.code === 200) accept(res.data); else fail(res.msg) },
     fail() { fail() }
    }, key)

    const appendLabel = row => {
     const text = row[nameField]
     if (text !== null && typeof text !== 'string' &&
         !(int32Name && Number.isInteger(text) && text >= -2147483648 && text <= 2147483647)) return false
     options.push({text: text === null ? '' : String(text), value: row.id})
     return true
    }
    const loadLabels = (orderedIds, offset = 0) => {
     const selectedIds = orderedIds.slice(offset, offset + labelsBatchSize)
     if (selectedIds.length === 0) { this[range] = options; return }
     send('/selection/selected', {...scope, selectedIds}, rows => {
      if (!Array.isArray(rows) || rows.length !== selectedIds.length ||
          !rows.every((row, index) => row?.id === selectedIds[index] && appendLabel(row))) { fail(); return }
      loadLabels(orderedIds, offset + selectedIds.length)
     })
    }
    const loadPage = pageNo => send('/page', {...scope, pageNo, pageSize, filters: {}, selectedIds: []}, page => {
     if (!page || !Number.isSafeInteger(page.total) || page.total < 0) { fail(); return }
     if (page.total > limit) { fail('多选候选超过' + limit + '项，请缩小目标页范围。'); return }
     if (total === undefined) total = page.total
     const pages = Math.ceil(total / pageSize)
     if (page.total !== total || page.current !== pageNo || page.size !== pageSize || page.pages !== pages ||
         !Array.isArray(page.records) || page.records.length !== Math.min(pageSize, total - (pageNo - 1) * pageSize)) {
      fail(); return
     }
     for (const row of page.records) {
      if (!isSelectionCheckboxId(row?.id) || ids.has(row.id) || (!textNames && !appendLabel(row))) { fail(); return }
      ids.add(row.id)
     }
     if (pageNo < pages) loadPage(pageNo + 1)
     else if (textNames) loadLabels([...ids])
     else this[range] = options
    })
    return loadPage(1)
   },
   /** 同一候选请求只接受最新回执；独立字段共用目标路由时仍按字段隔离。 */
   originalOptionRequest(options, key = options.url) {
    if (!this._originalListAlive || !this.hasOriginalListSession()) return
    const old = this._originalOptionTasks.get(key), sequence = ++this._originalOptionSequence, identity = this.originalListIdentity()
    this._originalOptionTasks.set(key, { sequence, task: null }); old?.task?.abort?.()
    const current = () => this._originalListAlive && this._originalOptionTasks.get(key)?.sequence === sequence && identity === this.originalListIdentity() && (typeof options.isCurrent !== 'function' || options.isCurrent())
    const task = yzp.request({ ...options, isCurrent: current, success: response => { if (current()) options.success?.(response) }, fail: error => { if (current()) options.fail?.(error) }, complete: value => { if (current()) options.complete?.(value) } })
    if (current()) this._originalOptionTasks.get(key).task = task
    return task
   },
   /** 先失效再取消，底层同步abort回调和取消异常都不能阻止其它任务清理。 */
   invalidateOriginalListOptions() { const old = [...this._originalOptionTasks.values()]; this._originalOptionTasks.clear(); this._originalOptionSequence++; for (const pending of old) { try { pending.task?.abort?.() } catch (_) { /* 回执时效不依赖取消成功。 */ } } },
   /** 父级、表单范围和身份变化只移除实际依赖该范围的已选复选 ID。 */
   clearScopedCheckboxFilters(affected) {
    if (!this.filters) return
    for (const scope of checkboxOptionScopes) {
     if (affected(scope)) delete this.filters[scope.field]
    }
   },
   /** 候选字段清单来自当前页元数据编译。 */
   clearOriginalListOptions() { this.invalidateOriginalListOptions(); for (const field of optionFields) this[field] = [] },
   /** 普通页与选择页均按原生命周期清理，不影响其他页面的事件订阅。 */
   disposeOriginalListOptions() { if (!this._originalListAlive) return; this._originalListAlive = false; this.invalidateOriginalListOptions(); for (const event of ['onChangeProject', 'onLogin']) uni.$off(event, this._originalOptionIdentity); uni.$off('onLogout', this._originalOptionLogout) }
  }
 }
}
