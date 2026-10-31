const clone = value => JSON.parse(JSON.stringify(value))
const busy = new Set(['previewing', 'confirming', 'applying', 'verifying'])

/** 结构工作台只发送服务器原计划；每次确认最多执行一次，执行后预览仅核验，不自动重试 DDL。 */
export function createSchemaSyncWorkbench({ projectId, tableId, request, confirm, isCurrent = () => true, onChange = () => {} }) {
  if (!/^[1-9][0-9]*$/.test(projectId || '') || (tableId && !/^[1-9][0-9]*$/.test(tableId))) throw new Error('项目或配置表标识无效')
  const scope = { projectId, ...(tableId ? { tableId } : {}) }
  let disposed = false, plan = null
  const state = { phase: 'idle', plan: null, journal: null, verification: null, message: '', error: '' }
  const current = () => !disposed && isCurrent()
  const emit = () => { if (current()) onChange(clone(state)) }
  const transition = phase => { state.phase = phase; emit() }
  const data = response => {
    if (!response || response.code !== 200) throw new Error(response && response.msg || '结构接口请求失败')
    return response.data
  }
  const readPreview = async () => {
    const value = data(await request('/admin/sys/database/preview', clone(scope)))
    if (!value || typeof value.id !== 'string' || !Array.isArray(value.changes) || value.changes.some(x => typeof x.sql !== 'string')) throw new Error('结构计划响应无效，请重新预览')
    return clone(value)
  }
  return {
    getState: () => clone(state),
    dispose() { disposed = true; plan = null },
    async preview() {
      if (!current() || busy.has(state.phase)) return
      plan = null; Object.assign(state, { plan: null, journal: null, verification: null, error: '', message: '' }); transition('previewing')
      try {
        const value = await readPreview()
        if (!current()) return
        plan = value; state.plan = clone(value)
        state.message = value.changes.length ? '请核对目标数据库、差异和 SQL，再确认执行。' : '当前受管结构与配置一致，无需执行。'
        transition('ready')
      } catch (error) { if (current()) { state.error = error.message; transition('error') } }
    },
    async apply() {
      if (!current() || state.phase !== 'ready' || !plan || !plan.changes.length) return
      const original = clone(plan); transition('confirming')
      let approved
      try { approved = await confirm(clone(original)) } catch (error) { if (current()) { state.error = error.message; transition('ready') }; return }
      if (!current()) return
      if (!approved) { transition('ready'); return }
      // 提交前消费当前计划，迟到回调和连点不会再次执行；失败也必须先重新预览。
      plan = null; state.error = ''; state.message = ''; transition('applying')
      let succeeded = false
      try {
        const response = await request('/admin/sys/database/apply', { projectId, plan: clone(original) })
        if (!current()) return
        state.journal = clone(response && (response.data || response.map && (response.map.result || response.map.journal)) || null)
        if (!response || response.code !== 200) throw new Error(response && response.msg || '执行结果未知，请查看日志及核验结果')
        if (!state.journal || state.journal.succeeded !== true) throw new Error('结构执行未成功，请查看执行记录')
        succeeded = true
      } catch (error) { if (current()) state.error = error.message + '；DDL 可能已部分生效，不会自动重试。' }
      if (!current()) return
      transition('verifying')
      try {
        state.verification = await readPreview()
        if (!current()) return
        const remaining = state.verification.changes.length
        state.message = remaining ? '核验仍有 ' + remaining + ' 项差异。请检查执行记录后手动重新预览。' : '再次读取数据库后，当前受管结构与配置一致。'
        transition(succeeded && remaining === 0 ? 'complete' : 'error')
      } catch (error) {
        if (current()) { state.error += (state.error ? ' ' : '') + '执行后核验失败：' + error.message; transition('error') }
      }
    }
  }
}
