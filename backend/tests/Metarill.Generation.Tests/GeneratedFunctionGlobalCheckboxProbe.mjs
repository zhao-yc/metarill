import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend=join(process.argv[2],'frontend')
const source=await readFile(join(frontend,'pages/yzp/summary_form-detail-102.vue'),'utf8')
const app=mountGenerated(source,{},{},{runtimeDirectory:join(frontend,'common/yzp_runtime')})
const reads=()=>app.requests.filter(entry=>entry.options.url.endsWith('/findNameIdReview39'))
const parents=['9007199254742000','9007199254742001']
const value='9007199254740997'
const candidate={id:value,value,name:'当前全局父级候选',text:'当前全局父级候选'}
try {
 await app.flush()
 assert.equal(reads().length,0)
 app.state.entity.reviewCountryId=parents[0]
 await app.flush()
 const old=reads().at(-1)
 assert.equal(old.options.data.parentId,parents[0])
 app.state.entity.reviewCountryId=parents[1]
 await app.flush()
 const current=reads().at(-1)
 assert.equal(current.options.data.parentId,parents[1])
 assert.equal(Object.hasOwn(current.options.data,'targetParentId'),false)
 app.respond(current,[candidate])
 app.respond(old,[{id:'8',value:'8',name:'旧父候选',text:'旧父候选'}])
 await app.flush()
 assert.deepEqual(plain(app.state.reviewIdsList),[candidate])
 app.state.reviewIdsSelectAll=[1]
 app.state.reviewIdsSelectAllChange()
 await app.flush()
 assert.deepEqual(plain(app.state.entity.reviewIds),[value])
 assert.deepEqual(plain(app.state.entity.reviewNames),[candidate.name])
 app.state.customerButtonHandler63()
 app.dialogs.at(-1).success({confirm:true})
 await app.flush()
 const request=app.requests.find(entry=>entry.options.url==='/admin/sys/config_button/execute')
 assert.deepEqual(plain(request.options.data.entity.reviewIds),[value])
 assert.deepEqual(plain(request.options.data.entity.reviewNames),[candidate.name])
 assert.equal(app.writes().length,0)
} finally {
 app.app.unmount()
}