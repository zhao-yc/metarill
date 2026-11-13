import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../common/menuWorkbench.js', import.meta.url), 'utf8')
const runtime = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
const { flattenMenuTree, createMenuDraft, menuDraftFingerprint, menuParentOptions, validateMenuDraft,
	buildMenuSaveRequest, createWorkbenchRequestLane, readWorkbenchIdentity } = runtime

/** 构造两个分组，覆盖有子项目录的跨组移动和三级边界。 */
function snapshot() {
	const leaf = { id: '9000000000000000011', parentId: '目录', firstMenuId: '分组甲', kind: 'GENERATED_PAGE', name: '订单', sortNo: 1, targetPageId: '页面甲', children: [] }
	return { projectId: '项目甲', adminTypeId: '后台甲', revision: '版本甲', isV8: true, canWrite: true,
		capabilities: { first: { create: true, update: true, delete: true }, second: { create: true, update: true, delete: true } },
		adminTypes: [{ id: '后台甲', name: '后台甲' }],
		targets: [{ id: '页面甲', kind: 'GENERATED_PAGE', name: '订单列表', route: '/pages/yzp/orders-list-1', showCountSupported: true },
			{ id: '自定义甲', kind: 'CUSTOM_PAGE', name: '工作台', route: '/pages/custom/work', showCountSupported: false }],
		tree: [{ id: '分组甲', parentId: null, firstMenuId: '分组甲', kind: 'DIRECTORY', name: '业务', sortNo: 10,
			children: [{ id: '目录', parentId: '分组甲', firstMenuId: '分组甲', kind: 'DIRECTORY', name: '交易', sortNo: 10, children: [leaf] }] },
			{ id: '分组乙', parentId: null, firstMenuId: '分组乙', kind: 'DIRECTORY', name: '系统', sortNo: 20, children: [] }] }
}

test('树展开保留长标识、完整路径和根分组语义，不改变输入', () => {
	const value = snapshot(), original = JSON.stringify(value)
	const nodes = flattenMenuTree(value.tree)
	assert.deepEqual(nodes.map(item => item.depth), [0, 1, 2, 0])
	assert.equal(nodes[2].id, '9000000000000000011')
	assert.deepEqual(nodes[2].path, ['业务', '交易', '订单'])
	assert.equal(createMenuDraft(nodes[0]).kind, 'FIRST_MENU')
	assert.equal(createMenuDraft(nodes[1]).kind, 'DIRECTORY')
	assert.equal(JSON.stringify(value), original)
})

test('父选项禁止循环、第四级、带子项目录跨分组；叶子可跨组移动', () => {
	const value = snapshot(), nodes = flattenMenuTree(value.tree)
	assert.deepEqual(menuParentOptions(value.tree, createMenuDraft(nodes[1])).map(item => item.value), ['分组甲'])
	assert.deepEqual(menuParentOptions(value.tree, createMenuDraft(nodes[2])).map(item => item.value), ['分组甲', '目录', '分组乙'])
	assert.ok(!menuParentOptions(value.tree, createMenuDraft(nodes[1])).some(item => item.value === '目录'))
})

test('无效版本、父节点、目标和角标在保存前拒绝', () => {
	const value = snapshot(), draft = createMenuDraft(flattenMenuTree(value.tree)[2])
	assert.equal(validateMenuDraft(value, draft), '')
	assert.match(validateMenuDraft({ ...value, isV8: false }, draft), /不可编辑/)
	assert.match(validateMenuDraft({ ...value, canWrite: false }, draft), /不可编辑/)
	assert.match(validateMenuDraft(value, { ...draft, parentId: draft.id }), /上级/)
	assert.match(validateMenuDraft(value, { ...draft, targetPageId: '其他后台页面' }), /目标页面/)
	assert.match(validateMenuDraft(value, { ...draft, kind: 'CUSTOM_PAGE', targetPageId: '自定义甲', showCount: true }), /角标/)
	assert.match(validateMenuDraft(value, { ...draft, sortNo: '' }), /整数/)
	assert.match(validateMenuDraft(value, { ...draft, sortNo: 2147483648 }), /整数/)
	assert.match(validateMenuDraft(value, { ...draft, kind: '未知类型' }), /菜单类型/)
})

