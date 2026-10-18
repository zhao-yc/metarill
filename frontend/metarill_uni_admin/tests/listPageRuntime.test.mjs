import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const runtimePath = new URL('../common/yzp_runtime/listPageRuntime.js', import.meta.url)
const source = await readFile(runtimePath, 'utf8')
const { createListPageMixin } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))

/** 用可控回调保留已经取消的请求，主动模拟网络的迟到响应。 */
function fixture({ pc = false, parent, child = false, pageSize = 2, behavior, onParentChanged, mixin: existingMixin } = {}) {
	const calls = []
	const effects = { dialogs: [], events: [], pulls: 0, loaded: [] }
	globalThis.uni = {
		showModal: options => effects.dialogs.push(options.content),
		$emit: name => effects.events.push(name),
		stopPullDownRefresh: () => { effects.pulls++ }
	}
	const request = options => {
		if (behavior) return behavior(options)
		const task = { aborts: 0, abort() {
			this.aborts++
			options.fail({ errMsg: '已取消的请求' })
			options.complete()
		} }
		calls.push({ options, task })
		return task
	}
	const mixin = existingMixin || createListPageMixin({ url: '/列表接口', pageSize, request, onParentChanged,
		onLoaded(res) { effects.loaded.push({ instance: this, res }); if (this.isShowCount) uni.$emit('updateMenuCount') } })
	const page = Object.assign(mixin.data(), { filters: {}, orderBy: {}, parent_id: parent,
		is_son_page: child, isShowCount: true, $deviceInfo: { is_pc: pc } })
	for (const [name, method] of Object.entries(mixin.methods)) page[name] = method.bind(page)
	return { page, mixin, calls, effects,
		/** 当前成功、失败均按照 uni 的回调顺序补齐 complete。 */
		succeed(index, records, { pages = 3, total = 6, map = { statistics: { 金额: index } } } = {}) {
			calls[index].options.success({ code: 200, data: { records, pages, total }, map })
			calls[index].options.complete()
		},
		fail(index, message = '网络暂不可用') {
			calls[index].options.fail({ errMsg: message })
			calls[index].options.complete()
		},
		parent(value) {
			page.parent_id = value
			mixin.watch.parent_id.call(page, value)
		}
	}
}

/** 断言只有公共展示数据进入组件响应状态。 */
test('普通列表首次加载由页面控制，响应数据与请求代次分开保存', () => {
	const f = fixture()
	assert.equal(f.calls.length, 0)
	assert.deepEqual(Object.keys(f.mixin.data()).sort(), ['list', 'list_error', 'load_more_status', 'page_no', 'page_size', 'pages_qty', 'pagination_version', 'rows_qty', 'statistics'].sort())
	f.page.getList()
	assert.equal(f.calls.length, 1)
	assert.equal(f.calls[0].options.method, 'POST')
	assert.equal(f.calls[0].options.sendSessionId, true)
	f.succeed(0, [{ id: '甲' }])
	assert.equal(f.page.list[0].id, '甲')
	assert.equal(f.effects.loaded[0].instance, f.page)
	assert.equal(f.effects.pulls, 0)
})

test('两次刷新逆序成功时仅最新响应修改列表、统计和菜单', () => {
	const f = fixture()
	f.page.getList()
	f.page.refreshData()
	assert.equal(f.calls[0].task.aborts, 1)
	assert.equal(f.calls[0].options.isCurrent(), false)
	f.succeed(1, [{ id: '新' }])
	f.succeed(0, [{ id: '旧' }])
	assert.deepEqual(f.page.list, [{ id: '新' }])
	assert.deepEqual(f.page.statistics, { 金额: 1 })
	assert.equal(f.effects.loaded.length, 1)
	assert.deepEqual(f.effects.events, ['updateMenuCount'])
})

test('旧业务失败与网络失败不污染新请求，也不弹出过期错误', () => {
	const f = fixture()
	f.page.getList()
	f.page.refreshData()
	f.calls[0].options.success({ code: 500, msg: '旧业务失败' })
	f.fail(0, '旧网络失败')
	assert.equal(f.page.load_more_status, 'loading')
	assert.equal(f.page.list_error, '')
	assert.deepEqual(f.effects.dialogs, [])
	f.succeed(1, [{ id: '最新' }])
	assert.equal(f.page.list[0].id, '最新')
})

