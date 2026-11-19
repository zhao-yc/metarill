import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend = join(process.argv[2], 'frontend')
const runtimeDirectory = join(frontend, 'common/yzp_runtime')
const page = filename => readFile(join(frontend, 'pages/yzp', filename), 'utf8')
const detailSource = await page('customer-detail-101.vue')
const listSource = await page('customer-list-101.vue')
assert.match(detailSource, /<picker mode='date' fields='month'[^>]*@change='billingMonthMonthChange'><uni-easyinput[^>]* disabled :class="billingMonthReadOnly \? '' : 'can-read'"[^>]*suffixIcon='bottom'/)
assert.ok(listSource.includes("@change='billingMonthMonthFilterChange'"), '原列表链接年月筛选事件')
const detail = mountGenerated(detailSource, {}, {}, { runtimeDirectory })
const list = mountGenerated(listSource, {}, {}, { runtimeDirectory })
const change = value => ({ detail: { value } })
try {
 await detail.flush()
 await list.flush()
 assert.equal(detail.state.entity.billingMonth, '2024-02-01')
 detail.state.entity.name = '年月回归'
 detail.state.billingMonthMonthChange(change('2024-02'))
 detail.state.periodCodeMonthChange(change('2024-02'))
 assert.equal(detail.state.entity.billingMonth, '2024-02-01')
 assert.equal(detail.state.entity.periodCode, '2024-02')
 await detail.state.saveEntity()
 await detail.flush()
 const write = detail.writes().at(-1)
 assert.ok(write)
 assert.equal(write.options.data.billingMonth, '2024-02-01')
 assert.equal(write.options.data.periodCode, '2024-02')
 detail.state.billingMonthMonthChange(change(''))
 assert.equal(detail.state.entity.billingMonth, '')

 list.state.billingMonthMonthFilterChange(change('2024-02'))
 list.state.periodCodeMonthFilterChange(change('2024-02'))
 list.state.filterList()
 await list.flush()
 const request = list.pages().at(-1)
 assert.deepEqual(plain(request.options.data.filters.billingMonth), ['2024-02-01', '2024-02-29'])
 assert.equal(request.options.data.filters.periodCode, '2024-02')
 list.state.billingMonthMonthFilterChange(change(''))
 assert.deepEqual(plain(list.state.filters.billingMonth), [])
} finally {
 list.app.unmount()
 detail.app.unmount()
}
