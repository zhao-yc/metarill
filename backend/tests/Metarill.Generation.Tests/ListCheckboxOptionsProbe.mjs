import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const [output, scenario, table, pageId, endpoint] = process.argv.slice(2)
const frontend = join(output, 'frontend')
const source = await readFile(join(frontend, 'pages/yzp', `${table}-list-${pageId}.vue`), 'utf8')
const key = scenario === 'catalog' ? 'yzYzpVersionId' : 'targetId'
assert(source.includes(`multiple v-model='filters.${key}' :localdata='${key}Range'`))
const root = scenario === 'root' || scenario === 'catalog'
const page = mountGenerated(source, root ? {} : { parent_id: '71', is_son_page: true }, {}, {
  runtimeDirectory: join(frontend, 'common/yzp_runtime')
})
const requestList = () => page.requests.filter(request => request.options.url.endsWith('/' + endpoint))
const readOptions = () => plain(page.state[key + 'Range'])
const id = scenario === 'catalog' ? '2300000000000000001' : '9007199254740993'
const nextId = '9223372036854775807'
const option = (id, text) => ({ id, value: id, name: text, text })

try {
  await page.flush()
  if (scenario === 'unbound') {
    assert.equal(requestList().length, 0)
    assert.deepEqual(readOptions(), [])
  } else {
    if (scenario === 'independent') {
      assert.equal(requestList().length, 0)
      page.state.filters.parentId = '81'
      await page.flush()
    }
    const first = requestList().at(-1)
    assert(first)
    assert.equal(first.options.method, 'POST')
    assert.equal(first.options.sendSessionId, true)
    const scope = root ? {} : { parentId: '71', targetParentId: scenario === 'independent' ? '81' : '71' }
    if (scenario === 'form') scope.formParameters = { relationSource: { categoryId: id } }
    assert.deepEqual(plain(first.options.data), scope)

    const rows = scenario === 'catalog' ? [option(id, 'Metarill C# v1')] : [option(id, '甲'), option(nextId, '乙')]
    page.respond(first, rows)
    assert.deepEqual(readOptions(), rows.map(({text, value}) => ({text, value})))
    page.state.filters[key] = rows.map(row => row.value)
    page.state.filterList()
    const businessQuery = page.requests.filter(request => request.options.url.endsWith(`/${table}/${pageId}/page`)).at(-1)
    assert.deepEqual(plain(businessQuery.options.data.filters[key]), rows.map(row => row.value))

    if (scenario === 'independent' || scenario === 'form' || scenario === 'same') {
      if (scenario === 'independent') page.state.filters.parentId = '82'
      else if (scenario === 'form') page.state.filters.categoryId = nextId
      else page.setProps({ parent_id: '72' })
      await page.flush()
      assert.deepEqual(readOptions(), [])
      assert.equal(page.state.filters[key], undefined)
      page.respond(first, [option(id, '旧范围')])
      assert.deepEqual(readOptions(), [])
      const next = requestList().at(-1)
      assert.notEqual(next, first)
      if (scenario === 'independent') assert.deepEqual(plain(next.options.data), { parentId: '71', targetParentId: '82' })
      if (scenario === 'form') assert.deepEqual(plain(next.options.data), { parentId: '71', targetParentId: '71',
        formParameters: { relationSource: { categoryId: nextId } } })
      if (scenario === 'same') assert.deepEqual(plain(next.options.data), { parentId: '72', targetParentId: '72' })
      page.respond(next, [option(nextId, '当前范围')])
      assert.deepEqual(readOptions(), [{ text: '当前范围', value: nextId }])
    }

    if (scenario === 'root') {
      page.state.initLists()
      const denied = requestList().at(-1)
      denied.options.success({ code: 401, msg: '无权读取候选' })
      assert.deepEqual(readOptions(), [])
      assert.equal(page.dialogs.at(-1).content, '无权读取候选')
      page.state.initLists()
      page.respond(requestList().at(-1), [option(id, '甲'), option(id, '重复')])
      assert.deepEqual(readOptions(), [])
      assert.match(page.dialogs.at(-1).content, /响应无效/)
    }
  }
} finally {
  page.app.unmount()
}
