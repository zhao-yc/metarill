import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { mergeTemplateVersionDefault } from '../scripts/sync-template-version.mjs'

const source=await readFile(new URL('../pages/yzp/yz_project-detail-6186303424888832.vue',import.meta.url),'utf8')
const metadata=JSON.parse(await readFile(new URL('../../../backend/metadata/platform.json',import.meta.url),'utf8'))
const versionField=metadata.configPageColumns.find(field=>field.id==='1669606903854936066')
const current=versionField.defaultValue
const legacyHint='新项目默认 v8；旧项目保留原版本。已有生成目录不能直接切换版本。'

/** 执行真实原页面的entity初始对象，确认长ID不经Number，且模板和样式没有变化。 */
test('原项目页新建默认选择元数据中的C#模板，原DOM样式事件逐字保留',()=>{
  const legacy=source.replace(versionField.hint,legacyHint).replace(/(yzYzpVersionId\s*:\s*)(['"])[0-9]+\2/,(_,prefix,quote)=>prefix+quote+'2104907095303213057'+quote)
  const result=mergeTemplateVersionDefault(legacy,metadata)
  const entity=new Function('return ('+result.match(/\bentity:\s*(\{[^{}]*\})/)[1]+')')()
  assert.equal(entity.yzYzpVersionId,current)
  assert.equal(typeof entity.yzYzpVersionId,'string')
  assert.equal(result.replace(current,'2104907095303213057').replace(versionField.hint,legacyHint),legacy)
  assert.equal(result.split('<script>')[0].replace(versionField.hint,legacyHint),legacy.split('<script>')[0])
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


/** 元数据中的Java/未知默认值及重复或冲突目录不能让同步显示成功。 */
test('默认值和页覆盖必须匹配受支持的固定目录',()=>{
  for(const value of ['999','2104907095303213057',2300000000000000001,null]) {
    const invalid=structuredClone(metadata)
    invalid.configPageColumns.find(field=>field.id==='1669606903854936066').defaultValue=value
    assert.throws(()=>mergeTemplateVersionDefault(source,invalid),/受支持/)
  }
  for(const change of [
    value=>{value.configColumns.find(column=>column.id==='1669606903854936065').controlDetail='{"catalog":"unknown","catalog":"metarill-csharp-templates/v1","nameField":"name"}'},
    value=>{value.configColumns.find(column=>column.id==='1669606903854936065').isJoinColumn=1},
    value=>{value.configPageColumns.find(field=>field.id==='1669606903854936066').controlKind='SelectPage'},
    value=>{value.configPageColumns.find(field=>field.id==='1669606903854936066').controlDetail='{"catalog":"unknown","nameField":"name"}'},
    value=>{value.configPageColumns.find(field=>field.id==='1669606903854936066').controlDetail='{"catalog":'}
  ]) {
    const invalid=structuredClone(metadata);change(invalid)
    assert.throws(()=>mergeTemplateVersionDefault(source,invalid),/已确认/)
  }
})


/** 已批准的提示只作为文本下发；元数据不能借hint插入HTML标签。 */
test('原版本提示随元数据同步并转义HTML，拒绝覆盖未知人工提示',()=>{
  const input=source.replace(versionField.hint,legacyHint), invalid=structuredClone(metadata)
  invalid.configPageColumns.find(field=>field.id==='1669606903854936066').hint='<img src=x>&提示'
  const result=mergeTemplateVersionDefault(input,invalid)
  assert.ok(result.includes('&lt;img src=x&gt;&amp;提示'))
  assert.ok(!result.includes('<img src=x>'))
  assert.throws(()=>mergeTemplateVersionDefault(input.replace(legacyHint,'其它来源的提示'),metadata),/提示不唯一/)
})
