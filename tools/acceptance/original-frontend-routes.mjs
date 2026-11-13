import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'

/** 只解释字面值和拼接前缀；动态路径保留为未解析，不猜测运行时表名或方法。 */
export function prefix(node) {
  if (node?.type === 'StringLiteral') return { text: node.value, complete: true }
  if (node?.type === 'TemplateLiteral') return { text: node.quasis[0].value.cooked ?? '', complete: node.expressions.length === 0 }
  if (node?.type === 'BinaryExpression' && node.operator === '+') {
    const left = prefix(node.left)
    if (!left || !left.complete) return left
    const right = prefix(node.right)
    return right ? { text: left.text + right.text, complete: right.complete } : { ...left, complete: false }
  }
  return null
}
/** 仅解释uni-app既有条件指令；保留原行数，对Web/App/微信分别取脚本，不混合互斥代码。 */
export function preprocess(source, flags) {
  let active = true
  const stack = []
  const output = source.split('\n').map(line => {
    const directive = line.match(/^\s*(?:\/\/|<!--)\s*#(ifdef|ifndef|endif|else)\b(.*)$/)
    if (!directive) return active ? line : ''
    const kind = directive[1], expression = directive[2].replace(/\s*-->\s*$/, '').trim()
    if (kind === 'ifdef' || kind === 'ifndef') {
      const names = expression.split(/\s*\|\|\s*/)
      if (!names.length || names.some(name => !/^[A-Z][A-Z0-9_-]*$/.test(name))) throw new Error('条件指令无法固定解释')
      const condition = names.some(name => flags.has(name))
      stack.push({ parent: active, selected: kind === 'ifdef' ? condition : !condition, alternate: false })
      active = active && stack.at(-1).selected
    } else if (kind === 'else') {
      const frame = stack.at(-1)
      if (!frame || frame.alternate || expression) throw new Error('条件分支无效')
      frame.alternate = true; active = frame.parent && !frame.selected
    } else {
      if (!stack.length || expression) throw new Error('条件结束无效')
      active = stack.pop().parent
    }
    return ''
  })
  if (stack.length) throw new Error('条件指令未闭合')
  return output.join('\n')
}
const key = node => node?.type === 'Identifier' ? node.name : node?.type === 'StringLiteral' ? node.value : null
/** AST只收集实际request调用参数，注释、字符串内代码及页面导航不充当HTTP证据。 */
export function collect(ast, file, offset = 0) {
  const fixed = [], unresolved = []
  function visit(node) {
    if (!node || typeof node !== 'object') return
    if ((node.type === 'CallExpression' || node.type === 'OptionalCallExpression') &&
        ['MemberExpression', 'OptionalMemberExpression'].includes(node.callee?.type) &&
        key(node.callee.property) === 'request') {
      const options = node.arguments[0], line = (node.loc?.start.line ?? 0) + offset
      if (options?.type !== 'ObjectExpression') unresolved.push({ file, line, reason: '请求参数不是固定对象' })
      else {
        const fields = options.properties.filter(p => p.type === 'ObjectProperty')
        const urls = fields.filter(p => key(p.key) === 'url'), methods = fields.filter(p => key(p.key) === 'method')
        const url = urls.length === 1 ? prefix(urls[0].value) : null
        // 外部URL和非管理路径不进入平台API范围；无法解析的URL必须单列。
        if (url && !url.text.startsWith('/admin/')) { /* 非本平台管理API */ }
        else if (!url || (!url.complete && !url.text.includes('?')) || urls.length !== 1 ||
                 methods.length > 1 || options.properties.some(p => p.type === 'SpreadElement')) {
          unresolved.push({ file, line, reason: '动态路径或对象覆盖，未猜测地址' })
        } else {
          const method = methods.length === 0 ? 'GET' :
            methods[0].value.type === 'StringLiteral' ? (methods[0].value.value || 'GET').toUpperCase() : null
          if (!method || !['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].includes(method))
            unresolved.push({ file, line, reason: 'HTTP方法不是固定值' })
          else fixed.push({ file, line, method, route: url.text.split(/[?#]/)[0] })
        }
      }
    }
    for (const [name, value] of Object.entries(node)) {
      if (['loc', 'start', 'end', 'extra', 'comments', 'tokens'].includes(name)) continue
      if (Array.isArray(value)) value.forEach(visit)
      else if (value && typeof value === 'object') visit(value)
    }
  }
  visit(ast)
  return { fixed, unresolved }
}

/** 使用仓库锁文件已带的Vue/Babel解析器，扫描原页面、共享逻辑及组件，不执行其代码。 */
export function inventory(frontend) {
  const require = createRequire(path.join(frontend, 'package.json'))
  const babel = require('@babel/parser'), sfc = require('@vue/compiler-sfc')
  const files = []
  function scan(directory) {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
      const current = path.join(directory, item.name)
      if (item.isSymbolicLink()) throw new Error('扫描范围不接受符号链接')
      if (item.isDirectory()) scan(current)
      else if (/\.(vue|js|mjs|ts)$/.test(item.name)) files.push(current)
    }
  }
  for (const directory of ['pages', 'common', 'components']) scan(path.join(frontend, directory))
  const fixed = [], unresolved = [], errors = []
  const platforms = { H5: ['H5','WEB','VUE3'], APP: ['APP','APP-PLUS','APP-VUE','VUE3'], WEIXIN: ['MP','MP-WEIXIN','VUE3'] }
  for (const file of files.sort()) for (const [platform, flags] of Object.entries(platforms)) {
    const relative = path.relative(frontend, file).replaceAll('\\', '/')
    try {
      const source = preprocess(fs.readFileSync(file, 'utf8'), new Set(flags))
      let blocks = [{ content: source, offset: 0 }]
      if (file.endsWith('.vue')) {
        const parsed = sfc.parse(source, { filename: file })
        if (parsed.errors.length) throw new Error('Vue脚本解析失败')
        blocks = [parsed.descriptor.script, parsed.descriptor.scriptSetup].filter(Boolean)
          .map(block => ({ content: block.content, offset: block.loc.start.line - 1 }))
      }
      for (const block of blocks) {
        const parsed = babel.parse(block.content, { sourceType: 'unambiguous', plugins: ['typescript'], allowReturnOutsideFunction: true })
        const found = collect(parsed, relative, block.offset)
        fixed.push(...found.fixed.map(item => ({ ...item, platform })))
        unresolved.push(...found.unresolved.map(item => ({ ...item, platform })))
      }
    } catch { errors.push({ file: relative, platform, reason: '源码解析失败，未计覆盖' }) }
  }
  return { scannedFiles: files.length, fixed, unresolved, errors }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const frontend = path.resolve(process.argv[2] ?? '')
  process.stdout.write(JSON.stringify(inventory(frontend)))
}
