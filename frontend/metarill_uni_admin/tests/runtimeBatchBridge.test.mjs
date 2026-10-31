import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import * as relationRuntime from '../common/yzp_runtime/relationReadValues.js';
import { createPageWriteFilter } from '../common/yzp_runtime/pageWritePayload.js';
import { createRuntimeBatchBridge } from '../common/runtimeBatchBridge.js';
const CACHE = 'metarill-runtime-batch-pending-v1';
const id = '11111111-1111-4111-8111-111111111111', task = '22222222-2222-4222-8222-222222222222';
const url = '/admin/custom/index/sync_runtime_all_projects?parentId=undefined';
const hash = value => createHash('sha256').update(value).digest('hex');
const turn = async () => { for (let i = 0; i < 15; i++) await Promise.resolve(); };
const item = (status, extra = {}) => ({ projectId: '7', projectName: '项目甲', status, errorCode: null, requestId: ['skipped', 'pending'].includes(status) ? null : task, ...extra });
const response = (items, extra = {}) => ({ code: 200, data: { batchId: id, deviceId: '11', items, ...extra } });
/** 实际执行桥接状态机和宿主适配层，网络/时钟为可控合同替身，不声称浏览器或真实OAuth验收。 */
function fixture({ cache, writeFails = false, ignoredWrites = false } = {}) {
 const storage = { loginStateId: '仅现有会话缓存的凭据', userInfo: { id: '1' }, sysParams: { yzProjectId: '7', yzManagerDevicesId: '11' } };
 if (cache !== undefined) storage[CACHE] = cache;
 const requests = [], callbacks = [], completed = [], dialogs = [], timers = new Map(), loading = [], failures = [];
 let sequence = 0, time = 100, base = 'http://test/api';
 const uni = { getStorageSync: key => storage[key], setStorageSync: (key, value) => { if (writeFails) throw new Error('模拟存储拒绝'); if (!ignoredWrites) storage[key] = structuredClone(value); }, showModal: value => dialogs.push(value), showLoading: value => loading.push(value), hideLoading: () => loading.push('结束') };
 const raw = options => { assert.ok(storage[CACHE]?.length || !options.url.includes('sync_runtime_all_projects')); const entry = { options, aborts: 0 }; requests.push(entry); return { abort() { entry.aborts++; options.fail?.({}); options.complete?.({}); } }; };
 const settings = { request: raw, uni, fingerprint: hash, environment: () => base, newBatchId: () => id, now: () => time, schedule: fn => { timers.set(++sequence, fn); return sequence; }, cancelSchedule: key => timers.delete(key) };
 const bridge = createRuntimeBatchBridge(settings);
 const options = { url, method: 'POST', sendSessionId: true, success: value => callbacks.push(value), fail: value => failures.push(value), complete: value => completed.push(value) };
 const reply = value => { const req = requests.at(-1).options; req.success(value); req.complete?.(value); };
 const tick = async (advance = 1500) => { time += advance; const timer = timers.entries().next().value; assert.ok(timer); timers.delete(timer[0]); timer[1](); await turn(); };
 return { storage, requests, callbacks, completed, dialogs, timers, loading, failures, uni, bridge, settings, options, raw, reply, tick, setBase: value => base = value };
}

test('发送前可靠保存编号；多个项目等实际应用后才沿原回调成功并汇总跳过', async () => {
 const f = fixture(); f.bridge.start(f.options);
 assert.equal(f.requests.length, 1); assert.equal(f.requests[0].options.url, url);
 assert.equal(f.requests[0].options.header['X-Yzp-Agent-Request-Id'], id); assert.equal(f.requests[0].options.header['X-Yzp-Agent-Confirm'], 'true');
 assert.deepEqual(Object.keys(f.storage[CACHE][0]).sort(), ['batchId', 'contextSha256', 'createdAt']); assert.ok(!JSON.stringify(f.storage[CACHE]).includes('凭据'));
 f.reply(response([item('queued'), item('skipped', { projectId: '8', projectName: '未配路径' })])); await turn(); assert.equal(f.callbacks.length, 0);
 await f.tick(); assert.equal(f.requests.at(-1).options.url, '/admin/sys/generation-assistant/runtime-batch/status'); assert.deepEqual(f.requests.at(-1).options.data, { batchId: id });
 f.reply(response([item('applied'), item('skipped', { projectId: '8', projectName: '未配路径' })])); await turn();
 assert.equal(f.callbacks[0].code, 200); assert.equal(f.completed.length, 1); assert.match(f.dialogs[0].content, /项目甲：已应用/); assert.match(f.dialogs[0].content, /未配路径：按原配置跳过/); assert.deepEqual(f.storage[CACHE], []);
});

