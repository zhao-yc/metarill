import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],id='9007199254740993',parent='9007199254742000',endpoint='/findNameIdTarget25'
const row=(value,name)=>({id:value,value,name,text:name})
for(const kind of ['detail','list']){
 const source=await readFile(join(root,'frontend/pages/yzp/relation_source-'+kind+'-201.vue'),'utf8')
 const f=mountGenerated(source,kind==='list'?{parent_id:parent,is_son_page:true}:{},kind==='detail'?{id,parent_id:parent}:undefined)
 try{
  if(kind==='detail'){
   f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/')),{id,name:'来源',targetId:id,allowed:"甲%_'",minimum:'9007199254743000'})
   await f.flush()
  }else{
   // 原列表由现有筛选控件更新草稿，不向缺失的详情缓存键伪造数据。
   assert.equal(f.state.formParametersName,undefined)
   assert.match(source,/v-model='filters\.allowed'/)
   assert.match(source,/v-model='filters\.minimum'/)
   Object.assign(f.state.filters,{targetId:id,allowed:"甲%_'",minimum:'9007199254743000',password:'不发送'})
   await f.flush()
  }
  const selected=()=>f.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/selected')),pages=()=>f.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/page'))
  const initial=selected().at(-1);assert(initial,kind+'应在真实父范围中独立读回已选')
  const form={allowed:"甲%_'",minimum:'9007199254743000'}
  const values=request=>kind==='detail'?request.options.data.formParameters.relationSource:request.options.data.formParameters
  assert.deepEqual(plain(initial.options.data.formParameters),kind==='detail'?{relationSource:form}:form)
  assert.equal(initial.options.data.parentId,parent);assert.deepEqual(plain(initial.options.data.selectedIds),[id])
  f.respond(initial,[row(id,'原已选')]);f.state.openOriginalRemoteOptions('25');const pending=pages().at(-1)
  assert.deepEqual(plain(pending.options.data.formParameters),plain(initial.options.data.formParameters))
  if(kind==='detail'){f.state.entity.allowed='乙';await f.flush()}
  else{f.state.filters.allowed='乙';await f.flush()}
  const current=selected().at(-1);assert.notEqual(current,initial);assert.equal(values(current).allowed,'乙')
  f.respond(pending,{records:[row(id,'旧条件迟到')],pages:'2'});assert.notEqual(f.state.target_list[0]?.name,'旧条件迟到')
  assert.equal((kind==='detail'?f.state.entity:f.state.filters).targetId,id)
  f.respond(current,[row(id,'当前已选')]);f.state.openOriginalRemoteOptions('25')
  const first=pages().at(-1);assert.equal(first.options.data.pageNo,1);assert.equal(values(first).allowed,'乙')
  assert.equal(values(initial).allowed,"甲%_'")
 }finally{f.app.unmount()}
}
console.log('实际原详情/筛选：声明表单槽、原联动、分页与已选和迟到隔离通过')
