import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'

const runtimePath = new URL('../common/yzp_runtime/yzp-runtime.js', import.meta.url)
const templatePath = new URL('../../../backend/yzp/src/main/resources/template/v8/runtime/uniapp/common/yzp_runtime/yzp-runtime.js', import.meta.url)
const source = await readFile(runtimePath, 'utf8')
const relationSource = await readFile(new URL('../common/yzp_runtime/relationReadValues.js', import.meta.url), 'utf8')
const relationRuntime = await import('data:text/javascript;base64,' + Buffer.from(relationSource).toString('base64'))

/** 使用真实请求封装和可控时钟，模拟 H5 加载框内部的延迟显示、关闭。 */
function createFixture({ startError = false, loggedIn = true, abortCallbacks = true } = {}) {
	let now = 0
	let nextTimerId = 0
	let loadingType = ''
	const timers = new Map()
	const requests = []
	const dialogs = []
	const errors = []
	const ui = { visible: false, shows: 0, hides: 0 }
	const setTimer = (callback, delay = 0) => {
		const id = ++nextTimerId
		timers.set(id, { callback, time: now + Math.max(0, delay) })
		return id
	}
	/** 按时间和注册顺序运行任务，覆盖关闭定时器与新请求之间的竞争。 */
	const advance = (duration) => {
		const end = now + duration
		while (true) {
			const next = [...timers.entries()].sort((a, b) => a[1].time - b[1].time || a[0] - b[0])[0]
			if (!next || next[1].time > end) break
			now = next[1].time
			timers.delete(next[0])
			next[1].callback()
		}
		now = end
	}
	const uni = {
		getStorageSync: key => key === 'loginStateId' && loggedIn ? '测试登录信息' : '',
		showLoading: () => {
			loadingType = 'loading'
			ui.shows++
			setTimer(() => { ui.visible = true }, 10)
		},
		hideLoading: () => {
			if (loadingType !== 'loading') return
			loadingType = ''
			ui.hides++
			setTimer(() => { ui.visible = false }, 10)
		},
		showModal: options => dialogs.push(options),
		request: options => {
			if (startError) throw new Error('模拟发起请求异常')
			const task = {
				aborts: 0,
				/** 验证包装任务转发原生扩展成员时保留原任务 this。 */
				onHeadersReceived(callback) {
					assert.equal(this, task)
					this.headersCallback = callback
					return '已监听'
				},
				abort() {
					this.aborts++
					if (!abortCallbacks) return
					const error = { errMsg: 'request:fail abort' }
					options.fail(error)
					options.complete(error)
				}
			}
			requests.push({ options, task })
			return task
		}
	}
	const context = vm.createContext({
		...relationRuntime,
		uni,
		utils: { isBlank: value => value == null || value === '' },
		getApp: () => ({ globalData: { environment: { base_url: 'http://test/api' } } }),
		Date: class extends Date { static now() { return now } },
		setTimeout: setTimer,
		clearTimeout: id => timers.delete(id),
		console: { error: (...args) => errors.push(args) }
	})
	// 上传和 XLSX 的外部依赖与请求测试无关；保留其后的整个真实运行时。
	vm.runInContext(source.slice(source.indexOf('const TAB_BAR_PAGES')).replace('export default yzp;', 'globalThis.runtime = yzp;'), context)
	const runtime = context.runtime
	runtime.relogin = () => dialogs.push({ title: '重新登录' })
	runtime.getCurrentPageRedirectUrl = () => '/当前页面'
	/** 模拟 SDK 的 success/fail 后再调用 complete。 */
	const finish = (index, phase = 'success', response = { data: { code: 200, data: {} } }) => {
		requests[index].options[phase](response)
		requests[index].options.complete(response)
	}
	return { runtime, advance, finish, requests, dialogs, errors, ui, timers, uni }
}

test('快速完成的请求取消待显示任务，不出现迟到的加载框', () => {
	const f = createFixture()
	f.runtime.request({ url: '/详情' })
	f.advance(20)
	f.finish(0)
	f.advance(1000)
	assert.equal(f.ui.shows, 0)
	assert.equal(f.ui.visible, false)
	assert.equal(f.timers.size, 0)
})

test('菜单静默请求不弹空响应或服务器错误，仍完成收尾并保留登录过期处理', () => {
	const f = createFixture()
	let completed = 0
	for (const data of [null, { status: 500, message: '模拟错误' }, { code: 500, msg: '登录超时' }]) {
		f.runtime.request({ url: '/菜单角标', showLoading: false, showError: false, complete: () => completed++ })
		f.finish(f.requests.length - 1, 'success', { data })
	}
	assert.equal(completed, 3)
	assert.deepEqual(f.dialogs.map(item => item.title), ['重新登录'])
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
})

