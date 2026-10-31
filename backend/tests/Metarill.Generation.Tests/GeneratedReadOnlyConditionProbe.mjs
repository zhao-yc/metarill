import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated} from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/customer-detail-101.vue'),'utf8')
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
