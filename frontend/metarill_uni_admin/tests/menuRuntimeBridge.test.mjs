import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../common/yzp_runtime/menuRuntimeBridge.js', import.meta.url), 'utf8')
const runtimeSource = await readFile(new URL('../common/yzp_runtime/menuRuntime.js', import.meta.url), 'utf8')
const { createMenuRuntime } = await import('data:text/javascript;base64,' + Buffer.from(runtimeSource).toString('base64'))

/** 直接执行桥接与真实状态机，只替换 uni 宿主和网络回调。 */
function fixture() {
	const events = new Map(), requests = [], mixins = [], permissions = [], navigations = []
	const storage = { loginStateId: '会话甲', userInfo: { id: '甲' }, sysParams: { yzProjectId: 'p' } }
	let pages = [{ route: 'pages/main/index', options: {} }]
	const uni = { getStorageSync: key => storage[key], $on: (name, handler) => events.set(name, handler),
		navigateTo: options => { navigations.push(options); return {} }, switchTab: options => navigations.push(options) }
	const yzp = { checkLogin: () => !!storage.loginStateId, saveCurrentPermissions: value => permissions.push(value),
		request: options => { const task = { abort() {} }; requests.push(options); return task } }
	const body = source.replace(/^import .*$/gm, '').replace(/export /g, '')
	const api = new Function('uni', 'yzp', 'createMenuRuntime', 'getCurrentPages', body + '\nreturn {getMenuRuntime,installMenuRuntime,navigateMenu,currentMenuRoute}')(
		uni, yzp, createMenuRuntime, () => pages)
	const respond = (index, menu = []) => { requests[index].success({ code: 200, menu, permissions: { isSuper: true } }); requests[index].complete() }
	return { ...api, storage, events, requests, permissions, navigations, mixins, respond, setPages: value => { pages = value } }
}

test('多次安装和窗口订阅共用请求，项目切换后旧响应不能覆盖权限', () => {
	const f = fixture()
	f.installMenuRuntime(mixin => f.mixins.push(mixin))
	f.installMenuRuntime(mixin => f.mixins.push(mixin))
	assert.equal(f.mixins.length, 1)
	const runtime = f.getMenuRuntime()
	f.mixins[0].onShow(); runtime.load()
	assert.equal(f.requests.length, 1)
	f.storage.sysParams = { yzProjectId: '另一项目' }
	f.events.get('onChangeProject')()
	f.respond(0)
	assert.equal(runtime.getState().status, 'loading')
	f.respond(1)
	assert.equal(runtime.getState().status, 'empty')
	delete f.storage.loginStateId
	f.events.get('onLogout')()
	assert.equal(runtime.getState().status, 'idle')
	assert.equal(f.permissions.at(-1), null)
	runtime.dispose()
})

test('编辑其他项目不重载当前导航；当前后台保存后才刷新菜单', () => {
	const f = fixture(); f.installMenuRuntime(() => {}); f.getMenuRuntime().load()
	f.respond(0, [{ menu_id: 'root', projectId: 'p', adminTypeId: 'a', children: [] }])
	f.events.get('onMenuConfigurationChanged')({ projectId: 'other', adminTypeId: 'a' })
	assert.equal(f.requests.length, 1)
	f.events.get('onMenuConfigurationChanged')({ projectId: 'p', adminTypeId: 'a' })
	assert.equal(f.requests.length, 2)
	f.getMenuRuntime().dispose()
})

test('tabBar 回退不提前失败，按实际无查询路由成功收尾一次', () => {
	const f = fixture(), results = [], complete = []
	f.navigateMenu({ url: '/pages/main/index?yzpMenuId=x', success: value => results.push(value), complete: value => complete.push(value), fail() { assert.fail('不应提前失败') } })
	f.navigations[0].fail({ errMsg: 'navigateTo:fail can not navigateTo a tabbar page' }); f.navigations[0].complete()
	assert.equal(results.length, 0); assert.equal(complete.length, 0)
	assert.equal(f.navigations[1].url, '/pages/main/index')
	f.navigations[1].success({}); f.navigations[1].complete({})
	assert.equal(results[0].actualRoute, '/pages/main/index'); assert.equal(complete.length, 1)
})

test('页面 fullPath 与 query 均可恢复，路由栈返回不保留前页参数', () => {
	const f = fixture()
	f.setPages([{ route: 'pages/yzp/order-list-p', $page: { fullPath: '/pages/yzp/order-list-p?state=open' }, options: { yzpMenuId: 'm' } }])
	assert.deepEqual(f.currentMenuRoute(), { fullPath: '/pages/yzp/order-list-p?state=open', options: { yzpMenuId: 'm' } })
	f.setPages([{ route: 'pages/main/index' }])
	assert.deepEqual(f.currentMenuRoute(), { fullPath: 'pages/main/index', options: {} })
})

test('tabBar 不能静默丢弃业务参数，失败后保持当前页面', () => {
	const f = fixture(), failures = []
	f.navigateMenu({ url: '/pages/main/index?filter=open&yzpMenuId=x', fail: error => failures.push(error), success() { assert.fail('参数不能被静默丢弃') } })
	f.navigations[0].fail({ errMsg: 'navigateTo:fail tabbar page' })
	assert.equal(f.navigations.length, 1)
	assert.match(failures[0].errMsg, /不支持入口参数/)
})
