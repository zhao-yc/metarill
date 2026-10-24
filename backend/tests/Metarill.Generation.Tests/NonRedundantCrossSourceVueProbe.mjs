import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const source=await readFile(join(process.argv[2],'pages/yzp/nr_source_full_source-detail-201.vue'),'utf8'),original=await readFile(join(process.argv[2],'components/yzp-cascade-tree/yzp-cascade-tree.vue'),'utf8')
const {descriptor,errors}=parse(original);assert.deepEqual(errors,[])
const derived=process.argv[3]==='true',sourceLevel=Number(process.argv[4])
const calls=[],trees=[],yzp={request(options){calls.push(options);return{abort(){}}}}
const component=new Function('yzp',descriptor.script.content.replace(/^import[^\n]*\n/gm,'').replace('export default','return'))(yzp)
component.render=Vue.compile(descriptor.template.content);component.mounted=function(){trees.push(this)}
const Stub={render(){return Vue.h('host',{},this.$slots.default?.())}},id='9007199254748888',root='9007199254740993',area='9007199254742001',leaf='9007199254743002',next='9007199254744003'
const f=mountGenerated(source,{}, {id},{components:{'yzp-cascade-tree':component,'uni-icons':Stub,'uni-load-more':Stub,'scroll-view':Stub}})
const answered=new Set(),respond=(call,data)=>{answered.add(call);call.success({code:200,data});call.complete?.({})}
/** 只替换网络响应，实际原组件自行维护层级、对象负载和字符串ID。 */
async function pump(){for(let i=0;i<8;i++){for(const call of calls.filter(call=>!answered.has(call)&&/\/cascadeTreeOptions(25|80)$/.test(call.url))){assert.equal(call.method,'POST');assert.equal(call.sendSessionId,true);const key=call.data.levelKey;respond(call,key==='country'?[{id:root,name:'国甲',hasChildren:true}]:key==='地区-二'?[{id:area,name:'区甲',hasChildren:true},{id:next,name:'新区',hasChildren:true}]:[{id:leaf,name:'城甲',hasChildren:false}])}await f.flush()}}
let firstError
try{
 const detail={id,name:'源记录',status:1,code:'e',amount:1,countryId:root,countryName:'国甲',areaId:area,areaName:'区甲',placeId:leaf,placeName:'城甲',placeIdDisplayText:'国甲/区甲/城甲'}
 if(derived)Object.assign(detail,{companyId:root,displayLabel:'当前记录显示名'})
 if(sourceLevel>0)detail[sourceLevel===1?'nrSourceFullCountryId':'nrSourceFullAreaId']=sourceLevel===1?root:area
 f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),detail);await f.flush();await pump()
 const tree=trees.find(tree=>tree.leafIdProperty==='placeId');assert(tree);assert.equal(tree.isSelfTreeMode,false);assert.equal(tree.displayText,'国甲/区甲/城甲')
 tree.activeLevelIndex=0;tree.handleOptionSelect({id:root,name:'国甲'},0);await f.flush();await pump();assert.equal(f.state.entity.countryId,root);assert.equal(f.state.entity.areaId,'');assert.equal(f.state.entity.placeId,'');assert.equal(tree.displayText,'国甲')
 tree.activeLevelIndex=1;tree.handleOptionSelect({id:next,name:'新区'},1);await f.flush();await pump();assert.equal(f.state.entity.areaId,next);assert.equal(f.state.entity.placeId,'');assert.equal(tree.displayText,'国甲/新区')
 tree.activeLevelIndex=2;tree.handleOptionSelect({id:leaf,name:'新城'},0);await f.flush();assert.equal(f.state.entity.placeId,leaf);assert.equal(tree.displayText,'国甲/新区/新城');assert.equal(typeof f.state.entity.countryId,'string');assert.equal(typeof f.state.entity.areaId,'string');assert.equal(typeof f.state.entity.placeId,'string');assert.equal(f.writes().length,0)
 f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.equal(write.options.data.placeId,leaf);assert.equal(write.options.data.areaId,next);assert.equal(write.options.data.placeName,'新城');assert.equal(plain(write.options.data).id,id)
 tree.clear();await f.flush();for(const field of ['countryId','countryName','areaId','areaName','placeId','placeName'])assert.equal(f.state.entity[field],'')
 f.setProps({parentReadOnlyCondition:true});await f.flush();f.state.receiveCascadeTree25({countryId:root,placeId:leaf});await f.flush();assert.equal(f.state.entity.placeId,'');assert.equal(f.writes().length,1)
}catch(error){firstError=error;throw error}finally{try{f.app.unmount()}catch(error){if(!firstError)throw error}}
console.log('通过：包内原详情和真实跨表Vue组件、来源参与原单控件、各层部分选择、长字符串ID、清空、只读及原整entity回传；非浏览器验收')
