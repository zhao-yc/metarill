import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const source = await readFile(new URL('../common/yzp_runtime/detailLayoutRuntime.js', import.meta.url), 'utf8')
const { createDetailLayoutMixin } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
const groups = [{ key: 'base', name: '基础资料' }, { key: 'extra', name: '补充资料' }, { key: 'empty', name: '空组' }]
const fields = [{ name: 'title', groupKey: '' }, { name: 'code', groupKey: 'base' },
  { name: 'note', groupKey: 'extra', visibleCondition: 'showExtra' }]

/** 建立独立页面上下文，执行实际 mixin 计算和方法。 */
function fixture() {
  const mixin = createDetailLayoutMixin({ version: 1, mode: 'tabs', groups }, fields)
  const locations = []
  const vm = { ...mixin.data(), ...mixin.methods, entity: { showExtra: true }, loadEntitySuccess: true,
    currentSonItem: 7, sonItems: ['子表'], $yzp: { isColumnVisible: (field, entity) => !field.visibleCondition || entity[field.visibleCondition] },
    $nextTick: fn => fn() }
  Object.defineProperty(vm, 'visibleDetailGroups', { get: () => mixin.computed.visibleDetailGroups.call(vm) })
  vm.scrollToDetailField = name => locations.push(name)
  const refresh = () => mixin.watch.visibleDetailGroups.handler.call(vm, vm.visibleDetailGroups)
  refresh()
  return { vm, refresh, locations, mixin }
}



test('跨组整表校验定位首个错误，顶部字段不切换分组且保存输入', () => {
  const { vm, locations } = fixture()
  vm.entity.note = '未保存输入'
  vm.revealDetailValidationError([{ key: 'note' }, { key: 'code' }])
  assert.equal(vm.activeDetailGroupKey, 'extra')
  assert.deepEqual(locations, ['note'])
  vm.revealDetailValidationError([{ key: 'title' }])
  assert.equal(vm.activeDetailGroupKey, 'extra')
  assert.deepEqual(locations, ['note', 'title'])
  assert.equal(vm.entity.note, '未保存输入')
  vm.revealDetailValidationError([])
  vm.revealDetailValidationError({ key: '不存在字段' })
  assert.equal(locations.length, 2)
})

test('关闭分组不产生有效标签，所有组动态变空时清空选择', () => {
  const disabled = createDetailLayoutMixin({ mode: 'none', groups }, fields)
  assert.deepEqual(disabled.computed.visibleDetailGroups.call({}), [])
  const { vm, refresh } = fixture()
  vm.$yzp.isColumnVisible = () => false
  refresh()
  assert.equal(vm.activeDetailGroupKey, '')
  assert.deepEqual(vm.visibleDetailGroups, [])
})

test('错误定位滚动实际表单容器并聚焦输入，兼容嵌入弹窗', () => {
  const { vm, mixin } = fixture()
  const calls = []
  vm.$el = { querySelector(selector) {
    calls.push(selector)
    return { scrollIntoView: options => calls.push(options), querySelector: () => ({ focus: options => calls.push(options) }) }
  } }
  mixin.methods.scrollToDetailField.call(vm, 'note')
  assert.equal(calls[0], '#yzp-detail-field-note')
  assert.equal(calls[1].block, 'center')
  assert.equal(calls[2].preventScroll, true)
})

test('非 H5 通过当前组件范围查询定位，不跳到同名子表字段', () => {
  const { vm, mixin } = fixture()
  const calls = []
  const previousUni = globalThis.uni
  const query = { in(scope) { assert.equal(scope, vm); return this }, select(selector) { calls.push(selector); return this },
    boundingClientRect() { return this }, selectViewport() { return this }, scrollOffset() { return this },
    exec(callback) { callback([{ top: 200 }, { scrollTop: 300 }]) } }
  globalThis.uni = { createSelectorQuery: () => query, pageScrollTo: options => calls.push(options) }
  try {
    mixin.methods.scrollToDetailField.call(vm, 'code')
    assert.equal(calls[0], '#yzp-detail-field-code')
    assert.equal(calls[1].scrollTop, 420)
  } finally {
    globalThis.uni = previousUni
  }
})