test('保存使用快照上下文和版本，客户端不能注入路由或表标识', () => {
	const value = snapshot(), draft = createMenuDraft(flattenMenuTree(value.tree)[2])
	const result = buildMenuSaveRequest(value, { ...draft, projectId: '伪造项目', adminTypeId: '伪造后台',
		route: '/任意页面', targetTableId: '其他表', name: ' 订单入口 ', showCount: true, sortNo: '20' })
	assert.equal(result.projectId, '项目甲'); assert.equal(result.adminTypeId, '后台甲')
	assert.equal(result.revision, '版本甲'); assert.equal(result.name, '订单入口'); assert.equal(result.sortNo, 20)
	assert.equal(result.showCount, true); assert.equal(result.route, undefined); assert.equal(result.targetTableId, undefined)
	const directory = buildMenuSaveRequest(value, { ...draft, kind: 'DIRECTORY', targetPageId: '页面甲', showCount: true })
	assert.equal(directory.targetPageId, ''); assert.equal(directory.showCount, false)
})

test('新建分组和已有历史入口保留各自语义，草稿比较忽略展示字段', () => {
	const draft = { ...createMenuDraft(null), kind: 'FIRST_MENU', name: '新分组' }
	assert.equal(buildMenuSaveRequest(snapshot(), draft).kind, 'FIRST_MENU')
	assert.equal(buildMenuSaveRequest(snapshot(), draft).id, '')
	assert.equal(menuDraftFingerprint(draft), menuDraftFingerprint({ ...draft, children: [] }))
	assert.notEqual(menuDraftFingerprint(draft), menuDraftFingerprint({ ...draft, name: '其他名称' }))
	assert.match(validateMenuDraft(snapshot(), { ...draft, kind: 'LEGACY_PAGE', parentId: '分组甲' }), /新菜单/)
})

/** 保留取消后的回调，重现旧项目响应迟到的情形。 */
function requestFixture() {
	const requests = []
	const lane = createWorkbenchRequestLane(options => {
		const task = { aborts: 0, abort() { this.aborts++; options.fail({ errMsg: 'abort' }) } }
		requests.push({ options, task }); return task
	})
	return { lane, requests }
}

test('读取通道取消旧请求，并隔离旧项目迟到的成功和失败', async () => {
	const f = requestFixture()
	const first = f.lane.run('/snapshot', { projectId: '甲' })
	const second = f.lane.run('/snapshot', { projectId: '乙' })
	assert.deepEqual(await first, { current: false })
	assert.equal(f.requests[0].task.aborts, 1)
	assert.equal(f.requests[0].options.isCurrent(), false)
	f.requests[0].options.success({ code: 200, data: '甲' })
	f.requests[0].options.fail({ errMsg: '迟到错误' })
	f.requests[1].options.success({ code: 200, data: '乙' })
	assert.deepEqual(await second, { current: true, data: '乙' })
	assert.equal(f.requests[1].options.showLoading, false)
	assert.equal(f.requests[1].options.showError, false)
	assert.equal(f.requests[1].options.sendSessionId, true)
})

test('卸载使请求失效，业务失败和网络失败后均可重试', async () => {
	const f = requestFixture(), first = f.lane.run('/snapshot')
	f.lane.invalidate(); assert.deepEqual(await first, { current: false })
	const failed = f.lane.run('/snapshot')
	f.requests[1].options.success({ code: 500, msg: '配置已被修改' })
	await assert.rejects(failed, /配置已被修改/)
	const network = f.lane.run('/snapshot')
	f.requests[2].options.fail({})
	await assert.rejects(network, /网络暂不可用/)
	const retry = f.lane.run('/snapshot')
	f.requests[3].options.success({ code: 200, data: snapshot() })
	assert.equal((await retry).data.projectId, '项目甲')
})

