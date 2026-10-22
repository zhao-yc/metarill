/** 原字段值是HTTP数组或旧JSON文本；显示和计数不能改写实体或丢失字符串ID。 */
function arrayValue(value) {
 if (value === null || value === undefined || value === '') return []
 const result = typeof value === 'string' ? JSON.parse(value) : value
 if (!Array.isArray(result)) throw new Error('多选字段必须为数组或JSON数组文本。')
 return result.slice()
}

/** 元数据编译的字段只承接原弹层props/events，不提供动态目标或SQL入口。 */
export function createMultiSelectFieldMixin(definitions) {
 const fields = new Map(definitions.map(def => [def.field, Object.freeze({ ...def })]))
 return {
  data() { return { multiSelectionField: '', multiSelectionVersion: 0, multiSelectionError: '' } },
  methods: {
   /** 详情切换建立新订阅，身份变化关闭未确认草稿。 */
   initializeMultiSelections() {
    this.disposeMultiSelections(); this._multiIdentityHandler = () => this.closeMultiSelection(this.multiSelectionField)
    for (const event of ['onChangeProject', 'onLogin', 'onLogout']) uni.$on(event, this._multiIdentityHandler)
   },
   /** 卸载和记录切换只关闭草稿，不提交选择或保存父实体。 */
   disposeMultiSelections() {
    this.closeMultiSelection(this.multiSelectionField)
    for (const event of ['onChangeProject', 'onLogin', 'onLogout']) if (this._multiIdentityHandler) uni.$off(event, this._multiIdentityHandler)
    this._multiIdentityHandler = null
   },
   /** 目标父仅来自同父合同或唯一声明外键，禁止跨层猜测。 */
   multiTargetParent(field) { const def = fields.get(field); return def?.sameParent ? String(this.parentId || '') : def?.targetParentField ? String(this.entity[def.targetParentField] ?? '') : '' },
   /** 只读显示优先采用明确辅助名称，缺失时保留原ID，不从任意表补查。 */
   multiSelectionText(row, field) {
    const def = fields.get(field), value = def?.namesField && row[def.namesField] !== null && row[def.namesField] !== undefined ? row[def.namesField] : row[field]
    try { return arrayValue(value).map(item => item === null ? '' : String(item)).join('，') } catch { return typeof value === 'string' ? value : '' }
   },
   /** 损坏存储显示错误而不自动清空原字段。 */
   multiSelectionCount(field) { try { return arrayValue(this.entity[field]).length } catch { return '?' } },
   /** 实际记录、父级、目标父和身份必须仍与打开时相同；前端快照不替代后端授权。 */
   multiSelectionCurrent(field) {
    const snap = this._multiSelectionSnapshot
    return Boolean(this._alive && !this.readOnlyCondition && snap && this.multiSelectionField === field && snap.field === field && snap.id === this.id && snap.parentId === this.parentId && snap.targetParent === this.multiTargetParent(field) && snap.identity === this.currentIdentity())
   },
   /** 仅已编译且可写字段打开原目标列表弹层；未知父或损坏ID都明确拒绝。 */
   openMultiSelection(field) {
    const def = fields.get(field); if (!this._alive || this.readOnlyCondition || !def?.writable) return
    try {
     const ids = arrayValue(this.entity[field]); if (ids.length > 200 || ids.some(id => typeof id !== 'string' || !/^[1-9][0-9]{0,18}$/.test(id) || id.length === 19 && id > '9223372036854775807') || new Set(ids).size !== ids.length) throw new Error('已选值必须为最多200个不重复的完整字符串ID。')
     const targetParent = this.multiTargetParent(field); if (def.requiresTargetParent && (!/^[1-9][0-9]{0,18}$/.test(targetParent) || targetParent.length === 19 && targetParent > '9223372036854775807')) throw new Error('目标父范围尚未明确，请先选择或保存父记录。')
     this.closeMultiSelection(this.multiSelectionField); this.multiSelectionField = field; this.multiSelectionVersion++; this.multiSelectionError = ''
     this._multiSelectionSnapshot = { field, id: this.id, parentId: this.parentId, targetParent, identity: this.currentIdentity() }
     this.$nextTick(() => { if (this.multiSelectionCurrent(field)) this.$refs[def.ref]?.open() })
    } catch (error) { this.multiSelectionError = error.message; uni.showModal({ showCancel: false, content: error.message }) }
   },
   /** 原onPopSelect返回完整行；只显式提交ID，名称仅回填显示并由源Service事务重新派生。 */
   receiveMultiSelection(field, rows) {
    const def = fields.get(field); if (!def?.writable || !this.multiSelectionCurrent(field)) return
    if (!Array.isArray(rows) || rows.length > 200 || rows.some(row => !row || typeof row.id !== 'string' || !/^[1-9][0-9]{0,18}$/.test(row.id) || row.id.length === 19 && row.id > '9223372036854775807' || (!Object.prototype.hasOwnProperty.call(row, def.nameField) || row[def.nameField] !== null && typeof row[def.nameField] !== 'string')) || new Set(rows.map(row => row.id)).size !== rows.length) { this.multiSelectionError = '选择页回调必须是完整且不重复的字符串ID行数组。'; return }
    const ids = rows.map(row => row.id), names = rows.map(row => row[def.nameField] === null ? null : String(row[def.nameField]))
    this.change(field, ids); this.changes[field] = ids.slice()
    // 原V8确认后立即更新可见名称；非冗余也只作显示，后端不会持久化辅助名。
    if (def.namesField) this.entity[def.namesField] = names
    this.multiSelectionError = ''
   },
   /** 原onPopClose和外部关闭都只销毁草稿，不修改ID/名称或触发保存。 */
   closeMultiSelection(field) { if (!field || this.multiSelectionField !== field) return; const def = fields.get(field); this.multiSelectionField = ''; this._multiSelectionSnapshot = null; this.$refs[def.ref]?.close() },
   /** 遮罩关闭不能留下可接受迟到回执的上下文。 */
   multiSelectionPopupChange(field, event) { if (!event.show) this.closeMultiSelection(field) }
  }
 }
}
