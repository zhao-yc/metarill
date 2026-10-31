import { spawn } from 'node:child_process';
import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

// 仅供正式随机空库夹具：Web令牌只经环境，私有CLI凭据目录由夹具创建并在结束后移除。
const [base,pageId,projectId,repository,evidence]=process.argv.slice(2),webToken=process.env.METARILL_AGENT_WEB_TOKEN;
const origin=new URL(base);
if(!['127.0.0.1','localhost'].includes(origin.hostname)||origin.pathname!=='/api'||origin.search||origin.hash||!/^\d+$/.test(pageId||'')||!/^\d+$/.test(projectId||'')||!webToken||!repository||!evidence)throw new Error('缺少受限完整包CLI验收参数。');
const evidenceRoot=path.resolve(evidence),allowed=path.join(path.resolve(repository),'.local')+path.sep;
if(!evidenceRoot.startsWith(allowed))throw new Error('CLI私有验收目录必须位于该检出.local内。');
const directory=path.join(evidenceRoot,'cli-'+randomUUID());
if(!directory.startsWith(evidenceRoot+path.sep))throw new Error('CLI临时目录越界。');
await mkdir(directory,{recursive:true});
const cli=path.resolve(repository,'tools/metarill-agent-cli/bin/metarill-agent.mjs');
const env={...process.env,METARILL_AGENT_CONFIG:path.join(directory,'config.json')};
const web={loginStateId:webToken,sysParams:encodeURIComponent(JSON.stringify({yzProjectId:projectId}))};
let loginProcess;

// 业务状态与正文独立校验，错误不输出HTTP响应、连接或凭据。
async function request(route,body,headers={},expected=200,form=false){
 const response=await fetch(base+route,{method:body===undefined?'GET':'POST',redirect:'error',headers:{...headers,...(body===undefined?{}:{'Content-Type':form?'application/x-www-form-urlencoded':'application/json'})},body:body===undefined?undefined:form?new URLSearchParams(body):JSON.stringify(body)});
 const value=await response.json();if(response.status!==expected||value.code!==undefined&&Number(value.code)!==expected)throw new Error('正式Agent接口状态不符。');return value;
}

// 所有CLI命令用真实入口；不向stdout或异常打印原始stderr中的登录上下文。
function run(args,onText){
 const child=spawn(process.execPath,[cli,...args],{cwd:repository,env,windowsHide:true});let output='',error='';
 child.stdout.on('data',bytes=>output+=bytes.toString());child.stderr.on('data',bytes=>{error+=bytes.toString();onText?.(error);});
 const complete=new Promise((resolve,reject)=>{child.on('error',()=>reject(new Error('真实CLI进程未启动。')));child.on('close',code=>{
  if(code===0){try{resolve(JSON.parse(output));}catch{reject(new Error('真实CLI未返回有效JSON。'));}return;}
  // 只保留受限错误码；网络、超时或其他失败不能冒充预期业务拒绝，也不泄露原始stderr。
  let failure={};for(const line of error.trim().split('\n').reverse()){try{const parsed=JSON.parse(line);if(typeof parsed.error==='string'){failure=parsed;break;}}catch{}}
  reject(Object.assign(new Error('真实CLI命令未成功。'),{cliError:/^[a-z_0-9]+$/.test(failure.error||'')?failure.error:'unknown',businessCode:/^YZP_[A-Z_]+$/.test(failure.code||'')?failure.code:''}));
 });});
 return {child,complete};
}
const call=async args=>run(args).complete;

// 显式授权的合成Web账号通过原approve接口作出测试决定，不宣称浏览器人工点击验收。
async function login(profile,approve){
 let decided=false,approvalError;
 loginProcess=run(['auth','login','--url',base,'--profile',profile,'--name','独立Agent正式验收'],text=>{
  const code=/用户码：([A-Z2-9]{8})/.exec(text)?.[1];if(decided||!code)return;decided=true;
  request('/admin/sys/agent/approve',{userCode:code,approve},web).catch(()=>{approvalError=true;loginProcess.child.kill();});
 });
 const timeout=setTimeout(()=>loginProcess.child.kill(),30000);
 try{await loginProcess.complete;if(!approve||approvalError||!decided)throw new Error('正式设备决定不符。');}
 catch(error){if(approve||approvalError||!decided||error.cliError!=='access_denied')throw error;}
 finally{clearTimeout(timeout);loginProcess=null;}
}

