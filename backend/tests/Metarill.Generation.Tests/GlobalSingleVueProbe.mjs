import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],name=process.argv[3],display=name==='caption'?'selectedCaption':'selectedName',shared=name==='caption'?'sharedCaption':'sharedName',runtimeDirectory=join(root,'frontend/common/yzp_runtime')
const list=mountGenerated(await readFile(join(root,'frontend/pages/yzp/gs_single_source-list-201.vue'),'utf8'),{parent_id:'71',is_son_page:true},{},{runtimeDirectory})
try{list.state.filters[display]="中文_%'";list.state.filterList();const request=list.pages().at(-1);assert.equal(request.options.data.filters[display],"中文_%'");list.state.filters[display]='后续草稿';assert.equal(request.options.data.filters[display],"中文_%'")}finally{list.app.unmount()}
const f=mountGenerated(await readFile(join(root,'frontend/pages/yzp/gs_single_source-detail-201.vue'),'utf8'),{},{id:'9007199254745002',parent_id:'71'},{runtimeDirectory})
try{
 f.respond(f.requests.find(x=>x.options.url.includes('/detail/')),{id:'9007199254745002',name:'来源',sharedId:'9007199254745001',[shared]:'原下拉',selectedId:'9007199254745001',[display]:'原单选',segment:1});await f.flush()
 f.state.openSelect2191();await f.flush();let target=f.targets.at(-1);assert(target);assert.equal(String(target.parent_id??''),'');target.cancel();await f.flush();assert.equal(f.state.entity.selectedId,'9007199254745001')
 f.state.openSelect2191();await f.flush();target=f.targets.at(-1);target.confirm({id:'9007199254745003',[name]:'新名称'});await f.flush();assert.equal(f.state.entity.selectedId,'9007199254745003');assert.equal(f.state.entity[display],'新名称')
 f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.equal(write.options.data.selectedId,'9007199254745003')
 f.state.openSelect2191();await f.flush();target=f.targets.at(-1);f.setPermission(false);target.confirm({id:'9007199254745004',[name]:'迟到值'});await f.flush();assert.equal(f.state.entity.selectedId,'9007199254745003');assert.equal(f.writes().length,1)
}finally{f.app.unmount()}
console.log('通过：包内原列表／详情、原单选取消确认、全局目标无父参数、长字符串ID、名称筛选冻结、整entity与迟到保护；非浏览器验收')
