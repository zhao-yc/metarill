import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRenderer, h, nextTick, version } from 'vue'

const source = await readFile(new URL('../common/yzp_runtime/selectionPageMixin.js', import.meta.url), 'utf8')
const longId = '9007199254740993'

/** 真实Vue3 Options API与自定义宿主；不手调getter，也不把实例私有_alive包装为响应式。 */
function fixture(authenticated = true) {
 const requests = [], subscriptions = new Map()
 const uni = { $on: (name, fn) => subscriptions.set(name, fn), $off: name => subscriptions.delete(name) }
 const yzp = { request(options) { if (!authenticated) return; const task = { abort() {} }; requests.push({ options, task }); return task } }
 const createMixin = new Function('uni', 'yzp', source.replace(/^import .*;\s*$/gm, '').replace('export function', 'function') + ';return createSelectionPageMixin')(uni, yzp)
 const element = (tag, text = '') => ({ tag, text, props: {}, children: [], parent: null })
 const renderer = createRenderer({
  createElement: tag => element(tag), createText: text => element('text', text), createComment: text => element('comment', text),
  setElementText(node, text) { node.text = text; node.children = [] }, setText(node, text) { node.text = text },
  patchProp(node, key, previous, value) { node.props[key] = value },
  insert(node, parent, anchor) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); const index = anchor ? parent.children.indexOf(anchor) : -1; parent.children.splice(index < 0 ? parent.children.length : index, 0, node); node.parent = parent },
  remove(node) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); node.parent = null },
  parentNode: node => node.parent, nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] || null
 })
 const host = element('root')
 const app = renderer.createApp({
  mixins: [createMixin('/admin/auto/fixture/201')],
  data() { return { readOnlyCondition: false, parentId: '', records: [] } },
  methods: { currentIdentity() { return authenticated ? '真实测试会话' : '无会话' } },
  render() { return h('button', { disabled: !this.selectionCanConfirm }, '选好了') }
 }, { selectedIds: [longId] })
 const vm = app.mount(host)
 return { vm, requests, app, disabled: () => host.children[0].props.disabled }
}

test('真实Vue3首次渲染后私有alive与已选响应能启用确认，busy/只读/错误可禁用并恢复', async () => {
 assert.match(version, /^3\./)
 const f = fixture()
 try {
  assert.equal(f.vm._alive, undefined); assert.equal(f.disabled(), true)
  f.vm._alive = true; await nextTick(); assert.equal(f.disabled(), true)
  f.vm.selectionReady = true; await nextTick(); assert.equal(f.disabled(), false)
  for (const [field, blocked, restored] of [['selectionBusy', true, false], ['readOnlyCondition', true, false], ['selectionError', '已选行失效', ''], ['selectionReady', false, true]]) {
   f.vm[field] = blocked; await nextTick(); assert.equal(f.disabled(), true, field + '必须禁用确认')
   f.vm[field] = restored; await nextTick(); assert.equal(f.disabled(), false, field + '恢复后必须重新启用确认')
  }
 } finally { f.app.unmount() }
})

test('真实Vue3选择回读完成后更新按钮，无会话空请求仍禁用并退出busy', async () => {
 const valid = fixture(), expired = fixture(false)
 try {
  valid.vm._alive = true; valid.vm.initializeSelection(); await nextTick(); assert.equal(valid.disabled(), true)
  const request = valid.requests[0].options; request.success({ code: 200, data: [{ id: longId, name: '当前名称' }] }); await nextTick(); assert.equal(valid.disabled(), true)
  request.complete(); await nextTick(); assert.equal(valid.disabled(), false)
  expired.vm._alive = true; expired.vm.initializeSelection(); await nextTick()
  assert.equal(expired.requests.length, 0); assert.equal(expired.vm.selectionBusy, false); assert.equal(expired.vm.selectionReady, false); assert.match(expired.vm.selectionError, /登录/); assert.equal(expired.disabled(), true)
 } finally { valid.app.unmount(); expired.app.unmount() }
})
