import { readFile, readdir, lstat, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { parse, babelParse } from '@vue/compiler-sfc'

const modulePath='@/common/yzp_runtime/originalRemoteOptionsMixin'
const factory='createOriginalRemoteOptionsMixin'
/** 两个原页面遗留104号字段路由；当前元数据/生成Controller已明确使用同名字段105，不能泛化猜ID。 */
const historicalEndpoints=new Map([
 ['/admin/auto/config_page/1868973685063311361/findNameIdYzAdminType2200000000000000104','/admin/auto/config_page/1868973685063311361/findNameIdYzAdminType2200000000000000105'],
 ['/admin/auto/config_page/6191175925956608/findNameIdYzAdminType2200000000000000104','/admin/auto/config_page/6191175925956608/findNameIdYzAdminType2200000000000000105']
])
const currentEndpoint=value=>historicalEndpoints.get(value)??value
const remoteNames=new Set(['key','remote-selected','remote-loading','remote-has-more'])
const remoteEvents=new Set(['open','inputChange','loadMore'])
const named=node=>node?.name??node?.value
const property=(object,key)=>object?.properties?.find(item=>named(item.key)===key)

/** 只遍历实际语法节点，不执行原页面或生成脚本。 */
function walk(node, visit) {
 if(!node||typeof node!=='object')return
 if(Array.isArray(node)){for(const child of node)walk(child,visit);return}
 if(typeof node.type==='string')visit(node)
 for(const [key,child] of Object.entries(node))if(!['loc','comments','leadingComments','trailingComments','tokens'].includes(key))walk(child,visit)
}
/** 原SFC与Babel脚本保留精确字节位置，未知宿主结构明确拒绝。 */
function document(source) {
 const {descriptor,errors}=parse(source)
 if(errors.length||!descriptor.template||!descriptor.script||descriptor.scriptSetup)throw Error('原页面不是已支持的普通Vue脚本')
 const script=descriptor.script.content,ast=babelParse(script,{sourceType:'module'})
 const declaration=ast.program.body.find(item=>item.type==='ExportDefaultDeclaration')?.declaration
 if(declaration?.type!=='ObjectExpression')throw Error('原页面默认导出结构未知')
 const tags=[]
 function visit(node){if(node.tag==='zxz-uni-data-select')tags.push(node);for(const child of node.children||[])visit(child)}
 visit(descriptor.template.ast)
 return{source,descriptor,script,ast,declaration,tags}
}
/** 模板解析器的位置须与实际源片段相符，不能用全文替换误碰相似组件。 */
function tagPosition(doc,tag) {
 const offset=tag.loc.start.offset,raw=tag.loc.source
 if(doc.source.slice(offset,offset+raw.length)===raw)return offset
 const shifted=offset+doc.descriptor.template.loc.start.offset
 if(doc.source.slice(shifted,shifted+raw.length)===raw)return shifted
 throw Error('原控件位置与源文件不一致')
}
const directive=(tag,name,arg)=>tag.props.find(prop=>prop.type===7&&prop.name===name&&(arg===undefined?prop.arg===undefined:prop.arg?.content===arg))
const remoteProps=tag=>tag.props.filter(prop=>prop.type===6&&prop.name==='remote'||prop.type===7&&(prop.name==='bind'&&remoteNames.has(prop.arg?.content)||prop.name==='on'&&remoteEvents.has(prop.arg?.content)))
/** 描述只从当前C#生成页读取JSON字面量，禁止执行或推测SQL/父映射。 */
function definitions(doc) {
 const calls=[]
 walk(doc.ast,node=>{if(node.type==='CallExpression'&&node.callee.type==='Identifier'&&node.callee.name===factory)calls.push(node)})
 if(calls.length===0)return null
 if(calls.length!==1)throw Error('生成页远程描述不唯一')
 const call=calls[0],owner=call.arguments[1]?.value??'entity'
 if(!['entity','filters'].includes(owner)||call.arguments[0]?.type!=='ArrayExpression')throw Error('生成页描述结构未知')
 const items=JSON.parse(doc.script.slice(call.arguments[0].start,call.arguments[0].end))
 if(!Array.isArray(items)||new Set(items.map(item=>item.id)).size!==items.length)throw Error('生成字段重复')
 for(const item of items)if(typeof item.id!=='string'||!/^[A-Za-z0-9_]+$/.test(item.id)||typeof item.field!=='string'||!/^[A-Za-z_][A-Za-z0-9_]*$/.test(item.field)||!/^\/admin\/auto\/[A-Za-z0-9_]+\/[A-Za-z0-9_]+\/findNameId[A-Za-z0-9_]+$/.test(item.endpoint))throw Error('生成字段或端点无效')
 const known=new Set(['id','field','list','endpoint','requireParent','requireTargetParent','sameParent','targetParentField','readonlyField'])
 for(const item of items)if(Object.keys(item).length!==known.size||Object.keys(item).some(key=>!known.has(key))||!['list'].every(key=>typeof item[key]==='string'&&/^[A-Za-z_][A-Za-z0-9_]*$/.test(item[key]))||['targetParentField','readonlyField'].some(key=>item[key]!==null&&(typeof item[key]!=='string'||!/^[A-Za-z_][A-Za-z0-9_]*$/.test(item[key])))||['requireParent','requireTargetParent','sameParent'].some(key=>typeof item[key]!=='boolean'))throw Error('生成页描述属性不完整或未知')
 return{items,owner}
}
/** 请求中若混入手写回调或额外参数则拒绝，不能为接分页而删除业务扩展。 */
function requireOriginalRequest(call,definition) {
 const request=call.arguments[0],allowed=new Set(['url','data','sendSessionId','method','success'])
 if(request?.type!=='ObjectExpression'||request.properties.some(item=>item.computed||!allowed.has(named(item.key))||item.type!=='ObjectProperty'&&!(item.type==='ObjectMethod'&&named(item.key)==='success'&&item.kind==='method')))throw Error('原候选请求存在未知扩展：'+definition.id)
 if(String(property(request,'method')?.value?.value).toUpperCase()!=='POST'||property(request,'sendSessionId')?.value?.value!==true)throw Error('原候选请求协议变化：'+definition.id)
 const data=property(request,'data')?.value
 if(data?.type!=='ObjectExpression'||data.properties.some(item=>item.type!=='ObjectProperty'||!['formParameters','parentId','targetParentId','id'].includes(named(item.key))))throw Error('原候选请求参数未知：'+definition.id)
 const expected=babelParse('const callback=function(res){if(res.code == 200){that.'+definition.list+'=res.data || [];}else{uni.showModal({showCancel:false,content:res.msg})}};',{sourceType:'module'}).program.body[0].declarations[0].init
 const normalize=value=>JSON.stringify(value,(key,item)=>['start','end','loc','extra','comments','leadingComments','trailingComments','innerComments'].includes(key)?undefined:['consequent','alternate'].includes(key)&&item?.type==='BlockStatement'&&item.body.length===1?item.body[0]:item)
 const success=property(request,'success'),callback=success?.type==='ObjectMethod'?{type:'FunctionExpression',id:null,generator:success.generator,async:success.async,params:success.params,body:success.body}:success?.value
 if(normalize(callback)!==normalize(expected))throw Error('原候选成功回调存在未知扩展：'+definition.id)
 if(new Set(request.properties.map(item=>named(item.key))).size!==request.properties.length||new Set(data.properties.map(item=>named(item.key))).size!==data.properties.length)throw Error('原候选请求含重复属性：'+definition.id)
 for(const item of data.properties){
  const key=named(item.key),expression=key==='formParameters'?'uni.getStorageSync(that.formParametersName)':key==='parentId'?'that.parent_id':key==='id'?'that.entity.'+definition.field:key==='targetParentId'&&definition.targetParentField?'that.entity.'+definition.targetParentField:key==='targetParentId'&&definition.sameParent?'that.parent_id':null
  if(item.computed||!expression)throw Error('原候选参数绑定未知：'+definition.id)
  const expectedValue=babelParse('const value='+expression+';',{sourceType:'module'}).program.body[0].declarations[0].init
  if(normalize(item.value)!==normalize(expectedValue))throw Error('原候选参数值存在未知扩展：'+definition.id)
 }
}

/** 一次形成全部补丁，按倒序位置应用；不重排或格式化原DOM、脚本和样式。 */
function apply(source,edits) {
 const ordered=edits.sort((left,right)=>right.start-left.start||right.end-left.end)
 let boundary=source.length
 for(const edit of ordered){if(edit.end>boundary||edit.end<edit.start)throw Error('原页同步补丁重叠');source=source.slice(0,edit.start)+edit.text+source.slice(edit.end);boundary=edit.start}
 return source
}
/** 保留原页面，仅同步实际存在的标准List；专用编辑器没有对应标签时不会被改造。 */
export function planOriginalPlatformLists(original,generated) {
 const target=document(original),origin=document(generated),compiled=definitions(origin)
 if(!compiled)return{content:original,columns:[],skipped:[]}
 const edits=[],active=[],skipped=[],scriptOffset=target.descriptor.script.loc.start.offset
 const methods=property(target.declaration,'methods')?.value
 const init=property(methods,'initLists'),body=init?.type==='ObjectMethod'?init.body:init?.value?.body
 const statements=body?.body?.filter(item=>item.type==='ExpressionStatement')??[]
 for(const definition of compiled.items){
  const model=compiled.owner+'.'+definition.field
  const tags=target.tags.filter(tag=>directive(tag,'model')?.exp?.content===model)
  if(tags.length===0){skipped.push({id:definition.id,reason:'原页未使用此标准List标签'});continue}
  if(tags.length!==1)throw Error('原页List字段标签不唯一：'+definition.id)
  if(!body)throw Error('原候选初始化入口不存在')
  if(!body.body.some(statement=>statement.type==='VariableDeclaration'&&statement.declarations.some(item=>item.id.name==='that'&&item.init?.type==='ThisExpression')))throw Error('原候选this绑定未知')
  const tag=tags[0],reference=origin.tags.find(node=>directive(node,'model')?.exp?.content===model)
  if(!reference||directive(tag,'bind','localdata')?.exp?.content!==definition.list)throw Error('原控件候选数组与元数据不一致：'+definition.id)
  for(const [key,value]of [['dataKey','name'],['dataValue','id']])if(tag.props.find(prop=>prop.type===6&&prop.name===key)?.value?.content!==value)throw Error('原控件回调数据结构未知：'+definition.id)
  const approved=remoteProps(reference)
  if(approved.length!==8)throw Error('生成页远程属性不完整')
  const current=remoteProps(tag)
  if(current.length&&current.map(item=>item.loc.source).join(' ')!==approved.map(item=>item.loc.source).join(' '))throw Error('原页远程属性存在未知手工变化：'+definition.id)
  if(!current.length){const start=tagPosition(target,tag)+tag.loc.source.length-2;if(target.source.slice(start,start+2)!=='/>')throw Error('原List标签不是自闭合');edits.push({start,end:start,text:' '+approved.map(item=>item.loc.source).join(' ')})}
  const old=statements.filter(statement=>{
   const call=statement.expression
   return call.type==='CallExpression'&&call.callee.type==='MemberExpression'&&named(call.callee.object)==='yzp'&&named(call.callee.property)==='request'&&currentEndpoint(property(call.arguments[0],'url')?.value?.value)===definition.endpoint
  })
  const existing=statements.filter(statement=>{const call=statement.expression;return call.type==='CallExpression'&&call.callee.type==='MemberExpression'&&named(call.callee.object)==='that'&&named(call.callee.property)==='loadOriginalRemoteOptions'&&call.arguments.length===1&&call.arguments[0]?.value===definition.id})
  if(old.length+existing.length!==1)throw Error('原List请求缺失、重复或已被改写：'+definition.id)
  if(old.length)requireOriginalRequest(old[0].expression,definition)
  if(old.length)edits.push({start:scriptOffset+old[0].start,end:scriptOffset+old[0].end,text:'that.loadOriginalRemoteOptions('+JSON.stringify(definition.id)+');'})
  active.push(definition)
 }
 if(!active.length)return{content:original,columns:[],skipped}
 const calls=[]
 walk(target.ast,node=>{if(node.type==='CallExpression'&&node.callee.type==='Identifier'&&node.callee.name===factory)calls.push(node)})
 const expected=factory+'('+JSON.stringify(active)+','+JSON.stringify(compiled.owner)+',{originalHost:true})'
 if(calls.length>1)throw Error('原页远程mixin不唯一')
 if(calls.length){
  const actual=target.script.slice(calls[0].start,calls[0].end)
  if(actual!==expected)throw Error('原页远程描述与当前元数据不同，请审核后重新同步')
 }else{
  const mixins=property(target.declaration,'mixins')
  if(mixins){if(mixins.value?.type!=='ArrayExpression')throw Error('原页mixin结构未知');const position=scriptOffset+mixins.value.end-1;edits.push({start:position,end:position,text:(mixins.value.elements.length?',':'')+expected})}
  else{const position=scriptOffset+target.declaration.start+1;edits.push({start:position,end:position,text:'\n  // 原List分页由当前元数据同步；不覆盖原页面与事件。\n  mixins:['+expected+'],'})}
 }
 const imports=target.ast.program.body.filter(item=>item.type==='ImportDeclaration'&&item.source.value===modulePath)
 if(target.ast.program.body.some(item=>item.type==='ImportDeclaration'&&item.source.value!==modulePath&&item.specifiers.some(binding=>binding.local.name===factory)))throw Error('原页存在同名但不同来源的mixin导入')
 if(imports.length>1||imports.length===1&&!imports[0].specifiers.some(item=>item.type==='ImportSpecifier'&&item.local.name===factory&&item.imported.name===factory))throw Error('原页远程导入存在冲突')
 if(!imports.length){const position=scriptOffset;edits.push({start:position,end:position,text:"\nimport { "+factory+" } from '"+modulePath+"'; // 元数据同步的原List接线\n"})}
 return{content:apply(original,edits),columns:active.map(item=>item.id),skipped}
}

/** 全部页面先校验、再检查并发变化，未知页面结构不能留下半份同步。 */
export async function syncOriginalPlatformLists(sourceRoot,{check=false}={}) {
 const generated=path.join(sourceRoot,'backend/generated/platform/frontend/pages/yzp'),original=path.join(sourceRoot,'frontend/metarill_uni_admin/pages/yzp'),plans=[],skipped=[]
 for(const name of (await readdir(generated)).filter(name=>name.endsWith('.vue')).sort()){
  const target=path.join(original,name);let stat
  try{stat=await lstat(target)}catch(error){if(error.code==='ENOENT')continue;throw error}
  if(!stat.isFile()||stat.isSymbolicLink())throw Error('原页面不是普通受管文件')
  const before=await readFile(target,'utf8'),result=planOriginalPlatformLists(before,await readFile(path.join(generated,name),'utf8'))
  skipped.push(...result.skipped.map(item=>({page:name,...item})))
  if(result.columns.length)plans.push({path:target,page:name,before,...result})
 }
 for(const plan of plans)if(await readFile(plan.path,'utf8')!==plan.before)throw Error('原页在同步期间改变，未写入')
 const changes=plans.filter(plan=>plan.content!==plan.before)
 if(!check)for(const plan of changes)await writeFile(plan.path,plan.content,'utf8')
 return{pages:plans.length,columns:plans.reduce((total,plan)=>total+plan.columns.length,0),changed:changes.length,check,skipped}
}
/** 命令入口只面向当前源码仓库；不从元数据接受任意输出路径。 */
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
 try{
  if(process.argv.slice(2).some(argument=>argument!=='--check'))throw Error('仅支持--check')
  const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..')
  const result=await syncOriginalPlatformLists(root,{check:process.argv.includes('--check')})
  console.log(JSON.stringify(result))
  if(result.check&&result.changed)process.exitCode=1
 }catch(error){console.error(error.message);process.exitCode=1}
}
