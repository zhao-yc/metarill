import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend = join(process.argv[2], 'frontend')
const global = process.argv[3] === 'global'
const label = process.argv[3] === 'derived' ? 'reviewCountryLabel' : 'reviewName'
const source = await readFile(join(frontend, 'pages/yzp/summary_form-detail-102.vue'), 'utf8')
assert.match(source, /<zxz-uni-data-select v-model='entity.reviewId'/)
const form = mountGenerated(source, {}, {}, { runtimeDirectory: join(frontend, 'common/yzp_runtime') })
const parentA = '9007199254742000', parentB = '9007199254742001'
const id = '9007199254740997'
const endpoint = '/admin/auto/summary_form/102/findNameIdReview39'
const pages = () => form.requests.filter(request => request.options.url === endpoint + '/page')
const row = (name) => ({ id, name, value: id, text: name })
try {
 await form.flush()
 form.state.openOriginalRemoteOptions('39')
 assert.equal(pages().length, 0)
 form.state.entity.title = '第一次范围'
 form.state.entity.reviewCountryId = parentA
 await form.flush()
 form.state.openOriginalRemoteOptions('39')
 const previous = pages().at(-1)
 assert.equal(previous.options.data.targetParentId, parentA)
 if (!global) assert.equal(previous.options.data.formParameters.summaryForm.title, '第一次范围')
 form.state.entity.title = '第二次范围'
 form.state.entity.reviewCountryId = parentB
 await form.flush()
 form.respond(previous, { records: [row('旧父级')], total: 1, pages: 1 })
 assert.deepEqual(plain(form.state.review_list), [])

 form.state.openOriginalRemoteOptions('39')
 const current = pages().at(-1)
 assert.equal(current.options.data.targetParentId, parentB)
 if (!global) assert.equal(current.options.data.formParameters.summaryForm.title, '第二次范围')
 form.respond(current, { records: [row('当前候选')], total: 1, pages: 1 })
 assert.equal(form.state.review_list[0].name, '当前候选')
 form.state.reviewIdDetailChange(form.state.review_list[0])
 assert.equal(form.state.entity.reviewId, id)
 assert.equal(form.state.entity[label], '当前候选')
 const selected = form.requests.findLast(request => request.options.url === endpoint + '/selected')
 assert.equal(selected.options.data.targetParentId, parentB)
 assert.deepEqual(plain(selected.options.data.selectedIds), [id])
 if (!global) assert.equal(selected.options.data.formParameters.summaryForm.title, '第二次范围')
 form.respond(selected, [row('当前候选')])

 form.state.customerButtonHandler63()
 form.dialogs.at(-1).success({ confirm: true })
 await form.flush()
 const command = form.requests.find(entry => entry.options.url === '/admin/sys/config_button/execute')
 assert.equal(command.options.data.entity.reviewId, id)
 assert.equal(command.options.data.entity[label], '当前候选')
 assert.equal(form.writes().length, 0)
} finally {
 form.app.unmount()
}