test('连续追加共用在途请求，成功才推进页码且每页只追加一次', () => {
	const f = fixture()
	f.page.getList()
	f.succeed(0, [{ id: '第一页' }])
	const task = f.page.loadNextPage()
	assert.equal(f.page.loadNextPage(), task)
	assert.equal(f.calls.length, 2)
	assert.equal(f.page.page_no, 1)
	assert.equal(f.calls[1].options.data.pageNo, 2)
	f.succeed(1, [{ id: '第二页' }])
	f.succeed(1, [{ id: '第二页重复回调' }])
	assert.deepEqual(f.page.list.map(item => item.id), ['第一页', '第二页'])
	assert.equal(f.page.page_no, 2)
})

test('追加失败保留已确认页和行，点击重试请求相同页码', () => {
	const f = fixture()
	f.page.getList()
	f.succeed(0, [{ id: '第一页' }])
	f.page.loadNextPage()
	f.fail(1)
	assert.equal(f.page.page_no, 1)
	assert.deepEqual(f.page.list, [{ id: '第一页' }])
	assert.equal(f.page.load_more_status, 'more')
	assert.match(f.mixin.computed.loadMoreText.call(f.page).contentdown, /网络暂不可用.*重试/)
	f.page.loadNextPage()
	assert.equal(f.calls[2].options.data.pageNo, 2)
	f.succeed(2, [{ id: '第二页' }])
	assert.equal(f.page.page_no, 2)
	assert.equal(f.page.list_error, '')
})

test('刷新打断追加并替换第一页，旧追加成功与失败均被隔离', () => {
	const f = fixture()
	f.page.getList()
	f.succeed(0, [{ id: '原第一页' }])
	f.page.loadNextPage()
	f.page.refreshData()
	assert.deepEqual(f.page.list, [{ id: '原第一页' }])
	assert.equal(f.calls[1].task.aborts, 1)
	f.succeed(2, [{ id: '刷新第一页' }])
	f.succeed(1, [{ id: '旧第二页' }])
	f.fail(1)
	assert.deepEqual(f.page.list, [{ id: '刷新第一页' }])
	assert.equal(f.page.page_no, 1)
	assert.equal(f.page.list_error, '')
})

test('PC 快速翻页隔离逆序响应，同页在途可复用并保持替换语义', () => {
	const f = fixture({ pc: true })
	f.page.getList()
	f.succeed(0, [{ id: '一' }])
	f.page.paginationChange({ current: 2 })
	const latest = f.page.paginationChange({ current: 3 })
	assert.equal(f.page.paginationChange({ current: 3 }), latest)
	assert.equal(f.calls.length, 3)
	assert.equal(f.page.page_no, 1)
	f.succeed(2, [{ id: '三' }])
	f.succeed(1, [{ id: '二' }])
	assert.equal(f.page.page_no, 3)
	assert.deepEqual(f.page.list, [{ id: '三' }])
	assert.equal(f.page.load_more_status, 'no-more')
})

test('已提交筛选快照冻结，未提交编辑不丢弃当前响应或改变追加条件', () => {
	const f = fixture()
	f.page.filters = { 名称: { in: ['甲'] } }
	f.page.orderBy = { id: 'desc' }
	f.page.getList()
	const payload = f.calls[0].options.data
	f.page.filters.名称.in.push('乙')
	f.page.orderBy.id = 'asc'
	assert.equal(f.calls[0].options.isCurrent(), true)
	assert.deepEqual(payload.filters, { 名称: { in: ['甲'] } })
	assert.ok(Object.isFrozen(payload))
	assert.ok(Object.isFrozen(payload.filters.名称.in))
	f.succeed(0, [{ id: '甲' }])
	f.page.loadNextPage()
	assert.deepEqual(f.calls[1].options.data.filters, payload.filters)
	assert.deepEqual(f.calls[1].options.data.orderBy, { id: 'desc' })
	f.page.refreshData()
	assert.deepEqual(f.page.list, [])
	assert.deepEqual(f.page.statistics, {})
	assert.deepEqual(f.calls[2].options.data.filters, { 名称: { in: ['甲', '乙'] } })
	assert.deepEqual(f.calls[2].options.data.orderBy, { id: 'asc' })
})

