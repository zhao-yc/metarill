import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import vm from 'node:vm'
const source = await readFile(join(process.argv[2], 'frontend/pages/yzp/relation_target-list-301.vue'), 'utf8')
const runtime = await readFile(new URL('../../../frontend/metarill_uni_admin/common/yzp_runtime/selectionPageMixin.js', import.meta.url), 'utf8')
const plain = value => JSON.parse(JSON.stringify(value))
const longId = '9007199254740993', parent = '9007199254742001'
/** 执行实际生成页面与真实选择mixin，网络只保留可控制的回执。 */
function fixture(ids = [longId], routed = false, noSession = false) {
 const requests = [], emissions = [], subscriptions = new Map(), storage = { loginStateId: noSession ? '' : '身份甲', sysParams: { yzProjectId: '1' } }, dialogs = []
 const uni = { getStorageSync: key => storage[key], $on: (name, fn) => subscriptions.set(name, fn), $off: (name, fn) => { if (subscriptions.get(name) === fn) subscriptions.delete(name) }, showModal: options => dialogs.push(options), navigateBack() {} }
 const yzp = { hasPagePermission: () => true, refreshCurrentPermissions: ready => ready(), request: options => { if (!storage.loginStateId) return; const task = { aborted: false, abort() { task.aborted = true } }; requests.push({ options, task }); return task } }
 const context = { JSON, Date, Number, Object, Array, uni, yzp, utils: {}, getApp: () => ({ globalData: {} }) }
 context.createSelectionPageMixin = new Function('uni', 'yzp', runtime.replace(/^import .*;\s*$/gm, '').replace('export function', 'function') + ';return createSelectionPageMixin')(uni, yzp)
 const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;$/gm, '').replace(/components: \{[^}]*\},/, 'components: {},').replace('export default', 'globalThis.options =')
 vm.runInNewContext(script, context)
 const options = context.options, mixins = options.mixins || []
 const props = Object.assign({}, ...[...mixins, options].map(part => Object.fromEntries(Object.entries(part.props || {}).map(([name, rule]) => [name, typeof rule.default === 'function' ? rule.default() : rule.default]))))
 const state = Object.assign(props, ...mixins.map(part => part.data?.() || {}), options.data(), ...mixins.map(part => part.methods), options.methods,
  { selectedIds: ids, selectedNames: ['旧缓存名称不能采信'], parent_id: parent, $refs: {}, $emit: (name, value) => emissions.push({ name, value }), getOpenerEventChannel: () => ({ emit: (name, value) => emissions.push({ name, value }) }) })
 for (const [name, method] of Object.entries(Object.assign({}, ...mixins.map(part => part.computed || {}), options.computed || {}))) Object.defineProperty(state, name, { get: () => method.call(state) })
 if (routed) options.onLoad.call(state, { parentId: parent }); else options.mounted.call(state)
 return { state, options, mixin: mixins[0], requests, emissions, subscriptions, storage, dialogs,
  selected: () => requests.filter(request => request.options.url.endsWith('/selection/selected')),
  pages: () => requests.filter(request => request.options.url.endsWith('/page')) }
}
/** 保留真实统一响应，失败不能回调成功数据。 */
function finish(request, data, code = 200) { request.options.success({ code, data, msg: code === 200 ? null : '已选记录已删除或不在范围' }); request.options.complete?.() }
{
 const ids = [longId], f = fixture(ids); assert.equal(f.state.multiSelecting, true); assert.equal(f.selected().length, 1)
 finish(f.selected()[0], [{ id: longId, name: '服务端当前名称' }]); assert.equal(f.state.selectedRows[0].name, '服务端当前名称'); assert.deepEqual(ids, [longId])
 finish(f.pages().at(-1), { records: [{ id: '2', name: '第二项' }], pages: 2 }); f.state.toggleSelection(f.state.records[0]); assert.deepEqual(plain(f.state.selectedDraftIds), [longId, '2']); assert.deepEqual(ids, [longId])
 f.state.movePage(1); finish(f.pages().at(-1), { records: [{ id: '3', name: '第三项' }], pages: 2 }); f.state.selectAll(); assert.deepEqual(plain(f.state.selectedDraftIds), [longId, '2', '3'])
 f.state.toggleSelectedOnly(); assert.deepEqual(plain(f.pages().at(-1).options.data.selectedIds), [longId, '2', '3']); assert.equal(f.pages().at(-1).options.data.parentId, parent)
 f.state.sureSelect(); assert.equal(f.emissions.length, 0); const pending = f.selected().at(-1)
 finish(pending, [{ id: '3', name: '三最新' }, { id: '2', name: '二最新' }, { id: longId, name: '长ID最新' }]); const result = f.emissions.find(item => item.name === 'onPopSelect')
 assert.deepEqual(plain(result.value.map(row => row.id)), [longId, '2', '3']); assert.equal(result.value[0].name, '长ID最新'); assert.ok(f.emissions.some(item => item.name === 'onPopClose'))
 assert.equal(f.requests.filter(request => /\/(add|update|delete)/.test(request.options.url)).length, 0)
 console.log('通过：实际type=2生成页跨页多选/全选本页/仅看已选/新鲜完整row数组确认，父数组未被草稿修改')
}
{
 const f = fixture(); finish(f.selected()[0], [{ id: longId, name: '初始名称' }]); f.state.sureSelect(); const pending = f.selected().at(-1); f.state.sureClose()
 finish(pending, [{ id: longId, name: '迟到不能提交' }]); assert.ok(pending.task.aborted); assert.equal(f.emissions.filter(item => item.name === 'onPopSelect').length, 0)
 const g = fixture(); finish(g.selected()[0], [{ id: longId, name: '初始名称' }]); g.state.sureSelect(); finish(g.selected().at(-1), null, 500)
 assert.equal(g.state.selectedRows.length, 0); assert.equal(g.state.selectionCanConfirm, false); assert.deepEqual(plain(g.state.selectedDraftIds), [longId]); assert.equal(g.emissions.length, 0)
 console.log('通过：取消隔离迟到确认；已删/越权整体失败保留ID且清旧名称，不回传缓存行')
}
{
 const f = fixture([]); finish(f.selected()[0], []); const count = f.pages().length; f.state.toggleSelectedOnly(); assert.equal(f.pages().length, count); assert.equal(f.dialogs.length, 1)
 f.state.showSelectedOnly = true; f.state.loadPage(1); assert.equal(f.pages().length, count); assert.deepEqual(plain(f.state.records), [])
 const g = fixture(); const old = g.selected()[0]; g.storage.sysParams = { yzProjectId: '2' }; g.subscriptions.get('onChangeProject')(); assert.ok(old.task.aborted); finish(old, [{ id: longId, name: '旧项目' }]); assert.equal(g.state.selectedRows.length, 0)
 g.options.beforeUnmount.call(g.state); const active = g.selected().at(-1); finish(active, [{ id: longId, name: '卸载后' }]); assert.equal(g.emissions.length, 0); assert.equal(g.subscriptions.size, 0)
 console.log('通过：空的仅看已选不查全部；项目/卸载隔离请求与旧名称')
}
{
 const f = fixture('[' + JSON.stringify(longId) + ']'); finish(f.selected()[0], [{ id: longId }]); assert.deepEqual(plain(f.state.selectedDraftIds), [longId])
 const g = fixture([0]); assert.equal(g.selected().length, 0); assert.ok(g.state.selectionError); assert.equal(g.emissions.length, 0)
 const h = fixture(Array(201).fill('1')); assert.equal(h.selected().length, 0); assert.ok(h.state.selectionError)
 console.log('通过：原JSON数组文本与长ID保持，非法零值/超量不发送错误读取或改写父数据')
}

