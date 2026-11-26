import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],parent=process.argv[3]==='parent',stem=process.argv[4]??'mt_nrs_offline',display=process.argv[5]??'customerName',targetName=process.argv[6]??'name'
const integerName=process.argv[7]==='int64',boolName=process.argv[7]==='bool'
const text=boolName?'fal':integerName?'5474099':"中文_%!引号'"
const oldName=boolName?true:integerName?'9007199254740992':'原名称'
const chosenName=boolName?false:integerName?'9007199254740993':'新名称'
const source=await readFile(join(root,`frontend/pages/yzp/${stem}_order-list-101.vue`),'utf8')
const list=mountGenerated(source,{parent_id:'71',is_son_page:true})
try{
 list.state.filters[display]=text;list.state.filterList();const request=list.pages().at(-1)
 assert.deepEqual(plain(request.options.data.filters),{[display]:text})
 list.state.filters[display]='后续草稿';assert.equal(request.options.data.filters[display],text)
}finally{list.app.unmount()}
const detail=mountGenerated(await readFile(join(root,`frontend/pages/yzp/${stem}_order-detail-101.vue`),'utf8'),{},{id:'9007199254741001',parent_id:'71'})
try{
 detail.respond(detail.requests.find(request=>request.options.url.includes('/detail/')),{id:'9007199254741001',name:'源记录',customerId:'101',[display]:oldName})
 await detail.flush();detail.state.openSelect15();await detail.flush();let target=detail.targets.at(-1);assert(target)
 assert.equal(String(target.parent_id),parent?'71':'');target.cancel();await detail.flush();assert.equal(detail.state.entity.customerId,'101')
 detail.state.openSelect15();await detail.flush();target=detail.targets.at(-1);target.confirm({id:'9007199254740993',[targetName]:chosenName});await detail.flush()
 assert.equal(detail.state.entity.customerId,'9007199254740993');assert.equal(detail.state.entity[display],chosenName)
 detail.state.openSelect15();await detail.flush();target=detail.targets.at(-1);detail.setPermission(false);target.confirm({id:'102',name:'迟到结果'});await detail.flush();assert.equal(detail.state.entity.customerId,'9007199254740993')
}finally{detail.app.unmount()}
if(process.argv[8]==='independent'){
 const detail=mountGenerated(await readFile(join(root,'frontend/pages/yzp',stem+'_order-detail-106.vue'),'utf8'),{},{id:'9007199254741001'})
 try{
  const oldParent='9007199254741002',newParent='9007199254741003',chosenId='9007199254741004'
  detail.respond(detail.requests.find(request=>request.options.url.includes('/detail/')),{id:'9007199254741001',name:'源记录',groupId:oldParent,customerId:'101',[display]:oldName})
  await detail.flush()
  detail.state.openSelect15();await detail.flush()
  const oldTarget=detail.targets.at(-1)
  assert.equal(oldTarget.parent_id,oldParent)
  detail.state.entity.groupId=newParent;await detail.flush()
  oldTarget.confirm({id:'9007199254741005',[targetName]:chosenName});await detail.flush()
  assert.equal(detail.state.entity.customerId,'101','原父值的单选回执不应写入当前表单')
  assert.equal(detail.state.entity[display],oldName)
  assert.equal(oldTarget.parent_id,oldParent)
  detail.state.openSelect15();await detail.flush()
  const newTarget=detail.targets.at(-1)
  assert.notEqual(newTarget,oldTarget)
  assert.equal(newTarget.parent_id,newParent)
  newTarget.confirm({id:chosenId,[targetName]:chosenName});await detail.flush()
  assert.equal(detail.state.entity.customerId,chosenId)
  assert.equal(detail.state.entity[display],chosenName)
 }finally{detail.app.unmount()}
}
console.log('通过：原单选页名称筛选/草稿冻结、取消、确认、长ID、父上下文与撤权迟到保护；网络和宿主替身不代表浏览器验收')
