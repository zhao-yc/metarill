import test from 'node:test';import assert from 'node:assert/strict';
import http from 'node:http';import {mkdtemp,mkdir,writeFile,readFile,rm} from 'node:fs/promises';import os from 'node:os';import path from 'node:path';
import {createRuntimeBrowserGateway,validateRuntimeBrowser} from './serve-runtime-browser.mjs';
const listen=server=>new Promise(resolve=>server.listen(0,'127.0.0.1',()=>resolve(server.address().port))),stop=server=>new Promise(resolve=>{server.closeAllConnections();server.close(resolve)});
const id='11111111-1111-4111-8111-111111111111',run='0123456789abcdef';
/** 这里后端和静态文件是明确合成的运输合同；不计平台、OAuth或原UI业务验收。 */
test('真实代理逐字转发、私有会话隔离、未知只丢运输并保持同一批次',async()=>{
 const root=await mkdtemp(path.join(os.tmpdir(),'metarill-runtime-browser-'));let backend,gateway;let posts=0;const receipts=[];
 try{
  const frontend=path.join(root,'frontend'),controls=path.join(root,'control'),credentials=path.join(root,'credentials');await mkdir(path.join(frontend,'unpackage/dist/build/web'),{recursive:true});await mkdir(controls);await mkdir(credentials);await writeFile(path.join(frontend,'unpackage/dist/build/web/index.html'),'本测试只验证运输');await writeFile(path.join(controls,'phase.json'),JSON.stringify({phase:'partial'}));
  backend=http.createServer(async(req,res)=>{let body='';for await(const chunk of req)body+=chunk;receipts.push({path:req.url,body});if(req.url.endsWith('/login')){res.writeHead(200,{'content-type':'application/json'}).end(JSON.stringify({code:200,map:{loginStateId:'仅供合成运输的非真实会话'}}));return}posts++;res.writeHead(200,{'content-type':'application/json','x-upstream-test':'unchanged'}).end(JSON.stringify({code:500,msg:'合成等待',data:{batchId:id,items:[]}}))});const backendPort=await listen(backend);
  const temporary=http.createServer();const port=await listen(temporary);await stop(temporary);
  const config={runId:run,leaseId:'a'.repeat(32),expiresAt:new Date(Date.now()+60000).toISOString(),frontend,frontendAddress:'http://127.0.0.1:'+port+'/',backendAddress:'http://127.0.0.1:'+backendPort+'/',browserAddress:'http://metarill-runtime-'+run+'.localhost:'+port+'/',controlDirectory:controls,credentialDirectory:credentials};
  gateway=await createRuntimeBrowserGateway(config);await new Promise(resolve=>gateway.listen(port,'127.0.0.1',resolve));
  // Node fetch不采用调用者Host覆盖；使用真实HTTP明确发送本轮域名，保持产品Host校验。
  const send=(uri,value)=>new Promise((resolve,reject)=>{const body=JSON.stringify(value);const request=http.request({hostname:'127.0.0.1',port,path:uri,method:'POST',headers:{host:new URL(config.browserAddress).host,'content-type':'application/json','content-length':Buffer.byteLength(body),'x-yzp-agent-request-id':id,'x-yzp-agent-confirm':'true',sysParams:encodeURIComponent('本测试不使用真实系统参数')}},response=>{const chunks=[];response.on('data',chunk=>chunks.push(chunk));response.on('end',()=>resolve(new Response(Buffer.concat(chunks),{status:response.statusCode,headers:response.headers})))});request.on('error',reject);request.end(body)});
  const login=await send('/api/admin/sys/yz_manager/login',{});assert.equal(login.status,200);assert.equal(JSON.parse(await readFile(path.join(credentials,'browser-session.json'),'utf8')).loginStateId,'仅供合成运输的非真实会话');
  const first=await send('/api/admin/custom/index/sync_runtime_all_projects',{});assert.equal(first.headers.get('x-upstream-test'),'unchanged');assert.equal(await first.text(),JSON.stringify({code:500,msg:'合成等待',data:{batchId:id,items:[]}}));assert.equal(posts,1);assert.equal(receipts.at(-1).body,'{}');
  await writeFile(path.join(controls,'phase.json'),JSON.stringify({phase:'unknown'}));await assert.rejects(send('/api/admin/custom/index/sync_runtime_all_projects',{}));assert.equal(posts,2);
  const failed=await send('/api/admin/sys/generation-assistant/runtime-batch/status',{batchId:id});assert.equal(failed.status,503);assert.equal(posts,2);
  const observed=JSON.parse(await readFile(path.join(controls,'enqueued-'+id+'.json'),'utf8'));assert.equal(observed.batchId,id);assert.equal(observed.confirmed,true);
  await writeFile(path.join(controls,'phase.json'),JSON.stringify({phase:'restored'}));const restored=await send('/api/admin/sys/generation-assistant/runtime-batch/status',{batchId:id});assert.equal((await restored.json()).data.batchId,id);assert.equal(posts,3);
  const trace=await readFile(path.join(controls,'gateway-trace.jsonl'),'utf8');assert.ok(!trace.includes('仅供合成运输的非真实会话'));assert.ok(!trace.includes('本测试不使用真实系统参数'));
  for(const patch of [{browserAddress:'http://example.com:'+port+'/'},{credentialDirectory:frontend},{runId:'invalid'}])assert.throws(()=>validateRuntimeBrowser({...config,...patch}));
 }finally{if(gateway)await stop(gateway);if(backend)await stop(backend);await rm(root,{recursive:true,force:true})}
});
