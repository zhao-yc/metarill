import { readFile, mkdir, writeFile, lstat } from 'node:fs/promises';
import { resolve, join, dirname } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import { ConfigStore, normalizeBaseUrl, publicProfile, assertProfileName } from './config.mjs';
import { requestJson, unwrap, capabilityUrl, redactValue } from './http.mjs';
import { CliError } from './errors.mjs';

const DEVICE_GRANT = 'urn:ietf:params:oauth:grant-type:device_code';
const sleep = (ms) => new Promise((done) => setTimeout(done, ms));
const DEFAULT_TIMEOUT = 30_000;
const READ_OPERATIONS = new Set(['list', 'detail', 'count', 'options']);
const REQUEST_ID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const HELP = `Metarill 项目 Agent CLI（Node.js 20+）
用法：metarill-agent <命令> [--profile 名称] [--timeout 毫秒]

  auth login --url https://项目域名/api [--profile 名称] [--name 设备名称]
  auth status | auth logout [--local-only]
  profiles add 名称 --url 地址 | profiles list | profiles use 名称 | profiles remove 名称
  capabilities list | capabilities describe 能力标识
  call 能力标识 [--json JSON或@文件] [--id 值] [--parent-id 值] [--param 名称=值] [--preview] [--confirm] [--request-id UUID]
  tasks show 请求UUID
  skill install [--dir 技能父目录] [--project] [--force]

stdout 输出 JSON；授权链接、提示和错误写到 stderr。默认配置：~/.metarill-agent/config.json
可用 METARILL_AGENT_CONFIG 指定独立配置文件。写操作不会自动重试。`;

/** 只解析明确支持的选项，避免拼写错误悄悄改变调用行为。 */
function parseArgs(argv) {
  const positional = [];
  const options = { param: [] };
  const values = new Set(['profile', 'url', 'name', 'json', 'id', 'parent-id', 'param', 'timeout', 'dir', 'request-id']);
  const flags = new Set(['confirm', 'local-only', 'project', 'force', 'help', 'preview']);
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (!argument.startsWith('--')) { positional.push(argument); continue; }
    const equals = argument.indexOf('=');
    const name = argument.slice(2, equals < 0 ? undefined : equals);
    if (flags.has(name)) {
      if (equals >= 0) throw new CliError(`选项 --${name} 不需要值。`, 'invalid_argument');
      options[name] = true;
    } else if (values.has(name)) {
      const value = equals < 0 ? argv[++index] : argument.slice(equals + 1);
      if (value === undefined || value.startsWith('--')) throw new CliError(`选项 --${name} 缺少值。`, 'invalid_argument');
      if (name === 'param') options.param.push(value);
      else if (options[name] !== undefined) throw new CliError(`选项 --${name} 不能重复。`, 'invalid_argument');
      else options[name] = value;
    } else throw new CliError('存在未支持的命令选项，请运行 metarill-agent --help。', 'invalid_argument');
  }
  if (options.timeout !== undefined) {
    options.timeout = Number(options.timeout);
    if (!Number.isSafeInteger(options.timeout) || options.timeout < 1 || options.timeout > 300_000) {
      throw new CliError('请求超时必须是 1–300000 毫秒的整数。', 'invalid_argument');
    }
  }
  return { positional, options };
}

/** 每条命令限定选项，尤其避免在写调用中忽略无效参数。 */
function allowOptions(options, allowed) {
  const names = new Set(['profile', 'timeout', ...allowed]);
  for (const [name, value] of Object.entries(options)) {
    if (name === 'param' && value.length === 0) continue;
    if (!names.has(name)) throw new CliError(`本命令不支持 --${name}。`, 'invalid_argument');
  }
}

function selectProfile(config, name) {
  const selected = name ?? config.activeProfile;
  const profile = selected && Object.hasOwn(config.profiles, selected) ? config.profiles[selected] : null;
  if (!profile) throw new CliError('未找到项目配置，请先运行 auth login --url 或 profiles add。', 'profile_missing');
  if (normalizeBaseUrl(profile.url) !== profile.url) throw new CliError('配置中的项目地址无效。', 'invalid_config');
  return { name: selected, profile };
}

