import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../common/yzp_runtime/tableStorageMode.js', import.meta.url), 'utf8')
const mode = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
const v8 = { isV8: true, supportsNonRedundant: true }
const v7 = { isV8: false, supportsNonRedundant: false }

test('创建模式默认冗余，可选择非冗余而不修改 CustomControl 输入实体', () => {
	const entity = { id: '', name: '新表' }
	assert.equal(mode.tableStorageMode(entity.isRongyu), 1)
	assert.deepEqual(mode.changeTableStorageMode(entity, '', false, 0, v8), { ...entity, isRongyu: 0 })
	assert.deepEqual(entity, { id: '', name: '新表' })
	assert.equal(mode.tableStorageMode('0'), 0)
	assert.equal(mode.changeTableStorageMode(entity, '', false, 2, v8), null)
})

test('表配置保存后立即固定，路由 ID 与实体 ID 均可锁定，权限只读也不可选择', () => {
	for (const [entity, recordId, readonly] of [[{ id: '已保存', isRongyu: 0 }, '', false],
		[{ isRongyu: 1 }, '详情路由', false], [{ isRongyu: 1 }, '', true]]) {
		assert.equal(mode.changeTableStorageMode(entity, recordId, readonly, 0, v8), null)
	}
	assert.equal(mode.isSavedTableConfig({}, 'undefined'), false)
	assert.equal(mode.isSavedTableConfig({}, 'null'), false)
})

test('V7 已保存表沿用原开关，未知能力和权限只读仍阻止修改', () => {
	const entity = { id: '旧表', isRongyu: 1 }
	assert.deepEqual(mode.changeTableStorageMode(entity, '旧表', false, 0, v7), { id: '旧表', isRongyu: 0 })
	assert.equal(mode.changeTableStorageMode(entity, '旧表', false, 0, null), null)
	assert.equal(mode.changeTableStorageMode(entity, '旧表', true, 0, v7), null)
	assert.equal(mode.changeTableStorageMode({}, '', false, 0, { isV8: true, supportsNonRedundant: false }), null)
	assert.equal(entity.isRongyu, 1)
})

test('真实 Vue 控件按服务端版本隔离新能力，V7 编辑与 V8 冻结均执行 receive 协议', async () => {
	const component = await readFile(new URL('../components/yzp-table-storage-mode/yzp-table-storage-mode.vue', import.meta.url), 'utf8')
	const script = component.match(/<script>([\s\S]*?)<\/script>/)[1]
	const requests = []
	const definition = new Function('yzp', 'tableStorageMode', 'isTableStorageModeLocked', 'changeTableStorageMode',
		script.replace(/^import.*$/gm, '').replace('export default', 'return'))({ request: options => requests.push(options) },
		mode.tableStorageMode, mode.isTableStorageModeLocked, mode.changeTableStorageMode)
	const events = []
	const context = { ...definition.data(), entity: { id: '', isRongyu: 1 }, recordId: '', readonly: false, $emit: (...args) => events.push(args) }
	for (const [name, method] of Object.entries(definition.methods)) context[name] = method.bind(context)
	definition.created.call(context)
	context.changeMode(0)
	assert.equal(events.length, 0)
	assert.equal(requests[0].sendSessionId, true)
	requests[0].success({ code: 200, data: v8 })
	context.changeMode(0)
	assert.deepEqual(events, [['receive', { id: '', isRongyu: 0 }]])
	context.entity = { id: '刚保存', isRongyu: 0 }
	assert.equal(definition.computed.locked.call(context), true)
	context.changeMode(1)
	assert.equal(events.length, 1)
	context.policy = v7
	assert.equal(definition.computed.locked.call(context), false)
	context.changeLegacyMode({ detail: { value: ['1'] } })
	assert.deepEqual(events[1], ['receive', { id: '刚保存', isRongyu: 1 }])
	context.loadPolicy()
	requests[1].fail()
	assert.equal(context.policy, null)
	assert.ok(context.policyError)
	assert.equal(definition.computed.locked.call(context), true)
	context.loadPolicy()
	requests[2].success({ code: 500, msg: '当前设备不属于已登录用户' })
	assert.equal(context.policyError, '当前设备不属于已登录用户')
	assert.equal(context.policy, null)
})
