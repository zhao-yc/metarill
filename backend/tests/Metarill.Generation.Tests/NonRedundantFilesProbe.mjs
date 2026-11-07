import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const instances=[]
// 仅替换文件控件底层宿主，验证原页v-model/上下文/只读传递；真实上传与引用由实库HTTP验证。
const FileHost={props:['modelValue','configColumnId','pageId','tableId','recordId','parentId','parentPageSn','accessMode','accept','maxCount','maxSizeMb','extensions','allowLibrary','cropImages','readonly','primaryColor'],emits:['update:modelValue'],mounted(){instances.push(this)},methods:{choose(value){if(!this.readonly)this.$emit('update:modelValue',value)}},render(){return null}}
const detail=await readFile(join(process.argv[2],'frontend/pages/yzp/mt_nrs_offline_order-detail-101.vue'),'utf8')
const f=mountGenerated(detail,{},{id:'9007199254741001',parent_id:'71'},{components:{'yzp-file-library':FileHost}})
try{
 f.respond(f.requests.find(r=>r.options.url.includes('/detail/')),{id:'9007199254741001',name:'文件记录',customerId:'101',customerName:'来源名称',attachments:['9007199254740993']});await f.flush()
 const file=instances.at(-1);assert(file);assert.equal(file.configColumnId,'112');assert.equal(file.pageId,'101');assert.equal(file.tableId,'1');assert.equal(file.parentId,'71');assert.equal(file.recordId,'9007199254741001');assert.equal(file.accessMode,'PROTECTED');assert.equal(file.readonly,false);assert.deepEqual(plain(file.modelValue),['9007199254740993'])
 file.choose([]);await f.flush();assert.deepEqual(plain(f.state.entity.attachments),[]);f.state.saveEntity();await f.flush();await f.flush();const write=f.writes().at(-1);assert(write);assert.deepEqual(plain(write.options.data.attachments),[]);assert.equal(write.options.data.id,'9007199254741001');assert(!('customerNotes' in write.options.data))
 const count=f.writes().length;f.setPermission(false);await f.flush();assert.equal(file.readonly,true);file.choose(['9007199254740994']);await f.flush();f.state.saveEntity();await f.flush();assert.equal(f.writes().length,count)
}finally{f.app.unmount()}
console.log('通过：包内原文件控件上下文、原生数组、清空写入与只读传递；控件/网络宿主替身不代表浏览器或上传验收')
