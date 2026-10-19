import assert from 'node:assert/strict'
import test from 'node:test'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'
import { createRenderer, h, nextTick } from 'vue'

const runtimeRoot = new URL('../common/yzp_runtime/', import.meta.url)
const sources = Object.fromEntries(await Promise.all(['listPageRuntime.js', 'originalListOptionsMixin.js', 'selectionPageMixin.js', 'originalSelectionPageMixin.js', 'yzp-runtime.js'].map(async name => [name, await readFile(new URL(name, runtimeRoot), 'utf8')])))
const actualModule = async name => import('data:text/javascript;base64,' + Buffer.from(await readFile(new URL(name, runtimeRoot), 'utf8')).toString('base64'))
const relation = await actualModule('relationReadValues.js')
const { createPageWriteFilter } = await actualModule('pageWritePayload.js')
const writeSchema = JSON.parse(await readFile(new URL('page-write-schema.generated.json', runtimeRoot), 'utf8'))
const plain = value => JSON.parse(JSON.stringify(value))

/** 真实Vue生命周期组合原列表/选择桥接与公共request，只有网络SDK与宿主渲染受控。 */
function fixture({ options = false, selection = false, reverse = false } = {}) {
 const requests = [], dialogs = [], subscriptions = new Map(), timers = new Map()
 const storage = { loginStateId: '会话甲', sysParams: { yzProjectId: '项目甲' } }
 let timerId = 0, pulls = 0, initializations = 0
 const uni = {
  getStorageSync: key => storage[key],
  $on(event, fn) { if (!subscriptions.has(event)) subscriptions.set(event, new Set()); subscriptions.get(event).add(fn) },
  $off(event, fn) { subscriptions.get(event)?.delete(fn) },
  // 实际事件总线可以复制监听快照；正反顺序都不能靠注销尚未执行的listener保证安全。
  $emit(event, ...args) { const snapshot = [...subscriptions.get(event) || []]; for (const fn of reverse ? snapshot.reverse() : snapshot) fn(...args) },
  stopPullDownRefresh() { pulls++ }, showModal: value => dialogs.push(value), showLoading() {}, hideLoading() {},
  request(options) { const entry = { options, aborts: 0 }; requests.push(entry); return { abort() { entry.aborts++; options.fail?.({ errMsg: '同步取消' }); options.complete?.({ errMsg: '同步取消' }) } } }
 }
 const context = vm.createContext({ ...relation, filterPageWriteData: createPageWriteFilter(writeSchema), uni,
  utils: { isBlank: value => value == null || value === '' }, getApp: () => ({ globalData: { environment: { base_url: 'http://test/api' } } }),
  setTimeout(fn) { const id = ++timerId; timers.set(id, fn); return id }, clearTimeout: id => timers.delete(id), console: { error() {} }
 })
 vm.runInContext(sources['yzp-runtime.js'].slice(sources['yzp-runtime.js'].indexOf('const TAB_BAR_PAGES')).replace('export default yzp;', 'globalThis.runtime=yzp;'), context)
 const runtime = context.runtime
 const factory = (name, symbol, globals) => new Function(...Object.keys(globals), sources[name].replace(/^import .*;?\s*$/gm, '').replace(/export function /g, 'function ') + ';return ' + symbol)(...Object.values(globals))
 const createListPageMixin = factory('listPageRuntime.js', 'createListPageMixin', { uni })
 const createOriginalListOptionsMixin = factory('originalListOptionsMixin.js', 'createOriginalListOptionsMixin', { uni, yzp: runtime })
 const createSelectionPageMixin = factory('selectionPageMixin.js', 'createSelectionPageMixin', { uni, yzp: runtime })
 const createOriginalSelectionPageMixin = factory('originalSelectionPageMixin.js', 'createOriginalSelectionPageMixin', { uni, createSelectionPageMixin })
 const mixins = [createOriginalListOptionsMixin({ targetParentFields: ['parentId'], optionFields: options ? ['target_list'] : [] }), createListPageMixin({ url: '/admin/auto/source/101/page', pageSize: 5, canLoad() { return !!uni.getStorageSync('loginStateId') }, request: value => runtime.request(value) })]
 if (selection) mixins.push(createOriginalSelectionPageMixin('/admin/auto/source/101'))
 const element = () => ({ parent: null, children: [] })
 const renderer = createRenderer({ createElement: element, createText: element, createComment: element, setElementText() {}, setText() {}, patchProp() {}, insert(node, parent) { node.parent = parent; parent.children.push(node) }, remove(node) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1) }, parentNode: node => node.parent, nextSibling: () => null })
 const app = renderer.createApp({ mixins, data() { return { filters: { parentId: '甲父' }, orderBy: {}, parent_id: '父甲', is_son_page: false, target_list: [], untouched_list: ['手写数组'], constantRange: ['固定项'], selectedList: [], isSelectAll: false, childDetailPopupVisible: false, childDetailPopupId: '', readOnlyCondition: false, $deviceInfo: { is_pc: true } } },
  mounted() { this.refreshData(); this.initLists() },
  methods: {
   initLists() { initializations++; if (options) { this.target_list = []; this.loadCandidate() } },
   loadCandidate() { return this.originalOptionRequest({ url: '/admin/auto/source/101/findNameIdTarget1', sendSessionId: true, method: 'POST', success: response => { this.target_list = response.data } }) },
   closeChildDetailPopup() { this.childDetailPopupVisible = false; this.childDetailPopupId = '' }
  }, render: () => h('view')
 }, selection ? { selectedIds: ['9007199254740993'] } : {})
 const state = app.mount(element())
 const respond = (entry, data) => { entry.options.success({ data: { code: 200, data } }); entry.options.complete?.({}) }
 const newest = suffix => requests.filter(entry => entry.options.url.endsWith(suffix)).at(-1)
 return { app, state, storage, requests, dialogs, runtime, uni, subscriptions, respond, newest, initializations: () => initializations, pulls: () => pulls }
}

