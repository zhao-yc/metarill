import test from 'node:test';import assert from 'node:assert/strict';import { mkdtemp,mkdir,readFile,writeFile,rm,lstat } from 'node:fs/promises';import { tmpdir } from 'node:os';import { join,resolve,sep } from 'node:path';import { randomUUID } from 'node:crypto';
import { bindGenerationDevice,resumeGenerationDevice,generationDeviceStatus,verifyGenerationRuntimeDevice } from '../lib/generation-device.mjs';import { deviceProjectPathFingerprint } from '../lib/device-project-path.mjs';import { CliError } from '../lib/errors.mjs';
/** 受控RPC/CLI只验证设备意图状态机；真实C#CLI、HTTP与OAuth另验，不作本用例的隐含前提。 */
async function fixture(t){
 const root=await mkdtemp(join(tmpdir(),'metarill-device-intent-'));const work=join(root,'work');await mkdir(work);
 const binding={bindingId:randomUUID(),localBindingSha256:'1'.repeat(64),accountId:'1',projectId:'2',grantId:'9',apiBaseUrl:'https://test.invalid/api',mode:'apply',allowedActions:['sync-runtime'],target:join(root,'target'),workRoot:work,rootNamespace:'Example.Device',databaseProvider:'mysql'};
 const current={deviceId:'11',projectId:'2',deviceSha256:'2'.repeat(64),configurationSha256:'3'.repeat(64),parts:{backend:true,frontend:true},backendPathSha256:deviceProjectPathFingerprint(binding.target),frontendPathSha256:deviceProjectPathFingerprint(join(binding.target,'frontend'))};
 const state={current,association:null,calls:[],cli:[],failBefore:false,failAfter:false,protocol:2,identityChanged:false};
 const context={timeoutMs:1000,identity:async()=>({changed:state.identityChanged}),rpc:async(path,body)=>{
  state.calls.push({path,body:structuredClone(body)});
  if(path.endsWith('/device-configuration'))return structuredClone(state.current);
  if(path.endsWith('/device-binding'))return structuredClone(state.association);
  if(!path.endsWith('/bind-device'))throw new Error('未登记RPC');
  if(state.failBefore)throw new CliError('合成网络未知','network_error');
  if(body.expectedDeviceSha256!==state.current.deviceSha256||body.expectedConfigurationSha256!==state.current.configurationSha256)throw new CliError('合成配置已变化','remote_error');
  if(state.association&&(state.association.bindingId!==body.bindingId||!state.association.matchesCurrentConfiguration)&&body.expectedAssociationSha256!==state.association.associationSha256)throw new CliError('合成旧摘要冲突','remote_error');
  state.association={deviceId:body.deviceId,projectId:'2',bindingId:body.bindingId,associationSha256:'a'.repeat(64),matchesCurrentConfiguration:true,deviceSha256:state.current.deviceSha256,configurationSha256:state.current.configurationSha256};
  if(state.failAfter)throw new CliError('合成回执未知','network_error');return structuredClone(state.association);
 }};
 const io={sameIdentity:(_binding,value)=>{if(value.changed)throw new CliError('合成身份变化','helper_identity_changed')},safePath:async path=>assert.ok(resolve(path).startsWith(resolve(work)+sep)),atomicJson:async(path,value)=>writeFile(path,JSON.stringify(value)),runCli:async(_binding,args)=>{
  state.cli.push(args);if(args[1]==='capabilities')return{value:{schemaVersion:1,requestSchemaVersions:[1,2],planSchemaVersions:[1,2],runtimePartsProtocol:state.protocol}};
  assert.equal(args[1],'target-identity');return{value:{projectId:binding.projectId,rootNamespace:binding.rootNamespace,databaseProvider:binding.databaseProvider}};
 }};
 const options={'device-id':'11',confirm:true};const posts=()=>state.calls.filter(item=>item.path.endsWith('/bind-device'));
 t.after(async()=>{assert.ok(resolve(root).startsWith(resolve(tmpdir())+sep));await rm(root,{recursive:true})});return{root,binding,state,context,io,options,posts,intent:join(work,'device-binding.json')};
}
test('设备意图只存固定ID/摘要，三元绑定保持且未应用源码',async t=>{
 const f=await fixture(t);const original=structuredClone(f.binding);const result=await bindGenerationDevice(f.context,f.binding,f.options,f.io);assert.equal(result.applied,false);assert.equal(result.bindingId,f.binding.bindingId);assert.deepEqual(f.binding,original);
 const text=await readFile(f.intent,'utf8');assert.ok(!text.includes(f.binding.target));assert.ok(!text.includes(f.binding.apiBaseUrl));const intent=JSON.parse(text);assert.equal(intent.status,'bound');assert.equal(intent.body.expectedConfigurationSha256,f.state.current.configurationSha256);assert.equal(f.posts().length,1);
 assert.equal(await resumeGenerationDevice(f.context,f.binding,f.io),null);
 const repeated=await bindGenerationDevice(f.context,f.binding,f.options,f.io);assert.equal(repeated.bindingId,result.bindingId);assert.equal(f.posts().length,1);
});
test('响应丢失后先只读核验，已登记不重复POST',async t=>{
 const f=await fixture(t);f.state.failAfter=true;const result=await bindGenerationDevice(f.context,f.binding,f.options,f.io);assert.equal(result.applied,false);assert.equal(f.posts().length,1);assert.equal(JSON.parse(await readFile(f.intent,'utf8')).status,'bound');
});
test('网络未知保留原请求，恢复只重放原摘要而不采纳新配置',async t=>{
 const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'network_error'});const bytes=await readFile(f.intent,'utf8');const first=f.posts()[0].body;assert.equal(JSON.parse(bytes).status,'pending');
 f.state.failBefore=false;f.state.current.configurationSha256='4'.repeat(64);await assert.rejects(resumeGenerationDevice(f.context,f.binding,f.io),{code:'remote_error'});assert.equal(await readFile(f.intent,'utf8'),bytes);assert.deepEqual(f.posts()[1].body,first);
});
test('原配置未变时恢复同一意图，绑定编号与摘要不重建',async t=>{
 const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));const original=JSON.parse(await readFile(f.intent,'utf8'));
 f.state.failBefore=false;const result=await resumeGenerationDevice(f.context,f.binding,f.io);assert.equal(result.bindingId,original.bindingId);assert.deepEqual(f.posts()[1].body,f.posts()[0].body);assert.equal(JSON.parse(await readFile(f.intent,'utf8')).status,'bound');
});
test('其他或失效关联只回读旧摘要，明确CAS确认后才替换',async t=>{
 const f=await fixture(t);f.state.association={deviceId:'11',projectId:'2',bindingId:randomUUID(),associationSha256:'b'.repeat(64),matchesCurrentConfiguration:false};
 await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'helper_device_conflict'});assert.equal(f.posts().length,0);await assert.rejects(lstat(f.intent),{code:'ENOENT'});
 const status=await generationDeviceStatus(f.context,f.binding,{'device-id':'11'});assert.equal(status.association.matchesCurrentConfiguration,false);assert.equal(status.applied,false);
 const result=await bindGenerationDevice(f.context,f.binding,{...f.options,'replace-device-binding':status.association.associationSha256},f.io);assert.equal(result.bindingId,f.binding.bindingId);assert.equal(f.posts()[0].body.expectedAssociationSha256,'b'.repeat(64));
});
test('目录不符、只预览和缺少运行时授权均不发关联写入',async t=>{
 for(const mode of ['path','preview','actions']){const f=await fixture(t);if(mode==='path')f.state.current.backendPathSha256='f'.repeat(64);if(mode==='preview')f.binding.mode='preview';if(mode==='actions')f.binding.allowedActions=['generate-project-config'];await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));assert.equal(f.posts().length,0)}
});
test('旧CLI或缺少能力入口明确拒绝，不登记服务器设备',async t=>{
 for(const mode of ['old','missing']){const f=await fixture(t);f.state.protocol=1;if(mode==='missing')f.io.runCli=async()=>{throw new CliError('旧子命令不存在','helper_prepare_failed')};await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'helper_device_protocol'});assert.equal(f.state.calls.length,0)}
});
test('坏设备ID、确认缺失和坏旧摘要在网络请求前拒绝',async t=>{
 for(const options of [{'device-id':'../11',confirm:true},{'device-id':'011',confirm:true},{'device-id':'11'},{'device-id':'11',confirm:true,'replace-device-binding':'bad'}]){const f=await fixture(t);await assert.rejects(bindGenerationDevice(f.context,f.binding,options,f.io));assert.equal(f.state.calls.length,0)}
});
test('本机身份变化、坏服务端hash类型或坏私有意图不成为成功',async t=>{
 const first=await fixture(t);first.state.identityChanged=true;await assert.rejects(bindGenerationDevice(first.context,first.binding,first.options,first.io),{code:'helper_identity_changed'});assert.equal(first.posts().length,0);
 const second=await fixture(t);second.state.current.deviceSha256=['2'.repeat(64)];await assert.rejects(bindGenerationDevice(second.context,second.binding,second.options,second.io),{code:'helper_invalid_device_receipt'});assert.equal(second.posts().length,0);
 const third=await fixture(t);await bindGenerationDevice(third.context,third.binding,third.options,third.io);const json=JSON.parse(await readFile(third.intent,'utf8'));json.body.deviceId='12';await writeFile(third.intent,JSON.stringify(json));await assert.rejects(resumeGenerationDevice(third.context,third.binding,third.io),{code:'helper_invalid_device_state'});
});

