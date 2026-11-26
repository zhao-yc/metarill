import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend=join(process.argv[2],'frontend')
const runtimeDirectory=join(frontend,'common/yzp_runtime')
const load=filename=>readFile(join(frontend,'pages/yzp',filename),'utf8')
const detail=await load('summary_form-detail-102.vue')
const targetImport=detail.match(/import OriginalSelection39 from ['"]@\/pages\/yzp\/([^'"]+)['"]/)
assert.ok(targetImport)
const listing=await load(targetImport[1])
const form=mountGenerated(detail,{},{},{runtimeDirectory})
const textParent=process.argv[3]==='text-parent'
const parentA=textParent?'0007-Ab':'9007199254742000',parentB=textParent?'0008-aB':'9007199254742001'
const value='9007199254740997'
let target
try {
 await form.flush()
 form.state.openSelect39()
 await form.flush()
 assert.equal(form.targets.length,0)
 assert.match(form.dialogs.at(-1).content,/先选择或保存父记录/)
 form.state.entity.reviewCountryId=textParent?'   ':'0007'
 form.state.openSelect39()
 await form.flush()
 assert.equal(form.targets.length,0)
 assert.match(form.dialogs.at(-1).content,/先选择或保存父记录/)
 form.state.entity.reviewCountryId=parentA
 form.state.openSelect39()
 await form.flush()
 const earlier=form.targets.at(-1)
 assert.ok(earlier,'有效父键应打开原单选列表')
 assert.equal(earlier.parent_id,parentA)
 form.state.entity.reviewCountryId=parentB
 await form.flush()
 earlier.confirm({id:'9',name:'旧父级',countryLabel:'旧父级'})
 await form.flush()
 assert.ok(!form.state.entity.reviewId)

 form.state.openSelect39()
 await form.flush()
 const current=form.targets.at(-1)
 assert.equal(current.parent_id,parentB)
 const { parent_id,selectedNameField }=current.$props
 target=mountGenerated(listing,{parent_id},{},{runtimeDirectory})
 await target.flush()
 const page=target.pages().at(-1)
 assert.equal(page.options.data.parentId,parentB)
 const chosen={id:value,[selectedNameField]:'当前候选'}
 target.respond(page,{records:[chosen],total:1,pages:1})
 await target.flush()
 target.state.selectThis(target.state.list[0])
 const receipt=target.events.find(event=>event[0]==='onPopSelect')
 current.confirm(receipt[1])
 await form.flush()
 assert.equal(form.state.entity.reviewId,value)
 const label=selectedNameField==='countryLabel'?'reviewCountryLabel':'reviewName'
 assert.equal(form.state.entity[label],'当前候选')

 form.state.customerButtonHandler63()
 form.dialogs.at(-1).success({confirm:true})
 await form.flush()
 const command=form.requests.find(entry=>entry.options.url==='/admin/sys/config_button/execute')
 const values=plain(command.options.data.entity)
 assert.equal(values.reviewId,value)
 assert.equal(values[label],'当前候选')
 assert.equal(values.reviewCountryId,parentB)
 assert.equal(form.writes().length,0)
} finally {
 target?.app.unmount()
 form.app.unmount()
}
