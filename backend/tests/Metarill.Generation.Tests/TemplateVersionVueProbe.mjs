import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {createRequire} from 'node:module'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url))
const {createRenderer,h,nextTick,compile}=require('vue'),{parse}=require('@vue/compiler-sfc')
const source=await readFile(join(process.argv[2],'detail.vue'),'utf8'),choices=JSON.parse(await readFile(join(process.argv[2],'catalog-options.json'),'utf8'))
const {descriptor,errors}=parse(source);assert.deepEqual(errors,[])
const requests=[],selects=[],storage={loginStateId:'离线会话',sysParams:{yzProjectId:'1608097412978577409'}},modals=[]
const uni={getStorageSync:key=>storage[key],setStorageSync:(key,value)=>storage[key]=value,$on(){},$off(){},$emit(){},showModal:options=>modals.push(options),showToast(){}}
const yzp={refreshCurrentPermissions:done=>done(),hasPagePermission:()=>true,isColumnVisible:()=>true,request:options=>{requests.push(options);return{abort(){}}}}
const utils={isBlank:value=>value===null||value===undefined||value===''}
const script=descriptor.script.content.replace(/^import[^\n]*\n/gm,'').replace('export default','return')
const options=new Function('utils','yzp','project','uni','getApp',script)(utils,yzp,{},uni,()=>({globalData:{readOnly:false}}))
options.render=compile(descriptor.template.content)
const element=(tag,text='')=>({tag,text,props:{},parent:null,children:[]})
const renderer=createRenderer({createElement:tag=>element(tag),createText:text=>element('text',text),createComment:text=>element('comment',text),setElementText(node,text){node.text=text;node.children=[]},setText(node,text){node.text=text},patchProp(node,key,old,value){node.props[key]=value},insert(node,parent,anchor){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1);const index=anchor?parent.children.indexOf(anchor):-1;parent.children.splice(index<0?parent.children.length:index,0,node);node.parent=parent},remove(node){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1)},parentNode:node=>node.parent,nextSibling:node=>node.parent?.children[node.parent.children.indexOf(node)+1]||null})
const Stub={render(){return h('view',{},this.$slots.default?.())}}
const app=renderer.createApp(options)
for(const name of ['view','text','uni-nav-bar','uni-forms-item','uni-easyinput'])app.component(name,Stub)
app.component('uni-forms',{props:['modelValue','rules'],methods:{validate(){return Promise.resolve({})}},render(){return h('form',{},this.$slots.default?.())}})
app.component('zxz-uni-data-select',{props:['modelValue','localdata','disabled','dataKey','dataValue'],emits:['change','update:modelValue'],mounted(){selects.push(this)},render(){return h('original-select')}})
app.config.globalProperties.$deviceInfo={is_pc:true};app.config.globalProperties.$yzp=yzp
app.config.warnHandler=message=>{throw new Error(message)}
const tree=element('root'),state=app.mount(tree)
const respond=(request,data)=>{assert(request);request.success({code:200,data});request.complete?.({})}
const flush=async()=>{await nextTick();await Promise.resolve();await nextTick()}
try{
 options.onLoad.call(state,{});await flush()
 assert.equal(state.entity.yzYzpVersionId,'2300000000000000001');assert.equal(typeof state.entity.yzYzpVersionId,'string')
 assert(state.entity_rules.yzYzpVersionId.rules.some(rule=>rule.required===true))
 const request=requests.find(request=>request.url.endsWith('/findNameIdYzYzpVersion1669606903854936065'));assert.equal(request.method,'POST');assert.equal(request.sendSessionId,true);assert('formParameters' in request.data)
 respond(request,choices);await flush();const select=selects.at(-1);assert(select);assert.equal(select.dataKey,'name');assert.equal(select.dataValue,'id');assert.equal(select.localdata[0].id,'2300000000000000001')
 state.entity.yzYzpVersionId=null;select.$emit('change',choices[0]);await flush();assert.equal(state.entity.yzYzpVersionId,choices[0].id);assert.equal(state.entity.yzYzpVersionName,choices[0].name)
 state.entity.name='离线目录验收';state.saveEntity();await flush();const write=requests.find(request=>request.url.endsWith('/add'));assert(write);assert.strictEqual(write.data,state.entity);assert.equal(write.data.yzYzpVersionName,'Metarill C# v1')
 // 原只读显示仍取计算名称；不把ID数字转换为JS Number。
 state.routeReadOnly=true;await flush();const text=node=>[node.text,...node.children.map(text)].join('');assert(text(tree).includes('Metarill C# v1'))
 // 原详情真实读取已有null后不得偷偷填新默认版本。
 state.routeReadOnly=false;options.onLoad.call(state,{id:'9007199254740993'});await flush();const load=requests.findLast(request=>request.url.includes('/detail/'))
 respond(load,{id:'9007199254740993',name:'既有空版本项目',yzYzpVersionId:null,yzYzpVersionName:null});await new Promise(resolve=>setTimeout(resolve,310));await flush();assert.equal(state.entity.yzYzpVersionId,null);assert.equal(state.entity.yzYzpVersionName,null)
 assert.equal(modals.length,0)
 console.log('通过：原zxz下拉DOM与change事件、字符串长ID、id/name回传、计算只读名称和既有null不回填；离线Vue不等于浏览器/数据库验收。')
}finally{app.unmount()}
