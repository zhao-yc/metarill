import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, readFile, writeFile, rm, stat, mkdir } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawn, execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { main } from '../lib/cli.mjs';
import { ConfigStore } from '../lib/config.mjs';

const requestBody = [{ name: 'request', binding: 'body', required: true }];
const propertySchema = { type: 'object', properties: { id: { type: 'integer' }, name: { type: 'string' }, notes: { type: ['string', 'null'] } } };
const descriptors = [
  { id: 'page:42:list', name: '物品列表', method: 'POST', path: '/admin/auto/items/item/getList', parameters: requestBody, requestSchema: { type: 'object', properties: { pageNo: { type: 'integer' } } } },
  { id: 'page:42:detail', name: '物品详情', method: 'GET', path: '/admin/auto/items/item/{id}', parameters: [{ name: 'id', binding: 'path', required: true }, { name: 'parentId', binding: 'query' }] },
  { id: 'page:42:create', name: '新增物品', method: 'POST', path: '/admin/auto/items/item', parameters: requestBody, requestSchema: propertySchema },
  { id: 'page:42:update', name: '修改物品', method: 'PUT', path: '/admin/auto/items/item', parameters: requestBody, requestSchema: propertySchema },
  { id: 'page:42:delete', name: '删除物品', method: 'DELETE', path: '/admin/auto/items/item', parameters: [...requestBody, { name: 'parentId', binding: 'query' }], requestSchema: { type: 'object', properties: { id: { type: 'integer' } } }, confirmationRequired: true },
  { id: 'page:42:copy', name: '复制物品', method: 'POST', path: '/admin/auto/items/item/copy', parameters: requestBody, requestSchema: propertySchema },
  { id: 'action:preview', name: '预览处理结果', method: 'POST', path: '/admin/custom/items/preview', parameters: requestBody, requestSchema: propertySchema, confirmationRequired: false },
  { id: 'action:approve', name: '审批物品', method: 'POST', path: '/admin/custom/items/approve', parameters: requestBody, requestSchema: propertySchema, confirmationRequired: true, confirmationMessage: '确认审批？' }
].map((entry) => ({ ...entry, operation: entry.id.startsWith('page:') ? entry.id.split(':').at(-1) : 'action' }));

