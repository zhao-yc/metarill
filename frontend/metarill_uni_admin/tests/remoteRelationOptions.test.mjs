import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
const source = await readFile(new URL('../common/yzp_runtime/remoteRelationOptions.js', import.meta.url), 'utf8')
const { createRemoteRelationOptions } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
const option = (id, name = id) => ({ id, value: id, name, text: name })
/** 使用真实状态机和人工时钟/网络回执，不重写查询逻辑。 */
function fixture(context = {}) {
 const requests = [], timers = new Map(); let timerId = 0
 const scope = { endpoint: '/admin/auto/source/201/findNameIdTarget25', identity: '项目甲', authenticated: true, ...context }
 const runtime = createRemoteRelationOptions({ request: options => { const task = { aborts: 0, abort() { task.aborts++ } }; requests.push({ options, task }); return task }, getContext: () => scope, schedule: callback => { timers.set(++timerId, callback); return timerId }, cancel: id => timers.delete(id) })
 const finish = (request, data, code = 200) => { request.options.success({ code, data }); request.options.complete() }
 return { runtime, scope, requests, finish, tick() { const pending = [...timers.values()]; timers.clear(); pending.forEach(callback => callback()) } }
}

test('搜索防抖、乱序、分页失败及底部重复请求隔离', () => {
 const f = fixture(); f.runtime.open(); const first = f.requests[0]; f.runtime.search('旧'); f.runtime.search("新%_'"); f.tick(); assert.equal(f.requests.length, 2); assert.ok(first.task.aborts)
 f.finish(first, { records: [option('1', '旧')], pages: '2' }); assert.equal(f.runtime.getState().records.length, 0)
 f.finish(f.requests[1], { records: [option('2', '新')], pages: '2' }); assert.equal(f.requests[1].options.data.keyword, "新%_'")
 f.runtime.more(); f.runtime.more(); assert.equal(f.requests.length, 3); assert.equal(f.requests[2].options.data.pageNo, 2)
 f.requests[2].options.fail(); f.requests[2].options.complete(); assert.equal(f.runtime.getState().pageNo, 1); assert.equal(f.runtime.getState().records[0].id, '2')
 f.runtime.more(); f.finish(f.requests[3], { records: [option('3')], pages: '2' }); assert.deepEqual(f.runtime.getState().records.map(row => row.id), ['2', '3']); assert.equal(f.runtime.getState().hasMore, false)
})

test('已选独立回读，非首屏长ID和顺序不因搜索丢失', () => {
 const f = fixture(); const ids = ['9007199254740993', '2']; f.runtime.setValue(ids); assert.deepEqual(f.requests[0].options.data.selectedIds, ids)
 f.finish(f.requests[0], [option('2', '乙'), option('9007199254740993', '甲')]); f.runtime.search('其它'); f.tick(); f.finish(f.requests[1], { records: [option('3')], pages: 1 })
 assert.deepEqual(f.runtime.getState().selected.map(row => row.text), ['甲', '乙']); assert.deepEqual(ids, ['9007199254740993', '2'])
 f.runtime.setValue([]); assert.deepEqual(f.runtime.getState().selected, [])
})

test('已选整体拒绝/丢项/软删保留原ID并明确不可用', () => {
 const f = fixture(); f.runtime.setValue(['1', '2']); f.finish(f.requests[0], [option('1')]); assert.deepEqual(f.runtime.getState().selected.map(row => row.id), ['1', '2']); assert.match(f.runtime.getState().message, /保留原值/); assert.equal(f.runtime.getState().known.length, 0)
 f.runtime.open(); f.finish(f.requests[1], null, 500); assert.deepEqual(f.runtime.getState().selected.map(row => row.id), ['1', '2'])
})

test('零、不安全数值及超量ID不发送错误回读，不自动清空', () => {
 for (const value of [0, 9007199254740992, Array.from({ length: 201 }, (_, i) => String(i + 1))]) { const f = fixture(); f.runtime.setValue(value); assert.equal(f.requests.length, 0); assert.ok(f.runtime.getState().selected.length); assert.match(f.runtime.getState().message, /保留原值/) }
})

test('readonly只回读名称，双父未知不请求，同父兼容与明确目标父保持独立', () => {
 let f = fixture({ readonly: true }); f.runtime.setValue('1'); f.runtime.open(); assert.equal(f.requests.length, 1); assert.ok(f.requests[0].options.url.endsWith('/selected'))
 f = fixture({ requireParent: true, parentId: '10', requireTargetParent: true }); f.runtime.setValue('1'); f.runtime.open(); assert.equal(f.requests.length, 0); assert.equal(f.runtime.getState().blocked, true)
 f = fixture({ requireParent: true, parentId: '10', requireTargetParent: true, sameParent: true }); f.runtime.open(); assert.equal(f.requests[0].options.data.targetParentId, '10')
 f = fixture({ requireParent: true, parentId: '10', requireTargetParent: true, targetParentId: '20' }); f.runtime.open(); assert.equal(f.requests[0].options.data.parentId, '10'); assert.equal(f.requests[0].options.data.targetParentId, '20')
})

test('项目/父切换、卸载清空标签与计时器，迟到回调不跨上下文', () => {
 const f = fixture({ parentId: '10' }); f.runtime.setValue('1'); const old = f.requests[0]; f.scope.identity = '项目乙'; f.runtime.refresh(); assert.ok(old.task.aborts); f.finish(old, [option('1', '旧项目名称')]); assert.equal(f.runtime.getState().selected[0].text, '1')
 const current = f.requests[1]; f.scope.parentId = '20'; f.finish(current, [option('1', '旧父名称')]); assert.equal(f.runtime.getState().selected[0].text, '1')
 f.runtime.search('离开'); f.runtime.dispose(); f.tick(); assert.equal(f.requests.length, 2)
})

test('同步SDK异常和未登录上下文有明确失败状态', () => {
 const f = fixture({ authenticated: false }); f.runtime.setValue('1'); f.runtime.open(); assert.equal(f.requests.length, 0); assert.equal(f.runtime.getState().blocked, true)
 const runtime = createRemoteRelationOptions({ request() { throw new Error('SDK异常') }, getContext: () => ({ endpoint: '/admin/auto/t/1/findNameIdT2' }) }); runtime.open(); assert.equal(runtime.getState().loading, false); assert.match(runtime.getState().message, /失败/)
})
