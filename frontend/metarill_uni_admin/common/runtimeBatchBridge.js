// 仅供平台原批量按钮使用；生成页面、公共运行时及独立应用模板不增加设备管理逻辑。
const CACHE = 'metarill-runtime-batch-pending-v1';
const UUID = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
const HASH = /^[a-f0-9]{64}$/;
const ID = /^[1-9][0-9]{0,18}$/;
const LABELS = Object.freeze({
 pending: '尚未准备', prepared: '意图已保存，尚未入队', queued: '已排队，等待本机助手', leased: '本机助手处理中',
 preview_ready: '预览完成，尚未应用', dependencies_required: '需要分别确认相关原按钮动作', awaiting_application: '等待应用回执',
 applied: '已应用', skipped: '按原配置跳过', failed: '执行失败', expired: '任务过期', rolled_back: '本次修改已回滚', manual_recovery: '结果未知，需核对现场'
});
const WAITING = new Set(['queued', 'leased', 'preview_ready', 'awaiting_application']);
const ERRORS = Object.freeze({ approved_runtime_binding_missing: '未绑定已授权的在线助手', 'target-project-reauthentication-required': '目标项目未获当前会话授权', runtime_enqueue_rejected: '配置或入队校验未通过' });
/** UUID仅标识幂等批次，不作为凭据；权限仍由服务端真实会话和项目授权决定。 */
function batchId() { return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => { const n = Math.floor(Math.random() * 16); return (c === 'x' ? n : (n & 3) | 8).toString(16); }); }
/** 本机只保留批次编号、上下文摘要和时间；不复制会话、路径、项目快照或任何凭据。 */
function records(uni) {
 const values = uni.getStorageSync(CACHE);
 if (values === '' || values == null) return [];
 if (!Array.isArray(values) || values.length > 20 || values.some(item => !item || Object.keys(item).sort().join(',') !== 'batchId,contextSha256,createdAt' || !UUID.test(item.batchId) || !HASH.test(item.contextSha256) || !Number.isSafeInteger(item.createdAt) || item.createdAt < 0) || new Set(values.map(item => item.contextSha256)).size !== values.length) throw new Error('批次缓存异常，请保留现场核查，未创建新批次');
 return values;
}
/** 同步回读确认编号可靠保存；写失败或静默丢写时不允许发送同步请求。 */
function write(uni, values) {
 uni.setStorageSync(CACHE, values);
 if (JSON.stringify(records(uni)) !== JSON.stringify(values)) throw new Error('批次编号未能可靠保存，未发送同步请求');
}
/** 稳定回执只接受当前设备、原批次与规范任务编号；未知状态不能被当作应用成功。 */
function validate(response, record, deviceId) {
 const value = response && response.data;
 if (![200, 500].includes(response && response.code) || !value || value.batchId !== record.batchId || value.deviceId !== deviceId || !Array.isArray(value.items) || value.items.length > 1000) throw new Error('未收到原批次的有效回执');
 const seen = new Set();
 for (const item of value.items) {
  if (!item || typeof item.projectName !== 'string' || !Object.hasOwn(LABELS, item.status) || (item.projectId != null && (typeof item.projectId !== 'string' || !ID.test(item.projectId) || seen.has(item.projectId))) || (item.requestId != null && !UUID.test(item.requestId)) || (item.errorCode != null && typeof item.errorCode !== 'string') || ((WAITING.has(item.status) || ['dependencies_required', 'prepared'].includes(item.status)) && (!item.projectId || !item.requestId)) || (item.status === 'applied' && (!item.projectId || !item.requestId || item.errorCode != null))) throw new Error('批次逐项回执异常，未确认同步成功');
  if (item.projectId != null) seen.add(item.projectId);
 }
 return value;
}
/** 汇总只展示有界纯文本，避免控制字符破坏原弹窗内容。 */
function text(value) { return String(value).replace(/[\x00-\x1f\x7f]/g, ' ').slice(0, 160); }
/** 原确认后进入桥接；同上下文已有记录时仅查询原批次，网络未知绝不重发POST。 */
export function createRuntimeBatchBridge({ request, uni, fingerprint, environment, schedule = setTimeout, cancelSchedule = clearTimeout, now = Date.now, newBatchId = batchId }) {
 const active = new Map();
 const context = () => {
  const params = uni.getStorageSync('sysParams') || {}, user = uni.getStorageSync('userInfo') || {}, session = uni.getStorageSync('loginStateId');
  const projectId = params.yzProjectId, deviceId = params.yzManagerDevicesId, accountId = user.id;
  if (!session || typeof session !== 'string' || typeof accountId !== 'string' || !ID.test(accountId) || typeof projectId !== 'string' || !ID.test(projectId) || typeof deviceId !== 'string' || !ID.test(deviceId)) throw new Error('请先登录并选择当前项目和设备');
  const digest = fingerprint(JSON.stringify([environment(), accountId, projectId, deviceId, session]));
  if (!HASH.test(digest)) throw new Error('无法固定当前会话上下文');
  return { deviceId, digest };
 };
 /** 请求结果必须完整收尾，包括底层登录失效只触发complete的路径；不挂起悬空轮询。 */
 function call(job, options) {
  return new Promise((resolve, reject) => {
   let settled = false;
   const settle = (handler, value) => { if (!settled) { settled = true; handler(value); } };
   job.inflight = request({ ...options, sendSessionId: true, showLoading: false, showError: false, isCurrent: job.current,
    success: response => settle(resolve, response), fail: () => settle(reject, new Error('网络结果未知')),
    complete: () => settle(reject, new Error('未收到完整批次回执')) });
  });
 }
 /** 回执汇总沿用原回调刷新规则；只有至少一个applied且其余全部applied/skipped才返回成功。 */
 function finish(job, value, failure) {
  if (job.done) return;
  const visible = job.current(); job.done = true; cancelSchedule(job.timer); active.delete(job.scope.digest); uni.hideLoading();
  if (!visible) return;
  const items = value ? value.items : [];
  const successful = !failure && items.some(item => item.status === 'applied') && items.every(item => ['applied', 'skipped'].includes(item.status));
  const definitive = value && items.every(item => ['applied', 'skipped', 'failed', 'expired', 'rolled_back'].includes(item.status));
  // 只有确定终态才能清除本机索引；未知、准备中和人工恢复始终保留原UUID。
  if (definitive) {
   try { write(uni, records(uni).filter(item => item.batchId !== job.record.batchId)); }
   catch (_) { /* 清理失败保留原批次，下次只查询，不重复同步。 */ }
  }
  const detail = items.map(item => text(item.projectName) + '：' + LABELS[item.status] + (item.errorCode ? '（' + (ERRORS[item.errorCode] || text(item.errorCode)) + '）' : '') + (item.requestId ? '；任务 ' + item.requestId : '')).join('\n');
  const msg = (failure || (successful ? '批量同步完成。' : items.length ? '批量同步尚未全部应用。' : '当前设备没有可同步的项目配置。')) + '\n批次：' + job.record.batchId + (detail ? '\n' + detail : '') + (!definitive ? '\n原编号已保留；再次点击原按钮只查询此批次，不重发同步。' : '');
  const result = { code: successful ? 200 : 500, msg, data: value || null };
  try {
   if (typeof job.options.success === 'function') job.options.success(result);
   // 原成功处理器只显示toast，因此另外展示逐项汇总；失败沿原错误弹窗，不重复弹出。
   if (successful || typeof job.options.success !== 'function') uni.showModal({ title: '批量同步结果', content: msg, showCancel: false });
  } finally { if (typeof job.options.complete === 'function') job.options.complete(result); }
 }
 /** 自动查询有界；离开身份/设备立即停止迟到副作用，暂停后保留索引供原按钮只读恢复。 */
 async function poll(job, initial) {
  if (!job.current()) { finish(job); return; }
  try {
   const value = validate(initial || await call(job, { url: '/admin/sys/generation-assistant/runtime-batch/status', method: 'POST', data: { batchId: job.record.batchId } }), job.record, job.scope.deviceId);
   if (!job.current()) { finish(job); return; }
   const waiting = value.items.some(item => WAITING.has(item.status));
   if (!waiting || now() >= job.deadline) { finish(job, value, waiting ? '尚未收到最终应用回执，已停止本轮等待。' : null); return; }
   if (value.items.some(item => item.status === 'dependencies_required') && !job.notified) {
    job.notified = true; uni.showModal({ title: '批量同步', content: '部分项目需要分别确认相关原按钮动作。其它项目继续查询；不会扩大本次同步范围。', showCancel: false });
   }
   job.timer = schedule(() => poll(job), 1500);
  } catch (failure) { finish(job, null, failure.message + '，请保留原批次核查。'); }
 }
 /** 包装原URL请求，不改原页面/确认/样式；保存失败时不发送任何写请求。 */
 function start(options) {
  let job;
  try {
   const scope = context(); if (active.has(scope.digest)) return active.get(scope.digest).handle;
   const values = records(uni); let record = values.find(item => item.contextSha256 === scope.digest); const resumed = !!record;
   if (!record) {
    if (values.length >= 20) throw new Error('未结批次达到保留上限，请先核查原批次');
    record = { batchId: newBatchId(), contextSha256: scope.digest, createdAt: now() };
    if (!UUID.test(record.batchId)) throw new Error('批次编号无效'); write(uni, [...values, record]);
   }
   job = { scope, record, options, deadline: now() + 16 * 60 * 1000, done: false, notified: false };
   job.current = () => { try { return !job.done && context().digest === scope.digest && (typeof options.isCurrent !== 'function' || options.isCurrent() !== false); } catch (_) { return false; } };
   job.handle = { abort() { if (job.done) return; finish(job, null, '等待已取消，应用结果仍需查询原批次。'); if (job.inflight && typeof job.inflight.abort === 'function') job.inflight.abort(); } };
   active.set(scope.digest, job); uni.showLoading({ title: '同步中', mask: false });
   if (resumed) poll(job);
   else call(job, { ...options, header: { ...(options.header || {}), 'X-Yzp-Agent-Request-Id': record.batchId, 'X-Yzp-Agent-Confirm': 'true' } })
    .then(response => poll(job, response)).catch(() => poll(job)); // 首次HTTP未知也仅查状态，绝不重发原POST。
   return job.handle;
  } catch (failure) {
   if (job) finish(job, null, failure.message);
   else {
    const result = { code: 500, msg: failure.message, data: null };
    try { if (typeof options.success === 'function') options.success(result); else uni.showModal({ content: failure.message, showCancel: false }); }
    finally { if (typeof options.complete === 'function') options.complete(result); }
   }
  }
 }
 return { start };
}
