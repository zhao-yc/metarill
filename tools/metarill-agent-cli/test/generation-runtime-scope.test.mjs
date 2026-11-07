import test from 'node:test';import assert from 'node:assert/strict';
import { runtimePartsEqual,validateRuntimeOrigin,validateRuntimeSnapshot,validateRuntimePlan } from '../lib/generation-runtime-scope.mjs';
const binding={accountId:'1',projectId:'2'};
const origin=()=>({device:{accountId:'1',deviceId:'11',projectId:'2',configurationId:'21',deviceSha256:'2'.repeat(64),configurationSha256:'3'.repeat(64),parts:{backend:true,frontend:false},backendPathSha256:'4'.repeat(64),frontendPathSha256:null},associationSha256:'5'.repeat(64)});
/** 固定网络协议向量，不读取本机profile/凭据，不把纯校验当作实际OAuth或磁盘应用。 */
for(const kind of ['account','project','device-id','configuration-id','device-hash','configuration-hash','association','unknown','unknown-device','empty-parts','partial-parts','parts-extra','disabled-path','enabled-path','number-id'])test('分端来源拒绝：'+kind,()=>{
 const value=origin();const device=value.device;
 if(kind==='account')device.accountId='9';if(kind==='project')device.projectId='9';if(kind==='device-id')device.deviceId='../11';if(kind==='configuration-id')device.configurationId='021';
 if(kind==='device-hash')device.deviceSha256=['2'.repeat(64)];if(kind==='configuration-hash')device.configurationSha256='bad';if(kind==='association')value.associationSha256='bad';if(kind==='unknown')value.command='anything';if(kind==='unknown-device')device.path='/tmp/unknown';
 if(kind==='empty-parts')device.parts={backend:false,frontend:false};if(kind==='partial-parts')delete device.parts.frontend;if(kind==='parts-extra')device.parts.paths=[];
 if(kind==='disabled-path')device.frontendPathSha256='5'.repeat(64);if(kind==='enabled-path')device.backendPathSha256=null;if(kind==='number-id')device.accountId=1;
 assert.throws(()=>validateRuntimeOrigin(value,binding),{code:'helper_invalid_runtime_origin'});
});
test('明确双端/单端与v1无来源分别保持原合同',()=>{
 const value=origin();assert.equal(validateRuntimeOrigin(value,binding),value);assert.equal(validateRuntimeSnapshot({schemaVersion:2,action:'sync-runtime',runtimeParts:value.device.parts},value,binding),value);
 assert.equal(validateRuntimeSnapshot({schemaVersion:1},undefined,binding),null);assert.equal(runtimePartsEqual({frontend:false,backend:true},value.device.parts),true);
 for(const parts of [{backend:false,frontend:true},{backend:true,frontend:true}]){const next=origin();next.device.parts=parts;next.device.backendPathSha256=parts.backend?'4'.repeat(64):null;next.device.frontendPathSha256='5'.repeat(64);validateRuntimeOrigin(next,binding)}
});
for(const kind of ['v1-origin','v1-parts','v2-no-origin','v2-other-action','v2-expand','unknown-version'])test('快照拒绝混用：'+kind,()=>{
 const value=origin();let snapshot={schemaVersion:2,action:'sync-runtime',runtimeParts:value.device.parts};let source=value;
 if(kind==='v1-origin')snapshot={schemaVersion:1};if(kind==='v1-parts'){snapshot={schemaVersion:1,runtimeParts:value.device.parts};source=null}if(kind==='v2-no-origin')source=null;if(kind==='v2-other-action')snapshot.action='generate-all-page';if(kind==='v2-expand')snapshot.runtimeParts={backend:true,frontend:true};if(kind==='unknown-version')snapshot.schemaVersion=3;
 assert.throws(()=>validateRuntimeSnapshot(snapshot,source,binding),{code:'helper_invalid_runtime_origin'});
});
for(const kind of ['downgrade','parts','actions','v1-scope'])test('原准备计划拒绝替换范围：'+kind,()=>{
 let value=origin();const plan={schemaVersion:2,actions:['sync-runtime'],runtimeParts:value.device.parts};if(kind==='downgrade')plan.schemaVersion=1;if(kind==='parts')plan.runtimeParts={backend:true,frontend:true};if(kind==='actions')plan.actions=['sync-runtime','generate-all-page'];if(kind==='v1-scope'){value=null;plan.schemaVersion=1}
 assert.throws(()=>validateRuntimePlan(plan,value),{code:'helper_invalid_plan'});
});
test('原计划的明确v2范围及旧v1均可核验',()=>{const value=origin();validateRuntimePlan({schemaVersion:2,actions:['sync-runtime'],runtimeParts:value.device.parts},value);validateRuntimePlan({schemaVersion:1},null)});