test('空响应及仅 complete 的服务端错误均收尾，不留下永久加载状态', async () => {
	const f = requestFixture()
	for (const [response, expected] of [[null, /未取得有效菜单配置/], [{ status: 500, message: '服务器处理失败' }, /服务器处理失败/],
		[{ code: 500, msg: '登录超时' }, /登录超时/]]) {
		const pending = f.lane.run('/snapshot')
		f.requests.at(-1).options.complete(response)
		await assert.rejects(pending, expected)
		assert.equal(f.requests.at(-1).options.isCurrent(), false)
	}
})

test('未创建请求任务立即失败，同步成功后返回空任务不重复收尾', async () => {
	await assert.rejects(createWorkbenchRequestLane(() => undefined).run('/snapshot'), /请求未发起.*登录/)
	const lane = createWorkbenchRequestLane(options => {
		options.success({ code: 200, data: '同步结果' })
		options.complete(null)
		return undefined
	})
	assert.deepEqual(await lane.run('/snapshot'), { current: true, data: '同步结果' })
})

test('登出使旧请求失效后，旧 complete 和成功回调都不影响新请求', async () => {
	const f = requestFixture(), old = f.lane.run('/snapshot')
	f.lane.invalidate()
	assert.deepEqual(await old, { current: false })
	const fresh = f.lane.run('/snapshot')
	f.requests[0].options.complete({ code: 500, msg: '登录超时' })
	f.requests[0].options.success({ code: 200, data: '旧账号菜单' })
	assert.equal(f.requests[1].options.isCurrent(), true)
	f.requests[1].options.success({ code: 200, data: '新账号菜单' })
	f.requests[1].options.complete(null)
	assert.deepEqual(await fresh, { current: true, data: '新账号菜单' })
})

test('身份或项目在事件到达前变化，响应也不能落入当前工作台', async () => {
	const state = { loginStateId: '登录甲', userInfo: { id: '账号甲' }, sysParams: { yzProjectId: '项目甲', deviceId: '设备甲' } }
	let options, invalidations = 0
	const lane = createWorkbenchRequestLane(value => { options = value; return { abort() {} } }, {
		getContext: () => readWorkbenchIdentity(key => state[key]), onContextChanged: () => invalidations++ })
	const first = lane.run('/snapshot')
	state.loginStateId = '登录乙'
	options.success({ code: 200, data: '迟到数据' })
	assert.deepEqual(await first, { current: false }); assert.equal(invalidations, 1)
	const second = lane.run('/snapshot')
	state.sysParams.yzProjectId = '项目乙'
	assert.equal(options.isCurrent(), false)
	assert.deepEqual(await second, { current: false }); assert.equal(invalidations, 2)
})

