import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const require = createRequire(new URL('../../../frontend/metarill_uni_admin/package.json', import.meta.url))
const Vue = require('vue')
const { parse } = require('@vue/compiler-sfc')
const frontend = join(process.argv[2], 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/summary_form-detail-102.vue'), 'utf8')
const componentSource = await readFile(join(frontend, 'components/yzp-cascade-tree/yzp-cascade-tree.vue'), 'utf8')
const { descriptor, errors } = parse(componentSource)
assert.deepEqual(errors, [])
const calls = [], trees = []
const zypRequest = { request(options) { calls.push(options); return { abort() {} } } }
const cascade = new Function('yzp', descriptor.script.content.replace(/^import[^\n]*\n/gm, '').replace('export default', 'return'))(zypRequest)
cascade.render = Vue.compile(descriptor.template.content)
cascade.mounted = function () { trees.push(this) }
const Stub = { render() { return Vue.h('host', {}, this.$slots.default?.()) } }
const form = mountGenerated(source, {}, {}, {
 runtimeDirectory: join(frontend, 'common/yzp_runtime'),
 components: { 'yzp-cascade-tree': cascade, 'uni-icons': Stub, 'uni-load-more': Stub, 'scroll-view': Stub }
})
const tree = '9007199254740993', country = '9007199254740994', area = '9007199254740995', place = '9007199254740996'
const country2 = '9007199254740997', area2 = '9007199254740998'
const answered = new Set()

/** 只应答固定原接口。路径、全字段交接和共享变更运行原组件。 */
async function pump() {
 for (let i = 0; i < 10; i++) {
  for (const call of calls.filter(request => !answered.has(request))) {
   answered.add(call)
   assert.equal(call.method, 'POST')
   if (call.url.endsWith('/cascadeTreePath27')) {
    assert.equal(call.data.leafId, tree)
    call.success({ code: 200, data: { path: [{ id: tree, name: '原树叶' }], displayText: '原树叶' } })
   } else if (call.url.endsWith('/cascadeTreeOptions27')) {
    call.success({ code: 200, data: [{ id: tree, name: '原树叶', hasChildren: false }] })
   } else {
    assert.match(call.url, /\/cascadeTree(Options|Path)(28|29)$/)
    const rows = call.data.levelKey === 'country' ? [{ id: country, name: '甲国' }, { id: country2, name: '乙国' }]
     : call.data.levelKey === 'area' ? [{ id: area, name: '甲区' }, { id: area2, name: '乙区' }]
     : [{ id: place, name: '甲城' }]
    call.success({ code: 200, data: rows })
   }
   call.complete?.({})
  }
  await form.flush()
 }
}

let failure
try {
 await form.flush()
 await pump()
 const self = trees.find(item => item.leafIdProperty === 'treeNodeId')
 const short = trees.find(item => item.leafIdProperty === 'areaId')
 const long = trees.find(item => item.leafIdProperty === 'placeId')
 assert.ok(self && short && long)
 self.handleInput()
 await pump()
 assert.ok(calls.some(call => call.url.endsWith('/cascadeTreePath27')))
 assert.equal(self.displayText, '原树叶')
 self.activeLevelIndex = 0
 self.handleOptionSelect({ id: tree, name: '原树叶', hasChildren: false }, 0)
 await pump()
 long.handleInput()
 await pump()
 long.activeLevelIndex = 0
 long.handleOptionSelect({ id: country, name: '甲国' }, 0)
 await pump()
 long.activeLevelIndex = 1
 long.handleOptionSelect({ id: area, name: '甲区' }, 0)
 await pump()
 long.activeLevelIndex = 2
 long.handleOptionSelect({ id: place, name: '甲城' }, 0)
 await pump()
 assert.equal(form.state.entity.placeId, place)
 assert.equal(short.internalValue.areaId, area)
 short.activeLevelIndex = 0
 short.handleOptionSelect({ id: country2, name: '乙国' }, 1)
 await pump()
 assert.equal(form.state.entity.countryId, country2)
 assert.equal(form.state.entity.areaId, '')
 assert.equal(form.state.entity.placeId, '')
 assert.equal(form.state.entity.placeName, '')
 short.activeLevelIndex = 1
 short.handleOptionSelect({ id: area2, name: '乙区' }, 1)
 await pump()
 assert.equal(long.internalValue.areaId, area2)

 form.state.customerButtonHandler63()
 form.dialogs.at(-1).success({ confirm: true })
 await form.flush()
 const execute = form.requests.find(entry => entry.options.url === '/admin/sys/config_button/execute')
 assert.ok(execute)
 const entity = plain(execute.options.data.entity)
 assert.equal(entity.treeNodeId, tree)
 assert.equal(entity.treeNodeName, '原树叶')
 assert.equal(entity.countryId, country2)
 assert.equal(entity.areaId, area2)
 assert.equal(entity.areaName, '乙区')
 assert.equal(entity.placeId, '')
 assert.equal(form.writes().length, 0)
} catch (error) {
 failure = error
 throw error
} finally {
 try { form.app.unmount() } catch (error) { if (!failure) throw error }
}
