import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const id='9007199254740993',parent='9007199254742000',selected='9007199254743000'
const row=name=>({id:selected,value:selected,name,text:name})
for(const [table,page,column,parentField,readonly] of [['config_index_column','2076977554350190593','2076977805534564352','configIndexId',true],['config_page_column','6218036784398336','1676901024774926338','configPageId',false]]) {
 const source=await readFile(join(process.argv[2],'frontend/pages/yzp/'+table+'-detail-'+page+'.vue'),'utf8')
 assert(source.includes('<zxz-uni-data-select'))
 const fixture=mountGenerated(source,{}, {id,parent_id:parent}),endpoint='/findNameIdConfigColumn'+column
 try {
  fixture.storage['formParameters'+page]={configTableId:'客户端假归属不能授权'}
  fixture.respond(fixture.requests.find(entry=>entry.options.url.includes('/detail/')),{id,[parentField]:parent,configColumnId:selected,configColumnName:'旧名称'})
  await fixture.flush()
  const reads=()=>fixture.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/selected')),pages=()=>fixture.requests.filter(entry=>entry.options.url.endsWith(endpoint+'/page'))
  const request=reads().at(-1);assert(request);assert.equal(request.options.data.parentId,parent);assert.equal(Object.hasOwn(request.options.data,'targetParentId'),false);assert.deepEqual(request.options.data.selectedIds,[selected]);assert.equal(Object.hasOwn(request.options.data,'formParameters'),false)
  assert.equal(fixture.dialogs.some(dialog=>String(dialog.content).includes('关联目标父范围尚未明确')),false)
  fixture.respond(request,[row('合法名称')]);assert.equal(fixture.state.originalRemoteOptions[column].selected[0].name,'合法名称')
  fixture.state.openOriginalRemoteOptions(column)
  if(readonly)assert.equal(pages().length,0)
  else {const pageRequest=pages().at(-1);assert(pageRequest);assert.equal(Object.hasOwn(pageRequest.options.data,'targetParentId'),false);fixture.respond(pageRequest,{records:[row('第一页')],pages:'1'});assert.equal(fixture.state.config_column_list[0].name,'第一页')}
  const pending=reads().at(-1);fixture.state.parent_id='9007199254742001';const current=reads().at(-1);assert.notEqual(current,pending);assert.equal(current.options.data.parentId,'9007199254742001')
  fixture.respond(pending,[row('旧父迟到')]);assert.deepEqual(plain(fixture.state.config_column_list),[])
  fixture.respond(current,[row('新父回读')]);assert.equal(fixture.state.originalRemoteOptions[column].selected[0].name,'新父回读');assert.equal(fixture.state.entity.configColumnId,selected)
  assert.equal(fixture.requests.filter(entry=>entry.options.url.endsWith(endpoint)).length,0)
 } finally {fixture.app.unmount()}
}
console.log('通过：原zxz分页与已选只传来源parentId，固定服务器归属、只读、长ID与父切换隔离')
