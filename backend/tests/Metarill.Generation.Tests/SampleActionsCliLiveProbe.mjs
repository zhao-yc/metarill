import {spawn} from 'node:child_process';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
import {randomUUID} from 'node:crypto';

// 只消费本轮独占回环宿主和私有CLI配置，不向日志输出令牌、HTTP正文或原始stderr。
const [mode,base,repository,evidence,recordsPath]=process.argv.slice(2),token=process.env.METARILL_AGENT_WEB_TOKEN;
const origin=new URL(base),root=path.resolve(repository),privateRoot=path.resolve(evidence),config=path.resolve(process.env.METARILL_AGENT_CONFIG||'');
if(!['login','exercise','busy','revoke'].includes(mode)||!['127.0.0.1','localhost'].includes(origin.hostname)||origin.pathname!=='/api'||origin.search||origin.hash||!token||!privateRoot.startsWith(path.join(root,'.local')+path.sep)||!config.startsWith(privateRoot+path.sep)||!path.resolve(recordsPath).startsWith(privateRoot+path.sep))throw new Error('原按钮正式CLI参数或私有目录不合法。');
const cli=path.join(root,'tools/metarill-agent-cli/bin/metarill-agent.mjs'),profile='sample-actions',env={...process.env,METARILL_AGENT_CONFIG:config};
const web={loginStateId:token,sysParams:encodeURIComponent(JSON.stringify({yzProjectId:'810000000000000001'}))};
const records=JSON.parse(await readFile(recordsPath,'utf8'));
let pending;
// 状态与业务码都核对，网络故障绝不能记为预期业务拒绝。
async function request(route,body,headers=web,expected=200){
 const response=await fetch(base+route,{method:body===undefined?'GET':'POST',redirect:'error',headers:{...headers,...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(20000)});
 const value=await response.json();if(response.status!==expected||value.code!==undefined&&Number(value.code)!==expected)throw new Error('原按钮正式HTTP结果不符。');return value;
}
// 真CLI进程输出只在内存解析，错误仅保留公开结构化错误码。
function run(args,onErrorText){
 const child=spawn(process.execPath,[cli,...args],{cwd:root,env,windowsHide:true});let output='',error='';
 child.stdout.on('data',bytes=>output+=bytes);child.stderr.on('data',bytes=>{error+=bytes;onErrorText?.(error);});
 const timeout=setTimeout(()=>child.kill(),30000);
 const complete=new Promise((resolve,reject)=>{
  child.once('error',()=>{clearTimeout(timeout);reject(new Error('原按钮CLI未启动。'));});
  child.once('close',code=>{clearTimeout(timeout);if(code===0){try{resolve(JSON.parse(output));}catch{reject(new Error('原按钮CLI没有有效JSON。'));}return;}
   let detail={};for(const line of error.trim().split('\n').reverse()){try{const item=JSON.parse(line);if(typeof item.error==='string'){detail=item;break;}}catch{}}
   reject(Object.assign(new Error('原按钮CLI拒绝或执行失败。'),{cliError:/^http_[0-9]+$/.test(detail.error||'')?detail.error:'unknown',businessCode:/^YZP_[A-Z_]+$/.test(detail.code||'')?detail.code:''}));
  });
 });return {child,complete};
}
const call=async args=>run(args).complete;
// 使用原设备流程和真实Web批准接口；批准对象为本轮合成账号，不声明浏览器人工审批。
async function login(){
 let decided=false,approvalFailed=false;
 pending=run(['auth','login','--url',base,'--profile',profile,'--name','原三按钮正式验收'],text=>{
  const code=/用户码：([A-Z2-9]{8})/.exec(text)?.[1];if(decided||!code)return;decided=true;
  request('/admin/sys/agent/approve',{userCode:code,approve:true}).catch(()=>{approvalFailed=true;pending?.child.kill();});
 });
 try{await pending.complete;if(!decided||approvalFailed)throw new Error('原设备批准没有完成。');}finally{pending=null;}
}
const action=(name,body,requestId=randomUUID())=>call(['call','action:sample-contract-'+name,'--profile',profile,'--json',JSON.stringify(body),'--request-id',requestId,...(name==='preview'?[]:['--confirm'])]);
async function task(id,outcome){const value=await call(['tasks','show',id,'--profile',profile]);if(value.outcome!==outcome)throw new Error('原业务任务结论不符。');return value;}
async function denied(operation,http,code){let rejected=false;try{await operation();}catch(error){rejected=error.cliError==='http_'+http&&error.businessCode===code;}if(!rejected)throw new Error('预期拒绝未得到精确业务码。');}
try{
 if(mode==='login'){await login();console.log(JSON.stringify({realCli:true,deviceApproved:true}));}
 if(mode==='exercise'){
  const capabilities=await call(['capabilities','list','--profile',profile]);
  for(const name of ['preview','complete','rollback'])if(!capabilities.capabilities.some(item=>item.id==='action:sample-contract-'+name&&item.operation==='action'))throw new Error('三个显式动作不完整。');
  const id=records[0].id,previewId=randomUUID(),before=await action('preview',{id},previewId);if(before.data.id!==id||typeof before.data.expectedDigest!=='string')throw new Error('原预览或字符串ID不符。');await task(previewId,'applied');
  await request('/admin/auto/sample_contract/81002/update',{id,name:'CLI预览后已改名'});
  const staleId=randomUUID();await denied(()=>action('complete',{id,expectedDigest:before.data.expectedDigest},staleId),409,'YZP_CONFLICT');await task(staleId,'unknown');
  const fresh=await action('preview',{id});const rollbackId=randomUUID();await denied(()=>action('rollback',{id,expectedDigest:fresh.data.expectedDigest},rollbackId),503,'YZP_OUTCOME_UNKNOWN');await task(rollbackId,'unknown');
  const rolled=(await request('/admin/auto/sample_contract/81002/detail/'+id)).data;if(rolled.remarks!=='待处理'||Number(rolled.amount)!==10||JSON.stringify(rolled.attachments)!==JSON.stringify(records[0].attachments))throw new Error('原回滚没有恢复业务/文件字段。');
  const completeId=randomUUID(),body={id,expectedDigest:fresh.data.expectedDigest};const completed=await action('complete',body,completeId);if(completed.data.id!==id||completed.data.remarks!=='已处理')throw new Error('原正常提交不符。');const completedTask=await task(completeId,'applied');if(completedTask.resultId!==id)throw new Error('正常任务缺少原字符串结果ID。');
  await denied(()=>action('complete',body,completeId),409,'YZP_REQUEST_ALREADY_RECORDED');
  console.log(JSON.stringify({realCli:true,threeActions:true,previewReadOnly:true,staleConflict:true,rollbackUnknown:true,rollbackReadback:true,completeApplied:true,duplicateUuid:true,stringId:true,requestIds:{preview:previewId,stale:staleId,rollback:rollbackId,complete:completeId}}));
 }
 if(mode==='busy'){
  const id=randomUUID();await denied(()=>action('preview',{id:records[1].id},id),503,'YZP_OUTCOME_UNKNOWN');await task(id,'unknown');console.log(JSON.stringify({busyBeforeBusiness:true,taskUnknown:true,requestId:id}));
 }
 if(mode==='revoke'){
  const saved=JSON.parse(await readFile(config,'utf8')).profiles[profile].credentials;
  await call(['auth','logout','--profile',profile]);await request('/admin/sys/agent/me',undefined,{Authorization:'Bearer '+saved.accessToken},401);await login();console.log(JSON.stringify({revoked:true,reapprovedForRestart:true}));
 }
}finally{if(pending)pending.child.kill();}