test('首个响应丢失只查原编号；再次点击和重建实例均不重发同步POST', async () => {
 const f = fixture(); f.bridge.start(f.options); const first = f.requests[0].options; first.fail({}); first.complete({}); await turn();
 assert.equal(f.requests.length, 2); assert.deepEqual(f.requests[1].options.data, { batchId: id });
 f.requests[1].options.fail({}); await turn(); assert.equal(f.callbacks[0].code, 500); assert.match(f.callbacks[0].msg, /原编号已保留/);
 const restored = createRuntimeBatchBridge(f.settings); restored.start(f.options); assert.equal(f.requests.filter(x => x.options.url === url).length, 1); assert.deepEqual(f.requests.at(-1).options.data, { batchId: id });
 f.reply(response([item('applied')])); await turn(); assert.equal(f.callbacks.at(-1).code, 200);
});

test('活动批次重复点击复用等待句柄，不额外排队或提前完成', async () => {
 const f = fixture(); const first = f.bridge.start(f.options); assert.equal(f.bridge.start(f.options), first); assert.equal(f.requests.length, 1); assert.equal(f.callbacks.length, 0);
 f.reply(response([item('leased')])); await turn(); assert.equal(f.callbacks.length, 0); await f.tick(); f.reply(response([item('applied')])); await turn(); assert.equal(f.callbacks.length, 1);
});

for (const status of ['failed', 'expired', 'rolled_back', 'manual_recovery', 'skipped', 'dependencies_required', 'prepared', 'pending']) {
 test(status + '及空集合均不触发原成功UI；未知/准备态保留编号', async () => {
  const f = fixture(); f.bridge.start(f.options); f.reply(response([item(status)])); await turn();
  assert.equal(f.callbacks[0].code, 500); assert.equal(f.completed.length, 1); assert.equal(f.dialogs.length, 0);
  assert.equal(f.storage[CACHE].length, ['manual_recovery', 'dependencies_required', 'prepared', 'pending'].includes(status) ? 1 : 0);
 });
}

test('空集合明确没有可同步项目；部分成功不会覆盖其它项目失败', async () => {
 for (const items of [[], [item('applied'), item('failed', { projectId: '8', projectName: '未授权项目', errorCode: 'approved_runtime_binding_missing', requestId: null })]]) {
  const f = fixture(); f.bridge.start(f.options); f.reply(response(items)); await turn(); assert.equal(f.callbacks[0].code, 500);
  if (items.length) { assert.match(f.callbacks[0].msg, /项目甲：已应用/); assert.match(f.callbacks[0].msg, /未绑定已授权/); }
  else assert.match(f.callbacks[0].msg, /没有可同步/);
 }
});

for (const drift of ['login', 'project', 'device', 'account', 'environment']) {
 test(drift + '变化后迟到应用回执不刷新/弹成功/发起新查询', async () => {
  const f = fixture(); f.bridge.start(f.options); const req = f.requests[0].options;
  if (drift === 'login') f.storage.loginStateId = '新会话'; if (drift === 'project') f.storage.sysParams.yzProjectId = '8'; if (drift === 'device') f.storage.sysParams.yzManagerDevicesId = '12'; if (drift === 'account') f.storage.userInfo.id = '2'; if (drift === 'environment') f.setBase('http://another/api');
  assert.equal(req.isCurrent(), false); req.success(response([item('applied')])); req.complete({}); await turn(); assert.equal(f.callbacks.length, 0); assert.equal(f.completed.length, 0); assert.equal(f.dialogs.length, 0); assert.equal(f.requests.length, 1); assert.equal(f.storage[CACHE].length, 1);
 });
}

test('坏回执编号/项目/任务/状态不清索引，也不当成功', async () => {
 for (const value of [response([item('applied')], { batchId: task }), response([item('applied')], { deviceId: '12' }), response([item('applied', { requestId: null })]), response([item('applied', { projectId: 7 })]), response([item('new_state')]), response([item('queued', { requestId: null })]), response([item('applied'), item('applied')])]) {
  const f = fixture(); f.bridge.start(f.options); f.reply(value); await turn(); assert.equal(f.callbacks[0].code, 500); assert.equal(f.storage[CACHE].length, 1); assert.equal(f.requests.length, 1);
 }
});