// Windows 检出可能使用 CRLF，先统一换行再移除真实组件的导入声明。
const componentSource = (await readFile(new URL('../components/yzp-menu-workbench/yzp-menu-workbench.vue', import.meta.url), 'utf8')).replace(/\r\n/g, '\n')
const script = componentSource.match(/<script>([\s\S]*?)<\/script>/)[1]
	.replace(/import yzp[^\n]*\n/, 'const yzp = {}\n')
	.replace(/import \{[\s\S]*?from '@\/common\/menuWorkbench'\n/, '')
const { default: component } = await import('data:text/javascript;base64,' + Buffer.from(source + '\n' + script).toString('base64'))

/** 只装配工作台状态及方法，直接验证权限、草稿和回读行为。 */
function componentFixture() {
	const cache = new Map(), events = []
	let confirm = true
	globalThis.uni = { getStorageSync: key => key === 'userInfo' ? { id: '用户甲' } : cache.get(key),
		setStorageSync: (key, value) => cache.set(key, value), removeStorageSync: key => cache.delete(key),
		showModal: options => options.success({ confirm }), showToast() {}, $emit: (...args) => events.push(args) }
	const view = { ...component.data(), $emit: (...args) => events.push(args) }
	for (const [key, method] of Object.entries(component.methods)) view[key] = method.bind(view)
	for (const [key, method] of Object.entries(component.computed)) Object.defineProperty(view, key, { get: () => method.call(view) })
	view.projectId = '项目甲'; view.loading = false; view._loadVersion = 1; view._draftOwnerId = '用户甲'
	view._editIdentity = readWorkbenchIdentity(key => globalThis.uni.getStorageSync(key))
	view.applySnapshot(snapshot())
	return { view, cache, events, setConfirm(value) { confirm = value } }
}

test('操作能力缺失默认只读，V7即使附带能力也不能编辑', () => {
	const { view } = componentFixture()
	assert.equal(view.canEdit, true)
	view.snapshot.capabilities = undefined; assert.equal(view.canEdit, false)
	view.snapshot = { ...snapshot(), isV8: false }; assert.equal(view.canEdit, false)
})

test('取消上下文切换保留草稿，确认放弃后离开不会重新写入缓存', async () => {
	const f = componentFixture(), { view } = f
	view.draft.name = '未保存名称'; view.persistDraft()
	assert.equal(f.cache.size, 1)
	f.setConfirm(false); assert.equal(await view.canLeave(), false); assert.equal(view.dirty, true)
	f.setConfirm(true); assert.equal(await view.canLeave(), true); assert.equal(view.dirty, false)
	view.persistDraft(); assert.equal(f.cache.size, 0)
})

test('相同版本草稿恢复可编辑，旧版本恢复只读且不洗掉原版本号', () => {
	const { view, cache } = componentFixture()
	view.draft.name = '未保存名称'; view.persistDraft()
	const key = view.draftKey()
	view.applySnapshot(snapshot()); view.restoreDraft()
	assert.equal(view.draft.name, '未保存名称'); assert.equal(view.recovered, true); assert.equal(view.staleDraft, false)
	view.applySnapshot({ ...snapshot(), revision: '版本乙' }); view.restoreDraft()
	assert.equal(view.staleDraft, true); assert.equal(view.editorDisabled, true)
	view.persistDraft(); assert.equal(cache.get(key).revision, '版本甲')
})

test('草稿键固定为编辑时身份，账号切换和登出不会串写或恢复旧草稿', () => {
	const { view, cache } = componentFixture()
	view.draft.name = '旧账号草稿'
	globalThis.uni.getStorageSync = key => key === 'userInfo' ? { id: '用户乙' } : cache.get(key)
	view.persistDraft()
	assert.equal([...cache.keys()][0], 'yzp-menu-draft:用户甲:项目甲:后台甲')
	view._catalogLane = view._snapshotLane = view._mutationLane = { invalidate() {} }
	cache.clear(); view.invalidateIdentity(); view.persistDraft()
	assert.equal(view.snapshot, null); assert.equal(view.draft, null); assert.equal(cache.size, 0)
})

test('账号在提交前变化时清空旧编辑区，不能以新身份发送旧草稿', async () => {
	const { view } = componentFixture()
	view.draft.name = '旧账号草稿'
	let writes = 0
	view._catalogLane = view._snapshotLane = { invalidate() {} }
	view._mutationLane = { invalidate() {}, run() { writes++; return Promise.resolve({ current: true, data: snapshot() }) } }
	globalThis.uni.getStorageSync = key => key === 'userInfo' ? { id: '用户乙' } : null
	await view.save()
	assert.equal(writes, 0); assert.equal(view.snapshot, null); assert.equal(view.draft, null)
})

test('成功保存回读服务端树，清除草稿并通知指定项目；并发错误保留编辑', async () => {
	const { view, cache, events } = componentFixture()
	view.draft.name = '新名称'; view.persistDraft()
	const next = snapshot(); next.revision = '版本乙'; next.tree[0].name = '新名称'; next.selectedId = '分组甲'
	view._mutationLane = { run: async () => ({ current: true, data: next }) }
	await view.save()
	assert.equal(view.snapshot.revision, '版本乙'); assert.equal(view.draft.name, '新名称'); assert.equal(view.dirty, false)
	assert.equal(cache.size, 0)
	assert.ok(events.some(event => event[0] === 'onMenuConfigurationChanged' && event[1].projectId === '项目甲'))
	view.draft.name = '并发草稿'
	view._mutationLane = { run: async () => { throw new Error('其他人已修改，请重新读取') } }
	await view.save()
	assert.equal(view.draft.name, '并发草稿'); assert.equal(view.dirty, true); assert.match(view.mutationError, /其他人已修改/)
})
