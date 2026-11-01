import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'
import { preprocess } from './original-frontend-routes.mjs'

const platforms = { H5: ['H5','WEB','VUE3'], APP: ['APP','APP-PLUS','APP-VUE','VUE3'], WEIXIN: ['MP','MP-WEIXIN','VUE3'] }
const recordId = '9007199254740993', parentId = '9007199254740995'
const key = node => node?.type === 'Identifier' ? node.name : node?.type === 'StringLiteral' ? node.value : null

/** 只提取原对象的data和methods，不执行import、生命周期、顶层语句或编造地址。 */
export function definition(script, parser, context, filename) {
  const ast = parser.parse(script, { sourceType: 'module' })
  const exports = ast.program.body.filter(node => node.type === 'ExportDefaultDeclaration')
  assert.equal(exports.length, 1, '原页必须明确导出一个组件：' + filename)
  const object = exports[0].declaration
  assert.equal(object.type, 'ObjectExpression', '不能猜测动态组件定义：' + filename)
  const selected = ['data','methods'].map(name => {
    const fields = object.properties.filter(node => !node.computed && key(node.key) === name)
    assert.equal(fields.length, 1, '原页data/methods缺失或重复：' + filename)
    const field = fields[0]
    if (name === 'data') assert.ok(field.type === 'ObjectMethod' && field.kind === 'method' || field.type === 'ObjectProperty' && ['FunctionExpression','ArrowFunctionExpression'].includes(field.value?.type), 'data只能是原函数，不执行表达式')
    else {
      assert.equal(field.value?.type, 'ObjectExpression', '不能猜测动态methods对象')
      for (const method of field.value.properties) assert.ok(!method.computed && (method.type === 'ObjectMethod' && method.kind === 'method' || method.type === 'ObjectProperty' && ['FunctionExpression','ArrowFunctionExpression'].includes(method.value?.type)), 'methods只允许静态函数定义，不执行展开、计算键或初始化表达式')
    }
    return script.slice(field.start, field.end)
  })
  const component = vm.runInNewContext('({' + selected.join(',') + '})', context, { filename, timeout: 1000 })
  const methods = object.properties.find(node => key(node.key) === 'methods').value
  assert.equal(methods?.type, 'ObjectExpression', '不能猜测动态methods对象')
  component.methodLines = Object.fromEntries(methods.properties.map(node => [key(node.key), node.loc.start.line]))
  return component
}

/** 原isBlank函数按AST原文装载；不为新增/更新分支另写一套空值规则。 */
function blankFunction(frontend, parser) {
  const source = fs.readFileSync(path.join(frontend, 'common/utils.js'), 'utf8')
  const ast = parser.parse(source, { sourceType: 'module' })
  const declarations = ast.program.body.filter(node => node.type === 'VariableDeclaration').flatMap(node => node.declarations)
  const object = declarations.find(node => node.id?.name === 'utils')?.init
  assert.equal(object?.type, 'ObjectExpression')
  const fields = object.properties.filter(node => !node.computed && key(node.key) === 'isBlank')
  assert.equal(fields.length, 1)
  assert.equal(fields[0].value?.type, 'FunctionExpression')
  return vm.runInNewContext('(' + source.slice(fields[0].value.start, fields[0].value.end) + ')', {}, { timeout: 1000 })
}

/** 驱动原方法的确认/校验边界；request仅捕获，无HTTP、数据库、业务身份或浏览器。 */
async function execute(script, parser, utils, file, action, parent, blocked = false) {
  const requests = [], diagnostics = []
  const context = {
    utils,
    yzp: { request: options => requests.push(options) },
    uni: { showModal: options => options.success?.({ confirm: !blocked, cancel: blocked }) },
    console: { log: (...values) => diagnostics.push(values.map(String).join(' ')) },
    setTimeout: callback => callback()
  }
  const component = definition(script, parser, context, file)
  const instance = { ...component.data(), ...component.methods, $deviceInfo: { is_pc: true }, $refs: { entityForm: { validate: () => blocked ? Promise.reject(new Error('本轮校验失败')) : Promise.resolve({}) } } }
  assert.equal(instance.page_is_function_table, false, '功能表不能冒充普通CRUD：' + file)
  instance.parent_id = parent
  instance.id = action === 'add' ? '' : recordId
  instance.entity = { ...instance.entity, id: instance.id }
  const methods = { detail: 'getEntity', add: 'saveEntity', update: 'saveEntity', delete: 'deleteEntity', copy: 'copyEntity' }
  assert.equal(typeof instance[methods[action]], 'function', '原方法缺失：' + file + ' ' + action)
  instance[methods[action]]()
  await new Promise(resolve => setImmediate(resolve))
  // 原保存方法会把父ID写入实体；允许原规则，只禁止捕获过程改变长主键。
  assert.equal(instance.entity.id, instance.id, '捕获过程不能改变主键')
  if (blocked) {
    assert.equal(requests.length, 0, '取消/校验失败不得发送请求：' + file + ' ' + action)
    if (action === 'add') assert.ok(diagnostics.length > 0, '必须实际进入原校验失败分支')
  } else {
    assert.equal(diagnostics.length, 0, '原方法执行异常：' + diagnostics.join(';'))
    assert.equal(requests.length, 1, '原动作必须捕获且仅捕获一笔请求：' + file + ' ' + action)
    const request = requests[0]
    verifyRequest(request, file, action, parent)
  }
  return { requests, hasCopy: typeof instance.copyEntity === 'function' }
}

