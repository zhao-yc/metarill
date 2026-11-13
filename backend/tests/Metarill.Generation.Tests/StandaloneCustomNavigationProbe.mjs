import fs from 'node:fs'
import assert from 'node:assert/strict'

// 直接消费实际C#模板输出和原菜单运行时，不重写页面或菜单组件。
const source = fs.readFileSync(process.argv[2], 'utf8')
const payload = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'))
const { createMenuRuntime } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
const requests = [], navigations = []
const runtime = createMenuRuntime({
 getContext: () => ({ projectId: '100', adminTypeId: '5', identity: 'test-navigation' }),
 request(options) {
  requests.push(options.url)
  queueMicrotask(() => { options.success(payload); options.complete?.() })
  return { abort() {} }
 },
 navigate(options) { navigations.push(options.url); options.success?.(); options.complete?.(); return {} }
})
runtime.load()
await new Promise(resolve => setImmediate(resolve))
assert.equal(runtime.getState().status, 'ready')
runtime.navigateTo('902')
assert.equal(navigations.length, 1)
assert.ok(navigations[0].startsWith('/pages/main/set_role_permission?'))
assert.ok(navigations[0].includes('yzpMenuId=902'))
assert.ok(!navigations[0].includes('isShowCount'))
assert.deepEqual(requests, ['/admin/sys/yz_manager/getMenu'])
runtime.dispose()
console.log('实际C#自定义菜单可由原运行时导航，未构造CRUD计数请求。')
