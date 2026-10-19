import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import vm from 'node:vm'

const root = process.argv[2], plain = value => JSON.parse(JSON.stringify(value))
const read = name => readFile(join(root, 'frontend/pages/yzp/' + name), 'utf8')
const parentSource = await read('customer-detail-101.vue'), listSource = await read('customer_note-list-201.vue'), detailSource = await read('customer_note-detail-201.vue')

/** 实际生成脚本使用 Vue 数据/props/计算属性及生命周期，网络和导航仅由探针记录。 */
function fixture(source, config = {}) {
 const requests = [], navigations = [], dialogs = [], emissions = [], popup = [], ticks = [], permissionCallbacks = []
 const storage = { loginStateId: '测试会话', sysParams: { yzProjectId: '100' } }, globalData = { environment: { base_url: 'https://example.test/api' }, ...config.global }
 const context = { JSON, Date, Number, Object, Array,
  yzp: { request(options) { const task = { aborted: false, abort() { task.aborted = true } }; requests.push({ options, task }); return task }, hasPagePermission: () => config.permission !== false,
   refreshCurrentPermissions(ready) { if (config.deferPermissions) permissionCallbacks.push(ready); else ready() }, chooseImportFile: callback => callback([{ 名称: '导入' }]), importSuccessAction() {} },
  utils: { openUrl() {} }, getApp: () => ({ globalData }),
  uni: { getStorageSync: key => storage[key], navigateTo: options => navigations.push(options), navigateBack: () => emissions.push({ name: 'navigateBack' }), showToast() {}, showModal: options => dialogs.push(options) } }
 const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;$/gm, '').replace(/components: \{[^}]*\},/, 'components: {},').replace('export default', 'globalThis.options =')
 vm.runInNewContext(script, context)
 const options = context.options, props = Object.fromEntries(Object.entries(options.props || {}).map(([key, value]) => [key, value.default]))
 const state = Object.assign(props, options.data(), options.methods, config.props, {
  $deviceInfo: { is_pc: config.pc !== false }, $refs: { child_detail_popup: { open: type => popup.push({ action: 'open', type }), close: () => popup.push({ action: 'close' }) } },
  $nextTick(callback) { if (config.deferTicks) ticks.push(callback); else callback() },
  $emit(name, value) { emissions.push({ name, value }); config.events?.[name]?.(value) },
  getOpenerEventChannel: () => ({ emit(name, value) { emissions.push({ name, value }); config.events?.[name]?.(value) } })
 })
 for (const [name, getter] of Object.entries(options.computed || {})) Object.defineProperty(state, name, { get: () => getter.call(state) })
 if (config.mount) options.mounted.call(state); else options.onLoad.call(state, config.route || {})
 return { state, options, requests, navigations, dialogs, emissions, popup, ticks, storage, globalData, permissionCallbacks,
  pages: () => requests.filter(request => request.options.url.endsWith('/page')),
  writes: () => requests.filter(request => /\/(add|update)\?/.test(request.options.url)),
  lastPage() { return this.pages().at(-1) }, lastWrite() { return this.writes().at(-1) } }
}
/** 使用既有 R<T> 返回一个成功写入或明细读取。 */
function success(request, data) { request.options.success?.({ code: 200, data }); request.options.complete?.() }
function finishPage(request, id = '子记录') { success(request, { records: [{ id }], pages: 3 }) }
function createParent() {
 const parent = fixture(parentSource); assert.equal(parent.state.childrenReady, false); parent.state.change('name', '新父记录'); parent.state.save()
 const write = parent.lastWrite(); assert.ok(write.options.url.includes('/add?parentId=')); success(write, { id: '9007199254740993', name: '新父记录', locked: false })
 assert.equal(parent.state.childrenReady, true); assert.equal(parent.state.saving, false); return parent
}
function childList(parent, config = {}) { return fixture(listSource, { mount: true, props: { parent_id: parent.state.id, parent_page_sn: '101', is_son_page: true, child_page_open_type: 2, parentReadOnlyCondition: parent.state.readOnlyCondition, ...config.props }, ...config }) }

