import assert from 'node:assert/strict'
import test from 'node:test'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'

const sources = await Promise.all([
 ['平台登录页', new URL('../pages/login.vue', import.meta.url)],
 ['独立应用登录模板', new URL('../../../backend/templates/standalone-frontend/frontend-login.vue.tpl', import.meta.url)]
].map(async ([name, path]) => [name, await readFile(path, 'utf8')]))

/** 执行实际登录选项的方法；本组只验证两种SDK输入事件，不伪造认证或设备运行。 */
function loginMethods(source) {
 const script = source.match(/<script[^>]*>([\s\S]*?)<\/script>/)[1]
 const context = { application: { name: '输入事件验收' } }
 vm.runInNewContext(script.replace(/^import[^\n]*\n/gm, '').replace('export default', 'globalThis.definition ='), context)
 return context.definition.methods
}

for (const [name, source] of sources) {
 const methods = loginMethods(source)
 for (const [method, field] of [['onUsernameInput', 'user_name'], ['onPasswordInput', 'password']]) {
  test(name + '的' + field + '接受H5原生input事件', () => {
   const page = { [field]: '旧内容' }
   methods[method].call(page, { target: { value: '新内容 空格' } })
   assert.equal(page[field], '新内容 空格')
   methods[method].call(page, { target: { value: '' } })
   assert.equal(page[field], '')
  })
  test(name + '的' + field + '接受uni-app输入并保持主动清空', () => {
   const page = { [field]: '旧内容' }
   methods[method].call(page, { detail: { value: '端侧内容' }, target: { id: field } })
   assert.equal(page[field], '端侧内容')
   // 小程序事件target可能不携带值，detail空字符串不能回退到旧target值。
   methods[method].call(page, { detail: { value: '' }, target: { value: '过期内容' } })
   assert.equal(page[field], '')
  })
 }
}

/** 只选择uni-app非H5脚本分支，执行真实首页生命周期，不让动态import进入测试替身。 */
function nativeHome(source) {
 const active = [], lines = []
 for (const line of source.match(/<script[^>]*>([\s\S]*?)<\/script>/)[1].split(/\r?\n/)) {
  if (line.includes('// #ifdef H5')) { active.push(false); continue }
  if (line.includes('// #ifndef H5')) { active.push(true); continue }
  if (line.includes('// #endif')) { active.pop(); continue }
  if (active.every(Boolean)) lines.push(line)
 }
 const listeners = new Map(), removed = []
 const context = { MainContent: {}, TableContent: {}, PageContent: {}, uni: { $on: (name, fn) => listeners.set(name, fn), $off: (name, fn) => removed.push([name, fn]) } }
 vm.runInNewContext(lines.join('\n').replace(/^\s*import[^\n]*\n/gm, '').replace('export default', 'globalThis.definition ='), context)
 return { definition: context.definition, listeners, removed }
}

test('平台非Web首页只挂载访问过的原配置页并保留可见状态与具名解绑', async () => {
 const source = await readFile(new URL('../pages/main/index.vue', import.meta.url), 'utf8')
 const { definition, listeners, removed } = nativeHome(source), page = definition.data()
 definition.onLoad.call(page)
 assert.equal(page.nativeContent, 'main')
 assert.equal(page.nativeVisited.table, false); assert.equal(page.nativeVisited.page, false)
 definition.onShow.call(page); assert.equal(page.homeVisible, true)
 const menu = listeners.get('onClickMenu')
 menu({ path: 'config_table-list-6191083005411328' }); assert.equal(page.nativeContent, 'table'); assert.equal(page.nativeVisited.table, true); assert.equal(page.isHomeContent, false)
 menu({ path: 'config_page-list-6191175925956608' }); assert.equal(page.nativeContent, 'page'); assert.equal(page.nativeVisited.table, true); assert.equal(page.nativeVisited.page, true)
 menu({ path: '其他菜单' }); assert.equal(page.nativeContent, 'page')
 definition.onHide.call(page); assert.equal(page.homeVisible, false)
 definition.onUnload.call(page); assert.equal(removed.length, 1); assert.equal(removed[0][1], menu)
})

test('独立应用非Web首页沿用原main插槽和显隐生命周期', async () => {
 const source = await readFile(new URL('../../../backend/templates/standalone-frontend/frontend-index.vue.tpl', import.meta.url), 'utf8')
 const { definition, listeners } = nativeHome(source), page = definition.data()
 definition.onLoad.call(page); assert.equal(listeners.size, 0)
 definition.onShow.call(page); assert.equal(page.homeVisible, true)
 definition.onHide.call(page); assert.equal(page.homeVisible, false)
 assert.deepEqual(Object.keys(definition.components), ['MainContent'])
})
