import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'
import { definition, blankFunction } from './original-detail-requests.mjs'
import { preprocess } from './original-frontend-routes.mjs'

const platforms={H5:['H5','WEB','VUE3'],APP:['APP','APP-PLUS','APP-VUE','VUE3'],WEIXIN:['MP','MP-WEIXIN','VUE3']}
const parentId='9007199254740995',oldId='9007199254740993',newId='9007199254740997'
const clone=value=>JSON.parse(JSON.stringify(value))
const key=node=>node?.type==='Identifier'?node.name:node?.type==='StringLiteral'?node.value:null

/** 只装载原模块函数声明，不执行import、顶层表达式或另一份近似状态机。 */
export function runtimeFactory(source,parser,context,name='createListPageMixin') {
  assert.ok(['createListPageMixin','mergeListRowAfterSave'].includes(name),'未知列表函数出口')
  const ast=parser.parse(source,{sourceType:'module'}),declarations=[]
  for(const item of ast.program.body){const node=item.type==='ExportNamedDeclaration'?item.declaration:item;assert.equal(node?.type,'FunctionDeclaration','列表模块出现未审查的顶层初始化');declarations.push(source.slice(node.start,node.end))}
  assert.equal(ast.program.body.filter(item=>item.type==='ExportNamedDeclaration'&&item.declaration?.id?.name===name).length,1)
  return vm.runInNewContext('(()=>{'+declarations.join('\n')+';return '+name+'})()',context,{timeout:1000})
}

/** 仅接受原页明确的列表工厂；配置表达式、展开或另一工厂不能在拒绝前执行。 */
export function mixinOptions(script,parser) {
  const ast=parser.parse(script,{sourceType:'module'}),exports=ast.program.body.filter(item=>item.type==='ExportDefaultDeclaration');assert.equal(exports.length,1)
  const object=exports[0].declaration;assert.equal(object.type,'ObjectExpression')
  const fields=object.properties.filter(item=>key(item.key)==='mixins');if(!fields.length)return null
  const imports=ast.program.body.filter(item=>item.type==='ImportDeclaration').flatMap(item=>item.specifiers.map(specifier=>({item,specifier}))).filter(value=>value.specifier.local.name==='createListPageMixin')
  assert.equal(imports.length,1,'列表工厂必须来自原精确运行时模块');assert.equal(imports[0].item.source.value,'@/common/yzp_runtime/listPageRuntime');assert.equal(imports[0].specifier.type,'ImportSpecifier');assert.equal(imports[0].specifier.imported.name,'createListPageMixin')
  assert.equal(fields.length,1);const field=fields[0];assert.equal(field.value?.type,'ArrayExpression');assert.equal(field.value.elements.length,1)
  const call=field.value.elements[0];assert.equal(call?.type,'CallExpression');assert.equal(call.callee.type,'Identifier');assert.equal(call.callee.name,'createListPageMixin');assert.equal(call.arguments.length,1)
  const options=call.arguments[0];assert.equal(options.type,'ObjectExpression')
  const names=new Set()
  for(const member of options.properties){assert.equal(member.computed,false);const name=key(member.key);assert.ok(['url','pageSize','request','onLoaded','onParentChanged'].includes(name)&&!names.has(name),'未知或重复列表配置');names.add(name);if(['url','pageSize'].includes(name))assert.equal(member.value?.type,name==='url'?'StringLiteral':'NumericLiteral');else assert.equal(member.type,'ObjectMethod')}
  assert.ok(names.has('url')&&names.has('request'))
  return {text:script.slice(options.start,options.end),line:field.loc.start.line}
}

/** 来源文件名约束真实URL和页ID，请求体只容许原分页槽，不冒充字段权限或SQL验收。 */
export function verifyListRequest(request,file,parent,pageNo) {
  const origin=/^pages\/yzp\/([a-z0-9_]+)-list-([0-9]+)\.vue$/.exec(file);assert.ok(origin)
  assert.equal(request.url,'/admin/auto/'+origin[1]+'/'+origin[2]+'/page');assert.equal(request.method,'POST');assert.equal(request.sendSessionId,true)
  const body=clone(request.data);assert.deepEqual(Object.keys(body).sort(),['filters','orderBy','pageNo','pageSize',...(parent===undefined?[]:['parentId'])].sort())
  assert.equal(body.pageNo,pageNo);assert.ok(Number.isInteger(body.pageSize)&&body.pageSize>0);assert.deepEqual(body.filters,{});assert.deepEqual(body.orderBy,{})
  assert.equal(body.parentId,parent);if(parent===parentId)assert.equal(typeof body.parentId,'string')
  return body
}

