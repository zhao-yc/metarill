import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {createRequire} from 'node:module'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const root=process.argv[2],require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const original=await readFile(join(root,'frontend/uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue'),'utf8'),{descriptor,errors}=parse(original);assert.deepEqual(errors,[])
const boxes=[],component=new Function('uniCloud',descriptor.script.content.replace('export default','return'))({mixinDatacom:{props:{collection:String}}})
component.render=Vue.compile(descriptor.template.content,{isCustomElement:tag=>['view','text','checkbox','checkbox-group','radio','radio-group','label'].includes(tag)});component.mounted=function(){boxes.push(this)}
const first='9007199254745001',second='9007199254745002',runtimeDirectory=join(root,'frontend/common/yzp_runtime'),f=mountGenerated(await readFile(join(root,'frontend/pages/yzp/gs_single_source-detail-201.vue'),'utf8'),{},{id:'9007199254746000',parent_id:'71'},{runtimeDirectory,components:{'uni-data-checkbox':component,'uni-load-more':{render(){return Vue.h('loading')}}}})
try{
 f.respond(f.requests.find(x=>x.options.url.includes('/detail/')),{id:'9007199254746000',name:'来源',segment:1,sharedIds:[first],sharedNames:['甲'],checkedIds:[first],checkedNames:['甲']});await f.flush()
 const request=f.requests.filter(x=>x.options.url.endsWith('/findNameIdChecked2204')).at(-1);assert(request);f.respond(request,[{id:first,name:'甲',value:first,text:'甲'},{id:second,name:'乙',value:second,text:'乙'}]);await f.flush()
 const all=boxes.find(x=>x.localdata[0]?.text==='全选'),items=boxes.find(x=>x.localdata[0]?.value===first);assert(all&&items);all.change({detail:{value:['1']}});await f.flush();assert.deepEqual(plain(f.state.entity.checkedIds),[first,second]);items.change({detail:{value:[second]}});await f.flush();assert.deepEqual(plain(f.state.entity.checkedIds),[second]);assert.deepEqual(plain(f.state.checkedIdsSelectAll),[])
 f.state.openSelect2202();await f.flush();let target=f.targets.at(-1);assert(target);assert.equal(String(target.parent_id??''),'');assert.deepEqual(plain(target.selectedIds),[first]);target.cancel();await f.flush();assert.deepEqual(plain(f.state.entity.sharedIds),[first])
 f.state.openSelect2202();await f.flush();target=f.targets.at(-1);target.confirm([{id:second,name:'乙'},{id:first,name:'甲'}]);await f.flush();assert.deepEqual(plain(f.state.entity.sharedIds),[second,first]);assert.deepEqual(plain(f.state.entity.sharedNames),['乙','甲'])
 f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.deepEqual(plain(write.options.data.checkedIds),[second]);assert.deepEqual(plain(write.options.data.sharedIds),[second,first])
 f.state.openSelect2202();await f.flush();target=f.targets.at(-1);f.setPermission(false);target.confirm([{id:'9007199254745003',name:'迟到值'}]);await f.flush();assert.deepEqual(plain(f.state.entity.sharedIds),[second,first]);assert.equal(f.writes().length,1)
}finally{f.app.unmount()}
console.log('通过：包内原多选确认／取消、真实uni-data-checkbox全选／单项、长ID／顺序、全局目标无父参数、整entity及迟到保护；非浏览器验收')