/** 当前设备意图是明确本机授权，不为新任务自动登记；检查本身只读。 */
test('分端新任务匹配已登记意图且不产生额外RPC',async t=>{
 const f=await fixture(t);await bindGenerationDevice(f.context,f.binding,f.options,f.io);const source={device:{accountId:'1',deviceId:'11',projectId:'2',configurationId:'21',deviceSha256:f.state.current.deviceSha256,configurationSha256:f.state.current.configurationSha256,parts:f.state.current.parts,backendPathSha256:f.state.current.backendPathSha256,frontendPathSha256:f.state.current.frontendPathSha256},associationSha256:f.state.association.associationSha256};
 const before=await readFile(f.intent,'utf8'),calls=f.state.calls.length;await verifyGenerationRuntimeDevice(f.binding,f.io,source);assert.equal(await readFile(f.intent,'utf8'),before);assert.equal(f.state.calls.length,calls);
 for(const kind of ['device','configuration','association','path']){const changed=structuredClone(source);if(kind==='device')changed.device.deviceSha256='6'.repeat(64);if(kind==='configuration')changed.device.configurationSha256='6'.repeat(64);if(kind==='association')changed.associationSha256='6'.repeat(64);if(kind==='path')changed.device.backendPathSha256='6'.repeat(64);await assert.rejects(verifyGenerationRuntimeDevice(f.binding,f.io,changed),{code:'helper_runtime_device_changed'})}
});

