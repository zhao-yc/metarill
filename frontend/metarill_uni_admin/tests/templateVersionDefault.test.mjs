import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { mergeTemplateVersionDefault } from '../scripts/sync-template-version.mjs'

const source=await readFile(new URL('../pages/yzp/yz_project-detail-6186303424888832.vue',import.meta.url),'utf8')
const metadata=JSON.parse(await readFile(new URL('../../../backend/metadata/platform.json',import.meta.url),'utf8'))
const current=metadata.configPageColumns.find(field=>field.id==='1669606903854936066').defaultValue

/** 执行真实原页面的entity初始对象，确认长ID不经Number，且模板和样式没有变化。 */
test('原项目页新建默认选择元数据中的C#模板，原DOM样式事件逐字保留',()=>{
  const legacy=source.replace(/(yzYzpVersionId\s*:\s*)(['"])[0-9]+\2/,(_,prefix,quote)=>prefix+quote+'2104907095303213057'+quote)
  const result=mergeTemplateVersionDefault(legacy,metadata)
  const entity=new Function('return ('+result.match(/\bentity:\s*(\{[^{}]*\})/)[1]+')')()
  assert.equal(entity.yzYzpVersionId,current)
  assert.equal(typeof entity.yzYzpVersionId,'string')
  assert.equal(result.replace(current,'2104907095303213057'),legacy)
  assert.equal(result.split('<script>')[0],legacy.split('<script>')[0])
  assert.equal(result.split('<style')[1],legacy.split('<style')[1])
  assert.equal(mergeTemplateVersionDefault(result,metadata),result)
})

/** 别的来源已改默认值、重复初始化或未知目录不能被同步脚本静默覆盖。 */
test('未知目录或原页归属变化立即拒绝同步',()=>{
  const replace=value=>source.replace(/(yzYzpVersionId\s*:\s*)(['"])[0-9]+\2/,(_,prefix,quote)=>prefix+quote+value+quote)
  assert.throws(()=>mergeTemplateVersionDefault(replace('999'),metadata),/其它来源/)
  assert.throws(()=>mergeTemplateVersionDefault(source+"\nconst other={yzYzpVersionId:'999'}",metadata),/不唯一/)
  const invalid=structuredClone(metadata)
  invalid.configColumns.find(column=>column.id==='1669606903854936065').controlDetail='{"catalog":"unknown","nameField":"name"}'
  assert.throws(()=>mergeTemplateVersionDefault(source,invalid),/已确认/)
})