{
 const parent = createParent(); assert.deepEqual(plain(parent.state.children.map(child => child.id)), ['cA', 'cZ', 'cB'])
 assert.equal(parent.pages().length, 0); const child = childList(parent); assert.equal(child.lastPage().options.data.parentId, '9007199254740993'); assert.equal(child.requests.length, 1)
 parent.state.change('name', '父表尚未保存'); parent.state.$refs.child_cB = child.state; const parentWrites = parent.writes().length
 parent.state.selectChild(2); parent.state.selectChild(99); assert.equal(parent.state.currentChild, 2)
 parent.state.refreshSonsData(); assert.equal(child.pages().length, 2); assert.equal(parent.writes().length, parentWrites); assert.deepEqual(plain(parent.state.changes), { name: '父表尚未保存' })
 console.log('通过：父保存前禁用子页、字符串父ID、整数排序、页签切换及子刷新不写父表')
}

{
 const pending = fixture(parentSource, { route: { id: '9007199254740993' } }); assert.equal(pending.state.childrenReady, false)
 success(pending.requests.at(-1), { id: '不同记录', name: '错误VO' }); assert.equal(pending.state.childrenReady, false)
 pending.state.load(); success(pending.requests.at(-1), { id: '9007199254740993', locked: false }); assert.equal(pending.state.childrenReady, true)
 const denied = fixture(parentSource, { route: { id: '9007199254740993' }, permission: false, deferPermissions: true }); assert.equal(denied.requests.length, 0); assert.equal(denied.state.readOnlyCondition, true)
 denied.permissionCallbacks[0](); success(denied.requests.at(-1), { id: '9007199254740993', name: '只读', locked: false }); assert.equal(denied.state.readOnlyCondition, true); denied.state.change('name', '不会写入'); denied.state.save(); assert.equal(denied.writes().length, 0)
 console.log('通过：读取成功才开放已有父记录，错误VO及无权限不能开放编辑')
}

{
 const child = fixture(listSource, { mount: true, props: { is_son_page: true, parent_id: '', parent_page_sn: '101', child_page_open_type: 2 } })
 assert.equal(child.requests.length, 0); child.state.openDetail(''); assert.equal(child.dialogs.length, 1); assert.equal(child.navigations.length, 0); assert.equal(child.state.childDetailPopupVisible, false)
 child.state.parent_id = '9007199254740993'; child.options.watch.parent_id.call(child.state); assert.equal(child.lastPage().options.data.parentId, '9007199254740993'); const old = child.lastPage()
 child.state.parent_id = '9007199254740995'; child.options.watch.parent_id.call(child.state); assert.equal(old.task.aborted, true); finishPage(old, '旧父数据'); assert.equal(child.state.records.length, 0)
 finishPage(child.lastPage(), '新父数据'); assert.equal(child.state.records[0].id, '新父数据'); child.options.beforeUnmount.call(child.state); child.state.loadPage(); assert.equal(child.pages().length, 2)
 console.log('通过：空父键无请求/导航，换父取消旧读并拒收乱序响应，卸载不再读取')
}

{
 const parent = createParent(), child = childList(parent); finishPage(child.lastPage()); parent.state.change('name', '父表脏字段')
 child.state.openDetail(''); assert.equal(child.state.childDetailPopupVisible, true); assert.equal(child.state.childDetailPopupId, ''); assert.equal(child.navigations.length, 0); assert.equal(child.popup.at(-1).action, 'open')
 const detail = fixture(detailSource, { mount: true, props: { is_props: true, props_id: child.state.childDetailPopupId, props_parent_id: child.state.parentId, props_parent_page_sn: child.state.parent_page_sn, parentReadOnlyCondition: child.state.readOnlyCondition }, events: { onChange: change => child.state.handleChildDetailChange(change), onPopClose: () => child.state.closeChildDetailPopup() } })
 detail.state.change('name', '新增子项'); detail.state.save(); assert.equal(detail.lastWrite().options.url, '/admin/auto/customer_note/201/add?parentId=9007199254740993'); assert.deepEqual(plain(detail.lastWrite().options.data), { name: '新增子项' })
 success(detail.lastWrite(), { id: '9007199254741001', name: '新增子项' }); assert.equal(child.pages().length, 2); assert.equal(child.lastPage().options.data.parentId, parent.state.id); assert.equal(child.state.childDetailPopupVisible, false)
 assert.equal(parent.writes().length, 1); assert.deepEqual(plain(parent.state.changes), { name: '父表脏字段' }); assert.equal(detail.emissions.some(event => event.name === 'navigateBack'), false)
 console.log('通过：PC按原props打开弹层，子保存带父键并只刷新子列表，关闭弹层不改导航栈')
}

