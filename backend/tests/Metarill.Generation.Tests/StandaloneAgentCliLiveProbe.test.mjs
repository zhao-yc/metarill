import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { spawn } from 'node:child_process';
import { mkdir, rmdir, realpath, lstat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

// 只替换HTTP传输，执行真实探针及CLI；本用例不证明OAuth身份或数据库验收。
test('审批401保留安全阶段并不泄漏秘密', { timeout: 20000 }, async () => {
 const source=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..');
 const local=path.join(source,'.local');await mkdir(local,{recursive:true});assert.equal((await lstat(local)).isSymbolicLink(),false);
 const evidence=path.join(local,'probe-privacy-'+randomUUID());assert.equal(path.dirname(evidence),local);await mkdir(evidence);
 const privateMarker='PRIVATE-RESPONSE-'+randomUUID(),webToken='PRIVATE-WEB-'+randomUUID(),deviceSecret='PRIVATE-DEVICE-'+randomUUID();let approvals=0,child;
 const server=http.createServer(async (request,response)=>{
  for await (const _ of request) { /* 消费请求，私有正文不写日志。 */ }
  response.setHeader('Content-Type','application/json');
  if(request.url==='/api/agent/oauth/device')return response.end(JSON.stringify({device_code:deviceSecret,user_code:'ABCD2345',verification_uri:`http://127.0.0.1:${server.address().port}/approve`,expires_in:60,interval:1}));
  if(request.url==='/api/admin/sys/agent/approve'){approvals++;assert.equal(request.headers.loginstateid,webToken);response.statusCode=401;return response.end(JSON.stringify({code:401,msg:privateMarker}));}
  response.statusCode=400;response.end(JSON.stringify({error:'authorization_pending'}));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try {
  child=spawn(process.execPath,[path.join(source,'backend/tests/Metarill.Generation.Tests/StandaloneAgentCliLiveProbe.mjs'),`http://127.0.0.1:${server.address().port}/api`,'2200000000000100020','2200000000000100000',source,evidence],{cwd:source,env:{...process.env,METARILL_AGENT_WEB_TOKEN:webToken},windowsHide:true,detached:process.platform!=='win32'});
  let stdout='',stderr='';child.stdout.on('data',value=>stdout+=value.toString());child.stderr.on('data',value=>stderr+=value.toString());
  // 超时只终止本用例拥有的进程树，避免CLI子进程遗留；正常失败走探针原finally。
  const timer=setTimeout(()=>{if(child.exitCode!==null)return;if(process.platform==='win32')spawn('taskkill',['/PID',String(child.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});else process.kill(-child.pid,'SIGKILL');},15000);
  let code;try{code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',resolve);});}finally{clearTimeout(timer);}
  assert.equal(approvals,1);assert.notEqual(code,0);assert.equal(stdout,'');
  assert.match(stderr,/phase: ['"]device-decision['"]/);assert.match(stderr,/httpStatus: 401/);
  for(const secret of [privateMarker,webToken,deviceSecret])assert.equal(stderr.includes(secret),false);
 } finally {
  server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
  assert.equal(await realpath(evidence),evidence);await rmdir(evidence);
 }
});
