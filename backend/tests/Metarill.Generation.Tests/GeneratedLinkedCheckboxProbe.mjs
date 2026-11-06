import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'

const source=await readFile(join(process.argv[2],'frontend/pages/yzp/gs_single_source-detail-201.vue'),'utf8')
const id='9007199254746001',first='9007199254745001',second='9007199254745002'
const row=(id,name)=>({id,value:id,name,text:name})
const candidates=page=>page.requests.filter(request=>request.options.url.endsWith('/findNameIdChecked2204'))

for(const existing of [true,false]){
 const f=mountGenerated(source,{},existing?{id}:{})
 try{
  if(existing){
   assert.equal(candidates(f).length,0,'取得实际VO前不得查询')
   f.respond(f.requests.find(request=>request.options.url.includes('/detail/')),{id,name:'来源',segment:1,checkedIds:[first],checkedNames:['甲']})
  }
  await f.flush()
  const [initial]=candidates(f)
  assert.equal(candidates(f).length,1,'首次应按实际记录或新增默认值只查一次')
  assert.deepEqual(plain(initial.options.data.formParameters),{gsSingleSource:{segment:1}})

  f.state.entity.segment=''
  assert.equal(initial.options.isCurrent(),false,'清空时旧候选立即失效')
  await f.flush()
  assert.equal(candidates(f).length,1,'空数字编辑值不发请求')
  f.state.entity.segment='2'
  assert.equal(initial.options.isCurrent(),false,'字段变动即失效，早于Vue刷新队列')
  await f.flush()
  const changed=candidates(f).at(-1)
  assert.equal(candidates(f).length,2,'联动变更只查一次')
  assert.deepEqual(plain(changed.options.data.formParameters),{gsSingleSource:{segment:'2'}})
  f.respond(changed,[row(second,'乙')])
  f.respond(initial,[row(first,'旧条件迟到')])
  await f.flush()
  assert.deepEqual(plain(f.state.checkedIdsList),[row(second,'乙')])
  assert.deepEqual(plain(f.state.entity.checkedIds),existing?[first]:[])
  assert.equal(f.writes().length,0)
  assert.equal(f.dialogs.length,0)
 }finally{f.app.unmount()}
}

const f=mountGenerated(source,{},{id})
try{
 f.setPermission(false)
 f.respond(f.requests.find(request=>request.options.url.includes('/detail/')),{id,name:'只读',segment:1,checkedIds:[first],checkedNames:['甲']})
 await f.flush()
 assert.equal(candidates(f).length,0,'只读详情不请求编辑候选')
}finally{f.app.unmount()}
