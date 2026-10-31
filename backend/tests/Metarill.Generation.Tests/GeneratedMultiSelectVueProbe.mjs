import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_source-detail-201.vue'),'utf8'),longId='9007199254740993',parentId='9007199254742001'
/** 实际生成原详情并挂载；目标组件只提供原props/events，真实Service范围由MySQL回归验证。 */
function fixture(){const f=mountGenerated(source,{}, {id:'9007199254748888',parent_id:parentId});f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),{id:'9007199254748888',name:'源记录',targetIds:[longId],targetNames:['历史名称']});f.state.changes={};return{...f,async open(){f.state.openSelect25();await f.flush();return f.targets.at(-1)}}}
{
 const f=fixture();try{
 assert.equal(f.state.readonlyDisplayText(['甲',null,'乙']),'甲、乙');assert.equal(f.state.readonlyDisplayText('["历史甲","历史乙"]'),'历史甲、历史乙');assert.equal(f.state.multiSelectionText({targetIds:[longId],targetNames:null},'targetIds'),longId)
 const target=await f.open();assert.deepEqual(plain(target.selectedIds),[longId]);assert.deepEqual(plain(target.selectedNames),['历史名称']);assert.equal(target.selectedNameField,'name');assert.equal(target.parent_id,parentId);target.cancel();await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[longId]);assert.deepEqual(plain(f.state.entity.targetNames),['历史名称']);assert.deepEqual(plain(f.state.changes),{})
 const selected=await f.open();selected.confirm([{id:longId,name:'服务端当前名称',ignored:'不写任意字段'},{id:'2',name:null}]);await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[longId,'2']);assert.deepEqual(plain(f.state.entity.targetNames),['服务端当前名称',null]);assert.deepEqual(Object.keys(f.state.changes),['targetIds']);assert.equal(f.state.entity.ignored,undefined);assert.equal(f.state.multiSelectionField,'')
 f.state.changes={};(await f.open()).confirm([{id:longId,name:'同ID实时名称'},{id:'2',name:null}]);await f.flush();assert.deepEqual(plain(f.state.changes.targetIds),[longId,'2']);assert.equal(f.state.changes.targetNames,undefined);(await f.open()).confirm([]);await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[]);assert.deepEqual(plain(f.state.entity.targetNames),[]);assert.equal(f.writes().length,0)
 }finally{f.app.unmount()}
 console.log('通过：原弹层props/events、确认关闭、取消不写、完整row[]/长ID、名称仅回显、同IDfresh与空选')
}
{
 const f=fixture();try{
 const target=await f.open();f.setProps({parentReadOnlyCondition:true});await f.flush();assert.equal(target.parentReadOnlyCondition,true);target.confirm([{id:'2',name:'只读回执'}]);await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[longId]);assert.deepEqual(plain(f.state.changes),{});f.setProps({parentReadOnlyCondition:false});await f.flush()
 const late=await f.open();f.storage.sysParams={yzProjectId:'另一项目'};late.confirm([{id:'2',name:'旧项目回执'}]);await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[longId]);f.storage.sysParams={yzProjectId:'100'}
 const duplicate=await f.open();f.state.onPopSelect25([{id:'2',name:'重复甲'},{id:'2',name:'重复乙'}]);await f.flush();assert.deepEqual(plain(f.state.changes),{});assert.match(f.state.multiSelectionError,/不重复/);duplicate.cancel();await f.flush()
 await f.open();f.state.onPopSelect25([{id:'2'}]);await f.flush();assert.deepEqual(plain(f.state.changes),{});assert.match(f.state.multiSelectionError,/完整/);f.state.onPopClose25();const old=await f.open();f.app.unmount();old.confirm([{id:'2',name:'卸载回执'}]);assert.deepEqual(plain(f.state.entity.targetIds),[longId]);assert.equal(f.subscriptions.size,0)
 }finally{if(f.state._alive)f.app.unmount()}
 console.log('通过：原父只读、项目切换/卸载迟到、重复和不完整回执均不写')
}
