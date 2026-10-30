import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRenderer, h, nextTick } from 'vue'
const source=await readFile(new URL('../common/yzp_runtime/originalRemoteOptionsMixin.js',import.meta.url),'utf8')
const remoteSource=await readFile(new URL('../common/yzp_runtime/remoteRelationOptions.js',import.meta.url),'utf8')
const { createRemoteRelationOptions }=await import('data:text/javascript;base64,'+Buffer.from(remoteSource).toString('base64'))
const id='9007199254750011',parent='9007199254742001',otherParent='9007199254742002',endpoint='/admin/auto/source/201/findNameIdTarget25'
const row=(id,name='候选'+id)=>({id:String(id),value:String(id),name,text:name})
/** 挂载真实共享mixin与分页状态机，只替换uni宿主和HTTP传输，不复制生产控制逻辑。 */
function fixture(owner='entity',readonly=false) {
 const requests=[],dialogs=[],events=new Map(),storage={loginStateId:'合成会话',sysParams:{yzProjectId:'100'}}
 const uni={getStorageSync:key=>storage[key],showModal:options=>dialogs.push(options),$on(name,fn){if(!events.has(name))events.set(name,new Set());events.get(name).add(fn)},$off(name,fn){events.get(name)?.delete(fn)}}
 const yzp={request(options){const request={options,aborted:false};requests.push(request);return{abort(){request.aborted=true;options.fail?.();options.complete?.()}}}}
 const factory=new Function('uni','yzp','createRemoteRelationOptions',source.replace(/^import .*;\s*$/gm,'').replace('export function','function')+';return createOriginalRemoteOptionsMixin')(uni,yzp,createRemoteRelationOptions)
 const definition={id:'25',field:'targetId',list:'target_list',endpoint,requireParent:true,requireTargetParent:true,sameParent:false,targetParentField:'ownerId',readonlyField:owner==='entity'?'targetIdReadOnly':null}
 const node=()=>({children:[],parent:null}),renderer=createRenderer({createElement:node,createText:node,createComment:node,setElementText(){},setText(){},patchProp(){},insert(n,p){n.parent=p;p.children.push(n)},remove(n){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1)},parentNode:n=>n.parent,nextSibling:()=>null})
 const app=renderer.createApp({mixins:[factory([definition],owner)],data(){return{entity:{targetId:id,ownerId:parent},filters:{targetId:id,ownerId:parent},target_list:[],id:'9007199254741000',parent_id:parent,permissionsReady:false,recordReady:false,targetIdReadOnly:readonly}},created(){this._alive=true;this._detailEpoch=1},render(){return h('view')}})
 const vm=app.mount(node())
 const respond=(request,data,code=200)=>{assert(request);request.options.success({code,data});request.options.complete?.()}
 return{app,vm,requests,storage,dialogs,respond,events,emit(name){for(const listener of events.get(name)||[])listener()},value:()=>vm[owner].targetId,selected:()=>requests.filter(r=>r.options.url.endsWith('/selected')),pages:()=>requests.filter(r=>r.options.url.endsWith('/page')),ready(){vm.permissionsReady=true;vm.recordReady=true;vm.loadOriginalRemoteOptions('25')}}
}

test('原详情大候选分101页加载，首屏外已选回读与搜索保持字符串ID',async()=>{
 const f=fixture();try{
 assert.equal(f.requests.length,0);f.ready();assert.equal(f.pages().length,0);assert.equal(f.selected().length,1)
 f.respond(f.selected().at(-1),[row(id,'首屏外已选')]);assert.equal(f.vm.originalRemoteOptions['25'].selected[0].name,'首屏外已选');assert.equal(f.value(),id)
 f.vm.openOriginalRemoteOptions('25')
 const all=Array.from({length:5014},(_,index)=>row(String(9007199254745000n+BigInt(index)),'名称'+index))
 for(let page=1;page<=101;page++){
  const request=f.pages().at(-1);assert.equal(request.options.data.pageNo,page);assert.equal(request.options.data.pageSize,50);assert.equal(request.options.data.parentId,parent);assert.equal(request.options.data.targetParentId,parent)
  f.respond(request,{records:all.slice((page-1)*50,page*50),total:'5014',pages:'101',current:page,size:50})
  if(page<101)f.vm.moreOriginalRemoteOptions('25')
 }
 assert.equal(f.vm.target_list.length,5014);assert.equal(f.vm.originalRemoteOptions['25'].hasMore,false);assert.equal(f.value(),id)
 f.vm.searchOriginalRemoteOptions('25',"%_'");await new Promise(resolve=>setTimeout(resolve,330))
 const search=f.pages().at(-1);assert.equal(search.options.data.keyword,"%_'");assert.equal(search.options.data.pageNo,1)
 f.respond(search,{records:[row('9007199254759000',"%_'匹配")],pages:'1'})
 assert.equal(f.vm.target_list.length,1);assert.equal(f.vm.originalRemoteOptions['25'].selected[0].id,id);assert.equal(f.value(),id)
 assert(f.requests.every(r=>r.options.url===endpoint+'/selected'||r.options.url===endpoint+'/page'))
 }finally{f.app.unmount()}
})