/** 校验 OAuth 返回值后再保存，任何输出都不直接回显 token 响应。 */
function credentialsFrom(payload, baseUrl, expectedProjectId) {
  if (typeof payload?.access_token !== 'string' || !payload.access_token || /[\r\n]/.test(payload.access_token)
      || typeof payload.refresh_token !== 'string' || !payload.refresh_token
      || String(payload.token_type).toLowerCase() !== 'bearer'
      || !Number.isFinite(Number(payload.expires_in)) || Number(payload.expires_in) <= 0
      || payload.project_id === undefined || payload.project_id === null) {
    throw new CliError('项目返回了无效的授权令牌。', 'invalid_token_response');
  }
  if (expectedProjectId !== undefined && String(expectedProjectId) !== String(payload.project_id)) {
    throw new CliError('授权令牌不属于当前项目，已拒绝保存。', 'project_mismatch');
  }
  return { accessToken: payload.access_token, refreshToken: payload.refresh_token,
    expiresAt: Date.now() + Number(payload.expires_in) * 1000, boundUrl: baseUrl, projectId: payload.project_id };
}

function oauthError(result) {
  const code = result.payload?.error;
  const messages = {
    access_denied: '用户拒绝了授权。', expired_token: '设备授权已过期，请重新登录。',
    invalid_grant: '授权已失效或被撤销，请重新登录。',
    invalid_token: '授权账号或令牌已失效，请重新登录。',
    invalid_client: '项目不接受当前 CLI 客户端。', unsupported_grant_type: '项目不支持此授权方式。',
    invalid_request: '项目拒绝了授权请求，请检查客户端和项目配置。'
  };
  return new CliError(messages[code] ?? `授权请求失败（HTTP ${result.status}）。`, typeof code === 'string' && Object.hasOwn(messages, code) ? code : 'oauth_error');
}

/** 操作前刷新；请求发出后即使认证失败也不自动重发，防止重复写入。 */
async function accessProfile(store, name, timeoutMs) {
  const selected = selectProfile(await store.read(), name);
  const check = (profile) => {
    if (!profile.credentials) throw new CliError('当前项目尚未授权，请运行 auth login。', 'not_authorized');
    if (profile.credentials.boundUrl !== profile.url || String(profile.credentials.projectId) !== String(profile.projectId)) {
      throw new CliError('凭据与项目地址或身份不一致，请重新登录。', 'project_mismatch');
    }
  };
  check(selected.profile);
  if (selected.profile.credentials.expiresAt > Date.now() + 15_000) return selected;
  return store.locked(async (config) => {
    const latest = selectProfile(config, selected.name);
    check(latest.profile);
    if (latest.profile.url !== selected.profile.url) throw new CliError('项目配置在请求期间变化，请重新执行命令。', 'profile_changed');
    if (latest.profile.credentials.expiresAt <= Date.now() + 15_000) {
      const result = await requestJson(`${latest.profile.url}/agent/oauth/token`, { method: 'POST', form: true, timeoutMs,
        data: { grant_type: 'refresh_token', client_id: 'metarill-agent-cli', refresh_token: latest.profile.credentials.refreshToken } });
      if (!result.ok || result.payload?.error) {
        if (['invalid_grant', 'invalid_token', 'access_denied'].includes(result.payload?.error)) {
          delete latest.profile.credentials;
          await store.write(config);
        }
        throw oauthError(result);
      }
      latest.profile.credentials = credentialsFrom(result.payload, latest.profile.url, latest.profile.projectId);
      await store.write(config);
    }
    return latest;
  });
}

async function authenticated(store, name, timeoutMs, path, extra = {}) {
  const selected = await accessProfile(store, name, timeoutMs);
  const result = await requestJson(`${selected.profile.url}${path}`, { timeoutMs,
    token: selected.profile.credentials.accessToken, secrets: [selected.profile.credentials.refreshToken], ...extra });
  return { selected, result };
}

