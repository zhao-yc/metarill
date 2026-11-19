import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend=join(process.argv[2],'frontend')
const runtimeDirectory=join(frontend,'common/yzp_runtime')
const load=async filename=>readFile(join(frontend,'pages/yzp',filename),'utf8')
const detail=await load('summary_form-detail-102.vue')
const listing=await load('customer-list-103.vue')
const form=mountGenerated(detail,{},{},{runtimeDirectory})
const ids=['9007199254740993','9007199254740994']
const parents=['9007199254742000','9007199254742001']
let target
try {
 await form.flush()
 form.state.openSelect39()
 await form.flush()
 assert.equal(form.targets.length,0)
 assert.match(form.dialogs.at(-1).content,/先选择或保存父记录/)

 form.state.entity.reviewCountryId=parents[0]
 form.state.openSelect39()
 await form.flush()
 const earlier=form.targets.at(-1)
 assert.equal(earlier.parent_id,parents[0])
 form.state.entity.reviewCountryId=parents[1]
 await form.flush()
 earlier.confirm([{id:ids[0],name:'旧范围',countryLabel:'旧范围'}])
 await form.flush()
 assert.equal(form.state.entity.reviewIds?.length??0,0)

 form.state.entity.reviewIds=[ids[1]]
 form.state.openSelect39()
 await form.flush()
 const current=form.targets.at(-1)
 assert.equal(current.parent_id,parents[1])
 const {parent_id,selectedIds,selectedNameField}=current.$props
 target=mountGenerated(listing,{parent_id,selectedIds,selectedNameField},{},{runtimeDirectory})
 await target.flush()
 const selected=()=>target.requests.filter(request=>request.options.url.endsWith('/selection/selected')).at(-1)
 const initial=selected()
 const page=target.pages().at(-1)
 assert.equal(initial.options.data.parentId,parents[1])
 assert.equal(page.options.data.parentId,parents[1])
 assert.deepEqual(plain(initial.options.data.selectedIds),[ids[1]])
 const original={id:ids[1],[selectedNameField]:'当前范围'}
 target.respond(initial,[original])
 target.respond(page,{records:[original],total:1,pages:1})
 await target.flush()
 target.state.sureSelect()
 const confirm=selected()
 assert.notEqual(confirm,initial)
 assert.equal(confirm.options.data.parentId,parents[1])
 target.respond(confirm,[original])
 await target.flush()
 current.confirm(target.events.find(event=>event[0]==='onPopSelect')[1])
 await form.flush()
 assert.deepEqual(plain(form.state.entity.reviewIds),[ids[1]])
 const label=selectedNameField==='countryLabel'?'reviewCountryLabels':'reviewNames'
 assert.deepEqual(plain(form.state.entity[label]),['当前范围'])
 assert.equal(form.writes().length,0)
} finally {
 target?.app.unmount()
 form.app.unmount()
}