test('页面单独刷新权限遇到身份切换，不覆盖菜单已经提交的新权限', () => {
	const f = createFixture(), writes = []
	let context = '甲'
	f.uni.getStorageSync = key => key === 'loginStateId' ? context : ''
	f.uni.setStorageSync = (key, value) => writes.push({ key, value })
	f.runtime.refreshCurrentPermissions(() => assert.fail('旧身份不应通知成功'))
	context = '乙'
	f.finish(0, 'success', { data: { code: 200, data: { isSuper: true } } })
	assert.equal(writes.length, 0)
	assert.equal(f.dialogs.length, 0)
})

test('并发请求只显示一次，全部结束后才关闭', () => {
	const f = createFixture()
	f.runtime.request({ url: '/详情' })
	f.runtime.request({ url: '/子表' })
	f.advance(200)
	f.finish(0)
	f.advance(200)
	assert.equal(f.ui.visible, true)
	assert.equal(f.ui.shows, 1)
	f.finish(1)
	f.advance(100)
	assert.equal(f.ui.visible, false)
	assert.equal(f.ui.hides, 1)
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
})

for (const callbackName of ['success', 'complete']) {
	test(`${callbackName} 中串联新请求，上一请求不能提前关闭加载框`, () => {
		const f = createFixture()
		f.runtime.request({ url: '/权限', [callbackName]: () => f.runtime.request({ url: '/详情' }) })
		f.advance(170)
		f.finish(0)
		f.advance(200)
		assert.equal(f.ui.visible, true)
		assert.equal(f.ui.hides, 0)
		f.finish(1)
		f.advance(100)
		assert.equal(f.ui.visible, false)
	})
}

test('H5 显示后立刻完成，关闭任务遇到新请求会取消', () => {
	const f = createFixture()
	f.runtime.request({ url: '/第一批' })
	f.advance(151)
	f.finish(0)
	f.advance(10)
	f.runtime.request({ url: '/第二批' })
	f.advance(100)
	assert.equal(f.ui.visible, true)
	assert.equal(f.ui.hides, 0)
	f.finish(1)
	f.advance(100)
	assert.equal(f.ui.visible, false)
})

for (const phase of ['success', 'fail']) {
	test(`${phase} 回调抛错仍释放加载框，并且 complete 只调用一次`, () => {
		const f = createFixture()
		let completed = 0
		f.runtime.request({ url: '/异常回调', [phase]: () => { throw new Error('业务回调异常') }, complete: () => completed++ })
		f.advance(200)
		f.finish(0, phase)
		f.advance(100)
		assert.equal(f.ui.visible, false)
		assert.equal(completed, 1)
		assert.equal(f.errors.length, 1)
	})
}

test('重复和迟到回调不能多扣计数或重复写入页面', () => {
	const f = createFixture()
	let successes = 0
	f.runtime.request({ url: '/已结束', success: () => successes++ })
	f.runtime.request({ url: '/仍在执行' })
	f.advance(200)
	f.finish(0)
	f.finish(0)
	f.finish(0, 'fail')
	f.advance(100)
	assert.equal(successes, 1)
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 1)
	assert.equal(f.ui.visible, true)
	f.finish(1)
	f.advance(100)
	assert.equal(f.ui.visible, false)
})

test('同步发起异常走 fail 和 complete，取消待显示任务', () => {
	const f = createFixture({ startError: true })
	let error
	let completed = 0
	f.runtime.request({ url: '/发起失败', fail: res => { error = res }, complete: () => completed++ })
	f.advance(1000)
	assert.match(error.errMsg, /模拟发起请求异常/)
	assert.equal(completed, 1)
	assert.equal(f.ui.visible, false)
	assert.equal(f.timers.size, 0)
})

test('SDK 不回调时超时兜底终止请求，并忽略之后的响应', () => {
	const f = createFixture()
	let failed = 0
	let completed = 0
	let succeeded = 0
	f.runtime.request({ url: '/超时', timeout: 300, fail: res => { assert.match(res.errMsg, /timeout/); failed++ }, success: () => succeeded++, complete: () => completed++ })
	assert.equal(f.requests[0].options.timeout, 300)
	f.advance(1400)
	assert.equal(f.requests[0].task.aborts, 1)
	assert.equal(failed, 1)
	assert.equal(completed, 1)
	assert.equal(f.ui.visible, false)
	f.finish(0)
	assert.equal(succeeded, 0)
	assert.equal(completed, 1)
})

test('一个请求超时不会关闭其他仍在执行的请求', () => {
	const f = createFixture()
	f.runtime.request({ url: '/短请求', timeout: 200, fail() {} })
	f.runtime.request({ url: '/长请求', timeout: 5000 })
	f.advance(1300)
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 1)
	assert.equal(f.ui.visible, true)
	f.finish(1)
	f.advance(100)
	assert.equal(f.ui.visible, false)
})

