import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { mountGenerated } from './GeneratedOriginalVueHarness.mjs'

const source = await readFile(process.argv[2], 'utf8')
const parent = process.argv[3] ?? ' 甲+001&区#北% '
const page = mountGenerated(source, {}, { id: '101', parent_id: parent })
try {
  await page.flush()
  const detail = page.requests.find(request => request.options.url.includes('/detail/'))
  assert(detail)
  // 实际生成详情请求保留父键原文，URL 保留符不会拆成其它参数。
  assert.equal(page.state.parent_id, parent)
  const url = new URL(detail.options.url, 'http://localhost')
  assert.deepEqual([...url.searchParams], [['parentId', parent]])
} finally {
  page.app.unmount()
}
