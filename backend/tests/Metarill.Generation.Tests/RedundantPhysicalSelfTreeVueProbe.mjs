import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {createRequire} from 'node:module'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const root=process.argv[2],mode=process.argv[3],same=mode==='same',shared=['shared','globalShared'].includes(mode),mixed=mode==='mixed',cid=same?'6':shared?'6101':mixed?'81':'25',key=same?'bookmarkId':'treeId',name=same?'bookmarkName':'treeName'
const source=await readFile(join(root,'pages/yzp/r1_self_'+(same?'node':'source')+'-detail-201.vue'),'utf8'),original=await readFile(join(root,'components/yzp-cascade-tree/yzp-cascade-tree.vue'),'utf8')
const {descriptor,errors}=parse(original);assert.deepEqual(errors,[])
const calls=[],trees=[],yzp={request(o){calls.push(o);return{abort(){}}}},component=new Function('yzp',descriptor.script.content.replace(/^import[^\n]*\n/gm,'').replace('export default','return'))(yzp)
component.render=Vue.compile(descriptor.template.content);component.mounted=function(){trees.push(this)}
const Stub={render(){return Vue.h('host',{},this.$slots.default?.())}},id='9007199254748888',parent='9007199254740993',leaf='9007199254742001',next='9007199254743002'
const f=mountGenerated(source,{}, {id},{runtimeDirectory:join(root,'common/yzp_runtime'),components:{'yzp-cascade-tree':component,'uni-icons':Stub,'uni-load-more':Stub,'scroll-view':Stub}}),answered=new Set()
/** 只替换网络；原Vue控件的路径、字符串ID、整entity、清空及只读均实际执行。 */
function respond(c,data){answered.add(c);c.success({code:200,data});c.complete?.({})}
async function pump(){for(let i=0;i<8;i++){for(const c of calls.filter(c=>!answered.has(c))){if(c.url.endsWith('/cascadeTreePath'+cid))respond(c,{path:[{id:parent,name:'根'},{id:leaf,name:'叶'}],displayText:'根/叶'});else if(c.url.endsWith('/cascadeTreeOptions'+cid))respond(c,c.data.selectedPath.parentId===parent?[{id:leaf,name:'叶',hasChildren:false}]:c.data.selectedPath.parentId?[]:[{id:parent,name:'根',hasChildren:true},{id:next,name:'新根',hasChildren:false}]);else if(/\/cascadeTreeOptions(25|80)$/.test(c.url))respond(c,c.data.levelKey==='country'?[{id:parent,name:'国家'}]:c.data.levelKey==='地区-二'?[{id:leaf,name:'地区'}]:[{id:next,name:'末级'}])}await f.flush()}}
let firstError
try{
 const data={id,name:'记录',[key]:leaf,[name]:'叶',[key+'DisplayText']:'根/叶'}
 if(mixed||shared)Object.assign(data,{notes:'备注',countryId:parent,countryName:'国家',areaId:leaf,areaName:'地区',placeId:next,placeName:'城市',placeIdDisplayText:'国家/地区/城市'})
 if(shared)Object.assign(data,{warehouseId:next,warehouseName:'仓库',warehouseIdDisplayText:'国家/地区/仓库'})
 if(mixed)Object.assign(data,{choiceIds:[parent],choiceNames:['根']})
 if(mode==='double')Object.assign(data,{alternateId:parent,alternateName:'根'})
 f.respond(f.requests.find(r=>r.options.url.includes('/detail/')),data);await f.flush();const self=trees.find(t=>t.leafIdProperty===key);assert(self);assert(self.isSelfTreeMode)
 const before=plain(f.state.entity);self.handleInput();await f.flush();await pump();assert.equal(self.displayText,'根/叶');assert(calls.some(c=>c.url.endsWith('/cascadeTreePath'+cid)))
 self.activeLevelIndex=0;self.handleOptionSelect({id:next,name:'新根',hasChildren:false},0);await f.flush();assert.equal(f.state.entity[key],next);assert.equal(f.state.entity[name],'新根');assert.equal(typeof f.state.entity[key],'string')
 for(const field of ['countryId','areaId','placeId','warehouseId','alternateId','choiceIds'])if(field in before)assert.deepEqual(plain(f.state.entity[field]),before[field])
 assert.equal(f.writes().length,0);f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.equal(write.options.data[key],next)
 self.clear();await f.flush();assert.equal(f.state.entity[key],'');assert.equal(f.state.entity[name],'');f.setProps({parentReadOnlyCondition:true});await f.flush();f.state['receiveCascadeTree'+cid]({[key]:leaf,[name]:'只读不能写'});await f.flush();assert.equal(f.state.entity[key],'');assert.equal(f.writes().length,1)
}catch(error){firstError=error;throw error}finally{try{f.app.unmount()}catch(error){if(!firstError)throw error}}
console.log('通过：原R1详情及真实自树Vue，路径、长ID、清空、只读、整entity及其它级联／多选互不覆盖；不计浏览器验收')
