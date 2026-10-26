import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {join} from 'node:path'
import {mountGenerated,plain} from './GeneratedOriginalVueHarness.mjs'
const instances=[]
// 文件控件宿主替身只发原v-model事件；本探针验证包内原页面上下文，不代替真实浏览器或上传。
const FileHost={props:['modelValue','configColumnId','pageId','tableId','recordId','parentId','parentPageSn','accessMode','readonly'],emits:['update:modelValue'],mounted(){instances.push(this)},methods:{choose(value){if(!this.readonly)this.$emit('update:modelValue',value)}},render(){return null}}
const root=process.argv[2],source=await readFile(join(root,'pages/yzp/shared_parent_source-detail-202.vue'),'utf8'),id='9007199254741001',parent='9007199254742001',fileId='9007199254740993'
const f=mountGenerated(source,{},{id,parent_id:parent},{runtimeDirectory:join(root,'common/yzp_runtime'),components:{'yzp-file-library':FileHost}})
try{
 f.respond(f.requests.find(r=>r.options.url.includes('/detail/')),{id,name:'共享父范围文件',countryId:parent,areaId:'9007199254743001',placeId:'9007199254744001',warehouseId:'9007199254745001',attachments:[fileId]});await f.flush()
 const file=instances.at(-1);assert(file);assert.equal(file.configColumnId,'9001');assert.equal(file.pageId,'202');assert.equal(file.tableId,'2');assert.equal(file.parentId,parent);assert.equal(file.recordId,id);assert.equal(file.accessMode,'PROTECTED');assert.equal(file.readonly,false);assert.deepEqual(plain(file.modelValue),[fileId])
 file.choose([]);await f.flush();assert.deepEqual(plain(f.state.entity.attachments),[]);f.state.saveEntity();await f.flush();const write=f.writes().at(-1);assert(write);assert.strictEqual(write.options.data,f.state.entity);assert.deepEqual(plain(write.options.data.attachments),[]);assert.match(write.options.url,new RegExp('parentId='+parent));assert.equal(write.options.data.warehouseId,'9007199254745001')
 const count=f.writes().length;f.setPermission(false);await f.flush();assert.equal(file.readonly,true);file.choose([fileId]);await f.flush();f.state.saveEntity();await f.flush();assert.deepEqual(plain(f.state.entity.attachments),[]);assert.equal(f.writes().length,count)
}finally{f.app.unmount()}
console.log('通过：完整包原文件字段父上下文、字符串ID、整entity数组清空与原权限只读；文件控件宿主替身，非浏览器验收')