test('存储失败/写入无效/缓存损坏时零同步请求，不覆盖原未知现场', () => {
 for (const options of [{ writeFails: true }, { ignoredWrites: true }, { cache: [{ batchId: '损坏', contextSha256: '原记录' }] }]) {
  const f = fixture(options), before = JSON.stringify(f.storage[CACHE]); f.bridge.start(f.options); assert.equal(f.requests.length, 0); assert.equal(f.callbacks[0].code, 500); if (options.cache) assert.equal(JSON.stringify(f.storage[CACHE]), before);
 }
});

test('取消及等待期限仅停止查询，原编号仍可只读恢复', async () => {
 for (const cancel of [true, false]) {
  const f = fixture(); const handle = f.bridge.start(f.options); f.reply(response([item('queued')])); await turn();
  if (cancel) handle.abort(); else { await f.tick(17 * 60 * 1000); f.reply(response([item('leased')])); await turn(); }
  assert.equal(f.callbacks[0].code, 500); assert.equal(f.storage[CACHE].length, 1); assert.equal(f.timers.size, 0); assert.equal(f.completed.length, 1);
 }
});

test('只收到complete或登录失效收尾不留下悬空等待', async () => {
 const f = fixture(); f.bridge.start(f.options); f.requests[0].options.complete({ code: 500, msg: '登录超时' }); await turn();
 assert.equal(f.requests.length, 2); f.requests[1].options.complete({ code: 500, msg: '登录超时' }); await turn(); assert.equal(f.callbacks[0].code, 500); assert.equal(f.completed.length, 1); assert.equal(f.storage[CACHE].length, 1);
});

test('部分依赖待确认时其它项目继续轮询，不主动确认或扩大动作', async () => {
 const f = fixture(); f.bridge.start(f.options); const states = [item('dependencies_required'), item('queued', { projectId: '8' })];
 f.reply(response(states)); await turn(); assert.equal(f.dialogs.length, 1); assert.equal(f.callbacks.length, 0); await f.tick();
 f.reply(response([states[0], item('applied', { projectId: '8' })])); await turn(); assert.equal(f.callbacks[0].code, 500); assert.equal(f.requests.filter(x => x.options.url !== url && !x.options.url.endsWith('/status')).length, 0);
});

test('真实原页面方法保留一次确认、旧POST、parentId和原customButtonHandler；适配后等待实际应用', async () => {
 const f = fixture(); const platform = await readFile(new URL('../common/yzp.js', import.meta.url), 'utf8');
 const yzp = { request: f.raw, customButtonHandler: result => f.callbacks.push(result) };
 const context = { uni: f.uni, yzp, CryptoJS: { SHA256: hash }, getApp: () => ({ globalData: { environment: { base_url: 'http://test/api' } } }), clearLoginStorageWithWorkspaceSelection() {}, createRuntimeBatchBridge: options => createRuntimeBatchBridge({ ...options, schedule: f.settings.schedule, cancelSchedule: f.settings.cancelSchedule, newBatchId: () => id, now: f.settings.now }) };
 vm.runInNewContext(platform.replace(/^import[^\n]*\n/gm, '').replace('export default yzp;', ''), context);
 const source = await readFile(new URL('../pages/yzp/yz_project-list-6186303424888832.vue', import.meta.url), 'utf8');
 const method = source.match(/customerButtonHandler2069009429197623296\(\) \{[\s\S]*?\},(?=\s*\/\/)/)[0].slice(0, -1);
 vm.runInNewContext('globalThis.button={' + method + '}', context); context.button.customerButtonHandler2069009429197623296();
 assert.equal(f.requests.length, 0); assert.match(f.dialogs[0].content, /同步当前设备全部项目/); f.dialogs[0].success({ confirm: false }); assert.equal(f.requests.length, 0); f.dialogs[0].success({ confirm: true });
 assert.equal(f.requests[0].options.url, url); f.reply(response([item('queued')])); await turn(); assert.equal(f.callbacks.length, 0); await f.tick(); f.reply(response([item('applied')])); await turn(); assert.equal(f.callbacks[0].code, 200);
 // 全文件冻结摘要证明本任务未重写任何DOM、样式或页面方法。
 assert.equal(hash(source), '91dbcb71fa191ac1c3888412ef5210403e89a4e9927af9f45b35f0b8081f90fd');
});

