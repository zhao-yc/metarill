import { spawn } from 'node:child_process'
import { readFile, rm, mkdir } from 'node:fs/promises'
import path from 'node:path'
import { randomUUID } from 'node:crypto'

// 仅在已初始化的隔离测试后端执行；不用于真实用户的自动授权。
const root = path.resolve(import.meta.dirname, '..')
const settingsFile = process.env.METARILL_TEST_SETTINGS
if (!settingsFile) throw new Error('请指定 METARILL_TEST_SETTINGS')
const settings = JSON.parse(await readFile(settingsFile, 'utf8')).Metarill
if (settings.Database.ExpectedDatabase !== 'metarill_test' || settings.BusinessDatabase.ExpectedDatabase !== 'metarill_business_test') throw new Error('只允许指定的隔离测试库')
const base = 'http://127.0.0.1:8088/api'
const directory = path.join(root, '.local', 'cli-smoke-' + randomUUID())
await mkdir(directory, { recursive: true })
const cliPath = path.join(root, 'tools/metarill-agent-cli/bin/metarill-agent.mjs')
const env = { ...process.env, METARILL_AGENT_CONFIG: path.join(directory, 'config.json') }
let recordId, loginProcess, accessToken, deleteCapability
async function request(route, data, headers = {}, form = false) {
  const response = await fetch(base + route, { method: data === undefined ? 'GET' : 'POST', headers: { ...headers, ...(data === undefined ? {} : { 'Content-Type': form ? 'application/x-www-form-urlencoded' : 'application/json' }) },
    body: data === undefined ? undefined : form ? new URLSearchParams(data) : JSON.stringify(data), redirect: 'error' })
  const value = await response.json()
  if (!response.ok || value.code !== undefined && Number(value.code) !== 200) throw new Error('测试接口未成功：' + route)
  return value
}
function run(args, onErrorText) {
  const child = spawn(process.execPath, [cliPath, ...args], { cwd: root, env, windowsHide: true })
  let out = '', error = ''
  child.stdout.on('data', bytes => { out += bytes.toString() })
  child.stderr.on('data', bytes => { error += bytes.toString(); onErrorText?.(error) })
  const complete = new Promise((resolve, reject) => {
    child.on('error', reject)
    child.on('close', code => code === 0 ? resolve(JSON.parse(out)) : reject(new Error('CLI测试命令失败：' + args.slice(0, 2).join(' ') + '；' + error.split('\n').filter(v => v.startsWith('{')).join(''))))
  })
  return { child, complete }
}
const cli = async args => run(args).complete
try {
  const login = await request('/admin/sys/yz_manager/login', { username: settings.Bootstrap.Username, password: settings.Bootstrap.Password }, {}, true)
  const webHeaders = { loginStateId: login.map.loginStateId, sysParams: encodeURIComponent(JSON.stringify({ yzProjectId: '2200000000000100000' })) }
  let approved = false
  loginProcess = run(['auth', 'login', '--url', base, '--profile', 'acceptance', '--name', 'CLI隔离验收'], async text => {
    const code = /用户码：([A-Z2-9]{8})/.exec(text)?.[1]
    if (approved || !code) return
    approved = true
    try { await request('/admin/sys/agent/approve', { userCode: code, approve: true }, webHeaders) }
    catch { loginProcess.child.kill() }
  })
  const timeout = setTimeout(() => loginProcess.child.kill(), 20000)
  await loginProcess.complete; clearTimeout(timeout)
  const config = JSON.parse(await readFile(env.METARILL_AGENT_CONFIG, 'utf8'))
  accessToken = config.profiles.acceptance.credentials.accessToken
  const catalog = await cli(['capabilities', 'list'])
  const create = catalog.capabilities.find(c => c.pageId === '2200000000000100020' && c.operation === 'create')
  deleteCapability = catalog.capabilities.find(c => c.pageId === '2200000000000100020' && c.operation === 'delete')
  if (!create || !deleteCapability) throw new Error('真实目录缺少CRUD能力')
  const requestId = randomUUID(), body = JSON.stringify({ name: 'CLI隔离验收-' + randomUUID(), notes: null, amount: 3.25 })
  const preview = await cli(['call', create.id, '--json', body, '--preview', '--request-id', requestId])
  if (preview.executed !== false) throw new Error('预览不应执行')
  const result = await cli(['call', create.id, '--json', body, '--confirm', '--request-id', requestId])
  recordId = result.data.id
  const task = await cli(['tasks', 'show', requestId])
  if (task.outcome !== 'applied' || task.resultId !== recordId) throw new Error('任务结果与业务主键不一致')
  let rejected = false
  try { await cli(['call', create.id, '--json', body, '--confirm', '--request-id', requestId]) } catch { rejected = true }
  if (!rejected) throw new Error('重复请求未拒绝')
  await cli(['call', deleteCapability.id, '--json', JSON.stringify({ id: recordId }), '--confirm'])
  recordId = undefined
  await cli(['auth', 'logout'])
  console.log('真实CLI端到端验收通过：设备登录、人工确认协议、目录、预览不写入、确认新增、任务查询、UUID去重、逻辑删除与撤权。')
} finally {
  if (recordId && deleteCapability) {
    try { await cli(['call', deleteCapability.id, '--json', JSON.stringify({ id: recordId }), '--confirm']) } catch {}
  }
  if (accessToken) { try { await request('/admin/sys/agent/revoke', {}, { Authorization: 'Bearer ' + accessToken }) } catch {} }
  loginProcess?.child.kill()
  if (path.dirname(directory) !== path.join(root, '.local')) throw new Error('清理目录超出测试范围')
  await rm(directory, { recursive: true, force: true })
}