/** 向已绑定 URL 获取身份，拒绝同域不同项目或服务切换后的能力目录。 */
async function identity(store, name, timeoutMs) {
  const { selected, result } = await authenticated(store, name, timeoutMs, '/admin/sys/agent/me');
  const me = unwrap(result);
  if (!me || String(me.projectId) !== String(selected.profile.projectId)) throw new CliError('当前服务的项目身份已变化，请重新登录。', 'project_mismatch');
  return { selected, me };
}

async function login(store, options, output, log) {
  allowOptions(options, ['url', 'name']);
  const initial = await store.read();
  const name = options.profile ?? initial.activeProfile ?? 'default';
  assertProfileName(name);
  const current = Object.hasOwn(initial.profiles, name) ? initial.profiles[name] : null;
  const url = normalizeBaseUrl(options.url ?? current?.url ?? '');
  if (current && current.url !== url) throw new CliError('该配置已绑定其他地址，请使用不同 --profile 名称。', 'profile_url_conflict');
  const timeoutMs = options.timeout ?? DEFAULT_TIMEOUT;
  const device = await requestJson(`${url}/agent/oauth/device`, { method: 'POST', form: true, timeoutMs,
    data: { client_id: 'metarill-agent-cli', client_name: options.name ?? 'Metarill Agent CLI' } });
  if (!device.ok || device.payload?.error) throw oauthError(device);
  const grant = device.payload;
  if (!grant || typeof grant.device_code !== 'string' || typeof grant.user_code !== 'string'
      || typeof grant.verification_uri !== 'string' || !Number.isFinite(Number(grant.expires_in)) || Number(grant.expires_in) <= 0) {
    throw new CliError('项目返回的设备授权信息不完整。', 'invalid_device_response');
  }
  const verification = grant.verification_uri_complete ?? grant.verification_uri;
  let verificationUrl;
  try { verificationUrl = new URL(verification); } catch { throw new CliError('项目返回的授权网址无效。', 'invalid_device_response'); }
  if (!['http:', 'https:'].includes(verificationUrl.protocol) || verificationUrl.username || verificationUrl.password || /[\r\n]/.test(verification)) {
    throw new CliError('项目返回的授权网址无效。', 'invalid_device_response');
  }
  // 直接提供服务端返回的浏览器授权 URL；CLI 不启动本地回调服务器。
  log(`请在浏览器打开并确认授权：${verification}\n用户码：${grant.user_code}\n等待网页授权，可按 Ctrl+C 取消。`);
  const deadline = Date.now() + Number(grant.expires_in) * 1000;
  let intervalMs = (grant.interval === undefined ? 5 : Number(grant.interval)) * 1000;
  if (!Number.isFinite(intervalMs) || intervalMs <= 0) throw new CliError('项目返回的授权轮询间隔无效。', 'invalid_device_response');
  let tokens;
  while (Date.now() < deadline) {
    await sleep(Math.min(intervalMs, Math.max(0, deadline - Date.now())));
    if (Date.now() >= deadline) break;
    const result = await requestJson(`${url}/agent/oauth/token`, { method: 'POST', form: true,
      timeoutMs: Math.min(timeoutMs, Math.max(1, deadline - Date.now())),
      data: { grant_type: DEVICE_GRANT, client_id: 'metarill-agent-cli', device_code: grant.device_code } });
    if (result.payload?.error === 'authorization_pending') continue;
    if (result.payload?.error === 'slow_down') { intervalMs += 5000; continue; }
    if (!result.ok || result.payload?.error) throw oauthError(result);
    tokens = credentialsFrom(result.payload, url);
    break;
  }
  if (!tokens) throw new CliError('设备授权已过期，请重新登录。', 'expired_token');
  const me = unwrap(await requestJson(`${url}/admin/sys/agent/me`, { token: tokens.accessToken, timeoutMs }));
  if (!me || String(me.projectId) !== String(tokens.projectId)) throw new CliError('授权令牌与项目身份不一致，已拒绝保存。', 'project_mismatch');
  await store.locked(async (config) => {
    if (Object.hasOwn(config.profiles, name) && config.profiles[name].url !== url) throw new CliError('项目配置在授权期间变化，请重新登录。', 'profile_changed');
    config.profiles[name] = { url, projectId: me.projectId, projectName: me.projectName,
      accountId: me.accountId, accountName: me.accountName, credentials: tokens };
    config.activeProfile = name;
    await store.write(config);
  });
  output({ ...publicProfile(name, { url, projectId: me.projectId, projectName: me.projectName, credentials: tokens }, true), accountId: me.accountId, accountName: me.accountName });
}

