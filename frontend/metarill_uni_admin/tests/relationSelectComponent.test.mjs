import test from 'node:test'
import assert from 'node:assert/strict'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'
const selector = await readFile(new URL('../components/zxz-uni-data-select/zxz-uni-data-select.vue', import.meta.url), 'utf8')
const wrapper = await readFile(new URL('../components/yzp-relation-select/yzp-relation-select.vue', import.meta.url), 'utf8')
const runtimeSource = await readFile(new URL('../common/yzp_runtime/remoteRelationOptions.js', import.meta.url), 'utf8')
const mixinSource = await readFile(new URL('../common/yzp_runtime/relationOptionsMixin.js', import.meta.url), 'utf8')
const { createRemoteRelationOptions } = await import('data:text/javascript;base64,' + Buffer.from(runtimeSource).toString('base64'))
const plain = value => JSON.parse(JSON.stringify(value))
const option = (id, text = id) => ({ id, name: text, value: id, text })
/** 实际 Vue 脚本只替换 uni 宿主和网络，保留组件方法/计算属性。 */
function evaluate(source, context) { const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;\s*$/gm, '').replace('export default', 'globalThis.options='); vm.runInNewContext(script, context); return context.options }
function methods(options, state) { for (const [key, method] of Object.entries(options.methods)) state[key] = method.bind(state); for (const [key, getter] of Object.entries(options.computed || {})) Object.defineProperty(state, key, { get: () => getter.call(state) }); return state }
function selectorFixture({ remote = false, multiple = false, value = '' } = {}) {
 const events = [], options = evaluate(selector, { uniCloud: { mixinDatacom: {} }, console: { log() {} } })
 const state = methods(options, { ...options.data(), remote, multiple, modelValue: value, dataKey: 'text', dataValue: 'value', disabled: false, filterable: true, remoteLoading: false, remoteHasMore: true, collection: '', format: '', defItem: 0, filterInput: '甲', remoteSelected: [option('old', '跨页名称')], mixinDatacomResData: [option('1', '甲'), option('2', '乙')], $emit: (...args) => events.push(args) })
 return { state, events, options }
}

test('原选择器默认本地搜索和回调不改变，remote候选不再二次过滤', () => {
 const local = selectorFixture(); assert.equal(local.state.filterMixinDatacomResData.length, 1); local.state.change(option('1', '甲')); assert.deepEqual(plain(local.events[1]), ['update:modelValue', '1'])
 const remote = selectorFixture({ remote: true }); delete remote.state.collection; assert.equal(remote.state.formatItemName(option('0', '零')), '零'); assert.equal(remote.state.filterMixinDatacomResData.length, 2); remote.state.loadMore(); assert.deepEqual(remote.events[0], ['loadMore'])
 assert.match(selector, /@scrolltolower="loadMore"/); assert.match(selector, /remote && remoteLoading/)
})

test('多选跨页/长ID/零保留，数字与字符串同ID点击取消不会制造重复', () => {
 const original = ['old', '9007199254740993', 0]; const f = selectorFixture({ remote: true, multiple: true, value: original }); f.state.initDefVal(); assert.deepEqual(plain(f.state.currentArr), original); assert.equal(f.state.current[0].text, '跨页名称')
 f.state.change(option('1', '甲')); assert.deepEqual(plain(f.state.currentArr), [...original, '1']); assert.deepEqual(original, ['old', '9007199254740993', 0]); assert.ok(f.events.some(event => event[0] === 'inputChange' && event[1] === ''))
 const numeric = selectorFixture({ remote: true, multiple: true, value: [1] }); numeric.state.initDefVal(); numeric.state.change(option('1', '甲')); assert.deepEqual(plain(numeric.state.currentArr), [])
 numeric.state.disabled = true; const count = numeric.events.length; numeric.state.change(option('2')); numeric.state.clearVal(); numeric.state.delItem(option('2')); assert.equal(numeric.events.length, count)
})

test('wrapper只在用户清空时发null，readonly/失败/卸载不改实体', () => {
 const requests = [], events = [], subscriptions = new Map(), storage = { loginStateId: '登录甲', sysParams: { yzProjectId: '1' } }
 const uni = { getStorageSync: key => storage[key], $on: (name, fn) => subscriptions.set(name, fn), $off: (name, fn) => { if (subscriptions.get(name) === fn) subscriptions.delete(name) } }
 const options = evaluate(wrapper, { uni, createRemoteRelationOptions, yzp: { request: request => { const task = { aborted: false, abort() { task.aborted = true } }; requests.push({ request, task }); return task } } })
 const state = methods(options, { ...options.data(), modelValue: '9007199254740993', endpoint: '/admin/auto/source/201/findNameIdTarget25', parentId: '', targetParentId: undefined, multiple: false, readonly: false, disabled: false, $emit: (...args) => events.push(args) })
 options.mounted.call(state); assert.equal(requests.length, 1); requests[0].request.success({ code: 500 }); requests[0].request.complete(); assert.equal(state.state.selected[0].id, state.modelValue); assert.equal(events.filter(event => event[0] === 'update:modelValue').length, 0)
 state.change(''); assert.deepEqual(plain(events.find(event => event[0] === 'update:modelValue')), ['update:modelValue', null]); state.readonly = true; const count = events.length; state.change('2'); assert.equal(events.length, count)
 options.beforeUnmount.call(state); assert.equal(subscriptions.size, 0); assert.ok(requests[0].task.aborted || state._relation === null)
})

