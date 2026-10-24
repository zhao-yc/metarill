import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],parentId='9007199254740993',childId='9007199254741001'
const read=name=>readFile(join(root,'frontend/pages/yzp/'+name),'utf8')
const parentSource=await read('customer-detail-101.vue'),listSource=await read('customer_note-list-201.vue'),detailSource=await read('customer_note-detail-201.vue'),createListSource=await read('customer_note-list-204.vue'),createDetailSource=await read('customer_note-detail-204.vue')
const lastPage=f=>f.pages().at(-1),lastWrite=f=>f.writes().at(-1)
const finishPage=(f,entry=lastPage(f),rows=[{id:childId,name:'当前子记录'}])=>f.respond(entry,{records:rows,pages:3,total:6})
/** 用原saveEntity和完整entity创建父记录，不能用手动赋id冒充保存闭环。 */
async function createParent(){const f=mountGenerated(parentSource,{},{});assert.equal(f.state.originalChildrenReady,false);f.state.change('name','新父记录');f.state.saveEntity();await f.flush();assert.ok(lastWrite(f));assert.strictEqual(lastWrite(f).options.data,f.state.entity);f.respond(lastWrite(f),{id:parentId,name:'新父记录',locked:false});await f.flush();assert.equal(f.state.originalChildrenReady,true);return f}
const childList=(parent,extra={})=>mountGenerated(listSource,{parent_id:parent.state.id,parent_page_sn:'101',is_son_page:true,child_page_open_type:2,parentReadOnlyCondition:parent.state.readOnlyCondition,...extra})