/** 本地真实 HTTP 模拟项目：分别验证 OAuth、目录权限与请求传输，不绕过 CLI 网络栈。 */
async function fixture(t) {
  const directory = await mkdtemp(join(tmpdir(), 'yzp-cli-test-'));
  const store = new ConfigStore(join(directory, 'config', 'config.json'));
  const state = { projectId: 9, projectName: '测试项目', accountId: 7, pending: 1, refused: false,
    tokenCount: 0, refreshCount: 0, accessToken: null, refreshToken: null, records: new Map(), nextId: 1,
    requests: [], tasks: new Map(), businessCalls: 0, catalogProjectId: null, descriptorOverride: null, businessError: null,
    businessDelayMs: 0, redirect: false, revoked: false, refreshError: false };
  const tokenResponse = () => {
    state.tokenCount++;
    state.accessToken = `test-access-secret-${state.tokenCount}`;
    state.refreshToken = `test-refresh-secret-${state.tokenCount}`;
    return { access_token: state.accessToken, refresh_token: state.refreshToken, token_type: 'Bearer', expires_in: 3600, project_id: state.projectId };
  };
  const server = createServer(async (request, response) => {
    try {
      const chunks = [];
      for await (const chunk of request) chunks.push(chunk);
      const text = Buffer.concat(chunks).toString('utf8');
      const url = new URL(request.url, state.baseUrl);
      const form = request.headers['content-type'] === 'application/x-www-form-urlencoded';
      const data = text ? form ? Object.fromEntries(new URLSearchParams(text)) : JSON.parse(text) : undefined;
      state.requests.push({ method: request.method, path: url.pathname, query: Object.fromEntries(url.searchParams), body: data, headers: request.headers });
      const send = (payload, status = 200) => {
        response.writeHead(status, { 'content-type': 'application/json' });
        response.end(JSON.stringify(payload));
      };
      const requestId = request.headers['x-yzp-agent-request-id'];
      const success = (data) => {
        if (requestId && state.tasks.has(requestId)) Object.assign(state.tasks.get(requestId), { status: 'succeeded', outcome: 'applied', httpStatus: 200, businessCode: 200 });
        send({ code: 200, data, ...(requestId ? { map: { requestId } } : {}) });
      };
      if (url.pathname === '/api/agent/oauth/device') {
        assert.equal(data.client_id, 'metarill-agent-cli');
        return send({ device_code: 'test-device-secret', user_code: 'ABCD-1234', verification_uri: `${state.baseUrl}/#/pages/metarill-agent/authorize`,
          verification_uri_complete: `${state.baseUrl}/#/pages/metarill-agent/authorize?user_code=ABCD-1234`, expires_in: state.deviceExpires ?? 10, interval: state.interval ?? 0.01 });
      }
      if (url.pathname === '/api/agent/oauth/token') {
        assert.equal(data.client_id, 'metarill-agent-cli');
        if (data.grant_type === 'refresh_token') {
          state.refreshCount++;
          assert.equal(data.refresh_token, state.refreshToken);
          if (state.refreshError) return send({ error: typeof state.refreshError === 'string' ? state.refreshError : 'invalid_grant', error_description: state.refreshToken }, 400);
          return send(tokenResponse());
        }
        assert.equal(data.grant_type, 'urn:ietf:params:oauth:grant-type:device_code');
        assert.equal(data.device_code, 'test-device-secret');
        if (state.refused) return send({ error: 'access_denied', error_description: 'test-device-secret' }, 400);
        // 真实HTTP返回明确业务错误；不以几十毫秒设备期限与本机网络调度竞争来模拟expired_token。
        if (state.deviceGrantErrors?.length) {
          state.deviceGrantErrorTimes.push(performance.now());
          return send({ error: state.deviceGrantErrors.shift() }, 400);
        }
        if (state.pending-- > 0) return send({ error: 'authorization_pending' }, 400);
        return send(tokenResponse());
      }
      if (request.headers.authorization !== `Bearer ${state.accessToken}` || state.revoked) return send({ code: 500, msg: '未授权' }, 401);
      if (url.pathname === '/api/admin/sys/agent/me') {
        if (state.revokeAfterMe) state.revoked = true;
        return success({ projectId: state.projectId, projectName: state.projectName, accountId: state.accountId, accountName: '测试账号', grantId: 'g1' });
      }
      if (url.pathname.startsWith('/api/admin/sys/agent/tasks/')) {
        const key = url.pathname.split('/').at(-1);
        const task = state.tasks.get(key);
        if (!task) return send({ code: 500, msg: '任务不存在或已过期，不能据此判断请求未执行，请回读业务数据。',
          map: { agentError: { code: 'YZP_NOT_FOUND', requestId: key, outcome: 'unknown', recovery: 'read_back', retryable: false } } }, 404);
        const { fingerprint, ...value } = task;
        return success(value);
      }
      if (url.pathname === '/api/admin/sys/agent/capabilities') return success({ protocolVersion: 1, projectId: state.catalogProjectId ?? state.projectId,
        projectName: state.projectName, capabilities: descriptors.map(({ id, name, method, path, confirmationRequired }) => ({ id, name, method, path, confirmationRequired })) });
      if (url.pathname.startsWith('/api/admin/sys/agent/capabilities/')) {
        const id = decodeURIComponent(url.pathname.slice('/api/admin/sys/agent/capabilities/'.length));
        return success(state.descriptorOverride ?? descriptors.find((entry) => entry.id === id));
      }
      if (url.pathname === '/api/admin/sys/agent/skill') return success({ content: '---\nname: metarill-agent\ndescription: 测试项目能力\n---\n\n# 测试项目\n' });
      if (url.pathname === '/api/admin/sys/agent/revoke') { state.revoked = true; return success(true); }
      if (requestId) {
        const fingerprint = JSON.stringify({ method: request.method, path: url.pathname, query: url.search, data, capabilityId: request.headers['x-yzp-agent-capability'] });
        const existing = state.tasks.get(requestId);
        if (existing) return send({ code: 500, msg: '请求标识已登记，请查询任务。', map: { requestId,
          agentError: { code: existing.fingerprint === fingerprint ? 'YZP_REQUEST_ALREADY_RECORDED' : 'YZP_CONFLICT',
            requestId, outcome: existing.outcome, recovery: 'query_task', retryable: false } } }, 409);
        state.tasks.set(requestId, { requestId, capabilityId: request.headers['x-yzp-agent-capability'], fingerprint,
          status: 'running', outcome: 'unknown', startedAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
          expiresAt: new Date(Date.now() + 86_400_000).toISOString(), retryable: false, recovery: 'query_task' });
      }
      state.businessCalls++;
      if (state.redirect) { response.writeHead(302, { location: `${state.baseUrl}/evil` }); response.end(); return; }
      if (state.businessDelayMs) await new Promise((done) => setTimeout(done, state.businessDelayMs));
      const id = request.headers['x-yzp-agent-capability'];
      assert.ok(descriptors.find((entry) => entry.id === id));
      if (state.structuredError) return send(state.structuredError, 409);
      if (state.businessError) return send({ code: 500, msg: `${state.businessError}; ${state.accessToken}; refresh_token=${state.refreshToken}` });
      if (id === 'page:42:create') {
        const record = { id: state.nextId++, ...data };
        state.records.set(record.id, record);
        return success(record);
      }
      if (id === 'page:42:list') return success({ records: [...state.records.values()], total: state.records.size });
      if (id === 'page:42:detail') return success(state.records.get(Number(url.pathname.split('/').pop())));
      if (id === 'page:42:update') {
        const record = { ...state.records.get(data.id), ...data };
        state.records.set(record.id, record);
        return success(record);
      }
      if (id === 'page:42:copy') {
        const record = { ...state.records.get(data.id), id: state.nextId++ };
        state.records.set(record.id, record);
        return success(record);
      }
      if (id === 'page:42:delete') {
        assert.equal(request.headers['x-yzp-agent-confirm'], 'true');
        state.records.delete(data.id);
        return success(true);
      }
      if (id === 'action:preview') return success({ id: data.id, preview: true });
      if (id === 'action:approve') {
        assert.equal(request.headers['x-yzp-agent-confirm'], 'true');
        return success({ id: data.id, approved: true, ...(state.echoToken ? { diagnostic: state.accessToken } : {}) });
      }
      send({ code: 500, msg: '接口不存在' }, 404);
    } catch (error) {
      response.writeHead(500, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ code: 500, msg: String(error) }));
    }
  });
  await new Promise((done) => server.listen(0, '127.0.0.1', done));
  state.baseUrl = `http://127.0.0.1:${server.address().port}/api`;
  t.after(async () => {
    server.closeAllConnections();
    await new Promise((done) => server.close(done));
    await rm(directory, { recursive: true, force: true });
  });
  const invoke = async (...argv) => {
    let stdout = '', stderr = '';
    const code = await main(argv, { store, stdout: { write: (value) => { stdout += value; } }, stderr: { write: (value) => { stderr += value; } } });
    const lastLine = stderr.trim().split('\n').at(-1);
    return { code, stdout, stderr, value: stdout ? JSON.parse(stdout) : null, error: code && lastLine.startsWith('{') ? JSON.parse(lastLine) : null };
  };
  const authorize = async () => {
    const result = await invoke('auth', 'login', '--url', state.baseUrl, '--profile', 'demo');
    assert.equal(result.code, 0, result.stderr);
    return result;
  };
  return { state, directory, store, invoke, authorize };
}

