import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {createRequire} from 'node:module'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const require=createRequire(new URL('../../../frontend/metarill_uni_admin/package.json',import.meta.url)),Vue=require('vue'),{parse}=require('@vue/compiler-sfc')
const root=process.argv[2],scenario=process.argv[3],id='9007199254742001',micro='11:22:33.123456',inputs=[],pickers=[]
let source=await readFile(join(root,'detail.vue'),'utf8')
if(scenario==='missing-binding')source=source.replace(/ @update:modelValue='[^']+'/g,'')
const inputSource=await readFile(new URL('../../../frontend/metarill_uni_admin/uni_modules/uni-easyinput/components/uni-easyinput/uni-easyinput.vue',import.meta.url),'utf8')
const {descriptor,errors}=parse(inputSource);assert.deepEqual(errors,[])
const Input=new Function(descriptor.script.content.replace('export default','return'))()
const mounted=Input.mounted
// 使用原输入组件的props/watch/onClear/onInput；仅其底层平台DOM由挂载宿主承接。
Input.mounted=function(){mounted?.call(this);inputs.push(this)}
Input.render=function(){return Vue.h('actual-easyinput',{value:this.val,disabled:this.disabled})}
const Picker={props:['value','disabled','mode'],emits:['change'],mounted(){pickers.push(this)},methods:{choose(value){this.$emit('change',{detail:{value}})}},render(){return Vue.h('platform-picker',{},this.$slots.default?.())}}
const f=mountGenerated(source,scenario==='embedded'?{is_props:true,props_id:id}:{},scenario==='embedded'?undefined:{id},{components:{'uni-easyinput':Input,picker:Picker}})
let first
try{
 const row={id,name:'时间控件原交互',clockMicro:micro,clockTenth:'11:22:33.1',clockZero:'11:22:33',duration:'-30:01:02.123456',sampleCustomerId:'9007199254742101',sampleCustomerName:'隔离客户',amount:'1.25',remarks:'原整entity',attachments:[]}
 const load=f.requests.find(item=>item.options.url.includes('/detail/'));f.respond(load,row);await f.flush()
 const input=inputs.find(item=>item.modelValue===micro),picker=pickers.find(item=>item.value===micro)
 assert(input&&picker);assert.equal(picker.mode,'time');assert.equal(picker.disabled,false)
 const save=async()=>{f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);const payload=plain(write.options.data);f.respond(write,payload);await f.flush();return payload}
 if(scenario==='missing-binding'){
  input.onClear();await f.flush();assert.equal(input.val,'');assert.equal(f.state.entity.clockMicro,micro);assert.equal((await save()).clockMicro,micro)
 }else if(scenario==='readonly'){
  f.setProps({parentReadOnlyCondition:true});await f.flush();assert.equal(input.disabled,true);assert.equal(picker.disabled,true)
  const before=plain(f.state.entity);input.onInput({detail:{value:'01:02:03.123456'}});input.onClear();picker.choose('07:08');await f.flush()
  assert.deepEqual(plain(f.state.entity),before);f.state.saveEntity();await f.flush();assert.equal(f.writes().length,0)
 }else if(scenario==='embedded'){
  // 原嵌入详情保存即关闭，必须先编辑再只保存一次，不能在已关闭实例上继续输入。
  input.onClear();await f.flush();assert.equal(f.state.entity.clockMicro,'');assert.equal((await save()).clockMicro,'')
  assert(f.events.some(event=>event[0]==='onPopClose'))
 }else{
  assert.equal((await save()).clockMicro,micro)
  input.onClear();await f.flush();assert.equal(f.state.entity.clockMicro,'');assert.equal((await save()).clockMicro,'')
  input.onInput({detail:{value:'04:05:06.123456'}});await f.flush();assert.equal(f.state.entity.clockMicro,'04:05:06.123456');assert.equal((await save()).clockMicro,'04:05:06.123456')
  picker.choose('07:08');await f.flush();assert.equal((await save()).clockMicro,'07:08')
  assert.equal(f.state.entity.duration,row.duration);assert.equal(f.state.entity.clockTenth,row.clockTenth);assert.equal(f.state.entity.clockZero,row.clockZero)
  assert.equal(typeof f.state.entity.id,'string')
 }
}catch(error){first=error;throw error}finally{try{f.app.unmount()}catch(error){if(!first)throw error}}
console.log('通过：原时间输入/清空/picker与整entity提交、只读迟到事件或缺绑定阴性；真实Vue，非浏览器或数据库验收：'+scenario)
