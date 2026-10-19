import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { createRequire } from 'node:module'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url))
const {createRenderer,h,nextTick,compile,reactive}=require('vue')
const {parse}=require('@vue/compiler-sfc')
const scenario=await readFile(join(process.argv[2],'scenario.txt'),'utf8').catch(()=> 'standard')
const multiSource=await readFile(new URL('../../../frontend/metarill_uni_admin/common/yzp_runtime/multiSelectFieldMixin.js',import.meta.url),'utf8')
const source=await readFile(join(process.argv[2],'detail.vue'),'utf8')
const {descriptor,errors}=parse(source);assert.deepEqual(errors,[])
const layoutSource=await readFile(new URL('../../../frontend/metarill_uni_admin/common/yzp_runtime/detailLayoutRuntime.js',import.meta.url),'utf8')
const pageId='2200000000000100020',id='9007199254740993',plain=value=>JSON.parse(JSON.stringify(value))
const element=(tag,text='')=>({tag,text,props:{},style:{},addEventListener(){},removeEventListener(){},setAttribute(){},removeAttribute(){},parent:null,children:[]})
/** 真实Vue3挂载原模板，网络与表单结果通过组件公开协议控制，不宣称浏览器或后端已验收。 */
function fixture(loadOptions={id,parent_id:'9007199254742000'}){
 const requests=[],modals=[],navigations=[],events=[],storage={loginStateId:'当前会话',sysParams:{yzProjectId:'2200000000000100000'}};let valid=true,permission=true,validations=0,backs=0,childRefreshes=0
 const uni={getStorageSync:key=>storage[key],setStorageSync:(key,value)=>storage[key]=value,$on(){},$off(){},$emit:(...args)=>events.push(args),navigateTo:options=>navigations.push(options),showModal:options=>modals.push(options),showToast(){},showActionSheet:options=>modals.push(options),navigateBack(){backs++}}
 const yzp={back(){backs++},refreshCurrentPermissions:done=>done(),hasPagePermission:()=>permission,isColumnVisible:()=>true,isButtonVisible:()=>permission,relationArray:value=>value==null||value===''?[]:typeof value==='string'?JSON.parse(value):value,request:options=>{requests.push(options);return{abort(){}}},uploadSelectedFiles(e,options,done){done([{url:'测试上传结果'}])},customButtonHandler(){}}
 const utils={isBlank:value=>value===null||value===undefined||value===''}
 const createDetailLayoutMixin=new Function('uni',layoutSource.replace('export function','function')+';return createDetailLayoutMixin')(uni)
 const createMultiSelectFieldMixin=new Function('uni',multiSource.replace('export function','function')+';return createMultiSelectFieldMixin')(uni)
 const linked=Object.fromEntries([...descriptor.script.content.matchAll(/^import (\w+) from [^\n]*pages\/yzp[^\n]*$/gm)].map(match=>[match[1],{props:['parent_id','parentReadOnlyCondition','parent_page_sn','is_son_page','child_page_open_type','selectedIds','selectedNames','selectedNameField'],emits:['onPopSelect','onPopClose'],methods:{refreshData(){childRefreshes++}},render(){return h('linked-page')}}]))
 const script=descriptor.script.content.replace(/^import[^\n]*\n/gm,'').replace('export default','return')
 const options=new Function('utils','yzp','project','uni','getApp','createDetailLayoutMixin','createMultiSelectFieldMixin',...Object.keys(linked),script)(utils,yzp,{},uni,()=>({globalData:{readOnly:false}}),createDetailLayoutMixin,createMultiSelectFieldMixin,...Object.values(linked))
 options.render=compile(descriptor.template.content)
 const renderer=createRenderer({createElement:tag=>element(tag),createText:text=>element('text',text),createComment:text=>element('comment',text),setElementText(node,text){node.text=text;node.children=[]},setText(node,text){node.text=text},patchProp(node,key,old,value){node.props[key]=value},insert(node,parent,anchor){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1);const index=anchor?parent.children.indexOf(anchor):-1;parent.children.splice(index<0?parent.children.length:index,0,node);node.parent=parent},remove(node){if(node.parent)node.parent.children.splice(node.parent.children.indexOf(node),1)},parentNode:node=>node.parent,nextSibling:node=>node.parent?.children[node.parent.children.indexOf(node)+1]||null})
 const Stub={render(){return h('view',{},this.$slots.default?.())}}
 const inputProps=reactive({parentReadOnlyCondition:false});const app=renderer.createApp({render(){return h(options,{...inputProps,ref:'detail'})}});for(const name of ['view','text','uni-nav-bar','uni-forms-item','uni-easyinput','uni-data-checkbox','zxz-uni-data-select','uni-datetime-picker','uni-file-picker','yzp-file-library','yzp-cascade-tree','yzp-original-custom','scroll-view','picker'])app.component(name,Stub)
 app.component('yzp-popup',{props:['type'],methods:{open(){},close(){}},render(){return h('popup',{},this.$slots.default?.())}})
 app.component('uni-forms',{props:['modelValue','rules'],methods:{validate(){validations++;return valid?Promise.resolve({}):Promise.reject([{key:'notes'}])}},render(){return h('form',{},this.$slots.default?.())}})
 app.config.globalProperties.$deviceInfo={is_pc:true};app.config.globalProperties.$checkboxRange=[{value:0,text:'否'},{value:1,text:'是'}];app.config.globalProperties.$imageStyles={};app.config.globalProperties.$multiCheckboxRange=[{value:1,text:'全选'}];app.config.globalProperties.$yzp=yzp
 app.config.warnHandler=message=>{if(!/Failed to resolve directive: html/.test(message))throw new Error(message)}
 const tree=element('root'),host=app.mount(tree),state=host.$refs.detail;state.getOpenerEventChannel=()=>({emit:(...args)=>events.push(args)});options.onLoad.call(state,loadOptions);
 const loads=()=>requests.filter(request=>request.url.includes('/detail/'))
 const respond=(request,data)=>{request.success({code:200,data});request.complete?.({})}
 const row={id,name:'原记录',notes:null,amount:0,isEnabled:0,rtf:null,constant:'a',constantShow:'甲',dropdown:'b',dropdownShow:'乙',relationId:null,readOnlyEcho:'服务端只读回显',protectedFiles:null}
 return{state,app,tree,requests,modals,navigations,events,storage,options,row,loads,respond,setValid:value=>valid=value,setPermission:value=>permission=value,setParentReadonly:value=>inputProps.parentReadOnlyCondition=value,validations:()=>validations,backs:()=>backs,childRefreshes:()=>childRefreshes,async loaded(){respond(loads().at(-1),plain(row));await new Promise(resolve=>setTimeout(resolve,310));await nextTick()},async flush(){await nextTick();await Promise.resolve();await nextTick()}}
}
if(scenario!=='selection'){
{
 const f=fixture();try{
 await f.loaded();assert.equal(f.state.originalChildrenReady,true);assert.equal(f.state.entity.rtf,null);assert.equal(f.state.entity.protectedFiles,null);f.state.receiveCascadeTree516({cascadeId:'9007199254746000',cascadeName:'原分类'});assert.equal(f.state.entity.cascadeId,'9007199254746000');assert.equal(f.state.entity.cascadeName,'原分类')
 f.state.saveEntity();await f.flush();const save=f.requests.find(request=>request.url.includes('/update?'));assert(save);assert.strictEqual(save.data,f.state.entity);assert.equal(save.data.readOnlyEcho,'服务端只读回显');assert.equal(save.data.notes,null);assert.equal(save.data.rtf,null);assert.equal(save.data.amount,0);assert.equal(save.data.isEnabled,0);assert.equal(save.data.id,id);assert.equal(save.data.yzProjectId,undefined);assert.equal(save.data.parentId,undefined);assert.match(save.url,/parentId=9007199254742000/);assert.equal(f.validations(),1)
 f.respond(save,{...f.row,notes:'保存后'});assert.equal(f.state.saving,false)
 f.state.rtfRtfDo('edit');const editor=f.navigations.at(-1);editor.events.onReceiveRtf('');assert.equal(f.state.entity.rtf,null);editor.events.onReceiveRtf('<p>修改</p>');assert.equal(f.state.entity.rtf,'<p>修改</p>')
 f.state.rtfRtfDo('edit');const late=f.navigations.at(-1);f.storage.sysParams={yzProjectId:'其他项目'};late.events.onReceiveRtf('迟到');assert.equal(f.state.entity.rtf,'<p>修改</p>')
 }finally{f.app.unmount()}
 console.log('通过：原单表单validate与整entity请求、只读回显/null/0、父键、未编辑RTF及项目迟到隔离')
}
{
 const f=fixture();try{await f.loaded();const count=f.requests.length;f.setValid(false);f.state.saveEntity();await f.flush();assert.equal(f.requests.length,count);assert.equal(f.state.activeDetailGroupKey,'other');f.setValid(true);f.state.customerButtonHandler900();assert.equal(f.modals.at(-1).content,'原确认提示');assert.equal(f.requests.length,count);f.modals.at(-1).success({confirm:true});const action=f.requests.at(-1);assert.equal(action.url,'/admin/sys/config_button/execute');assert.equal(action.data.buttonId,'900');assert.strictEqual(action.data.entity,f.state.entity)
 f.state.customerButtonHandler900();const modal=f.modals.at(-1);f.setPermission(false);modal.success({confirm:true});assert.equal(f.requests.at(-1),action)
 }finally{f.app.unmount()}
 console.log('通过：原校验失败不写/跳到错误分组，确认按钮、整entity动作和权限变化阻断迟到确认')
}
{
 const f=fixture();try{const first=f.loads()[0];f.options.onLoad.call(f.state,{id:'9007199254740994'});const second=f.loads().at(-1);f.respond(first,{id,name:'旧记录'});assert.notEqual(f.state.entity.name,'旧记录');f.respond(second,{id:'9007199254740994',name:'当前记录'});assert.equal(f.state.entity.name,'当前记录');f.app.unmount();f.respond(second,{id:'9007199254740994',name:'卸载迟到'});assert.equal(f.state.entity.name,'当前记录')
 }finally{if(f.state._alive)f.app.unmount()}
 console.log('通过：原props/页面初始化生命周期中换记录和卸载后的旧详情响应不会回填')
}

{
 const f=fixture({id:''});try{assert.equal(f.state.originalChildrenReady,false);f.state.page_is_save=false;f.state.entity.name='新建原记录';f.state.customerButtonHandler901();await f.flush();const add=f.requests.find(request=>request.url.includes('/add'));assert(add);assert.strictEqual(add.data,f.state.entity);f.respond(add,{...f.row,id:'9007199254740998',name:'新建保存后'});const action=f.requests.at(-1);assert.equal(action.url,'/admin/sys/config_button/execute');assert.equal(action.data.entityId,'9007199254740998');assert.strictEqual(action.data.entity,f.state.entity);assert.equal(f.state.id,'9007199254740998');assert.equal(f.state.originalChildrenReady,true)
 }finally{f.app.unmount()}
 console.log('通过：新建先保存再执行动作使用服务端真实ID、原整entity和原validate，不发送空ID动作')
}
}
if(scenario==='children'){
 const f=fixture({id:''});try{
 await f.flush();assert.equal(f.state.$refs.ChildList88==null,true);assert.equal(f.state.originalChildrenReady,false);const writes=f.requests.filter(request=>request.method==='POST').length;f.state.refreshSonsData();assert.equal(f.childRefreshes(),0);assert.equal(f.requests.filter(request=>request.method==='POST').length,writes)
 f.state.id=id;f.state.entity={...f.row};f.state.markOriginalRecordReady();await f.flush();const child=f.state.$refs.ChildList88;assert(child);assert.equal(child.parent_id,id);assert.equal(child.parent_page_sn,pageId);assert.equal(child.child_page_open_type,2);assert.equal(child.is_son_page,true);f.state.refreshSonsData();assert.equal(f.childRefreshes(),1);assert.equal(f.requests.filter(request=>request.method==='POST').length,writes)
 f.setParentReadonly(true);await f.flush();assert.equal(child.parentReadOnlyCondition,true);f.state.initPage({id:'9007199254740999',parent_id:'9007199254742000'});await f.flush();assert.equal(f.state.$refs.ChildList88==null,true)
 }finally{f.app.unmount()}
 console.log('通过：保存前不挂载子页，真实VO开放原子组件props，子刷新不产生父写，父只读及记录切换继承')
}
if(scenario==='selection'){
 const f=fixture({id,parent_id:'9007199254742000'});try{
 f.respond(f.loads().at(-1),{id,name:'多选原记录',targetIds:['9007199254744000'],targetNames:['原名称'],singleId:null,singleName:null});await f.flush();const initial=plain(f.state.entity),requests=f.requests.length
 f.state.openSelect25();await f.flush();assert.equal(f.state.multiSelectionField,'targetIds');assert.equal(f.state.multiTargetParent('targetIds'),'9007199254742000');const component=f.state.$refs['25'];assert(component);assert.deepEqual(plain(component.selectedIds),initial.targetIds);assert.equal(component.selectedNameField,'name');assert.equal(component.parent_id,'9007199254742000')
 f.state.onPopClose25();assert.deepEqual(plain(f.state.entity),initial);f.state.onPopSelect25([{id:'9007199254744001',name:'取消后的迟到'}]);assert.deepEqual(plain(f.state.entity),initial)
 f.state.openSelect25();await f.flush();f.state.onPopSelect25([{id:'9007199254744001'}]);assert.deepEqual(plain(f.state.entity),initial);assert(f.state.multiSelectionError)
 f.state.onPopSelect25([{id:'9007199254744001',name:'完整名称'},{id:'9007199254744002',name:null}]);assert.deepEqual(plain(f.state.entity.targetIds),['9007199254744001','9007199254744002']);assert.deepEqual(plain(f.state.entity.targetNames),['完整名称',null]);assert.equal(f.requests.length,requests);assert.equal(f.state.multiSelectionField,'')
 f.state.openSelect25();await f.flush();f.state.parent_id='9007199254742001';f.state.onPopSelect25([]);assert.equal(f.state.entity.targetIds.length,2);f.state.onPopClose25()
 f.state.openSelect28();await f.flush();f.state.onPopSelect28({id:'9007199254745001',name:'原单选名称'});assert.equal(f.state.entity.singleId,'9007199254745001');assert.equal(f.state.entity.singleName,'原单选名称');f.state.onPopClose28();f.state.onPopSelect28({id:'9007199254745002',name:'单选迟到'});assert.equal(f.state.entity.singleId,'9007199254745001')
 f.state.openSelect25();await f.flush();f.setParentReadonly(true);await f.flush();f.state.onPopSelect25([]);assert.equal(f.state.entity.targetIds.length,2);f.state.onPopClose25();f.state.openSelect28();assert.equal(f.state.originalSelection28,false)
 }finally{f.app.unmount()}
 console.log('通过：原选择页props/events、长ID、取消不写、完整row[]、父切换/父只读与迟到确认隔离')
}
