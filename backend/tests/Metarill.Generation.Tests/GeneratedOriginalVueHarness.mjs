import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
const sourceRoot=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..')
const require=createRequire(path.join(sourceRoot,'frontend/metarill_uni_admin/package.json'))
const Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const runtime=path.join(sourceRoot,'frontend/metarill_uni_admin/common/yzp_runtime')
export const plain=value=>JSON.parse(JSON.stringify(value))
/** 真实Vue3解析、编译并挂载生成页面；仅网络和外部控件底层渲染受控，不复制生成逻辑。 */
export function mountGenerated(source,props={},route,{components={}}={}){
 const requests=[],dialogs=[],navigations=[],events=[],toasts=[],targets=[],subscriptions=new Map(),storage={loginStateId:'测试身份',sysParams:{yzProjectId:'100'}},inputProps=Vue.reactive({...props})
 let permission=true,valid=true,validationCount=0
 const uni={getStorageSync:key=>storage[key],setStorageSync:(key,value)=>storage[key]=value,$on(key,fn){if(!subscriptions.has(key))subscriptions.set(key,new Set());subscriptions.get(key).add(fn)},$off(key,fn){subscriptions.get(key)?.delete(fn);if(subscriptions.get(key)?.size===0)subscriptions.delete(key)},$emit:(...args)=>events.push(args),showModal:options=>dialogs.push(options),showToast:options=>toasts.push(options),showActionSheet:options=>dialogs.push(options),navigateTo:options=>navigations.push(options),navigateBack(){},stopPullDownRefresh(){}}
 const yzp={request(options){const entry={options,aborted:false};requests.push(entry);return{abort(){entry.aborted=true}}},hasPagePermission:()=>permission,hasButtonPermission:()=>permission,isButtonVisible:()=>permission,refreshCurrentPermissions:ready=>ready(),isColumnVisible:()=>true,back(){},showDetailPageSuccessToast(){},relationArray(value){if(value==null||value==='')return[];return typeof value==='string'?JSON.parse(value):value},relationDisplayText(value){if(value==null)return'';try{return(typeof value==='string'?JSON.parse(value):value).map(v=>v==null?'':String(v)).join('，')}catch{return String(value)}},chooseImportFile(){},importSuccessAction(){},customButtonHandler(){}}
 const utils={isBlank:value=>value===null||value===undefined||value==='',getTodayDateAry:()=>['2026-10-20','2026-10-20'],openUrl:url=>navigations.push(url)}
 const exported=(name,symbol,globals={})=>new Function(...Object.keys(globals),readFileSync(path.join(runtime,name),'utf8').replace(/^import .*;?\s*$/gm,'').replace(/export function /g,'function ')+';return '+symbol)(...Object.values(globals))
 const createListPageMixin=exported('listPageRuntime.js','createListPageMixin',{uni})
 const createOriginalListOptionsMixin=exported('originalListOptionsMixin.js','createOriginalListOptionsMixin',{uni,yzp})
 const createSelectionPageMixin=exported('selectionPageMixin.js','createSelectionPageMixin',{uni,yzp})
 const createOriginalSelectionPageMixin=exported('originalSelectionPageMixin.js','createOriginalSelectionPageMixin',{uni,createSelectionPageMixin})
 const createDetailLayoutMixin=exported('detailLayoutRuntime.js','createDetailLayoutMixin',{uni})
 const createMultiSelectFieldMixin=exported('multiSelectFieldMixin.js','createMultiSelectFieldMixin',{uni})
 const imports={'@/common/utils':utils,'@/common/project':{},'@/common/yzp_runtime/yzp-runtime':yzp,'@/common/yzp_runtime/menuRuntime':{menuBoolean:value=>value===true||value==='true'},'@/common/yzp_runtime/listPageRuntime':{createListPageMixin},'@/common/yzp_runtime/originalListOptionsMixin':{createOriginalListOptionsMixin},'@/common/yzp_runtime/originalSelectionPageMixin':{createOriginalSelectionPageMixin},'@/common/yzp_runtime/detailLayoutRuntime':{createDetailLayoutMixin},'@/common/yzp_runtime/multiSelectFieldMixin':{createMultiSelectFieldMixin}}
 const {descriptor,errors}=parse(source);assert.deepEqual(errors,[])
 for(const match of descriptor.script.content.matchAll(/from\s+['"](@\/pages\/yzp[^'"]+)['"]/g))imports[match[1]]={props:['selectedIds','selectedNames','selectedNameField','parent_id','parentReadOnlyCondition','parent_page_sn','is_son_page','child_page_open_type'],emits:['onPopSelect','onPopClose'],mounted(){targets.push(this)},methods:{confirm(rows){this.$emit('onPopSelect',rows);this.$emit('onPopClose')},cancel(){this.$emit('onPopClose')},refreshData(){}},render(){return Vue.h('linked-page')}}
 const script=descriptor.script.content.replace(/import\s+(.+?)\s+from\s+(['"])([^'"]+)\2\s*;/g,(_,name,q,id)=>`const ${name}=imports[${JSON.stringify(id)}];`).replace('export default','return')
 const component=new Function('imports','uni','getApp',script)(imports,uni,()=>({globalData:{readOnly:false}}));component.render=Vue.compile(descriptor.template.content)
 const element=(tag,text='')=>({tag,text,props:{},style:{},addEventListener(){},removeEventListener(){},setAttribute(){},removeAttribute(){},parent:null,children:[]})
 const renderer=Vue.createRenderer({createElement:tag=>element(tag),createText:text=>element('#text',text),createComment:text=>element('#comment',text),setElementText(node,text){node.text=text;node.children=[]},setText(node,text){node.text=text},patchProp(node,key,previous,value){node.props[key]=value},insert(node,parent,anchor){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1);const i=anchor?parent.children.indexOf(anchor):-1;parent.children.splice(i<0?parent.children.length:i,0,node);node.parent=parent},remove(node){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1);node.parent=null},parentNode:node=>node.parent,nextSibling:node=>node.parent?.children[node.parent.children.indexOf(node)+1]||null})
 const app=renderer.createApp({render(){return Vue.h(component,{onOnChange:value=>events.push(['onChange',value]),onOnPopClose:()=>events.push(['onPopClose']),onOnPopSelect:value=>events.push(['onPopSelect',value]),...inputProps,ref:'page'})}})
 const Stub={render(){return Vue.h('host',{},this.$slots.default?.())}}
 for(const name of new Set([...descriptor.template.content.matchAll(/<([a-z][a-z0-9-]*)/g)].map(match=>match[1]).filter(name=>!['template','uni-forms','yzp-popup'].includes(name))))app.component(name,components[name]||Stub)
 app.component('uni-forms',{props:['modelValue','rules'],methods:{validate(){validationCount++;return valid?Promise.resolve({}):Promise.reject([{key:'name'}])}},render(){return Vue.h('form',{},this.$slots.default?.())}})
 app.component('yzp-popup',{emits:['change'],methods:{open(){this.$emit('change',{show:true})},close(){this.$emit('change',{show:false})}},render(){return Vue.h('popup',{},this.$slots.default?.())}})
 for(const [name,component] of Object.entries(components))if(!app.component(name))app.component(name,component)
 app.config.globalProperties.$deviceInfo={is_pc:true};app.config.globalProperties.$yzp=yzp;app.config.globalProperties.$checkboxRange=[{value:0,text:'否'},{value:1,text:'是'}];app.config.globalProperties.$multiCheckboxRange=[{value:1,text:'全选'}];app.config.globalProperties.$imageStyles={}
 app.config.warnHandler=message=>{throw new Error(message)}
 const tree=element('root'),host=app.mount(tree),state=host.$refs.page;state.getOpenerEventChannel=()=>({emit:(...args)=>events.push(args)})
 if(route)component.onLoad?.call(state,route)
 const settle=async()=>{await Vue.nextTick();await Promise.resolve();await Vue.nextTick()}
 return{state,options:component,app,tree,requests,dialogs,navigations,events,toasts,targets,storage,subscriptions,uni,yzp,async flush(){await settle()},setProps(value){Object.assign(inputProps,value)},setPermission(value){permission=value;state.permissionVersion++},setValid(value){valid=value},validations:()=>validationCount,pages:()=>requests.filter(item=>item.options.url.endsWith('/page')),writes:()=>requests.filter(item=>/\/(add|update)(\?|$)/.test(item.options.url)),respond(request,data){assert(request,'必须存在实际请求');request.options.success({code:200,data});request.options.complete?.({})},text(){const visit=node=>(node.text||'')+node.children.map(visit).join('');return visit(tree)}}
}
