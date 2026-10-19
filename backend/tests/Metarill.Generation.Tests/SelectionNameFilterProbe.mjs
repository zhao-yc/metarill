import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const multi=process.argv[3].startsWith('multi'),reverse=process.argv[3].endsWith('-id'),source=await readFile(join(process.argv[2],multi?'frontend/pages/yzp/relation_source-list-201.vue':'frontend/pages/yzp/customer-list-101.vue'),'utf8'),key=multi?(reverse?'targetIds':'targetNames'):(reverse?'groupId':'groupName'),text='中文"引号_%'
const f=mountGenerated(source,multi?{parent_id:'9007199254742000',is_son_page:true}:{})
try{f.state.filters[key]=text;f.state.filterList();const request=f.pages().at(-1);assert(request);assert.deepEqual(plain(request.options.data.filters),{[key]:text});f.state.filters[key]='后续草稿';assert.equal(request.options.data.filters[key],text);assert.equal(Object.hasOwn(request.options.data.filters,multi?(reverse?'targetNames':'targetIds'):(reverse?'groupName':'groupId')),false)}finally{f.app.unmount()}
console.log('通过：原列表真实Vue按实际有效控件键提交并冻结文本；此探针不代表后端Query已接线')
