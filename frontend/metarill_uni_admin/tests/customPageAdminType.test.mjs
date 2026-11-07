import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { mountGenerated } from '../../../backend/tests/Metarill.Generation.Tests/GeneratedOriginalVueHarness.mjs'
const detail=await readFile(new URL('../pages/yzp/config_custom_page-detail-2062361618137833473.vue',import.meta.url),'utf8')
const list=await readFile(new URL('../pages/yzp/config_custom_page-list-2062361618137833473.vue',import.meta.url),'utf8')
const field='2200000000000101010',endpoint='/admin/auto/config_custom_page/2062361618137833473/findNameIdYzAdminType'+field
/** 编译并挂载真实原详情及分页mixin，复用现有Vue宿主，不复制生产状态或替代候选控件。 */
function fixture(){return mountGenerated(detail,{}, {})}

test('自定义页后台List选择器保留搜索、ID/name回调和原组件接口',()=>{
 assert.match(detail,/<zxz-uni-data-select[^>]*v-model='entity.yzAdminTypeId'[^>]*filterable[^>]*dataKey='name'[^>]*dataValue='id'[^>]*@change='yz_admin_typeSelectChange'/)
 assert.match(detail,/readonlyDisplayText\(entity.yzAdminTypeName\)/);assert.doesNotMatch(detail,/<picker\b|<uni-data-select\b/)
 const f=fixture();try{
 assert.equal(f.state.entity.yzAdminTypeId,'');assert.equal(f.state.entity.yzAdminTypeName,'')
 f.state.yz_admin_typeSelectChange({id:'9007199254740993',name:'管理后台'});assert.equal(f.state.entity.yzAdminTypeId,'9007199254740993');assert.equal(f.state.entity.yzAdminTypeName,'管理后台')
 f.state.yz_admin_typeSelectChange(null);assert.equal(f.state.entity.yzAdminTypeId,'');assert.equal(f.state.entity.yzAdminTypeName,'')
 }finally{f.app.unmount()}
})

test('原自定义页候选按固定字段分页，旧formParameters不进入严格分页协议',async()=>{
 const f=fixture();try{
 f.state.initLists();assert.equal(f.requests.length,0)
 f.state.openOriginalRemoteOptions(field)
 const request=f.requests.at(-1);assert.equal(request.options.url,endpoint+'/page');assert.equal(request.options.method,'POST');assert.equal(request.options.sendSessionId,true);assert.equal(request.options.data.formParameters,undefined);assert.equal(request.options.data.pageNo,1)
 f.respond(request,{records:[{id:'1',value:'1',name:'后台',text:'后台'}],pages:'1'});assert.equal(f.state.yz_admin_type_list[0].name,'后台')
 f.state.searchOriginalRemoteOptions(field,'无权限');await new Promise(resolve=>setTimeout(resolve,330))
 const denied=f.requests.at(-1);denied.options.success({code:500,msg:'无权限'});denied.options.complete?.()
 assert.equal(f.state.yz_admin_type_list.length,0);assert.match(f.dialogs.at(-1).content,/候选读取失败/);assert.equal(f.state.entity.yzAdminTypeId,'')
 }finally{f.app.unmount()}
})

test('整页及列级只读仍展示服务端名称，不触发编辑候选分页',async()=>{
 const f=fixture();try{
 f.setProps({parentReadOnlyCondition:true});await f.flush();assert.equal(f.state.readOnlyCondition,true);f.state.initLists();f.state.openOriginalRemoteOptions(field);assert.equal(f.requests.length,0);assert.equal(f.state.yzAdminTypeIdReadOnly,true)
 f.setProps({parentReadOnlyCondition:false});await f.flush();f.state.entity_column_readonly.yzAdminTypeId=true;assert.equal(f.state.yzAdminTypeIdReadOnly,true)
 f.state.entity_column_readonly.yzAdminTypeId=false;assert.equal(f.state.yzAdminTypeIdReadOnly,false)
 }finally{f.app.unmount()}
})

test('PC与移动列表都展示后台名称，不用内部ID替代名称或额外开启筛选',()=>{
 assert.equal((list.match(/relationDisplayText\(item.yzAdminTypeName\)/g)||[]).length,2)
 assert.match(list,/<uni-th >所属后台<\/uni-th>/);assert.match(list,/<view class='row-item'><text>所属后台<\/text>/)
 assert.match(list,/page_is_filter: false/);assert.doesNotMatch(list,/item.yzAdminTypeId\s*}}/)
})