{
 const parent = createParent(), child = childList(parent, { pc: false }); child.state.setFilter('name', '固定条件'); child.state.applyFilters(); finishPage(child.lastPage())
 child.state.openDetail('9007199254741001'); assert.equal(child.state.childDetailPopupVisible, false); const navigation = child.navigations.at(-1), url = new URL('https://example.test' + navigation.url)
 assert.equal(url.pathname, '/pages/yzp/customer_note-detail-201'); assert.equal(url.searchParams.get('parentId'), '9007199254740993'); assert.equal(url.searchParams.get('parentPageId'), '101'); assert.equal(url.searchParams.get('id'), '9007199254741001'); assert.equal(url.searchParams.get('parentReadOnly'), '0')
 navigation.events.onChange({ action: 'save', method: 'update', entity: { id: '9007199254741001' } }); assert.equal(child.lastPage().options.data.filters.name, '固定条件'); assert.equal(child.lastPage().options.data.parentId, '9007199254740993')
 const count = child.pages().length; child.state.parentId = '新父'; navigation.events.onChange('delete'); assert.equal(child.pages().length, count)
 const newPage = childList(parent, { props: { parent_id: parent.state.id, parent_page_sn: '101', is_son_page: true, child_page_open_type: 1 } }); newPage.state.openDetail(''); assert.equal(newPage.navigations.length, 1)
 const encoded = childList(parent, { pc: false }); encoded.state.parentId = '父 &?/#'; encoded.state.openDetail('子 &?/#'); const encodedUrl = new URL('https://example.test' + encoded.navigations.at(-1).url); assert.equal(encodedUrl.searchParams.get('parentId'), '父 &?/#'); assert.equal(encodedUrl.searchParams.get('id'), '子 &?/#')
 console.log('通过：移动弹层配置回退新页、PC新页配置、父子URL编码及迟到导航事件隔离')
}

{
 const parent = createParent(); parent.state.entity.locked = true; assert.equal(parent.state.readOnlyCondition, true)
 const child = childList(parent); child.state.openDetail(''); child.state.initImport(); assert.equal(child.navigations.length, 0); assert.equal(child.state.childDetailPopupVisible, false); assert.equal(child.writes().length, 0)
 child.state.openDetail('9007199254741001'); assert.equal(child.state.childDetailPopupVisible, true)
 const detail = fixture(detailSource, { mount: true, props: { is_props: true, props_id: '9007199254741001', props_parent_id: parent.state.id, props_parent_page_sn: '101', parentReadOnlyCondition: true } }); success(detail.requests.at(-1), { id: '9007199254741001', name: '只读子项', remark: '<p>原文</p>' })
 detail.state.change('name', '禁止'); detail.state.applyCustom({ name: '禁止' }); detail.state.save(); detail.state.copy(); detail.state.remove(); detail.state.openRtf('remark', 'edit'); detail.state.selectPage('name', '/不会跳转'); assert.equal(detail.writes().length, 0); assert.equal(detail.dialogs.length, 0); assert.equal(detail.navigations.length, 0); assert.deepEqual(plain(detail.state.changes), {})
 detail.state.openRtf('remark', 'review'); assert.equal(detail.navigations.length, 1)
 const route = fixture(detailSource, { route: { id: '9007199254741001', parent_id: parent.state.id, parent_page_sn: '101', parentReadOnly: '1' } }); assert.equal(route.state.parentId, parent.state.id); assert.equal(route.state.parentPageId, '101'); assert.equal(route.state.readOnlyCondition, true)
 const global = fixture(parentSource, { global: { readOnly: true } }); assert.equal(global.state.readOnlyCondition, true)
 console.log('通过：父条件/全局只读继承、原路由别名、只读允许详情与预览但所有写入口拒绝')
}

