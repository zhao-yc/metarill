import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import vm from 'node:vm'
import test from 'node:test'

const component = await readFile(new URL('../components/zxz-uni-data-select/zxz-uni-data-select.vue', import.meta.url), 'utf8')

/** 只处理原组件的 Vue3 条件分支，运行真实选项方法和计算属性。 */
function vue3Script(source) {
 const script = source.match(/<script>([\s\S]*?)<\/script>/)[1]
 const branches = [], lines = []
 for (const line of script.split(/\r?\n/)) {
  if (line.includes('// #ifndef VUE3')) { branches.push(false); continue }
  if (line.includes('// #ifdef VUE3')) { branches.push(true); continue }
  if (line.includes('// #endif')) { branches.pop(); continue }
  if (branches.every(Boolean)) lines.push(line)
 }
 return lines.join('\n').replace('export default', 'globalThis.options =')
}

/** 模拟 uni-app 的组件环境；事件保存快照，避免后续选择改变已发出的断言值。 */
function fixture(props = {}) {
 const events = [], storage = {}, context = { uniCloud: { mixinDatacom: {} }, console: { log() {} },
  setTimeout, clearTimeout, uni: { getSystemInfoSync: () => ({ windowHeight: 800 }),
   getStorageSync: key => storage[key], setStorageSync: (key, value) => { storage[key] = value } } }
 vm.runInNewContext(vue3Script(component), context)
 const options = context.options, defaults = Object.fromEntries(Object.entries(options.props).map(([key, value]) =>
  [key, typeof value.default === 'function' ? value.default() : value.default]))
 const state = { ...defaults, ...props, ...options.data(), collection: '', $emit: (name, value) => events.push({ name, value: value === undefined ? undefined : JSON.parse(JSON.stringify(value)) }) }
 for (const [name, method] of Object.entries(options.methods)) state[name] = method.bind(state)
 for (const [name, compute] of Object.entries(options.computed)) Object.defineProperty(state, name, { get: () => compute.call(state) })
 options.watch.localdata.handler.call(state, state.localdata, undefined)
 options.watch.valueCom.handler.call(state, state.valueCom, undefined)
 options.created.call(state)
 return { state, events, options }
}

const rows = [{ id: '9000000000000000001', name: '客户甲' }, { id: '9000000000000000002', name: '客户乙' }, { id: '禁用', name: '禁用客户', disable: true }]

test('原单选支持自定义显示字段、搜索及对象回调，长字符串ID不转数字', () => {
 const { state, events } = fixture({ localdata: rows, dataKey: 'name', dataValue: 'id', filterable: true })
 state.filterInput = '乙'
 assert.deepEqual(Array.from(state.filterMixinDatacomResData, item => item.id), ['9000000000000000002'])
 state.inputChange({ detail: { value: '乙' } })
 assert.deepEqual(events.pop(), { name: 'inputChange', value: '乙' })
 state.showSelector = true; state.change(rows[1])
 assert.equal(state.current, '客户乙'); assert.equal(state.showSelector, false)
 assert.deepEqual(events, [{ name: 'input', value: rows[1].id }, { name: 'update:modelValue', value: rows[1].id }, { name: 'change', value: rows[1] }])
})

test('多选搜索后连续选择、取消和标签移除保留ID数组与完整对象回调', () => {
 const { state, events } = fixture({ localdata: rows, dataKey: 'name', dataValue: 'id', multiple: true, filterable: true })
 state.filterInput = '甲'; state.change(rows[0]); assert.equal(state.filterInput, '')
 state.filterInput = '乙'; state.change(rows[1])
 assert.deepEqual(events.at(-2), { name: 'update:modelValue', value: [rows[0].id, rows[1].id] })
 assert.deepEqual(events.at(-1), { name: 'change', value: rows.slice(0, 2) })
 state.change(rows[0]); assert.deepEqual(events.at(-2).value, [rows[1].id])
 state.delItem(rows[1]); assert.deepEqual(events.at(-2).value, []); assert.deepEqual(events.at(-1).value, [])
})

test('禁用选项不写回，整控件禁用后清空、标签移除与展开均无效', () => {
 const { state, events } = fixture({ localdata: rows, dataKey: 'name', dataValue: 'id', multiple: true, modelValue: [rows[0].id] })
 state.change(rows[2]); assert.equal(events.length, 0)
 state.disabled = true; state.clearVal(); state.delItem(rows[0]); state.toggleSelector()
 assert.equal(events.length, 0); assert.equal(state.showSelector, false)
 assert.deepEqual(Array.from(state.currentArr), [rows[0].id])
})

test('清空分别发送单选空字符串、多选空数组，并重置搜索', () => {
 for (const multiple of [false, true]) {
  const { state, events } = fixture({ localdata: rows, dataKey: 'name', dataValue: 'id', multiple, modelValue: multiple ? [rows[0].id] : rows[0].id })
  state.filterInput = '甲'; state.clearVal()
  assert.equal(state.filterInput, '')
  assert.deepEqual(events.at(-2), { name: 'update:modelValue', value: multiple ? [] : '' })
  assert.deepEqual(events.at(-1), { name: 'change', value: multiple ? [] : undefined })
 }
})

test('异步候选到达后显示既有值，父级修改选择通过原watch刷新', () => {
 const { state, options, events } = fixture({ localdata: [], dataKey: 'name', dataValue: 'id', modelValue: rows[1].id })
 assert.equal(state.current, '')
 options.watch.localdata.handler.call(state, rows, [])
 options.watch.mixinDatacomResData.handler.call(state, rows)
 assert.equal(state.current, '客户乙'); assert.equal(events.length, 0)
 state.modelValue = rows[0].id; options.watch.valueCom.handler.call(state, state.modelValue, rows[1].id)
 assert.equal(state.current, '客户甲'); assert.equal(events.length, 0)
})

test('许可来源固定为作者MIT声明并保留DCloud上游许可，不包含富文本凭据', async () => {
 const notice = await readFile(new URL('../components/zxz-uni-data-select/NOTICE.md', import.meta.url), 'utf8')
 const mit = await readFile(new URL('../components/zxz-uni-data-select/LICENSE', import.meta.url), 'utf8')
 const upstream = await readFile(new URL('../components/zxz-uni-data-select/LICENSE.upstream', import.meta.url), 'utf8')
 assert.match(notice, /https:\/\/ext\.dcloud\.net\.cn\/plugin\?name=zxz-uni-data-select/)
 assert.match(mit, /MIT License/); assert.match(upstream, /Apache License/)
 assert.doesNotMatch(component, /api[-_]?key|__UNI__|pending-control/i)
})
