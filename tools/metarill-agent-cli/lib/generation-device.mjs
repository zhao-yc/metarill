import { readFile,lstat } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { CliError } from './errors.mjs';
import { deviceProjectPathFingerprint } from './device-project-path.mjs';
const hash=/^[a-f0-9]{64}$/;const uuid=/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
const validHash=value=>typeof value==='string'&&hash.test(value);const validUuid=value=>typeof value==='string'&&uuid.test(value);
const endpoint='/admin/sys/generation-assistant/helper/';
const keys=['schemaVersion','bindingId','localBindingSha256','deviceId','body','status','associationSha256'];
const bodyKeys=['bindingId','deviceId','backendPathSha256','frontendPathSha256','runtimePartsProtocol','expectedDeviceSha256','expectedConfigurationSha256','expectedAssociationSha256'];
const digest=value=>createHash('sha256').update('metarill-device-intent-v1'+String.fromCharCode(10)+JSON.stringify(Object.fromEntries(keys.map(key=>[key,value[key]])))).digest('hex');
/** 设备选择不是授权；仅校验规范ID和本机明确重绑摘要，真实拥有权仍由服务端查库。 */
export function validateDeviceOptions(options,writing=true){
 const id=options['device-id'];if(typeof id!=='string'||! /^[1-9][0-9]{0,18}$/.test(id)||BigInt(id)>9223372036854775807n)throw new CliError('设备关联需要规范的--device-id。','helper_invalid_device');
 if(writing&&!options.confirm)throw new CliError('设备关联必须显式--confirm；不自动换绑。','confirmation_required');
 if(options['replace-device-binding']!==undefined&&!validHash(options['replace-device-binding']))throw new CliError('替换关联必须指定回读的完整旧摘要。','helper_invalid_device');
 return id;
}
/** 公开回执只认可当前项目/设备的UUID与摘要，不接受目录、命令或隐式成功。 */
function view(value,binding,device,required=false){
 if(value===null&&!required)return null;
 if(!value||value.deviceId!==device||value.projectId!==binding.projectId||!validUuid(value.bindingId)||!validHash(value.associationSha256)||typeof value.matchesCurrentConfiguration!=='boolean'||required&&(value.bindingId!==binding.bindingId||value.matchesCurrentConfiguration!==true))throw new CliError('设备关联回执身份无效。','helper_invalid_device_receipt');return value;
}
/** 配置预检只读取服务端发布的启用端和摘要，不能从网络接收本机写入路径。 */
function configuration(value,binding,device){
 const parts=value?.parts;
 if(!value||value.deviceId!==device||value.projectId!==binding.projectId||!validHash(value.deviceSha256)||!validHash(value.configurationSha256)||!parts||Object.keys(parts).sort().join(',')!=='backend,frontend'||typeof parts.backend!=='boolean'||typeof parts.frontend!=='boolean'||!parts.backend&&!parts.frontend||parts.backend&&!validHash(value.backendPathSha256)||!parts.backend&&value.backendPathSha256!==null||parts.frontend&&!validHash(value.frontendPathSha256)||!parts.frontend&&value.frontendPathSha256!==null)throw new CliError('设备配置预检响应无效。','helper_invalid_device_receipt');return value;
}
/** 只读查询旧ETag，可显示失效状态；不以旧关联或设备选择授予写入权。 */
export async function generationDeviceStatus(context,binding,options){const device=validateDeviceOptions(options,false);return {deviceId:device,projectId:binding.projectId,association:view(await context.rpc(endpoint+'device-binding',{deviceId:device}),binding,device),applied:false}}
/** 实际固定CLI必须支持请求/计划v2，旧Generation依赖混换也会因能力入口缺失而拒绝。 */
async function ready(context,binding,io){
 if(binding.mode!=='apply'||!binding.allowedActions.includes('sync-runtime'))throw new CliError('设备批量关联要求apply且允许sync-runtime的本机绑定。','helper_invalid_policy');
 io.sameIdentity(binding,await context.identity());
 let capabilities;try{capabilities=(await io.runCli(binding,['update','capabilities'],context.timeoutMs)).value}catch(error){if(error.code==='helper_binding_changed')throw error;throw new CliError('固定CLI无法确认分端协议，请使用配套新CLI重新绑定。','helper_device_protocol')}
 if(capabilities?.schemaVersion!==1||capabilities.runtimePartsProtocol!==2||!Array.isArray(capabilities.requestSchemaVersions)||!capabilities.requestSchemaVersions.includes(2)||!Array.isArray(capabilities.planSchemaVersions)||!capabilities.planSchemaVersions.includes(2))throw new CliError('固定CLI不支持分端运行时协议，请使用配套新CLI重新绑定。','helper_device_protocol');
 const identity=(await io.runCli(binding,['update','target-identity','--target',binding.target],context.timeoutMs)).value;
 if(identity?.projectId!==binding.projectId||identity.rootNamespace!==binding.rootNamespace||identity.databaseProvider!==binding.databaseProvider)throw new CliError('设备关联的本机工程归属已变化。','helper_binding_changed');
 return {backend:deviceProjectPathFingerprint(binding.target),frontend:deviceProjectPathFingerprint(join(binding.target,'frontend'))};
}
/** 设备意图不加入v1核心绑定对象；先持久化，再发一次固定配置快照的请求。 */
async function readIntent(binding,io){
 const path=join(binding.workRoot,'device-binding.json');await io.safePath(path);let value;
 try{const info=await lstat(path);if(!info.isFile()||info.size>8192)throw new CliError('设备关联意图文件无效。','helper_invalid_device_state');value=JSON.parse(await readFile(path,'utf8'))}catch(error){if(error.code==='ENOENT')return null;throw error}
 if(!value||Object.keys(value).some(key=>![...keys,'integritySha256'].includes(key))||keys.some(key=>!Object.hasOwn(value,key))||value.schemaVersion!==1||value.bindingId!==binding.bindingId||value.localBindingSha256!==binding.localBindingSha256||digest(value)!==value.integritySha256||!['pending','bound'].includes(value.status)||value.status==='bound'&&!validHash(value.associationSha256)||value.status==='pending'&&value.associationSha256!==null||!value.body||Object.keys(value.body).sort().join(',')!==[...bodyKeys].sort().join(',')||value.body.bindingId!==binding.bindingId||value.body.deviceId!==value.deviceId||value.body.runtimePartsProtocol!==2||['backendPathSha256','frontendPathSha256','expectedDeviceSha256','expectedConfigurationSha256'].some(key=>!validHash(value.body[key]))||value.body.expectedAssociationSha256!==null&&!validHash(value.body.expectedAssociationSha256))throw new CliError('设备关联意图与原绑定不一致，请保留现场。','helper_invalid_device_state');
 validateDeviceOptions({'device-id':value.deviceId},false);return value;
}
/** 状态只存ID/摘要和固定请求，不存token、路径或任意命令。 */
async function saveIntent(binding,io,value){await io.atomicJson(join(binding.workRoot,'device-binding.json'),{...value,integritySha256:digest(value)})}
/** 明确确认后固定当前配置；已有不同关联仅接受用户提交且回读匹配的旧ETag。 */
export async function bindGenerationDevice(context,binding,options,io){
 const device=validateDeviceOptions(options);const paths=await ready(context,binding,io);const existing=await readIntent(binding,io);
 if(existing&&existing.deviceId!==device)throw new CliError('本profile已有另一设备意图，请使用新的profile保留原关联。','helper_device_conflict');
 const current=configuration(await context.rpc(endpoint+'device-configuration',{deviceId:device}),binding,device);
 if(current.parts.backend&&current.backendPathSha256!==paths.backend||current.parts.frontend&&current.frontendPathSha256!==paths.frontend)throw new CliError('本机工程目录与原设备启用端路径不一致，请先核对配置和目标。','helper_device_path_mismatch');
 const old=view(await context.rpc(endpoint+'device-binding',{deviceId:device}),binding,device);
 const expected=options['replace-device-binding']??null;
 if(old&&(old.bindingId!==binding.bindingId||!old.matchesCurrentConfiguration)&&expected!==old.associationSha256)throw new CliError('当前设备项目已有不同或失效关联，请回读后使用--replace-device-binding '+old.associationSha256+' --confirm明确替换。','helper_device_conflict');
 if(!old&&expected!==null)throw new CliError('原设备关联已不存在，请先重新查询。','helper_device_conflict');
 io.sameIdentity(binding,await context.identity());
 const body={bindingId:binding.bindingId,deviceId:device,backendPathSha256:paths.backend,frontendPathSha256:paths.frontend,runtimePartsProtocol:2,expectedDeviceSha256:current.deviceSha256,expectedConfigurationSha256:current.configurationSha256,expectedAssociationSha256:expected};
 const state={schemaVersion:1,bindingId:binding.bindingId,localBindingSha256:binding.localBindingSha256,deviceId:device,body,status:'pending',associationSha256:null};
 if(old&&old.bindingId===binding.bindingId&&old.matchesCurrentConfiguration){await saveIntent(binding,io,{...state,status:'bound',associationSha256:old.associationSha256});return {...old,applied:false}}
 await saveIntent(binding,io,state);return submit(context,binding,io,state);
}
/** 响应未知时保留原意图；只在原配置摘要仍相等且回读到当前绑定时确认登记成功。 */
async function submit(context,binding,io,state){
 let result;
 try{result=view(await context.rpc(endpoint+'bind-device',state.body),binding,state.deviceId,true)}catch(error){
  try{const current=configuration(await context.rpc(endpoint+'device-configuration',{deviceId:state.deviceId}),binding,state.deviceId);if(current.deviceSha256!==state.body.expectedDeviceSha256||current.configurationSha256!==state.body.expectedConfigurationSha256)throw error;result=view(await context.rpc(endpoint+'device-binding',{deviceId:state.deviceId}),binding,state.deviceId,true)}catch{throw error}
 }
 io.sameIdentity(binding,await context.identity());await saveIntent(binding,io,{...state,status:'bound',associationSha256:result.associationSha256});return {...result,applied:false};
}
/** 轮询只重放原pending请求，不重新预检采纳新配置，不新增bindingId或默认覆盖旧ETag。 */
export async function resumeGenerationDevice(context,binding,io){
 const state=await readIntent(binding,io);if(!state||state.status==='bound')return null;await ready(context,binding,io);return submit(context,binding,io,state);
}
