import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const [output, scenario] = process.argv.slice(2)
const frontend = join(output, 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/relation_source-list-201.vue'), 'utf8')
assert(source.includes("multiple v-model='filters.targetIds' :localdata='targetIdsRange'"))
const page = mountGenerated(source, ['independent', 'unbound'].includes(scenario) ? {} : { parent_id: '71', is_son_page: true }, {}, {
  runtimeDirectory: join(frontend, 'common/yzp_runtime')
})
const targetRequests = () => page.requests.filter(request => request.options.url.endsWith('/relation_target/301/page'))
const labelRequests = () => page.requests.filter(request => request.options.url.endsWith('/relation_target/301/selection/selected'))
const sourceRequests = () => page.requests.filter(request => request.options.url.endsWith('/relation_source/201/page'))
const records = (count, start = 1) => Array.from({ length: count }, (_, index) => ({ id: String(start + index), name: '选项' + (start + index) }))
const respondPage = (request, rows, total = rows.length) => page.respond(request, {
  records: rows, total, pages: Math.ceil(total / 1000), current: request.options.data.pageNo, size: 1000
})
const range = () => plain(page.state.targetIdsRange)
const adjacent = '9007199254740993'

try {
  await page.flush()
  if (scenario === 'unbound' || scenario === 'redundant') {
    assert.equal(targetRequests().length, 0)
    assert.deepEqual(range(), [])
  } else {
    const first = targetRequests().at(-1)
    assert(first)
    assert.equal(first.options.method, 'POST')
    assert.equal(first.options.sendSessionId, true)
    const expected = { pageNo: 1, pageSize: 1000, filters: {}, selectedIds: [] }
    if (scenario !== 'root') expected.parentId = '71'
    assert.deepEqual(plain(first.options.data), expected)

    if (scenario === 'shared') {
      const [left, right] = targetRequests()
      assert.equal(targetRequests().length, 2)
      assert.equal(left.aborted, false)
      assert.equal(right.aborted, false)
      respondPage(left, [{ id: adjacent, name: '甲' }])
      respondPage(right, [{ id: '9223372036854775807', name: '乙' }])
      assert.deepEqual([range()[0].value, page.state.backupIdsRange[0].value].sort(), [adjacent, '9223372036854775807'].sort())
    } else if (scenario === 'typed') {
      const rows = records(201).map(row => ({ id: row.id, name: 9007199254740992 }))
      respondPage(first, rows)
      assert.deepEqual(range(), [])
      const labels = labelRequests().at(-1)
      assert.deepEqual(plain(labels.options.data), { parentId: '71', selectedIds: rows.slice(0, 200).map(row => row.id) })
      const precise = '9007199254740993.1234567890'
      page.respond(labels, rows.slice(0, 200).map(row => ({ id: row.id, name: precise })))
      assert.deepEqual(range(), [])
      const remaining = labelRequests().at(-1)
      assert.deepEqual(plain(remaining.options.data), { parentId: '71', selectedIds: ['201'] })
      page.respond(remaining, [{ id: '201', name: precise }])
      assert.equal(range().length, 201)
      assert.deepEqual(range().at(-1), { value: '201', text: precise })
    } else if (scenario === 'parent') {
      respondPage(first, records(1000), 1002)
      assert.deepEqual(range(), [])
      const second = targetRequests().at(-1)
      assert.deepEqual(plain(second.options.data), { ...expected, pageNo: 2 })
      page.state.filters.targetIds = [adjacent]
      page.setProps({ parent_id: '72' })
      await page.flush()
      assert.equal(second.aborted, true)
      assert.equal(page.state.filters.targetIds, undefined)
      respondPage(second, records(2, 1001), 1002)
      assert.deepEqual(range(), [])
      const current = targetRequests().at(-1)
      assert.deepEqual(plain(current.options.data), { ...expected, parentId: '72' })
      respondPage(current, [{ id: adjacent, name: '新父级' }])
      assert.deepEqual(range(), [{ value: adjacent, text: '新父级' }])
      page.state.filters.targetIds = [adjacent]
      page.state.filterList()
      assert.deepEqual(plain(sourceRequests().at(-1).options.data.filters.targetIds), [adjacent])
    } else if (scenario === 'independent') {
      respondPage(first, [{ id: adjacent, name: '旧范围' }])
      page.state.filters.targetIds = [adjacent]
      page.state.filters.parentId = '72'
      await page.flush()
      assert.equal(page.state.filters.targetIds, undefined)
      assert.deepEqual(range(), [])
      const second = targetRequests().at(-1)
      assert.deepEqual(plain(second.options.data), { ...expected, parentId: '72' })
      respondPage(second, [{ id: '9223372036854775807', name: '独立父级' }])
      assert.deepEqual(range(), [{ value: '9223372036854775807', text: '独立父级' }])
    } else {
      respondPage(first, records(1000), 1002)
      assert.deepEqual(range(), [])
      const second = targetRequests().at(-1)
      respondPage(second, records(2, 1001), 1002)
      assert.equal(range().length, 1002)
      page.state.initLists()
      respondPage(targetRequests().at(-1), [], 5001)
      assert.deepEqual(range(), [])
      assert.match(page.dialogs.at(-1).content, /5000/)
      page.state.initLists()
      respondPage(targetRequests().at(-1), records(1000), 1002)
      respondPage(targetRequests().at(-1), [{ id: '1', name: '重复' }, { id: '1002', name: '尾项' }], 1002)
      assert.deepEqual(range(), [])
      assert.match(page.dialogs.at(-1).content, /不完整/)
    }
  }
} finally {
  page.app.unmount()
}
