import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..')
const require=createRequire(path.join(root,'frontend/metarill_uni_admin/package.json'))
const Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const runtime=path.join(root,'frontend/metarill_uni_admin/common/yzp_runtime')
const fixture=JSON.parse(readFileSync(path.join(process.argv[2],'contract.json'),'utf8'))
const tag=fixture.Tag,fields=fixture.fields
/** 加载真实共享模块；只替换其utils外部空值检查，不复制规则或存储模式实现。 */
function module(name,exports,imports={}) {
 const text=readFileSync(path.join(runtime,name),'utf8').replace(/^import .*$/gm,'').replace(/export\s*\{[\s\S]*?\}\s*$/,'').replace(/export function /g,'function ')
 return new Function(...Object.keys(imports),text+';return {'+exports.join(',')+'};')(...Object.values(imports))
}
const rule=module('buttonVisibleRule.js',['LOGIC_OPTIONS','OPERATOR_OPTIONS','createEmptyCondition','createEmptyRule','normalizeRule','serializeRule'],{utils:{isBlank:value=>value==null||typeof value==='string'&&!value.trim()}})
const storage=module('tableStorageMode.js',['tableStorageMode','isTableStorageModeLocked','changeTableStorageMode'])
const source=readFileSync(path.join(root,'frontend/metarill_uni_admin/components',tag,tag+'.vue'),'utf8')
const {descriptor,errors}=parse(source);assert.deepEqual(errors,[])
/** 实际挂载组件完整script与template；网络只返回本次读取需要的受控现有接口响应。 */
async function mount(entity) {
 const requests=[],events=[],reads=new Set(),raw={...entity}
 const tracked=new Proxy(raw,{get(object,key,receiver){if(typeof key==='string'&&!key.startsWith('__')&&!['toJSON','constructor'].includes(key))reads.add(key);return Reflect.get(object,key,receiver)}})
 const imports={'@/common/yzp_runtime/buttonVisibleRule':rule,'@/common/yzp_runtime/tableStorageMode':storage,'@/common/yzp_runtime/yzp-runtime':{request(options){requests.push(options);return{abort(){}}}}}
 const content=descriptor.script.content.replace(/import\s+([\s\S]+?)\s+from\s+(['"])([^'"]+)\2\s*;?/g,(_,binding,q,id)=>'const '+binding+'=imports['+JSON.stringify(id)+'];').replace('export default','return')
 const component=new Function('imports',content)(imports);component.render=Vue.compile(descriptor.template.content)
 const node=(type,text='')=>({type,text,children:[],parent:null,props:{}})
 const renderer=Vue.createRenderer({createElement:node,createText:text=>node('#text',text),createComment:text=>node('#comment',text),setElementText(n,text){n.text=text;n.children=[]},setText(n,text){n.text=text},patchProp(n,k,old,value){n.props[k]=value},insert(n,parent,anchor){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1);const i=anchor?parent.children.indexOf(anchor):-1;parent.children.splice(i<0?parent.children.length:i,0,n);n.parent=parent},remove(n){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1)},parentNode:n=>n.parent,nextSibling:n=>n.parent?.children[n.parent.children.indexOf(n)+1]||null})
 const app=renderer.createApp({render(){return Vue.h(component,{entity:Vue.reactive(tracked),recordId:'',readonly:false,onReceive:value=>events.push(JSON.parse(JSON.stringify(value))),ref:'consumer'})}})
 const Stub={render(){return Vue.h('stub',{},this.$slots.default?.())}}
 for(const name of new Set([...descriptor.template.content.matchAll(/<([a-z][a-z0-9-]*)/g)].map(match=>match[1]).filter(name=>name!=='template')))app.component(name,Stub)
 // 使用标准Vue3实例，不提供$set；真实组件必须依靠响应式对象直接赋值。
 app.config.warnHandler=message=>{if(!message.startsWith('Extraneous non-props'))throw new Error(message)}
 const host=app.mount(node('root')),state=host.$refs.consumer
 const settle=async()=>{await Vue.nextTick();await Promise.resolve();await Vue.nextTick()}
 await settle()
 for(let index=0;index<requests.length;index++) {
  const request=requests[index]
  if(request.url.endsWith('/storage_mode_policy'))request.success({code:200,data:{isV8:true,supportsNonRedundant:true}})
  else if(request.url.endsWith('/control_metadata')) {
   assert.equal(request.data.id,'9007199254740993')
   request.success({code:200,data:{controlKind:'FileLibrary',columnName:'attachments',columnType:'text',name:'继承字段',controlDetail:'{"accessMode":"PROTECTED","maxCount":3}'}})
  } else if(request.url.endsWith('/list_control_tables')||request.url.endsWith('/select_page_options'))request.success({code:200,data:[]})
  else throw new Error('不期望的实际组件请求：'+request.url)
  await settle()
 }
 return{app,state,raw,events,reads,requests,settle}
}
const all={id:null,name:'固定名称',unrelatedSecret:'完整对象仅透传，不能加入读计划'}
if(fixture.table==='config_button')all.visibleRuleJson='{"logic":"AND","conditions":[{"field":"status","operator":"eq","value":"1"}]}'
else if(fixture.table==='config_table')Object.assign(all,{isRongyu:1,isView:fixture.booleanFlags?fixture.isView:0,isFunctionTable:fixture.booleanFlags?fixture.isFunction:0})
else {all.controlDetail='{"accessMode":"PROTECTED","maxCount":3}';if(fixture.table==='config_column')all.controlKind='FileLibrary';else all.configColumnId='9007199254740993'}
const projected=Object.fromEntries(fields.map(key=>[key,all[key]]))
assert(!Object.hasOwn(projected,'unrelatedSecret'))
const full=await mount(all),narrow=await mount(projected)
try {
 // 首次完整渲染只读取已声明字段；分支的已证明不存在属性不需要伪造null列。
 const absent=fixture.table==='config_column'?['configColumnId']:fixture.table==='config_page_column'?['controlKind']:[]
 for(const read of narrow.reads)assert(fields.includes(read)||absent.includes(read),'未声明的真实组件读取：'+read)
 const summarize=state=>fixture.table==='config_button'?state.serializedRule:fixture.table==='config_table'?{mode:state.mode,locked:state.locked,options:state.options}:{kind:state.resolvedControlKind,title:state.resolvedFieldTitle,columnId:state.resolvedConfigColumnId,detail:state.serializedControlDetail}
 assert.deepEqual(JSON.parse(JSON.stringify(summarize(full.state))),JSON.parse(JSON.stringify(summarize(narrow.state))))
 const initial={...projected};full.reads.clear();narrow.reads.clear()
 if(fixture.table==='config_button'){full.state.rule.conditions[0].value='2';narrow.state.rule.conditions[0].value='2'}
 else if(fixture.table==='config_table'){
  assert.equal(narrow.state.options.length,all.isView||all.isFunctionTable?1:2)
  // 用当前确实可见的0/1选项；有视图/功能限制时把无效旧值切回原合法冗余值。
  const next=all.isView||all.isFunctionTable?1:0
  if(next===1){full.state.entity.isRongyu=0;narrow.state.entity.isRongyu=0;initial.isRongyu=0}
  full.state.changeMode(next);narrow.state.changeMode(next)
 }
 else {full.state.fileLibraryForm.maxCount=7;narrow.state.fileLibraryForm.maxCount=7}
 await full.settle();await narrow.settle()
 const fullEvent=full.events.at(-1),event=narrow.events.at(-1);assert(fullEvent);assert(event)
 const own=fixture.table==='config_button'?'visibleRuleJson':fixture.table==='config_table'?'isRongyu':'controlDetail'
 assert.equal(event[own],fullEvent[own]);assert.notEqual(event[own],initial[own])
 assert.equal(fullEvent.unrelatedSecret,all.unrelatedSecret);assert(!Object.hasOwn(event,'unrelatedSecret'))
 assert.deepEqual(Object.keys(event).sort(),fields.slice().sort())
 for(const key of fields.filter(key=>key!==own))assert.deepEqual(event[key],initial[key],'控件改写了固定值以外的属性：'+key)
 if(fixture.table==='config_page_column')assert(narrow.requests.some(request=>request.url.endsWith('/control_metadata')))
 else if(fixture.table==='config_column')assert(!narrow.requests.some(request=>request.url.endsWith('/control_metadata')))
 console.log('通过：真实组件完整Vue渲染、窄读取依赖及原实体事件合同 '+fixture.table)
} finally {full.app.unmount();narrow.app.unmount()}
