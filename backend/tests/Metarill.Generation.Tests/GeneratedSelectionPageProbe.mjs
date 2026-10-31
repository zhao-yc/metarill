import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_target-list-301.vue'),'utf8')
const ids=['9007199254740993','9007199254740994'],parent='9007199254742000'
const reads=f=>f.requests.filter(entry=>entry.options.url.endsWith('/selection/selected')),lastPage=f=>f.pages().at(-1)
const finish=(f,entry,records,total=records.length,pages=1)=>f.respond(entry,{records,total,pages})
function fixture(props={}){return mountGenerated(source,{parent_id:parent,selectedIds:ids,selectedNames:['过期甲','过期乙'],...props})}

{
 const f=fixture({selectedIds:[ids[0],ids[0],ids[1]]});try{
  assert.deepEqual(plain(reads(f)[0].options.data.selectedIds),ids)
  f.respond(reads(f)[0],[{id:ids[1],name:'最新乙'},{id:ids[0],name:'最新甲'}]);finish(f,lastPage(f),[{id:'9007199254740995',name:'第三项'}],21,2);await f.flush()
  assert.deepEqual(plain(f.state.selectedList.map(row=>row.name)),['最新甲','最新乙'])
  f.state.chkChange(0);await f.flush();assert.equal(f.state.selectedList.length,3);assert.deepEqual(plain(f.state.selectedIds),[ids[0],ids[0],ids[1]])
  f.state.paginationChange({current:2});finish(f,lastPage(f),[{id:ids[0],name:'翻页已选'}],21,2);await f.flush();assert.equal(f.state.list[0].isSelect,true)
  f.state.toggleSelectedOnly();assert.deepEqual(plain(lastPage(f).options.data.selectedIds),[...ids,'9007199254740995']);finish(f,lastPage(f),[{id:ids[0],name:'最新甲'},{id:ids[1],name:'最新乙'},{id:'9007199254740995',name:'第三项'}]);await f.flush()
  const before=f.pages().length;f.state.selectAll();await f.flush();assert.equal(f.state.selectedList.length,0);assert.equal(f.state.list.length,0);assert.equal(f.pages().length,before)
  f.state.sureSelect();assert.deepEqual(plain(reads(f).at(-1).options.data.selectedIds),[]);f.respond(reads(f).at(-1),[]);await f.flush();assert.deepEqual(plain(f.events.find(event=>event[0]==='onPopSelect')[1]),[])
 }finally{f.app.unmount()}
 console.log('通过：原跨页草稿、稳定长ID顺序、原row[]确认、空已选不退全量')
}
{
 const f=fixture();try{
  const initial=reads(f)[0];f.setProps({parent_id:'9007199254742001'});await f.flush();assert.equal(initial.aborted,true)
  f.respond(initial,[{id:ids[0],name:'旧父甲'},{id:ids[1],name:'旧父乙'}]);await f.flush();assert.equal(f.state.selectedRows.length,0)
  const current=reads(f).at(-1);assert.equal(current.options.data.parentId,'9007199254742001');f.respond(current,[{id:ids[0],name:'当前甲'},{id:ids[1],name:'当前乙'}]);await f.flush()
  f.state.sureSelect();const confirm=reads(f).at(-1);f.state.sureClose();f.respond(confirm,[{id:ids[0],name:'取消后甲'},{id:ids[1],name:'取消后乙'}]);await f.flush();assert.equal(f.events.some(event=>event[0]==='onPopSelect'),false)
 }finally{f.app.unmount()}
 console.log('通过：真实父props切换、确认后取消和迟到回执不写回父关系')
}
{
 const f=fixture();try{
  f.respond(reads(f)[0],[{id:ids[0],name:'甲'},{id:ids[1],name:'乙'}]);await f.flush();f.state.sureSelect();const confirm=reads(f).at(-1)
  f.respond(confirm,[{id:ids[0],name:'只返回一项'}]);await f.flush();assert.equal(f.events.some(event=>event[0]==='onPopSelect'),false);assert.equal(f.state.selectedRows.length,0);assert.ok(f.dialogs.length>0)
  f.state.sureSelect();assert.notEqual(reads(f).at(-1),confirm);f.respond(reads(f).at(-1),[{id:ids[0],name:'重试甲'},{id:ids[1],name:'重试乙'}]);await f.flush();assert.deepEqual(plain(f.events.find(event=>event[0]==='onPopSelect')[1].map(row=>row.name)),['重试甲','重试乙'])
 }finally{f.app.unmount()}
 console.log('通过：缺失已选整批拒绝，原确认按钮可重新同权回读')
}
for(const invalid of ['[','{}','[0]','["9223372036854775808"]',[Number.MAX_SAFE_INTEGER+1]]){
 const f=fixture({selectedIds:invalid});try{assert.equal(reads(f).length,0);f.state.sureSelect();await f.flush();assert.equal(reads(f).length,0);assert.equal(f.events.some(event=>event[0]==='onPopSelect'),false)}finally{f.app.unmount()}
}
{
 const missing=fixture({parent_id:''});try{assert.equal(missing.pages().length,0);assert.equal(reads(missing).length,0)}finally{missing.app.unmount()}
 const f=fixture();try{const old=reads(f)[0];f.storage.loginStateId='另一会话';f.respond(old,[{id:ids[0],name:'旧会话甲'},{id:ids[1],name:'旧会话乙'}]);await f.flush();assert.equal(f.state.selectedRows.length,0)}finally{f.app.unmount()}
}
console.log('通过：非法原值不空确认、空父不查询、身份先变化时旧回执失效')
