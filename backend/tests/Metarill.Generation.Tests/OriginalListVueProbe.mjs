import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const require = createRequire(path.join(root, 'frontend/metarill_uni_admin/package.json'))
const Vue = require('vue'), { parse } = require('@vue/compiler-sfc'), { compile } = require('@vue/compiler-dom')
const runtime = path.join(root, 'frontend/metarill_uni_admin/common/yzp_runtime')
const output = process.argv[2], longId = '9007199254740993'
const plain = value => JSON.parse(JSON.stringify(value))

/** 从实际源码建立运行时，只替换宿主网络，不复制生成逻辑。 */
function exported(name, symbol, globals = {}) {
 const source = readFileSync(path.join(runtime, name), 'utf8').replace(/^import .*;?\s*$/gm, '').replace(/export function /g, 'function ')
 return new Function(...Object.keys(globals), source + ';return ' + symbol)(...Object.values(globals))
}

/** 原完整template由Vue编译并挂载；uni组件仅替换底层渲染宿主。 */
function fixture(page, props = {}) {
 const requests = [], events = [], dialogs = [], subscriptions = new Map(), navigations = []
 const uni = { getStorageSync: key => key === 'loginStateId' ? '真实测试会话' : {}, $on(name,fn) { if(!subscriptions.has(name))subscriptions.set(name,new Set());subscriptions.get(name).add(fn) }, $off(name,fn) {subscriptions.get(name)?.delete(fn)}, $emit: (...args)=>events.push(args), showModal: value=>dialogs.push(value), navigateTo: value=>navigations.push(value), stopPullDownRefresh() {} }
 const yzp = { request(options){const request={options,aborted:false};requests.push(request);return{abort(){request.aborted=true}}}, hasPagePermission(){return true}, refreshCurrentPermissions(success){success()}, back(){}, showDetailPageSuccessToast(){}, hasButtonPermission(){return true} }
 const utils={isBlank:v=>v===undefined||v===null||v==='',getTodayDateAry:()=>['2026-10-20','2026-10-20']}
 const createListPageMixin=exported('listPageRuntime.js','createListPageMixin',{uni})
 const createSelectionPageMixin=exported('selectionPageMixin.js','createSelectionPageMixin',{uni,yzp})
 const createOriginalSelectionPageMixin=exported('originalSelectionPageMixin.js','createOriginalSelectionPageMixin',{uni,createSelectionPageMixin})
 const imports={'@/common/utils':utils,'@/common/project':{},'@/common/yzp_runtime/yzp-runtime':yzp,'@/common/yzp_runtime/menuRuntime':{menuBoolean:v=>v===true||v==='true'},'@/common/yzp_runtime/listPageRuntime':{createListPageMixin},'@/common/yzp_runtime/originalSelectionPageMixin':{createOriginalSelectionPageMixin}}
 const source=readFileSync(path.join(output,`frontend/pages/yzp/customer-list-${page}.vue`),'utf8'), {descriptor,errors}=parse(source)
 assert.deepEqual(errors,[])
 const template=compile(descriptor.template.content,{mode:'function',onError:error=>{throw error}})
 let script=descriptor.script.content.replace(/import\s+(.+?)\s+from\s+(['"])([^'"]+)\2\s*;/g,(_,name,q,id)=>`const ${name}=imports[${JSON.stringify(id)}]||{};`).replace('export default','return')
 const component=new Function('imports','uni','getApp',script)(imports,uni,()=>({globalData:{readOnly:false}}))
 component.render=new Function('Vue',template.code)(Vue)
 const element=(tag,text='')=>({tag,text,props:{},children:[],parent:null})
 const renderer=Vue.createRenderer({createElement:tag=>element(tag),createText:text=>element('#text',text),createComment:text=>element('#comment',text),setElementText(node,text){node.text=text;node.children=[]},setText(node,text){node.text=text},patchProp(node,key,previous,value){node.props[key]=value},insert(node,parent,anchor){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1);const i=anchor?parent.children.indexOf(anchor):-1;parent.children.splice(i<0?parent.children.length:i,0,node);node.parent=parent},remove(node){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1);node.parent=null},parentNode:node=>node.parent,nextSibling:node=>node.parent?.children[node.parent.children.indexOf(node)+1]||null})
 const app=renderer.createApp(component,props);app.config.warnHandler=message=>{if(!message.includes('Property undefined')&&!message.includes('Failed to resolve component')&&!message.includes('is not defined on instance'))throw new Error(message)}
 app.component('yzp-popup',{setup(props,{slots,expose}){expose({open(){},close(){}});return()=>Vue.h('yzp-popup',{},slots.default?.())}});
 app.config.globalProperties.$deviceInfo={is_pc:true};app.config.globalProperties.$yzp=yzp
 const host=element('root'),vm=app.mount(host)
 return{vm,app,requests,events,dialogs,navigations,host,source}
}
const success=(request,data)=>{request.options.success({code:200,data});request.options.complete?.()}
const pageRequests=f=>f.requests.filter(r=>r.options.url.endsWith('/page'))

const normal=fixture('101')
try{
 assert.equal(pageRequests(normal).length,1)
 const first=pageRequests(normal)[0];assert.equal(first.options.data.pageNo,1);assert.deepEqual(plain(first.options.data.filters),{})
 normal.vm.filters.name='未提交';assert.deepEqual(plain(first.options.data.filters),{})
 success(first,{records:[{id:longId,name:'原名称'}],total:41,pages:3});await Vue.nextTick();assert.equal(normal.vm.list[0].name,'原名称')
 normal.vm.filterList();const filtered=pageRequests(normal).at(-1);assert.equal(filtered.options.data.filters.name,'未提交')
 success(filtered,{records:[{id:longId,name:'筛选结果'}],total:41,pages:3})
 normal.vm.paginationChange({current:2});const second=pageRequests(normal).at(-1);assert.equal(second.options.data.pageNo,2)
 success(second,{records:[{id:'9007199254740994',name:'第二页'}],total:41,pages:3});await Vue.nextTick();assert.equal(normal.vm.list[0].name,'第二页')
 normal.vm.nameSortChange({order:null});assert.equal(pageRequests(normal).at(-1).options.data.orderBy.order,null)
 normal.vm.toDetail(longId);assert.match(normal.navigations.at(-1).url,/customer-detail-101\?id=9007199254740993/)
 normal.vm.selectThis({id:longId,name:'原选择'});await Vue.nextTick()
 console.log('通过：原完整Vue模板编译挂载、冻结筛选、PC翻页、取消排序与原详情导航')
}finally{normal.app.unmount()}

const selected=fixture('102',{selectedIds:[longId],selectedNames:['过期名称'],onOnPopSelect:rows=>selected.events.push(['onPopSelect',rows])})
try{
 const read=selected.requests.find(r=>r.options.url.endsWith('/selection/selected'));assert.ok(read)
 success(read,[{id:longId,name:'实时名称'}]);success(pageRequests(selected)[0],{records:[{id:'9007199254740994',name:'另一项'}],total:41,pages:3});await Vue.nextTick()
 assert.deepEqual(plain(selected.vm.selectedList),[{id:longId,name:'实时名称'}])
 selected.vm.chkChange(0);await Vue.nextTick();assert.equal(selected.vm.selectedList.length,2)
 selected.vm.toggleSelectedOnly();let page=pageRequests(selected).at(-1);assert.deepEqual(plain(page.options.data.selectedIds),[longId,'9007199254740994'])
 success(page,{records:[{id:longId,name:'实时名称'},{id:'9007199254740994',name:'另一项'}],total:2,pages:1});await Vue.nextTick()
 const before=pageRequests(selected).length;selected.vm.selectAll();await Vue.nextTick();assert.equal(selected.vm.selectedList.length,0);assert.equal(selected.vm.list.length,0);assert.equal(pageRequests(selected).length,before)
 selected.vm.filterList();assert.equal(pageRequests(selected).length,before)
 selected.vm.sureSelect();const confirm=selected.requests.at(-1);assert.match(confirm.options.url,/selection\/selected$/);assert.deepEqual(plain(confirm.options.data.selectedIds),[])
 success(confirm,[]);await Vue.nextTick();assert.ok(selected.events.some(event=>event[0]==='onPopSelect'&&event[1].length===0))
 console.log('通过：原多选DOM、跨页草稿、完整回读、仅看已选清空后不查询全部、原row[]确认')
}finally{selected.app.unmount()}