{
 const parent=await createParent();try{
  assert.deepEqual(plain(parent.state.sonItems),['子页203','子页202','子页201']);assert.equal(parent.targets.length,3)
  parent.state.change('name','父表未保存字段');let refreshes=0;for(const target of parent.targets)target.refreshData=()=>refreshes++
  const before=parent.writes().length;parent.state.onClickSonItem({currentIndex:2});parent.state.refreshSonsData();await parent.flush()
  assert.equal(parent.state.currentSonItem,2);assert.equal(parent.writes().length,before);assert.equal(parent.state.entity.name,'父表未保存字段');assert.equal(refreshes,3)
 }finally{parent.app.unmount()}
 console.log('通过：原父表单实际保存、子页排序/Tab与刷新不写父脏字段')
}
{
 const child=mountGenerated(listSource,{parent_id:'',parent_page_sn:'101',is_son_page:true,child_page_open_type:2})
 try{
  assert.equal(child.pages().length,0);child.state.toDetail('');assert.equal(child.dialogs.length,1);assert.equal(child.navigations.length,0)
  child.setProps({parent_id:parentId});await child.flush();const old=lastPage(child);assert.equal(old.options.data.parentId,parentId)
  child.setProps({parent_id:'9007199254740995'});await child.flush();assert.equal(old.aborted,true);finishPage(child,old,[{id:childId,name:'旧父记录'}]);assert.equal(child.state.list.length,0)
  finishPage(child);await child.flush();assert.equal(child.state.list[0].name,'当前子记录')
 }finally{child.app.unmount()}
 console.log('通过：真实props换父、空父无请求/导航、旧父回执拒绝')
}
{
 const parent=await createParent(),child=childList(parent);let detail
 try{
  finishPage(child);parent.state.change('name','父表保留脏值');child.state.toDetail('');await child.flush();assert.equal(child.state.childDetailPopupVisible,true);assert.equal(child.navigations.length,0)
  detail=mountGenerated(detailSource,{is_props:true,props_id:'',props_parent_id:parentId,props_parent_page_sn:'101',parentReadOnlyCondition:false})
  detail.state.change('name','新增子项');detail.state.saveEntity();await detail.flush();const write=lastWrite(detail)
  assert.equal(write.options.url,'/admin/auto/customer_note/201/add?parentId='+parentId);assert.strictEqual(write.options.data,detail.state.entity);assert.equal(write.options.data.customerId,parentId)
  detail.respond(write,{id:childId,name:'新增子项',customerId:parentId,remark:null});await detail.flush()
  const changed=detail.events.find(event=>event[0]==='onChange');assert.equal(changed[1],'save');assert.ok(detail.events.some(event=>event[0]==='onPopClose'))
  child.state.handleChildDetailChange(changed[1]);child.state.closeChildDetailPopup();assert.equal(lastPage(child).options.data.parentId,parentId);assert.equal(parent.state.entity.name,'父表保留脏值');assert.equal(parent.writes().length,1)
 }finally{detail?.app.unmount();child.app.unmount();parent.app.unmount()}
 console.log('通过：原PC弹窗props/完整entity/父键/保存和关闭事件，只刷新子页')
}
{
 const parent=await createParent(),child=childList(parent)
 try{
  child.state.$deviceInfo.is_pc=false;child.state.filters.name='固定条件';child.state.filterList();finishPage(child)
  child.state.toDetail(childId);const navigation=child.navigations.at(-1),url=new URL('https://example.test'+navigation.url)
  assert.equal(url.pathname,'/pages/yzp/customer_note-detail-201');assert.equal(url.searchParams.get('parent_id'),parentId);assert.equal(url.searchParams.get('parent_page_sn'),'101')
  navigation.events.onChange('delete');assert.equal(lastPage(child).options.data.filters.name,'固定条件')
  const before=child.pages().length;child.setProps({parent_id:'9007199254740995'});await child.flush();const changedCount=child.pages().length;assert.ok(changedCount>before);navigation.events.onChange('delete');assert.equal(child.pages().length,changedCount)
  child.state.toDetail('子 &?/#');const encoded=new URL('https://example.test'+child.navigations.at(-1).url);assert.equal(encoded.searchParams.get('id'),'子 &?/#')
 }finally{child.app.unmount();parent.app.unmount()}
 console.log('通过：移动端保持原新页路由、筛选快照和URL编码、换父后旧返回事件失效')
}
{
 const detail=mountGenerated(detailSource,{is_props:true,props_id:childId,props_parent_id:parentId,props_parent_page_sn:'101',parentReadOnlyCondition:true})
 try{
  detail.respond(detail.requests.at(-1),{id:childId,name:'只读子项',remark:'<p>原文</p>'});await detail.flush();assert.equal(detail.state.readOnlyCondition,true)
  detail.state.change('name','不应写入');detail.state.saveEntity();detail.state.copyEntity();detail.state.deleteEntity();await detail.flush();assert.equal(detail.writes().length,0);assert.equal(detail.state.entity.name,'只读子项');assert.equal(detail.dialogs.length,0)
  detail.state.remarkRtfDo('edit');assert.equal(detail.navigations.length,0);detail.state.remarkRtfDo('review');assert.equal(detail.navigations.length,1)
 }finally{detail.app.unmount()}
 console.log('通过：原只读子页允许预览，所有写入/复制/删除/编辑入口均拒绝')
}
{
 const create=mountGenerated(createDetailSource,{}, {parent_id:parentId,parent_page_sn:'101'}),list=mountGenerated(createListSource,{parent_id:parentId,parent_page_sn:'101',is_son_page:true})
 try{
  assert.equal(list.state.page_is_update,false);assert.equal(list.state.page_is_new,true);assert.equal(list.state.readOnlyCondition,false)
  assert.equal(create.state.readOnlyCondition,false);create.state.change('name','只允许新增');create.state.saveEntity();await create.flush();assert.equal(lastWrite(create).options.url,'/admin/auto/customer_note/204/add?parentId='+parentId)
  create.respond(lastWrite(create),{id:childId,name:'只允许新增',customerId:parentId});await create.flush();assert.equal(create.state.readOnlyCondition,true)
  const before=create.writes().length;create.state.change('name','不能更新');create.state.saveEntity();await create.flush();assert.equal(create.writes().length,before)
 }finally{create.app.unmount();list.app.unmount()}
 console.log('通过：原仅新增子页保留入口，保存后禁止更新')
}
{
 const detail=mountGenerated(detailSource,{is_props:true,props_id:'',props_parent_id:parentId,props_parent_page_sn:'101'})
 detail.state.change('name','迟到写入');detail.state.saveEntity();await detail.flush();const pending=lastWrite(detail);detail.app.unmount();detail.respond(pending,{id:childId,name:'迟到结果'});assert.equal(pending.aborted,true);assert.equal(detail.state.id,'');assert.equal(detail.events.length,0)
 console.log('通过：原详情卸载后写回执失效，不发送父事件')
}