/** 服务端已登记而响应和当次回读都未知，下一次恢复必须先查旧意图，不能先重发绑定。 */
test('持久pending恢复先回读已登记状态，不重复绑定POST',async t=>{
 const f=await fixture(t),rpc=f.context.rpc;let unavailable=false;
 f.context.rpc=async(path,body)=>{
  if(unavailable&&(path.endsWith('/device-configuration')||path.endsWith('/device-binding')))throw new CliError('合成回读暂不可用','network_error');
  try{return await rpc(path,body)}catch(error){if(path.endsWith('/bind-device'))unavailable=true;throw error}
 };
 f.state.failAfter=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'network_error'});
 const pending=JSON.parse(await readFile(f.intent,'utf8'));assert.equal(pending.status,'pending');assert.equal(f.posts().length,1);assert.equal(f.state.association.bindingId,f.binding.bindingId);
 unavailable=false;f.state.failAfter=false;const before=f.state.calls.length;
 const result=await resumeGenerationDevice(f.context,f.binding,f.io);assert.equal(result.bindingId,f.binding.bindingId);assert.equal(result.applied,false);assert.equal(f.posts().length,1);
 assert.deepEqual(f.state.calls.slice(before).map(call=>call.path),['/admin/sys/generation-assistant/helper/device-configuration','/admin/sys/generation-assistant/helper/device-binding']);
 const bound=JSON.parse(await readFile(f.intent,'utf8'));assert.equal(bound.status,'bound');assert.deepEqual(bound.body,pending.body);
});