for (const scenario of [{ name: '普通无候选', options: false }, { name: '普通固定候选', options: true }, { name: '原多选正序监听', options: true, selection: true }, { name: '原多选逆序监听快照', options: true, selection: true, reverse: true }]) {
 test(scenario.name + '：退出同步清旧行和草稿且零新请求/零relogin，再登录和切项目真实恢复', async () => {
  const f = fixture(scenario)
  try {
   f.respond(f.newest('/page'), { records: [{ id: '9007199254740993', name: '旧行' }], pages: 3, total: 13 })
   if (scenario.options) f.respond(f.newest('/findNameIdTarget1'), [{ id: '9007199254740994', name: '旧候选' }])
   if (scenario.selection) f.respond(f.newest('/selection/selected'), [{ id: '9007199254740993', name: '旧已选' }])
   await nextTick()
   f.state.selectedList = [{ id: '9007199254740993' }]; f.state.isSelectAll = true
   f.state.childDetailPopupVisible = true; f.state.childDetailPopupId = '旧详情'
   f.state.statistics = { 金额: 18.25 }; f.state.list_error = '旧错误'
   f.state.refreshData()
   if (scenario.options) f.state.loadCandidate()
   if (scenario.selection) f.state.readSelection(false)
   const active = f.requests.filter(entry => entry.aborts === 0)
   const count = f.requests.length, initializationCount = f.initializations()
   f.storage.loginStateId = ''; f.storage.sysParams = {}; f.uni.$emit('onLogout')
   assert.equal(f.requests.length, count, '退出事件不能新增任何SDK请求')
   assert.equal(f.initializations(), initializationCount, '退出不能调用会请求的initLists')
   assert.deepEqual(f.dialogs, [], '公共relogin不得被退出后的后台请求触发')
   assert.deepEqual(plain(f.state.list), []); assert.deepEqual(plain(f.state.statistics), {})
   assert.equal(f.state.page_no, 1); assert.equal(f.state.pages_qty, 0); assert.equal(f.state.rows_qty, 0)
   assert.equal(f.state.list_error, ''); assert.equal(f.state.load_more_status, 'no-more')
   assert.deepEqual(plain(f.state.selectedList), []); assert.equal(f.state.isSelectAll, false)
   assert.equal(f.state.childDetailPopupVisible, false); assert.equal(f.state.childDetailPopupId, '')
   if (scenario.options) assert.deepEqual(plain(f.state.target_list), [])
   assert.deepEqual(plain(f.state.untouched_list), ['手写数组']); assert.deepEqual(plain(f.state.constantRange), ['固定项'])
   assert.equal(f.runtime.getRequestLoadingState().pending.length, 0)
   if (scenario.selection) { assert.deepEqual(plain(f.state.selectedDraftIds), []); assert.deepEqual(plain(f.state.selectedRows), []); assert.equal(f.state.selectionReady, false); assert.equal(f.state.selectionBusy, false); assert.equal(f.state._alive, true) }
   for (const entry of active) { entry.options.success({ data: { code: 500, msg: '登录超时' } }); entry.options.fail?.({ errMsg: '迟到失败' }); entry.options.complete?.({}) }
   assert.deepEqual(plain(f.state.list), []); assert.deepEqual(f.dialogs, [])
   // 缺会话的父watch、显式登录事件和项目事件也只能清理，不能间接触发公共relogin。
   f.state.parent_id = '父乙'; f.state.filters.parentId = '乙父'; await nextTick()
   f.uni.$emit('onLogin'); f.uni.$emit('onChangeProject'); await nextTick()
   assert.equal(f.requests.length, count); assert.deepEqual(f.dialogs, [])
   if (scenario.selection) f.state._originalConfirmPending = true
   f.storage.loginStateId = '会话乙'; f.storage.sysParams = { yzProjectId: '项目乙' }; f.uni.$emit('onLogin')
   if (scenario.selection) assert.equal(f.state._originalConfirmPending, false, 'onLogin必须废弃旧身份的确认动作')
   assert(f.requests.length > count, '真实onLogin生命周期必须恢复请求，不能测试中手工initialize')
   f.respond(f.newest('/page'), { records: [{ id: '9007199254740995', name: '新行' }], pages: 1, total: 1 })
   if (scenario.options) f.respond(f.newest('/findNameIdTarget1'), [{ id: '9007199254740996', name: '新候选' }])
   if (scenario.selection) f.respond(f.newest('/selection/selected'), [{ id: '9007199254740993', name: '当前同权名称' }])
   await nextTick()
   assert.equal(f.state.list[0].name, '新行')
   if (scenario.options) assert.equal(f.state.target_list[0].name, '新候选')
   if (scenario.selection) { assert.equal(f.state.selectionReady, true); assert.equal(f.state.selectionBusy, false); assert.equal(f.state.selectedRows[0].name, '当前同权名称') }
   for (const entry of active) entry.options.success({ data: { code: 200, data: [{ id: '旧身份' }] } })
   assert.equal(f.state.list[0].name, '新行'); assert.deepEqual(f.dialogs, [])
   const prior = f.requests.length
   if (scenario.selection) f.state._originalConfirmPending = true
   f.storage.sysParams = { yzProjectId: '项目丙' }; f.uni.$emit('onChangeProject')
   assert(f.requests.length > prior, '有会话的项目切换仍按原合同重新加载')
   if (scenario.selection) {
    assert.equal(f.state._originalConfirmPending, false, 'onChangeProject必须废弃旧身份的确认动作')
    f.respond(f.newest('/selection/selected'), [{ id: '9007199254740993', name: '项目丙同权名称' }]); await nextTick()
    assert.equal(f.state.selectionReady, true); assert.equal(f.state.selectionBusy, false)
    assert.equal(f.state.selectedRows[0].name, '项目丙同权名称'); assert.deepEqual(f.dialogs, [])
   }
  } finally { f.app.unmount(); for (const listeners of f.subscriptions.values()) assert.equal(listeners.size, 0) }
 })
}

test('公共未登录请求继续弹原relogin，修复只阻止退出后的自动重载', () => {
 const f = fixture()
 try {
  f.storage.loginStateId = ''; f.uni.$emit('onLogout')
  assert.equal(f.dialogs.length, 0)
  const count = f.requests.length
  f.runtime.request({ url: '/显式受保护请求', sendSessionId: true })
  assert.equal(f.requests.length, count)
  assert.equal(f.dialogs.length, 1); assert.equal(f.dialogs[0].content, '尚未登录或已过期，请重新登录')
 } finally { f.app.unmount() }
})