import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {join} from 'node:path';
import {mountGenerated} from './GeneratedOriginalVueHarness.mjs';
const root=process.argv[2],runtimeDirectory=join(root,'frontend/common/yzp_runtime');
const detail=mountGenerated(await readFile(join(root,'frontend/pages/yzp/cached_consumer-detail-30.vue'),'utf8'),{},{id:'9007199254740993'},{runtimeDirectory});
try {
 detail.respond(detail.requests.find(item=>item.options.url.includes('/detail/')),{id:'9007199254740993',name:'原记录',globalSourceId:'9007199254740994',globalSourceGlobalTargetName:'旧下拉名称',selectedGlobalSourceId:'9007199254740994',selectedGlobalSourceGlobalTargetName:'旧单选名称',attachments:null});await detail.flush();
 // 从实际渲染的原下拉派发change，不猜测生成器的方法命名。
 const walk=node=>[node,...(node.children||[]).flatMap(walk)];const selects=walk(detail.tree).filter(node=>(node.props?.dataValue??node.props?.['data-value'])==='id'&&node.props?.modelValue==='9007199254740994'&&typeof node.props?.onChange==='function');assert.equal(selects.length,1);selects[0].props.onChange({id:'9007199254740995',name:'新下拉名称'});assert.equal(detail.state.entity.globalSourceId,'9007199254740995');assert.equal(detail.state.entity.globalSourceGlobalTargetName,'新下拉名称');
 detail.state.openSelect38();await detail.flush();let target=detail.targets.at(-1);assert(target);assert.equal(String(target.parent_id??''),'');target.cancel();await detail.flush();assert.equal(detail.state.entity.selectedGlobalSourceId,'9007199254740994');
 detail.state.openSelect38();await detail.flush();target=detail.targets.at(-1);target.confirm({id:'9007199254740996',globalTargetName:'新单选名称'});await detail.flush();assert.equal(detail.state.entity.selectedGlobalSourceId,'9007199254740996');assert.equal(detail.state.entity.selectedGlobalSourceGlobalTargetName,'新单选名称');
 detail.state.saveEntity();await detail.flush();const write=detail.writes().at(-1);assert(write);assert.strictEqual(write.options.data,detail.state.entity);assert.equal(write.options.data.globalSourceId,'9007199254740995');assert.equal(write.options.data.selectedGlobalSourceId,'9007199254740996');
 detail.state.openSelect38();await detail.flush();target=detail.targets.at(-1);detail.storage.sysParams.yzProjectId='101';target.confirm({id:'9007199254740997',globalTargetName:'迟到选项'});await detail.flush();assert.equal(detail.state.entity.selectedGlobalSourceId,'9007199254740996');
} finally {detail.app.unmount();}
console.log('全局缓存名称目标：原下拉、type=1、字符串ID、整entity及迟到隔离通过；非浏览器或实库验收');
