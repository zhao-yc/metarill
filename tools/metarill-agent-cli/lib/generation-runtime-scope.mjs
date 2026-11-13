import { CliError } from './errors.mjs';
const hash=value=>typeof value==='string'&&/^[a-f0-9]{64}$/.test(value);
const id=value=>typeof value==='string'&&/^[1-9][0-9]{0,18}$/.test(value)&&BigInt(value)<=9223372036854775807n;
const keys=(value,names)=>value&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).sort().join(',')===[...names].sort().join(',');
/** 两端必须明确bool，不把缺省值、未知字段或真值转换解释成范围。 */
export function runtimePartsEqual(left,right){return [left,right].every(value=>keys(value,['backend','frontend'])&&typeof value.backend==='boolean'&&typeof value.frontend==='boolean'&&(value.backend||value.frontend))&&left.backend===right.backend&&left.frontend===right.frontend}
/** 来源只接受当前本机核心绑定的账号/项目和固定字段；不是网络授予本机路径的接口。 */
export function validateRuntimeOrigin(origin,binding){
 const device=origin?.device;
 if(!keys(origin,['device','associationSha256'])||!hash(origin.associationSha256)||!keys(device,['accountId','deviceId','projectId','configurationId','deviceSha256','configurationSha256','parts','backendPathSha256','frontendPathSha256'])||![device.accountId,device.deviceId,device.projectId,device.configurationId].every(id)||device.accountId!==binding.accountId||device.projectId!==binding.projectId||![device.deviceSha256,device.configurationSha256].every(hash)||!runtimePartsEqual(device.parts,device.parts)||device.parts.backend&&!hash(device.backendPathSha256)||!device.parts.backend&&device.backendPathSha256!==null||device.parts.frontend&&!hash(device.frontendPathSha256)||!device.parts.frontend&&device.frontendPathSha256!==null)
  throw new CliError('分端任务来源与本机绑定或固定范围不一致。','helper_invalid_runtime_origin');
 return origin;
}
/** v1无范围/来源，v2只接受固定来源的单运行时；不能借协议升级混入原配置或页面动作。 */
export function validateRuntimeSnapshot(snapshot,origin,binding){
 if(snapshot?.schemaVersion===1){if(origin!=null||snapshot.runtimeParts!=null)throw new CliError('普通任务不能带分端来源或范围。','helper_invalid_runtime_origin');return null}
 validateRuntimeOrigin(origin,binding);
 if(snapshot?.schemaVersion!==2||snapshot.action!=='sync-runtime'||!runtimePartsEqual(snapshot.runtimeParts,origin.device.parts))throw new CliError('v2任务只允许原设备启用端的运行时范围。','helper_invalid_runtime_origin');
 return origin;
}
/** 固定准备计划必须保留v2单范围；无来源计划沿原v1，禁止降级后重新做全端preview。 */
export function validateRuntimePlan(plan,origin){
 if(origin==null){if(plan?.schemaVersion!==1||plan.runtimeParts!=null)throw new CliError('普通应用计划版本或范围无效。','helper_invalid_plan');return}
 if(plan?.schemaVersion!==2||!Array.isArray(plan.actions)||plan.actions.length!==1||plan.actions[0]!=='sync-runtime'||!runtimePartsEqual(plan.runtimeParts,origin.device.parts))throw new CliError('分端应用必须沿原v2计划，不能扩大端范围。','helper_invalid_plan');
}