/** 同一绑定编号不够；当前设备／配置摘要不等于原pending时，不能用新配置登记成功或默认换CAS。 */
test('pending回读的当前配置已变仍保留原请求和旧CAS',async t=>{
 const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));
 const pending=await readFile(f.intent,'utf8'),request=f.posts()[0].body;f.state.failBefore=false;
 f.state.association={deviceId:'11',projectId:'2',bindingId:f.binding.bindingId,associationSha256:'a'.repeat(64),matchesCurrentConfiguration:true,deviceSha256:f.state.current.deviceSha256,configurationSha256:f.state.current.configurationSha256};f.state.current.configurationSha256='4'.repeat(64);
 await assert.rejects(resumeGenerationDevice(f.context,f.binding,f.io),{code:'remote_error'});assert.equal(await readFile(f.intent,'utf8'),pending);assert.deepEqual(f.posts()[1].body,request);
});

/** 同binding编号不足以确认；失效关联、其它binding或设备摘要变化都必须交给原请求的服务端校验。 */
test('pending先回读不将失效关联或变化设备误记成功',async t=>{
 for(const mode of ['stale','other','device']){
  const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));
  const pending=await readFile(f.intent,'utf8'),request=f.posts()[0].body;
  f.state.association={deviceId:'11',projectId:'2',bindingId:mode==='other'?randomUUID():f.binding.bindingId,associationSha256:'a'.repeat(64),matchesCurrentConfiguration:mode!=='stale'};
  if(mode==='device')f.state.current.deviceSha256='4'.repeat(64);
  const rpc=f.context.rpc;f.context.rpc=async(path,body)=>{if(path.endsWith('/bind-device')){assert.deepEqual(body,request);throw new CliError('合成原请求被拒绝','remote_error')}return rpc(path,body)};
  await assert.rejects(resumeGenerationDevice(f.context,f.binding,f.io),{code:'remote_error'});assert.equal(await readFile(f.intent,'utf8'),pending);
 }
});

/** 先回读成功仍不免除落盘前身份复核；变化时保留pending且没有新增POST。 */
test('pending回读已登记后身份变化不能落盘成功',async t=>{
 const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));
 const pending=await readFile(f.intent,'utf8');f.state.failBefore=false;
 f.state.association={deviceId:'11',projectId:'2',bindingId:f.binding.bindingId,associationSha256:'a'.repeat(64),matchesCurrentConfiguration:true,deviceSha256:f.state.current.deviceSha256,configurationSha256:f.state.current.configurationSha256};
 const rpc=f.context.rpc;f.context.rpc=async(path,body)=>{const result=await rpc(path,body);if(path.endsWith('/device-binding'))f.state.identityChanged=true;return result};
 await assert.rejects(resumeGenerationDevice(f.context,f.binding,f.io),{code:'helper_identity_changed'});assert.equal(await readFile(f.intent,'utf8'),pending);assert.equal(f.posts().length,1);
});

/** 配置和关联分两次读取；后一响应必须证明仍属于原pending快照，不能借同binding编号确认新配置。 */
test('pending回读跨越配置变更时保留旧意图并交给原CAS拒绝',async t=>{
 for(const field of ['deviceSha256','configurationSha256']){
  const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));
  const pending=await readFile(f.intent,'utf8'),request=f.posts()[0].body;f.state.failBefore=false;
  const rpc=f.context.rpc;f.context.rpc=async(path,body)=>{
   if(path.endsWith('/device-binding')){
    f.state.current[field]='4'.repeat(64);
    f.state.association={deviceId:'11',projectId:'2',bindingId:f.binding.bindingId,associationSha256:'b'.repeat(64),matchesCurrentConfiguration:true,deviceSha256:f.state.current.deviceSha256,configurationSha256:f.state.current.configurationSha256};
   }
   return rpc(path,body);
  };
  await assert.rejects(resumeGenerationDevice(f.context,f.binding,f.io),{code:'remote_error'});
  assert.equal(await readFile(f.intent,'utf8'),pending);assert.deepEqual(f.posts()[1].body,request);
 }
});

