import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_source-detail-201.vue'),'utf8'),scenario=process.argv[3],id='9007199254740993',parent='9007199254742000',targetParent='9007199254743000'
const f=mountGenerated(source,{}, {id,parent_id:parent})
try{
 f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/')),{id,name:'来源记录',targetId:id,parentId:targetParent});await f.flush()
 const candidates=()=>f.requests.filter(entry=>entry.options.url.endsWith('/findNameIdTarget25'))
 if(scenario==='unresolved'){assert.equal(candidates().length,0);assert(f.dialogs.some(dialog=>dialog.content.includes('关联目标父范围尚未明确')));assert.deepEqual(plain(f.state.target_list),[])}
 else{
 const candidate=candidates().at(-1);assert(candidate);assert.equal(candidate.options.method,'POST');assert.equal(candidate.options.data.parentId,parent);assert.equal(candidate.options.data.targetParentId,scenario==='independent'?targetParent:parent);assert.equal(typeof candidate.options.data.formParameters,'object');assert.equal(candidate.options.data.id,scenario==='readonly'?id:undefined)
 f.respond(candidate,[{id,name:'原候选名称',value:id,text:'原候选名称'}]);assert.equal(f.state.target_list[0].name,'原候选名称');f.state.targetIdDetailChange({id:'9007199254744000',name:'选择新候选'});assert.equal(f.state.entity.targetId,scenario==='readonly'?id:'9007199254744000')
 f.state.initLists();const reordered=candidates().at(-1);f.state.initLists();const newest=candidates().at(-1);f.respond(newest,[{id,name:'最新候选'}]);f.respond(reordered,[{id,name:'乱序旧候选'}]);assert.equal(f.state.target_list[0].name,'最新候选');f.state.initLists();const pending=candidates().at(-1);if(scenario==='independent')f.state.entity.parentId='9007199254743001';else f.state.parent_id='9007199254742001';f.respond(pending,[{id,name:'另一父范围迟到'}]);assert.notEqual(f.state.target_list[0]?.name,'另一父范围迟到')
 f.state.initLists();const identity=candidates().at(-1);f.storage.sysParams={yzProjectId:'另一个项目'};f.respond(identity,[{id,name:'旧项目迟到'}]);assert.notEqual(f.state.target_list[0]?.name,'旧项目迟到')
 }
}finally{f.app.unmount()}
console.log('通过：原zxz和findNameId/formParameters协议、真实固定两父参数、未知映射拒绝与迟到回填隔离：'+scenario)
