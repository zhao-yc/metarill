import test from 'node:test'
import assert from 'node:assert/strict'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'

const source = await readFile(new URL('../components/yzp-control-detail-editor/yzp-control-detail-editor.vue', import.meta.url), 'utf8')

/** 执行真实组件方法与 computed，仅替换 uni 宿主和 HTTP 回调，不另造字段继承实现。 */
function fixture() {
	const requests = []
	const context = { yzp: { request: options => requests.push(options) } }
	const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;\s*$/gm, '').replace('export default', 'globalThis.options=')
	vm.runInNewContext(script, context)
	const options = context.options
	const state = { ...options.data(), entity: { configColumnId: '字段甲', controlDetail: '' }, controlKind: '', columnName: '', columnType: '', $nextTick: callback => callback() }
	for (const [name, method] of Object.entries(options.methods)) state[name] = method.bind(state)
	for (const [name, getter] of Object.entries(options.computed)) Object.defineProperty(state, name, { get: () => getter.call(state) })
	return { state, requests }
}

test('字段继承使用专用认证接口和明确 id，不放宽标准页面父范围', () => {
	const { state, requests } = fixture()
	state.ensureConfigColumnMeta('字段甲')
	assert.equal(requests.length, 1)
	assert.equal(requests[0].url, '/admin/custom/config_column/control_metadata')
	assert.equal(requests[0].method, 'POST')
	assert.equal(requests[0].sendSessionId, true)
	assert.deepEqual(JSON.parse(JSON.stringify(requests[0].data)), { id: '字段甲' })
	assert.doesNotMatch(source, /6217715532627968\/detail\//)
})

test('原控件明细字节保持，继承条件继续进入真实编辑表单与保存参数', () => {
	const { state, requests } = fixture()
	const definition = ' {\n "table":"mt_target", "whereSql":"is_enabled = 1", "orderBy":"sort_no asc,id desc", "futureOption":{"keep":true}\n } '
	state.ensureConfigColumnMeta('字段甲')
	requests[0].success({ code: 200, data: { controlKind: 'List', columnName: 'target_id', columnType: 'bigint', name: '目标字段', controlDetail: definition } })
	assert.equal(state.fetchedColumnMeta.controlDetail, definition)
	assert.equal(state.resolvedFieldTitle, '目标字段')
	state.syncFromEntity()
	const payload = JSON.parse(state.buildListPayload())
	assert.equal(payload.whereSql, 'is_enabled = 1')
	assert.equal(payload.orderBy, 'sort_no asc,id desc')
	state.ensureConfigColumnMeta('字段甲')
	// 同一字段命中缓存；syncFromEntity 只会另发已选表元信息请求。
	assert.equal(requests.filter(request => request.url.endsWith('/control_metadata')).length, 1)
})

test('迟到及失败响应不污染新字段，显式控件类型继续使用当前实体', () => {
	const { state, requests } = fixture()
	state.ensureConfigColumnMeta('字段甲')
	state.entity.configColumnId = '字段乙'
	requests[0].success({ code: 200, data: { controlKind: 'List', controlDetail: '旧条件' } })
	assert.equal(state.fetchedColumnMeta.controlKind, '')
	state.ensureConfigColumnMeta('字段乙')
	requests[1].success({ code: 500, data: { controlKind: 'List', controlDetail: '失败条件' } })
	assert.equal(state.lastFetchedConfigColumnId, '')
	state.entity.controlKind = 'Input'
	state.ensureConfigColumnMeta('字段乙')
	assert.equal(requests.length, 2)
	delete state.entity.controlKind
	state.ensureConfigColumnMeta('')
	assert.equal(state.fetchedColumnMeta.controlDetail, '')
})

test('单选和多选择页使用同一专用候选接口并显式绑定类型，迟到结果不能混用', () => {
  const { state, requests } = fixture()
  state.entity.controlKind = 'SelectPage'
  state.ensureConfigPageOptions()
  assert.equal(requests.length, 1)
  assert.equal(requests[0].url, '/admin/custom/config_column/select_page_options')
  assert.equal(requests[0].sendSessionId, true)
  assert.deepEqual(JSON.parse(JSON.stringify(requests[0].data)), { controlKind: 'SelectPage' })
  state.entity.controlKind = 'MultiSelectPage'
  requests[0].success({ code: 200, data: [{ id: '单选页', name: '旧结果', value: '单选页', text: '旧结果' }] })
  assert.equal(state.configPageOptions.length, 1)
  requests[0].complete()
  assert.equal(requests.length, 2)
  assert.equal(requests[1].url, '/admin/custom/config_column/select_page_options')
  assert.deepEqual(JSON.parse(JSON.stringify(requests[1].data)), { controlKind: 'MultiSelectPage' })
  requests[1].success({ code: 200, data: [{ id: '多选页', name: '多选目标', value: '多选页', text: '多选目标' }] })
  requests[1].complete()
  assert.equal(state.loadedConfigPageOptionsControlKind, 'MultiSelectPage')
  assert.equal(state.configPageOptions[1].id, '多选页')
  state.ensureConfigPageOptions()
  assert.equal(requests.length, 2)
  assert.doesNotMatch(source, /findNameIdConfigPage1676901024972058625/)
})
