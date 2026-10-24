import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../common/yzp_runtime/menuRuntime.js', import.meta.url), 'utf8')
const { createMenuRuntime, menuBoolean, MENU_ID_QUERY } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))

/** 创建带结构化页面身份的菜单，测试不依赖平台自身表与业务入口。 */
function leaf(id = '入口甲', { pageId = '页面甲', tableName = 'sample', route, count = false, endpoint, type = 'GENERATED_PAGE' } = {}) {
	return { menu_id: id, text: id, nodeType: type, value: '不得用于推导接口', isShowCount: count,
		target: { pageId, tableId: '表-' + tableName, tableName, route: route || '/pages/yzp/' + tableName + '-list-' + pageId,
			countEndpoint: endpoint === undefined ? '/admin/auto/' + tableName + '/' + pageId + '/count' : endpoint } }
}

/** 模拟网络不保证取消有效，允许显式触发已经取消请求的迟到回调。 */
function fixture({ concurrency = 2, requestOverride, navigateOverride } = {}) {
	const calls = [], navigations = [], commits = [], observations = [], timers = new Map()
	let context = { identity: '账号甲', projectId: '项目甲' }, authenticated = true, route = '', timerId = 0
	const runtime = createMenuRuntime({
		getContext: () => context,
		isAuthenticated: () => authenticated,
		getCurrentRoute: () => route,
		commitPermissions: permissions => commits.push(permissions),
		onChange: state => observations.push({ state, committed: commits.at(-1) }),
		countConcurrency: concurrency,
		schedule(fn) { const id = ++timerId; timers.set(id, fn); return id },
		cancelScheduled(id) { timers.delete(id) },
		request(options) {
			if (requestOverride) return requestOverride(options)
			const task = { aborts: 0, abort() { this.aborts++; options.fail({ errMsg: '已取消' }); options.complete() } }
			calls.push({ options, task })
			return task
		},
		navigate(options) {
			if (navigateOverride) return navigateOverride(options)
			const task = {}
			navigations.push({ options, task })
			return task
		}
	})
	return { runtime, calls, navigations, commits, observations, timers,
		setContext(next) { context = next },
		logout() { authenticated = false },
		setRoute(next) { route = next },
		flush() { const pending = Array.from(timers.values()); timers.clear(); pending.forEach(fn => fn()) },
		success(index, response) { calls[index].options.success(response); calls[index].options.complete() },
		menus(items = [leaf()], permissions = { pagePermissions: { 页面甲: ['is_query'] } }, index = 0) {
			this.success(index, { code: 200, menu: [{ menu_id: '分组', nodeType: 'DIRECTORY', children: items }], permissions })
		},
		fail(index, message = '网络不可用') { calls[index].options.fail({ errMsg: message }); calls[index].options.complete() }
	}
}

/** 深层菜单同样纳入选中和计数，避免测试仅覆盖固定两层。 */
function child(state, id) {
	const find = items => {
		for (const node of items) {
			if (node.menu_id === id) return node
			const nested = find(node.children || [])
			if (nested) return nested
		}
	}
	return find(state.menus)
}



test('菜单与权限按同一响应发布，订阅者从未看到跨代快照', () => {
	const f = fixture()
	f.runtime.load()
	assert.equal(f.calls[0].options.showLoading, false)
	assert.equal(f.calls[0].options.showError, false)
	f.menus()
	assert.equal(f.runtime.getState().status, 'ready')
	for (const observation of f.observations) assert.deepEqual(observation.state.permissions, observation.committed)
	assert.equal(f.runtime.getState().menus[0].children[0].target.pageId, '页面甲')
})

test('同上下文重复加载复用任务，强制刷新废弃旧响应与旧失败', () => {
	const f = fixture()
	const task = f.runtime.load()
	assert.equal(f.runtime.load(), task)
	f.runtime.load({ force: true })
	assert.equal(f.calls[0].task.aborts, 1)
	assert.equal(f.calls[0].options.isCurrent(), false)
	f.menus([leaf('新菜单')], { isSuper: true }, 1)
	f.menus([leaf('旧菜单')], { isSuper: false }, 0)
	f.fail(0)
	assert.equal(f.runtime.getState().menus[0].children[0].menu_id, '新菜单')
	assert.deepEqual(f.runtime.getState().permissions, { isSuper: true })
})