test('设备授权、CRUD、复制、确认动作与撤销完整闭环', async (t) => {
  const { state, directory, invoke, authorize, store } = await fixture(t);
  const login = await authorize();
  assert.match(login.stderr, /user_code=ABCD-1234/);
  assert.doesNotMatch(login.stdout + login.stderr, /test-(?:access|refresh|device)-secret/);
  assert.equal(login.value.projectId, 9);
  assert.equal((await invoke('auth', 'status')).value.accountId, 7);
  assert.equal((await invoke('capabilities', 'list')).value.capabilities.length, 8);
  assert.equal((await invoke('capabilities', 'describe', 'page:42:update')).value.requestSchema.properties.notes.type[1], 'null');
  const create = await invoke('call', 'page:42:create', '--json', '{"name":"初始物品","notes":"待更新"}');
  assert.equal(create.code, 0, create.stderr);
  assert.equal(create.value.data.id, 1);
  assert.equal((await invoke('call', 'page:42:list', '--json', '{"pageNo":1}')).value.data.total, 1);
  assert.equal((await invoke('call', 'page:42:detail', '--id', '1', '--parent-id', '11')).value.data.name, '初始物品');
  const filename = join(directory, 'update.json');
  await writeFile(filename, '{"id":1,"notes":null}', 'utf8');
  const updated = await invoke('call', 'page:42:update', '--json', `@${filename}`);
  assert.deepEqual(updated.value.data, { id: 1, name: '初始物品', notes: null });
  assert.equal((await invoke('call', 'page:42:copy', '--json', '{"id":1}')).value.data.id, 2);
  const before = state.businessCalls;
  assert.equal((await invoke('call', 'action:approve', '--json', '{"id":1}')).error.error, 'confirmation_required');
  assert.equal(state.businessCalls, before);
  assert.deepEqual((await invoke('call', 'action:approve', '--json', '{"id":1}', '--confirm')).value.data, { id: 1, approved: true });
  assert.equal((await invoke('call', 'page:42:delete', '--json', '{"id":1,"parentId":11}', '--confirm')).code, 0);
  const deletion = state.requests.findLast((entry) => entry.method === 'DELETE');
  assert.deepEqual(deletion.body, { id: 1 });
  assert.deepEqual(deletion.query, { parentId: '11' });
  assert.equal(state.records.size, 1);
  assert.equal((await invoke('auth', 'logout')).code, 0);
  assert.equal(state.revoked, true);
  assert.equal((await store.read()).profiles.demo.credentials, undefined);
  assert.equal((await invoke('auth', 'status')).error.error, 'not_authorized');
});

