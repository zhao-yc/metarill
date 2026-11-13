import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const configuration=JSON.parse(process.argv[3]);const rejected=JSON.parse(process.argv[5]);
assert.equal(configuration.enabled,false);assert.notEqual(rejected.code,200);
const clients=[{kind:'original',file:process.argv[2]}, {kind:'generated',file:process.argv[4]}];

/** 只解释实际登录页已有的H5条件标记，不把方法探针当成完整跨端编译。 */
function scriptFor(raw,platform){
 const active=[];const lines=[];
 for(const line of raw.split('\n')){
  const directive=line.match(/^\s*\/\/ #(ifdef|ifndef) (\S+)\s*$/);
  if(directive){assert.equal(directive[2],'H5');active.push(directive[1]==='ifdef'?platform==='H5':platform!=='H5');continue;}
  if(/^\s*\/\/ #endif\s*$/.test(line)){assert.ok(active.length);active.pop();continue;}
  if(active.every(Boolean))lines.push(line);
 }
 assert.equal(active.length,0);
 return lines.join('\n').replace(/^import[^\n]*\n/gm,'').replace('export default','globalThis.definition =');
}

const payloads=[];
for(const client of clients){
 const raw=fs.readFileSync(client.file,'utf8').match(/<script>([\s\S]*?)<\/script>/)?.[1];assert.ok(raw,'真实登录页必须有script');
 const application=client.kind==='generated'?JSON.parse(fs.readFileSync(path.join(path.dirname(client.file),'../common/application.generated.json'),'utf8')):undefined;
 for(const platform of ['H5','APP-PLUS','MP-WEIXIN']){
  const requests=[];const modals=[];
  const context={application,window:platform==='H5'?{}:undefined,utils:{isBlank:value=>value==null||String(value).trim()===''},uni:{showModal:value=>modals.push(value.content)},yzp:{request(options){
   if(options.url.endsWith('/cap-config')){options.success({code:200,data:configuration});options.complete?.();return;}
   assert.equal(options.url,'/admin/sys/yz_manager/login');requests.push(options);options.success(rejected);options.complete?.();
  }}};
  vm.runInNewContext(scriptFor(raw,platform),context);
  const definition=context.definition;const page={...definition.data(),user_name:'original-client-offline',password:'offline-invalid'};
  for(const [name,method] of Object.entries(definition.methods))page[name]=method.bind(page);
  await page.login();assert.equal(requests.length,1);
  const request=requests[0];payloads.push({client:client.kind,platform,url:request.url,method:request.method,contentType:request.header?.['content-type']??null,sendSessionId:request.sendSessionId===true,data:request.data,modals,submittingReset:page.loginSubmitting===false});
 }
}
process.stdout.write(JSON.stringify(payloads));