test('默认超时为一分钟，没有失败回调时显示明确的超时提示', () => {
	const f = createFixture()
	f.runtime.request({ url: '/无回调' })
	assert.equal(f.requests[0].options.timeout, 60000)
	f.advance(61100)
	assert.match(f.dialogs[0].content, /请求超时/)
	assert.equal(f.ui.visible, false)
})

test('主动取消请求也会正常释放加载框', () => {
	const f = createFixture()
	let failed = 0
	const task = f.runtime.request({ url: '/取消', fail: res => { assert.match(res.errMsg, /abort/); failed++ } })
	f.advance(200)
	task.abort()
	f.advance(100)
	assert.equal(failed, 1)
	assert.equal(f.ui.visible, false)
})

test('平台只调用 complete 也能释放；网络失败保留 errMsg', () => {
	const f = createFixture()
	let error
	f.runtime.request({ url: '/网络失败', fail: res => { error = res } })
	f.advance(200)
	f.requests[0].options.complete({ errMsg: 'request:fail network error' })
	f.advance(100)
	assert.equal(error.errMsg, 'request:fail network error')
	assert.equal(f.ui.visible, false)
})

test('静默请求不显示加载框，也不阻止可见请求关闭', () => {
	const f = createFixture()
	f.runtime.request({ url: '/后台刷新', showLoading: false })
	f.runtime.request({ url: '/详情' })
	f.advance(200)
	f.finish(1)
	f.advance(100)
	assert.equal(f.ui.visible, false)
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 1)
	f.finish(0)
	assert.equal(f.ui.hides, 1)
})

test('缺少登录信息直接退出；登录过期和服务器错误正常收尾', () => {
	const unauthenticated = createFixture({ loggedIn: false })
	unauthenticated.runtime.request({ url: '/需登录', sendSessionId: true })
	assert.equal(unauthenticated.requests.length, 0)
	assert.equal(unauthenticated.timers.size, 0)
	for (const data of [{ code: 500, msg: '登录超时' }, { status: 500, message: '服务器错误' }, null]) {
		const f = createFixture()
		let completed = 0
		f.runtime.request({ url: '/详情', complete: () => completed++ })
		f.advance(200)
		f.finish(0, 'success', { data })
		f.advance(100)
		assert.equal(f.dialogs.length, 1)
		assert.equal(f.ui.visible, false)
		assert.equal(completed, 1)
	}
})

test('重用请求配置不会重复拼接 URL，成功和 complete 保留业务响应与回调上下文', () => {
	const f = createFixture()
	const response = { code: 200, data: { id: '123' } }
	const headers = { example: '原值' }
	const options = { url: '/详情', header: headers, sendSessionId: true, success(data) { assert.equal(this, options); assert.equal(data, response) }, complete(data) { assert.equal(data, response) } }
	f.runtime.request(options)
	f.finish(0, 'success', { data: response })
	f.runtime.request(options)
	f.finish(1, 'success', { data: response })
	assert.equal(options.url, '/详情')
	assert.equal(headers.loginStateId, undefined)
	assert.equal(f.requests[1].options.url, 'http://test/api/详情')
})

test('诊断记录有容量限制，不包含查询参数、登录头、请求体和响应体', () => {
	const f = createFixture()
	for (let i = 0; i < 60; i++) {
		f.runtime.request({ url: '/详情?secret=查询机密', data: { secret: '请求机密' }, sendSessionId: true })
		f.finish(i, 'success', { data: { code: 200, secret: '响应机密' } })
	}
	const state = f.runtime.getRequestLoadingState()
	assert.equal(state.events.length, 80)
	assert.doesNotMatch(JSON.stringify(state), /机密|测试登录信息/)
	state.events[0].phase = '外部改动'
	assert.notEqual(f.runtime.getRequestLoadingState().events[0].phase, '外部改动')
})



for (const scenario of [
	{ name: '成功数据', phase: 'success', response: { data: { code: 200, data: { records: ['旧数据'] } } } },
	{ name: '网络失败', phase: 'fail', response: { errMsg: 'request:fail network error' } },
	{ name: '空响应', phase: 'success', response: { data: null } },
	{ name: '服务器错误', phase: 'success', response: { data: { status: 500, message: '旧服务器错误' } } },
	{ name: '登录超时', phase: 'success', response: { data: { code: 500, msg: '登录超时' } } }
]) {
	test(`过期请求的${scenario.name}不提示、不调用业务，只完成一次资源收尾`, () => {
		const f = createFixture()
		let current = true
		let successes = 0
		let failures = 0
		let completed = 0
		const options = {
			url: '/旧列表',
			isCurrent() { assert.equal(this, options); return current },
			success: () => successes++, fail: () => failures++, complete: () => completed++
		}
		f.runtime.request(options)
		f.advance(200)
		current = false
		f.finish(0, scenario.phase, scenario.response)
		f.finish(0, scenario.phase, scenario.response)
		f.advance(100)
		assert.equal(successes, 0)
		assert.equal(failures, 0)
		assert.equal(completed, 1)
		assert.equal(f.dialogs.length, 0)
		assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
		assert.equal(f.ui.visible, false)
		assert.equal(f.timers.size, 0)
	})
}