test('上下文切换在新响应前清空菜单和权限，即使旧任务同步取消回调也安全', () => {
	const f = fixture()
	f.runtime.load()
	f.menus()
	f.runtime.load({ force: true })
	f.setContext({ projectId: '项目乙', identity: '账号甲' })
	f.runtime.load()
	assert.equal(f.runtime.getState().status, 'loading')
	assert.deepEqual(f.runtime.getState().menus, [])
	assert.equal(f.runtime.getState().permissions, null)
	assert.equal(f.calls[1].task.aborts, 1)
	f.menus([leaf('乙项目')], {}, 2)
	f.menus([leaf('甲项目')], { isSuper: true }, 1)
	assert.equal(f.runtime.getState().menus[0].children[0].menu_id, '乙项目')
})

test('同值上下文对象键顺序变化不重新清空或取消任务', () => {
	const f = fixture()
	const task = f.runtime.load()
	f.setContext({ projectId: '项目甲', identity: '账号甲' })
	assert.equal(f.runtime.load(), task)
	assert.equal(f.calls.length, 1)
})

test('页面反复 onShow 复用菜单和角标快照，明确刷新才重新请求', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲', { count: true })])
	f.flush()
	f.success(1, { code: 200, data: 5 })
	f.runtime.load()
	f.runtime.load()
	f.runtime.syncRoute('/pages/yzp/sample-detail-详情')
	f.flush()
	assert.equal(f.calls.length, 2)
	assert.equal(child(f.runtime.getState(), '甲').count, '5')
	f.runtime.load({ force: true })
	assert.equal(f.calls.length, 3)
})

test('左右窗口重复挂载与页面 onShow 共用快照，窗口销毁不会取消另一窗口的请求', () => {
	const f = fixture()
	const left = [], mobile = []
	const releaseLeft = f.runtime.subscribe(state => left.push(state))
	f.runtime.load()
	const releaseMobile = f.runtime.subscribe(state => mobile.push(state))
	f.runtime.load()
	assert.equal(f.calls.length, 1)
	releaseLeft()
	f.menus([leaf('菜单')])
	assert.equal(mobile.at(-1).status, 'ready')
	const previousLeft = left.length
	const releaseNewLeft = f.runtime.subscribe(state => left.push(state))
	f.runtime.load()
	f.runtime.syncRoute('/pages/yzp/sample-list-页面甲')
	f.runtime.load()
	assert.equal(f.calls.length, 1)
	assert.ok(left.length > previousLeft)
	assert.equal(mobile.at(-1).activeMenuId, '菜单')
	f.setContext({ identity: '账号乙', projectId: '项目乙' })
	f.runtime.syncRoute('/pages/main/index')
	assert.equal(mobile.at(-1).permissions, null)
	assert.deepEqual(left.at(-1).menus, [])
	f.runtime.load()
	f.runtime.load()
	assert.equal(f.calls.length, 2)
	f.menus([leaf('新身份菜单')], {}, 1)
	assert.equal(left.at(-1).menus[0].children[0].menu_id, '新身份菜单')
	releaseMobile(); releaseNewLeft(); f.runtime.dispose()
})

test('没有收到宿主切换事件时，迟到响应仍不能覆盖新身份；读取状态立即清理', () => {
	const f = fixture()
	f.runtime.load()
	f.setContext({ identity: '账号乙', projectId: '项目甲' })
	assert.equal(f.calls[0].options.isCurrent(), false)
	f.menus()
	assert.deepEqual(f.runtime.getState().menus, [])
	assert.equal(f.runtime.getState().permissions, null)
})

test('退出登录清除菜单和权限，取消全部在途角标，迟到数量不再发布', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('角标', { count: true })])
	f.flush()
	f.logout()
	f.runtime.clear()
	assert.equal(f.calls[1].task.aborts, 1)
	f.success(1, { code: 200, data: 100 })
	assert.deepEqual(f.runtime.getState().menus, [])
	assert.equal(f.runtime.getState().permissions, null)
	f.runtime.load()
	assert.equal(f.calls.length, 2)
})

test('空数组成功切换为空菜单状态，旧节点不残留', () => {
	const f = fixture()
	f.runtime.load()
	f.menus()
	f.runtime.load({ force: true })
	f.success(1, { code: 200, menu: [], permissions: {} })
	assert.equal(f.runtime.getState().status, 'empty')
	assert.deepEqual(f.runtime.getState().menus, [])
	assert.deepEqual(f.runtime.getState().permissions, {})
})

