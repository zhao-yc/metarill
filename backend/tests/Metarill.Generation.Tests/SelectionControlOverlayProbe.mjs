import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/overlay_source-detail-203.vue'),'utf8'),id='9007199254740993',chosen='9007199254741000'
const f=mountGenerated(source,{}, {id,parent_id:'9007199254742000'})
try{
 f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/')),{id,name:'原记录',targetIds:[chosen]});await f.flush();f.state.openSelect25();await f.flush();const target=f.targets.at(-1);assert(target);assert.equal(target.selectedNameField,'id');assert.deepEqual(plain(target.selectedIds),[chosen]);assert.deepEqual(plain(target.selectedNames),[chosen]);target.confirm([{id:'9007199254741001'}]);await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),['9007199254741001']);assert.equal(f.state.entity.targetNames,undefined);assert.equal(f.state.multiSelectionField,'');assert.equal(f.writes().length,0)
}finally{f.app.unmount()}
console.log('通过：页级仅nameField=id继承字段pageId，原弹层props/长ID回执及不写辅助名称')