test('子表父 A 到 B 到空立即失效，watcher 执行前也不接受旧父数据', () => {
	const f = fixture({ child: true, parent: 'A' })
	f.page.getList()
	f.page.parent_id = 'B'
	assert.equal(f.calls[0].options.isCurrent(), false)
	f.succeed(0, [{ id: 'A记录' }])
	assert.deepEqual(f.page.list, [])
	f.mixin.watch.parent_id.call(f.page)
	assert.equal(f.calls[1].options.data.parentId, 'B')
	f.succeed(1, [{ id: 'B记录' }])
	f.page.refreshData()
	f.parent('')
	assert.equal(f.calls.length, 3)
	assert.deepEqual(f.page.list, [])
	assert.equal(f.page.rows_qty, 0)
	assert.equal(f.page.load_more_status, 'no-more')
	f.succeed(2, [{ id: '迟到B' }])
	assert.deepEqual(f.page.list, [])
	f.page.getList()
	f.page.loadNextPage()
	assert.equal(f.calls.length, 3)
})

test('组件卸载幂等，迟到列表、统计、错误、菜单和下拉回调均无副作用', () => {
	const f = fixture()
	f.mixin.onPullDownRefresh.call(f.page)
	const snapshot = JSON.stringify(f.page)
	f.mixin.onUnload.call(f.page)
	f.mixin.beforeUnmount.call(f.page)
	f.succeed(0, [{ id: '迟到' }])
	f.fail(0)
	f.calls[0].options.success({ code: 500, msg: '卸载后错误' })
	assert.equal(JSON.stringify(f.page), snapshot)
	assert.equal(f.calls[0].task.aborts, 1)
	assert.equal(f.effects.pulls, 0)
	assert.equal(f.effects.events.length, 0)
	assert.equal(f.effects.dialogs.length, 0)
	assert.equal(f.effects.loaded.length, 0)
	f.page.getList()
	f.page.loadNextPage()
	assert.equal(f.calls.length, 1)
})

test('只有本实例下拉刷新停止下拉，刷新替换仍由最新请求收尾', () => {
	const f = fixture()
	f.page.getList()
	f.succeed(0, [])
	assert.equal(f.effects.pulls, 0)
	f.mixin.onPullDownRefresh.call(f.page)
	f.page.refreshData()
	f.succeed(1, [])
	assert.equal(f.effects.pulls, 0)
	f.succeed(2, [])
	assert.equal(f.effects.pulls, 1)
	f.succeed(2, [])
	assert.equal(f.effects.pulls, 1)
})

test('同步异常与无登录任务都释放 loading，首屏点击可重试第一页且不重复弹窗', () => {
	for (const behavior of [() => { throw new Error('同步请求失败') }, () => undefined]) {
		let starts = 0
		const f = fixture({ behavior: options => { starts++; assert.equal(options.data.pageNo, 1); return behavior() } })
		f.page.getList()
		assert.equal(f.page.load_more_status, 'more')
		assert.ok(f.page.list_error)
		assert.deepEqual(f.effects.dialogs, [])
		f.page.loadNextPage()
		assert.equal(starts, 2)
		assert.equal(f.page.page_no, 1)
	}
})

test('首屏业务失败允许原页重试，成功缺省统计为空对象', () => {
	const f = fixture()
	f.page.getList()
	f.calls[0].options.success({ code: 500, msg: '筛选条件不可用' })
	f.calls[0].options.complete()
	assert.deepEqual(f.effects.dialogs, ['筛选条件不可用'])
	f.page.loadNextPage()
	assert.equal(f.calls[1].options.data.pageNo, 1)
	f.succeed(1, [], { map: null, pages: 0, total: 0 })
	assert.deepEqual(f.page.statistics, {})
	// 不带 map 的实际响应也必须稳定回落为空统计。
	f.page.getList()
	f.calls[2].options.success({ code: 200, data: { records: [], pages: 0, total: 0 } })
	f.calls[2].options.complete()
	assert.deepEqual(f.page.statistics, {})
	assert.equal(f.page.load_more_status, 'no-more')
})

test('无分页配置零或负数原样提交且不会追加请求', () => {
	for (const pageSize of [0, -1]) {
		const f = fixture({ pageSize })
		f.page.getList()
		assert.equal(f.calls[0].options.data.pageSize, pageSize)
		f.succeed(0, [{ id: '全部' }], { pages: 8 })
		f.page.loadNextPage()
		assert.equal(f.calls.length, 1)
		assert.equal(f.page.load_more_status, 'no-more')
	}
})

test('同一个 mixin 的两个组件并发互不取消和覆盖', () => {
	const first = fixture()
	const second = fixture({ mixin: first.mixin })
	first.page.getList()
	second.page.getList()
	first.page.refreshData()
	assert.equal(first.calls.length, 3)
	assert.equal(first.calls[0].task.aborts, 1)
	assert.equal(first.calls[1].task.aborts, 0)
	first.succeed(1, [{ id: '第二实例' }])
	first.succeed(2, [{ id: '第一实例' }])
	first.succeed(0, [{ id: '第一实例过期' }])
	assert.equal(first.page.list[0].id, '第一实例')
	assert.equal(second.page.list[0].id, '第二实例')
})