test('授权拒绝、过期与 slow_down 不保存凭据', { timeout: 20_000 }, async (t) => {
  const { state, invoke, store } = await fixture(t);
  state.refused = true;
  assert.equal((await invoke('auth', 'login', '--url', state.baseUrl)).code, 1);
  assert.deepEqual((await store.read()).profiles, {});
  state.refused = false;
  state.deviceExpires = 30;
  state.deviceGrantErrors = ['expired_token'];
  state.deviceGrantErrorTimes = [];
  const expired = await invoke('auth', 'login', '--url', state.baseUrl);
  assert.equal(expired.code, 1);
  assert.match(expired.stderr, /expired_token/);
  assert.deepEqual((await store.read()).profiles, {});
  state.deviceGrantErrors = ['slow_down', 'expired_token'];
  state.deviceGrantErrorTimes = [];
  const before = state.requests.length;
  const slow = await invoke('auth', 'login', '--url', state.baseUrl);
  assert.equal(slow.code, 1);
  assert.match(slow.stderr, /expired_token/);
  assert.equal(state.requests.slice(before).filter((entry) => entry.path.endsWith('/token')).length, 2);
  // 仍实际等待原5秒退避；第二次服务端过期回应有充分HTTP期限，request_timeout由独立用例保留。
  assert.equal(state.deviceGrantErrorTimes.length, 2);
  assert.ok(state.deviceGrantErrorTimes[1] - state.deviceGrantErrorTimes[0] >= 5000);
  assert.deepEqual((await store.read()).profiles, {});
});

test('多项目配置绑定 API 路径，列表与真实进程输出不含令牌', async (t) => {
  const { state, invoke, authorize, store } = await fixture(t);
  await authorize();
  assert.equal((await invoke('profiles', 'add', 'second', '--url', `${state.baseUrl}/other`)).code, 0);
  assert.equal((await invoke('profiles', 'use', 'second')).code, 0);
  assert.equal((await invoke('auth', 'status')).error.error, 'not_authorized');
  assert.equal((await invoke('profiles', 'use', 'demo')).code, 0);
  const conflict = await invoke('auth', 'login', '--profile', 'demo', '--url', `${state.baseUrl}/other`);
  assert.match(conflict.stderr, /profile_url_conflict/);
  const bin = fileURLToPath(new URL('../bin/metarill-agent.mjs', import.meta.url));
  const processResult = await new Promise((done, reject) => {
    const child = spawn(process.execPath, [bin, 'profiles', 'list'], { env: { ...process.env, METARILL_AGENT_CONFIG: store.path }, windowsHide: true });
    let stdout = '', stderr = '';
    child.stdout.on('data', (data) => { stdout += data; });
    child.stderr.on('data', (data) => { stderr += data; });
    child.on('error', reject);
    child.on('close', (code) => done({ code, stdout, stderr }));
  });
  assert.equal(processResult.code, 0, processResult.stderr);
  assert.equal(JSON.parse(processResult.stdout).length, 2);
  assert.doesNotMatch(processResult.stdout, /test-(?:access|refresh)-secret/);
  assert.equal((await invoke('profiles', 'remove', 'second')).code, 0);
  const config = await store.read();
  config.profiles.demo.url += '/other';
  await store.write(config);
  assert.equal((await invoke('auth', 'status')).error.error, 'project_mismatch');
});