async function profiles(store, positional, options, output) {
  allowOptions(options, positional[1] === 'add' ? ['url'] : []);
  const operation = positional[1];
  if (operation === 'list' && positional.length === 2) {
    const config = await store.read();
    output(Object.entries(config.profiles).map(([name, profile]) => publicProfile(name, profile, config.activeProfile === name)));
    return;
  }
  if (!['add', 'use', 'remove'].includes(operation) || positional.length !== 3) throw new CliError('配置命令无效，请运行 metarill-agent --help。', 'invalid_argument');
  const name = positional[2];
  assertProfileName(name);
  await store.locked(async (config) => {
    if (operation === 'add') {
      if (Object.hasOwn(config.profiles, name)) throw new CliError('该配置已存在，请选择其他名称。', 'profile_exists');
      config.profiles[name] = { url: normalizeBaseUrl(options.url ?? '') };
      config.activeProfile ??= name;
    } else {
      if (!Object.hasOwn(config.profiles, name)) throw new CliError('未找到指定项目配置。', 'profile_missing');
      if (operation === 'use') config.activeProfile = name;
      else { delete config.profiles[name]; if (config.activeProfile === name) config.activeProfile = Object.keys(config.profiles)[0] ?? null; }
    }
    await store.write(config);
    output(operation === 'remove' ? { removed: name, activeProfile: config.activeProfile } : publicProfile(name, config.profiles[name], config.activeProfile === name));
  });
}

async function catalog(store, profileName, timeoutMs) {
  const { selected } = await identity(store, profileName, timeoutMs);
  const { result } = await authenticated(store, selected.name, timeoutMs, '/admin/sys/agent/capabilities');
  const value = unwrap(result);
  if (!value || value.protocolVersion !== 1 || !Array.isArray(value.capabilities) || String(value.projectId) !== String(selected.profile.projectId)) {
    throw new CliError('能力目录协议或项目身份不匹配。', 'invalid_catalog');
  }
  return { selected, value };
}

/** 先读当前账号目录，再读取选中能力详情；不从用户参数构造任意业务 URL。 */
async function describe(store, profileName, timeoutMs, capabilityId) {
  const { selected, value } = await catalog(store, profileName, timeoutMs);
  if (!value.capabilities.some((entry) => entry.id === capabilityId)) throw new CliError('当前账号能力目录中不存在该能力。', 'capability_missing');
  const { result } = await authenticated(store, selected.name, timeoutMs,
    `/admin/sys/agent/capabilities/${encodeURIComponent(capabilityId)}`);
  const descriptor = unwrap(result);
  if (!descriptor || descriptor.id !== capabilityId || !Array.isArray(descriptor.parameters ?? [])) {
    throw new CliError('能力详情与请求标识不一致。', 'invalid_capability');
  }
  return { selected, descriptor };
}

async function readInput(value) {
  if (value === undefined) return undefined;
  try {
    const source = value.startsWith('@') ? await readFile(resolve(value.slice(1)), 'utf8') : value;
    return JSON.parse(source);
  } catch { throw new CliError('请求 JSON 无效或文件不可读取。', 'invalid_json'); }
}

