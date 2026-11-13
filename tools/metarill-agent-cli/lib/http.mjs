import { CliError } from './errors.mjs';

/** 禁止自动跟随重定向，避免认证头被发送到目录声明以外的位置。 */
export async function requestJson(url, { method = 'GET', token, data, form = false, timeoutMs = 30_000, confirm = false, capabilityId, requestId, secrets = [] } = {}) {
  const headers = { Accept: 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (confirm) headers['X-Yzp-Agent-Confirm'] = 'true';
  if (capabilityId) headers['X-Yzp-Agent-Capability'] = capabilityId;
  if (requestId) headers['X-Yzp-Agent-Request-Id'] = requestId;
  let body;
  if (data !== undefined) {
    headers['Content-Type'] = form ? 'application/x-www-form-urlencoded' : 'application/json';
    body = form ? new URLSearchParams(data).toString() : JSON.stringify(data);
  }
  let response;
  try {
    response = await fetch(url, { method, headers, body, redirect: 'manual', signal: AbortSignal.timeout(timeoutMs) });
  } catch (error) {
    const timedOut = ['TimeoutError', 'AbortError'].includes(error.name);
    throw new CliError(timedOut ? '请求超时；操作结果可能未知，请查询数据后再决定是否重试。' : '无法连接项目，请检查网络和项目地址。',
      timedOut ? 'request_timeout' : 'network_error');
  }
  if (response.status >= 300 && response.status < 400) throw new CliError('项目返回重定向，已停止请求；请使用最终 API 地址。', 'redirect_refused');
  let payload;
  try { payload = await response.json(); }
  catch (error) {
    if (['TimeoutError', 'AbortError'].includes(error.name)) throw new CliError('读取响应超时；操作结果可能未知，请查询数据后再决定是否重试。', 'request_timeout');
    throw new CliError('项目未返回有效 JSON。', 'invalid_response');
  }
  const sensitive = [token, ...secrets, data?.refresh_token, data?.device_code].filter((value) => typeof value === 'string' && value);
  return { status: response.status, ok: response.ok, payload,
    safeMessage: safeMessage(payload?.message ?? payload?.msg ?? payload?.error?.message, sensitive),
    agentError: agentErrorDetails(payload, sensitive) };
}

/** 仅接受可供恢复决策的稳定字段，拒绝回显任意服务端诊断或凭据。 */
function agentErrorDetails(payload, secrets) {
  const source = payload?.map?.agentError ?? (typeof payload?.error === 'object' ? payload.error : null);
  const details = {};
  if (typeof source?.code === 'string' && /^YZP_[A-Z0-9_]{1,80}$/.test(source.code)) details.code = source.code;
  const requestId = source?.requestId ?? payload?.map?.requestId;
  if (typeof requestId === 'string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(requestId)) details.requestId = requestId;
  if (['applied', 'not_applied', 'unknown'].includes(source?.outcome)) details.outcome = source.outcome;
  if (['query_task', 'read_back', 'fix_request'].includes(source?.recovery)) details.recovery = source.recovery;
  if (typeof source?.retryable === 'boolean') details.retryable = false;
  return redactValue(details, secrets);
}

/** 保留业务条件说明，并清除认证字段、令牌值及终端控制字符。 */
function safeMessage(value, secrets) {
  if (typeof value !== 'string') return '';
  let message = value;
  for (const secret of secrets) message = message.split(secret).join('[已隐藏]');
  return message.replace(/\bBearer\s+[^\s,;"']+/gi, 'Bearer [已隐藏]')
    .replace(/((?:access_token|refresh_token|device_code|authorization)["']?\s*[=:]\s*["']?)[^\s,;"']+/gi, '$1[已隐藏]')
    .replace(/[\u0000-\u001f\u007f]/g, ' ').slice(0, 500);
}

/** 即使业务响应意外回显认证值，stdout 也不输出本机持有的访问/刷新令牌。 */
export function redactValue(value, secrets) {
  if (typeof value === 'string') {
    let result = value;
    for (const secret of secrets) if (secret) result = result.split(secret).join('[已隐藏]');
    return result;
  }
  if (Array.isArray(value)) return value.map((entry) => redactValue(entry, secrets));
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, entry]) => [redactValue(key, secrets), redactValue(entry, secrets)]));
  return value;
}

/** 业务失败返回可定位的脱敏说明，认证响应原文不会直接输出。 */
export function unwrap(result) {
  if (!result.ok) throw new CliError(`项目请求失败（HTTP ${result.status}）。${result.safeMessage ?? ''}`, `http_${result.status}`, result.agentError);
  if (result.payload?.code !== 200 && result.payload?.code !== '200') {
    throw new CliError(result.safeMessage || '项目拒绝该操作或业务执行失败，请检查账号权限与请求参数。', 'business_error', result.agentError);
  }
  return result.payload.data;
}

/** 枚举有限 HTTP 方法；路径解码后仍必须在当前项目的 /admin/ 命名空间。 */
export function capabilityUrl(baseUrl, descriptor, pathValues = {}, queryValues = {}) {
  if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(descriptor.method)) throw new CliError('能力声明的 HTTP 方法不受支持。', 'invalid_capability');
  if (typeof descriptor.path !== 'string' || !descriptor.path.startsWith('/admin/') || descriptor.path.includes('\\') || descriptor.path.includes('?') || descriptor.path.includes('#')) {
    throw new CliError('能力路径必须是当前项目 /admin/ 下的相对路径。', 'invalid_capability');
  }
  let path = descriptor.path.replace(/\{([^}]+)\}/g, (_, name) => {
    const value = pathValues[name];
    if (value === undefined || value === null || value === '') throw new CliError(`缺少路径参数 ${name}。`, 'missing_parameter');
    return encodeURIComponent(String(value));
  });
  let decoded;
  try { decoded = decodeURIComponent(path); } catch { throw new CliError('能力路径编码无效。', 'invalid_capability'); }
  if (decoded.includes('\\') || decoded.split('/').some((part) => part === '.' || part === '..') || /%2f|%5c|%2e/i.test(decoded)) {
    throw new CliError('能力路径不能包含路径穿越或重复编码。', 'invalid_capability');
  }
  const base = new URL(baseUrl);
  const url = new URL(`${baseUrl}${path}`);
  if (url.origin !== base.origin || !url.pathname.startsWith(`${base.pathname}/admin/`)) throw new CliError('能力路径超出当前项目。', 'invalid_capability');
  for (const [name, value] of Object.entries(queryValues)) {
    if (value === undefined || value === null) continue;
    if (Array.isArray(value)) value.forEach((entry) => url.searchParams.append(name, String(entry)));
    else url.searchParams.set(name, String(value));
  }
  return url;
}
