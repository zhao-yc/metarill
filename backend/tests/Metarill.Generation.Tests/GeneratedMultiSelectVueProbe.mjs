import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { createRequire } from 'node:module'
const require = createRequire(new URL('../../../frontend/metarill_uni_admin/package.json', import.meta.url))
const { createRenderer, h, nextTick, compile } = require('vue')
const source = await readFile(join(process.argv[2], 'frontend/pages/yzp/relation_source-detail-201.vue'), 'utf8')
const runtime = await readFile(new URL('../../../frontend/metarill_uni_admin/common/yzp_runtime/multiSelectFieldMixin.js', import.meta.url), 'utf8')
const longId = '9007199254740993', parentId = '9007199254742001'
const plain = value => JSON.parse(JSON.stringify(value))

/** 真实Vue3编译当前生成模板，目标列表仅作为原事件协议宿主，Service范围由MySQL测试验证。 */
function fixture() {
 const requests = [], dialogs = [], subscriptions = new Map(), children = [], storage = { loginStateId: '会话甲', sysParams: { yzProjectId: '1' } }
 const uni = { getStorageSync: key => storage[key], $on: (key, fn) => subscriptions.set(key, fn), $off: (key, fn) => { if (subscriptions.get(key) === fn) subscriptions.delete(key) }, showModal: options => dialogs.push(options) }
 const yzp = { hasPagePermission: () => true, refreshCurrentPermissions: ready => ready(), request: options => { requests.push(options); return { abort() {} } } }
 const createMultiSelectFieldMixin = new Function('uni', runtime.replace('export function', 'function') + ';return createMultiSelectFieldMixin')(uni)
 const Target = { props: ['selectedIds', 'selectedNames', 'selectedNameField', 'parent_id', 'parentReadOnlyCondition'], emits: ['onPopSelect', 'onPopClose'], mounted() { children.push(this) }, methods: { confirm(rows) { this.$emit('onPopSelect', rows); this.$emit('onPopClose') }, cancel() { this.$emit('onPopClose') } }, render() { return h('section') } }
 const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;$/gm, '').replace('export default', 'return')
 const options = new Function('yzp', 'uni', 'getApp', 'createMultiSelectFieldMixin', 'MultiSelection25', script)(yzp, uni, () => ({ globalData: { readOnly: false } }), createMultiSelectFieldMixin, Target)
 options.render = compile(source.match(/<template>([\s\S]*?)<\/template>/)[1])
 const element = (tag, text = '') => ({ tag, text, props: {}, parent: null, children: [] })
 const renderer = createRenderer({ createElement: tag => element(tag), createText: text => element('text', text), createComment: text => element('comment', text), setElementText(node, text) { node.text = text; node.children = [] }, setText(node, text) { node.text = text }, patchProp(node, key, old, value) { node.props[key] = value }, insert(node, parent, anchor) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); const at = anchor ? parent.children.indexOf(anchor) : -1; parent.children.splice(at < 0 ? parent.children.length : at, 0, node); node.parent = parent }, remove(node) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); node.parent = null }, parentNode: node => node.parent, nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] || null })
 const app = renderer.createApp(options)
 const Stub = { render() { return h('view', {}, this.$slots.default?.()) } }
 for (const name of ['uni-forms', 'uni-forms-item', 'uni-easyinput']) app.component(name, Stub)
 app.component('yzp-popup', { emits: ['change'], methods: { open() { this.$emit('change', { show: true }) }, close() { this.$emit('change', { show: false }) } }, render() { return h('view', {}, this.$slots.default?.()) } })
 app.config.globalProperties.$deviceInfo = { is_pc: true }
 const state = app.mount(element('root')); options.onLoad.call(state, { parentId }); state.id = '9007199254748888'; state.entity = { id: state.id, targetIds: [longId], targetNames: ['历史名称'] }; state.recordReady = true; state.changes = {}
 return { state, app, requests, dialogs, storage, subscriptions, target: () => children.at(-1), async open() { state.openMultiSelection('targetIds'); await nextTick(); await nextTick(); return children.at(-1) } }
}
{
 const f = fixture()
 try {
  assert.equal(f.state.displayValue({ targetIds: [longId], targetNames: ['甲', null, '乙'] }, 'targetIds'), '甲，，乙')
  assert.equal(f.state.displayValue({ targetIds: [longId], targetNames: null }, 'targetIds'), longId)
  assert.equal(f.state.displayValue({ targetIds: [longId], targetNames: '["历史甲","历史乙"]' }, 'targetIds'), '历史甲，历史乙')
  const target = await f.open(); assert.deepEqual(plain(target.selectedIds), [longId]); assert.deepEqual(plain(target.selectedNames), ['历史名称']); assert.equal(target.selectedNameField, 'name'); assert.equal(target.parent_id, parentId)
  target.cancel(); await nextTick(); assert.deepEqual(plain(f.state.entity.targetIds), [longId]); assert.deepEqual(plain(f.state.entity.targetNames), ['历史名称']); assert.deepEqual(plain(f.state.changes), {})
  const confirmed = await f.open(); confirmed.confirm([{ id: longId, name: '服务端当前名称', ignored: '不回填任意字段' }, { id: '2', name: null }]); await nextTick()
  assert.deepEqual(plain(f.state.entity.targetIds), [longId, '2']); assert.deepEqual(plain(f.state.entity.targetNames), ['服务端当前名称', null]); assert.deepEqual(Object.keys(f.state.changes), ['targetIds']); assert.equal(f.state.entity.ignored, undefined)
  f.state.changes = {}; const unchanged = await f.open(); unchanged.confirm([{ id: longId, name: '名称再更新' }, { id: '2', name: null }]); await nextTick(); assert.deepEqual(plain(f.state.changes.targetIds), [longId, '2']); assert.equal(f.state.changes.targetNames, undefined)
  const emptied = await f.open(); emptied.confirm([]); await nextTick(); assert.deepEqual(plain(f.state.entity.targetIds), []); assert.deepEqual(plain(f.state.entity.targetNames), [])
  assert.equal(f.requests.length, 0)
 } finally { f.app.unmount() }
 console.log('通过：真实生成详情模板的原props/row[]/关闭事件、取消不写、长ID与名称顺序、名称不入changes、同IDfresh与空选清空')
}
{
 const f = fixture()
 try {
  const target = await f.open(); f.state.routeReadOnly = true; await nextTick(); assert.equal(target.parentReadOnlyCondition, true); target.confirm([{ id: '2', name: '恶意只读回执' }]); await nextTick(); assert.deepEqual(plain(f.state.entity.targetIds), [longId]); assert.deepEqual(plain(f.state.changes), {})
  f.state.routeReadOnly = false; const late = await f.open(); f.storage.sysParams = { yzProjectId: '2' }; late.confirm([{ id: '2', name: '旧项目迟到' }]); await nextTick(); assert.deepEqual(plain(f.state.entity.targetIds), [longId]); assert.deepEqual(plain(f.state.changes), {})
  f.storage.sysParams = { yzProjectId: '1' }; const duplicate = await f.open(); duplicate.confirm([{ id: '2', name: '重复甲' }, { id: '2', name: '重复乙' }]); await nextTick(); assert.deepEqual(plain(f.state.changes), {}); assert.match(f.state.multiSelectionError, /不重复/)
  const old = await f.open(); f.app.unmount(); old.confirm([{ id: '2', name: '卸载迟到' }]); assert.deepEqual(plain(f.state.entity.targetIds), [longId]); assert.equal(f.subscriptions.size, 0)
 } finally { if (f.state._alive) f.app.unmount() }
 console.log('通过：真实Vue只读传递、项目切换/卸载迟到回执、重复或不完整选择均不写源实体')
}
