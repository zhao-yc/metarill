import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {createRequire} from 'node:module'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..')
const require=createRequire(path.join(repository,'frontend/metarill_uni_admin/package.json'))
const Vue=require('vue'),{parse}=require('@vue/compiler-sfc'),controls=[]
// 原uni-data-checkbox真实script/template参与挂载，不复制全选或change逻辑。
const original=readFileSync(path.join(repository,'frontend/metarill_uni_admin/uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue'),'utf8')
const descriptor=parse(original).descriptor,checkbox=new Function('uniCloud',descriptor.script.content.replace('export default','return'))({})
checkbox.render=Vue.compile(descriptor.template.content,{isCustomElement:()=>true});checkbox.mounted=function(){controls.push(this)}
const source=readFileSync(process.argv[2],'utf8'),longId='9007199254740993'
assert.match(source,/<uni-data-checkbox multiple v-model='yzRoleIdsSelectAll'[^>]+:localdata='\$multiCheckboxRange' @change='yzRoleIdsSelectAllChange'/)
assert.match(source,/<uni-data-checkbox multiple v-model='entity.yzRoleIds'[^>]+:localdata='yzRoleIdsList' @change='yzRoleIdsChange'/)
assert.match(source,/readonlyDisplayText\(entity.yzRoleNames\)/)
assert.doesNotMatch(source,/<MultiSelection|multi_selection_|openMultiSelect/)
const f=mountGenerated(source,{}, {id:'7'}, {components:{'uni-data-checkbox':checkbox}})
await f.flush();f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/7')),{id:'7',name:'原角色账号',yzRoleIds:[longId],yzRoleNames:['甲'],yzAdminTypeId:'1'});await f.flush()
const candidate=f.requests.find(entry=>entry.options.url.endsWith('/findNameIdYzRole1692528723801219073'))
assert(candidate);assert.equal(candidate.options.method,'POST');assert.deepEqual(plain(candidate.options.data.formParameters),{yzManager:{}})
f.respond(candidate,[{id:longId,name:'甲',value:longId,text:'甲'},{id:'2',name:'乙',value:'2',text:'乙'}]);await f.flush()
const all=controls.find(control=>control.multiple&&control.localdata[0]?.text==='全选'),roles=controls.find(control=>control.multiple&&control.localdata[0]?.id===longId)
assert(all&&roles);assert.deepEqual(roles.dataList.filter(item=>item.selected).map(item=>item.value),[longId]);assert.deepEqual(plain(f.state.yzRoleIdsSelectAll),[])
all.change({detail:{value:['1']}});await f.flush();assert.deepEqual(plain(f.state.entity.yzRoleIds),[longId,'2'])
roles.change({detail:{value:['2']}});await f.flush();assert.deepEqual(plain(f.state.entity.yzRoleIds),['2']);assert.deepEqual(plain(f.state.yzRoleIdsSelectAll),[])
all.change({detail:{value:[]}});await f.flush();assert.deepEqual(plain(f.state.entity.yzRoleIds),[])
roles.change({detail:{value:[longId]}});await f.flush();f.state.saveEntity();await f.flush()
const write=f.writes().at(-1);assert(write);assert.equal(write.options.method,'POST');assert.match(write.options.url,/\/update$/);assert.deepEqual(plain(write.options.data.yzRoleIds),[longId]);assert.deepEqual(plain(write.options.data.yzRoleNames),['甲']);assert.equal(write.options.data.id,'7')
f.respond(write,{id:'7',name:'原角色账号',yzRoleIds:[longId],yzRoleNames:['甲'],yzAdminTypeId:'1'});await f.flush();f.setProps({parentReadOnlyCondition:true});await f.flush();assert.match(f.text(),/甲/)
const count=f.writes().length;f.state.yzRoleIdsSelectAllChange();await f.flush();assert.deepEqual(plain(f.state.entity.yzRoleIds),[longId]);assert.equal(f.writes().length,count)
f.app.unmount();console.log('通过：原真实角色复选props/events、原POST和空formParameters、全选/取消/部分选、长ID、整entity与只读名称')
