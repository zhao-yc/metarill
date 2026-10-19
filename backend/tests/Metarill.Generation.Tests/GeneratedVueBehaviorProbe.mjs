import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import vm from 'node:vm'

const root = process.argv[2]
const list = await readFile(join(root, 'frontend/pages/yzp/customer-list-101.vue'), 'utf8')
const detail = await readFile(join(root, 'frontend/pages/yzp/customer-detail-101.vue'), 'utf8')
const plain = value => JSON.parse(JSON.stringify(value))

/** 执行真实生成脚本；仅HTTP及导航由探针记录，不用手写页面替代生成输出。 */
function fixture(source, parameters = {}) {
 const requests = [], navigations = [], dialogs = [], toasts = [], emissions = [], storage = { loginStateId: '测试身份', sysParams: { yzProjectId: '100' } }
 const context = { JSON, Date, Number, Object, Array,
  yzp: { request: options => { const task = { aborted: false, abort() { task.aborted = true } }; requests.push({ options, task }); return task }, chooseImportFile() {}, importSuccessAction() {} },
  utils: { openUrl: url => navigations.push(url) },
  getApp: () => ({ globalData: { environment: { base_url: 'https://test.example/api' } } }),
  uni: { getStorageSync: key => storage[key], navigateTo: options => navigations.push(options), showModal: options => dialogs.push(options), showToast: options => toasts.push(options), navigateBack() {} } }
 const script = source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;$/gm, '').replace('export default', 'globalThis.options =')
 vm.runInNewContext(script, context)
 const options = context.options, state = Object.assign(options.data(), options.methods, { getOpenerEventChannel: () => ({ emit: (name, value) => emissions.push({ name, value }) }) })
 options.onLoad.call(state, { parentId: '父记录甲', ...parameters })
 return { state, options, requests, navigations, dialogs, toasts, emissions, storage,
  pages: () => requests.filter(item => item.options.url.endsWith('/page')),
  lastPage() { return this.pages().at(-1) },
  writes: () => requests.filter(item => /\/(add|update)\?/.test(item.options.url)) }
}

/** 按服务端统一响应完成一次真实生成页发出的读取请求。 */
function finish(request, rows = [], pages = 5) { request.options.success({ code: 200, data: { records: rows, pages } }); request.options.complete?.() }

{
 const f = fixture(list, { select: '1' }); f.options.onShow.call(f.state); finish(f.lastPage(), [{ id: '旧记录' }])
 f.state.setFilter('name', '已提交名称'); f.state.setFilter('enabled', false); f.state.setFilter('status', 0); f.state.setFilter('amount', '12345678901234567890.12345678'); f.state.setFilter('groupId', '9007199254740993'); f.state.setFilter('signedAt', ['2026-10-01', '2026-10-31']); f.state.setFilter('notAllowed', '不会发送')
 f.state.draftFilters.attachments = ['禁止伪装成文本筛选']; f.state.applyFilters()
 const request = f.lastPage()
 assert.deepEqual(plain(request.options.data), { pageNo: 1, pageSize: 20, filters: { name: '已提交名称', status: 0, enabled: false, amount: '12345678901234567890.12345678', signedAt: ['2026-10-01', '2026-10-31'], groupId: '9007199254740993' }, parentId: '父记录甲', orderBy: null })
 f.state.setFilter('name', '未提交草稿'); finish(request, [{ id: '第一页' }]); f.state.movePage(1)
 assert.equal(f.lastPage().options.data.pageNo, 2); assert.equal(f.lastPage().options.data.filters.name, '已提交名称'); assert.equal(f.state.selecting, true); assert.equal(f.state.parentId, '父记录甲')
 f.lastPage().options.fail({}); f.lastPage().options.complete(); assert.equal(f.state.pageNo, 1); assert.equal(f.state.records[0].id, '第一页')
 f.state.movePage(1); finish(f.lastPage(), [{ id: '第二页' }]); assert.equal(f.state.pageNo, 2)
 console.log('通过：筛选冻结、false/零/高精度字符串、日期范围、分页失败及父选择上下文')
}