test('原筛选独立接回分页，换目标父即清旧标签但保留筛选值，迟到与失败不回填',async()=>{
 const f=fixture('filters');try{
 f.vm.loadOriginalRemoteOptions('25');f.respond(f.selected().at(-1),[row(id,'甲父已选')]);f.vm.openOriginalRemoteOptions('25');const pending=f.pages().at(-1)
 f.vm.filters.ownerId=otherParent;await nextTick();assert.equal(f.value(),id);assert.equal(f.vm.target_list.length,0);assert.equal(f.vm.originalRemoteOptions['25'].selected[0].name,id);assert.equal(pending.aborted,true)
 f.respond(pending,{records:[row(id,'甲父迟到')],pages:'1'});assert.equal(f.vm.target_list.length,0)
 const selected=f.selected().at(-1);assert.equal(selected.options.data.targetParentId,otherParent);f.respond(selected,null,500)
 assert.equal(f.value(),id);assert.match(f.vm.originalRemoteOptions['25'].message,/已保留原值/)
 f.vm.openOriginalRemoteOptions('25');const next=f.pages().at(-1);assert.equal(next.options.data.targetParentId,otherParent);assert.equal(f.dialogs.length,0)
 f.respond(next,null,500);assert(f.dialogs.some(dialog=>/读取失败/.test(dialog.content)));assert.equal(f.value(),id)
 }finally{f.app.unmount()}
})

test('只读详情只回读已选，不发分页；卸载取消请求且不修改实体',()=>{
 const f=fixture('entity',true);f.ready();f.vm.openOriginalRemoteOptions('25');assert.equal(f.pages().length,0)
 const request=f.selected().at(-1);assert(request);f.app.unmount();assert(request.aborted)
 f.respond(request,[row(id,'卸载迟到')]);assert.equal(f.value(),id);for(const listeners of f.events.values())assert.equal(listeners.size,0)
})

test('身份与来源父改变隔离旧回执，注销不自动登录或发送新请求',async()=>{
 const f=fixture('filters');try{
 f.vm.loadOriginalRemoteOptions('25');f.respond(f.selected().at(-1),[row(id,'原会话')]);f.vm.openOriginalRemoteOptions('25');const pending=f.pages().at(-1)
 f.storage.sysParams={yzProjectId:'101'};f.respond(pending,{records:[row(id,'旧项目迟到')],pages:'1'});assert.equal(f.vm.target_list.length,0);f.emit('onChangeProject')
 assert.equal(f.vm.originalRemoteOptions['25'].selected[0].name,id)
 f.vm.parent_id=otherParent;await nextTick();const selected=f.selected().at(-1);assert.equal(selected.options.data.parentId,otherParent)
 f.storage.loginStateId='';f.emit('onLogout');const count=f.requests.length;f.vm.openOriginalRemoteOptions('25');f.respond(selected,[row(id,'退出迟到')]);assert.equal(f.requests.length,count);assert.equal(f.value(),id);assert.equal(f.vm.originalRemoteOptions['25'].selected[0].name,id)
 }finally{f.app.unmount()}
})

test('原详情换记录的准备期不请求；非法已选保值，清空只由外部操作改变',()=>{
 const f=fixture();try{
 f.ready();const pending=f.selected().at(-1);f.vm.permissionsReady=false;f.vm.recordReady=false;f.vm.entity={targetId:'0',ownerId:otherParent};const count=f.requests.length
 f.respond(pending,[row(id,'上一记录')]);assert.equal(f.vm.originalRemoteOptions['25'].selected[0].id,'0');assert.equal(f.requests.length,count)
 f.vm.permissionsReady=true;f.vm.recordReady=true;assert.equal(f.requests.length,count);assert.match(f.vm.originalRemoteOptions['25'].message,/已选标识无效/)
 f.vm.entity.targetId=null;assert.equal(f.value(),null);assert.equal(f.vm.originalRemoteOptions['25'].selected.length,0)
 }finally{f.app.unmount()}
})
