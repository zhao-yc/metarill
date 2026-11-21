import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated } from './GeneratedOriginalVueHarness.mjs'

const runtime = new URL('../../../frontend/metarill_uni_admin/common/yzp_runtime/', import.meta.url)
const utils = { isBlank: value => value == null || typeof value === 'string' && !value.trim() }
const rule = await readFile(new URL('buttonVisibleRule.js', runtime), 'utf8')
const evaluate = new Function('utils', rule.replace(/^import .*$/m, '')
  .replace(/^export\s*\{[\s\S]*?\}\s*$/m, '') + ';return evaluateRule')(utils)
const yzpSource = await readFile(new URL('yzp-runtime.js', runtime), 'utf8')
const start = yzpSource.indexOf('\tisButtonVisible(')
const end = yzpSource.indexOf('\tshowUploadSuccess:', start)
assert(start >= 0 && end > start)
const real = new Function('utils', 'evaluateButtonVisibleRuleCore',
  'return ({' + yzpSource.slice(start, end) + '})')(utils, evaluate)
const pages = join(process.argv[2], 'frontend/pages/yzp')
const detail = mountGenerated(await readFile(join(pages, 'customer-detail-101.vue'), 'utf8'), {}, { id: '7' })
const list = mountGenerated(await readFile(join(pages, 'customer-list-101.vue'), 'utf8'))
for (const fixture of [detail, list]) {
  fixture.yzp.isColumnVisible = real.isColumnVisible
  fixture.yzp.isButtonVisible = real.isButtonVisible
}
const hasRemark = node => node.props.id === 'yzp-detail-field-remark' || node.children.some(hasRemark)
try {
  await detail.flush()
  detail.respond(detail.requests.find(item => item.options.url.includes('/detail/7')),
    { id: '7', name: '记录', remark: '审批后显示', approvalFlag: 1 })
  // 原详情的骨架在载入回执后按既有计时结束。只核对已实装的规则状态。
  await new Promise(resolve => setTimeout(resolve, 320))
  await detail.flush()
  assert.equal(detail.state.loadEntitySuccess, true)
  assert.equal(detail.state.customerButtonShow900, true, '布尔真回显为数字 1，动作应可见')
  assert.equal(hasRemark(detail.tree), true, '布尔真回显为数字 1，备注应可见')
  assert.deepEqual(detail.state.visibleDetailGroups.map(group => group.key), ['review'])
  for (const [value, visible] of [[1,true], ['1',true], [0,false], ['0',false], [true,true], [false,false], [null,false], [2,false]]) {
    detail.state.entity.approvalFlag = value
    await detail.flush()
    assert.equal(hasRemark(detail.tree), visible)
    assert.equal(detail.state.visibleDetailGroups.length, visible ? 1 : 0)
    assert.equal(detail.state.customerButtonShow900, visible)
    assert.equal(list.state.customerButtonShow900({ id: '7', approvalFlag: value }), visible)
    assert.equal(detail.state.entity.approvalFlag, value)
  }
  detail.state.entity.approvalFlag = 1
  detail.setPermission(false)
  list.setPermission(false)
  await detail.flush()
  assert.equal(detail.state.customerButtonShow900, false)
  assert.equal(list.state.customerButtonShow900({ id: '7', approvalFlag: 1 }), false)
} finally {
  detail.app.unmount()
  list.app.unmount()
}