/** 路径、查询和 JSON 体分开传递，JSON 内的 null 与字段存在性保持原样。 */
async function call(store, positional, options, output, log) {
  allowOptions(options, ['json', 'id', 'parent-id', 'param', 'confirm', 'preview', 'request-id']);
  if (positional.length !== 2) throw new CliError('call 需要一个能力标识。', 'invalid_argument');
  if (options['request-id'] !== undefined && !REQUEST_ID.test(options['request-id'])) throw new CliError('--request-id 必须是 UUID。', 'invalid_argument');
  const timeoutMs = options.timeout ?? DEFAULT_TIMEOUT;
  const { selected, descriptor } = await describe(store, options.profile, timeoutMs, positional[1]);
  const writing = !READ_OPERATIONS.has(descriptor.operation);
  if (!writing && options['request-id']) throw new CliError('只读能力不登记写任务，不能使用 --request-id。', 'invalid_argument');
  const input = await readInput(options.json);
  const supplied = Object.create(null);
  if (options.id !== undefined) supplied.id = options.id;
  if (options['parent-id'] !== undefined) supplied.parentId = options['parent-id'];
  for (const parameter of options.param) {
    const separator = parameter.indexOf('=');
    if (separator <= 0) throw new CliError('--param 必须采用 名称=值 格式。', 'invalid_argument');
    const name = parameter.slice(0, separator);
    if (Object.hasOwn(supplied, name)) throw new CliError(`参数 ${name} 重复。`, 'invalid_argument');
    supplied[name] = parameter.slice(separator + 1);
  }
  const pathValues = Object.create(null);
  const queryValues = Object.create(null);
  const known = new Set();
  const bodyParameters = [];
  for (const parameter of descriptor.parameters ?? []) {
    if (!parameter || typeof parameter.name !== 'string' || !['path', 'query', 'body'].includes(parameter.binding)) {
      throw new CliError('能力包含无效的参数绑定。', 'invalid_capability');
    }
    if (parameter.binding === 'body') { bodyParameters.push(parameter); continue; }
    known.add(parameter.name);
    const value = supplied[parameter.name] ?? (input && typeof input === 'object' && Object.hasOwn(input, parameter.name) ? input[parameter.name] : undefined);
    if (parameter.required && (value === undefined || value === null || value === '')) throw new CliError(`缺少参数 ${parameter.name}。`, 'missing_parameter');
    (parameter.binding === 'path' ? pathValues : queryValues)[parameter.name] = value;
  }
  for (const name of Object.keys(supplied)) if (!known.has(name)) throw new CliError(`能力未声明参数 ${name}。`, 'unknown_parameter');
  const url = capabilityUrl(selected.profile.url, descriptor, pathValues, queryValues);
  const latest = await accessProfile(store, selected.name, timeoutMs);
  if (latest.profile.url !== selected.profile.url || String(latest.profile.projectId) !== String(selected.profile.projectId)) {
    throw new CliError('项目配置在调用期间变化，已停止请求。', 'profile_changed');
  }
  if (descriptor.method === 'GET' && input !== undefined) {
    if (!input || Array.isArray(input) || typeof input !== 'object' || Object.keys(input).some((name) => !known.has(name))) {
      throw new CliError('GET 的 --json 仅允许能力声明的路径和查询参数。', 'invalid_json');
    }
  }
  if (bodyParameters.some((parameter) => parameter.required) && input === undefined) throw new CliError('该能力需要 --json 请求体。', 'missing_parameter');
  let body = input;
  if (input && !Array.isArray(input) && typeof input === 'object') {
    body = { ...input };
    // 参数可以同名属于 DTO 与 URL；仅去掉 schema 未声明的 URL 参数。
    for (const name of known) {
      if (!Object.hasOwn(descriptor.requestSchema?.properties ?? {}, name)) delete body[name];
    }
  }
  if (options.preview) {
    // 展示已编码成 URL 的实际查询参数，避免把未发送的 null 显示为请求内容。
    const query = Object.create(null);
    for (const [name, value] of url.searchParams) query[name] = Object.hasOwn(query, name) ? [].concat(query[name], value) : value;
    output({ schemaVersion: 1, preview: true, executed: false, projectId: selected.profile.projectId,
      capabilityId: descriptor.id, name: descriptor.name, operation: descriptor.operation, method: descriptor.method,
      path: url.pathname.slice(new URL(selected.profile.url).pathname.length), query,
      ...(options['request-id'] ? { requestId: options['request-id'].toLowerCase() } : {}),
      ...(descriptor.method === 'GET' ? {} : { body }),
      confirmation: { required: !!descriptor.confirmationRequired, provided: !!options.confirm, message: descriptor.confirmationMessage ?? '' },
      notice: '仅预览拟发送请求，不调用业务接口、不登记任务；这不是服务端状态锁或批准票据，执行前仍需核对数据和授权。' });
    return;
  }
  if (descriptor.confirmationRequired && !options.confirm) throw new CliError('该能力需要确认，请核对能力详情和请求后添加 --confirm。', 'confirmation_required');
  // 完成参数校验后才分配请求标识；在请求发出前记录，响应丢失时仍可查询任务。
  const requestId = writing ? (options['request-id'] ?? randomUUID()).toLowerCase() : undefined;
  if (requestId) log(JSON.stringify({ event: 'request_started', requestId, capabilityId: descriptor.id }));
  try {
    const result = await requestJson(url, { method: descriptor.method, timeoutMs, token: latest.profile.credentials.accessToken,
      secrets: [latest.profile.credentials.refreshToken], capabilityId: descriptor.id, requestId, confirm: !!options.confirm,
      data: descriptor.method === 'GET' ? undefined : body });
    unwrap(result);
    output(result.payload);
  } catch (error) {
    if (requestId && error instanceof CliError) {
      error.details = { outcome: 'unknown', recovery: 'query_task', ...error.details, requestId, retryable: false,
        ...( ['request_timeout', 'network_error', 'invalid_response'].includes(error.code) ? { outcome: 'unknown', recovery: 'query_task' } : {}),
        taskQuery: `metarill-agent tasks show ${requestId} --profile ${selected.name}` };
      error.message += ' 写请求不会自动重发；请先查询任务并回读业务数据。';
    }
    throw error;
  }
}

