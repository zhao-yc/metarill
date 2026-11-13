import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import vm from 'node:vm'

/** 执行实际顶栏脚本，记录菜单回调和路由，不调用真实退出接口。 */
async function fixture(template = false, agentGrants = false) {
 const path = template ? '../../../backend/templates/standalone-frontend/frontend-top.vue.tpl' : '../pages/main/top.vue'
 const source = await readFile(new URL(path, import.meta.url), 'utf8')
 const sheets = [], navigation = [], toasts = [], events = []
 let logout = 0
 const context = {
  application: { capabilities: { agentGrants } },
  uni: { showActionSheet: options => sheets.push(options), getTopWindowStyle: () => ({ height: '60px' }),
   navigateTo: options => navigation.push(options), showToast: options => toasts.push(options), $emit: name => events.push(name) },
  getApp: () => ({ globalData: { windowInfo: { windowWidth: 1366 } } }),
  yzp: { exitLogin: () => logout++ }
 }
 vm.runInNewContext(source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^\s*import .*;\s*$/gm, '').replace('export default', 'globalThis.options ='), context)
 const options = context.options, state = { ...options.methods }
 for (const [name, getter] of Object.entries(options.computed || {})) Object.defineProperty(state, name, { get: () => getter.call(state) })
 state.showActions()
 return { sheet: sheets[0], navigation, toasts, events, logout: () => logout }
}

for (const [name, template, capability, visible] of [['平台', false, false, true], ['启用 Agent 的应用', true, true, true], ['未启用 Agent 的应用', true, false, false]]) {
 test(`${name}的头像菜单顺序、分派、取消及跳转失败反馈`, async () => {
  const f = await fixture(template, capability)
  assert.deepEqual(Array.from(f.sheet.itemList), visible ? ['修改密码', 'Agent 授权管理', '退出'] : ['修改密码', '退出'])
  assert.equal(f.sheet.popover.width, 180)
  assert.equal(f.sheet.popover.left + f.sheet.popover.width, 1346)
  // 未选择或取消时不会调用成功回调，因此不应发生导航或退出。
  assert.equal(f.navigation.length + f.events.length + f.logout(), 0)
  f.sheet.success({ tapIndex: 0 })
  assert.deepEqual(f.events, ['navigateToChangePassword'])
  if (visible) {
   f.sheet.success({ tapIndex: 1 })
   assert.equal(f.navigation[0].url, '/pages/custom/agent_grants')
   f.navigation[0].fail()
   assert.equal(f.toasts[0].title, '授权管理页暂时无法打开，请刷新后重试')
  }
  f.sheet.success({ tapIndex: visible ? 2 : 1 })
  assert.equal(f.logout(), 1)
 })
}

test('左侧菜单移除授权入口，授权管理页仍使用原返回操作', async () => {
 const menu = await readFile(new URL('../components/yzp-menu/yzp-menu.vue', import.meta.url), 'utf8')
 assert.doesNotMatch(menu, /agent-entry|openAgentGrants|agent_grants/)
 const page = await readFile(new URL('../pages/custom/agent_grants.vue', import.meta.url), 'utf8')
 let returned = 0
 const context = { yzp: { back: () => returned++ } }
 vm.runInNewContext(page.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^\s*import .*;\s*$/gm, '').replace('export default', 'globalThis.options ='), context)
 context.options.methods.back()
 assert.equal(returned, 1)
})
