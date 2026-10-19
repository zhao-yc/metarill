import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_source-list-201.vue'),'utf8'),scenario=process.argv[3],parent='9007199254742000',target='9007199254743000'
assert(source.includes("<zxz-uni-data-select v-model='filters.targetId'"));assert(!source.includes('<yzp-relation-select'))
const f=mountGenerated(source,{parent_id:parent,is_son_page:true})
try{
 const candidates=()=>f.requests.filter(entry=>entry.options.url.endsWith('/findNameIdTarget25'))
 await f.flush()
 if(scenario==='unresolved'){assert.equal(candidates().length,0);assert.deepEqual(plain(f.state.target_list),[])}
 else if(scenario==='same'){
  const request=candidates().at(-1);assert(request);assert.equal(request.options.method,'POST');assert.equal(request.options.data.parentId,parent);assert.equal(request.options.data.targetParentId,parent)
  f.respond(request,[{id:'9007199254745000',name:'同父候选'}]);assert.equal(f.state.target_list[0].name,'同父候选')
 }else{
  assert.equal(candidates().length,0,'空独立目标父不能请求全量候选');f.state.filters.parentId=target;const first=candidates().at(-1);assert(first);assert.equal(first.options.data.parentId,parent);assert.equal(first.options.data.targetParentId,target)
  f.respond(first,[{id:'9007199254745000',name:'甲候选'}]);assert.equal(f.state.target_list[0].name,'甲候选');f.state.filters.targetId='9007199254745000'
  f.state.initLists();const old=candidates().at(-1);f.state.filters.parentId='9007199254743001';const current=candidates().at(-1);assert.notEqual(current,old);assert.deepEqual(plain(f.state.target_list),[]);assert.equal(f.state.filters.targetId,'9007199254745000');assert.equal(current.options.data.parentId,parent);assert.equal(current.options.data.targetParentId,'9007199254743001')
  f.respond(old,[{id:'9007199254745000',name:'上一父迟到'}]);assert.deepEqual(plain(f.state.target_list),[]);f.respond(current,[{id:'9007199254745001',name:'乙候选'}]);assert.equal(f.state.target_list[0].name,'乙候选')
  const count=candidates().length;f.state.filters.parentId='';assert.deepEqual(plain(f.state.target_list),[]);await f.flush();assert.equal(candidates().length,count);f.respond(current,[{id:'9007199254745001',name:'清空后旧值'}]);assert.deepEqual(plain(f.state.target_list),[])
  f.state.filters.parentId='9223372036854775808';await f.flush();assert.equal(candidates().length,count);f.state.filters.parentId=target;const identity=candidates().at(-1);f.storage.sysParams={yzProjectId:'其他项目'};f.respond(identity,[{id:'9007199254745000',name:'上一项目'}]);assert.deepEqual(plain(f.state.target_list),[])
 }

 // 真实生成原页的固定候选与列表在logout同步清理，组件保留供onLogin重新加载。
 f.state.refreshData();f.respond(f.pages().at(-1),{records:[{id:'9007199254745000',name:'退出前行'}],pages:1,total:1})
 assert.equal(f.state.list.length,1)
 const beforeLogout=f.requests.length
 f.storage.loginStateId='';f.storage.sysParams={}
 for(const handler of [...f.subscriptions.get('onLogout')||[]])handler()
 assert.equal(f.requests.length,beforeLogout);assert.deepEqual(plain(f.state.list),[]);assert.deepEqual(plain(f.state.target_list),[])
 await f.flush();assert.equal(f.requests.length,beforeLogout)
 f.storage.loginStateId='新登录';f.storage.sysParams={yzProjectId:'100'}
 for(const handler of [...f.subscriptions.get('onLogin')||[]])handler()
 assert(f.requests.length>beforeLogout)
 f.respond(f.pages().at(-1),{records:[{id:'9007199254745001',name:'再次登录行'}],pages:1,total:1});assert.equal(f.state.list[0].name,'再次登录行')
}finally{f.app.unmount()}
console.log('通过：原列表数组候选与固定双父筛选、换父清理、空父/溢出拒绝和迟到隔离：'+scenario)
