import test from 'node:test'
import assert from 'node:assert/strict'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { collect, preprocess } from './original-frontend-routes.mjs'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
const require = createRequire(path.join(root, 'frontend/metarill_uni_admin/package.json'))
const parse = source => require('@babel/parser').parse(source, { sourceType: 'module' })
test('原默认GET、明确POST、查询拼接及模板查询保持固定路径', () => {
  const source = "yzp.request({url:'/admin/a?x='+id});yzp.request({url:'/admin/b',method:'POST'});uni.request({url:\`/admin/c?x=\${value}\`});"
  const result = collect(parse(source), '原页.vue', 30)
  assert.deepEqual(result.fixed.map(x => [x.method,x.route]), [['GET','/admin/a'],['POST','/admin/b'],['GET','/admin/c']])
  assert.ok(result.fixed.every(x => x.line === 31)); assert.equal(result.unresolved.length,0)
})
test('动态路径、方法、对象覆盖和重复字段不猜测', () => {
  const source = "yzp.request({url:'/admin/'+table});yzp.request({url:'/admin/a',method:verb});yzp.request({url:'/admin/a',...more});yzp.request(options);yzp.request({url:'/admin/a',url:'/admin/b'});"
  const result = collect(parse(source), '原页.js')
  assert.equal(result.fixed.length,0);assert.equal(result.unresolved.length,5)
})
test('注释、字符串、导航与外站不充当平台HTTP证据', () => {
  const source = "// yzp.request({url:'/admin/fake'});\n const sample=\"yzp.request({url:'/admin/fake'})\";uni.navigateTo({url:'/pages/main'});yzp.request({url:'https://outside.invalid/admin/a'});"
  assert.deepEqual(collect(parse(source),'原页.js'),{fixed:[],unresolved:[]})
})
test('条件编译保持行号，互斥Web/App脚本分别解析', () => {
  const source = "// #ifdef H5\nconst a=1;yzp.request({url:'/admin/web'});\n// #endif\n// #ifndef H5\nconst a=2;yzp.request({url:'/admin/app'});\n// #endif"
  for (const [flags,route,line] of [[new Set(['H5']),'/admin/web',2],[new Set(['APP']),'/admin/app',5]]) {
    const result=collect(parse(preprocess(source,flags)),'原页.js')
    assert.equal(result.fixed[0].route,route);assert.equal(result.fixed[0].line,line)
  }
})
test('嵌套、或条件和else按原分支处理，破损指令明确拒绝', () => {
  const source="// #ifdef H5 || APP-PLUS\n// #ifndef MP\nconst a=1;\n// #else\nconst a=2;\n// #endif\n// #endif"
  assert.match(preprocess(source,new Set(['APP-PLUS'])),/const a=1/);assert.doesNotMatch(preprocess(source,new Set(['APP-PLUS'])),/const a=2/)
  for (const broken of ['// #endif','// #ifdef H5','// #ifdef H5 && APP','// #else'])assert.throws(()=>preprocess(broken,new Set()))
})
