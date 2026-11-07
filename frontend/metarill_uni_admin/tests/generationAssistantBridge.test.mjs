import {createHash} from 'node:crypto'
import test from 'node:test'
import assert from 'node:assert/strict'
import vm from 'node:vm'
import {readFile} from 'node:fs/promises'
import {createGenerationAssistantBridge,generationAssistantActions} from '../common/generationAssistantBridge.js'

const binding='11111111-1111-4111-8111-111111111111', id='22222222-2222-4222-8222-222222222222'
const turn=async()=>{for(let i=0;i<8;i++) await Promise.resolve()}
function view(action='sync-runtime', status='queued', extra={}) {return {requestId:id,bindingId:binding,projectId:'777',action,status,requestSha256:'a'.repeat(64),applied:status==='applied',outcome:status==='applied'?'applied':'not_applied',...extra}}
/** 执行真实共享handler并手动调度真实回调，传输合约替身不冒充浏览器验收。 */
function fixture(mode='apply') {
 const storage={loginStateId:'只在现有会话缓存',sysParams:{yzProjectId:'777'}},requests=[],dialogs=[],errors=[],toasts=[],timers=[]; let current=true
 const uni={getStorageSync:key=>storage[key],setStorageSync:(key,value)=>storage[key]=value,showModal:value=>dialogs.push(value),showToast:value=>toasts.push(value),showActionSheet:value=>value.success({tapIndex:0})}
 const yzp={request:value=>requests.push(value),showErrorModal:value=>errors.push(value)}
 const options={uni,yzp,projectId:'777',isCurrent:()=>current,schedule:callback=>{timers.push(callback);return timers.length},cancelSchedule:()=>{},newRequestId:()=>id}
 const helper=createGenerationAssistantBridge(options)
 return {storage,requests,dialogs,errors,toasts,timers,uni,yzp,options,helper,leave:()=>current=false,bindings:()=>requests.at(-1).success({code:200,data:[{bindingId:binding,projectId:'777',rootNamespace:'Example.Assistant',mode,allowedActions:mode==='apply'?Object.keys(generationAssistantActions):[]} ]})}
}

test('原三个按钮保留原确认和各自GET，仅应用回执显示完成',async()=>{
 const source=await readFile(new URL('../pages/main/main.vue',import.meta.url),'utf8')
 for(const [method,action] of [['generateProjectConfig','generate-project-config'],['syncRuntime','sync-runtime'],['generateAllView','generate-all-page']]) {
  const f=fixture(); const context={uni:f.uni,yzp:f.yzp,createGenerationAssistantBridge:options=>createGenerationAssistantBridge({...options,schedule:f.options.schedule,cancelSchedule:f.options.cancelSchedule,newRequestId:()=>id})}
  vm.runInNewContext(source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;\s*$/gm,'').replace('export default','globalThis.component='),context)
  const component=Object.assign(context.component.data(),context.component.methods,{homeVisible:true}); component[method](); assert.equal(f.requests.length,0); assert.match(f.dialogs[0].title,/确定/); f.dialogs[0].success({confirm:false}); assert.equal(f.requests.length,0); f.dialogs[0].success({confirm:true}); assert.equal(f.requests[0].url,'/admin/sys/generation-assistant/bindings'); f.bindings(); await turn()
  const queued=f.requests.at(-1); assert.equal(queued.url,generationAssistantActions[action]); assert.equal(queued.method,'GET'); assert.equal(queued.header['X-Yzp-Agent-Confirm'],'true'); assert.equal(queued.header['X-Metarill-Assistant-Binding'],binding); assert.equal(queued.header['X-Yzp-Agent-Request-Id'],id)
  queued.success({code:200,data:view(action)}); await turn(); assert.equal(f.toasts.filter(item=>item.icon==='success').length,0); f.timers.shift()(); await turn(); assert.equal(f.requests.at(-1).url,'/admin/sys/generation-assistant/requests/'+id); f.requests.at(-1).success({code:200,data:view(action,'applied')}); await turn(); assert.equal(f.toasts.filter(item=>item.icon==='success').length,1); assert.match(f.toasts.find(item=>item.icon==='success').title,/完成/); assert.ok(!JSON.stringify(f.storage['metarill-generation-pending-v1']).includes('只在现有会话缓存'))
 }
})

test('只预览绑定明确无法写入；缺依赖提示原按钮补做而非扩大all',async()=>{
 for(const mode of ['preview','apply']) { const f=fixture(mode); f.helper.start('sync-runtime'); f.bindings(); await turn(); f.requests.at(-1).success({code:200,data:view('sync-runtime','dependencies_required',{preview:{requiredActions:['generate-project-config']}})}); await turn(); assert.equal(f.toasts.filter(item=>item.icon==='success').length,0); assert.match(f.dialogs[0].content,mode==='preview'?/无法写入/:/原按钮补做/); assert.equal(f.requests.filter(item=>item.url===generationAssistantActions['generate-project-config']).length,0) }
})

