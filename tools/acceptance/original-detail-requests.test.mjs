import test from 'node:test'
import assert from 'node:assert/strict'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { definition, inventoryDetails, verifyRequest } from './original-detail-requests.mjs'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const require = createRequire(path.join(root, 'frontend/metarill_uni_admin/package.json'))
const parser = require('@babel/parser')

/** imports、顶层代码、生命周期均不在本验证范围，不能意外执行网络或页面初始化。 */
test('方法装载只取原data与methods，保留局部方法闭包而不执行模块顶层', () => {
  const source = "import missing from 'not-installed';throw new Error('不得执行');export default {data(){return {id:''}},created(){throw new Error('不得执行')},methods:{route(){const action=this.id?'update':'add';return '/admin/a/'+action}}}"
  const component = definition(source, parser, {}, '边界.vue')
  const page = { ...component.data(), ...component.methods }
  assert.equal(page.route(), '/admin/a/add'); page.id='9007199254740993'; assert.equal(page.route(), '/admin/a/update')
  assert.equal(component.created, undefined); assert.ok(component.methodLines.route > 0)
})

test('缺失、重复或动态组件和methods明确失败，不静默减少覆盖', () => {
  for (const source of [
    'export default factory()',
    'export default {data(){return {}}}',
    'export default {data(){return {}},data(){return {}},methods:{}}',
    'export default {data(){return {}},methods:externalMethods}'
  ]) assert.throws(() => definition(source, parser, {}, '非法.vue'))
  let calls = 0
  for (const source of ['export default {data:danger(),methods:{}}', 'export default {data(){return {}},methods:danger()}', 'export default {data(){return {}},methods:{[danger()](){}}}', 'export default {data(){return {}},methods:{...danger()}}']) assert.throws(() => definition(source, parser, { danger: () => { calls++; return {} } }, '初始化表达式.vue'))
  assert.equal(calls, 0, '拒绝之前不能执行任何初始化表达式')
})

/** 读取真实18页源码，四种父值分别走原方法，取消和校验失败分支必须实际执行。 */
test('全部原详情页三端动态请求保持方法、长ID、父范围与零请求分支', async () => {
  const result = await inventoryDetails(path.join(root, 'frontend/metarill_uni_admin'))
  assert.ok(result.scannedFiles >= 18); assert.equal(result.errors.length, 0); assert.equal(result.unresolved.length, 0)
  assert.deepEqual([...new Set(result.fixed.map(x => x.platform))].sort(), ['APP','H5','WEIXIN'])
  for (const action of ['detail','add','update','delete','copy']) assert.ok(result.fixed.some(x => x.action === action))
  assert.ok(result.cases.some(x => x.blocked && x.action === 'add'))
  assert.ok(result.cases.some(x => x.blocked && x.action === 'delete'))
  assert.ok(result.cases.some(x => x.blocked && x.action === 'copy'))
  assert.ok(result.cases.filter(x => x.blocked).every(x => x.requestCount === 0))
  assert.ok(result.fixed.every(x => x.line > 1 && !x.route.includes('?') && !x.route.includes('undefined')))
  const plain = result.fixed.filter(x => !x.url.includes('?')), children = result.fixed.filter(x => x.url.includes('?'))
  assert.ok(plain.length > children.length); assert.ok(children.length > 0)
  assert.ok(children.every(x => x.url.endsWith('?parentId=9007199254740995')))
})

/** 阴性样本刻意使用另一条合法形态地址，不能因为MVC里恰有动作就通过原页面合同。 */
test('跨表跨页、操作混淆和长ID数值化在路由核对前拒绝', () => {
  const file = 'pages/yzp/example-detail-42.vue'
  const request = { url: '/admin/auto/example/42/update?parentId=9007199254740995', method: 'POST', sendSessionId: true, data: { id: '9007199254740993' } }
  verifyRequest(request, file, 'update', '9007199254740995')
  for (const url of ['/admin/auto/other/42/update', '/admin/auto/example/43/update', '/admin/auto/example/42/add', '/admin/auto/example/42/update?parentId=9007199254740994', '/admin/auto/example/42/update?parentId=9007199254740995&projectId=8']) assert.throws(() => verifyRequest({ ...request, url }, file, 'update', '9007199254740995'))
  assert.throws(() => verifyRequest({ ...request, data: { id: 9007199254740993 } }, file, 'update', '9007199254740995'))
  assert.throws(() => verifyRequest({ ...request, method: 'GET' }, file, 'update', '9007199254740995'))
})
