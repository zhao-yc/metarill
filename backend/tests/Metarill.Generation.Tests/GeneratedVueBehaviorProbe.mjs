import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],longId='9007199254740993',parentId='9007199254742000'
const list=await readFile(join(root,'frontend/pages/yzp/customer-list-101.vue'),'utf8'),detail=await readFile(join(root,'frontend/pages/yzp/customer-detail-101.vue'),'utf8')
const rows=(f,request,records=[],pages=5)=>f.respond(request,{records,pages,total:pages*20})
/** 原筛选弹层、PC翻页、移动加载更多和排序，验证实际生成组件与冻结请求。 */
{
 const f=mountGenerated(list,{parent_id:parentId});try{
 const first=f.pages().at(-1);assert.equal(first.options.data.pageNo,1);assert.deepEqual(plain(first.options.data.filters),{})
 Object.assign(f.state.filters,{name:'已提交名称',enabled:false,status:0,amount:'12345678901234567890.12345678',groupId:longId,signedAt:['2026-10-01','2026-10-31']});f.state.filterList();let request=f.pages().at(-1)
 assert.deepEqual(plain(request.options.data.filters),{name:'已提交名称',enabled:false,status:0,amount:'12345678901234567890.12345678',groupId:longId,signedAt:['2026-10-01','2026-10-31']});assert.equal(request.options.data.parentId,parentId);assert.equal(request.options.data.pageSize,20)
 f.state.filters.name='未提交草稿';assert.equal(request.options.data.filters.name,'已提交名称');rows(f,request,[{id:longId,name:'第一页'}]);f.state.loadNextPage();request=f.pages().at(-1);assert.equal(request.options.data.pageNo,2);assert.equal(request.options.data.filters.name,'已提交名称')
 request.options.fail({message:'翻页失败'});request.options.complete();assert.equal(f.state.page_no,1);assert.equal(f.state.list[0].name,'第一页');f.state.retryListRequest();request=f.pages().at(-1);assert.equal(request.options.data.pageNo,2);rows(f,request,[{id:'9007199254740994',name:'第二页'}]);assert.equal(f.state.page_no,2)
 // 原PC分页按钮会提交当前筛选；移动追加继续冻结之前已提交的筛选。
 f.state.filters.name='PC当前筛选';f.state.paginationChange({current:3});request=f.pages().at(-1);assert.equal(request.options.data.pageNo,3);assert.equal(request.options.data.filters.name,'PC当前筛选');rows(f,request,[])
 f.state.nameSortChange({order:'descending'});assert.deepEqual(plain(f.pages().at(-1).options.data.orderBy),{column:'name',order:'descending'});assert.equal(f.pages().at(-1).options.data.filters.name,'PC当前筛选');f.state.nameSortChange({order:null});assert.equal(f.pages().at(-1).options.data.orderBy.order,null)
 f.state.resetFilter();assert.deepEqual(plain(f.state.filters),{});f.state.filterList();assert.deepEqual(plain(f.pages().at(-1).options.data.filters),{});assert.equal(f.pages().at(-1).options.data.parentId,parentId);assert.equal(f.pages().at(-1).options.data.orderBy.order,null)
 }finally{f.app.unmount()}
 console.log('通过：原筛选弹层提交/冻结、false/0/高精度和日期、原分页重试、排序及父上下文')
}
{
 const f=mountGenerated(list,{parent_id:parentId,is_son_page:true});try{
 const old=f.pages().at(-1);f.state.filters.name='新查询';f.state.filterList();const current=f.pages().at(-1);assert(old.aborted);rows(f,old,[{id:'1',name:'旧响应'}]);assert.equal(f.state.list.length,0);rows(f,current,[{id:longId,name:'新响应',status:0,statusShow:'停用',groupId:longId,groupName:'真实VO名称',ownerId:'2',ownerName:'不得使用未声明名称',enabled:false}]);await f.flush();assert.equal(f.state.list[0].name,'新响应');assert(f.text().includes('停用'));assert(f.text().includes('真实VO名称'));assert(!f.text().includes('不得使用未声明名称'))
 f.state.getList();const parentOld=f.pages().at(-1);f.setProps({parent_id:'9007199254742001'});await f.flush();rows(f,parentOld,[{id:'1',name:'旧父数据'}]);assert.equal(f.state.list.length,0);assert.equal(f.pages().at(-1).options.data.parentId,'9007199254742001');const departed=f.pages().at(-1);f.app.unmount();rows(f,departed,[{id:'2',name:'卸载数据'}]);assert.equal(f.state.list.length,0);assert(departed.aborted)
 }finally{if(f.state._alive)f.app.unmount()}
 console.log('通过：原列表请求乱序、父切换和卸载隔离，真实常量Show及声明关系显示字段')
}

{
 const f=mountGenerated(list,{parent_id:parentId});try{const pending=f.pages().at(-1);f.storage.loginStateId='切换后的身份';rows(f,pending,[{id:longId,name:'旧身份响应'}]);assert.equal(f.state.list.length,0)}finally{f.app.unmount()}
 console.log('通过：原列表身份切换后旧响应不能回填')
}

