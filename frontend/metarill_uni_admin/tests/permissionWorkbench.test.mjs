import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../common/yzp_runtime/permissionWorkbench.js', import.meta.url), 'utf8')
const { copyPermissionGrants, permissionFingerprint, permissionMenuRows, changePermissionGrant,
	selectedPageActions, invalidPermissionGrants, createPermissionRequestLane, createPermissionWorkbench } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))

/** 同一页面具有两个菜单入口，另一页面没有菜单，按钮授权仍单独存在。 */
function snapshot(mode = 'LEGACY') {
	return { supported: true, roleId: '角色乙', mode, revision: '版本一', issues: [],
		grants: { menuIds: ['菜单甲'], pagePermissions: { 页面甲: ['is_query'] }, buttonIds: [] },
		catalog: { adminTypeId: '后台甲', projectId: '项目甲', issues: [],
			menus: { roots: [{ id: '分组', kind: 'DIRECTORY', name: '业务', children: [
				{ id: '菜单甲', name: '订单', kind: 'GENERATED_PAGE', targetPageId: '页面甲', children: [] },
				{ id: '菜单乙', name: '待处理订单', kind: 'GENERATED_PAGE', targetPageId: '页面甲', children: [] }] }] },
			pages: [{ id: '页面甲', name: '订单', actions: ['is_query', 'is_new'] }, { id: '页面乙', name: '详情', actions: ['is_query'] }],
			buttons: [{ id: '按钮甲', pageId: '页面甲', name: '审批' }], children: [{ parentPageId: '页面甲', childPageId: '页面乙' }] } }
}

test('菜单、页面、按钮分别变更，同页面多入口不互相授予或撤销', () => {
	const value = snapshot(), original = JSON.stringify(value.grants)
	const menus = permissionMenuRows(value.catalog)
	assert.equal(menus[0].grantable, false); assert.equal(menus[1].grantable, true)
	let grants = changePermissionGrant(value.grants, 'menu', '菜单乙', true)
	assert.deepEqual(grants.menuIds, ['菜单乙', '菜单甲'].sort()); assert.deepEqual(grants.pagePermissions, value.grants.pagePermissions)
	grants = changePermissionGrant(grants, 'menu', '菜单甲', false)
	assert.deepEqual(grants.pagePermissions, value.grants.pagePermissions)
	grants = changePermissionGrant(grants, 'page', '页面乙', ['is_query'])
	assert.deepEqual(grants.menuIds, ['菜单乙']); assert.deepEqual(grants.buttonIds, [])
	grants = changePermissionGrant(grants, 'button', '按钮甲', true)
	assert.deepEqual(grants.pagePermissions.页面乙, ['is_query']); assert.equal(JSON.stringify(value.grants), original)
})

test('旧全功能仅预览可用动作，改变一个页面不重写其他页面的兼容值', () => {
	const value = snapshot(); value.grants.pagePermissions.页面甲 = ['is_all']
	assert.deepEqual(selectedPageActions(value.grants, value.catalog.pages[0]), ['is_query', 'is_new'])
	assert.deepEqual(changePermissionGrant(value.grants, 'page', '页面乙', ['is_query']).pagePermissions.页面甲, ['is_all'])
	assert.deepEqual(changePermissionGrant(value.grants, 'page', '页面甲', ['is_new']).pagePermissions.页面甲, ['is_new'])
})

test('全功能必须显式启用或关闭，普通动作全选不生成 is_all 且不授予按钮', () => {
	const value = snapshot(), page = value.catalog.pages[0]
	const ordinary = changePermissionGrant(value.grants, 'page', page.id, page.actions)
	assert.ok(!ordinary.pagePermissions[page.id].includes('is_all'))
	const full = changePermissionGrant(ordinary, 'pageAll', page.id, true)
	assert.ok(full.pagePermissions[page.id].includes('is_all')); assert.deepEqual(full.buttonIds, []); assert.deepEqual(full.menuIds, ordinary.menuIds)
	const revoked = changePermissionGrant(full, 'pageAll', page.id, false)
	assert.deepEqual(revoked, ordinary)
})

