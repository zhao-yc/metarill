import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],source=await readFile(join(root,'frontend/pages/yzp/relation_source-detail-201.vue'),'utf8'),scenario=process.argv[3],id='9007199254740993',parent='9007199254742000',targetParent='9007199254743000'
const endpoint='/findNameIdTarget25'
const row=(value,name)=>({id:value,value,name,text:name})
const f=mountGenerated(source,{parentReadOnlyCondition:scenario==='independentReadonly'}, {id,parent_id:parent})
try{
 f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/')),{id,name:'来源记录',targetId:id,parentId:targetParent});await f.flush()
 const selected=()=>f.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/selected'))
 const candidates=()=>f.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/page'))
 assert.equal(f.requests.filter(entry=>entry.options.url.endsWith(endpoint)).length,0)
 if(scenario==='unresolved'){assert.equal(selected().length,0);assert(f.dialogs.some(dialog=>dialog.content.includes('关联目标父范围尚未明确')));assert.deepEqual(plain(f.state.target_list),[])}
 else{
 const initial=selected().at(-1);assert(initial);assert.equal(initial.options.method,'POST');assert.equal(initial.options.data.parentId,parent);assert.equal(initial.options.data.targetParentId,scenario.startsWith('independent')?targetParent:parent);assert.deepEqual(initial.options.data.selectedIds,[id]);assert.equal(initial.options.data.formParameters,undefined)
 f.respond(initial,[row(id,'非首屏已选')]);assert.equal(f.state.originalRemoteOptions['25'].selected[0].name,'非首屏已选');assert.equal(f.state.entity.targetId,id);assert.equal(candidates().length,0)
 f.state.openOriginalRemoteOptions('25')
 const readonly=scenario==='readonly'||scenario==='independentReadonly'
 if(readonly)assert.equal(candidates().length,0)
 else{
  const first=candidates().at(-1);assert(first);assert.equal(first.options.data.pageNo,1);f.respond(first,{records:[row('9007199254744000','第一屏')],pages:'101',total:'5014',current:1,size:50})
  assert.equal(f.state.target_list[0].name,'第一屏');assert.equal(f.state.entity.targetId,id);assert.equal(f.state.originalRemoteOptions['25'].selected[0].name,'非首屏已选')
  f.state.moreOriginalRemoteOptions('25');const second=candidates().at(-1);assert.equal(second.options.data.pageNo,2);f.respond(second,{records:[row('9007199254744001','第二屏')],pages:'101'})
  assert.equal(f.state.target_list.length,2)
  f.state.targetIdDetailChange(row('9007199254744001','第二屏'));assert.equal(f.state.entity.targetId,'9007199254744001')
 }
 f.state.targetIdDetailChange(row('9007199254744000','选择新候选'));assert.equal(f.state.entity.targetId,readonly?id:'9007199254744000')
 const previousId=f.state.entity.targetId;f.state.openOriginalRemoteOptions('25');if(!readonly)f.state.moreOriginalRemoteOptions('25')
 const pending=candidates().at(-1)
 if(scenario.startsWith('independent')){
  f.state.entity.parentId='9007199254743001';assert.deepEqual(plain(f.state.target_list),[]);assert.equal(f.state.entity.targetId,previousId)
  if(pending)f.respond(pending,{records:[row(id,'旧父迟到')],pages:'1'})
  assert.deepEqual(plain(f.state.target_list),[]);await f.flush();const failed=selected().at(-1);assert.equal(failed.options.data.targetParentId,'9007199254743001')
  failed.options.fail?.({message:'新父读取失败'});failed.options.complete?.();assert.equal(f.state.entity.targetId,previousId);assert.equal(f.state.originalRemoteOptions['25'].selected[0].name,previousId)
  f.state.entity.parentId='';const count=f.requests.length;await f.flush();assert.equal(f.requests.length,count);assert.equal(f.state.entity.targetId,previousId)
  f.state.entity.parentId='9007199254743002';await f.flush();assert.equal(selected().at(-1).options.data.targetParentId,'9007199254743002')
 }else{
  f.state.parent_id='9007199254742001';if(pending)f.respond(pending,{records:[row(id,'旧来源父迟到')],pages:'1'});assert.notEqual(f.state.target_list[0]?.name,'旧来源父迟到')
 }
 const identity=selected().at(-1);f.storage.sysParams={yzProjectId:'另一个项目'};f.respond(identity,[row(previousId,'旧项目迟到')]);assert.notEqual(f.state.originalRemoteOptions['25'].selected[0]?.name,'旧项目迟到')
 }
}finally{f.app.unmount()}

if(scenario==='same'){
 const list=await readFile(join(root,'frontend/pages/yzp/relation_source-list-201.vue'),'utf8')
 const l=mountGenerated(list,{parent_id:parent,is_son_page:true})
 try{
  l.state.filters.targetId=id;l.state.initLists();await l.flush()
  const selected=l.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/selected')).at(-1);assert(selected);assert.equal(selected.options.data.targetParentId,parent)
  l.respond(selected,[row(id,'原筛选已选')]);l.state.openOriginalRemoteOptions('25')
  const page=l.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/page')).at(-1);assert(page);l.respond(page,{records:[row('9007199254744000','原筛选第一页')],pages:'101',total:'5014'})
  assert.equal(l.state.filters.targetId,id);assert.equal(l.state.target_list.length,1);assert.equal(l.state.originalRemoteOptions['25'].selected[0].name,'原筛选已选')
  l.state.filterList();await l.flush();const filtered=l.requests.filter(entry=>entry.options.url==='/admin/auto/relation_source/201/page').at(-1);assert(filtered);assert.equal(filtered.options.data.filters.targetId,id)
  assert.equal(l.requests.filter(entry=>entry.options.url.endsWith(endpoint)).length,0)
 }finally{l.app.unmount()}
}
console.log('通过：实际生成的原zxz详情/筛选分页与已选回读、固定两父、只读与迟到保值：'+scenario)