try{
 await login('denied',false);await login('acceptance',true);
 const catalog=await call(['capabilities','list','--profile','acceptance']);
 const find=operation=>catalog.capabilities.find(item=>item.pageId===pageId&&item.operation===operation);
 const create=find('create'),remove=find('delete'),detail=find('detail');if(!create||!remove||!detail)throw new Error('独立应用真实目录缺少CRUD。');
 const requestId=randomUUID(),body=JSON.stringify({name:'独立Agent验收-'+randomUUID(),notes:null,amount:3.25});
 const preview=await call(['call',create.id,'--profile','acceptance','--json',body,'--preview','--request-id',requestId]);if(preview.executed!==false)throw new Error('预览不应执行。');
 const added=await call(['call',create.id,'--profile','acceptance','--json',body,'--confirm','--request-id',requestId]);
 const id=added.data.id;if(typeof id!=='string'||!/^\d+$/.test(id))throw new Error('业务ID必须保持字符串。');
 const read=await call(['call',detail.id,'--profile','acceptance','--id',id]);if(read.data.id!==id||read.data.notes!==null)throw new Error('真实读取或显式null合同不符。');
 const task=await call(['tasks','show',requestId,'--profile','acceptance']);if(task.outcome!=='applied'||task.resultId!==id)throw new Error('任务与业务结果不符。');
 let duplicate=false;try{await call(['call',create.id,'--profile','acceptance','--json',body,'--confirm','--request-id',requestId]);}catch(error){duplicate=error.cliError==='http_409'&&error.businessCode==='YZP_REQUEST_ALREADY_RECORDED';}if(!duplicate)throw new Error('重复UUID没有拒绝。');
 await call(['call',remove.id,'--profile','acceptance','--json',JSON.stringify({id}),'--confirm']);
 const credentials=JSON.parse(await readFile(env.METARILL_AGENT_CONFIG,'utf8')).profiles.acceptance.credentials;
 await call(['auth','logout','--profile','acceptance']);
 await request('/admin/sys/agent/me',undefined,{Authorization:'Bearer '+credentials.accessToken},401);
 // 新授权验证刷新重放家族撤销；旧refresh仅在内存使用，不进入日志或任务。
 await login('replay',true);const saved=JSON.parse(await readFile(env.METARILL_AGENT_CONFIG,'utf8')),before={...saved.profiles.replay.credentials};
 // 仅将本轮私有CLI缓存的到期时间置旧，触发真实refresh请求；不修改服务端令牌或认证实现。
 saved.profiles.replay.credentials.expiresAt=Date.now()-1000;await writeFile(env.METARILL_AGENT_CONFIG,JSON.stringify(saved));
 await call(['capabilities','list','--profile','replay']);
 const rotated=JSON.parse(await readFile(env.METARILL_AGENT_CONFIG,'utf8')).profiles.replay.credentials;
 if(rotated.refreshToken===before.refreshToken)throw new Error('真实CLI未完成刷新轮转。');
 const replay=await request('/agent/oauth/token',{client_id:'metarill-agent-cli',grant_type:'refresh_token',refresh_token:before.refreshToken},{},400,true);
 if(replay.error!=='invalid_grant')throw new Error('旧刷新凭据未被拒绝。');
 let family=false;try{await call(['capabilities','list','--profile','replay']);}catch(error){family=error.cliError==='http_401';}if(!family)throw new Error('刷新重放未撤销授权家族。');
 console.log(JSON.stringify({deviceDenied:true,deviceApproved:true,realCli:true,strictCatalog:true,preview:true,create:true,detail:true,explicitNull:true,stringId:true,task:true,duplicateUuid:true,delete:true,revoke:true,refreshReplay:true}));
}finally{
 if(loginProcess)loginProcess.child.kill();
 await rm(directory,{recursive:true,force:true});
}
