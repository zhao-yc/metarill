import {spawn} from 'node:child_process';
import {readFile,realpath} from 'node:fs/promises';
import path from 'node:path';

// 正式探针只运行固定CLI、独占回环生产宿主和本轮输入；不输出令牌或远端正文。
const [mode,base,source,evidence,inputPath,indexText]=process.argv.slice(2);
const root=await realpath(source),privateRoot=path.resolve(evidence),origin=new URL(base),token=process.env.METARILL_AGENT_WEB_TOKEN;
if(!['bind','poll','device-status','logout'].includes(mode)||!['127.0.0.1','localhost'].includes(origin.hostname)||origin.pathname!=='/api'||origin.search||origin.hash||!token||!privateRoot.startsWith(path.join(root,'.local')+path.sep)||!path.resolve(inputPath).startsWith(privateRoot+path.sep))throw new Error('设备批量正式探针边界不符。');
const input=JSON.parse(await readFile(inputPath,'utf8')),index=Number(indexText),item=input.projects[index];
if(![0,1].includes(index)||!item||!/^\d+$/.test(item.projectId)||!/^\d+$/.test(input.deviceId))throw new Error('设备批量正式输入不符。');
const config=path.resolve(input.config),profile='device-runtime-'+index;
if(!config.startsWith(privateRoot+path.sep))throw new Error('CLI凭据落点不符。');
const env={...process.env,METARILL_AGENT_CONFIG:config},cli=path.join(root,'tools/metarill-agent-cli/bin/metarill-agent.mjs');
let stage='开始',pending=null;
/** 子进程有界运行；失败只保留固定阶段和CLI公开错误码，禁止输出原始stderr。 */
function run(args,onText){
 const child=spawn(process.execPath,[cli,...args,'--profile',profile],{cwd:root,env,windowsHide:true});let out='',err='';
 child.stdout.on('data',b=>out+=b);child.stderr.on('data',b=>{err+=b;onText?.(err)});
 const timer=setTimeout(()=>child.kill(),150000);
 const complete=new Promise((resolve,reject)=>{child.on('error',()=>{clearTimeout(timer);reject(new Error('CLI进程未启动'))});child.on('close',code=>{clearTimeout(timer);if(code){let publicCode='cli_failed';try{publicCode=JSON.parse(out).error?.code||publicCode}catch{}reject(new Error('CLI固定阶段失败：'+stage+'／'+publicCode));return;}try{resolve(JSON.parse(out))}catch{reject(new Error('CLI未返回结构化结果'))}})});
 return {child,complete};
}
const call=args=>run(args).complete;
/** 真实Web会话批准本轮真实设备码，项目来自原sysParams；不是注入Agent身份。 */
async function approve(userCode){
 const response=await fetch(base+'/admin/sys/agent/approve',{method:'POST',redirect:'error',headers:{'Content-Type':'application/json',loginStateId:token,sysParams:encodeURIComponent(JSON.stringify({yzProjectId:item.projectId,yzManagerDevicesId:input.deviceId}))},body:JSON.stringify({userCode,approve:true}),signal:AbortSignal.timeout(20000)});
 const value=await response.json();if(response.status!==200||value.code!==200)throw new Error('真实Web批准被拒绝：HTTP'+response.status+'/R'+Number(value.code));
}
try{
 let result;
 if(mode==='bind'){
  stage='真实OAuth登录';let decided=false,approvalFailure=null,approvalPromise;
  pending=run(['auth','login','--url',base,'--name','原设备批量正式验收'],text=>{const code=/用户码：([A-Z2-9]{8})/.exec(text)?.[1];if(decided||!code)return;decided=true;approvalPromise=approve(code).catch(error=>{approvalFailure=error;pending?.child.kill()})});
  try{await pending.complete}catch(error){if(approvalFailure)throw approvalFailure;throw error}await approvalPromise;if(!decided||approvalFailure)throw new Error('真实设备码批准没有完成');pending=null;
  stage='固定CLI核心及原设备关联';result=await call(['helper','bind','--target',item.target,'--source-root',root,'--cli',input.cli,'--dotnet',input.dotnet,'--work-root',item.work,'--mode','apply','--allow-actions','sync-runtime','--device-id',input.deviceId,'--confirm']);
  if(result.applied!==false)throw new Error('绑定不得报告源码已应用');
  stage='真实设备关联回读';const status=await call(['helper','device-status','--device-id',input.deviceId]);if(status.applied!==false||status.projectId!==item.projectId||status.association?.matchesCurrentConfiguration===false||!status.association?.associationSha256)throw new Error('真实关联回读不符');
  result={realCli:true,realOAuth:true,deviceBound:true,projectId:item.projectId,bindingId:status.association.bindingId,associationSha256:status.association.associationSha256,applied:false};
 }
 if(mode==='poll'){
  stage='原v2任务领取及本机应用';for(let n=0;n<4;n++){result=await call(['helper','poll','--once']);if(result.applied===true)break;if(!['receipt_replayed','device_bound'].includes(result.status))break}
  if(result.applied!==true||result.status!=='applied')throw new Error('原运行时任务尚未实际应用');
  result={realCli:true,applied:true,status:result.status,requestId:result.requestId,projectId:item.projectId};
 }
 if(mode==='device-status'){stage='设备配置回读';const status=await call(['helper','device-status','--device-id',input.deviceId]);result={projectId:item.projectId,applied:status.applied,matches:status.association?.matchesCurrentConfiguration,associationSha256:status.association?.associationSha256}}
 if(mode==='logout'){stage='真实CLI注销';await call(['auth','logout']);result={projectId:item.projectId,loggedOut:true}}
 console.log(JSON.stringify(result));
}catch(error){console.error(JSON.stringify({stage,failed:true,message:String(error?.message||'设备批量正式验收失败').replace(/Bearer\s+\S+/gi,'Bearer [已脱敏]')}));process.exitCode=1}
finally{pending?.child.kill()}