test('同域服务身份或能力目录项目变化时拒绝业务请求', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  state.projectId = 10;
  assert.equal((await invoke('call', 'page:42:create', '--json', '{}')).error.error, 'project_mismatch');
  state.projectId = 9;
  state.catalogProjectId = 10;
  assert.equal((await invoke('call', 'page:42:create', '--json', '{}')).error.error, 'invalid_catalog');
  assert.equal(state.businessCalls, 0);
});

test('即将过期时轮转刷新，两个并发请求只刷新一次', async (t) => {
  const { state, invoke, authorize, store } = await fixture(t);
  await authorize();
  const initial = await store.read();
  const oldRefreshToken = initial.profiles.demo.credentials.refreshToken;
  initial.profiles.demo.credentials.expiresAt = Date.now() - 1;
  await store.write(initial);
  const results = await Promise.all([invoke('auth', 'status'), invoke('capabilities', 'list')]);
  assert.ok(results.every((entry) => entry.code === 0), JSON.stringify(results));
  assert.equal(state.refreshCount, 1);
  const saved = await store.read();
  assert.notEqual(saved.profiles.demo.credentials.refreshToken, oldRefreshToken);
  assert.doesNotMatch(results.map((entry) => entry.stdout + entry.stderr).join(''), /test-(?:access|refresh)-secret/);
});

test('刷新授权失效时清除凭据并提示重新登录', async (t) => {
  const { state, invoke, authorize, store } = await fixture(t);
  await authorize();
  const config = await store.read();
  config.profiles.demo.credentials.expiresAt = Date.now() - 1;
  await store.write(config);
  state.refreshError = true;
  const result = await invoke('auth', 'status');
  assert.equal(result.error.error, 'invalid_grant');
  assert.match(result.stderr, /重新登录/);
  assert.doesNotMatch(result.stderr, /test-refresh-secret/);
  assert.equal((await store.read()).profiles.demo.credentials, undefined);
  // 兼容旧服务把失效账号返回为 invalid_token，仍只提示重登录，不重发请求。
  state.refreshError = 'invalid_token';
  await authorize();
  const oldServiceConfig = await store.read();
  oldServiceConfig.profiles.demo.credentials.expiresAt = Date.now() - 1;
  await store.write(oldServiceConfig);
  const oldService = await invoke('auth', 'status');
  assert.equal(oldService.error.error, 'invalid_token');
  assert.match(oldService.stderr, /重新登录/);
  assert.equal((await store.read()).profiles.demo.credentials, undefined);
});

test('能力路径、未登记能力和重定向拒绝，认证头不传其他地址', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  assert.equal((await invoke('call', 'action:missing', '--json', '{}')).error.error, 'capability_missing');
  const source = descriptors.find((entry) => entry.id === 'page:42:create');
  for (const path of ['https://outside.invalid/admin/create', '//outside.invalid/admin/create', '/admin/../public/create', '/admin/%252e%252e/public/create', '/admin\\outside', '/public/create']) {
    state.descriptorOverride = { ...source, path };
    assert.equal((await invoke('call', source.id, '--json', '{}')).error.error, 'invalid_capability', path);
  }
  assert.equal(state.businessCalls, 0);
  state.descriptorOverride = null;
  state.redirect = true;
  assert.equal((await invoke('call', source.id, '--json', '{}')).error.error, 'redirect_refused');
  assert.equal(state.requests.some((entry) => entry.path.endsWith('/evil')), false);
});

