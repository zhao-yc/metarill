import { spawn } from 'node:child_process';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

// 三阶段使用同一个真实CLI授权缓存；授权完成后由C#夹具撤去临时超级角色，再运行业务。
const [mode,base,projectId,repository,evidence,scenarioText]=process.argv.slice(2);
const origin=new URL(base),scenario=JSON.parse(scenarioText||'null'),webToken=process.env.METARILL_AGENT_WEB_TOKEN;
if(!['authorize','exercise','revoked'].includes(mode)||!['127.0.0.1','localhost'].includes(origin.hostname)||!['http:','https:'].includes(origin.protocol)||origin.username||origin.password||origin.pathname!=='/api'||origin.search||origin.hash||!/^\d+$/.test(projectId||'')||!scenario||!repository||!evidence||mode==='authorize'&&!webToken)throw new Error('规则正式CLI参数无效。');
const root=path.resolve(repository),evidenceRoot=path.resolve(evidence),allowed=path.join(root,'.local')+path.sep;
if(!evidenceRoot.startsWith(allowed))throw new Error('规则CLI证据目录越界。');
const directory=path.join(evidenceRoot,'rule-cli'),config=path.join(directory,'config.json');
if(!directory.startsWith(evidenceRoot+path.sep))throw new Error('规则CLI缓存越界。');
await mkdir(directory,{recursive:true});
const env={...process.env,METARILL_AGENT_CONFIG:config},cli=path.join(root,'tools/metarill-agent-cli/bin/metarill-agent.mjs');
let loginProcess;

// 真实CLI的错误只保留固定错误码，stdout/完整stderr与访问凭据不进入本脚本回执。
function run(args,onText){
 const child=spawn(process.execPath,[cli,...args],{cwd:root,env,windowsHide:true});let output='',error='';
 child.stdout.on('data',bytes=>output+=bytes.toString());child.stderr.on('data',bytes=>{error+=bytes.toString();onText?.(error);});
 const complete=new Promise((resolve,reject)=>{child.on('error',()=>reject(new Error('规则CLI未启动。')));child.on('close',code=>{
  if(code===0){try{resolve(JSON.parse(output));}catch{reject(new Error('规则CLI输出不是JSON。'));}return;}
  let failure={};for(const line of error.trim().split('\n').reverse()){try{const value=JSON.parse(line);if(typeof value.error==='string'){failure=value;break;}}catch{}}
  reject(Object.assign(new Error('规则CLI失败。'),{cliError:/^[a-z_0-9]+$/.test(failure.error||'')?failure.error:'unknown',businessCode:/^YZP_[A-Z_]+$/.test(failure.code||'')?failure.code:''}));
 });});return {child,complete};
}
const call=async args=>run(args).complete;
// 只有authorize阶段持有本轮临时超级账号的Web令牌；后续真实权限从数据库重新建立。
async function authorize(){
 let decided=false,approvalError=false;
 loginProcess=run(['auth','login','--url',base,'--profile','rules','--name','共享规则正式验收'],text=>{
  const code=/用户码：([A-Z2-9]{8})/.exec(text)?.[1];if(decided||!code)return;decided=true;
  fetch(base+'/admin/sys/agent/approve',{method:'POST',redirect:'error',headers:{'Content-Type':'application/json',loginStateId:webToken,sysParams:encodeURIComponent(JSON.stringify({yzProjectId:projectId}))},body:JSON.stringify({userCode:code,approve:true}),signal:AbortSignal.timeout(30000)})
   .then(async response=>{const value=await response.json();if(response.status!==200||Number(value.code)!==200)throw new Error('授权失败。');})
   .catch(()=>{approvalError=true;loginProcess?.child.kill();});
 });
 const timer=setTimeout(()=>loginProcess?.child.kill(),45000);
 try{await loginProcess.complete;if(!decided||approvalError)throw new Error('规则设备授权未完成。');}
 finally{clearTimeout(timer);loginProcess=null;}
 return {deviceApproved:true,realCliAuth:true};
}
function args(capability,body,parentId,requestId){return ['call',capability.id,'--profile','rules','--json',JSON.stringify(body),'--confirm','--request-id',requestId,...(parentId?['--parent-id',parentId]:[])];}
async function denied(capability,body,parentId,requests){
 const id=randomUUID();let rejected=false;try{await call(args(capability,body,parentId,id));}catch(error){rejected=error.cliError==='http_403'&&error.businessCode==='YZP_CAPABILITY_DENIED';}
 if(!rejected)throw new Error('规则请求未在任务前明确拒绝。');requests.push(id);
}
async function exercise(){
 const result=await call(['capabilities','list','--profile','rules']);const deniedIds=[],allowedIds=[];
 for(const page of scenario.pages){
  const entries=result.capabilities.filter(item=>item.pageId===page.id);
  if(entries.length!==2||entries.some(item=>!['update','delete'].includes(item.operation)))throw new Error('无查询权普通账号的真实目录不符。');
  const update=entries.find(item=>item.operation==='update'),remove=entries.find(item=>item.operation==='delete');if(!update||!remove)throw new Error('普通更新或删除缺失。');
  await denied(update,{id:scenario.blockedId,name:'允许回显'},page.parentId,deniedIds);await denied(remove,{id:scenario.blockedId},page.parentId,deniedIds);
  if(page.parentId)await denied(update,{id:scenario.allowedId,name:'越界回显'},page.wrongParentId,deniedIds);
  const requestId=randomUUID(),response=await call(args(update,{id:scenario.allowedId,name:'允许-'+page.id},page.parentId,requestId));
  if(response.data?.id!==scenario.allowedId)throw new Error('规则允许写入结果ID不符。');
  const task=await call(['tasks','show',requestId,'--profile','rules']);if(task.outcome!=='applied'||task.resultId!==scenario.allowedId)throw new Error('规则真实任务结果不符。');allowedIds.push(requestId);
 }
 const root=result.capabilities.find(item=>item.pageId==='201'&&item.operation==='update');
 for(const id of [scenario.foreignId,scenario.deletedId])await denied(root,{id,name:'越界回显'},null,deniedIds);
 return {ordinaryWithoutQuery:true,readonlyDenied:true,logicalParents:true,realCliWrites:true,taskResults:true,deniedRequestIds:deniedIds,allowedRequestIds:allowedIds};
}
async function revoked(){
 const result=await call(['capabilities','list','--profile','rules']);if(result.capabilities.some(item=>item.pageId==='201'))throw new Error('撤权后目录仍保留该页面。');
 // 绕过CLI的目录缓存，使用真实签发令牌直接验证服务器仍在建任务前拒绝旧能力。
 const cached=JSON.parse(await readFile(config,'utf8')),token=cached.profiles.rules.credentials.accessToken,requestId=randomUUID();
 const response=await fetch(base+scenario.updatePath,{method:'POST',redirect:'error',signal:AbortSignal.timeout(30000),headers:{'Content-Type':'application/json',Authorization:'Bearer '+token,'X-Yzp-Agent-Confirm':'true','X-Yzp-Agent-Capability':'page:201:update','X-Yzp-Agent-Request-Id':requestId},body:JSON.stringify({id:scenario.allowedId,name:'撤权回显'})});
 if(response.status!==403)throw new Error('撤权后服务器没有拒绝旧能力。');
 await call(['auth','logout','--profile','rules']);return {revokedDirectory:true,revokedBeforeTask:true,logout:true,deniedRequestIds:[requestId]};
}
try{console.log(JSON.stringify(await (mode==='authorize'?authorize():mode==='exercise'?exercise():revoked())));}
finally{if(loginProcess)loginProcess.child.kill();}