test('精准保存合并保留当前行其它字段，并取消旧请求防止保存结果回退', () => {
	const f = fixture()
	f.page.getList()
	f.succeed(0, [{ id: '甲', name: '旧值', other: 1 }])
	f.page.refreshData()
	const pendingPaginationVersion = f.page.pagination_version
	assert.equal(f.page.applyUpdatedEntityFromDetail({ action: 'save', method: 'update', entity: { id: '甲', name: '保存值' } }), true)
	assert.equal(f.calls[1].task.aborts, 1)
	assert.equal(f.page.pagination_version, pendingPaginationVersion + 1)
	f.succeed(1, [{ id: '甲', name: '旧响应' }])
	assert.equal(f.page.pagination_version, pendingPaginationVersion + 1)
	assert.deepEqual(f.page.list, [{ id: '甲', name: '保存值', other: 1 }])
	assert.equal(f.page.applyUpdatedEntityFromDetail({ action: 'save', method: 'insert', entity: { id: '乙' } }), false)
	assert.equal(f.page.applyUpdatedEntityFromDetail('save'), false)
})

test('PC 翻页失败重试原页与查询快照，成功替换而非追加', () => {
	const f = fixture({ pc: true })
	f.page.filters = { 状态: '已提交' }
	f.page.getList()
	f.succeed(0, [{ id: '第一页' }])
	f.page.paginationChange({ current: 3 })
	f.fail(1, '第三页失败')
	assert.equal(f.page.page_no, 1)
	assert.deepEqual(f.page.list, [{ id: '第一页' }])
	f.page.filters = { 状态: '尚未提交的编辑' }
	const retryTask = f.page.retryListRequest()
	assert.equal(f.page.retryListRequest(), retryTask)
	assert.equal(f.calls.length, 3)
	assert.equal(f.calls[2].options.data.pageNo, 3)
	assert.deepEqual(f.calls[2].options.data.filters, { 状态: '已提交' })
	f.succeed(2, [{ id: '第三页' }])
	assert.equal(f.page.page_no, 3)
	assert.deepEqual(f.page.list, [{ id: '第三页' }])
})

test('PC 按钮 getList 重取已确认第三页并使旧请求失效，新筛选回第一页', () => {
	const f = fixture({ pc: true })
	f.page.filters = { 状态: '原筛选' }
	f.page.getList()
	f.succeed(0, [{ id: '第一页' }])
	f.page.paginationChange({ current: 3 })
	f.succeed(1, [{ id: '原第三页' }])
	const previous = f.page.getList()
	const latest = f.page.getList()
	assert.notEqual(latest, previous)
	assert.equal(f.calls[2].task.aborts, 1)
	assert.equal(f.calls[2].options.data.pageNo, 3)
	assert.equal(f.calls[3].options.data.pageNo, 3)
	f.succeed(3, [{ id: '更新第三页' }])
	f.succeed(2, [{ id: '过期第三页' }])
	assert.deepEqual(f.page.list, [{ id: '更新第三页' }])
	assert.equal(f.page.page_no, 3)
	f.page.filters = { 状态: '新筛选' }
	f.page.getList()
	assert.equal(f.calls[4].options.data.pageNo, 1)
	assert.deepEqual(f.page.list, [])
	f.succeed(4, [{ id: '新筛选第一页' }])
	assert.equal(f.page.page_no, 1)
})

test('PC 明确刷新和下拉仍回第一页，移动 getList 不重复追加', () => {
	const pc = fixture({ pc: true })
	pc.page.paginationChange({ current: 3 })
	pc.succeed(0, [{ id: '第三页' }])
	pc.page.refreshData()
	assert.equal(pc.calls[1].options.data.pageNo, 1)
	pc.succeed(1, [{ id: '第一页' }])
	pc.page.paginationChange({ current: 3 })
	pc.succeed(2, [{ id: '再次第三页' }])
	pc.mixin.onPullDownRefresh.call(pc.page)
	assert.equal(pc.calls[3].options.data.pageNo, 1)
	pc.succeed(3, [{ id: '下拉第一页' }])
	const mobile = fixture()
	mobile.page.getList()
	mobile.succeed(0, [{ id: '一' }])
	mobile.page.loadNextPage()
	mobile.succeed(1, [{ id: '二' }])
	mobile.page.getList()
	assert.equal(mobile.calls[2].options.data.pageNo, 1)
	mobile.succeed(2, [{ id: '新一' }])
	assert.deepEqual(mobile.page.list, [{ id: '新一' }])
})