{
 const f = fixture(list); f.state.setFilter('name', '有效条件'); f.state.applyFilters(); finish(f.lastPage())
 const previous = plain(f.state.appliedFilters), count = f.pages().length
 for (const [field, value] of [['amount', 'NaN'], ['signedAt', ['2026-10-31', '2026-10-01']], ['signedAt', ['2026-10-01']], ['enabled', 'false']]) {
  f.state.draftFilters = { name: '不会覆盖旧查询' }; f.state.setFilter(field, value); f.state.applyFilters(); assert.equal(f.pages().length, count); assert.deepEqual(plain(f.state.appliedFilters), previous)
 }
 assert.equal(f.dialogs.length, 4)
 console.log('通过：无效数值、不完整或反向日期、字符串布尔不发出错误请求')
}

{
 const f = fixture(list, { select: '1' }); f.state.setFilter('name', '保持筛选'); f.state.applyFilters(); finish(f.lastPage())
 f.state.changeSort('name', { order: 'descending' }); assert.deepEqual(plain(f.lastPage().options.data.orderBy), { column: 'name', order: 'descending' }); assert.equal(f.lastPage().options.data.filters.name, '保持筛选')
 let count = f.pages().length; f.state.changeSort('hidden', { order: 'descending' }); f.state.changeSort('name', { order: 'desc' }); assert.equal(f.pages().length, count)
 f.state.selectSortField('amount'); f.state.selectSortDirection('ascending'); assert.deepEqual(plain(f.lastPage().options.data.orderBy), { column: 'amount', order: 'ascending' })
 f.state.resetFilters(); assert.deepEqual(plain(f.lastPage().options.data.filters), {}); assert.equal(f.lastPage().options.data.parentId, '父记录甲'); assert.equal(f.state.selecting, true); assert.deepEqual(plain(f.lastPage().options.data.orderBy), { column: 'amount', order: 'ascending' })
 f.state.changeSort('amount', { order: null }); assert.equal(f.lastPage().options.data.orderBy, null); assert.equal(f.lastPage().options.data.pageNo, 1)
 console.log('通过：只允许生成排序字段、真实column/order协议、重置保留排序与父上下文')
}

{
 const f = fixture(list); f.options.onShow.call(f.state); const old = f.lastPage(); f.state.setFilter('name', '新请求'); f.state.applyFilters(); const latest = f.lastPage()
 assert.equal(old.task.aborted, true); finish(old, [{ id: '旧响应' }]); assert.equal(f.state.records.length, 0); assert.equal(f.state.loading, true)
 finish(latest, [{ id: '新响应' }]); assert.equal(f.state.records[0].id, '新响应'); assert.equal(f.state.loading, false)
 f.state.loadPage(); const departed = f.lastPage(); f.options.onUnload.call(f.state); finish(departed, [{ id: '离开后旧数据' }]); assert.equal(f.state.records[0].id, '新响应'); assert.equal(departed.task.aborted, true)
 const changed = fixture(list); changed.options.onShow.call(changed.state); changed.storage.loginStateId = '新身份'; finish(changed.lastPage(), [{ id: '旧身份数据' }]); assert.equal(changed.state.records.length, 0)
 console.log('通过：请求乱序、卸载和身份切换隔离')
}