test('响应丢失只查询已登记UUID，首页恢复不重发三个生成GET',async()=>{
 const f=fixture(); f.helper.start('sync-runtime'); f.bindings(); await turn(); const original=f.requests.at(-1); original.fail(); await turn(); assert.equal(f.requests.filter(item=>item.url===generationAssistantActions['sync-runtime']).length,1); assert.equal(f.requests.at(-1).url,'/admin/sys/generation-assistant/requests/'+id)
 f.requests.at(-1).success({code:200,data:view('sync-runtime','applied')}); await turn(); assert.equal(f.toasts.filter(item=>item.icon==='success').length,1)
 f.storage['metarill-generation-pending-v1']=[{requestId:id,bindingId:binding,projectId:'777',action:'sync-runtime',mode:'apply'}]; f.helper.stop(); const next=createGenerationAssistantBridge(f.options); next.resume(); await turn(); assert.equal(f.requests.at(-1).url,'/admin/sys/generation-assistant/requests/'+id); assert.equal(f.requests.filter(item=>item.url===generationAssistantActions['sync-runtime']).length,1)
})

test('离开当前项目后迟到回执不显示成功，错误项目和伪applied拒绝',async()=>{
 for(const scenario of ['stale','project','fake']) { const f=fixture(); f.helper.start('sync-runtime'); f.bindings(); await turn(); const response=f.requests.at(-1); if(scenario==='stale')f.leave(); response.success({code:200,data:view('sync-runtime','applied',scenario==='project'?{projectId:'778'}:scenario==='fake'?{applied:false}:{})}); await turn(); assert.equal(f.toasts.filter(item=>item.icon==='success').length,0); if(scenario!=='stale') assert.equal(f.errors.length,1) }
})

test('空绑定、未知状态和BackendOnly失败都不能显示生成成功或要求日常手动JSON',async()=>{
 const f=fixture(); f.helper.start('sync-runtime'); f.requests[0].success({code:200,data:[]}); await turn(); assert.match(f.errors[0],/一次|先完成/); assert.equal(f.requests.length,1); assert.equal(f.toasts.filter(item=>item.icon==='success').length,0)
 const second=fixture(); second.helper.start('sync-runtime'); second.bindings(); await turn(); second.requests.at(-1).success({code:500,msg:'平台BackendOnly未写入源码'}); await turn(); assert.equal(second.toasts.filter(item=>item.icon==='success').length,0); assert.match(second.errors[0],/BackendOnly/); assert.equal(second.requests.at(-1).url,'/admin/sys/generation-assistant/requests/'+id)
})

/** 在线助手只改原按钮必要handler，原YZP首页DOM/CSS保持e6fe956冻结原字节。 */
test('在线接线保持原首页模板和样式逐字节不变',async()=>{
 const source=await readFile(new URL('../pages/main/main.vue',import.meta.url),'utf8'); const digest=text=>createHash('sha256').update(text).digest('hex');
 assert.equal(digest(source.slice(source.indexOf('<template>'),source.indexOf('</template>')+'</template>'.length)),'1ef8c7d17c6be09567640af5c08e16a77ed7b91793bf05224591dc9fbf277379');
 assert.equal(digest(source.slice(source.indexOf('<style'))),'37c10b5c6ecdca8436199becfe769590384a71153d059a8a91c8dc3ee07ef7b5');
});

/** HBuilderX按uni.方法按需导入；对象简写uni不会被转换，须验证桥接不依赖空全局对象。 */
test('首页在HBuilderX按需API编译后仍可恢复和投递原按钮',async()=>{
 const source=await readFile(new URL('../pages/main/main.vue',import.meta.url),'utf8')
 for(const [method,action] of [['generateProjectConfig','generate-project-config'],['syncRuntime','sync-runtime'],['generateAllView','generate-all-page']]) {
  const f=fixture()
  const context={uni:{},compiledUni:f.uni,yzp:f.yzp,createGenerationAssistantBridge:options=>createGenerationAssistantBridge({...options,schedule:f.options.schedule,cancelSchedule:f.options.cancelSchedule,newRequestId:()=>id})}
  const script=source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;\s*$/gm,'').replace('export default','globalThis.component=').replace(/\buni\.(getStorageSync|setStorageSync|showModal|showToast|showActionSheet)\b/g,'compiledUni.$1')
  vm.runInNewContext(script,context)
  const component=Object.assign(context.component.data(),context.component.methods,{homeVisible:true})
  assert.doesNotThrow(()=>component.enterHome())
  component[method]();f.dialogs.at(-1).success({confirm:true});f.bindings();await turn()
  assert.equal(f.requests.at(-1).url,generationAssistantActions[action])
  f.requests.at(-1).success({code:200,data:view(action,'applied')});await turn()
  assert.equal(f.errors.length,0);assert.equal(f.toasts.filter(item=>item.icon==='success').length,1)
 }
})
