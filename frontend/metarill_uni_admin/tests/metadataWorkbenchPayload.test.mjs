import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';
const source=await readFile(new URL('../common/metadataWorkbenchPayload.js',import.meta.url),'utf8');
const payload=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));

/** 控制工作台的真实保存方法，确认只裁剪载荷，不修改原始显示实体。 */
for(const [page,query,fn]of [['config_page_columns','config_page_id','pageFieldPatchPayload'],['config_columns_sort','config_table_id','columnOrderPatchPayload']])
 test(page+' 保留原路由/界面并提交白名单与本次读取版本',async()=>{
  const text=await readFile(new URL('../pages/custom/'+page+'.vue',import.meta.url),'utf8'),requests=[],errors=[];
  const context=vm.createContext({...payload,uni:{showModal:()=>{}},yzp:{request:options=>requests.push(options),showErrorModal:message=>errors.push(message)},utils:{}});
  vm.runInContext(text.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^\s*import .*$/gm,'').replace('export default','globalThis.definition ='),context);
  const definition=context.definition,instance={...definition.data(),...definition.methods};
  instance.save();assert.equal(requests.length,0);assert.equal(errors.length,1);
  definition.onLoad.call(instance,{[query]:'42'});
  const original={id:'51',name:'显示名称',configColumnName:'标题',configPageId:'42',insertTime:'系统值',sortNo:'10',isInEdit:0,isMust:null};
  requests[0].success({code:200,data:[original],map:{revision:'a'.repeat(64)}});
  instance.save();const request=requests[1];
  assert.equal(request.header['X-Metarill-Metadata-Revision'],'a'.repeat(64));
  assert.deepEqual(request.data,payload[fn]([original]));
  assert.equal(original.insertTime,'系统值');assert.equal(original.configPageId,'42');
  assert.equal(Object.hasOwn(request.data[0],'name'),false);assert.equal(Object.hasOwn(request.data[0],'insertTime'),false);
 });
