import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'

const directory = process.argv[2]
const runtime = await readFile(join(directory, 'common/yzp_runtime/multiSelectFieldMixin.js'), 'utf8')
const detail = await readFile(join(directory, 'pages/yzp/textrel_source-detail-201.vue'), 'utf8')
const call = detail.match(/createMultiSelectFieldMixin\(\[[^\n]+?\]\s*,\s*\["multiitemIds"\]\)/)?.[0]
assert.ok(call, '生成页必须显式声明使用文本父键的字段')

const messages = []
const uni = { showModal: ({content}) => messages.push(content) }
const mixin = new Function('uni', `${runtime.replace('export function', 'function')}\nreturn ${call}`)(uni)
const selectedId = '9007199254740997'
let opened = 0
const view = Object.assign(mixin.data(), mixin.methods, {
 _alive: true, readOnlyCondition: false, parentId: '001', id: '100', changes: {},
 entity: { multiitemIds: [selectedId] },
 currentIdentity: () => 'demo-user',
 change(field, value) { this.entity[field] = value },
 $nextTick: callback => callback(),
 $refs: { selectPageDialog704: { open: () => { opened++ }, close() {} } }
})
view.openMultiSelection('multiitemIds')
assert.equal(view.multiTargetParent('multiitemIds'), '001')
assert.equal(opened, 1)
assert.deepEqual(messages, [])
view.parentId = '1'
view.receiveMultiSelection('multiitemIds', [{id: '42', name: '旧父候选'}])
assert.deepEqual(view.entity.multiitemIds, [selectedId])
view.parentId = '  '
view.openMultiSelection('multiitemIds')
assert.equal(opened, 1)
assert.equal(messages.length, 1)

const list = await readFile(join(directory, 'pages/yzp/textrel_source-list-201.vue'), 'utf8')
const definitions = Array.from(list.matchAll(/loadOriginalArrayCheckboxOptions\(\s*(\{[^\r\n]+?\})\s*\)/g), match => JSON.parse(match[1]))
const definition = definitions.find(candidate => candidate.field === 'checkitemIds' && candidate.textParent === true)
assert.ok(definition, '复选筛选候选必须沿已发布文本父加载')
const listRuntime = await readFile(join(directory, 'common/yzp_runtime/originalListOptionsMixin.js'), 'utf8')
const listMixin = new Function(`${listRuntime.replace(/^import .*$/m, '').replace('export function', 'function')}\nreturn createOriginalListOptionsMixin()`)()
const requests = []
const search = { parent_id: '001', originalOptionRequest: request => requests.push(request) }
listMixin.methods.loadOriginalArrayCheckboxOptions.call(search, definition)
assert.deepEqual(requests[0].data, { parentId: '001' })
search.parent_id = '1'
listMixin.methods.loadOriginalArrayCheckboxOptions.call(search, definition)
assert.deepEqual(requests[1].data, { parentId: '1' })

if (process.argv[3] === 'sparse') {
 const selection = Array.from(list.matchAll(/loadOriginalSelectionCheckboxOptions\(\s*(\{[^\r\n]+?\})\s*,/g), match => JSON.parse(match[1]))
  .find(candidate => candidate.field === 'multiitemIds' && candidate.textParent === true)
 assert.ok(selection, '原多选项筛选沿已发布文本父加载目标页面')
 listMixin.methods.loadOriginalSelectionCheckboxOptions.call(search, selection, '001')
 assert.equal(requests[2].data.parentId, '001')
 listMixin.methods.loadOriginalSelectionCheckboxOptions.call(search, selection, '1')
 assert.equal(requests[3].data.parentId, '1')
}