/** 显式登记也不能将先前预检与另一快照的现有关联拼接成功；不写新意图，不提交绑定。 */
test('登记预检跨配置的关联回执明确拒绝且不覆盖本地意图',async t=>{
 const f=await fixture(t),rpc=f.context.rpc;
 f.context.rpc=async(path,body)=>{
  if(path.endsWith('/device-binding')){
   f.state.current.configurationSha256='4'.repeat(64);
   f.state.association={deviceId:'11',projectId:'2',bindingId:f.binding.bindingId,associationSha256:'b'.repeat(64),matchesCurrentConfiguration:true,deviceSha256:f.state.current.deviceSha256,configurationSha256:f.state.current.configurationSha256};
  }
  return rpc(path,body);
 };
 await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'helper_device_configuration_changed'});
 assert.equal(f.posts().length,0);await assert.rejects(lstat(f.intent),{code:'ENOENT'});
});

/** 旧服务端缺少快照证据时不凭只读回执确认，只重放原请求让原服务端验证CAS。 */
test('旧格式关联回执只经原请求CAS确认而不误作快照证明',async t=>{
 const f=await fixture(t);f.state.failBefore=true;await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io));
 const original=f.posts()[0].body;f.state.failBefore=false;
 f.state.association={deviceId:'11',projectId:'2',bindingId:f.binding.bindingId,associationSha256:'a'.repeat(64),matchesCurrentConfiguration:true};
 const rpc=f.context.rpc;f.context.rpc=async(path,body)=>{
  const result=await rpc(path,body);
  if(result&&(path.endsWith('/device-binding')||path.endsWith('/bind-device'))){delete result.deviceSha256;delete result.configurationSha256}
  return result;
 };
 const result=await resumeGenerationDevice(f.context,f.binding,f.io);
 assert.equal(result.applied,false);assert.equal(f.posts().length,2);assert.deepEqual(f.posts()[1].body,original);assert.equal(JSON.parse(await readFile(f.intent,'utf8')).status,'bound');
});

/** 摘要必须成对且类型正确；状态查询也不能接受半份新合同后回退旧格式。 */
test('半份或非法新快照回执在写入前拒绝',async t=>{
 for(const proof of [{deviceSha256:'2'.repeat(64)},{configurationSha256:'3'.repeat(64)},{deviceSha256:null,configurationSha256:null},{deviceSha256:['2'.repeat(64)],configurationSha256:'3'.repeat(64)}]){
  const f=await fixture(t);f.state.association={deviceId:'11',projectId:'2',bindingId:f.binding.bindingId,associationSha256:'a'.repeat(64),matchesCurrentConfiguration:true,...proof};
  await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'helper_invalid_device_receipt'});
  assert.equal(f.posts().length,0);await assert.rejects(lstat(f.intent),{code:'ENOENT'});
 }
});

/** 新服务端登记回执有明确快照时必须匹配原请求；未知后的回读同样不能确认另一配置。 */
test('登记响应快照与原请求不符时保留pending',async t=>{
 const f=await fixture(t),rpc=f.context.rpc;
 f.context.rpc=async(path,body)=>{
  const result=await rpc(path,body);
  if(path.endsWith('/bind-device')){f.state.current.configurationSha256='4'.repeat(64);return {...result,configurationSha256:f.state.current.configurationSha256}}
  return result;
 };
 await assert.rejects(bindGenerationDevice(f.context,f.binding,f.options,f.io),{code:'helper_invalid_device_receipt'});
 const intent=JSON.parse(await readFile(f.intent,'utf8'));assert.equal(intent.status,'pending');assert.equal(intent.body.expectedConfigurationSha256,'3'.repeat(64));assert.equal(f.posts().length,1);
});
