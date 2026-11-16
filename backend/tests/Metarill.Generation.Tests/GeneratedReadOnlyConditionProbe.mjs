import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated} from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/customer-detail-101.vue'),'utf8')
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
