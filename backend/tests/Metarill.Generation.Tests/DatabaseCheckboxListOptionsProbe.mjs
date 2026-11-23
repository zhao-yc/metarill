import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const [output, scenario] = process.argv.slice(2)
const frontend = join(output, 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/relation_source-list-201.vue'), 'utf8')
const dropdown = scenario.endsWith('_dropdown')
assert(source.includes(dropdown
  ? "<zxz-uni-data-select v-model='filters.targetIds' :localdata='target__list'"
  : "multiple v-model='filters.targetIds' :localdata='targetIdsRange'"))
const stored = scenario.startsWith('stored')
const root = scenario === 'root' || stored
const page = mountGenerated(source, root ? {} : { parent_id: '71', is_son_page: true }, {}, {
  runtimeDirectory: join(frontend, 'common/yzp_runtime')
})
const key = '9007199254740993'
const nextKey = '9007199254740994'
const optionRequests = () => page.requests.filter(request => request.options.url.endsWith('/findNameIdTarget25'))
const option = (id, text) => ({ id, name: text, value: id, text })
const range = () => plain(dropdown ? page.state.target__list : page.state.targetIdsRange)
const displayed = (...items) => dropdown ? items : items.map(({text, value}) => ({text, value}))
const empty = () => assert.deepEqual(range(), [])
const broadcast = name => {
  const listeners = [...(page.subscriptions.get(name) || [])]
  assert(listeners.length > 0, `页面应订阅原 ${name} 事件`)
  for (const listener of listeners) listener()
}

try {
  await page.flush()
  if (scenario === 'unbound' || scenario === 'redundant') {
    assert.equal(optionRequests().length, 0)
    empty()
  } else {
    const first = optionRequests().at(-1)
    assert(first)
    assert.equal(first.options.method, 'POST')
    assert.equal(first.options.sendSessionId, true)
    const expected = root ? {} : { parentId: '71' }
    if (scenario === 'form') expected.formParameters = { relationSource: { categoryId: key } }
    assert.deepEqual(plain(first.options.data ?? {}), expected)
    page.respond(first, [option(key, '甲'), option(nextKey, '乙')])
    assert.deepEqual(range(), displayed(option(key, '甲'), option(nextKey, '乙')))

    if (stored) assert.deepEqual(plain(page.state.filters.targetIds), dropdown ? key : [key])
    const chosen = dropdown ? nextKey : [key, nextKey]
    page.state.filters.targetIds = chosen
    page.state.filterList()
    assert.deepEqual(plain(page.pages().at(-1).options.data.filters.targetIds), chosen)

    if (scenario === 'form') {
      const count = optionRequests().length
      page.state.filters.categoryId = Number(key)
      await page.flush()
      assert.equal(optionRequests().length, count)
      assert.equal(page.state.filters.targetIds, undefined)
      empty()
      page.respond(first, [option(key, '旧条件')])
      empty()
      page.state.filters.categoryId = nextKey
      await page.flush()
      const second = optionRequests().at(-1)
      assert.notEqual(first, second)
      assert.deepEqual(plain(second.options.data), { parentId: '71', formParameters: { relationSource: { categoryId: nextKey } } })
    }

    if (scenario === 'parent') {
      page.setProps({ parent_id: '72' })
      await page.flush()
      assert.equal(first.aborted, true)
      assert.equal(page.state.filters.targetIds, undefined)
      empty()
      page.respond(first, [option(key, '旧父级')])
      empty()
      const second = optionRequests().at(-1)
      assert.notEqual(first, second)
      assert.deepEqual(plain(second.options.data), { parentId: '72' })
      const currentPage = page.pages().at(-1)
      assert.equal(currentPage.options.data.parentId, '72')
      assert.equal(currentPage.options.data.filters.targetIds, undefined)
      page.respond(second, [option(nextKey, '新父级')])
      assert.deepEqual(range(), [{ text: '新父级', value: nextKey }])
    }

    const pending = optionRequests().at(-1)
    const beforeLogout = optionRequests().length
    page.state.filters.targetIds = dropdown ? key : [key]
    page.storage.loginStateId = ''
    broadcast('onLogout')
    await page.flush()
    assert.equal(pending.aborted, true)
    assert.equal(page.state.filters.targetIds, undefined)
    empty()
    page.respond(pending, [option(key, '已退出账号')])
    empty()
    assert.equal(optionRequests().length, beforeLogout)

    page.storage.loginStateId = '新会话'
    broadcast('onLogin')
    await page.flush()
    assert.equal(optionRequests().length, beforeLogout + 1)
    assert.equal(page.state.filters.targetIds, undefined)
    const reopened = optionRequests().at(-1)
    page.respond(reopened, [option(nextKey, '当前账号')])
    assert.deepEqual(range(), displayed(option(nextKey, '当前账号')))
  }
} finally {
  page.app.unmount()
}
