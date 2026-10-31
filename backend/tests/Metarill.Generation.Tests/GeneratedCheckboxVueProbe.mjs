import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {createRequire} from 'node:module'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..')
const require=createRequire(path.join(repository,'frontend/metarill_uni_admin/package.json'))
const Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
// 使用原uni-data-checkbox的真实script/template，不复制其严格等值与change实现。
const original=readFileSync(path.join(repository,'frontend/metarill_uni_admin/uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue'),'utf8')
const descriptor=parse(original).descriptor,controls=[]
const checkbox=new Function('uniCloud',descriptor.script.content.replace('export default','return'))({})
checkbox.render=Vue.compile(descriptor.template.content,{isCustomElement:()=>true})
checkbox.mounted=function(){controls.push(this)}
const f=mountGenerated(readFileSync(process.argv[2],'utf8'),{}, {id:'7'}, {components:{'uni-data-checkbox':checkbox}})
await f.flush()
const request=f.requests.find(entry=>entry.options.url.includes('/detail/7'))
const row=JSON.parse(readFileSync(process.argv[3],'utf8'));f.respond(request,row);await f.flush()
assert.equal(f.state.entity.flag,1);assert.equal(f.state.entity.overlayFlag,0);assert.equal(f.state.entity.normalFlag,true)
assert.equal(controls.length,2)
const yes=controls.find(control=>control.modelValue===1),no=controls.find(control=>control.modelValue===0)
assert.equal(yes.dataList.find(item=>item.selected).value,1)
assert.equal(no.dataList.find(item=>item.selected).value,0)
yes.change({detail:{value:'0'}});await f.flush()
assert.equal(f.state.entity.flag,0);assert.equal(yes.dataList.find(item=>item.selected).value,0)
f.state.saveEntity();await f.flush()
const write=f.writes().at(-1);assert(write,'必须发出原整entity保存请求')
assert.equal(write.options.data.flag,0);assert.equal(write.options.data.overlayFlag,0);assert.equal(write.options.data.normalFlag,true)
assert.equal(write.options.data.id,'7');assert.equal(write.options.data.name,'原复选框')
f.respond(write,{...row,flag:0});await f.flush()
assert.equal(yes.dataList.find(item=>item.selected).value,0)
no.change({detail:{value:'1'}});await f.flush();assert.equal(f.state.entity.overlayFlag,1)
assert.equal(plain(f.state.entity).id,'7');f.app.unmount()
console.log('原真实复选框数字0/1选中、change、整entity保存与回显通过')