test('草稿比较忽略勾选顺序，长 ID 不转换成数字', () => {
	const first = { menuIds: ['9000000000000000021', '1', '1'], pagePermissions: { p: ['is_new', 'is_query'] }, buttonIds: ['2'] }
	const second = { menuIds: ['1', '9000000000000000021'], pagePermissions: { p: ['is_query', 'is_new'] }, buttonIds: ['2'] }
	assert.equal(permissionFingerprint(first), permissionFingerprint(second))
	assert.ok(copyPermissionGrants(first).menuIds.includes('9000000000000000021'))
})

test('失效菜单、页面、动作和按钮显式列出，读取不会静默清理', () => {
	const value = snapshot(); value.grants.menuIds.push('已删除菜单'); value.grants.buttonIds.push('已删除按钮')
	value.grants.pagePermissions.已删除页面 = ['is_all']; value.grants.pagePermissions.页面甲.push('is_delete')
	const before = JSON.stringify(value.grants), invalid = invalidPermissionGrants(value.catalog, value.grants)
	assert.deepEqual(invalid.map(item => item.type).sort(), ['menu', 'page', 'pageAction', 'button'].sort())
	assert.equal(JSON.stringify(value.grants), before)
	const action = invalid.find(item => item.type === 'pageAction')
	const cleaned = changePermissionGrant(value.grants, 'page', action.id, value.grants.pagePermissions[action.id].filter(item => item !== action.action))
	assert.deepEqual(cleaned.pagePermissions.页面甲, ['is_query']); assert.ok(cleaned.pagePermissions.已删除页面)
})

/** 可手动完成、失败或取消请求，并主动发送过期响应。 */
function fixture() {
	const calls = [], states = [], saved = []
	let context = '身份甲'
	const request = options => { const task = { abort() { options.complete({ errMsg: 'abort' }) } }; calls.push({ options, task }); return task }
	const workbench = createPermissionWorkbench({ request, getContext: () => context, onChange: state => states.push(state), onSaved: value => saved.push(value) })
	const succeed = (index, data) => { calls[index].options.success({ code: 200, data }); calls[index].options.complete(data) }
	return { workbench, calls, states, saved, succeed, context(value) { context = value },
		async start(value = snapshot()) {
			const pending = workbench.loadRoles('角色乙')
			succeed(0, { supported: true, roles: [{ id: '角色甲', name: '管理员', isSuper: true }, { id: '角色乙', name: '客服', isSuper: false }] })
			await Promise.resolve()
			assert.equal(calls[1].options.data.roleId, '角色乙')
			succeed(1, value); await pending
		}
	}
}

test('路由指定角色被保留，LEGACY 只预览，激活不提交客户端修改的 grants', async () => {
	const f = fixture(); await f.start()
	assert.equal(f.calls[0].options.method, 'GET'); assert.equal(f.workbench.getState().roleId, '角色乙')
	f.workbench.change('menu', '菜单乙', true)
	assert.deepEqual(f.workbench.getState().grants.menuIds, ['菜单甲'])
	assert.equal(await f.workbench.save(), false); assert.equal(f.calls.length, 2)
	const activation = f.workbench.activate()
	assert.deepEqual(f.calls[2].options.data, { roleId: '角色乙', revision: '版本一' })
	f.succeed(2, { ...snapshot('RESOURCE'), revision: '版本二' })
	assert.equal(await activation, true); assert.equal(f.workbench.getState().snapshot.mode, 'RESOURCE')
})

test('预览问题或 readiness 未满足阻断激活，不发送写请求', async () => {
	const f = fixture(); await f.start({ ...snapshot(), issues: ['尚未接入后端授权检查'] })
	assert.equal(await f.workbench.activate(), false); assert.equal(f.calls.length, 2)
})

test('RESOURCE 有失效问题仍可明确撤销并保存，所有其他授权原样提交', async () => {
	const f = fixture(), value = snapshot('RESOURCE'); value.issues = ['旧按钮已删除']; value.grants.buttonIds = ['旧按钮']
	await f.start(value); f.workbench.change('button', '旧按钮', false)
	const pending = f.workbench.save()
	assert.deepEqual(f.calls[2].options.data.grants, snapshot('RESOURCE').grants)
	f.succeed(2, { ...snapshot('RESOURCE'), revision: '版本二' }); assert.equal(await pending, true)
	assert.equal(f.workbench.getState().baseline, permissionFingerprint(f.workbench.getState().grants))
})