test('业务失败保留脱敏原因且写请求不重试', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  state.businessError = '记录只读，不能修改';
  const result = await invoke('call', 'page:42:update', '--json', '{"id":1,"notes":null}');
  assert.equal(result.code, 1);
  assert.equal(result.stdout, '');
  assert.match(result.stderr, /记录只读/);
  assert.doesNotMatch(result.stderr, /test-(?:access|refresh)-secret/);
  assert.equal(state.businessCalls, 1);
});

test('成功业务响应意外回显令牌时 stdout 仍隐藏认证值', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  state.echoToken = true;
  const result = await invoke('call', 'action:approve', '--json', '{"id":1}', '--confirm');
  assert.equal(result.code, 0, result.stderr);
  assert.doesNotMatch(result.stdout, /test-access-secret/);
  assert.equal(result.value.data.diagnostic, '[已隐藏]');
});

test('写请求超时后结果未知且不重试', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  state.businessDelayMs = 250;
  const result = await invoke('call', 'page:42:create', '--json', '{"name":"仅发送一次"}', '--timeout', '100');
  assert.equal(result.error.error, 'request_timeout', result.stderr);
  assert.equal(state.businessCalls, 1);
  assert.match(result.stderr, /结果可能未知/);
});

test('参数按真实绑定发送，DTO 的同名 id 不被误删', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  assert.equal((await invoke('call', 'page:42:detail')).error.error, 'missing_parameter');
  assert.equal((await invoke('call', 'page:42:detail', '--id', '1', '--param', 'unexpected=1')).error.error, 'unknown_parameter');
  assert.equal((await invoke('call', 'page:42:create')).error.error, 'missing_parameter');
  const detail = await invoke('call', 'page:42:detail', '--json', '{"id":1,"parentId":2}', '--id', '3');
  assert.equal(detail.code, 0, detail.stderr);
  const get = state.requests.findLast((entry) => entry.headers['x-yzp-agent-capability'] === 'page:42:detail');
  assert.equal(get.path.endsWith('/3'), true);
  assert.equal(get.query.parentId, '2');
  assert.equal(get.body, undefined);
  state.descriptorOverride = { ...descriptors.find((entry) => entry.id === 'page:42:update'), parameters: [...requestBody, { name: 'id', binding: 'query', required: true }] };
  assert.equal((await invoke('call', 'page:42:update', '--json', '{"id":8,"notes":null}')).code, 0);
  const update = state.requests.findLast((entry) => entry.method === 'PUT');
  assert.equal(update.query.id, '8');
  assert.deepEqual(update.body, { id: 8, notes: null });
});

test('预览保留实际参数及 null，不执行业务、不登记任务且不要求确认', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  const preview = await invoke('call', 'page:42:delete', '--json', '{"id":8,"parentId":11}', '--preview');
  assert.equal(preview.code, 0, preview.stderr);
  assert.equal(preview.value.preview, true);
  assert.equal(preview.value.executed, false);
  assert.equal(preview.value.method, 'DELETE');
  assert.equal(preview.value.path, '/admin/auto/items/item');
  assert.deepEqual(preview.value.query, { parentId: '11' });
  assert.deepEqual(preview.value.body, { id: 8 });
  assert.equal(preview.value.confirmation.required, true);
  assert.equal(preview.value.confirmation.provided, false);
  assert.match(preview.value.notice, /不是服务端状态锁或批准票据/);
  const update = await invoke('call', 'page:42:update', '--json', '{"id":8,"notes":null}', '--preview');
  assert.deepEqual(update.value.body, { id: 8, notes: null });
  const detail = await invoke('call', 'page:42:detail', '--id', '8', '--parent-id', '11', '--preview');
  assert.equal(detail.value.path, '/admin/auto/items/item/8');
  assert.equal(Object.hasOwn(detail.value, 'body'), false);
  const emptyQuery = await invoke('call', 'page:42:detail', '--json', '{"id":8,"parentId":null}', '--preview');
  assert.deepEqual(emptyQuery.value.query, {});
  assert.equal(state.businessCalls, 0);
  assert.equal(state.tasks.size, 0);
  assert.equal(preview.stderr + update.stderr + detail.stderr, '');
});

