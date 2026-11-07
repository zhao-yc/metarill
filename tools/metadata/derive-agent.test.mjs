import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { deriveAgent, renderAgent } from './derive-agent.mjs';
const source = () => JSON.parse(readFileSync(new URL('../../backend/metadata/platform.json', import.meta.url),'utf8'));

// 两个固定模型和三个唯一约束是设备/刷新及任务去重的必要合同。
test('固定两表物理字段与三个唯一约束来自原平台', () => {
 const model=deriveAgent(source());
 assert.deepEqual(model.configTables.map(row=>row.tableName),['sys_agent_grant','sys_agent_task']);
 assert.equal(model.configColumns.length,44);assert.equal(model.configIndexes.length,3);
 assert.ok(model.configIndexes.every(row=>row.isUnique===1&&row.isEnabled===1));
 assert.equal(model.configIndexColumns.filter(row=>row.configIndexId==='2200000000000000742').length,3);
 assert.equal(model.configPages.length+model.configPageColumns.length,0);
});

test('无关展示信息和输入顺序不改变派生结果，也不污染源对象', () => {
 const input=source(),before=JSON.stringify(input),expected=renderAgent(input);
 assert.equal(JSON.stringify(input),before);
 for(const key of ['configTables','configColumns','configIndexes','configIndexColumns'])input[key].reverse();
 input.configTables.find(row=>row.tableName==='sys_agent_task').name='仅修改展示标题';
 assert.equal(renderAgent(input),expected);
});

for (const [name,change] of [
 ['源项目身份',s=>{s.yzProjectId='999';}],
 ['虚拟表',s=>{s.configTables.find(row=>row.tableName==='sys_agent_grant').isView=1;}],
 ['字段类型',s=>{s.configColumns.find(row=>row.id==='2200000000000000335').logicalType='string';}],
 ['物理标志null',s=>{s.configColumns.find(row=>row.id==='2200000000000000335').isVirtualColumn=null;}],
 ['精度null',s=>{s.configColumns.find(row=>row.id==='2200000000000000335').precision=null;}],
 ['字段长度',s=>{s.configColumns.find(row=>row.id==='2200000000000000336').columnLength='255';}],
 ['秘密默认值',s=>{s.configColumns.find(row=>row.id==='2200000000000000341').defaultValue='不许进入生成结果的秘密';}],
 ['关闭去重约束',s=>{s.configIndexes.find(row=>row.id==='2200000000000000742').isEnabled=0;}],
 ['跨表索引',s=>{s.configIndexColumns.find(row=>row.configIndexId==='2200000000000000742').configColumnId='2200000000000000335';}],
 ['索引顺序',s=>{s.configIndexColumns.find(row=>row.configIndexId==='2200000000000000742').sortNo=99;}],
 ['未知额外字段',s=>{s.configColumns.push({...s.configColumns.find(row=>row.id==='2200000000000000335'),id:'999999999999999',columnName:'extra'});}],
]) test('拒绝'+name+'漂移且错误不打印输入值',()=>{
 const input=source();change(input);assert.throws(()=>deriveAgent(input),error=>!error.message.includes('不许进入生成结果的秘密'));
});
