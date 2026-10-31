import test from 'node:test'
import assert from 'node:assert/strict'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'
import * as relationRuntime from '../common/yzp_runtime/relationReadValues.js'
const source=await readFile(new URL('../common/yzp_runtime/yzp-runtime.js',import.meta.url),'utf8')
/** 执行真实公共按钮处理器，隔离网络和页面宿主，验证刷新回执不会覆盖编辑实体。 */
function fixture(){
 const events=[],toasts=[],dialogs=[]
 const uni={$emit:(...args)=>events.push(args),showToast:options=>toasts.push(options),showModal:options=>dialogs.push(options),hideToast(){},navigateBack(){}}
 const context={...relationRuntime,uni,setTimeout:callback=>callback(),clearTimeout(){}}
 vm.runInNewContext(source.slice(source.indexOf('const TAB_BAR_PAGES')).replace('export default yzp;','globalThis.runtime=yzp;'),context)
 return {runtime:context.runtime,events,toasts,dialogs}
}
const command=action=>({code:200,data:{type:'metarillRefresh',projectId:'777',configuration:{action,pageId:'999',adminTypeId:'888',created:true}}})
test('复制和创建页面只执行原详情刷新，配置回执不污染当前实体',()=>{
 for(const action of ['copyPage','createPage']){
  const f=fixture(),entity={id:'原页面',name:'保留'},calls=[]
  const page={entity,getEntity:()=>calls.push('详情'),refreshSonsData:()=>calls.push('子表')}
  f.runtime.customButtonHandler(command(action),page,false,'detail')
  assert.equal(page.entity,entity);assert.deepEqual(calls,['详情','子表']);assert.equal(f.toasts.length,1);assert.equal(f.events.length,0)
 }
})
test('菜单创建保留列表刷新并发送现有菜单变更事件',()=>{
 const f=fixture(),entity={id:'原页面'};let refresh=0
 const page={entity,getList:()=>refresh++}
 f.runtime.customButtonHandler(command('createMenu'),page,false,'list')
 assert.equal(refresh,1);assert.equal(page.entity,entity)
 assert.equal(f.events[0][0],'onMenuConfigurationChanged');assert.equal(f.events[0][1].projectId,'777');assert.equal(f.events[0][1].adminTypeId,'888')
})
test('配置回执仍遵守原自动关闭设置，失败不刷新也不污染实体',()=>{
 const f=fixture(),entity={id:'原页面'},calls=[]
 const page={entity,emitDetailChange:()=>calls.push('保存事件'),closeDetail:()=>calls.push('关闭')}
 f.runtime.customButtonHandler(command('copyPage'),page,true,'detail');assert.deepEqual(calls,['保存事件','关闭']);assert.equal(page.entity,entity)
 f.runtime.customButtonHandler({code:500,msg:'项目不匹配'},page,false,'detail');assert.equal(page.entity,entity);assert.equal(f.dialogs[0].content,'项目不匹配')
})
test('既有普通实体响应仍保留原赋值行为',()=>{
 const f=fixture(),entity={id:'原'},saved={id:'新'};const page={entity,getEntity(){}}
 f.runtime.customButtonHandler({code:200,data:saved},page,false,'detail');assert.equal(page.entity,saved)
})
