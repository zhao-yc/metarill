import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRenderer, h, nextTick } from 'vue'
const source=await readFile(new URL('../common/yzp_runtime/originalListOptionsMixin.js',import.meta.url),'utf8')
/** 真实Vue实例验证原数组选项回执，不复制生产请求隔离逻辑。 */
function fixture(targetParentFields=[],checkboxOptionScopes=[],optionFields=["options"]) {
 const requests=[],events=new Map(),storage={loginStateId:'会话甲',sysParams:{yzProjectId:'1'}}
 const uni={getStorageSync:key=>storage[key],$on(name,fn){if(!events.has(name))events.set(name,new Set());events.get(name).add(fn)},$off(name,fn){events.get(name)?.delete(fn)}}
 const yzp={request(options){const request={options,aborted:false};requests.push(request);return{abort(){request.aborted=true;options.fail?.({errMsg:'已取消'});options.complete?.()}}}}
 const factory=new Function('uni','yzp',source.replace(/^import .*;\s*$/gm,'').replace('export function','function')+';return createOriginalListOptionsMixin')(uni,yzp)
 const node=()=>({children:[],parent:null}),renderer=createRenderer({createElement:node,createText:node,createComment:node,setElementText(){},setText(){},patchProp(){},insert(n,p){n.parent=p;p.children.push(n)},remove(n){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1)},parentNode:n=>n.parent,nextSibling:()=>null})
 const app=renderer.createApp({mixins:[factory({targetParentFields,optionFields,checkboxOptionScopes})],data(){return{filters:{parentId:"10"},parent_id:'100',is_son_page:true,list:[],options:[],initializations:0,refreshes:0,targetIdsRange:[],target__list:[]}},methods:{initLists(){this.initializations++},refreshData(){this.refreshes++},load(isCurrent){return this.originalOptionRequest({isCurrent,url:'/固定来源页/findNameIdTarget1',success:res=>{this.options=res.data}})}},render(){return h('view')}})
 return{app,vm:app.mount(node()),requests,storage,events,emit(name){for(const fn of events.get(name)||[])fn()}}
}
test('原数组端点只接收最新请求，取消同步回调不回填旧候选',async()=>{
 const f=fixture();try{f.vm.load();const first=f.requests[0];f.vm.load();assert.equal(first.aborted,true);first.options.success({data:['旧值']});assert.deepEqual([...f.vm.options],[]);f.requests[1].options.success({data:['新值']});await nextTick();assert.deepEqual([...f.vm.options],['新值'])}finally{f.app.unmount()}
})
test('换父在watch前即拒绝迟到候选，watch后重新初始化原选项',async()=>{
 const f=fixture();try{f.vm.load();f.vm.parent_id='200';f.requests[0].options.success({data:['前一父记录']});assert.deepEqual([...f.vm.options],[]);await nextTick();assert.equal(f.requests[0].aborted,true);assert.equal(f.vm.initializations,1);f.vm.load();f.requests[1].options.success({data:['当前父记录']});assert.deepEqual([...f.vm.options],['当前父记录'])}finally{f.app.unmount()}
})
test('身份先变化仍拒旧回执，退出只清理而有会话登录才重新加载',async()=>{
 const f=fixture();try{f.vm.load();f.storage.loginStateId='会话乙';f.requests[0].options.success({data:['旧会话']});assert.deepEqual([...f.vm.options],[]);f.emit('onLogout');assert.equal(f.vm.initializations,0);assert.equal(f.vm.refreshes,0);assert.equal(f.requests[0].aborted,true);assert.deepEqual([...f.vm.options],[]);f.emit('onLogin');assert.equal(f.vm.initializations,1);assert.equal(f.vm.refreshes,1)}finally{f.app.unmount()}
})
test('卸载只清理本实例任务和订阅，所有迟到success均无效',()=>{
 const first=fixture(),second=fixture();first.vm.load();second.vm.load();first.app.unmount();first.requests[0].options.success({data:['卸载后']});assert.deepEqual([...first.vm.options],[]);for(const set of first.events.values())assert.equal(set.size,0);assert.equal(second.requests[0].aborted,false);second.app.unmount()
})

/** 来源父/身份不变时，固定目标父快照也必须独立失效。 */
test('原候选组合调用方固定范围谓词，目标父同步watch清理并重新初始化',()=>{
 const f=fixture(['parentId']);try{const parent=f.vm.filters.parentId;f.vm.load(()=>parent===f.vm.filters.parentId);f.vm.filters.parentId='20';assert.equal(f.vm.initializations,1);assert.equal(f.requests[0].aborted,true);f.requests[0].options.success({data:['甲父旧值']});assert.deepEqual([...f.vm.options],[]);let current=true;f.vm.load(()=>current);current=false;f.requests[1].options.success({data:['调用方已失效']});assert.deepEqual([...f.vm.options],[])}finally{f.app.unmount()}
})


test('独立父字段使用原复选 parentId，并在换父时隔离原下拉与显式复选候选',()=>{
 for(const optionField of [undefined,'target__list']){
  const range=optionField??'targetIdsRange'
  const f=fixture([],[{field:'targetIds',parentScoped:false,formFields:['parentId']}],[range])
  try{
   const definition={field:'targetIds',url:'/固定来源页/findNameIdTarget1',formParentField:'parentId',formFields:[],limit:5000,optionField}
   f.vm.filters.parentId='9007199254740992'
   f.vm.filters.targetIds=['8']
   f.vm.loadOriginalArrayCheckboxOptions(definition)
   assert.deepEqual(f.requests[0].options.data,{parentId:'9007199254740992'})
   f.vm.filters.parentId='9007199254740993'
   assert.equal(f.vm.filters.targetIds,undefined)
   assert.equal(f.requests[0].aborted,true)
   const item={id:'8',name:'选项',value:'8',text:'选项'}
   f.requests[0].options.success({code:200,data:[item]})
   assert.deepEqual([...f.vm[range]],[])
   f.vm.loadOriginalArrayCheckboxOptions(definition)
   assert.deepEqual(f.requests[1].options.data,{parentId:'9007199254740993'})
   f.requests[1].options.success({code:200,data:[item]})
   assert.deepEqual([...f.vm[range]],optionField?[item]:[{text:'选项',value:'8'}])
   f.vm.filters.parentId=''
   f.vm.loadOriginalArrayCheckboxOptions(definition)
   assert.equal(f.requests.length,2)
   assert.deepEqual([...f.vm[range]],[])
  }finally{f.app.unmount()}
 }
})
