import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_source-detail-201.vue'),'utf8'),scenario=process.argv[3],id='9007199254740993',parent='9007199254742000',targetParent='9007199254743000'
const f=mountGenerated(source,{parentReadOnlyCondition:scenario==='independentReadonly'}, {id,parent_id:parent})
try{
 f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/')),{id,name:'来源记录',targetId:id,parentId:targetParent});await f.flush()
 const candidates=()=>f.requests.filter(entry=>entry.options.url.endsWith('/findNameIdTarget25'))
 if(scenario==='unresolved'){assert.equal(candidates().length,0);assert(f.dialogs.some(dialog=>dialog.content.includes('关联目标父范围尚未明确')));assert.deepEqual(plain(f.state.target_list),[])}
 else{
 const candidate=candidates().at(-1);assert(candidate);assert.equal(candidate.options.method,'POST');assert.equal(candidate.options.data.parentId,parent);assert.equal(candidate.options.data.targetParentId,scenario.startsWith('independent')?targetParent:parent);assert.equal(typeof candidate.options.data.formParameters,'object');assert.equal(candidate.options.data.id,scenario==='readonly'||scenario==='independentReadonly'?id:undefined)
 f.respond(candidate,[{id,name:'原候选名称',value:id,text:'原候选名称'}]);assert.equal(f.state.target_list[0].name,'原候选名称');f.state.targetIdDetailChange({id:'9007199254744000',name:'选择新候选'});assert.equal(f.state.entity.targetId,scenario==='readonly'||scenario==='independentReadonly'?id:'9007199254744000')
 f.state.loadOriginalOptions25();const reordered=candidates().at(-1);f.state.loadOriginalOptions25();const newest=candidates().at(-1);f.respond(newest,[{id,name:'最新候选'}]);f.respond(reordered,[{id,name:'乱序旧候选'}]);assert.equal(f.state.target_list[0].name,'最新候选');f.state.loadOriginalOptions25();const pending=candidates().at(-1);if(scenario.startsWith('independent')){
 const previousId=f.state.entity.targetId;f.state.entity.parentId='9007199254743001';assert.deepEqual(plain(f.state.target_list),[]);assert.equal(f.state.entity.targetId,previousId);f.respond(pending,[{id,name:'另一父范围迟到'}]);assert.deepEqual(plain(f.state.target_list),[]);await f.flush();const failed=candidates().at(-1);assert.notEqual(failed,pending);assert.equal(failed.options.data.targetParentId,'9007199254743001');failed.options.fail?.({message:'新父候选失败'});failed.options.complete?.();assert.deepEqual(plain(f.state.target_list),[]);f.respond(pending,[{id,name:'失败后旧父回执'}]);assert.deepEqual(plain(f.state.target_list),[])
 f.state.entity.parentId='';assert.deepEqual(plain(f.state.target_list),[]);const count=candidates().length;await f.flush();assert.equal(candidates().length,count);assert.equal(f.state.entity.targetId,previousId);f.state.entity.parentId='9007199254743002';await f.flush();assert.equal(candidates().at(-1).options.data.targetParentId,'9007199254743002')
 }else{f.state.parent_id='9007199254742001';f.respond(pending,[{id,name:'另一父范围迟到'}]);assert.notEqual(f.state.target_list[0]?.name,'另一父范围迟到')}
 f.state.loadOriginalOptions25();const identity=candidates().at(-1);f.storage.sysParams={yzProjectId:'另一个项目'};f.respond(identity,[{id,name:'旧项目迟到'}]);assert.notEqual(f.state.target_list[0]?.name,'旧项目迟到')
 }
}finally{f.app.unmount()}
console.log('通过：原zxz和findNameId/formParameters协议、真实固定两父参数、未知映射拒绝与迟到回填隔离：'+scenario)