test('请求失败清空旧授权并可重试，complete 缺少业务回调也有失败状态', () => {
	const f = fixture()
	f.runtime.load()
	f.menus()
	f.runtime.load({ force: true })
	f.fail(1)
	assert.equal(f.runtime.getState().status, 'error')
	assert.equal(f.runtime.getState().error, '网络不可用')
	assert.equal(f.runtime.getState().permissions, null)
	f.runtime.retry()
	f.calls[2].options.complete()
	assert.equal(f.runtime.getState().status, 'error')
	f.runtime.retry()
	f.menus([leaf('已恢复')], {}, 3)
	assert.equal(f.runtime.getState().status, 'ready')
})

test('未发出请求、同步异常和协议错误不会永久停在加载中', () => {
	for (const behavior of [() => undefined, () => { throw new Error('启动失败') }]) {
		const f = fixture({ requestOverride: behavior })
		f.runtime.load()
		assert.equal(f.runtime.getState().status, 'error')
	}
	const f = fixture()
	f.runtime.load()
	f.success(0, { code: 200, menu: [] })
	assert.equal(f.runtime.getState().status, 'error')
	f.runtime.retry()
	f.menus([leaf(), leaf()], {}, 1)
	assert.equal(f.runtime.getState().status, 'error')
	assert.match(f.runtime.getState().error, /标识/)
})

test('同一路径不同查询参数命中各自入口，直接刷新与返回恢复父目录展开', () => {
	const f = fixture()
	f.setRoute({ route: 'pages/custom/orders', options: { state: '已完成' } })
	f.runtime.load()
	f.menus([
		leaf('所有', { type: 'CUSTOM_PAGE', route: '/pages/custom/orders' }),
		{ menu_id: '子目录', nodeType: 'DIRECTORY', children: [leaf('完成', { type: 'CUSTOM_PAGE', route: '/pages/custom/orders?state=%E5%B7%B2%E5%AE%8C%E6%88%90' })] }
	])
	assert.equal(f.runtime.getState().activeMenuId, '完成')
	assert.deepEqual(f.runtime.getState().expandedMenuIds, ['分组', '子目录'])
	f.runtime.syncRoute('/pages/custom/orders')
	assert.equal(f.runtime.getState().activeMenuId, '所有')
	f.runtime.syncRoute('/pages/custom/other')
	assert.equal(f.runtime.getState().activeMenuId, '')
})

test('同页面相同参数多入口使用显式菜单标识，伪造不相关标识不能误选', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲'), leaf('乙'), leaf('另一张表', { tableName: 'other' })])
	f.runtime.syncRoute('/pages/yzp/sample-list-页面甲?' + MENU_ID_QUERY + '=乙')
	assert.equal(f.runtime.getState().activeMenuId, '乙')
	f.runtime.syncRoute('/pages/yzp/sample-list-页面甲?' + MENU_ID_QUERY + '=另一张表')
	assert.equal(f.runtime.getState().activeMenuId, '乙')
})

test('详情页面按表关联最近入口，显式入口标识在不同详情页仍可恢复', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲'), leaf('乙', { pageId: '页面乙' })])
	f.runtime.syncRoute('/pages/yzp/sample-list-页面乙')
	f.runtime.syncRoute('/pages/yzp/sample-detail-详情页?id=10')
	assert.equal(f.runtime.getState().activeMenuId, '乙')
	f.runtime.syncRoute('/pages/yzp/sample-detail-详情页?yzpMenuId=甲&id=10')
	assert.equal(f.runtime.getState().activeMenuId, '甲')
})

test('生成页和自定义页统一导航，保留原有参数与片段，成功才更新选中', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('生成', { count: 'false', route: '/pages/yzp/sample-list-页面甲?filter=待处理#位置' }),
		leaf('自定义', { type: 'CUSTOM_PAGE', route: '/pages/custom/example?tab=2' })])
	f.runtime.navigateTo('生成')
	assert.equal(f.runtime.getState().activeMenuId, '')
	assert.match(f.navigations[0].options.url, /filter=/)
	assert.match(f.navigations[0].options.url, /isShowCount=false/)
	assert.match(f.navigations[0].options.url, /#位置$/)
	f.navigations[0].options.success()
	f.navigations[0].options.complete()
	assert.equal(f.runtime.getState().activeMenuId, '生成')
	f.runtime.navigateTo('自定义')
	assert.match(f.navigations[1].options.url, /^\/pages\/custom\/example\?tab=2&yzpMenuId=/)
	assert.doesNotMatch(f.navigations[1].options.url, /isShowCount/)
	f.navigations[1].options.success()
	f.navigations[1].options.complete()
	assert.equal(f.runtime.getState().activeMenuId, '自定义')
})

