import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated } from './GeneratedOriginalVueHarness.mjs'

const [root, payload, type] = process.argv.slice(2)
const data = JSON.parse(payload)
const original = data.businessKey
const saved = type === 'string' ? ' A-02+南&区 ' : '9007199254740999'
const read = name => readFile(join(root, 'frontend/pages/yzp', name), 'utf8')
const parent = mountGenerated(await read('crosschild_country-detail-301.vue'), {}, { id: data.id })
let list
let detail
try {
  assert.equal(parent.targets.length, 0)
  parent.respond(parent.requests.find(entry => entry.options.url.includes('/detail/')), data)
  await parent.flush()
  const linked = parent.targets[0]
  assert(linked)
  assert.equal(linked.parent_id, original)
  assert.notEqual(linked.parent_id, data.id)

  parent.state.change('businessKey', saved)
  await parent.flush()
  assert.equal(parent.state.entity.businessKey, saved)
  assert.equal(linked.parent_id, original)
  const refreshParents = []
  linked.refreshData = () => refreshParents.push(linked.parent_id)
  parent.state.saveEntity()
  await parent.flush()
  const write = parent.writes().at(-1)
  assert(write)
  assert.equal(write.options.data.businessKey, saved)
  parent.respond(write, { ...data, businessKey: saved })
  await parent.flush()
  assert.equal(linked.parent_id, saved)
  assert.deepEqual(refreshParents, [saved])

  // 使用真实生成子列表及详情，验证查询体、移动新页和详情接口的父键。
  list = mountGenerated(await read('crosschild_source-list-201.vue'), {
    parent_id: linked.parent_id, parent_page_sn: '301', is_son_page: true, child_page_open_type: 2
  })
  await list.flush()
  assert.equal(list.pages().at(-1).options.data.parentId, saved)
  list.state.$deviceInfo.is_pc = false
  list.state.toDetail('205')
  const navigation = new URL(list.navigations.at(-1).url, 'http://localhost')
  assert.equal(navigation.searchParams.get('parent_id'), saved)
  detail = mountGenerated(await read('crosschild_source-detail-201.vue'), {}, Object.fromEntries(navigation.searchParams))
  await detail.flush()
  const detailRequest = detail.requests.find(entry => entry.options.url.includes('/detail/'))
  assert.equal(new URL(detailRequest.options.url, 'http://localhost').searchParams.get('parentId'), saved)

  parent.state.initPage({ id: '102' })
  await parent.flush()
  assert.equal(parent.state.originalChildrenReady, false)
  assert.deepEqual(Object.keys(parent.state.originalChildParentKeys), [])
} finally {
  detail?.app.unmount()
  list?.app.unmount()
  parent.app.unmount()
}
