import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend = join(process.argv[2], 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/summary_form-detail-102.vue'), 'utf8')
const require = createRequire(new URL('../../../frontend/metarill_uni_admin/package.json', import.meta.url))
const Vue = require('vue')
const { parse } = require('@vue/compiler-sfc')
const checkboxSource = await readFile(join(frontend, 'uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue'), 'utf8')
const { descriptor, errors } = parse(checkboxSource)
assert.deepEqual(errors, [])
const checkbox = new Function('uniCloud', descriptor.script.content.replace('export default', 'return'))({ mixinDatacom: { props: { collection: String } } })
const boxes = []
checkbox.mounted = function () { boxes.push(this) }
checkbox.render = Vue.compile(descriptor.template.content, {
 isCustomElement: tag => ['view', 'text', 'checkbox', 'checkbox-group', 'radio', 'radio-group', 'label'].includes(tag)
})
const app = mountGenerated(source, {}, {}, {
 runtimeDirectory: join(frontend, 'common/yzp_runtime'),
 components: { 'uni-data-checkbox': checkbox, 'uni-load-more': { render() { return Vue.h('loading') } } }
})
const ids = ['9007199254740993', '9007199254740994']
const names = ['客户甲', '客户乙']

let failure
try {
 await app.flush()
 assert.equal(app.state.readOnlyCondition, false)
 const checkboxRequests = () => app.requests.filter(entry => entry.options.url.endsWith('/findNameIdApproved26'))
 const firstRequest = checkboxRequests().at(-1)
 assert.deepEqual(plain(firstRequest.options.data.formParameters), { summaryForm: { title: '业务汇总' } })
 app.state.entity.title = '新条件'
 await app.flush()
 const filteredRequest = checkboxRequests().at(-1)
 assert.notEqual(firstRequest, filteredRequest)
 assert.deepEqual(plain(filteredRequest.options.data.formParameters), { summaryForm: { title: '新条件' } })
 const candidates = ids.map((id, index) => ({ id, value: id, name: names[index], text: names[index] }))
 app.respond(firstRequest, [{ id: '7', value: '7', name: '旧条件', text: '旧条件' }])
 assert.deepEqual(plain(app.state.approvedIdsList), [])
 app.respond(filteredRequest, candidates)
 await app.flush()
 const all = boxes.find(box => box.localdata[0]?.text === '全选')
 const items = boxes.find(box => box.localdata[0]?.value === ids[0])
 assert.ok(all && items)
 all.change({ detail: { value: ['1'] } })
 await app.flush()
 assert.deepEqual(plain(app.state.entity.approvedIds), ids)
 assert.deepEqual(plain(app.state.entity.approvedNames), names)
 items.change({ detail: { value: [ids[1]] } })
 await app.flush()
 assert.deepEqual(plain(app.state.approvedIdsSelectAll), [])
 assert.deepEqual(plain(app.state.entity.approvedNames), [names[1]])

 app.state.openSelect25()
 await app.flush()
 app.targets.at(-1).confirm(ids.map((id, index) => ({ id, name: names[index] })))
 await app.flush()
 assert.deepEqual(plain(app.state.entity.customerIds), ids)
 assert.deepEqual(plain(app.state.entity.customerNames), names)

 app.state.openSelect25()
 await app.flush()
 assert.deepEqual(plain(app.targets.at(-1).selectedIds), ids)
 assert.deepEqual(plain(app.targets.at(-1).selectedNames), names)
 app.targets.at(-1).cancel()

 app.state.customerButtonHandler63()
 app.dialogs.at(-1).success({ confirm: true })
 await app.flush()
 const request = app.requests.find(entry => entry.options.url === '/admin/sys/config_button/execute')
 assert.ok(request)
 assert.deepEqual(plain(request.options.data.entity.customerIds), ids)
 assert.deepEqual(plain(request.options.data.entity.customerNames), names)
 assert.deepEqual(plain(request.options.data.entity.approvedIds), [ids[1]])
 assert.deepEqual(plain(request.options.data.entity.approvedNames), [names[1]])
 assert.equal(app.validations(), 1)
 assert.equal(app.writes().length, 0)
} catch (error) {
 failure = error
 throw error
} finally {
 try { app.app.unmount() } catch (error) { if (!failure) throw error }
}