/** 执行真实原方法和回调；只捕获网络，不启动生命周期、浏览器或数据库。 */
function execute(script,parser,utils,runtime,file,{pc,parent,action,child=false,error=false}) {
  const requests=[],dialogs=[],popups=[],pending=[],context={utils,console:{log(){}},uni:{getStorageSync:key=>key==='userInfo'?{id:oldId}:key==='loginStateId'?'offline-list-token':{},showModal:options=>dialogs.push(options.content),stopPullDownRefresh(){},$emit(){}}}
  context.yzp={request:options=>{const snapshot={url:options.url,method:options.method,sendSessionId:options.sendSessionId,data:clone(options.data)};requests.push(snapshot);pending.push(options);return {abort(){}}},showDetailPageSuccessToast(){}}
  const component=definition(script,parser,context,file),options=mixinOptions(script,parser)
  const mixin=options?runtimeFactory(runtime,parser,context)(vm.runInNewContext('('+options.text+')',context,{timeout:1000})):null
  const page={...(mixin?.data()||{}),...component.data(),...(mixin?.methods||{}),...component.methods,parent_id:parent,is_son_page:child,$deviceInfo:{is_pc:pc},$refs:{filter_popup:{close:()=>popups.push('close')}},list:[{id:oldId,name:'已有记录'}]}
  assert.equal(page.page_sn,/list-([0-9]+)\.vue$/.exec(file)[1])
  const method={page:'getList',pagination:'paginationChange',refresh:'refreshData',filter:'filterList',append:'loadNextPage',reset:'resetFilter',update:'handleDetailChange'}[action];assert.equal(typeof page[method],'function',file+'原方法缺失：'+method)
  if(action==='reset'){page.filters={original:'清空前'};page.resetFilter();assert.deepEqual(clone(page.filters),{});assert.equal(requests.length,0);return {requests,mixin:!!mixin,line:component.methodLines[method]||options?.line}}
  if(action==='update'){
    // 原页通过 yzp 接入共享函数。载入真实源码，避免用测试替身掩盖字段串入列表。
    context.yzp.mergeListRowAfterSave=runtimeFactory(runtime,parser,context,'mergeListRowAfterSave')
    page.list=[{id:oldId,name:'已有记录',remark:'待清空',listOnly:'保留',isSelect:true}]
    page.handleDetailChange({action:'save',method:'update',entity:{id:oldId,name:'已保存',remark:null,detailOnly:'详情字段',isSelect:false}})
    assert.deepEqual(clone(page.list),[{id:oldId,name:'已保存',remark:null,listOnly:'保留',isSelect:true}])
    assert.equal(requests.length,0)
    return {requests,mixin:!!mixin,line:component.methodLines[method]||options?.line}
  }
  if(action==='append'){
    page.getList();assert.equal(requests.length,1);pending[0].success({code:200,data:{records:[{id:oldId}],pages:3,total:9},map:{statistics:{}}});pending[0].complete();page.loadNextPage()
  } else if(action==='pagination')page.paginationChange({current:2})
  else page[method]()
  if(child&&utils.isBlank(parent)){assert.equal(requests.length,0);return {requests,mixin:!!mixin,line:component.methodLines.getList||options?.line}}
  const count=action==='append'?2:1;assert.equal(requests.length,count,file+'实际请求数异常')
  for(let index=0;index<requests.length;index++)verifyListRequest(requests[index],file,parent,action==='pagination'||index===1?2:1)
  const current=pending.at(-1),before=clone(page.list),rows=[{id:newId,name:'本轮响应'}],statistics={amount:{currentPage:1,total:2}}
  current.success(error?{code:500,msg:'本轮合成错误'}:{code:200,data:{records:rows,pages:3,total:9},map:{statistics}});current.complete()
  if(error){assert.equal(dialogs.at(-1),'本轮合成错误');assert.deepEqual(clone(page.list),before)}
  else{
    const appended=action==='append'&&!pc||!mixin&&!pc&&!['refresh','filter'].includes(action)
    assert.deepEqual(clone(page.list),appended?before.concat(rows):rows);assert.deepEqual(clone(page.statistics),statistics);assert.equal(page.pages_qty,3);assert.equal(page.rows_qty,9);assert.equal(page.load_more_status,'more')
  }
  if(action==='filter')assert.deepEqual(popups,['close'])
  return {requests,mixin:!!mixin,line:component.methodLines.getList||options?.line}
}

