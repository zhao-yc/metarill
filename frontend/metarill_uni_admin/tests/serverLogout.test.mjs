import assert from 'node:assert/strict'
import test from 'node:test'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'

const definitions = await Promise.all([
 ['平台', new URL('../common/yzp.js', import.meta.url)],
 ['独立应用模板', new URL('../../../backend/templates/standalone-frontend/frontend-yzp.js.tpl', import.meta.url)]
].map(async ([name, path]) => [name, await readFile(path, 'utf8')]))

/** 执行真实宿主适配代码；只控制 SDK 与网络响应，不替代浏览器或数据库验收。 */
function fixture(source, session = '会话甲') {
 const storage = { loginStateId: session }, requests = [], dialogs = [], events = [], navigation = []
 let clears = 0
 const clear = () => { clears++; delete storage.loginStateId }
 const uni = { getStorageSync: key => storage[key], showModal: value => dialogs.push(value),
  $emit: value => events.push(value), reLaunch: value => navigation.push(value.url) }
 const yzp = { request: value => requests.push(value), clearLoginStorage: clear, customButtonHandler() {} }
 const context = vm.createContext({ uni, yzp, clearLoginStorageWithWorkspaceSelection: clear })
 vm.runInContext(source.replace(/^import[^\n]*\n/gm, '').replace(/export default yzp;?/, ''), context)
 const confirm = value => dialogs[0].success({ confirm: value })
 const respond = code => { const r = requests.at(-1); if (r.isCurrent()) r.success({ code }) }
 return { yzp, storage, requests, dialogs, events, navigation, confirm, respond, clears: () => clears }
}

for (const [name, source] of definitions) {
 test(name + '取消退出不请求、不清理身份', () => {
  const f = fixture(source); f.yzp.exitLogin(); f.confirm(false)
  assert.equal(f.dialogs[0].content, '您确认要退出登录吗？')
  assert.equal(f.requests.length, 0); assert.equal(f.clears(), 0)
 })
 test(name + '原确认后POST撤销成功才清理并跳转', () => {
  const f = fixture(source); f.yzp.exitLogin(); f.confirm(true)
  assert.equal(f.requests.length, 1); const request = f.requests[0]
  assert.equal(request.url, '/admin/sys/yz_manager/exit_login'); assert.equal(request.method, 'POST')
  assert.equal(request.sendSessionId, true); assert.equal(request.showLoading, false)
  assert.equal(f.clears(), 0); assert.equal(f.navigation.length, 0)
  f.respond(200); assert.equal(f.clears(), 1)
  assert.deepEqual(f.events, ['onLogout']); assert.deepEqual(f.navigation, ['/pages/login'])
 })
 test(name + '服务器拒绝不能伪装退出成功', () => {
  const f = fixture(source); f.yzp.exitLogin(); f.confirm(true); f.respond(500)
  assert.equal(f.storage.loginStateId, '会话甲'); assert.equal(f.clears(), 0)
  assert.equal(f.events.length, 0); assert.equal(f.navigation.length, 0)
 })
 test(name + '旧确认弹窗不能注销后来登录的身份', () => {
  const f = fixture(source); f.yzp.exitLogin(); f.storage.loginStateId = '会话乙'; f.confirm(true)
  assert.equal(f.requests.length, 0); assert.equal(f.clears(), 0)
 })
 test(name + '撤销迟到响应不能清除新身份或导航', () => {
  const f = fixture(source); f.yzp.exitLogin(); f.confirm(true); f.storage.loginStateId = '会话乙'
  assert.equal(f.requests[0].isCurrent(), false); f.respond(200)
  // 即便宿主回调被直接调用，清理入口仍有身份守卫。
  f.requests[0].success({ code: 200 })
  assert.equal(f.storage.loginStateId, '会话乙'); assert.equal(f.clears(), 0); assert.equal(f.navigation.length, 0)
 })
 test(name + '无会话只清理本地并返回原登录页', () => {
  const f = fixture(source, undefined); delete f.storage.loginStateId
  f.yzp.exitLogin(); f.confirm(true)
  assert.equal(f.requests.length, 0); assert.equal(f.clears(), 1); assert.deepEqual(f.navigation, ['/pages/login'])
 })
}
