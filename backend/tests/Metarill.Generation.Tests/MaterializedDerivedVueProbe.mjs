import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated} from './GeneratedOriginalVueHarness.mjs'
const directory=process.argv[2]
const cases=JSON.parse(await readFile(join(directory,'join-ui-cases.json'),'utf8'))
// 真实Vue3编译挂载原详情：后端写白名单收紧不能改原readonly、rules与整entity提交。
for(const scenario of cases){
 const source=await readFile(join(directory,scenario.file),'utf8')
 const f=mountGenerated(source,{},{});
 try{
  await f.flush()
  assert.equal(f.state.entity_column_readonly.configTableTableName,scenario.readOnly)
  assert.equal(f.state.configTableTableNameReadOnly,scenario.readOnly)
  const rules=f.state.entity_rules.configTableTableName?.rules||[]
  assert.equal(rules.some(rule=>rule.required===true),scenario.required)
  if(scenario.required){
   f.state.entity.name='原输入';f.state.entity.configTableId='10';f.state.entity.configTableTableName='完整回显字段'
   f.state.saveEntity();await f.flush();assert.equal(f.validations(),1)
   const request=f.writes().at(-1);assert(request);assert.equal(request.options.data.configTableTableName,'完整回显字段');assert.strictEqual(request.options.data,f.state.entity)
  }
 }finally{f.app.unmount()}
}
console.log('通过：4组原冗余/非冗余join readonly、真实rules和原整entity事件；未作为浏览器或后端验收')
