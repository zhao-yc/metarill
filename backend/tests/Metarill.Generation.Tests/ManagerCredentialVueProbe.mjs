import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {createRequire} from 'node:module'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..'),require=createRequire(path.join(root,'frontend/metarill_uni_admin/package.json'))
const Vue=require('vue'),{parse}=require('@vue/compiler-sfc'),controls=[]
// 原checkbox真实script和template保持“是”1/“否”0，不在UI反转值。
const original=readFileSync(path.join(root,'frontend/metarill_uni_admin/uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue'),'utf8'),descriptor=parse(original).descriptor
const checkbox=new Function('uniCloud',descriptor.script.content.replace('export default','return'))({})
checkbox.render=Vue.compile(descriptor.template.content,{isCustomElement:()=>true});checkbox.mounted=function(){controls.push(this)}
const f=mountGenerated(readFileSync(process.argv[2],'utf8'),{},{id:'7'},{components:{'uni-data-checkbox':checkbox}})
await f.flush();const row=JSON.parse(readFileSync(process.argv[3],'utf8'));f.respond(f.requests.find(entry=>entry.options.url.includes('/detail/7')),row);await f.flush()
assert.equal(f.state.entity.password,'********');assert.equal(f.state.entity.salt,'');assert.equal(f.state.entity.oldPassword,'');assert.equal(f.state.entity.isDisabled,1)
const enabled=controls.find(control=>control.multiple!==true&&control.modelValue===1);assert(enabled);assert.equal(enabled.dataList.find(item=>item.selected).text,'是')
f.state.saveEntity();await f.flush();let write=f.writes().at(-1);assert(write);assert.equal(write.options.data.password,'********');assert.equal(write.options.data.salt,'');assert.equal(write.options.data.oldPassword,'');assert.equal(write.options.data.isDisabled,1)
f.respond(write,row);await f.flush();enabled.change({detail:{value:'0'}});await f.flush();assert.equal(f.state.entity.isDisabled,0)
f.state.entity.password='真实新口令需要至少十二字符';f.state.saveEntity();await f.flush();write=f.writes().at(-1);assert.equal(write.options.data.password,'真实新口令需要至少十二字符');assert.equal(write.options.data.isDisabled,0)
assert.equal(plain(f.state.entity).id,'7');f.app.unmount();console.log('原账号必填密码掩码、整entity保存及原启用0/1控件模型通过')
