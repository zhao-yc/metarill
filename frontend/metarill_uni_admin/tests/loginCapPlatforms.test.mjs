import assert from 'node:assert/strict'
import test from 'node:test'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'

const source = await readFile(new URL('../pages/login.vue', import.meta.url), 'utf8')

/** 按uni-app条件编译选择真实页面脚本；不把设备编译或认证结果替换成测试结论。 */
function scriptFor(platform) {
 const active = [], lines = []
 for (const line of source.match(/<script[^>]*>([\s\S]*?)<\/script>/)[1].split(/\r?\n/)) {
  const directive = line.match(/^\s*\/\/ #(ifdef|ifndef) ([A-Z0-9_-]+)\s*$/)
  if (directive) { active.push(directive[1] === 'ifdef' ? directive[2] === platform : directive[2] !== platform); continue }
  if (/^\s*\/\/ #endif\s*$/.test(line)) { assert.ok(active.length); active.pop(); continue }
  if (active.every(Boolean)) lines.push(line)
 }
 assert.equal(active.length, 0)
 return lines.join('\n')
}

/** 执行实际solveCapToken与login方法，仅用配置和SDK替身观察调用，不发送账号密码。 */
function load(platform, Cap) {
 const script = scriptFor(platform)
 const context = { Cap, window: platform === 'H5' ? {} : undefined, utils: { isBlank: value => !value }, yzp: {} }
 vm.runInNewContext(script.replace(/^import[^\n]*\n/gm, '').replace('export default', 'globalThis.definition ='), context)
 return { script, definition: context.definition }
}

for (const platform of ['H5', 'APP-PLUS', 'MP-WEIXIN']) {
 test(platform + '验证关闭时继续原空令牌协议', async () => {
  const { definition } = load(platform)
  const token = await definition.methods.solveCapToken.call({ ensureCapConfig: async () => ({ enabled: false }) })
  assert.equal(token, '')
 })
 test(platform + '配置加载失败时不提交登录', async () => {
  const { definition } = load(platform); let submitted = 0, failure
  const page = { user_name: '账号', password: '密码', loginSubmitting: false, syncLoginInputFromDom: () => {}, ensureCapConfig: async () => { throw new Error('配置失败') }, submitLogin: () => { submitted++ }, showLoginError: message => { failure = message } }
  page.solveCapToken = definition.methods.solveCapToken.bind(page)
  await definition.methods.login.call(page)
  assert.equal(submitted, 0); assert.equal(failure, '配置失败'); assert.equal(page.loginSubmitting, false)
 })
 if (platform !== 'H5') {
  test(platform + '不包含浏览器SDK且要求验证时明确拒绝登录', async () => {
   const { script, definition } = load(platform)
   assert.doesNotMatch(script, /cap-widget|new Cap|capClient\.solve/)
   let submitted = 0, failure
   const page = { user_name: '账号', password: '密码', loginSubmitting: false, syncLoginInputFromDom: () => {}, ensureCapConfig: async () => ({ enabled: true, apiEndpoint: 'https://example.invalid/cap' }), submitLogin: () => { submitted++ }, showLoginError: message => { failure = message } }
   page.solveCapToken = definition.methods.solveCapToken.bind(page)
   await definition.methods.login.call(page)
   assert.equal(submitted, 0); assert.equal(failure, '安全验证暂不可用，请稍后再试'); assert.equal(page.loginSubmitting, false)
  })
 }
}

test('H5继续使用原Cap实例、进度、重置和一次性令牌', async () => {
 let created = 0, reset = 0, solved = 0
 class Cap {
  constructor(options) { created++; assert.equal(options.apiEndpoint, 'https://example.invalid/cap') }
  addEventListener(name, callback) { assert.equal(name, 'progress'); this.progress = callback }
  reset() { reset++ }
  async solve() { solved++; this.progress({ detail: { progress: 42.4 } }); return { token: '测试一次性令牌' } }
 }
 const { script, definition } = load('H5', Cap); assert.match(script, /import Cap from "cap-widget"/)
 const page = { ensureCapConfig: async () => ({ enabled: true, apiEndpoint: 'https://example.invalid/cap' }) }
 assert.equal(await definition.methods.solveCapToken.call(page), '测试一次性令牌')
 assert.equal(page.capProgress, 42); assert.equal(page.capStatusText, '安全验证中 42%')
 assert.equal(await definition.methods.solveCapToken.call(page), '测试一次性令牌')
 assert.equal(created, 1); assert.equal(reset, 2); assert.equal(solved, 2)
})

test('H5缺少端点或SDK不返回令牌时仍拒绝', async () => {
 const { definition } = load('H5', class { addEventListener() {} async solve() { return {} } })
 await assert.rejects(definition.methods.solveCapToken.call({ ensureCapConfig: async () => ({ enabled: true, apiEndpoint: '' }) }), /安全验证暂不可用/)
 await assert.rejects(definition.methods.solveCapToken.call({ ensureCapConfig: async () => ({ enabled: true, apiEndpoint: 'https://example.invalid/cap' }) }), /安全验证失败/)
})
