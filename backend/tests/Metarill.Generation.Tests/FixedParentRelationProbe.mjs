import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const id='9007199254740993',parent='9007199254742000',selected='9007199254743000'
for(const [table,page,column,parentField,readonly] of [['config_index_column','2076977554350190593','2076977805534564352','configIndexId',true],['config_page_column','6218036784398336','1676901024774926338','configPageId',false]]) {
 const source=await readFile(join(process.argv[2],`frontend/pages/yzp/${table}-detail-${page}.vue`),'utf8')
 assert(source.includes('<zxz-uni-data-select'))
 const fixture=mountGenerated(source,{}, {id,parent_id:parent})
 try {
  fixture.storage[`formParameters${page}`]={configTableId:'客户端假归属不能授权'}
  fixture.respond(fixture.requests.find(entry=>entry.options.url.includes('/detail/')),{id,[parentField]:parent,configColumnId:selected,configColumnName:'旧名称'})
  await fixture.flush()
  const candidates=()=>fixture.requests.filter(entry=>entry.options.url.endsWith(`/findNameIdConfigColumn${column}`))
  // 原整页只读保持只展示详情、不自动加载编辑候选；直接调用生成方法单独验证旧只读id合同。
  if(readonly){assert.equal(candidates().length,0);fixture.state[`loadOriginalOptions${column}`]()}
  const request=candidates().at(-1);assert(request)
  assert.equal(request.options.data.parentId,parent)
  assert.equal(Object.hasOwn(request.options.data,'targetParentId'),false)
  assert.equal(request.options.data.id,readonly?selected:undefined)
  assert.equal(fixture.dialogs.some(dialog=>String(dialog.content).includes('关联目标父范围尚未明确')),false)
  fixture.respond(request,[{id:selected,name:'合法名称',value:selected,text:'合法名称'}])
  assert.equal(fixture.state.config_column_list[0].name,'合法名称')
  fixture.state[`loadOriginalOptions${column}`]();const pending=candidates().at(-1)
  fixture.state.parent_id='9007199254742001';fixture.state[`loadOriginalOptions${column}`]();const current=candidates().at(-1)
  assert.equal(current.options.data.parentId,'9007199254742001')
  fixture.respond(pending,[{id:selected,name:'旧父迟到'}]);assert.deepEqual(plain(fixture.state.config_column_list),[])
  fixture.respond(current,[{id:selected,name:'新父回读'}]);assert.equal(fixture.state.config_column_list[0].name,'新父回读')
  assert.equal(fixture.state.entity.configColumnId,selected)
 } finally {fixture.app.unmount()}
}
console.log('通过：两个原zxz列表仅传来源parentId，服务器归属无新props，长ID/只读选中/父切换迟到隔离保持')
