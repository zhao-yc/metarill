import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const output=process.argv[2],idOnly=process.argv[3]==='idOnly',id='9007199254740993',parent='9007199254742001',record='9007199254748888'
// 同一原模板按各页元数据渲染；服务端隐藏缓存不作为本页名称来源。
for(const [page,nameField,namesField] of idOnly?[['203','id',null]]:[['203','id',null],['204','title','targetTitles']]){
 const source=await readFile(join(output,`frontend/pages/yzp/relation_source-detail-${page}.vue`),'utf8')
 const f=mountGenerated(source,{}, {id:record,parent_id:parent})
 try{
  f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),{id:record,name:'来源',parentId:parent,targetIds:[id],...(namesField?{[namesField]:['原标题']}: {})});await f.flush();f.state.changes={};f.state.openSelect25();await f.flush();const target=f.targets.at(-1)
  assert.equal(target.selectedNameField,nameField);assert.deepEqual(plain(target.selectedIds),[id]);assert.deepEqual(plain(target.selectedNames),namesField?['原标题']:[id]);target.cancel();await f.flush();assert.deepEqual(plain(f.state.changes),{})
  f.state.openSelect25();await f.flush();f.targets.at(-1).confirm([{id:'2',[nameField]:nameField==='id'?'2':'当前标题',name:'不应串用的根名'}]);await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),['2']);if(namesField)assert.deepEqual(plain(f.state.entity[namesField]),['当前标题']);assert.deepEqual(Object.keys(f.state.changes),['targetIds']);assert.equal(f.state.changes.targetNames,undefined);assert.equal(f.writes().length,0)
 }finally{f.app.unmount()}
 const list=await readFile(join(output,`frontend/pages/yzp/relation_source-list-${page}.vue`),'utf8'),g=mountGenerated(list,{parent_id:parent},{})
 try{g.state.list=[{id:record,name:'记录',targetIds:[id],targetNames:['不应串用的根名'],targetTitles:['正确标题']}];await g.flush();assert.ok(g.text().includes(namesField?'正确标题':id),g.text());assert.ok(!g.text().includes('不应串用的根名'))}finally{g.app.unmount()}
}
console.log('通过：原ID／标题页面列表与详情、长ID、各自名称映射、原确认／取消及只提交IDs')
