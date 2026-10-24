import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';

/** 执行真实页面方法，以浏览器既有登录态和请求回调核对契约。 */
async function fixture(page, loggedIn = true) {
  const source = await readFile(new URL(`../pages/custom/${page}.vue`, import.meta.url), 'utf8');
  const requests = [], redirects = [], dialogs = [];
  const context = { Date, setInterval: () => 1, clearInterval: () => {},
    uni: { getStorageSync: key => key === 'loginStateId' ? (loggedIn ? '测试会话' : '') : {}, showModal: options => dialogs.push(options), showToast: () => {} },
    yzp: { request: options => { requests.push(options); return { abort() {} }; }, goLoginWithRedirect: url => redirects.push(url) } };
  vm.runInNewContext(source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;\s*$/gm, '').replace('export default', 'globalThis.options ='), context);
  const options = context.options, state = Object.assign(options.data(), options.methods);
  for (const [name, getter] of Object.entries(options.computed || {})) Object.defineProperty(state, name, { get: () => getter.call(state) });
  return { state, options, requests, redirects, dialogs };
}

test('授权列表兼容 C# ISO 日期和旧数字毫秒，缺失与非法时间有明确占位', async () => {
  const { state } = await fixture('agent_grants');
  const milliseconds = Date.parse('2026-10-19T01:02:03Z');
  const expected = new Date(milliseconds).toLocaleString();
  for (const input of [milliseconds, String(milliseconds), '2026-10-19T01:02:03Z', '2026-10-19T09:02:03+08:00']) assert.equal(state.formatTime(input), expected);
  for (const input of [null, undefined, '', ' ', '非法日期']) assert.equal(state.formatTime(input, '尚未使用'), '尚未使用');
});

test('未登录的设备授权链接回跳保留终端用户码', async () => {
  const { state, options, redirects, requests } = await fixture('agent_authorize', false);
  options.onLoad.call(state, { user_code: ' abcd ef23 ' });
  options.onShow.call(state);
  assert.equal(redirects[0], '/pages/custom/agent_authorize?user_code=ABCDEF23');
  assert.equal(requests.length, 0);
});

test('设备查询只读取请求，批准与拒绝仍需明确调用且防止重复提交', async () => {
  for (const approve of [true, false]) {
    const { state, requests } = await fixture('agent_authorize');
    state.userCode = 'ABCDEF23'; state.loadDevice();
    assert.equal(requests.length, 1); assert.equal(requests[0].method, 'GET'); assert.equal(requests[0].sendSessionId, true);
    requests[0].success({ code: 200, data: { status: 'PENDING', expiresAt: Date.now() + 60000 } }); requests[0].complete();
    state.decide(approve); state.decide(approve);
    assert.equal(requests.length, 2); assert.equal(requests[1].method, 'POST'); assert.equal(requests[1].data.approve, approve);
    requests[1].success({ code: 200 }); requests[1].complete();
    assert.equal(state.device.status, approve ? 'APPROVED' : 'DENIED');
  }
});

test('批准结果未知时要求重新查询，不自动重发写请求', async () => {
  const { state, requests } = await fixture('agent_authorize');
  state.userCode = 'ABCDEF23'; state.device = { status: 'PENDING', expiresAt: Date.now() + 60000 };
  state.decide(true); requests[0].fail(); requests[0].complete();
  assert.equal(state.device, null); assert.match(state.error, /重新查询/); assert.equal(requests.length, 1);
});

test('授权管理保留服务端授权ID，撤销只在人工确认之后提交', async () => {
  const { state, requests, dialogs } = await fixture('agent_grants');
  state.loadGrants();
  const grant = { id: '2200000000000000001', clientName: '测试客户端', createdAt: '2026-10-19T01:02:03Z' };
  requests[0].success({ code: 200, data: [grant] }); requests[0].complete();
  state.confirmRevoke(grant); assert.equal(requests.length, 1);
  dialogs[0].success({ confirm: true }); assert.equal(requests.length, 2); assert.equal(requests[1].data.grantId, grant.id);
  requests[1].success({ code: 200 }); requests[1].complete(); assert.equal(state.grants.length, 0);
});