test('只读 POST 不登记任务，写标识在校验完成后输出且原 R 响应不变', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  const invalid = await invoke('call', 'page:42:create');
  assert.equal(invalid.error.error, 'missing_parameter');
  assert.doesNotMatch(invalid.stderr, /request_started/);
  assert.equal((await invoke('call', 'page:42:create', '--json', '{}', '--request-id', 'not-a-uuid')).error.error, 'invalid_argument');
  const list = await invoke('call', 'page:42:list', '--json', '{}');
  assert.equal(list.code, 0, list.stderr);
  assert.equal(list.stderr, '');
  assert.equal(state.requests.at(-1).headers['x-yzp-agent-request-id'], undefined);
  assert.equal((await invoke('call', 'page:42:list', '--json', '{}', '--request-id', 'aaaaaaaa-1111-4222-8333-bbbbbbbbbbbb')).error.error, 'invalid_argument');
  const write = await invoke('call', 'page:42:create', '--json', '{"name":"合同"}');
  assert.equal(write.value.code, 200);
  const started = JSON.parse(write.stderr);
  assert.equal(started.event, 'request_started');
  assert.equal(started.requestId, write.value.map.requestId);
  assert.equal(state.requests.at(-1).headers['x-yzp-agent-request-id'], started.requestId);
  assert.equal(state.tasks.size, 1);
});

test('重复请求标识不重做，冲突结构透传并可查询原任务', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  const requestId = 'aaaaaaaa-1111-4222-8333-bbbbbbbbbbbb';
  const args = ['call', 'page:42:create', '--json', '{"name":"合同"}', '--request-id', requestId];
  assert.equal((await invoke(...args)).code, 0);
  const repeated = await invoke(...args);
  assert.equal(repeated.error.error, 'http_409');
  assert.equal(repeated.error.code, 'YZP_REQUEST_ALREADY_RECORDED');
  assert.equal(repeated.error.requestId, requestId);
  assert.equal(repeated.error.outcome, 'applied');
  assert.equal(repeated.error.recovery, 'query_task');
  assert.equal(repeated.error.retryable, false);
  assert.match(repeated.error.taskQuery, /tasks show/);
  const changed = await invoke('call', 'page:42:create', '--json', '{"name":"另一合同"}', '--request-id', requestId);
  assert.equal(changed.error.code, 'YZP_CONFLICT');
  assert.equal(state.businessCalls, 1);
  const task = await invoke('tasks', 'show', requestId);
  assert.equal(task.code, 0, task.stderr);
  assert.equal(task.value.status, 'succeeded');
  assert.equal(task.value.outcome, 'applied');
  assert.equal(task.value.requestId, requestId);
  assert.equal(state.requests.at(-2).path, '/api/admin/sys/agent/me');
});

test('写请求超时携带恢复标识，先查询任务再回读且不自动重发', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  state.businessDelayMs = 250;
  const write = await invoke('call', 'page:42:create', '--json', '{"name":"仅创建一次"}', '--timeout', '100');
  assert.equal(write.error.error, 'request_timeout');
  assert.equal(write.error.outcome, 'unknown');
  assert.equal(write.error.retryable, false);
  assert.equal(write.error.recovery, 'query_task');
  const requestId = write.error.requestId;
  assert.equal(JSON.parse(write.stderr.split('\n')[0]).requestId, requestId);
  const running = await invoke('tasks', 'show', requestId);
  assert.equal(running.value.status, 'running');
  await new Promise((done) => setTimeout(done, 260));
  const finished = await invoke('tasks', 'show', requestId);
  assert.equal(finished.value.status, 'succeeded');
  assert.equal(state.businessCalls, 1);
  state.businessDelayMs = 0;
  const readBack = await invoke('call', 'page:42:detail', '--id', '1');
  assert.equal(readBack.value.data.name, '仅创建一次');
  assert.equal(state.records.size, 1);
});

test('任务查询不会绕过撤权，未找到任务也不推断未执行', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  const requestId = 'aaaaaaaa-1111-4222-8333-bbbbbbbbbbbb';
  const missing = await invoke('tasks', 'show', requestId);
  assert.equal(missing.error.error, 'http_404');
  assert.equal(missing.error.code, 'YZP_NOT_FOUND');
  assert.equal(missing.error.outcome, 'unknown');
  assert.equal(missing.error.recovery, 'read_back');
  assert.match(missing.error.message, /不能据此判断请求未执行/);
  await invoke('call', 'page:42:create', '--json', '{}', '--request-id', requestId);
  state.revokeAfterMe = true;
  const revoked = await invoke('tasks', 'show', requestId);
  assert.equal(revoked.error.error, 'http_401');
  assert.equal(state.requests.at(-1).path, `/api/admin/sys/agent/tasks/${requestId}`);
  assert.equal(revoked.stdout, '');
});

