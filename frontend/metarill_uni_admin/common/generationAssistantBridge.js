/** 三个原按钮各自确认单一动作；常规操作仅在线排队，不下载JSON、不接收目录。 */
export const generationAssistantActions = Object.freeze({
 'generate-project-config': '/admin/custom/index/generate_project_config',
 'sync-runtime': '/admin/custom/index/sync_runtime',
 'generate-all-page': '/admin/custom/index/generate_all_page'
})
const titles = { 'generate-project-config': '生成项目配置', 'sync-runtime': '同步运行时', 'generate-all-page': '生成全部页面' }
const uuid = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/
const hash = /^[a-f0-9]{64}$/
const key = 'metarill-generation-pending-v1'
/** UUID只标识幂等请求，不充当凭据；跨端无crypto时仍由真实会话与服务器授权。 */
function requestId() { return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => { const n = Math.floor(Math.random() * 16); return (c === 'x' ? n : (n & 3) | 8).toString(16) }) }
/** 缓存只保存有界任务标识，不保存token、元数据、路径或命令。 */
function pending(uni) { const value = uni.getStorageSync(key); return Array.isArray(value) ? value.filter(item => item && uuid.test(item.requestId || '') && uuid.test(item.bindingId || '') && /^[1-9][0-9]*$/.test(item.projectId || '') && Object.hasOwn(generationAssistantActions,item.action)).slice(-20) : [] }
/** 原请求UUID先落本机，网络结果未知时只查询状态，不自动重发原GET。 */
function save(uni, task) { const values = pending(uni).filter(item => item.requestId !== task.requestId); values.push({ requestId: task.requestId, bindingId: task.bindingId, projectId: task.projectId, action: task.action, mode: task.mode }); uni.setStorageSync(key, values.slice(-20)) }
function remove(uni, task) { uni.setStorageSync(key,pending(uni).filter(item => item.requestId !== task.requestId)) }
/** 回执必须属于本次项目/绑定/动作；只有明确applied三元组能显示完成。 */
function validate(view, task) {
 if (!view || view.requestId !== task.requestId || view.bindingId !== task.bindingId || view.projectId !== task.projectId || view.action !== task.action || !hash.test(view.requestSha256 || '') || !['queued','leased','preview_ready','dependencies_required','failed','expired','awaiting_application','applied','rolled_back','manual_recovery'].includes(view.status) || !['not_applied','unknown','applied','rolled_back'].includes(view.outcome) || typeof view.applied !== 'boolean' || view.applied !== (view.status === 'applied' && view.outcome === 'applied')) throw new Error('助手回执与当前任务不一致，请保留任务编号核查')
 return view
}
/** 每个页面实例保留自己的轮询；离开/切换身份立即阻断迟到提示，回到首页可恢复已登记UUID。 */
export function createGenerationAssistantBridge({ yzp, uni, projectId, isCurrent, schedule = setTimeout, cancelSchedule = clearTimeout, newRequestId = requestId }) {
 let stopped = false; const timers = new Set(); const watching = new Set()
 const current = () => !stopped && isCurrent() && /^[1-9][0-9]*$/.test(projectId)
 const error = message => { if (current()) yzp.showErrorModal(message) }
 const modal = content => { if (current()) uni.showModal({ title: '本地生成助手', content, showCancel: false }) }
 const call = (url, header) => new Promise((resolve,reject) => {
  if (!current()) { reject(new Error('当前项目已变化')); return }
  yzp.request({ url, method: 'GET', header, sendSessionId: true, showError: false, showLoading: false, isCurrent: current,
   success: response => { if (!current()) return; if (response.code === 200) resolve(response.data); else reject(new Error(response.msg || '助手请求未完成')) },
   fail: () => reject(new Error('网络结果未知，原任务编号已保留；返回首页仅查询，不重发动作')) })
 })
 /** 状态查询只有固定UUID路径；预览和依赖提示不能复用普通生成成功处理器。 */
 async function watch(task, initial = null) {
  if (watching.has(task.requestId) || !current()) return; watching.add(task.requestId); let notified = false; const deadline = Date.now() + 16 * 60 * 1000
  const poll = async view => {
   if (!current()) { watching.delete(task.requestId); return }
   try {
    view = validate(view || await call('/admin/sys/generation-assistant/requests/' + task.requestId),task)
    if (view.status === 'applied') { remove(uni,task); watching.delete(task.requestId); uni.showToast({ title: titles[task.action] + '完成', icon: 'success' }); return }
    if (['failed','expired','rolled_back'].includes(view.status)) { remove(uni,task); watching.delete(task.requestId); modal((view.status === 'rolled_back' ? '应用失败，本次修改已恢复' : view.status === 'expired' ? '任务已过期，未收到应用完成回执' : '本机准备失败，未应用') + '。任务：' + task.requestId); return }
    if (view.status === 'manual_recovery') { watching.delete(task.requestId); modal('应用结果未知或需人工核对，现场已保留，不会自动重跑。任务：' + task.requestId); return }
    if (['preview_ready','dependencies_required'].includes(view.status) && task.mode === 'preview') { remove(uni,task); watching.delete(task.requestId); modal('预览已返回；当前绑定为只预览，无法写入工程。请在本机一次性明确授权后使用允许写入的绑定。任务：' + task.requestId); return }
    if (view.status === 'dependencies_required' && !notified) { notified = true; const required = view.preview?.requiredActions; modal('当前预览还需要分别确认：' + (Array.isArray(required) ? required.map(action => titles[action] || action).join('、') : '相关原按钮动作') + '。请按原按钮补做；助手仅合并同一快照下已确认且本机允许的动作，本任务继续查询回执。') }
    if (Date.now() >= deadline) { watching.delete(task.requestId); modal('尚未收到最终应用回执，任务编号已保留。返回首页可继续查询：' + task.requestId); return }
    const timer = schedule(() => { timers.delete(timer); poll(null) },1000); timers.add(timer)
   } catch (failure) { watching.delete(task.requestId); error(failure.message + '；任务：' + task.requestId) }
  }
  await poll(initial)
 }
 /** 保留原按钮确认后才调用，选择绑定只在多设备时出现，不增加每次命令行步骤。 */
 async function start(action) {
  try {
   if (!Object.hasOwn(generationAssistantActions,action)) throw new Error('生成动作无效')
   const bindings = await call('/admin/sys/generation-assistant/bindings')
   if (!Array.isArray(bindings) || bindings.length > 1000 || bindings.some(item => !uuid.test(item.bindingId || '') || item.projectId !== projectId || !['preview','apply'].includes(item.mode) || !Array.isArray(item.allowedActions) || item.allowedActions.some(action => !Object.hasOwn(generationAssistantActions,action)))) throw new Error('当前项目助手绑定响应无效')
   if (!bindings.length) throw new Error('当前项目尚无有效本机助手绑定。请先完成一次设备授权和本机目录/允许动作绑定，并启动助手；日常操作仍使用这里的原按钮')
   let binding = bindings[0]
   if (bindings.length > 1) { const index = await new Promise(resolve => uni.showActionSheet({ itemList: bindings.map(item => item.rootNamespace + ' · ' + item.bindingId.slice(0,8) + (item.mode === 'preview' ? '（只预览）' : '（允许写入）')), success: value => resolve(value.tapIndex), fail: () => resolve(-1) })); if (!Number.isInteger(index) || !bindings[index]) return; binding = bindings[index] }
   if (!current()) return
   const task = { requestId: newRequestId(), bindingId: binding.bindingId, projectId, action, mode: binding.mode === 'apply' && binding.allowedActions.includes(action) ? 'apply' : 'preview' }
   if (!uuid.test(task.requestId)) throw new Error('请求编号无效')
   save(uni,task)
   let view
   try { view = await call(generationAssistantActions[action],{ 'X-Yzp-Agent-Request-Id':task.requestId,'X-Yzp-Agent-Confirm':'true','X-Metarill-Assistant-Binding':task.bindingId }) }
   catch (failure) { error(failure.message + '；只查询原任务：' + task.requestId); watch(task); return }
   if(current() && view?.status === 'queued') uni.showToast({title:'已排队，等待本机助手',icon:'none'})
   await watch(task,view)
  } catch (failure) { error(failure.message) }
 }
 return { start, resume: () => { for (const task of pending(uni).filter(item => item.projectId === projectId)) watch(task) }, stop: () => { stopped=true; for (const timer of timers) cancelSchedule(timer); timers.clear(); watching.clear() } }
}