test('过期请求没有 fail 回调也不显示默认错误，超时仍终止 SDK 请求', () => {
	for (const timeout of [false, true]) {
		const f = createFixture()
		let completed = 0
		f.runtime.request({ url: '/旧列表', timeout: 100, isCurrent: () => false, complete: () => completed++ })
		if (timeout) f.advance(1200)
		else f.finish(0, 'fail', { errMsg: 'request:fail network error' })
		f.advance(100)
		assert.equal(f.dialogs.length, 0)
		assert.equal(completed, 1)
		assert.equal(f.requests[0].task.aborts, timeout ? 1 : 0)
		assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
	}
})

test('当前请求接入守卫后仍执行成功、失败及公共错误提示', () => {
	for (const data of [null, { status: 500, message: '服务器错误' }, { code: 500, msg: '登录超时' }]) {
		const f = createFixture()
		let completed = 0
		f.runtime.request({ url: '/当前列表', isCurrent: () => true, complete: () => completed++ })
		f.finish(0, 'success', { data })
		assert.equal(f.dialogs.length, 1)
		assert.equal(completed, 1)
	}
	const f = createFixture()
	let successes = 0
	let failures = 0
	f.runtime.request({ url: '/当前列表', isCurrent: () => true, success: () => successes++ })
	f.finish(0)
	f.runtime.request({ url: '/当前列表', isCurrent: () => true, fail: () => failures++ })
	f.finish(1, 'fail', { errMsg: 'request:fail network error' })
	assert.equal(successes, 1)
	assert.equal(failures, 1)
	assert.equal(f.dialogs.length, 0)
})

test('主动取消无需 SDK 回调即可释放凭据和超时任务，重复取消或迟到响应不再收尾', () => {
	const f = createFixture({ abortCallbacks: false })
	let failures = 0
	let completed = 0
	let successes = 0
	const task = f.runtime.request({
		url: '/待取消', timeout: 5000,
		fail: error => { assert.match(error.errMsg, /abort/); failures++ },
		success: () => successes++, complete: () => completed++
	})
	f.advance(200)
	task.abort()
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
	assert.equal(completed, 1)
	assert.equal(failures, 1)
	task.abort()
	assert.equal(f.requests[0].task.aborts, 1)
	f.finish(0)
	f.advance(6100)
	assert.equal(successes, 0)
	assert.equal(completed, 1)
	assert.equal(failures, 1)
	assert.equal(f.ui.visible, false)
	assert.equal(f.timers.size, 0)
})

test('先使旧请求失效再取消，不触发旧 fail；不能关闭新的有效请求加载框', () => {
	const f = createFixture()
	let current = true
	let completed = 0
	let failures = 0
	const oldTask = f.runtime.request({ url: '/旧列表', isCurrent: () => current, fail: () => failures++, complete: () => completed++ })
	f.runtime.request({ url: '/新列表' })
	f.advance(200)
	current = false
	oldTask.abort()
	f.advance(100)
	assert.equal(completed, 1)
	assert.equal(failures, 0)
	assert.equal(f.dialogs.length, 0)
	assert.equal(f.ui.visible, true)
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 1)
	f.finish(1)
	f.advance(100)
	assert.equal(f.ui.visible, false)
})

test('返回任务保留扩展成员并将原生方法绑定到原任务', () => {
	const f = createFixture()
	const task = f.runtime.request({ url: '/任务接口' })
	const callback = () => {}
	assert.equal(task.onHeadersReceived(callback), '已监听')
	assert.equal(f.requests[0].task.headersCallback, callback)
	task.marker = '调用方标记'
	assert.equal(f.requests[0].task.marker, '调用方标记')
	assert.equal(task.aborts, 0)
	f.finish(0)
	task.abort()
	assert.equal(task.aborts, 0)
})

test('时效守卫异常不会泄漏资源或提交业务副作用', () => {
	const f = createFixture()
	let completed = 0
	let successes = 0
	f.runtime.request({ url: '/守卫异常', isCurrent() { throw new Error('守卫异常') }, success: () => successes++, complete: () => completed++ })
	f.finish(0)
	assert.equal(successes, 0)
	assert.equal(completed, 1)
	assert.equal(f.errors.length, 1)
	assert.equal(f.dialogs.length, 0)
	assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
	assert.equal(f.timers.size, 0)
})