/** 请求必须仍属于原文件声明的表／页面与当前动作，不能只因另一条有效路由存在就计通过。 */
export function verifyRequest(request, file, action, parent) {
  const origin = /^pages\/yzp\/([a-z0-9_]+)-detail-([0-9]+)\.vue$/.exec(file)
  assert.ok(origin, '原详情来源文件名必须明确表和页面')
  assert.equal(request.sendSessionId, true, '原请求仍须发送会话')
  assert.equal((request.method || 'GET').toUpperCase(), ({ detail: 'GET', add: 'POST', update: 'POST', delete: 'DELETE', copy: 'POST' })[action])
  assert.equal(typeof request.url, 'string')
  assert.ok(request.url.startsWith('/admin/auto/'))
  const url = new URL(request.url, 'https://offline.invalid')
  const suffix = ({ detail: '/detail/' + recordId, copy: '/copy/' + recordId, add: '/add', update: '/update', delete: '' })[action]
  assert.equal(url.pathname, '/admin/auto/' + origin[1] + '/' + origin[2] + suffix, '原操作不得串表／串页或误用新增更新路由')
  assert.equal(url.searchParams.get('parentId'), parent === parentId ? parentId : null, '原父范围及undefined/null字符串兼容')
  assert.ok([...url.searchParams.keys()].every(key => key === 'parentId'))
  assert.equal(url.hash, '')
  if (action === 'update' || action === 'delete') assert.equal(request.data.id, recordId, '正文长ID必须保持原字符串')
}

/** 三端分别装载全部原详情页，捕获实际动态CRUD与父范围，不以正则替换变量或构造理想路由。 */
export async function inventoryDetails(frontend) {
  const require = createRequire(path.join(frontend, 'package.json'))
  const parser = require('@babel/parser'), sfc = require('@vue/compiler-sfc')
  const directory = path.join(frontend, 'pages/yzp')
  const files = fs.readdirSync(directory, { withFileTypes: true }).filter(item => /-detail-[0-9]+\.vue$/.test(item.name)).map(item => {
    assert.ok(item.isFile() && !item.isSymbolicLink(), '原详情页不接受链接或非文件'); return item.name
  }).sort()
  assert.ok(files.length >= 10, '原详情页扫描范围异常缩小')
  const utils = { isBlank: blankFunction(frontend, parser) }, fixed = [], cases = []
  for (const name of files) for (const [platform, flags] of Object.entries(platforms)) {
    const file = 'pages/yzp/' + name
    assert.equal(fs.lstatSync(path.join(directory, name)).isSymbolicLink(), false)
    const source = preprocess(fs.readFileSync(path.join(directory, name), 'utf8'), new Set(flags))
    const parsed = sfc.parse(source, { filename: file })
    assert.equal(parsed.errors.length, 0)
    assert.ok(parsed.descriptor.script && !parsed.descriptor.scriptSetup, '原详情页脚本形态变化须重新核对')
    const script = parsed.descriptor.script.content
    const component = definition(script, parser, {}, file)
    const hasCopy = typeof component.methods.copyEntity === 'function'
    const actions = ['detail','add','update','delete', ...(hasCopy ? ['copy'] : [])]
    const actionMethods = { detail: 'getEntity', add: 'saveEntity', update: 'saveEntity', delete: 'deleteEntity', copy: 'copyEntity' }
    for (const parent of ['', 'undefined', 'null', parentId]) for (const action of actions) {
      const result = await execute(script, parser, utils, file, action, parent)
      for (const request of result.requests) fixed.push({ file, line: parsed.descriptor.script.loc.start.line - 1 + component.methodLines[actionMethods[action]], method: (request.method || 'GET').toUpperCase(), route: request.url.split(/[?#]/)[0], platform, action, url: request.url })
      cases.push({ file, platform, action, parent, requestCount: result.requests.length })
    }
    for (const action of ['add', 'delete', ...(hasCopy ? ['copy'] : [])]) {
      await execute(script, parser, utils, file, action, parentId, true)
      cases.push({ file, platform, action, blocked: true, requestCount: 0 })
    }
  }
  return { scannedFiles: files.length, fixed, cases, unresolved: [], errors: [] }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.stdout.write(JSON.stringify(await inventoryDetails(path.resolve(process.argv[2] ?? ''))))
}
