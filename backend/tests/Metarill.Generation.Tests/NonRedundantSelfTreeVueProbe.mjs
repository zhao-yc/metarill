import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const source=await readFile(join(process.argv[2],'frontend/pages/yzp/nr_tree_source-detail-201.vue'),'utf8'),original=await readFile(new URL('../../../frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue',import.meta.url),'utf8')
const {descriptor,errors}=parse(original);assert.deepEqual(errors,[])
const calls=[],trees=[],yzp={request(options){calls.push(options);return{abort(){}}}}
const component=new Function('yzp',descriptor.script.content.replace(/^import[^\n]*\n/gm,'').replace('export default','return'))(yzp)
component.render=Vue.compile(descriptor.template.content);component.mounted=function(){trees.push(this)}
const Stub={render(){return Vue.h('host',{},this.$slots.default?.())}},id='9007199254748888',root='9007199254740993',leaf='9007199254742001',next='9007199254743002'
const f=mountGenerated(source,{}, {id},{components:{'yzp-cascade-tree':component,'uni-icons':Stub,'uni-load-more':Stub,'scroll-view':Stub}})
const answered=new Set();const respond=(call,data)=>{answered.add(call);call.success({code:200,data});call.complete?.({})}
async function pump(){for(let i=0;i<8;i++){for(const call of calls.filter(call=>!answered.has(call)&&call.url.endsWith('/cascadeTreeOptions25')))respond(call,call.data.selectedPath.parentId===root?[{id:leaf,name:'叶',hasChildren:false}]:call.data.selectedPath.parentId?[]:[{id:root,name:'根',hasChildren:true},{id:next,name:'当前新根',hasChildren:false}]);await f.flush()}}
let firstError
try{
 f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),{id,name:'源记录',treeId:leaf,treeName:'叶',treeIdDisplayText:'根/叶'});await f.flush()
 const tree=trees.at(-1);assert(tree);tree.handleInput();await f.flush()
 const path=calls.filter(call=>call.url.endsWith('/cascadeTreePath25')).at(-1);assert(path,JSON.stringify({calls:calls.map(call=>({url:call.url,data:call.data})),trees:trees.map(tree=>({value:tree.value,mode:tree.normalizedDefinition})),entity:f.state.entity}));assert.equal(path.method,'POST');assert.equal(path.data.leafId,leaf);respond(path,{path:[{id:root,name:'根'},{id:leaf,name:'叶'}],displayText:'根/叶'});await f.flush()
 await pump();assert.equal(tree.displayText,'根/叶');assert.equal(tree.leafIdProperty,'treeId');assert.equal(tree.leafNameProperty,'treeName')
 tree.activeLevelIndex=0;tree.handleOptionSelect({id:next,name:'当前新根',hasChildren:false},0);await f.flush();assert.equal(f.state.entity.treeId,next);assert.equal(f.state.entity.treeName,'当前新根');assert.equal(typeof f.state.entity.treeId,'string');assert.equal(tree.displayText,'当前新根')
 await pump();assert.equal(f.writes().length,0)
 f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.equal(write.options.data.treeId,next);assert.equal(write.options.data.treeName,'当前新根');assert.strictEqual(write.options.data,f.state.entity)
 tree.clear();await f.flush();assert.equal(f.state.entity.treeId,'');assert.equal(f.state.entity.treeName,'')
 f.setProps({parentReadOnlyCondition:true});await f.flush();tree.handleOptionSelect({id:leaf,name:'不能写'},0);await f.flush();assert.equal(f.state.entity.treeId,'');assert.equal(f.state.entity.treeName,'')
 assert.equal(f.writes().length,1);assert.equal(plain(write.options.data).id,id)
}catch(error){firstError=error;throw error}finally{try{f.app.unmount()}catch(error){if(!firstError)throw error}}
console.log('通过：原生成详情与真实级联Vue3组件、路径／所选长ID／清空／只读，原整entity回传，物理写边界由C#接口另验；非浏览器验收')
