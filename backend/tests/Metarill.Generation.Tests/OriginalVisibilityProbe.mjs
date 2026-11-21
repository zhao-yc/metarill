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
if (process.argv[3].startsWith('editor-')) {
  const kind = process.argv[3].slice('editor-'.length)
  const key = kind === 'button' ? 'visibleRuleJson' : 'visibleCondition'
  const component = 'yzp-' + kind + '-visible-rule-editor'
  const url = new URL('../../../frontend/metarill_uni_admin/components/' + component + '/' + component + '.vue', import.meta.url)
  const runtimeScript = rule.replace(/^import .*$/m, "import utils from '@/common/utils';")
    .replace(/^export\s*\{[\s\S]*?\}\s*$/m, '')
  const editorSource = (await readFile(url, 'utf8'))
    .replace(/import\s+\{[^}]+\}\s+from\s+'@\/common\/yzp_runtime\/buttonVisibleRule'/, () => runtimeScript)
  const stored = '{"logic":"AND","conditions":[{"field":"state","operator":"eq","value":"待,审"},'
    + '{"field":"label","operator":"in","valueList":["a,b","两行\\n其二",null,9007199254740993]},'
    + '{"field":"owner","operator":"eq","value":9007199254740993}],'
    + '"groups":[{"logic":"OR","conditions":[{"field":"branch","operator":"eq","value":"乙"}],'
    + '"groups":[{"conditions":[{"field":"ticket","operator":"eq","value":9007199254740995}]}]}]}'
  const emitted = []
  const editor = mountGenerated(editorSource, {
    entity: { id: '7', caption: '原记录', [key]: stored },
    onReceive(entity) {
      emitted.push(entity)
      editor.setProps({ entity })
    }
  })
  try {
    await editor.flush()
    assert.equal(emitted.length, 0, '加载配置不回写')
    assert.equal(editor.state.rule.conditions[0].value, '待,审', '等于条件的逗号属于普通文字')
    assert.equal(editor.state.rule.conditions[2].value, '9007199254740993')
    editor.state.rule.conditions[0].value = '已审'
    await editor.flush()
    assert.equal(emitted.length, 1)
    let saved = emitted[0][key]
    let parsed = JSON.parse(saved)
    assert.equal(emitted[0].caption, '原记录')
    assert.equal(parsed.conditions[0].value, '已审')
    assert.equal(parsed.groups?.[0]?.logic, 'OR', kind + ' 应保留原嵌套分组')
    assert.deepEqual(parsed.conditions[1].valueList.slice(0, 3), ['a,b', '两行\n其二', null], kind + ' 应保留原候选分隔符和空值')
    assert.match(saved, /"owner","operator":"eq","value":9007199254740993/)
    assert.match(saved, /"ticket","operator":"eq","value":9007199254740995/)

    editor.state.rule.conditions[1].operator = 'notIn'
    await editor.flush()
    saved = emitted.at(-1)[key]
    parsed = JSON.parse(saved)
    assert.equal(parsed.conditions[1].operator, 'notIn')
    assert.deepEqual(parsed.conditions[1].valueList.slice(0, 3), ['a,b', '两行\n其二', null])

    editor.setProps({ entity: { id: '8', [key]: saved.replace('"value":"乙"', '"value":"丙"') } })
    await editor.flush()
    assert.equal(emitted.length, 2, '换记录只回显')
    assert.equal(JSON.parse(editor.state.serializedRule).groups[0].conditions[0].value, '丙')
    const previousLabels = editor.state.rule.conditions[1].value
    editor.state.rule.conditions[1].value = '甲,丁'
    await editor.flush()
    const otherRecordRule = emitted.at(-1)[key]
    assert.deepEqual(JSON.parse(otherRecordRule).conditions[1].valueList, ['甲', '丁'])
    editor.setProps({ entity: { id: '9', [key]: otherRecordRule } })
    await editor.flush()
    editor.state.rule.conditions[1].value = previousLabels
    await editor.flush()
    assert.deepEqual(JSON.parse(emitted.at(-1)[key]).conditions[1].valueList,
      ['a', 'b', '两行', '其二', '9007199254740993'], '切换同规则文本的记录不能沿用上一记录的候选原值')

    editor.state.rule.conditions.splice(0)
    await editor.flush()
    assert.deepEqual(JSON.parse(emitted.at(-1)[key]).conditions, [])
    assert.equal(JSON.parse(emitted.at(-1)[key]).groups[0].conditions[0].value, '丙')
  } finally {
    editor.app.unmount()
  }
  process.exit(0)
}
const pages = join(process.argv[2], 'frontend/pages/yzp')
const detail = mountGenerated(await readFile(join(pages, 'customer-detail-101.vue'), 'utf8'), {}, { id: '7' })
const list = mountGenerated(await readFile(join(pages, 'customer-list-101.vue'), 'utf8'))
for (const fixture of [detail, list]) {
  fixture.yzp.isColumnVisible = real.isColumnVisible
  fixture.yzp.isButtonVisible = real.isButtonVisible
}
const hasRemark = node => node.props.id === 'yzp-detail-field-remark' || node.children.some(hasRemark)
if (process.argv[3] === 'numeric' || process.argv[3] === 'json-numeric') {
  const jsonMember = process.argv[3] === 'json-numeric'
  const payload = id => '{"review":{"owner":' + id + '},"text":' + JSON.stringify('含 " 引号和数字 123') + '}'
  try {
    await detail.flush()
    detail.respond(detail.requests.find(item => item.options.url.includes('/detail/7')),
      { id: '7', name: '记录', remark: '审批后显示', ownerId: '9007199254740992',
        ...(jsonMember ? { approvalPayload: payload('9007199254740992') } : {}) })
    await new Promise(resolve => setTimeout(resolve, 320))
    await detail.flush()
    assert.equal(detail.state.loadEntitySuccess, true)
    assert.equal(detail.state.customerButtonShow900, false, '相邻雪花 ID 不可匹配目标编号')
    assert.equal(hasRemark(detail.tree), false)
    assert.equal(detail.state.visibleDetailGroups.length, 0)
    assert.equal(list.state.customerButtonShow900({ id: '7', ownerId: '9007199254740992' }), false)
    detail.state.entity.ownerId = '9007199254740993'
    if (jsonMember) detail.state.entity.approvalPayload = payload('9007199254740993')
    await detail.flush()
    assert.equal(detail.state.customerButtonShow900, true)
    assert.equal(hasRemark(detail.tree), true)
    assert.deepEqual(detail.state.visibleDetailGroups.map(group => group.key), ['review'])
    assert.equal(list.state.customerButtonShow900({ id: '7', ownerId: '9007199254740993' }), true)
    assert.equal(detail.state.entity.ownerId, '9007199254740993')
    if (jsonMember) {
      assert.equal(detail.state.entity.approvalPayload, payload('9007199254740993'))
      detail.state.entity.approvalPayload = '{"review":[9007199254740993]}'
      await detail.flush()
      assert.equal(hasRemark(detail.tree), false)
      detail.state.entity.approvalPayload = '{"review":{"owner":9007199254740993}'
      await detail.flush()
      assert.equal(hasRemark(detail.tree), false)
    }

    const compare = (actual, operator, expected) =>
      evaluate(JSON.stringify({ conditions: [{ field: 'value', operator, value: expected }] }), { value: actual })
    assert.equal(compare('0.10000000000000000000002', 'gt', '1.0000000000000000000001e-1'), true)
    assert.equal(compare('-0.10000000000000000000002', 'lt', '-1.0000000000000000000001e-1'), true)
    assert.equal(compare('0002.000', 'eq', '+2e0'), true)
    assert.equal(compare('1e2147483648', 'gt', '9e2147483647'), true)
    assert.equal(compare('-0e-2147483647', 'eq', '0'), true)
    assert.equal(compare('包含,1]的原文本', 'eq', '包含,1]的原文本'), true)
    assert.equal(compare('表单空值', 'eq', ''), false)
  } finally {
    detail.app.unmount()
    list.app.unmount()
  }
  process.exit(0)
}
if (process.argv[3] === 'text') {
  try {
    await detail.flush()
    detail.respond(detail.requests.find(item => item.options.url.includes('/detail/7')),
      { id: '7', name: 'A', remark: '审批后显示' })
    await new Promise(resolve => setTimeout(resolve, 320))
    await detail.flush()
    assert.equal(detail.state.loadEntitySuccess, true)
    for (const [name, showColumn, showButton] of [
      ['A', true, true], ['a', false, false], ['e\u0301', false, false], ['\u00e9', false, true],
      ['\u00a0', false, true], ['\u001c', true, true], ['', true, true], ['\u202f', false, false]
    ]) {
      detail.state.entity.name = name
      await detail.flush()
      const label = JSON.stringify(name)
      assert.equal(hasRemark(detail.tree), showColumn, '字段的原文字顺序 ' + label)
      assert.equal(detail.state.visibleDetailGroups.length, showColumn ? 1 : 0, '分组 ' + label)
      assert.equal(detail.state.customerButtonShow900, showButton, '详情按钮 ' + label)
      assert.equal(list.state.customerButtonShow900({ id: '7', name }), showButton, '行按钮 ' + label)
      assert.equal(detail.state.entity.name, name)
    }

    // 条件编辑器提交的包含值仍可用逗号或换行分隔，不丢掉不换行空格。
    const nodes = JSON.stringify({ conditions: [{ field: 'value', operator: 'in', value: 'A,\u00a0,\u001c' }] })
    assert.equal(evaluate(nodes, { value: '\u00a0' }), true)
    assert.equal(evaluate(nodes, { value: '\u001c' }), false)
  } finally {
    detail.app.unmount()
    list.app.unmount()
  }
  process.exit(0)
}
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