{
 const f = fixture(list)
 assert.equal(f.state.displayValue({ status: 0, statusShow: '停用' }, 'status'), '停用'); assert.equal(f.state.displayValue({ status: 9, statusShow: null }, 'status'), 9)
 assert.equal(f.state.displayValue({ groupId: '9007199254740993', groupName: '真实VO名称' }, 'groupId'), '真实VO名称')
 f.state.options.groupId = [{ value: '9007199254740993', text: '合法选项名称' }]; assert.equal(f.state.displayValue({ groupId: '9007199254740993' }, 'groupId'), '合法选项名称')
 f.state.options.ownerId = [{ id: '9007199254740993', name: '另一合法名称' }]; assert.equal(f.state.displayValue({ ownerId: '9007199254740993', ownerName: '不存在的VO属性' }, 'ownerId'), '另一合法名称')
 f.state.options.ownerId = [{ id: '9007199254740993', text: '' }]; assert.equal(f.state.displayValue({ ownerId: '9007199254740993', ownerName: '仍不能使用未声明名' }, 'ownerId'), '9007199254740993')
 assert.equal(f.state.displayValue({ enabled: false }, 'enabled'), false)
 f.state.previewRtf({ remark: '<p>原HTML</p>' }, 'remark'); const navigation = f.navigations.at(-1), messages = []; assert.equal(navigation.url, '/pages/main/rtf/rtf?action=review'); navigation.success({ eventChannel: { emit: (name, data) => messages.push({ name, data }) } }); assert.deepEqual(plain(messages), [{ name: 'sendRtf', data: { data: '<p>原HTML</p>' } }])
 console.log('通过：真实常量Show/关系VO/合法选项映射与缺失名称ID兜底')
}

/** 回读真实生成详情请求，再操作原RTF事件和部分写入方法。 */
function loadedDetail(entity = {}) {
 const f = fixture(detail, { id: '9007199254740993' })
 f.requests.find(item => item.options.url.includes('/detail/')).options.success({ code: 200, data: { id: '9007199254740993', name: '原名', remark: null, status: 0, statusShow: '停用', amount: 0, enabled: false, ...entity } })
 return f
}

{
 const f = loadedDetail(); f.state.openRtf('remark', 'edit'); const navigation = f.navigations.at(-1), emitted = []
 assert.equal(navigation.url, '/pages/main/rtf/rtf?action=edit'); navigation.success({ eventChannel: { emit: (name, data) => emitted.push({ name, data }) } }); assert.deepEqual(plain(emitted), [{ name: 'sendRtf', data: { data: '' } }])
 navigation.events.onReceiveRtf(''); f.state.change('amount', 0); f.state.change('enabled', false); f.state.change('statusShow', '禁止回写'); f.state.change('groupName', '只读名称'); f.state.save()
 assert.deepEqual(plain(f.writes().at(-1).options.data), { id: '9007199254740993' }); assert.match(f.writes().at(-1).options.url, /parentId=/); f.writes().at(-1).options.complete()
 navigation.events.onReceiveRtf('<p>已修改</p>'); f.state.change('name', null); f.state.save()
 assert.deepEqual(plain(f.writes().at(-1).options.data), { id: '9007199254740993', remark: '<p>已修改</p>', name: null })
 const count = f.writes().length; f.state.save(); assert.equal(f.writes().length, count)
 console.log('通过：RTF未修改不回写、原事件载荷、显式null和白名单部分更新')
}

{
 for (const invalidate of [f => { f.state.id = '其他记录' }, f => { f.state.parentId = '其他父记录' }, f => { f.storage.loginStateId = '其他身份' }, f => { f.options.onUnload.call(f.state) }]) {
  const f = loadedDetail(); f.state.openRtf('remark', 'edit'); const navigation = f.navigations.at(-1); invalidate(f); navigation.events.onReceiveRtf('<p>迟到修改</p>'); assert.deepEqual(plain(f.state.changes), {})
 }
 const review = loadedDetail({ remark: '<p>原值</p>' }); review.state.openRtf('remark', 'review'); review.navigations.at(-1).events.onReceiveRtf('<p>不能通过预览修改</p>'); assert.deepEqual(plain(review.state.changes), {})
 const readOnly = loadedDetail(); readOnly.state.openRtf('groupName', 'edit'); assert.equal(readOnly.navigations.length, 0)
 console.log('通过：RTF记录、父上下文、身份切换与卸载后的回传保护')
}