/** 每次从当前授权项目查询；不以本机缓存替代服务端权限和撤权校验。 */
async function showTask(store, positional, options, output) {
  allowOptions(options, []);
  if (positional.length !== 3 || positional[1] !== 'show' || !REQUEST_ID.test(positional[2])) throw new CliError('用法：tasks show 请求UUID。', 'invalid_argument');
  const timeoutMs = options.timeout ?? DEFAULT_TIMEOUT;
  const { selected } = await identity(store, options.profile, timeoutMs);
  const { result } = await authenticated(store, selected.name, timeoutMs, `/admin/sys/agent/tasks/${positional[2].toLowerCase()}`);
  const task = unwrap(result);
  if (!task || task.requestId !== positional[2].toLowerCase() || !['running', 'succeeded', 'failed', 'unknown'].includes(task.status)
      || !['applied', 'not_applied', 'unknown'].includes(task.outcome)) throw new CliError('项目返回了无效的任务状态。', 'invalid_response');
  output(task);
}

async function installSkill(store, options, output) {
  allowOptions(options, ['dir', 'project', 'force']);
  let content;
  if (options.project) {
    await identity(store, options.profile, options.timeout ?? DEFAULT_TIMEOUT);
    const { result } = await authenticated(store, options.profile, options.timeout ?? DEFAULT_TIMEOUT, '/admin/sys/agent/skill');
    const value = unwrap(result);
    if (typeof value?.content !== 'string' || !value.content.startsWith('---')) throw new CliError('项目没有返回有效 Skill 内容。', 'invalid_skill');
    content = value.content;
  } else {
    content = await readFile(fileURLToPath(new URL('../skills/metarill-agent/SKILL.md', import.meta.url)), 'utf8');
  }
  const root = resolve(options.dir ?? join(process.env.CODEX_HOME ?? join(homedir(), '.codex'), 'skills'));
  const directory = join(root, 'metarill-agent');
  const target = join(directory, 'SKILL.md');
  try {
    const stat = await lstat(directory);
    if (!stat.isDirectory() || stat.isSymbolicLink()) throw new CliError('Skill 目录必须是普通目录。', 'invalid_skill_path');
  } catch (error) { if (error.code !== 'ENOENT') throw error; }
  try {
    const stat = await lstat(target);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new CliError('Skill 文件路径无效。', 'invalid_skill_path');
    if (!options.force) throw new CliError('Skill 已存在；核对内容后可使用 --force 更新。', 'skill_exists');
  } catch (error) { if (error.code !== 'ENOENT') throw error; }
  await mkdir(directory, { recursive: true });
  await writeFile(target, content, { flag: options.force ? 'w' : 'wx' });
  output({ installed: target, source: options.project ? 'project' : 'bundled' });
}

