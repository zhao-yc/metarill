import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {createRequire} from 'node:module'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/relation_source-detail-201.vue'),'utf8')
const original=await readFile(new URL('../../../frontend/metarill_uni_admin/uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue',import.meta.url),'utf8')
const {descriptor,errors}=parse(original);assert.deepEqual(errors,[])
const boxes=[],component=new Function('uniCloud',descriptor.script.content.replace('export default','return'))({mixinDatacom:{props:{collection:String}}})
component.render=Vue.compile(descriptor.template.content,{isCustomElement:tag=>['view','text','checkbox','checkbox-group','radio','radio-group','label'].includes(tag)});component.mounted=function(){boxes.push(this)}
const id='9007199254748888',first='9007199254740993',second='9007199254742001'
const f=mountGenerated(source,{}, {id},{components:{'uni-data-checkbox':component,'uni-load-more':{render(){return Vue.h('loading')}}}})
let firstError
try{
 f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),{id,name:'复选记录',targetIds:[first],targetNames:['甲']});await f.flush()
 const candidate=f.requests.filter(item=>item.options.url.endsWith('/findNameIdTarget25')).at(-1);assert(candidate);assert.equal(candidate.options.method,'POST');assert(!Object.hasOwn(candidate.options.data,'id'))
 f.respond(candidate,[{id:first,name:'甲',value:first,text:'甲'},{id:second,name:'乙',value:second,text:'乙'}]);await f.flush()
 const all=boxes.find(box=>box.localdata[0]?.text==='全选'),items=boxes.find(box=>box.localdata[0]?.value===first)
 assert(all&&items);assert.equal(items.multiple,true);assert.equal(items.dataList[0].selected,true)
 all.change({detail:{value:['1']}});await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[first,second]);assert.deepEqual(plain(f.state.targetIdsSelectAll),[1])
 items.change({detail:{value:[second]}});await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[second]);assert.deepEqual(plain(f.state.targetIdsSelectAll),[]);assert.equal(typeof f.state.entity.targetIds[0],'string')
 f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.deepEqual(plain(write.options.data.targetIds),[second]);assert.deepEqual(plain(write.options.data.targetNames),['甲'])
 f.setProps({parentReadOnlyCondition:true});await f.flush();f.state.targetIdsSelectAll=[1];f.state.targetIdsSelectAllChange();await f.flush();assert.deepEqual(plain(f.state.entity.targetIds),[second]);assert.equal(f.writes().length,1)
}catch(error){firstError=error;throw error}finally{try{f.app.unmount()}catch(error){if(!firstError)throw error}}
console.log('通过：原生成详情与真实uni-data-checkbox Vue3组件、全选／单项／长ID／只读／原整entity回传；不代表浏览器验收')
