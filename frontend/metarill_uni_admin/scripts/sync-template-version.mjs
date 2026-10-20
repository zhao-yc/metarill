import { readFile, writeFile } from 'node:fs/promises'
import { pathToFileURL } from 'node:url'
import { resolve } from 'node:path'

/** 仅同步用户确认的原版本下拉默认值和提示，页面结构、事件、样式及其它字段逐字保留。 */
export function mergeTemplateVersionDefault(source, metadata) {
  const columns=metadata.configColumns?.filter(column=>column.id==='1669606903854936065')??[]
  const fields=metadata.configPageColumns?.filter(field=>field.configColumnId==='1669606903854936065'&&field.configPageId==='6186303424888832')??[]
  if(columns.length!==1||fields.length!==1) throw new Error('原项目页缺少唯一模板版本声明')
  const column=columns[0], value=fields[0].defaultValue
  // 当前固定目录与后端TemplateVersionCatalog采用同一稳定v1 ID；新增版本必须成套更新该边界。
  function validDeclaration(text) {
    try {
      const detail=JSON.parse(text)
      return detail&&typeof detail==='object'&&!Array.isArray(detail)&&Object.keys(detail).length===2
        &&detail.catalog==='metarill-csharp-templates/v1'&&detail.nameField==='name'
        &&(text.match(/"(?:\\.|[^"\\])*"\s*:/g)??[]).length===2
    } catch { return false }
  }
  const field=fields[0], overlay=field.controlDetail??''
  if(column.configTableId!=='1669251289347932162'||column.columnName!=='yz_yzp_version_id'||column.controlKind!=='List'||column.logicalType!=='int64'||column.isVirtualColumn!==0||column.linkConfigTableId||column.linkConfigPageId||[1,'1',true,'true'].includes(column.isJoinColumn)||!validDeclaration(column.controlDetail)||!['',undefined,'List'].includes(field.controlKind)||(overlay!==''&&!validDeclaration(overlay))) throw new Error('仅同步已确认的C#模板版本下拉')
  if(value!=='2300000000000000001') throw new Error('模板默认版本必须属于受支持的C#固定目录')
  const pattern=/(\byzYzpVersionId\s*:\s*)(['"])([0-9]*)\2/g
  const matches=[...source.matchAll(pattern)]
  if(matches.length!==1||!['2104907095303213057',value].includes(matches[0][3])) throw new Error('原下拉默认值不唯一或已被其它来源改动，未覆盖')
  const legacyHint='新项目默认 v8；旧项目保留原版本。已有生成目录不能直接切换版本。'
  const hint=field.hint
  if(typeof hint!=='string'||!hint.length||hint.length>512) throw new Error('模板版本提示必须为有界文本')
  const escapedHint=hint.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'",'&#39;')
  const hintPattern=/(<zxz-uni-data-select(?=[^>]*v-model=['"]entity\.yzYzpVersionId['"])[^>]*\/><\/template><view class='input-tips'>)([^<]*)(<\/view>)/g
  const hints=[...source.matchAll(hintPattern)]
  if(hints.length!==1||![legacyHint,escapedHint].includes(hints[0][2])) throw new Error('原版本提示不唯一或已被其它来源改动，未覆盖')
  return source.replace(pattern,(_,prefix,quote)=>prefix+quote+value+quote).replace(hintPattern,(_,prefix,oldHint,suffix)=>prefix+escapedHint+suffix)
}

/** 读取正式元数据的页级默认值，不复制生成器整页覆盖保留的原平台页面。 */
export async function syncTemplateVersionDefault() {
  const target=new URL('../pages/yzp/yz_project-detail-6186303424888832.vue',import.meta.url)
  const previous=await readFile(target,'utf8')
  const metadata=JSON.parse(await readFile(new URL('../../../backend/metadata/platform.json',import.meta.url),'utf8'))
  const output=mergeTemplateVersionDefault(previous,metadata)
  if(output!==previous) await writeFile(target,output)
  return {changed:output!==previous,pageId:'6186303424888832',columnId:'1669606903854936065'}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href) console.log(JSON.stringify(await syncTemplateVersionDefault()))
