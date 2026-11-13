import test from 'node:test'
import assert from 'node:assert/strict'
import http from 'node:http'
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises'
import path from 'node:path'
import os from 'node:os'
import { validatePreview, createManagementPreview } from './serve-management-browser.mjs'

// 临时上游只验证传输保真，不代替真实Program、登录或数据库验收。
const config = (directory, frontend, backend) => ({ leaseId: 'a'.repeat(32), frontend: directory, frontendAddress: frontend, backendAddress: backend, expiresAt: new Date(Date.now() + 60000).toISOString() })
const listen = server => new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve('http://127.0.0.1:' + server.address().port)))
const close = server => new Promise(resolve => { server.closeAllConnections(); server.close(resolve) })

test('非回环、带凭据、过期及无期限配置均不能创建验收入口', () => {
 const valid = config(path.resolve('.'), 'http://127.0.0.1:5191', 'http://127.0.0.1:5080'); assert.ok(validatePreview(valid))
 for (const changed of [{ backendAddress: 'https://example.com' }, { frontendAddress: 'http://user:secret@127.0.0.1:5191' }, { expiresAt: new Date(0).toISOString() }, { expiresAt: new Date(Date.now() + 3600000).toISOString() }, { leaseId: 'old' }]) assert.throws(() => validatePreview({ ...valid, ...changed }))
})

test('静态产物与实际HTTP状态、请求头和正文分别保真，静态目录不越界', async () => {
 const directory = await mkdtemp(path.join(os.tmpdir(), 'metarill-browser-preview-')), root = path.join(directory, 'unpackage/dist/build/web')
 await mkdir(root, { recursive: true }); await writeFile(path.join(root, 'index.html'), '<main>原产物</main>'); await writeFile(path.join(directory, 'outside.txt'), '不可作为Web资源')
 const upstream = http.createServer(async (request, response) => { const chunks = []; for await (const chunk of request) chunks.push(chunk); response.writeHead(403, { 'content-type': 'application/json' }); response.end(JSON.stringify({ code: 403, headerKept: request.headers.loginstateid === 'transport-test-only', bodyKept: Buffer.concat(chunks).toString() === '{"value":null}' })) })
 let preview
 try {
  const backend = await listen(upstream); preview = await createManagementPreview(config(directory, 'http://127.0.0.1:5191', backend), path.join(directory, 'trace.jsonl')); const address = await listen(preview)
  const html = await fetch(address + '/index.html?version=original'); assert.equal(html.status, 200); assert.equal(await html.text(), '<main>原产物</main>')
  const api = await fetch(address + '/api/check?password=not-for-log', { method: 'POST', headers: { loginStateId: 'transport-test-only', 'content-type': 'application/json' }, body: '{"value":null}' }); assert.equal(api.status, 403); assert.deepEqual(await api.json(), { code: 403, headerKept: true, bodyKept: true })
  const escaped = await fetch(address + '/%2e%2e%2foutside.txt'); assert.equal(escaped.status, 403)
  // 等待异步日志落盘，只检查公开传输元信息，查询值、身份头和正文不进入日志。
  let trace = ''; for (let attempt = 0; attempt < 50 && !trace; attempt++) { try { trace = await readFile(path.join(directory, 'trace.jsonl'), 'utf8') } catch {} if (!trace) await new Promise(resolve => setTimeout(resolve, 10)) }
  assert.ok(trace); assert.ok(!trace.includes('transport-test-only') && !trace.includes('not-for-log') && !trace.includes('value'))
  const entry = JSON.parse(trace.trim()); assert.deepEqual(Object.keys(entry).sort(), ['at', 'method', 'pathname', 'status']); assert.equal(entry.pathname, '/api/check')
 } finally {
  if (preview) await close(preview); await close(upstream)
  const target = path.resolve(directory); assert.ok(target.startsWith(path.resolve(os.tmpdir()) + path.sep + 'metarill-browser-preview-')); await rm(target, { recursive: true })
 }
})