{
 const parent = createParent(), child = childList(parent, { deferTicks: true }); child.state.openDetail(''); assert.equal(child.state.childDetailPopupVisible, true)
 child.state.parent_id = '新父'; child.options.watch.parent_id.call(child.state); const opens = child.popup.filter(item => item.action === 'open').length; child.ticks[0](); assert.equal(child.popup.filter(item => item.action === 'open').length, opens)
 const detail = fixture(detailSource, { mount: true, props: { is_props: true, props_parent_id: parent.state.id, props_parent_page_sn: '101' } }); detail.state.change('name', '迟到写入'); detail.state.save(); const pending = detail.lastWrite(); detail.options.beforeUnmount.call(detail.state); success(pending, { id: '迟到子记录' }); assert.equal(pending.task.aborted, true); assert.equal(detail.state.id, ''); assert.equal(detail.emissions.length, 0)
 const late = fixture(parentSource, { deferPermissions: true }); late.options.beforeUnmount.call(late.state); late.permissionCallbacks[0](); assert.equal(late.requests.length, 0)
 console.log('通过：换父后的延迟弹层、卸载后的写响应及权限回调均失效')
}

{
 const parent = createParent(), child = childList(parent)
 function editDetail() {
  child.state.openDetail('9007199254741001')
  const detail = fixture(detailSource, { mount: true, props: { is_props: true, props_id: '9007199254741001', props_parent_id: parent.state.id, props_parent_page_sn: '101' }, events: { onChange: change => child.state.handleChildDetailChange(change), onPopClose: () => child.state.closeChildDetailPopup() } })
  assert.equal(detail.requests.at(-1).options.url, '/admin/auto/customer_note/201/detail/9007199254741001?parentId=9007199254740993')
  success(detail.requests.at(-1), { id: '9007199254741001', name: '原值', remark: null }); return detail
 }
 const update = editDetail(); update.state.change('name', '修改子项'); update.state.change('customerId', '不会接受的父键'); update.state.save()
 assert.equal(update.lastWrite().options.url, '/admin/auto/customer_note/201/update?parentId=9007199254740993'); assert.deepEqual(plain(update.lastWrite().options.data), { id: '9007199254741001', name: '修改子项' })
 success(update.lastWrite(), { id: '9007199254741001', name: '修改子项', remark: null }); assert.equal(update.emissions[0].value.method, 'update')
 const copy = editDetail(); copy.state.copy(); copy.dialogs.at(-1).success({ confirm: true }); assert.equal(copy.requests.at(-1).options.url, '/admin/auto/customer_note/201/copy/9007199254741001?parentId=9007199254740993'); assert.equal(copy.requests.at(-1).options.method, 'POST'); success(copy.requests.at(-1), { id: '9007199254741003', name: '副本' })
 const remove = editDetail(); remove.state.remove(); remove.dialogs.at(-1).success({ confirm: true }); assert.equal(remove.requests.at(-1).options.url, '/admin/auto/customer_note/201?parentId=9007199254740993'); assert.equal(remove.requests.at(-1).options.method, 'DELETE'); assert.deepEqual(plain(remove.requests.at(-1).options.data), { id: '9007199254741001' }); success(remove.requests.at(-1), '删除成功')
 assert.equal(child.pages().length, 4); assert.equal(parent.writes().length, 1)
 console.log('通过：子明细读取、部分更新、复制和删除均携带正确父范围，外键不能由表单覆盖')
}