test('HTTP顶层结构化错误只保留白名单字段并脱敏', async (t) => {
  const { state, invoke, authorize } = await fixture(t);
  await authorize();
  state.structuredError = { error: { code: 'YZP_CONFLICT', message: `冲突 ${state.accessToken}`, outcome: 'not_applied',
    recovery: 'fix_request', retryable: true, token: state.accessToken, internalStack: '不能输出' } };
  const result = await invoke('call', 'page:42:update', '--json', '{"id":1,"notes":null}');
  assert.equal(result.error.code, 'YZP_CONFLICT');
  assert.equal(result.error.outcome, 'not_applied');
  assert.equal(result.error.recovery, 'fix_request');
  assert.equal(result.error.retryable, false);
  assert.equal(Object.hasOwn(result.error, 'token'), false);
  assert.equal(Object.hasOwn(result.error, 'internalStack'), false);
  assert.doesNotMatch(result.stderr, /test-access-secret|不能输出/);
});

test('配置限制当前用户权限且不改变已有宿主目录权限', async (t) => {
  const { state, invoke, store, directory } = await fixture(t);
  assert.equal((await invoke('profiles', 'add', 'demo', '--url', state.baseUrl)).code, 0);
  if (process.platform === 'win32') {
    const acl = execFileSync('icacls.exe', [store.path], { encoding: 'utf8', windowsHide: true });
    assert.doesNotMatch(acl, /\(I\)/);
  } else {
    assert.equal((await stat(store.path)).mode & 0o777, 0o600);
    assert.equal((await stat(join(directory, 'config'))).mode & 0o777, 0o700);
    const shared = join(directory, 'shared');
    await mkdir(shared, { mode: 0o755 });
    const before = (await stat(shared)).mode;
    const customStore = new ConfigStore(join(shared, 'custom.json'));
    await customStore.write({ version: 1, activeProfile: null, profiles: {} });
    assert.equal((await stat(shared)).mode, before);
  }
});

test('Skill 安装仅写所选技能目录，覆盖需显式指定', async (t) => {
  const { directory, invoke, authorize } = await fixture(t);
  const root = join(directory, 'skills');
  const bundled = await invoke('skill', 'install', '--dir', root);
  assert.equal(bundled.code, 0, bundled.stderr);
  const target = join(root, 'metarill-agent', 'SKILL.md');
  assert.match(await readFile(target, 'utf8'), /name: metarill-agent/);
  assert.equal((await invoke('skill', 'install', '--dir', root)).error.error, 'skill_exists');
  await authorize();
  assert.equal((await invoke('skill', 'install', '--dir', root, '--project', '--force')).code, 0);
  assert.match(await readFile(target, 'utf8'), /# 测试项目/);
});

/** 服务端预览动作与CLI本地--preview分开：前者调用业务并登记任务，后者只输出拟发送请求。 */
test('无需确认的业务预览仍携带任务编号并可查询，CLI本地预览不执行业务', async (t) => {
  const { state, invoke, authorize } = await fixture(t); await authorize();
  const local = await invoke('call', 'action:preview', '--json', '{"id":1}', '--preview');
  assert.equal(local.code, 0, local.stderr); assert.equal(local.value.executed, false); assert.equal(local.value.confirmation.required, false);
  assert.equal(state.businessCalls, 0); assert.equal(state.tasks.size, 0);
  const result = await invoke('call', 'action:preview', '--json', '{"id":1}');
  assert.equal(result.code, 0, result.stderr); assert.deepEqual(result.value.data, { id: 1, preview: true });
  const started = JSON.parse(result.stderr.split('\n')[0]); assert.equal(started.event, 'request_started');
  const sent = state.requests.findLast((request) => request.path === '/api/admin/custom/items/preview');
  assert.equal(sent.headers['x-yzp-agent-request-id'], started.requestId); assert.notEqual(sent.headers['x-yzp-agent-confirm'], 'true');
  const task = await invoke('tasks', 'show', started.requestId); assert.equal(task.value.status, 'succeeded'); assert.equal(state.businessCalls, 1);
});
