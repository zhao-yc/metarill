/** ID 全程按文本比较，零/无效值保留显示但不作为数据库标识发送。 */
const keyOf = value => value === null || value === undefined || value === '' ? '' : String(value)
const validId = id => typeof id === 'string' && /^[0-9]{1,19}$/.test(id) && id !== '0' && (id.length < 19 || id <= '9223372036854775807')
const fixedEndpoint = /^\/admin\/auto\/[A-Za-z0-9_]+\/[A-Za-z0-9_]+\/findNameId[A-Za-z0-9_]+$/
/** 候选状态不拥有实体，读取失败绝不清空选择。 */
export function createRemoteRelationOptions({ request, getContext, onChange = () => {}, pageSize = 50, delay = 300, schedule = setTimeout, cancel = clearTimeout }) {
 let alive = true, epoch = 0, pageSeq = 0, selectedSeq = 0, pageTask, selectedTask, timer, scopeKey = '', values = [], keyword = '', pageNo = 0
 let records = [], loading = false, selectedLoading = false, hasMore = true, pageError = '', selectedError = '', scopeError = ''
 const cache = new Map()
 /** 换搜索后只保留已选标签，避免多次搜索累积无界缓存。 */
 function retainSelected() { const keep = new Set(values.map(keyOf)); for (const key of cache.keys()) if (!keep.has(key)) cache.delete(key) }
 /** 同步 SDK 异常也执行失败/完成边界，不留下永久加载状态。 */
 function start(options) { try { return request(options) } catch (error) { options.fail?.(error); options.complete?.(); return undefined } }
 /** 身份及已声明表单快照只隔离响应，父键、条件和权限仍由固定端点验证。 */
 function context() { const c = getContext() || {}; return { ...(c.formParameters === undefined ? {} : { formParameters: JSON.parse(JSON.stringify(c.formParameters)) }), endpoint: c.endpoint || '', identity: c.identity || '', authenticated: c.authenticated !== false, parentId: keyOf(c.parentId) || null, targetParentId: c.targetParentId === undefined || c.targetParentId === null ? null : keyOf(c.targetParentId), requireParent: !!c.requireParent, requireTargetParent: !!c.requireTargetParent, sameParent: !!c.sameParent, readonly: !!c.readonly } }
 /** 未知双父上下文只读提示，不猜索引到表等跨层关系。 */
 function checked() { const c = context(); let error = ''; if (!fixedEndpoint.test(c.endpoint)) error = '关系候选尚未编译，保留原值。'; else if (!c.authenticated) error = '请先登录后读取关联名称。'; else if (c.requireParent && !validId(c.parentId)) error = '请先保存或选择来源父记录，当前关联保留原值。'; if (!error && c.requireTargetParent) { if (c.targetParentId === null && c.sameParent) c.targetParentId = c.parentId; if (!validId(c.targetParentId)) error = '缺少明确的目标父记录，当前关联只读并保留原值。' } return { c, error } }
 /** 返回副本，未知/不可用 ID 也保留原顺序。 */
 function snapshot() { return { records: records.map(r => ({ ...r })), selected: values.map(value => ({ ...(cache.get(keyOf(value)) || { id: keyOf(value), value, name: keyOf(value), text: keyOf(value), unavailable: !!selectedError }) })), known: [...cache.values()].map(r => ({ ...r })), loading, selectedLoading, hasMore, keyword, pageNo, blocked: !!scopeError, message: [...new Set([scopeError, selectedError, pageError].filter(Boolean))].join(' ') } }
 /** 状态回调只更新视图，不产生实体写入。 */
 function notify() { if (alive) onChange(snapshot()) }
 /** 先递增序号再取消，SDK 同步取消回调也不能污染新请求。 */
 function abortPage() { pageSeq++; if (timer !== undefined) cancel(timer); timer = undefined; pageTask?.abort?.(); pageTask = undefined; loading = false }
 /** 已选请求也先失效序号再取消。 */
 function abortSelected() { selectedSeq++; selectedTask?.abort?.(); selectedTask = undefined; selectedLoading = false }
 /** 项目或父记录变化立即清空旧名称，不修改实体。 */
 function sync() { const next = JSON.stringify(context()); if (next === scopeKey) return false; scopeKey = next; epoch++; abortPage(); abortSelected(); cache.clear(); records = []; keyword = ''; pageNo = 0; hasMore = true; pageError = ''; selectedError = ''; scopeError = checked().error; notify(); return true }
 /** 只消费固定四字段，ID/value 必须相同，拒绝重复响应项。 */
 function rows(value) { if (!Array.isArray(value)) throw new Error('关联候选响应无效。'); const result = value.map(row => { const id = keyOf(row?.id), text = row?.text ?? row?.name ?? id; if (!validId(id) || id !== keyOf(row?.value) || typeof text !== 'string') throw new Error('关联候选响应无效。'); return { id, value: id, name: row.name ?? text, text } }); if (new Set(result.map(r => r.id)).size !== result.length) throw new Error('关联候选响应包含重复ID。'); return result }
 /** 不用浮点转换长 ID，原始数量和格式都受限制。 */
 function ids() { const result = [...new Set(values.map(keyOf))]; if (values.length > 200 || result.some(id => !validId(id)) || values.some(v => typeof v === 'number' && !Number.isSafeInteger(v))) throw new Error('已选标识无效或超过200项，已保留原值。'); return result }
 /** 已选回读独立于关键词，整体拒绝不能伪装为部分成功。 */
 function readSelected() { abortSelected(); const { c, error } = checked(); scopeError = error; if (error) { notify(); return } let selected; try { selected = ids() } catch (e) { selectedError = e.message; notify(); return } if (!selected.length) { selectedError = ''; notify(); return }
  const stamp = epoch, seq = selectedSeq, key = scopeKey; const current = () => alive && stamp === epoch && seq === selectedSeq && key === JSON.stringify(context()); let finished = false; selectedLoading = true; selectedError = ''; notify()
  const unavailable = () => { selected.forEach(id => cache.delete(id)); selectedError = '已选项不存在、不可访问或读取失败，已保留原值。'; notify() }
  const task = start({ url: c.endpoint + '/selected', method: 'POST', sendSessionId: true, isCurrent: current, data: { selectedIds: selected, parentId: c.parentId, ...(c.requireTargetParent ? { targetParentId: c.targetParentId } : {}), ...(c.formParameters === undefined ? {} : { formParameters: c.formParameters }) },
   success: res => { if (!current()) return; try { if (res.code !== 200) throw new Error(); const result = rows(res.data); if (result.length !== selected.length || result.some(r => !selected.includes(r.id))) throw new Error(); result.forEach(r => cache.set(r.id, r)); selectedError = ''; notify() } catch (_) { unavailable() } }, fail: () => { if (current()) unavailable() }, complete: () => { finished = true; if (current()) { selectedLoading = false; selectedTask = undefined; notify() } } })
  if (!finished) selectedTask = task; if (!task && !finished && current()) { selectedLoading = false; unavailable() }
 }
 /** 底部按页追加；最多保留一万条候选，达到上限明确要求搜索。 */
 function load(target) { sync(); const { c, error } = checked(); scopeError = error; if (error || c.readonly || loading) { notify(); return } if (records.length >= 10000 && target > 1) { hasMore = false; pageError = '候选较多，请输入关键词缩小范围。'; notify(); return }
  const stamp = epoch, seq = ++pageSeq, key = scopeKey, query = keyword; const current = () => alive && stamp === epoch && seq === pageSeq && key === JSON.stringify(context()) && keyword === query; let finished = false; loading = true; pageError = ''; notify()
  const task = start({ url: c.endpoint + '/page', method: 'POST', sendSessionId: true, isCurrent: current, data: { pageNo: target, pageSize: Math.min(100, Math.max(1, pageSize)), keyword: query, parentId: c.parentId, ...(c.requireTargetParent ? { targetParentId: c.targetParentId } : {}), ...(c.formParameters === undefined ? {} : { formParameters: c.formParameters }) },
   success: res => { if (!current()) return; try { if (res.code !== 200) throw new Error('关联候选读取失败，请重试。'); const result = rows(res.data?.records), pages = Number(res.data?.pages); if (!Number.isSafeInteger(pages) || pages < 0 || result.length > pageSize) throw new Error('关联分页响应无效。'); const merged = new Map((target === 1 ? [] : records).map(r => [r.id, r])); result.forEach(r => { merged.set(r.id, r); cache.set(r.id, r) }); records = [...merged.values()]; pageNo = target; hasMore = target < pages && target < 1000000; pageError = '' } catch (e) { pageError = e.message } notify() }, fail: () => { if (current()) { pageError = '关联候选读取失败，请重试。'; notify() } }, complete: () => { finished = true; if (current()) { loading = false; pageTask = undefined; notify() } } })
  if (!finished) pageTask = task; if (!task && !finished && current()) { loading = false; pageError = '关联候选读取未完成，请重试。'; notify() }
 }
 return { getState: snapshot,
  /** 外部值只刷新标签，初始化和读取失败均不发实体更新。 */
  setValue(value) { if (!alive) return; const changed = sync(), next = Array.isArray(value) ? value.slice() : value === null || value === undefined || value === '' ? [] : [value]; if (!changed && JSON.stringify(values) === JSON.stringify(next)) return; values = next; readSelected() },
  /** 展开时清旧搜索，已选失败可显式重试，已有页不重复加载。 */
  open() { if (!alive) return; const changed = sync(); if (changed || selectedError) readSelected(); if (keyword) { abortPage(); keyword = ''; records = []; retainSelected(); pageNo = 0; hasMore = true } if (!pageNo) load(1) },
  /** 新搜索先使旧请求失效，再防抖；关键词按字面传送。 */
  search(value) { if (!alive) return; sync(); abortPage(); keyword = typeof value === 'string' ? value : ''; records = []; retainSelected(); pageNo = 0; hasMore = true; pageError = ''; if (keyword.length > 100) { pageError = '关键词最多100个字符。'; notify(); return } notify(); timer = schedule(() => { timer = undefined; load(1) }, delay) },
  /** 底部一次只加载下一页。 */
  more() { if (alive && !loading && hasMore) load(pageNo + 1) },
  /** 身份/父范围变化重新回读，不复用旧标签。 */
  refresh() { if (!alive) return; scopeKey = ''; sync(); readSelected() },
  /** 生命周期结束释放请求、定时器和缓存。 */
  dispose() { if (!alive) return; alive = false; epoch++; abortPage(); abortSelected(); cache.clear(); records = [] }
 }
}
