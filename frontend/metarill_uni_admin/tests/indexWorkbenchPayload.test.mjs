import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

/** 执行实际Vue方法，确认版本随聚合传递且不改变界面编辑模型。 */
async function fixture(){
 const text=await readFile(new URL('../pages/custom/config_indexes.vue',import.meta.url),'utf8');
 const requests=[],errors=[];
 const context=vm.createContext({uni:{showModal(){}},yzp:{request:options=>requests.push(options),showErrorModal:message=>errors.push(message),showSuccessToast(){}}});
 vm.runInContext(text.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^\s*import .*$/gm,'').replace('export default','globalThis.definition ='),context);
 const definition=context.definition,instance={...definition.data()};
 for(const [name,fn]of Object.entries(definition.methods))instance[name]=fn.bind(instance);
 definition.onLoad.call(instance,{config_table_id:'42'});
 return {instance,requests,errors,text};
}

test('索引快照版本随完整列表提交，系统列真实ID得以保留',async()=>{
 const {instance,requests}=await fixture();
 assert.equal(requests[0].url,'/admin/custom/config_table/index_config');
 requests[0].success({code:200,data:{configTableId:'42',revision:'a'.repeat(64),columnOptions:[{configColumnId:'51',columnName:'id',label:'ID',systemColumn:true}],indexes:[]}});
 instance.addIndex();instance.indexes[0].nameSuffix='account';instance.addColumn(instance.indexes[0]);instance.save();
 const request=requests[1];assert.equal(request.url,'/admin/custom/config_table/save_indexes');
 const body=JSON.parse(JSON.stringify(request.data));
 assert.equal(body.revision,'a'.repeat(64));assert.equal(body.configTableId,'42');
 assert.equal(body.indexes[0].indexName,'yzp_idx_account');assert.equal(body.indexes[0].columns[0].configColumnId,'51');
 assert.equal(Object.hasOwn(body.indexes[0],'clientKey'),false);assert.equal(Object.hasOwn(body.indexes[0].columns[0],'clientKey'),false);
});

test('版本冲突保留现有草稿，不误提示物理索引已删除',async()=>{
 const {instance,requests,errors,text}=await fixture();
 requests[0].success({code:200,data:{revision:'b'.repeat(64),indexes:[]}});
 instance.addIndex();instance.indexes[0].nameSuffix='draft';instance.indexes[0].columns=[{id:'',configColumnId:'51',columnName:'title',sortDirection:'ASC'}];instance.save();
 requests[1].success({code:409,msg:'版本冲突'});
 assert.equal(errors.at(-1),'版本冲突');assert.equal(instance.indexes[0].nameSuffix,'draft');assert.equal(instance.detail.revision,'b'.repeat(64));
 assert.match(text,/保存仅删除索引配置/);assert.doesNotMatch(text,/保存后目标项目会删除对应托管索引/);
});