/** 原详情保留整entity，字段省略与显式null不由前端裁剪成另一套请求。 */
function loaded(entity={}){
 const f=mountGenerated(detail,{}, {id:longId,parent_id:parentId});const get=f.requests.find(item=>item.options.url.includes('/detail/'));f.respond(get,{id:longId,name:'原名',remark:null,status:0,statusShow:'停用',amount:0,enabled:false,groupName:'只读名称',readOnlyEcho:'回显保留',...entity});return f
}
{
 const f=loaded({fee:1,rank:longId,tier:'basic'});try{
 assert.equal(f.state.feeRange[0].value,1);assert.equal(f.state.feeRange[1].value,1);assert.equal(f.state.rankRange[0].value,longId);assert.equal(typeof f.state.rankRange[0].value,'string');assert.equal(f.state.tierRange[0].value,'basic')
 f.state.remarkRtfDo('edit');const nav=f.navigations.at(-1),messages=[];assert.equal(nav.url,'/pages/main/rtf/rtf?action=edit');nav.success({eventChannel:{emit:(...args)=>messages.push(args)}});assert.deepEqual(plain(messages),[['sendRtf',{data:''}]]);nav.events.onReceiveRtf('');assert.equal(f.state.entity.remark,null)
 f.state.saveEntity();await f.flush();let write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.equal(write.options.data.remark,null);assert.equal(write.options.data.name,'原名');assert.equal(write.options.data.statusShow,'停用');assert.equal(write.options.data.groupName,'只读名称');assert.equal(write.options.data.readOnlyEcho,'回显保留');assert.equal(write.options.data.amount,0);assert.equal(write.options.data.enabled,false);assert.equal(Object.hasOwn(write.options.data,'attachments'),false);assert.match(write.options.url,/parentId=9007199254742000/);assert.equal(f.validations(),1);f.respond(write,{...f.state.entity})
 f.state.remarkRtfDo('edit');f.navigations.at(-1).events.onReceiveRtf('<p>已修改</p>');f.state.entity.name=null;f.state.saveEntity();await f.flush();write=f.writes().at(-1);assert.equal(write.options.data.name,null);assert.equal(write.options.data.remark,'<p>已修改</p>');const count=f.writes().length;f.state.saveEntity();await f.flush();assert.equal(f.writes().length,count)
 }finally{f.app.unmount()}
 console.log('通过：原RTF事件、未编辑null、整entity/只读回显/0/false、字段省略及显式null和重复保存阻断')
}
{
 for(const invalidate of [f=>{f.state.id='9007199254740994'},f=>{f.state.parent_id='9007199254742001'},f=>{f.storage.loginStateId='另一身份'},f=>f.app.unmount()]){
 const f=loaded();f.state.remarkRtfDo('edit');const nav=f.navigations.at(-1);invalidate(f);nav.events.onReceiveRtf('<p>迟到修改</p>');assert.equal(f.state.entity.remark,null);if(f.state._alive)f.app.unmount()
 }
 const f=loaded({remark:'<p>原值</p>'});try{f.state.remarkRtfDo('review');f.navigations.at(-1).events.onReceiveRtf('不允许通过预览改写');assert.equal(f.state.entity.remark,'<p>原值</p>');f.setValid(false);const count=f.writes().length;f.state.saveEntity();await f.flush();assert.equal(f.writes().length,count);f.setValid(true);f.setPermission(false);f.state.saveEntity();await f.flush();assert.equal(f.writes().length,count)}finally{f.app.unmount()}
 console.log('通过：原详情RTF记录/父/身份/卸载迟到回传、预览只读、validate失败与撤权不写')
}

{
 const f=mountGenerated(detail,{is_props:true,props_id:longId,props_parent_id:parentId});try{
 const first=f.requests.find(entry=>entry.options.url.includes('/detail/'));assert(first);f.setProps({props_id:'9007199254740994',props_parent_id:'9007199254742001'});await f.flush();const current=f.requests.filter(entry=>entry.options.url.includes('/detail/')).at(-1);assert.match(current.options.url,/9007199254740994\?parentId=9007199254742001/);f.respond(first,{id:longId,name:'旧props记录'});assert.notEqual(f.state.entity.name,'旧props记录');f.respond(current,{id:'9007199254740994',name:'当前props记录',remark:null});f.state.remarkRtfDo('edit');const nav=f.navigations.at(-1);f.setPermission(false);nav.events.onReceiveRtf('<p>撤权回执</p>');assert.equal(f.state.entity.remark,null);f.setPermission(true);f.state.closeDetail();assert(f.events.some(event=>event[0]==='onPopClose'))
 }finally{f.app.unmount()}
 console.log('通过：原嵌入详情props切记录/父范围、迟到请求、撤权RTF回执与原onPopClose事件')
}

{
 for(const responseId of ['9007199254740994',Number(longId),null,undefined]){
 const f=mountGenerated(detail,{}, {id:longId,parent_id:parentId});try{assert.equal(f.state.readOnlyCondition,true);const before=f.requests.length;f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/')),{id:responseId,name:'错误VO'});assert.equal(f.state.recordReady,false);assert.equal(f.state.readOnlyCondition,true);assert.notEqual(f.state.entity.name,'错误VO');assert.equal(f.requests.length,before);f.state.saveEntity();await f.flush();assert.equal(f.writes().length,0);assert(f.dialogs.some(dialog=>dialog.content.includes('不一致')))}finally{f.app.unmount()}
 }
 const f=loaded();try{assert.equal(f.state.recordReady,true);assert.equal(f.state.readOnlyCondition,false);f.state.saveEntity();await f.flush();const write=f.writes().at(-1);f.respond(write,{id:'9007199254740994',name:'错误保存VO'});assert.equal(f.state.id,longId);assert.notEqual(f.state.entity.name,'错误保存VO');assert.equal(f.state.recordReady,false);const count=f.writes().length;f.state.saveEntity();await f.flush();assert.equal(f.writes().length,count)}finally{f.app.unmount()}
 console.log('通过：既有详情错误/数值/空VO.id整体拒收，未读同ID不能写、合法回读仍加载候选，错误保存回执不切写对象')
}
