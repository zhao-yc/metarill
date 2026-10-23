import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const source=await readFile(join(process.argv[2],'pages/yzp/nr_cascade_mix_source-detail-201.vue'),'utf8')
const original=await readFile(join(process.argv[2],'components/yzp-cascade-tree/yzp-cascade-tree.vue'),'utf8')
const {descriptor,errors}=parse(original);assert.deepEqual(errors,[])
const calls=[],trees=[],yzp={request(options){calls.push(options);return{abort(){}}}}
const component=new Function('yzp',descriptor.script.content.replace(/^import[^\n]*\n/gm,'').replace('export default','return'))(yzp)
component.render=Vue.compile(descriptor.template.content);component.mounted=function(){trees.push(this)}
const Stub={render(){return Vue.h('host',{},this.$slots.default?.())}}
const id='9007199254748888',country='9007199254740993',area='9007199254742001',place='9007199254743002',root='9007199254744003',leaf='9007199254745004',next='9007199254746005'
const f=mountGenerated(source,{}, {id},{components:{'yzp-cascade-tree':component,'uni-icons':Stub,'uni-load-more':Stub,'scroll-view':Stub}})
const answered=new Set(),respond=(call,data)=>{answered.add(call);call.success({code:200,data});call.complete?.({})}
/** 只替换网络与基础渲染；两个模式的实际原组件在同一详情内维护各自字段。 */
async function pump(){for(let i=0;i<8;i++){for(const call of calls.filter(call=>!answered.has(call))){
 if(call.url.endsWith('/cascadeTreePath81'))respond(call,{path:[{id:root,name:'树根'},{id:leaf,name:'树叶'}],displayText:'树根/树叶'})
 else if(call.url.endsWith('/cascadeTreeOptions81'))respond(call,call.data.selectedPath.parentId===root?[{id:leaf,name:'树叶',hasChildren:false}]:call.data.selectedPath.parentId?[]:[{id:root,name:'树根',hasChildren:true},{id:next,name:'另一根',hasChildren:false}])
 else if(call.url.endsWith('/cascadeTreeOptions25'))respond(call,call.data.levelKey==='country'?[{id:country,name:'国甲',hasChildren:true}]:call.data.levelKey==='地区-二'?[{id:area,name:'区甲',hasChildren:true}]:[{id:place,name:'城甲',hasChildren:false}])
 }await f.flush()}}
let firstError
try{
 f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),{id,name:'混合记录',notes:'备注',countryId:country,countryName:'国甲',areaId:area,areaName:'区甲',placeId:place,placeName:'城甲',placeIdDisplayText:'国甲/区甲/城甲',treeId:leaf,treeName:'树叶',treeIdDisplayText:'树根/树叶',choiceIds:[root],choiceNames:['树根']});await f.flush()
 const cross=trees.find(tree=>tree.leafIdProperty==='placeId'),self=trees.find(tree=>tree.leafIdProperty==='treeId');assert(cross);assert(self);assert.equal(cross.isSelfTreeMode,false);assert.equal(self.isSelfTreeMode,true)
 self.handleInput();await f.flush();await pump();assert.equal(self.displayText,'树根/树叶');assert.equal(cross.displayText,'国甲/区甲/城甲')
 const choices=plain(f.state.entity.choiceIds)
 cross.activeLevelIndex=0;cross.handleOptionSelect({id:country,name:'国甲'},0);await f.flush();await pump();assert.equal(f.state.entity.countryId,country);assert.equal(f.state.entity.areaId,'');assert.equal(f.state.entity.placeId,'');assert.equal(f.state.entity.treeId,leaf);assert.equal(self.displayText,'树根/树叶')
 self.activeLevelIndex=0;self.handleOptionSelect({id:next,name:'另一根',hasChildren:false},0);await f.flush();assert.equal(f.state.entity.treeId,next);assert.equal(f.state.entity.countryId,country);assert.deepEqual(plain(f.state.entity.choiceIds),choices);assert.equal(f.writes().length,0)
 f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.equal(write.options.data.treeId,next);assert.equal(write.options.data.countryId,country);assert.equal(typeof write.options.data.treeId,'string');assert.equal(typeof write.options.data.countryId,'string')
 cross.clear();await f.flush();assert.equal(f.state.entity.countryId,'');assert.equal(f.state.entity.treeId,next);self.clear();await f.flush();assert.equal(f.state.entity.treeId,'');assert.deepEqual(plain(f.state.entity.choiceIds),choices)
 f.setProps({parentReadOnlyCondition:true});await f.flush();f.state.receiveCascadeTree25({countryId:country});f.state.receiveCascadeTree81({treeId:leaf});await f.flush();assert.equal(f.state.entity.countryId,'');assert.equal(f.state.entity.treeId,'');assert.equal(f.writes().length,1)
}catch(error){firstError=error;throw error}finally{try{f.app.unmount()}catch(error){if(!firstError)throw error}}
console.log('通过：包内原详情同页挂载真实自树与跨表Vue组件，互不覆盖、部分选择、原整entity保存、长字符串ID、清空及只读；非浏览器验收')
