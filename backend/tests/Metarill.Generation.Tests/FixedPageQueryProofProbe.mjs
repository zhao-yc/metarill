import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/config_child-list-1679390851298631682.vue'),'utf8')
const fixture=mountGenerated(source,{})
try {
  fixture.state.filters.parentConfigPageName='父页中文_%'
  fixture.state.filters.childConfigPageName='子页引号\''
  fixture.state.filterList()
  const request=fixture.pages().at(-1)
  assert(request)
  assert.deepEqual(plain(request.options.data.filters),{parentConfigPageName:'父页中文_%',childConfigPageName:'子页引号\''})
  assert.equal(Object.hasOwn(request.options.data,'whereSql'),false)
  assert.equal(Object.hasOwn(request.options.data.filters,'parentConfigPageId'),false)
  assert.equal(Object.hasOwn(request.options.data.filters,'childConfigPageId'),false)
} finally {fixture.app.unmount()}
console.log('通过：原平台两个名称筛选仍按固定辅助键提交，没有新增SQL或项目参数')