/** 所有原列表逐页清点；菜单树包装明确留作未覆盖，不把缺methods静默算通过。 */
export function inventoryLists(frontend) {
  const require=createRequire(path.join(frontend,'package.json')),parser=require('@babel/parser'),sfc=require('@vue/compiler-sfc'),utils={isBlank:blankFunction(frontend,parser)}
  const runtime=fs.readFileSync(path.join(frontend,'common/yzp_runtime/listPageRuntime.js'),'utf8'),directory=path.join(frontend,'pages/yzp'),files=fs.readdirSync(directory,{withFileTypes:true}).filter(item=>/-list-[0-9]+\.vue$/.test(item.name)).sort((a,b)=>a.name.localeCompare(b.name))
  const fixed=[],cases=[],unresolved=[],types=new Map()
  for(const item of files){assert.ok(item.isFile()&&!item.isSymbolicLink());const file='pages/yzp/'+item.name
    for(const [platform,flags] of Object.entries(platforms)){
      const parsed=sfc.parse(preprocess(fs.readFileSync(path.join(directory,item.name),'utf8'),new Set(flags)),{filename:file});assert.equal(parsed.errors.length,0);assert.ok(parsed.descriptor.script&&!parsed.descriptor.scriptSetup)
      const script=parsed.descriptor.script.content
      if(item.name==='config_menu-list-2200000000000099000.vue'){
        const ast=parser.parse(script,{sourceType:'module'});assert.equal(ast.program.body.length,1);assert.equal(ast.program.body[0].declaration?.type,'ObjectExpression');assert.equal(ast.program.body[0].declaration.properties.length,0);assert.equal(parsed.descriptor.template.content.trim(),'<yzp-menu-workbench />')
        unresolved.push({file,platform,line:parsed.descriptor.script.loc.start.line,reason:'明确菜单工作台包装，本标准列表捕获不执行其组件'});continue
      }
      for(const pc of [true,false])for(const parent of [undefined,'',parentId])for(const action of ['page','pagination','refresh','filter','reset','update',...(!pc?['append']:[])]){
        const value=execute(script,parser,utils,runtime,file,{pc,parent,action});types.set(file,value.mixin?'mixin':'legacy')
        for(const request of value.requests)fixed.push({file,platform,line:parsed.descriptor.script.loc.start.line-1+value.line,method:request.method,route:request.url,action:'page',payload:request.data})
        cases.push({file,platform,pc,parent:parent??null,action,requestCount:value.requests.length})
      }
      for(const pc of [true,false]){
        for(const parent of [undefined,null,'']){const value=execute(script,parser,utils,runtime,file,{pc,parent,action:'page',child:true});assert.equal(value.requests.length,0);cases.push({file,platform,pc,action:'child-empty',requestCount:0})}
        const value=execute(script,parser,utils,runtime,file,{pc,parent:parentId,action:'page',error:true});for(const request of value.requests)fixed.push({file,platform,line:parsed.descriptor.script.loc.start.line-1+value.line,method:request.method,route:request.url,action:'page',payload:request.data});cases.push({file,platform,pc,action:'error-response',requestCount:value.requests.length})
      }
    }
  }
  assert.ok(files.length>=20&&types.size>=19,'原列表覆盖范围意外减少')
  return {scannedFiles:files.length,standardFiles:types.size,legacyFiles:[...types.values()].filter(type=>type==='legacy').length,mixinFiles:[...types.values()].filter(type=>type==='mixin').length,fixed,cases,unresolved,errors:[]}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)process.stdout.write(JSON.stringify(inventoryLists(path.resolve(process.argv[2]??''))))
