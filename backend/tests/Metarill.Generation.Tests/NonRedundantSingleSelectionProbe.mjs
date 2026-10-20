import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],parent=process.argv[3]==='parent'
const text="中文_%!引号'"
const source=await readFile(join(root,'frontend/pages/yzp/mt_nrs_offline_order-list-101.vue'),'utf8')
const list=mountGenerated(source,{parent_id:'71',is_son_page:true})
try{
 list.state.filters.customerName=text;list.state.filterList();const request=list.pages().at(-1)
 assert.deepEqual(plain(request.options.data.filters),{customerName:text})
 list.state.filters.customerName='后续草稿';assert.equal(request.options.data.filters.customerName,text)
}finally{list.app.unmount()}
const detail=mountGenerated(await readFile(join(root,'frontend/pages/yzp/mt_nrs_offline_order-detail-101.vue'),'utf8'),{},{id:'9007199254741001',parent_id:'71'})
try{
 detail.respond(detail.requests.find(request=>request.options.url.includes('/detail/')),{id:'9007199254741001',name:'源记录',customerId:'101',customerName:'原名称'})
 await detail.flush();detail.state.openSelect15();await detail.flush();let target=detail.targets.at(-1);assert(target)
 assert.equal(String(target.parent_id),parent?'71':'');target.cancel();await detail.flush();assert.equal(detail.state.entity.customerId,'101')
 detail.state.openSelect15();await detail.flush();target=detail.targets.at(-1);target.confirm({id:'9007199254740993',name:'新名称'});await detail.flush()
 assert.equal(detail.state.entity.customerId,'9007199254740993');assert.equal(detail.state.entity.customerName,'新名称')
 detail.state.openSelect15();await detail.flush();target=detail.targets.at(-1);detail.setPermission(false);target.confirm({id:'102',name:'迟到结果'});await detail.flush();assert.equal(detail.state.entity.customerId,'9007199254740993')
}finally{detail.app.unmount()}
console.log('通过：原单选页名称筛选/草稿冻结、取消、确认、长ID、父上下文与撤权迟到保护；网络和宿主替身不代表浏览器验收')