test('导航失败保留原选中，重复点击复用任务，旧导航回调不能覆盖新导航', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲'), leaf('乙', { pageId: '页面乙' })])
	f.runtime.syncRoute('/pages/yzp/sample-list-页面甲')
	const task = f.runtime.navigateTo('乙')
	assert.equal(f.runtime.navigateTo('乙'), task)
	f.navigations[0].options.fail({ errMsg: '页面未注册' })
	f.navigations[0].options.complete()
	assert.equal(f.runtime.getState().activeMenuId, '甲')
	assert.equal(f.runtime.getState().navigationError, '页面未注册')
	f.runtime.navigateTo('乙')
	f.runtime.navigateTo('甲')
	f.navigations[2].options.success()
	f.navigations[2].options.complete()
	f.navigations[1].options.success()
	assert.equal(f.runtime.getState().activeMenuId, '甲')
})

test('导航同步异常或非法路径只报告当前导航错误，不抛出未处理异常', () => {
	const f = fixture({ navigateOverride() { throw new Error('导航不可用') } })
	f.runtime.load()
	f.menus([leaf('正常'), leaf('非法', { route: 'https://other.example/path' })])
	f.runtime.navigateTo('正常')
	assert.equal(f.runtime.getState().navigationError, '导航不可用')
	f.runtime.navigateTo('非法')
	assert.match(f.runtime.getState().navigationError, /内部页面/)
})

test('递归角标按结构化接口去重，严格限制并发，字符串 false 不请求', () => {
	const f = fixture({ concurrency: 2 })
	f.runtime.load()
	f.menus([leaf('一', { count: true }), leaf('重复', { count: true }),
		{ menu_id: '目录', children: [leaf('二', { pageId: '页面二', count: 'true' }), leaf('三', { pageId: '页面三', count: 1 })] },
		leaf('关闭', { pageId: '不请求', count: 'false' })])
	f.flush()
	assert.equal(f.calls.length, 3)
	for (const call of f.calls.slice(1)) {
		assert.equal(call.options.showLoading, false)
		assert.equal(call.options.showError, false)
		assert.equal(call.options.method, 'POST')
		assert.deepEqual(call.options.data, { filters: {}, parentId: '' })
	}
	f.success(1, { code: 200, data: 0 })
	assert.equal(f.calls.length, 4)
	assert.equal(child(f.runtime.getState(), '一').count, '0')
	assert.equal(child(f.runtime.getState(), '重复').count, '0')
	f.success(2, { code: 200, data: 2 })
	f.success(3, { code: 200, data: 3 })
	assert.equal(child(f.runtime.getState(), '三').count, '3')
	assert.equal(f.calls.length, 4)
})

test('按页面或表只刷新相关角标，读取与翻页不触发全量刷新', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲', { count: true }), leaf('乙', { count: true, pageId: '页面乙', tableName: 'other' })])
	f.flush()
	f.success(1, { code: 200, data: 1 })
	f.success(2, { code: 200, data: 2 })
	f.runtime.invalidateCounts({ reason: 'pagination', tableName: 'sample' })
	f.runtime.invalidateCounts({ reason: 'read' })
	f.flush()
	assert.equal(f.calls.length, 3)
	f.runtime.invalidateCounts({ pageId: '页面乙' })
	f.runtime.invalidateCounts({ tableName: 'other' })
	f.flush()
	assert.equal(f.calls.length, 4)
	assert.equal(f.calls[3].options.url, '/admin/auto/other/页面乙/count')
	f.success(3, { code: 200, data: 5 })
	assert.equal(child(f.runtime.getState(), '甲').count, '1')
	assert.equal(child(f.runtime.getState(), '乙').count, '5')
})

test('角标请求过程中再次写入会抛弃旧数量，合并补发一轮最新请求', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲', { count: true })])
	f.flush()
	f.runtime.invalidateCounts({ tableName: 'sample' })
	f.runtime.invalidateCounts({ pageId: '页面甲' })
	f.flush()
	assert.equal(f.calls.length, 2)
	assert.equal(f.calls[1].options.isCurrent(), false)
	f.success(1, { code: 200, data: 1 })
	assert.equal(child(f.runtime.getState(), '甲').count, undefined)
	assert.equal(f.calls.length, 3)
	f.success(2, { code: 200, data: 9 })
	assert.equal(child(f.runtime.getState(), '甲').count, '9')
	assert.equal(child(f.runtime.getState(), '甲').countLoading, false)
})