{
 const f = fixture([longId], false, true); assert.equal(f.selected().length, 0); assert.equal(f.state.selectionBusy, false); assert.equal(f.state.selectionCanConfirm, false); assert.match(f.state.selectionError, /登录/); assert.equal(f.emissions.length, 0)
 const g = fixture(); finish(g.selected()[0], [{ id: longId }]); finish(g.pages().at(-1), { records: [{ id: '2' }], pages: 1 }); g.state.parentReadOnlyCondition = true
 const count = g.selected().length; g.state.toggleSelection(g.state.records[0]); g.state.selectAll(); g.state.sureSelect(); assert.equal(g.selected().length, count); assert.deepEqual(plain(g.state.selectedDraftIds), [longId]); assert.equal(g.emissions.length, 0)
 console.log('通过：无登录会话未启动请求时退出loading并报中文错误，只读不能勾选或提交')
}

{
 const f = fixture([longId], true); finish(f.selected()[0], [{ id: longId }]); f.state.sureSelect(); finish(f.selected().at(-1), [{ id: longId, name: '路由当前名称' }]); assert.deepEqual(plain(f.emissions.find(item => item.name === 'selected').value), [{ id: longId, name: '路由当前名称' }])
 const g = fixture(); const old = g.selected()[0]; g.state.parent_id = '9007199254742002'; g.options.watch.parent_id.call(g.state); assert.ok(old.task.aborted); finish(old, [{ id: longId, name: '旧父名称' }]); assert.equal(g.state.selectedRows.length, 0); assert.equal(g.selected().at(-1).options.data.parentId, '9007199254742002'); assert.equal(g.emissions.length, 0)
 console.log('通过：路由确认回传row数组；目标父切换取消旧回读，不借旧父名称')
}
