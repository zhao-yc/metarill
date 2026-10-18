import test from 'node:test'
import assert from 'node:assert/strict'
import { createSchemaSyncWorkbench } from '../common/schemaSyncWorkbench.js'
const copy=value=>JSON.parse(JSON.stringify(value))
const plan={id:'original',provider:'mysql',database:'metarill_test',beforeFingerprint:'a',desiredFingerprint:'b',changes:[{kind:'AddColumn',table:'test',column:'name',sql:'ALTER TABLE test ADD name varchar(50)'}],future:{keep:true}}
const ok=data=>({code:200,data})
const journal={planId:plan.id,database:plan.database,succeeded:true,appliedStatements:1}
function fixture({confirm=async()=>true,apply=async()=>ok(journal),verify=async()=>ok({...plan,id:'verification',changes:[]}),isCurrent=()=>true}={}){
 const calls=[];let previews=0
 const workbench=createSchemaSyncWorkbench({projectId:'777',tableId:'888',confirm,isCurrent,request:async(url,body)=>{
  calls.push({url,body:copy(body)})
  if(url.endsWith('/apply'))return apply(body)
  return ++previews===1?ok(copy(plan)):verify()
 }})
 return {workbench,calls}
}
test('执行前须预览并确认，完整计划原样传输，执行后只核验一次且不能重复提交',async()=>{
 const {workbench,calls}=fixture({confirm:async shown=>{shown.changes[0].sql='不能污染计划';return true}})
 await workbench.apply();assert.equal(calls.length,0)
 await workbench.preview();workbench.getState().plan.changes[0].sql='展示不能污染计划'
 await Promise.all([workbench.apply(),workbench.apply()])
 assert.deepEqual(calls.map(c=>c.url),['/admin/sys/database/preview','/admin/sys/database/apply','/admin/sys/database/preview'])
 assert.deepEqual(calls[1].body,{projectId:'777',plan})
 assert.deepEqual(calls[0].body,{projectId:'777',tableId:'888'})
 assert.equal(workbench.getState().phase,'complete');assert.deepEqual(workbench.getState().journal,journal)
 await workbench.apply();assert.equal(calls.length,3)
})
test('取消确认不提交，零差异也不能提交',async()=>{
 const {workbench,calls}=fixture({confirm:async()=>false});await workbench.preview();await workbench.apply();assert.equal(calls.length,1);assert.equal(workbench.getState().phase,'ready')
 const empty=createSchemaSyncWorkbench({projectId:'777',request:async()=>ok({...plan,changes:[]}),confirm:()=>assert.fail('零差异无需确认')});await empty.preview();await empty.apply();assert.equal(empty.getState().phase,'ready')
})
test('业务失败保存执行记录并重新预览，剩余差异不能自动重试',async()=>{
 const failed={...journal,succeeded:false,appliedStatements:1,error:'执行失败'}
 const {workbench,calls}=fixture({apply:async()=>({code:500,msg:'部分DDL失败',map:{result:failed}}),verify:async()=>ok({...plan,id:'remaining'})})
 await workbench.preview();await workbench.apply();await workbench.apply()
 assert.equal(calls.length,3);assert.equal(workbench.getState().phase,'error');assert.deepEqual(workbench.getState().journal,failed);assert.match(workbench.getState().error,/不会自动重试/)
})
test('网络结果未知同样再预览一次，核验失败也不重试执行',async()=>{
 const {workbench,calls}=fixture({apply:async()=>{throw new Error('网络断开')},verify:async()=>{throw new Error('读取失败')}})
 await workbench.preview();await workbench.apply();assert.equal(calls.length,3);assert.equal(workbench.getState().phase,'error');assert.match(workbench.getState().error,/核验失败/)
})
test('登录/项目变化与离开页面后不再确认执行或提交迟到响应',async()=>{
 let current=true,release;const {workbench,calls}=fixture({isCurrent:()=>current,confirm:()=>new Promise(resolve=>{release=resolve})})
 await workbench.preview();const applying=workbench.apply();current=false;release(true);await applying;assert.equal(calls.length,1)
 const other=fixture();await other.workbench.preview();other.workbench.dispose();await other.workbench.apply();assert.equal(other.calls.length,1)
})
test('执行中的预览/重复点击被隔离，核验有差异必须显式重新预览',async()=>{
 let finish;const {workbench,calls}=fixture({apply:()=>new Promise(resolve=>{finish=resolve}),verify:async()=>ok(plan)})
 await workbench.preview();const applying=workbench.apply();await Promise.resolve();await workbench.preview();await workbench.apply();finish(ok(journal));await applying
 assert.equal(calls.length,3);assert.equal(workbench.getState().phase,'error');await workbench.apply();assert.equal(calls.length,3)
})
