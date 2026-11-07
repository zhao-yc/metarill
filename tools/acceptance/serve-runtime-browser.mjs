import http from 'node:http';
import path from 'node:path';
import {readFile,writeFile,appendFile,realpath} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import {createManagementPreview,validatePreview} from './serve-management-browser.mjs';

const LEGACY='/api/admin/custom/index/sync_runtime_all_projects',STATUS='/api/admin/sys/generation-assistant/runtime-batch/status',LOGIN='/api/admin/sys/yz_manager/login';
const UUID=/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
/** 真实浏览器门面只代理专属回环宿主；控制文件和秘密不进入静态目录或HTTP接口。 */
export function validateRuntimeBrowser(config,now=Date.now()){
 const lease=validatePreview(config,now),browser=new URL(config.browserAddress),run=config.runId;
 if(!/^[a-f0-9]{16}$/.test(run||'')||browser.protocol!=='http:'||browser.hostname!=='metarill-runtime-'+run+'.localhost'||browser.port!==lease.frontend.port||browser.pathname!=='/'||browser.username||browser.password||browser.search||browser.hash)throw new Error('原浏览器独占地址不符');
 for(const key of ['controlDirectory','credentialDirectory'])if(!path.isAbsolute(config[key]||'')||path.normalize(config[key])!==config[key])throw new Error('原浏览器私有目录必须明确且规范');
 const frontend=path.resolve(config.frontend),controls=path.resolve(config.controlDirectory),credentials=path.resolve(config.credentialDirectory);
 if([controls,credentials].some(value=>value===frontend||value.startsWith(frontend+path.sep))||controls===credentials)throw new Error('原浏览器控制/凭据不得由静态站点公开');
 return {...lease,browser,run,controls,credentials};
}
/** 业务响应逐字转发；只在unknown阶段丢失原入队回执并阻断状态运输，绝不合成业务成功。 */
export async function createRuntimeBrowserGateway(config){
 const lease=validateRuntimeBrowser(config);
 // 私有落点禁止链接重定向到静态站点；不靠字符串前缀宣称秘密隔离。
 for(const field of ['frontend','controlDirectory','credentialDirectory'])if(await realpath(config[field])!==path.resolve(config[field]))throw new Error('原浏览器目录不允许链接或重解析跳转');
 const preview=await createManagementPreview(config,path.join(lease.controls,'api-trace.jsonl')),traceFile=path.join(lease.controls,'gateway-trace.jsonl');let sequence=0;
 const trace=async value=>appendFile(traceFile,JSON.stringify({sequence:++sequence,...value})+'\n');
 const phase=async()=>{const state=JSON.parse(await readFile(path.join(lease.controls,'phase.json'),'utf8'));if(!['partial','complete','unknown','restored'].includes(state.phase))throw new Error('原浏览器阶段不合法');return state.phase};
 return http.createServer(async(request,response)=>{
  let pathname='';
  try{
   if(request.headers.host!==lease.browser.host){response.writeHead(403).end('仅限本轮独占主机');return}
   pathname=new URL(request.url,lease.browser).pathname;
   if(![LOGIN,LEGACY,STATUS].includes(pathname)){preview.emit('request',request,response);return}
   if(Date.now()>=lease.expires){response.writeHead(410).end('本轮验收已到期');return}
   const current=await phase();
   if(pathname===STATUS&&current==='unknown'){
    await trace({pathname,method:request.method,httpStatus:503,phase:current,transportFault:true});response.writeHead(503,{'content-type':'text/plain; charset=utf-8','cache-control':'no-store'}).end('本轮只注入运输故障');return;
   }
   const headers={...request.headers,host:lease.backend.host};delete headers.connection;
   const upstream=http.request({hostname:lease.backend.hostname,port:lease.backend.port,path:request.url,method:request.method,headers},result=>{
    const chunks=[];let bytes=0;
    result.on('data',chunk=>{bytes+=chunk.length;if(bytes>1048576){upstream.destroy(new Error('本轮观察响应过大'));return}chunks.push(chunk)});
    result.on('end',async()=>{
     try{
      const body=Buffer.concat(chunks);let value;try{value=JSON.parse(body.toString('utf8'))}catch{}
      if(pathname===LOGIN&&result.statusCode===200&&value?.code===200&&typeof value?.map?.loginStateId==='string'){
       // 只保存本轮真实浏览器登录的会话供独立只读核验，不制造或返回任何额外身份。
       await writeFile(path.join(lease.credentials,'browser-session.json'),JSON.stringify({loginStateId:value.map.loginStateId}),{encoding:'utf8',mode:0o600});
      }
      const batchId=value?.data?.batchId,requestId=request.headers['x-yzp-agent-request-id'];
      if(pathname===LEGACY&&typeof request.headers.sysparams==='string')await writeFile(path.join(lease.credentials,'browser-context.json'),JSON.stringify({sysParams:request.headers.sysparams}),{encoding:'utf8',mode:0o600});
      const observation={pathname,method:request.method,httpStatus:result.statusCode,phase:current,code:Number.isInteger(value?.code)?value.code:null,...(UUID.test(batchId||'')?{batchId}:{}),...(UUID.test(requestId||'')?{requestId}:{}),...(pathname===LEGACY?{sysParamsPresent:typeof request.headers.sysparams==='string',confirmed:request.headers['x-yzp-agent-confirm']==='true'}:{})};
      if(pathname===LEGACY&&UUID.test(batchId||'')){
       const name='enqueued-'+batchId+'.json';
       try{await writeFile(path.join(lease.controls,name),JSON.stringify({...observation,items:value.data.items}),{encoding:'utf8',flag:'wx'})}catch(error){if(error.code!=='EEXIST')throw error}
      }
      await trace({...observation,transportFault:pathname===LEGACY&&current==='unknown'});
      if(pathname===LEGACY&&current==='unknown'){response.destroy();return}
      response.writeHead(result.statusCode,result.headers);response.end(body);
     }catch{if(!response.headersSent)response.writeHead(502);response.end('原浏览器代理观察失败')}
    });
   });
   upstream.on('error',()=>{if(!response.headersSent)response.writeHead(502);response.end('原浏览器宿主不可用')});request.on('aborted',()=>upstream.destroy());request.pipe(upstream);
  }catch{if(!response.headersSent)response.writeHead(502);response.end('本轮原浏览器网关拒绝异常请求')}
 });
}

// 配置不含令牌，服务只监听专属回环端口；正常退出或租期结束时关闭本进程连接。
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
 const config=JSON.parse(await readFile(path.resolve(process.argv[2]),'utf8')),lease=validateRuntimeBrowser(config),server=await createRuntimeBrowserGateway(config);
 const stop=()=>{server.closeAllConnections();server.close()};server.listen(Number(lease.frontend.port),lease.frontend.hostname,()=>console.log(JSON.stringify({runId:lease.run,address:lease.browser.origin,pid:process.pid,realProxy:true})));
 server.on('error',()=>{console.error('原浏览器网关启动失败，未替换任何已有服务');process.exitCode=1});const timer=setTimeout(stop,lease.expires-Date.now());timer.unref();process.on('SIGINT',stop);process.on('SIGTERM',stop);
}