test('页级名称缓存只按当前ID分批selected，未知双父不发查询且卸载取消', () => {
 const requests = [], callbacks = [], subscriptions = new Map(), storage = { loginStateId: '登录甲', sysParams: { yzProjectId: '1' } }
 const uni = { getStorageSync: key => storage[key], $on: (key, fn) => subscriptions.set(key, fn), $off: key => subscriptions.delete(key) }
 const yzp = { request: options => { const task = { aborted: false, abort() { task.aborted = true } }; requests.push({ options, task }); return task } }
 const createMixin = new Function('uni', 'yzp', 'createRemoteRelationOptions', mixinSource.replace(/^import .*;\s*$/gm, '').replace('export function', 'function') + '; return createRelationOptionsMixin')(uni, yzp, createRemoteRelationOptions)
 const mixin = createMixin([{ field: 'ownerId', endpoint: '/admin/auto/source/201/findNameIdTarget25', requireParent: true, parentId: '10' }, { field: 'missingId', unresolvedTarget: true, endpoint: '/admin/auto/source/201/findNameIdTarget26' }])
 const state = methods(mixin, { _alive: true, parentId: '10', records: Array.from({ length: 401 }, (_, i) => ({ ownerId: String(i + 1), missingId: '2' })), options: {}, $watch: (getter, callback) => { callbacks.push(callback); return () => {} } })
 mixin.mounted.call(state); assert.equal(requests.length, 3); assert.ok(requests.every(item => item.options.url.endsWith('/selected') && item.options.data.selectedIds.length <= 200)); assert.deepEqual(requests[0].options.data.selectedIds.slice(0, 2), ['1', '2'])
 const old = requests[0]; storage.sysParams = { yzProjectId: '2' }; subscriptions.get('onChangeProject')(); assert.ok(old.task.aborted); old.options.success({ code: 200, data: [option('1', '旧项目名称')] }); old.options.complete(); assert.equal(state.options.ownerId.length, 0)
 mixin.onUnload.call(state); assert.equal(subscriptions.size, 0); assert.ok(requests.every(item => item.task.aborted))
})


test('同ID跨目标父组时名称隔离，任一selected整体失败也不串用成功组标签', () => {
 const requests = [], storage = { loginStateId: '登录甲', sysParams: { yzProjectId: '1' } }
 const uni = { getStorageSync: key => storage[key], $on() {}, $off() {} }
 const yzp = { request: options => { requests.push(options); return { abort() {} } } }
 const createMixin = new Function('uni', 'yzp', 'createRemoteRelationOptions', mixinSource.replace(/^import .*;\s*$/gm, '').replace('export function', 'function') + '; return createRelationOptionsMixin')(uni, yzp, createRemoteRelationOptions)
 const mixin = createMixin([{ field: 'ownerId', targetParentField: 'groupId', endpoint: '/admin/auto/source/201/findNameIdTarget25', requireTargetParent: true }])
 const records = [{ ownerId: '1', groupId: '10' }, { ownerId: '1', groupId: '20' }, { ownerId: '2', groupId: '10' }]
 const state = methods(mixin, { _alive: true, records, options: {}, $watch: () => () => {} })
 mixin.mounted.call(state); assert.equal(requests.length, 2)
 const first = requests.find(request => request.data.targetParentId === '10'), second = requests.find(request => request.data.targetParentId === '20')
 first.success({ code: 200, data: [option('1', '甲组名称'), option('2', '独立名称')] }); first.complete()
 assert.deepEqual(plain(state.options.ownerId), [option('2', '独立名称')])
 second.success({ code: 500, msg: '已选记录超出目标父范围' }); second.complete()
 assert.deepEqual(plain(state.options.ownerId), [option('2', '独立名称')]); assert.deepEqual(records.map(row => row.ownerId), ['1', '1', '2'])
 // 切走失败组后重新读取，同ID只存在一个目标上下文时可以恢复合法名称。
 state.records = [records[0], records[2]]; state.refreshRelationLabels()
 assert.deepEqual(plain(state.options.ownerId), [option('1', '甲组名称'), option('2', '独立名称')])
 mixin.onUnload.call(state)
})
