import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
// 挂载真实生成原详情，名称由响应/原选择回调提供；不改DOM或另造选择器。
const source=readFileSync(process.argv[2],'utf8'),f=mountGenerated(source,{}, {id:'9007199254740993'})
assert.match(source,/readonlyDisplayText\(entity.yzAdminTypeName\)/)
assert.match(source,/<zxz-uni-data-select v-model='entity.yzAdminTypeId'[^>]+dataKey='name' dataValue='id'/)
await f.flush()
f.respond(f.requests.find(item=>item.options.url.includes('/detail/')),{id:'9007199254740993',name:'角色甲',yzAdminTypeId:'9007199254740995',yzAdminTypeName:'平台后台',yzProjectId:'100'})
const options=f.requests.find(item=>item.options.url.endsWith('/findNameIdYzAdminType1692533179766878209'))
assert(options);assert.equal(options.options.method,'POST');assert.deepEqual(plain(options.options.data.formParameters),{yzRole:{}})
f.respond(options,[{id:'9007199254740995',name:'平台后台'}]);await f.flush()
f.setProps({parentReadOnlyCondition:true});await f.flush();assert.match(f.text(),/平台后台/)
f.setProps({parentReadOnlyCondition:false});await f.flush()
f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write)
assert.equal(write.options.data.yzAdminTypeName,'平台后台');assert.equal(write.options.data.yzAdminTypeId,'9007199254740995')
f.respond(write,{id:'9007199254740993',name:'角色甲',yzAdminTypeId:'9007199254740995',yzAdminTypeName:'平台后台',yzProjectId:'100'});await f.flush()
assert.equal(f.state.entity.yzAdminTypeName,'平台后台');f.app.unmount()
console.log('通过：原后台List长ID/名称回读、只读显示及原整entity保存回显')
