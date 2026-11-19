import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend = join(process.argv[2], 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/summary_form-detail-102.vue'), 'utf8')
const app = mountGenerated(source, {}, {}, {
 runtimeDirectory: join(frontend, 'common/yzp_runtime')
})
const ids = ['9007199254740993', '9007199254740994']
const names = ['客户甲', '客户乙']

try {
 await app.flush()
 assert.equal(app.state.readOnlyCondition, false)
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
 assert.equal(app.validations(), 1)
 assert.equal(app.writes().length, 0)
} finally {
 app.app.unmount()
}
