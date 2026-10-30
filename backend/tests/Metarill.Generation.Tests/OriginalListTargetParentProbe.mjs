import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_source-list-201.vue'),'utf8'),scenario=process.argv[3],parent='9007199254742000',target='9007199254743000',value='9007199254745000'
assert(source.includes("<zxz-uni-data-select v-model='filters.targetId'"));assert(!source.includes('<yzp-relation-select'))
const f=mountGenerated(source,{parent_id:parent,is_son_page:true}),endpoint='/findNameIdTarget25'
const row=(id,name)=>({id,value:id,name,text:name})
try{
 const candidates=()=>f.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/page'))
 const selected=()=>f.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/selected'))
 await f.flush();assert.equal(candidates().length,0)
 if(scenario==='unresolved'){assert.equal(selected().length,0);assert.deepEqual(plain(f.state.target_list),[])}
 else if(scenario==='same'){
  f.state.openOriginalRemoteOptions('25');const request=candidates().at(-1);assert(request);assert.equal(request.options.method,'POST');assert.equal(request.options.data.parentId,parent);assert.equal(request.options.data.targetParentId,parent)
  f.respond(request,{records:[row(value,'同父候选')],pages:'1'});assert.equal(f.state.target_list[0].name,'同父候选')
 }else{
  f.state.openOriginalRemoteOptions('25');assert.equal(candidates().length,0,'空独立目标父不能请求全量候选')
  f.state.filters.parentId=target;f.state.openOriginalRemoteOptions('25');const first=candidates().at(-1);assert(first);assert.equal(first.options.data.parentId,parent);assert.equal(first.options.data.targetParentId,target)
  f.respond(first,{records:[row(value,'甲候选')],pages:'2'});assert.equal(f.state.target_list[0].name,'甲候选');f.state.filters.targetId=value
  f.state.moreOriginalRemoteOptions('25');const old=candidates().at(-1);f.state.filters.parentId='9007199254743001';assert.deepEqual(plain(f.state.target_list),[]);assert.equal(f.state.filters.targetId,value)
  const nextSelected=selected().at(-1);assert.equal(nextSelected.options.data.parentId,parent);assert.equal(nextSelected.options.data.targetParentId,'9007199254743001')
  f.state.openOriginalRemoteOptions('25');const current=candidates().at(-1);assert.notEqual(current,old)
  f.respond(old,{records:[row(value,'上一父迟到')],pages:'2'});assert.deepEqual(plain(f.state.target_list),[]);f.respond(current,{records:[row('9007199254745001','乙候选')],pages:'1'});assert.equal(f.state.target_list[0].name,'乙候选')
  const count=f.requests.length;f.state.filters.parentId='';assert.deepEqual(plain(f.state.target_list),[]);await f.flush();f.state.openOriginalRemoteOptions('25');assert.equal(f.requests.length,count)
  f.respond(current,{records:[row(value,'清空后旧值')],pages:'1'});assert.deepEqual(plain(f.state.target_list),[])
  f.state.filters.parentId='9223372036854775808';await f.flush();f.state.openOriginalRemoteOptions('25');assert.equal(f.requests.length,count)
  f.state.filters.parentId=target;f.state.openOriginalRemoteOptions('25');const identity=candidates().at(-1);f.storage.sysParams={yzProjectId:'其他项目'};f.respond(identity,{records:[row(value,'上一项目')],pages:'1'});assert.deepEqual(plain(f.state.target_list),[])
 }
 assert.equal(f.requests.filter(entry=>entry.options.url.endsWith(endpoint)).length,0)
 // 普通业务列表与关系候选分别核验，不能把关系/page当作业务/page回执。
 const business=()=>f.requests.filter(entry=>entry.options.url==='/admin/auto/relation_source/201/page')
 f.state.refreshData();f.respond(business().at(-1),{records:[{id:value,name:'退出前行'}],pages:1,total:1});assert.equal(f.state.list.length,1)
 const beforeLogout=f.requests.length;f.storage.loginStateId='';f.storage.sysParams={}
 for(const handler of [...f.subscriptions.get('onLogout')||[]])handler()
 assert.equal(f.requests.length,beforeLogout);assert.deepEqual(plain(f.state.list),[]);assert.deepEqual(plain(f.state.target_list),[])
 await f.flush();assert.equal(f.requests.length,beforeLogout)
 f.storage.loginStateId='新登录';f.storage.sysParams={yzProjectId:'100'}
 for(const handler of [...f.subscriptions.get('onLogin')||[]])handler()
 assert(f.requests.length>beforeLogout);f.respond(business().at(-1),{records:[{id:value,name:'再次登录行'}],pages:1,total:1});assert.equal(f.state.list[0].name,'再次登录行')
}finally{f.app.unmount()}
console.log('通过：原列表分页候选/已选回读与固定双父、空父/溢出拒绝、退出隔离：'+scenario)