test('旧无参数角标事件集中防抖，计数失败保留导航并能在下一次变更恢复', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲', { count: true })])
	f.runtime.invalidateCounts()
	f.runtime.invalidateCounts()
	assert.equal(f.timers.size, 1)
	f.flush()
	f.fail(1)
	assert.equal(f.runtime.getState().status, 'ready')
	assert.equal(child(f.runtime.getState(), '甲').countError, '网络不可用')
	f.runtime.invalidateCounts({ tableId: '表-sample' })
	f.flush()
	f.success(2, { code: 200, data: 3 })
	assert.equal(child(f.runtime.getState(), '甲').count, '3')
	assert.equal(child(f.runtime.getState(), '甲').countError, '')
})

test('计数协议缺失或返回 null 不转为有效零，旧菜单计数不能污染重新加载的菜单', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('甲', { count: true })])
	f.flush()
	f.success(1, { code: 200, data: null })
	assert.match(child(f.runtime.getState(), '甲').countError, /格式/)
	f.runtime.invalidateCounts()
	f.flush()
	f.runtime.load({ force: true })
	assert.equal(f.calls[2].task.aborts, 1)
	f.menus([leaf('新入口')], {}, 3)
	f.success(2, { code: 200, data: 100 })
	assert.equal(child(f.runtime.getState(), '甲'), undefined)
	assert.equal(child(f.runtime.getState(), '新入口').count, undefined)
})

test('多个订阅者共享状态，注销订阅或销毁后没有迟到副作用', () => {
	const f = fixture()
	let notices = 0
	const unsubscribe = f.runtime.subscribe(() => notices++)
	f.runtime.load()
	unsubscribe()
	const before = notices
	f.menus()
	assert.equal(notices, before)
	f.runtime.load({ force: true })
	f.runtime.dispose()
	f.runtime.dispose()
	const published = f.observations.length
	f.menus([leaf('迟到')], {}, 1)
	f.runtime.invalidateCounts()
	f.runtime.load()
	assert.equal(f.observations.length, published)
	assert.equal(f.calls[1].task.aborts, 1)
})

test('URL 布尔参数只允许明确的真值，兼容数值零和字符串 false', () => {
	for (const value of [true, 1, '1', 'true']) assert.equal(menuBoolean(value), true)
	for (const value of [false, 0, '0', 'false', '', undefined, null, 'yes']) assert.equal(menuBoolean(value), false)
})

test('旧 hidden 和 visibleScheme 配置原样传递，第二阶段不重新解释历史显示语义', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([{ ...leaf('旧配置'), hidden: true, visibleScheme: '旧规则' }])
	assert.equal(child(f.runtime.getState(), '旧配置').hidden, true)
	assert.equal(child(f.runtime.getState(), '旧配置').visibleScheme, '旧规则')
})

test('显式目录和带子节点的兼容节点不可跳转，也不请求目录自身的角标', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([{ ...leaf('兼容目录', { count: true }), children: [leaf('子页')] },
		{ ...leaf('空目录', { count: true }), nodeType: 'DIRECTORY', children: [] }])
	f.runtime.navigateTo('兼容目录')
	f.runtime.navigateTo('空目录')
	f.flush()
	assert.equal(f.navigations.length, 0)
	assert.equal(f.calls.length, 1)
})

test('tabBar 宿主报告真实路径后，不把已经丢弃的查询参数当作选中条件', () => {
	const f = fixture()
	f.runtime.load()
	f.menus([leaf('首页', { type: 'CUSTOM_PAGE', route: '/pages/main/index' }),
		leaf('附参首页', { type: 'CUSTOM_PAGE', route: '/pages/main/index?tab=2' })])
	f.runtime.navigateTo('附参首页')
	f.navigations[0].options.success({ actualRoute: '/pages/main/index' })
	f.navigations[0].options.complete()
	assert.equal(f.runtime.getState().activeMenuId, '首页')
})

test('同步返回菜单和多个计数请求时，无递归队列卡死且数量完整', () => {
	let runtime
	const menus = Array.from({ length: 50 }, (_, index) => leaf('入口' + index, { pageId: '页' + index, count: true }))
	let next
	runtime = createMenuRuntime({
		schedule(fn) { next = fn; return 1 }, cancelScheduled() {},
		request(options) {
			options.success(options.method === 'GET' ? { code: 200, menu: menus, permissions: {} } : { code: 200, data: 7 })
			options.complete()
			return { abort() {} }
		}
	})
	runtime.load()
	next()
	assert.equal(runtime.getState().status, 'ready')
	assert.equal(runtime.getState().menus.length, 50)
	assert.ok(runtime.getState().menus.every(node => node.count === '7' && !node.countLoading))
	runtime.dispose()
})
