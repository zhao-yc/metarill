import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated,plain } from './GeneratedOriginalVueHarness.mjs'
const scenario=process.argv[3],multi=scenario.startsWith('multi'),stored=scenario.startsWith('stored'),reverse=scenario.endsWith('-id')
const source=await readFile(join(process.argv[2],multi||stored?'frontend/pages/yzp/relation_source-list-201.vue':'frontend/pages/yzp/customer-list-101.vue'),'utf8')
const key=stored?'targetName':multi?(reverse?'targetIds':'targetNames'):(reverse?'groupId':'groupName')
const other=stored?'targetId':multi?(reverse?'targetNames':'targetIds'):(reverse?'groupName':'groupId')
const parent=multi||scenario==='stored-same',text='中文"引号_%'
const f=mountGenerated(source,parent?{parent_id:'9007199254742000',is_son_page:true}:{})
try{f.state.filters[key]=text;f.state.filterList();const request=f.pages().at(-1);assert(request);assert.deepEqual(plain(request.options.data.filters),{[key]:text});f.state.filters[key]='后续草稿';assert.equal(request.options.data.filters[key],text);assert.equal(Object.hasOwn(request.options.data.filters,other),false)}finally{f.app.unmount()}
console.log('通过：原列表真实Vue按实际有效控件键提交并冻结文本；此探针不代表后端Query已接线')
