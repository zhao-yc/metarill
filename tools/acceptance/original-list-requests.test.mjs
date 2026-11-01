import test from 'node:test'
import assert from 'node:assert/strict'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {createRequire} from 'node:module'
import {runtimeFactory,mixinOptions,inventoryLists,verifyListRequest} from './original-list-requests.mjs'
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..'),front=path.join(root,'frontend/metarill_uni_admin'),require=createRequire(path.join(front,'package.json')),parser=require('@babel/parser')

/** 顶层调用和未审查的列表初始化均先拒绝，不借捕获工具执行外部代码。 */
test('共享列表只装载函数声明，工厂配置不执行初始化表达式',()=>{
 let effects=0
 assert.throws(()=>runtimeFactory('danger();export function createListPageMixin(){}',parser,{danger:()=>effects++}))
 for(const mixin of ['other({})','createListPageMixin(danger())','createListPageMixin({...danger()})','createListPageMixin({url:danger(),request(x){}})','createListPageMixin({url:"/a",request(x){},unknown:1})','createListPageMixin({url:"/a",url:"/b",request(x){}})'])assert.throws(()=>mixinOptions("import {createListPageMixin} from '@/common/yzp_runtime/listPageRuntime';export default {mixins:["+mixin+']}',parser))
 for(const source of ["export default {mixins:[createListPageMixin({url:'/a',request(x){}})]}","import {createListPageMixin} from 'other';export default {mixins:[createListPageMixin({url:'/a',request(x){}})]}"])assert.throws(()=>mixinOptions(source,parser))
 assert.equal(effects,0)
 const factory=runtimeFactory('function helper(){return 3}export function createListPageMixin(){return helper()}',parser,{})
 assert.equal(factory(),3)
})
/** 实际原页与原共享状态机覆盖三端/两视口，工作台包装明确保留未覆盖。 */
test('十九原标准列表执行真实分页、回调、空父及保存刷新方法',()=>{
 const v=inventoryLists(front);assert.equal(v.scannedFiles,20);assert.equal(v.standardFiles,19);assert.equal(v.legacyFiles,16);assert.equal(v.mixinFiles,3);assert.deepEqual(v.errors,[])
 assert.equal(new Set(v.fixed.map(x=>x.route)).size,19);assert.deepEqual([...new Set(v.fixed.map(x=>x.platform))].sort(),['APP','H5','WEIXIN'])
 for(const action of ['page','pagination','refresh','filter','reset','update','append','child-empty','error-response'])assert.ok(v.cases.some(x=>x.action===action),action)
 assert.ok(v.cases.filter(x=>['reset','update','child-empty'].includes(x.action)).every(x=>x.requestCount===0))
 assert.equal(v.unresolved.length,3);assert.ok(v.unresolved.every(x=>x.file.endsWith('config_menu-list-2200000000000099000.vue')))
 assert.ok(v.fixed.every(x=>x.line>1&&x.method==='POST'&&x.payload.pageSize>0));assert.ok(v.fixed.some(x=>x.payload.parentId==='9007199254740995'))
})
/** 合法形态的另一个表/页也必须拒绝，且正文不接受身份或长ID数值化。 */
test('请求捕获拒绝串表串页、错误方法、额外身份和父ID数值化',()=>{
 const file='pages/yzp/example-list-42.vue',parent='9007199254740995',request={url:'/admin/auto/example/42/page',method:'POST',sendSessionId:true,data:{orderBy:{},filters:{},pageNo:1,pageSize:15,parentId:parent}}
 verifyListRequest(request,file,parent,1)
 for(const change of [{url:'/admin/auto/other/42/page'},{url:'/admin/auto/example/43/page'},{method:'GET'},{sendSessionId:false},{data:{...request.data,projectId:'1'}},{data:{...request.data,parentId:9007199254740995}},{data:{...request.data,pageNo:2}}])assert.throws(()=>verifyListRequest({...request,...change},file,parent,1))
})
