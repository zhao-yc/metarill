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
const detail = await readFile(join(root, 'frontend/pages/yzp/relation_source-detail-201.vue'), 'utf8')
const firstRecord = '9007199254746001', secondRecord = '9007199254746002'
const firstValue = { allowed: '甲类', minimum: '9007199254746003' }
const secondValue = { allowed: '乙类', minimum: '9007199254746004' }
const firstForm = mountGenerated(detail, {}, { id: firstRecord, parent_id: parent })
let secondForm
const lastRequest = (form, action) => form.requests.findLast(entry => entry.options.url.endsWith(endpoint + '/' + action))
const assertFormScope = (request, data) => {
 assert(request)
 assert.equal(request.options.data.parentId, parent)
 assert.deepEqual(plain(request.options.data.formParameters), { relationSource: data })
}
const respondRecord = (form, id, fields) => form.respond(form.requests.find(entry => entry.options.url.includes('/detail/')),
 { id, ...fields, targetId: '9007199254746005' })

try {
 respondRecord(firstForm, firstRecord, firstValue)
 await firstForm.flush()
 assertFormScope(lastRequest(firstForm, 'selected'), firstValue)
 firstForm.state.openOriginalRemoteOptions('25')
 const pending = lastRequest(firstForm, 'page')
 assertFormScope(pending, firstValue)

 // 同一原页面的两个编辑实例共用 uni 本地键。第二条实际回读会更新旧缓存。
 secondForm = mountGenerated(detail, {}, { id: secondRecord, parent_id: parent }, { storage: firstForm.storage })
 respondRecord(secondForm, secondRecord, secondValue)
 await secondForm.flush()
 assert.deepEqual(plain(firstForm.storage[firstForm.state.formParametersName]), { relationSource: secondValue })
 assertFormScope(lastRequest(secondForm, 'selected'), secondValue)

 // 另一实例更新缓存不能让本实例的正常回执失效，也不能把第二条记录的条件传给第一条的搜索。
 firstForm.respond(pending, { records: [row(firstValue.minimum, '甲类候选')], pages: 2 })
 assert.equal(firstForm.state.target_list[0]?.name, '甲类候选')
 firstForm.state.moreOriginalRemoteOptions('25')
 const oldPage = lastRequest(firstForm, 'page')
 assert.equal(oldPage.options.data.pageNo, 2)
 assertFormScope(oldPage, firstValue)
 secondForm.state.openOriginalRemoteOptions('25')
 const secondPage = lastRequest(secondForm, 'page')
 assertFormScope(secondPage, secondValue)

 firstForm.state.entity.allowed = '甲类已改'
 await firstForm.flush()
 assert.equal(oldPage.aborted, true)
 const newFirst = { ...firstValue, allowed: '甲类已改' }
 assertFormScope(lastRequest(firstForm, 'selected'), newFirst)
 firstForm.respond(oldPage, { records: [row(firstValue.minimum, '旧条件第二页')], pages: 2 })
 assert.deepEqual(plain(firstForm.state.target_list), [])
 secondForm.respond(secondPage, { records: [row(secondValue.minimum, '乙类候选')], pages: 1 })
 assert.equal(secondForm.state.target_list[0]?.name, '乙类候选')
 assert.equal(firstForm.state.entity.targetId, '9007199254746005')
 assert.equal(secondForm.state.entity.targetId, '9007199254746005')
 assert.deepEqual(firstForm.writes(), [])
 assert.deepEqual(secondForm.writes(), [])
} finally {
 secondForm?.app.unmount()
 firstForm.app.unmount()
}
console.log('原详情与筛选：当前表单候选、两实例隔离和旧回执验证通过')
