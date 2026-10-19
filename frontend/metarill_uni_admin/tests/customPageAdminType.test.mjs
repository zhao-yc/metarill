import test from 'node:test'
import assert from 'node:assert/strict'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'
const detail=await readFile(new URL('../pages/yzp/config_custom_page-detail-2062361618137833473.vue',import.meta.url),'utf8')
const list=await readFile(new URL('../pages/yzp/config_custom_page-list-2062361618137833473.vue',import.meta.url),'utf8')
/** 运行实际详情选项，使用原回调对象格式验证候选加载、名称同步及只读边界。 */
function fixture(){
 const requests=[],dialogs=[],parameters={configCustomPage:{id:'已有页'}}
 const context={utils:{},project:{},yzp:{request:options=>requests.push(options)},uni:{getStorageSync:()=>parameters,showModal:options=>dialogs.push(options)}}
 const script=detail.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;\s*$/gm,'').replace('export default','globalThis.options=')
 vm.runInNewContext(script,context)
 const options=context.options,state=options.data();Object.assign(state,options.methods,{readOnlyCondition:false})
 return {options,state,requests,parameters,dialogs}
}
test('自定义页后台List选择器保留搜索、ID/name回调和原组件接口',()=>{
 assert.match(detail,/<zxz-uni-data-select[^>]*v-model='entity.yzAdminTypeId'[^>]*filterable[^>]*dataKey='name'[^>]*dataValue='id'[^>]*@change='yz_admin_typeSelectChange'/)
 assert.match(detail,/readonlyDisplayText\(entity.yzAdminTypeName\)/);assert.doesNotMatch(detail,/<picker\b|<uni-data-select\b/)
 const {state}=fixture();assert.equal(state.entity.yzAdminTypeId,'');assert.equal(state.entity.yzAdminTypeName,'')
 state.yz_admin_typeSelectChange({id:'后台ID',name:'管理后台'});assert.equal(state.entity.yzAdminTypeId,'后台ID');assert.equal(state.entity.yzAdminTypeName,'管理后台')
 state.yz_admin_typeSelectChange(null);assert.equal(state.entity.yzAdminTypeId,'');assert.equal(state.entity.yzAdminTypeName,'')
})
test('候选请求使用新生成字段ID并保留原formParameters协议',()=>{
 const {state,requests,parameters,dialogs}=fixture();state.initLists();assert.equal(requests.length,1)
 const request=requests[0];assert.equal(request.url,'/admin/auto/config_custom_page/2062361618137833473/findNameIdYzAdminType2200000000000101010');assert.equal(request.method,'POST');assert.equal(request.sendSessionId,true);assert.equal(request.data.formParameters,parameters)
 const rows=[{id:'1',name:'后台'}];request.success({code:200,data:rows});assert.equal(state.yz_admin_type_list,rows)
 request.success({code:200,data:null});assert.equal(state.yz_admin_type_list.length,0);request.success({code:500,msg:'无权限'});assert.equal(dialogs[0].content,'无权限')
})
test('整页及列级只读仍展示服务端名称，不触发编辑候选加载',()=>{
 const {options,state,requests}=fixture();state.readOnlyCondition=true;state.initLists();assert.equal(requests.length,0)
 assert.equal(options.computed.yzAdminTypeIdReadOnly.call(state),true)
 state.readOnlyCondition=false;state.entity_column_readonly.yzAdminTypeId=true;assert.equal(options.computed.yzAdminTypeIdReadOnly.call(state),true)
 state.entity_column_readonly.yzAdminTypeId=false;assert.equal(options.computed.yzAdminTypeIdReadOnly.call(state),false)
})
test('PC与移动列表都展示后台名称，不用内部ID替代名称或额外开启筛选',()=>{
 assert.equal((list.match(/relationDisplayText\(item.yzAdminTypeName\)/g)||[]).length,2)
 assert.match(list,/<uni-th >所属后台<\/uni-th>/);assert.match(list,/<view class='row-item'><text>所属后台<\/text>/)
 assert.match(list,/page_is_filter: false/);assert.doesNotMatch(list,/item.yzAdminTypeId\s*}}/)
})