test('父上下文回调先清选择再加载，普通分页不清选择且卸载后不回调', () => {
	const contexts = []
	const f = fixture({ child: true, parent: 'A', onParentChanged() {
		contexts.push({ instance: this, parent: this.parent_id, child: this.is_son_page })
		this.selectedList = []
		assert.equal(f.calls.length, contexts.length === 1 ? 2 : 3)
	} })
	f.page.selectedList = ['A已选行']
	f.page.getList()
	f.succeed(0, [{ id: 'A第一页' }])
	f.page.paginationChange({ current: 2 })
	f.succeed(1, [{ id: 'A第二页' }])
	assert.deepEqual(f.page.selectedList, ['A已选行'])
	assert.equal(contexts.length, 0)
	f.parent('B')
	assert.equal(contexts[0].instance, f.page)
	assert.equal(contexts[0].parent, 'B')
	assert.deepEqual(f.page.selectedList, [])
	assert.equal(f.calls[2].options.data.parentId, 'B')
	f.page.is_son_page = false
	f.mixin.watch.is_son_page.call(f.page)
	assert.equal(contexts[1].child, false)
	assert.equal(f.calls[2].options.isCurrent(), false)
	f.mixin.beforeUnmount.call(f.page)
	f.parent('C')
	f.mixin.watch.is_son_page.call(f.page)
	assert.equal(contexts.length, 2)
})

test('同一更新周期内同步失败仍递增分页版本，完成后保留确认页码', () => {
	const versions = []
	const f = fixture({ pc: true, behavior(options) {
		const started = f.page.pagination_version
		options.fail({ errMsg: '同步失败' })
		options.complete()
		versions.push({ started, completed: f.page.pagination_version })
		return { abort() {} }
	} })
	assert.equal(f.page.pagination_version, 0)
	f.page.getList()
	assert.equal(versions[0].completed, versions[0].started + 1)
	const beforeRetry = f.page.pagination_version
	f.page.retryListRequest()
	assert.equal(versions[1].started, beforeRetry + 1)
	assert.equal(versions[1].completed, beforeRetry + 2)
	assert.equal(f.page.page_no, 1)
	assert.equal(f.page.load_more_status, 'more')
})

test('复用在途翻页不新增版本，当前完成与空父清理递增，迟到回调不递增', () => {
	const f = fixture({ pc: true, child: true, parent: 'A' })
	f.page.getList()
	f.succeed(0, [{ id: '第一页' }])
	f.page.paginationChange({ current: 2 })
	const loadingVersion = f.page.pagination_version
	f.page.paginationChange({ current: 2 })
	assert.equal(f.page.pagination_version, loadingVersion)
	f.fail(1)
	assert.equal(f.page.pagination_version, loadingVersion + 1)
	assert.equal(f.page.page_no, 1)
	f.page.getList()
	f.page.getList()
	const latestLoadingVersion = f.page.pagination_version
	f.succeed(2, [{ id: '旧请求' }])
	assert.equal(f.page.pagination_version, latestLoadingVersion)
	f.succeed(3, [{ id: '新请求' }])
	assert.equal(f.page.pagination_version, latestLoadingVersion + 1)
	const beforeClear = f.page.pagination_version
	f.parent('')
	assert.equal(f.page.pagination_version, beforeClear + 1)
	assert.equal(f.page.page_no, 1)
})

test('读取查询快照失败会取消旧请求并重置分页版本，保留已确认页和行', () => {
	const f = fixture({ pc: true })
	f.page.getList()
	f.succeed(0, [{ id: '已确认行' }])
	f.page.paginationChange({ current: 2 })
	const pendingVersion = f.page.pagination_version
	f.page.filters = Object.defineProperty({}, '测试字段', { enumerable: true, get() {
		throw new Error('筛选数据无法读取')
	} })
	f.page.getList()
	assert.equal(f.calls[1].task.aborts, 1)
	assert.equal(f.page.pagination_version, pendingVersion + 1)
	assert.equal(f.page.page_no, 1)
	assert.equal(f.page.list_error, '筛选数据无法读取')
	assert.deepEqual(f.page.list, [{ id: '已确认行' }])
	f.succeed(1, [{ id: '过期第二页' }])
	assert.equal(f.page.pagination_version, pendingVersion + 1)
	assert.deepEqual(f.page.list, [{ id: '已确认行' }])
})