test('保存版本冲突保留草稿和原 revision，不自动覆盖其他人的授权', async () => {
	const f = fixture(); await f.start(snapshot('RESOURCE')); f.workbench.change('page', '页面乙', ['is_query'])
	const pending = f.workbench.save()
	f.calls[2].options.success({ code: 500, msg: '授权已被其他人修改，请重新读取' })
	assert.equal(await pending, false)
	assert.deepEqual(f.workbench.getState().grants.pagePermissions.页面乙, ['is_query'])
	assert.equal(f.workbench.getState().snapshot.revision, '版本一'); assert.match(f.workbench.getState().mutationError, /其他人/)
})

test('只在 supported:false 降级，404/空响应/错误内容均保持不可用', async () => {
	const f = fixture(), unsupported = f.workbench.loadRoles()
	f.succeed(0, { supported: false, roles: [] }); await unsupported
	assert.equal(f.workbench.getState().status, 'unsupported')
	const missing = f.workbench.loadRoles()
	f.calls[1].options.complete({ statusCode: 404 }); await missing
	assert.equal(f.workbench.getState().status, 'error'); assert.match(f.workbench.getState().error, /更新后端/)
	const malformed = f.workbench.loadRoles()
	f.calls[2].options.success({ code: 200, data: { roles: [] } }); await malformed
	assert.equal(f.workbench.getState().status, 'error')
})

test('未知授权模式或损坏文档不进入可编辑界面', async () => {
	for (const value of [{ ...snapshot('RESOURCE'), grants: '损坏内容' }, { ...snapshot(), mode: 'UNKNOWN' }]) {
		const f = fixture(); await f.start(value)
		assert.equal(f.workbench.getState().status, 'error'); assert.equal(f.workbench.getState().snapshot, null)
		assert.equal(await f.workbench.save(), false)
	}
})

test('角色切换隔离乱序响应，登出清空角色与授权且旧 complete 不回写', async () => {
	const f = fixture(); await f.start(snapshot('RESOURCE'))
	const first = f.workbench.loadRole('角色甲'), second = f.workbench.loadRole('角色乙')
	f.succeed(2, { ...snapshot('RESOURCE'), roleId: '角色甲' }); f.succeed(3, snapshot('RESOURCE'))
	await Promise.all([first, second]); assert.equal(f.workbench.getState().roleId, '角色乙')
	const pending = f.workbench.loadRole('角色甲'); f.workbench.clear()
	f.succeed(4, { ...snapshot('RESOURCE'), roleId: '角色甲' }); await pending
	assert.equal(f.workbench.getState().snapshot, null); assert.deepEqual(f.workbench.getState().roles, [])
})

test('身份先于事件变化，旧响应和以新身份提交旧草稿都被拒绝', async () => {
	const f = fixture(); await f.start(snapshot('RESOURCE')); f.workbench.change('menu', '菜单乙', true)
	f.context('身份乙'); assert.equal(await f.workbench.save(), false)
	assert.equal(f.calls.length, 2); assert.equal(f.workbench.getState().snapshot, null)
	const reload = f.workbench.loadRoles(); f.context('身份丙')
	f.succeed(2, { supported: true, roles: [] }); await reload
	assert.equal(f.workbench.getState().status, 'error'); assert.equal(f.workbench.getState().snapshot, null)
})

test('请求未发起与 complete-only 均结束加载，同步成功不被空任务重复失败', async () => {
	const noTask = createPermissionWorkbench({ request: () => undefined, getContext: () => '' })
	await noTask.loadRoles(); assert.equal(noTask.getState().busy, false); assert.match(noTask.getState().error, /请求未发起/)
	const lane = createPermissionRequestLane(options => { options.success({ code: 200, data: { supported: false } }); return undefined })
	assert.deepEqual(await lane.run('roles'), { current: true, data: { supported: false } })
	const f = fixture(), pending = f.workbench.loadRoles(); f.calls[0].options.complete(null); await pending
	assert.equal(f.workbench.getState().busy, false); assert.equal(f.workbench.getState().status, 'error')
})
