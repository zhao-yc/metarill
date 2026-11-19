import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend = join(process.argv[2], 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/relation_source-list-201.vue'), 'utf8')
for (const key of ['billingMonth', 'eventTime', 'priority', 'referenceNo', 'isActive', 'targetIds']) {
  assert(source.includes(`multiple v-model='filters.${key}' :localdata='${key}Range'`))
}
assert(!source.includes('billingMonthMonthFilterChange'))
assert(!source.includes('this.filters.targetNames='))

const page = mountGenerated(source, { parent_id: '71', is_son_page: true }, {}, {
  runtimeDirectory: join(frontend, 'common/yzp_runtime')
})
try {
  await page.flush()
  assert.deepEqual(plain(page.state.targetIdsRange), [], '关系配置明细不能变成候选标签')
  assert.deepEqual(plain(page.state.isActiveRange).map(option => option.value), [0, 1])
  const sourcePages = () => page.requests.filter(request => request.options.url.endsWith("/relation_source/201/page"))
  const initial = sourcePages().at(-1)
  const selected = {
    targetIds: process.argv[3] === 'nonredundant' ? ['1', '9007199254740993'] : ['["1","9007199254740993"]'],
    billingMonth: ['2026-10'],
    eventTime: ['2026-10-20 09:30:00', '2026-10-20 17:15:00'],
    priority: [2],
    referenceNo: ['9007199254740993'],
    isActive: [process.argv[3] === 'nonredundant' ? 1 : 0]
  }
  assert.deepEqual(plain(page.state.filters), selected)
  assert.deepEqual(plain(initial.options.data.filters), selected)
  assert.equal(initial.options.data.parentId, '71')
  page.respond(initial, { records: [], total: 0, pages: 0 })

  page.state.resetFilter()
  await page.flush()
  assert.deepEqual(plain(page.state.filters), {})
  page.state.filterList()
  await page.flush()
  const cleared = sourcePages().at(-1)
  assert.notEqual(cleared, initial)
  assert.deepEqual(plain(cleared.options.data.filters), {})
  assert.deepEqual(plain(initial.options.data.filters), selected)
} finally {
  page.app.unmount()
}