/** 可注入输出与配置存储以完成真实 HTTP 合约测试，入口仍使用同一实现。 */
export async function main(argv, { stdout = process.stdout, stderr = process.stderr, store = new ConfigStore() } = {}) {
  let outputValue;
  const output = (value) => { outputValue = value; };
  const log = (value) => stderr.write(`${value}\n`);
  try {
    const { positional, options } = parseArgs(argv);
    if (options.help || positional.length === 0) { stdout.write(`${JSON.stringify({ help: HELP }, null, 2)}\n`); return 0; }
    if (positional[0] === 'profiles') await profiles(store, positional, options, output);
    else if (positional[0] === 'auth' && positional.length === 2) {
      if (positional[1] === 'login') await login(store, options, output, log);
      else if (positional[1] === 'status') {
        allowOptions(options, []);
        const { selected, me } = await identity(store, options.profile, options.timeout ?? DEFAULT_TIMEOUT);
        output({ ...publicProfile(selected.name, selected.profile, true), accountId: me.accountId, accountName: me.accountName, grantId: me.grantId });
      } else if (positional[1] === 'logout') {
        allowOptions(options, ['local-only']);
        const selected = selectProfile(await store.read(), options.profile);
        if (selected.profile.credentials && !options['local-only']) {
          const { result } = await authenticated(store, selected.name, options.timeout ?? DEFAULT_TIMEOUT, '/admin/sys/agent/revoke', { method: 'POST', data: {} });
          unwrap(result);
        }
        await store.locked(async (config) => {
          const latest = selectProfile(config, selected.name);
          if (latest.profile.url !== selected.profile.url) throw new CliError('项目配置已变化，已停止退出操作。', 'profile_changed');
          delete latest.profile.credentials;
          await store.write(config);
        });
        output({ profile: selected.name, authorized: false, revoked: !options['local-only'] });
      } else throw new CliError('授权命令无效，请运行 metarill-agent --help。', 'invalid_argument');
    } else if (positional[0] === 'capabilities') {
      allowOptions(options, []);
      if (positional[1] === 'list' && positional.length === 2) output((await catalog(store, options.profile, options.timeout ?? DEFAULT_TIMEOUT)).value);
      else if (positional[1] === 'describe' && positional.length === 3) output((await describe(store, options.profile, options.timeout ?? DEFAULT_TIMEOUT, positional[2])).descriptor);
      else throw new CliError('能力命令无效，请运行 metarill-agent --help。', 'invalid_argument');
    } else if (positional[0] === 'call') await call(store, positional, options, output, log);
    else if (positional[0] === 'tasks') await showTask(store, positional, options, output);
    else if (positional[0] === 'skill' && positional[1] === 'install' && positional.length === 2) await installSkill(store, options, output);
    else throw new CliError('命令无效，请运行 metarill-agent --help。', 'invalid_argument');
    let secrets = [];
    try {
      const saved = await store.read();
      secrets = Object.values(saved.profiles).flatMap((profile) => [profile.credentials?.accessToken, profile.credentials?.refreshToken]).filter(Boolean);
    } catch { /* 无需授权的帮助/Skill 安装不因其他配置损坏而失败。 */ }
    stdout.write(`${JSON.stringify(redactValue(outputValue, secrets), null, 2)}\n`);
    return 0;
  } catch (error) {
    // 文件系统和网络原始错误可能带敏感内容，因此只展示经过约束的 CLI 错误。
    const safe = error instanceof CliError ? error : new CliError('操作失败，请检查文件访问权限、项目配置和网络。', 'unexpected_error');
    log(JSON.stringify({ error: safe.code, message: safe.message, ...safe.details }));
    return 1;
  }
}
