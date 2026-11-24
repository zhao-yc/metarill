import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated} from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/customer-detail-101.vue'),'utf8')
if (process.argv[3] === 'boolean-choice') {
 const fixture=mountGenerated(source,{}, {id:'7'})
 try {
  await fixture.flush()
  fixture.respond(fixture.requests.find(request=>request.options.url.includes('/detail/7')),
    {id:'7',name:'记录',locked:1,approvalState:'待审'})
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,true,'回显数字 1 仍按布尔真求值')
  fixture.state.saveEntity()
  await fixture.flush()
  assert.equal(fixture.writes().length,0)
  for (const [value,readOnly] of [[0,false],['1',true],['0',false],[true,true],[false,false],[null,false],[2,true],[undefined,true]]) {
   fixture.state.entity.locked=value
   await fixture.flush()
   assert.equal(fixture.state.readOnlyCondition,readOnly)
  }
  fixture.state.entity.locked=null
  fixture.state.entity.approvalState='停用'
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,true)
 } finally {fixture.app.unmount()}
 process.exit(0)
}
if (process.argv[3] === 'array-length') {
 const fixture = mountGenerated(source, {}, {id:'7'})
 try {
  await fixture.flush()
  fixture.respond(fixture.requests.find(request => request.options.url.includes('/detail/7')),
    {id:'7',name:'附件记录',galleryImages:[],limitCount:'0',locked:false})
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,false)
  for (const [value, limitCount, readOnly] of [
   [[{name:'附件'}], '0',true], ['[{"name":"附件"}]', '1',false],
   [[{name:'附件'}], '9007199254740993',false], ['[]','0',false],
   [{length:0},'0',true], ['坏数组','0',true], [null,'0',true], [undefined,'0',true]
  ]) {
   fixture.state.entity.galleryImages = value
   fixture.state.entity.limitCount = limitCount
   await fixture.flush()
   assert.equal(fixture.state.readOnlyCondition,readOnly)
  }
  fixture.state.saveEntity()
  await fixture.flush()
  assert.equal(fixture.writes().length,0)
 } finally { fixture.app.unmount() }
 process.exit(0)
}
if (process.argv[3] === 'int64') {
 const fixture=mountGenerated(source,{}, {id:'7'})
 try {
  await fixture.flush()
  fixture.respond(fixture.requests.find(request=>request.options.url.includes('/detail/7')),
    {id:'7',name:'记录',ownerId:'9007199254740992',otherId:'9'})
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,false)
  fixture.state.entity.ownerId='9007199254740993'
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,true)
  fixture.state.entity.ownerId='-2'
  fixture.state.entity.otherId='-10'
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,true)
  fixture.state.entity.ownerId='-20'
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,false)
  fixture.state.entity.ownerId=9007199254740992
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,true)
 } finally {fixture.app.unmount()}
 process.exit(0)
}
if (process.argv[3] === 'json-form') {
 const fixture=mountGenerated(source,{}, {id:'7'})
 try {
  await fixture.flush()
  const detail=fixture.requests.find(request=>request.options.url.includes('/detail/7'))
  fixture.respond(detail,{id:'7',name:'原记录',payload:'{"flags":{"locked":false}}'})
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,false)
  fixture.state.loadEntitySuccess=true
  fixture.state.getEntity()
  fixture.respond(fixture.requests.filter(request=>request.options.url.includes('/detail/7')).at(-1),{id:'7',name:'刷新回显',payload:{flags:{locked:false}}})
  await fixture.flush()
  assert.equal(fixture.writes().length,0)
  for (const invalid of [null,'坏数据',{}, {flags:[]}]) {
   fixture.state.entity.payload=invalid
   await fixture.flush()
   assert.equal(fixture.state.readOnlyCondition,true)
  }
  fixture.state.entity.payload={flags:{locked:true}}
  await fixture.flush()
  assert.equal(fixture.state.readOnlyCondition,true)
  fixture.state.entity.name='只读期间的迟到变化'
  await fixture.flush()
  assert.equal(fixture.writes().length,0)
  fixture.state.entity.payload={flags:{locked:false}}
  await fixture.flush()
  fixture.state.entity.name='用户更改'
  await fixture.flush()
  assert.equal(fixture.writes().length,1)
  fixture.respond(fixture.writes()[0],{id:'7',name:'服务器规范值',payload:{flags:{locked:false}}})
  await fixture.flush()
  assert.equal(fixture.writes().length,1)
  assert.equal(fixture.state.entity.name,'服务器规范值')
  fixture.state.entity.name='下一次用户更改'
  await fixture.flush()
  assert.equal(fixture.writes().length,2)
  console.log('通过：只读JSON对象与历史文本、缺失保护及自动保存回显去重')
 } finally {fixture.app.unmount()}
 process.exit(0)
}
if (process.argv[3] === 'pending-autosave') {
 const fixture = mountGenerated(source, {}, {id:'7'})
 const form = fixture.state
 const writes = fixture.writes
 try {
  await fixture.flush()
  fixture.respond(fixture.requests.find(request => request.options.url.includes('/detail/7')),
    {id:'7',name:'原记录',approvalState:'草稿',locked:false})
  await fixture.flush()
  form.loadEntitySuccess = true
  form.entity.name = '第一次编辑'
  await fixture.flush()
  assert.equal(writes().length,1)
  form.entity.name = '第二次编辑'
  form.entity.approvalState = '还未提交的备注'
  await fixture.flush()
  assert.equal(writes().length,1)
  fixture.respond(writes()[0],{id:'7',name:'第一次回显',approvalState:'草稿',locked:false,serverOnly:'服务器最新值'})
  await fixture.flush()
  await fixture.flush()
  assert.equal(form.entity.name,'第二次编辑','旧保存回显必须保留下次编辑')
  assert.equal(form.entity.approvalState,'还未提交的备注')
  assert.equal(form.entity.serverOnly,'服务器最新值')
  assert.equal(writes().length,2)
  assert.equal(writes()[0].options.data.name,'第一次编辑')
  assert.equal(writes()[1].options.data.name,'第二次编辑')
  assert.equal(writes()[1].options.data.approvalState,'还未提交的备注')
  fixture.respond(writes()[1],{id:'7',name:'第二次规范回显',approvalState:'还未提交的备注',locked:false})
  await fixture.flush()
  assert.equal(form.entity.name,'第二次规范回显')
  assert.equal(writes().length,2,'规范回显不能触发第三次保存')

  form.entity.name = '正在保存'
  await fixture.flush()
  form.entity.name = '迟到编辑'
  await fixture.flush()
  fixture.respond(writes()[2],{id:'7',name:'服务端已锁定',approvalState:'还未提交的备注',locked:true})
  await fixture.flush()
  assert.equal(form.entity.name,'服务端已锁定')
  assert.equal(form.readOnlyCondition,true)
  assert.equal(writes().length,3,'服务端已锁定时不再续发在途编辑')
  form.getEntity()
  fixture.respond(fixture.requests.filter(request => request.options.url.includes('/detail/7')).at(-1),
    {id:'7',name:'重新开放',approvalState:'服务器值',locked:false})
  await fixture.flush()
  form.loadEntitySuccess = true
  form.entity.name = '被拒绝的版本'
  await fixture.flush()
  form.entity.name = '保留的草稿'
  await fixture.flush()
  writes()[3].options.success({code:500,msg:'未保存'})
  writes()[3].options.complete()
  await fixture.flush()
  assert.equal(form.entity.name,'保留的草稿')
  assert.equal(writes().length,4,'失败后保留草稿与原错误提示，等待用户再次保存')
  assert.equal(fixture.dialogs.at(-1).content,'未保存')

  let buttonResult
  form.saveEntity(response => { buttonResult = response.data.name })
  await fixture.flush()
  form.entity.name = '按钮执行前的最后更改'
  await fixture.flush()
  fixture.respond(writes()[4],{id:'7',name:'较早回执',approvalState:'服务器值',locked:false})
  await fixture.flush()
  await fixture.flush()
  assert.equal(buttonResult,undefined)
  assert.equal(writes().length,6)
  fixture.respond(writes()[5],{id:'7',name:'按钮前最新回执',approvalState:'服务器值',locked:false})
  await fixture.flush()
  assert.equal(buttonResult,'按钮前最新回执')

  form.entity.name = '旧身份首次编辑'
  await fixture.flush()
  form.entity.name = '旧身份后续编辑'
  await fixture.flush()
  writes()[6].options.success({code:200,data:{id:'7',name:'旧身份回执',approvalState:'服务器值',locked:false}})
  fixture.storage.loginStateId = '切换后的身份'
  writes()[6].options.complete()
  await fixture.flush()
  assert.equal(writes().length,7,'身份切换后不得向当前记录续发原身份编辑')
 } finally { fixture.app.unmount() }
 process.exit(0)
}
if (process.argv[3] === 'inline-blur') {
 const fixture = mountGenerated(source, {is_props:true,props_id:'7'})
 const form = fixture.state
 const writes = fixture.writes
 const walk = node => [node,...node.children.flatMap(walk)]
 const blurText = async () => {
  const inputs = walk(fixture.tree).filter(node => node.props?.modelValue === form.entity.name && typeof node.props?.onBlur === 'function')
  assert.equal(inputs.length,1)
  inputs[0].props.onBlur()
  await fixture.flush()
 }
 try {
  await fixture.flush()
  fixture.respond(fixture.requests.find(request => request.options.url.includes('/detail/7')),
    {id:'7',name:'原记录',approvalState:'草稿',locked:false})
  await fixture.flush()
  form.entity.name = '第一次失焦'
  await fixture.flush()
  await blurText()
  form.entity.name = '第二次失焦'
  await fixture.flush()
  await blurText()
  assert.equal(writes().length,1)
  fixture.respond(writes()[0],{id:'7',name:'旧回显',approvalState:'草稿',locked:false})
  await fixture.flush()
  await fixture.flush()
  assert.equal(form.entity.name,'第二次失焦','第一笔回显之后必须保留第二次失焦编辑')
  assert.equal(writes().length,2)
  assert.equal(writes()[0].options.data.name,'第一次失焦')
  assert.equal(writes()[1].options.data.name,'第二次失焦')
  assert.equal(fixture.events.filter(event=>event[0]==='onPopClose').length,0)

  form.entity.name = '仍在输入'
  await fixture.flush()
  fixture.respond(writes()[1],{id:'7',name:'第二笔回显',approvalState:'草稿',locked:false})
  await fixture.flush()
  assert.equal(form.entity.name,'仍在输入')
  assert.equal(writes().length,2,'没有新的失焦时只保留草稿')
  assert.equal(fixture.events.filter(event=>event[0]==='onPopClose').length,0)
  await blurText()
  assert.equal(writes().length,3)
  assert.equal(writes()[2].options.data.name,'仍在输入')
  fixture.respond(writes()[2],{id:'7',name:'最终回显',approvalState:'草稿',locked:false})
  await fixture.flush()
  assert.equal(form.entity.name,'最终回显')
  assert.equal(fixture.events.filter(event=>event[0]==='onPopClose').length,1)

  form.entity.name = '先保存'
  await fixture.flush()
  await blurText()
  form.entity.name = '将被撤权'
  await fixture.flush()
  await blurText()
  fixture.setProps({parentReadOnlyCondition:true})
  await fixture.flush()
  fixture.respond(writes()[3],{id:'7',name:'第一笔已完成',approvalState:'草稿',locked:false})
  await fixture.flush()
  assert.equal(writes().length,4,'父页设为只读后不得接续保存')
  fixture.setProps({parentReadOnlyCondition:false})
  await fixture.flush()
  form.entity.name = '通过表单的版本'
  await fixture.flush()
  await blurText()
  form.entity.name = '下一笔校验不通过'
  await fixture.flush()
  await blurText()
  const validated = fixture.validations()
  fixture.setValid(false)
  fixture.respond(writes()[4],{id:'7',name:'已保存的版本',approvalState:'草稿',locked:false})
  await fixture.flush()
  await fixture.flush()
  assert.equal(fixture.validations(),validated+1)
  assert.equal(writes().length,5,'续发也必须通过原表单校验')
  assert.equal(form.entity.name,'下一笔校验不通过')
 } finally { fixture.app.unmount() }
 process.exit(0)
}
const fixture=mountGenerated(source,{is_props:true,props_id:'7'})
try {
 await fixture.flush()
 assert.equal(fixture.state.readOnlyCondition,true)
 const detail=fixture.requests.find(request=>request.options.url.includes('/detail/7'))
 fixture.respond(detail,{id:'7',name:'普通',locked:false,approvalState:'待审'})
 await fixture.flush();assert.equal(fixture.state.readOnlyCondition,false)
 fixture.state.entity.locked=true;await fixture.flush();assert.equal(fixture.state.readOnlyCondition,true)
 const writes=fixture.writes().length;fixture.state.saveEntity();await fixture.flush();assert.equal(fixture.writes().length,writes)
 fixture.state.entity.locked=false;fixture.state.entity.approvalState='已审';await fixture.flush();assert.equal(fixture.state.readOnlyCondition,true)
 fixture.state.entity.name='允许';await fixture.flush();assert.equal(fixture.state.readOnlyCondition,false)
 fixture.setProps({parentReadOnlyCondition:true});await fixture.flush();assert.equal(fixture.state.readOnlyCondition,true)
 fixture.setProps({parentReadOnlyCondition:false});fixture.setPermission(false);await fixture.flush();assert.equal(fixture.state.readOnlyCondition,true)
 fixture.setPermission(true);await fixture.flush();assert.equal(fixture.state.readOnlyCondition,false)
 fixture.state.recordReady=false;await fixture.flush();assert.equal(fixture.state.readOnlyCondition,true)
 console.log('通过：实际生成Vue的复合条件、字段别名、父只读、撤权及记录加载门禁保持')
} finally {fixture.app.unmount()}