test('HBuilderX按需uni API替换后桥接仍工作，其它请求原样透传', async () => {
 const f = fixture(); let platform = await readFile(new URL('../common/yzp.js', import.meta.url), 'utf8');
 const yzp = { request: f.raw, customButtonHandler() {} };
 const context = { uni: {}, compiledUni: f.uni, yzp, CryptoJS: { SHA256: hash }, getApp: () => ({ globalData: { environment: { base_url: 'http://test/api' } } }), clearLoginStorageWithWorkspaceSelection() {}, createRuntimeBatchBridge: options => createRuntimeBatchBridge({ ...options, ...f.settings, request: options.request, uni: options.uni }) };
 platform = platform.replace(/^import[^\n]*\n/gm, '').replace('export default yzp;', '').replace(/\buni\.(getStorageSync|setStorageSync|showModal|showLoading|hideLoading)\b/g, 'compiledUni.$1'); vm.runInNewContext(platform, context);
 yzp.request(f.options); f.reply(response([item('applied')])); await turn(); assert.equal(f.callbacks[0].code, 200);
 const ordinary = { url: '/admin/auto/source/7/page', method: 'POST', data: { pageNo: 1 } }; yzp.request(ordinary); assert.equal(f.requests.at(-1).options, ordinary);
});


test('实际共享request经过平台桥接后仍发送原sysParams/会话/空对象，只在最终应用回调成功', async () => {
 const f = fixture(), sdk = [], sdkTimers = new Map(); let timerId = 0;
 f.uni.request = options => { sdk.push(options); return { abort() { options.fail({}); options.complete({}); } }; };
 const runtimeSource = await readFile(new URL('../common/yzp_runtime/yzp-runtime.js', import.meta.url), 'utf8');
 const platform = await readFile(new URL('../common/yzp.js', import.meta.url), 'utf8');
 const schema = JSON.parse(await readFile(new URL('../common/yzp_runtime/page-write-schema.generated.json', import.meta.url), 'utf8'));
 const context = vm.createContext({ ...relationRuntime, uni: f.uni, CryptoJS: { SHA256: hash }, getApp: () => ({ globalData: { environment: { base_url: 'http://test/api' } } }), clearLoginStorageWithWorkspaceSelection() {},
  utils: { isBlank: value => value == null || value === '' }, filterPageWriteData: createPageWriteFilter(schema),
  setTimeout: callback => { sdkTimers.set(++timerId, callback); return timerId; }, clearTimeout: key => sdkTimers.delete(key), console,
  createRuntimeBatchBridge: options => createRuntimeBatchBridge({ ...options, schedule: f.settings.schedule, cancelSchedule: f.settings.cancelSchedule, newBatchId: () => id, now: f.settings.now }) });
 vm.runInContext(runtimeSource.slice(runtimeSource.indexOf('const TAB_BAR_PAGES')).replace('export default yzp;', 'globalThis.runtime=yzp;'), context);
 context.yzp = context.runtime; vm.runInContext(platform.replace(/^import[^\n]*\n/gm, '').replace('export default yzp;', ''), context);
 context.yzp.request(f.options); assert.equal(sdk.length, 1); assert.equal(sdk[0].url, 'http://test/api' + url); assert.equal(JSON.stringify(sdk[0].data), '{}');
 assert.equal(sdk[0].header.loginStateId, f.storage.loginStateId); assert.equal(JSON.parse(decodeURIComponent(sdk[0].header.sysParams)).yzManagerDevicesId, '11');
 sdk[0].success({ data: response([item('queued')]) }); sdk[0].complete({}); await turn(); assert.equal(f.callbacks.length, 0); await f.tick();
 assert.equal(sdk[1].url, 'http://test/api/admin/sys/generation-assistant/runtime-batch/status'); assert.equal(JSON.stringify(sdk[1].data), JSON.stringify({ batchId: id }));
 sdk[1].success({ data: response([item('applied')]) }); sdk[1].complete({}); await turn(); assert.equal(f.callbacks[0].code, 200); assert.equal(f.completed.length, 1); assert.equal(sdk.length, 2);
});

test('原缓存容量已满时不驱逐未知批次，也不发起第21个同步', () => {
 const values = Array.from({ length: 20 }, (_, i) => ({ batchId: id, contextSha256: hash('其它上下文' + i), createdAt: i }));
 const f = fixture({ cache: values }), before = JSON.stringify(f.storage[CACHE]); f.bridge.start(f.options); assert.equal(f.requests.length, 0); assert.equal(f.callbacks[0].code, 500); assert.match(f.callbacks[0].msg, /保留上限/); assert.equal(JSON.stringify(f.storage[CACHE]), before);
});
