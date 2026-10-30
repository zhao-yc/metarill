import test from 'node:test'
import assert from 'node:assert/strict'
import {readFile,mkdir,mkdtemp,writeFile,rm} from 'node:fs/promises'
import {createHash} from 'node:crypto'
import path from 'node:path'
import os from 'node:os'
import {fileURLToPath} from 'node:url'
import {parse} from '@vue/compiler-sfc'
import {planOriginalPlatformLists,syncOriginalPlatformLists} from '../scripts/sync-original-list-options.mjs'

/** 小样本覆盖同步保护；真实十二页另外由冻结旧摘要核对，不更新黄金值迎合新输出。 */
function fixture({endpoint='/admin/auto/source/201/findNameIdTarget25',oldEndpoint=endpoint,id='25',extra='',callbackExtra='',short=false,tag='zxz-uni-data-select',duplicate=false}={}){
 const definition={id,field:'targetId',list:'target_list',endpoint,requireParent:false,requireTargetParent:false,sameParent:false,targetParentField:null,readonlyField:'targetIdReadOnly'}
 const state='originalRemoteOptions['+JSON.stringify(id)+']'
 const bind=(name,value)=>':'+name+"='"+value+"'"
 const event=(name,value)=>'@'+name+"='"+value+"'"
 const attrs=['remote',bind('key',JSON.stringify('original-options-'+id+'-')+'+'+state+'.revision'),bind('remote-selected',state+'.selected'),bind('remote-loading',state+'.loading'),bind('remote-has-more',state+'.hasMore'),event('open','openOriginalRemoteOptions('+JSON.stringify(id)+')'),event('inputChange','searchOriginalRemoteOptions('+JSON.stringify(id)+',$event)'),event('loadMore','moreOriginalRemoteOptions('+JSON.stringify(id)+')')].join(' ')
 const markup=remote=>'<'+tag+' v-model="entity.targetId" :localdata="target_list" filterable :disabled="targetIdReadOnly" dataKey="name" dataValue="id" @change="originalSelectChange" '+(remote?attrs:'')+' />'
 const callback=(short?'success(res)':'success:function(res)')+'{if(res.code == 200){that.target_list=res.data || [];'+callbackExtra+'}else{uni.showModal({showCancel:false,content:res.msg})}}'
 const request='yzp.request({url:'+JSON.stringify(oldEndpoint)+',data:{formParameters:uni.getStorageSync(that.formParametersName)},sendSessionId:true,method:"POST",'+callback+(extra?','+extra:'')+'});'
 const original='<template><view class="原页面">'+markup(false)+'</view></template>\n<script>import yzp from "@/common/yzp_runtime/yzp-runtime";export default{data(){return{entity:{targetId:""},target_list:[]}},methods:{initLists:function(){var that=this;'+request+(duplicate?request:'')+'},originalSelectChange(item){this.entity.targetId=item?item.id:""},untouched(){return"保留手写"}}};</script>\n<style scoped>.原页面{color:red}</style>'
 const generated='<template><view>'+markup(true)+'</view></template>\n<script>export default{mixins:[createOriginalRemoteOptionsMixin('+JSON.stringify([definition])+')]};</script><style></style>'
 return{original,generated,definition}
}

test('按固定生成描述只改原下拉与请求，脚本/事件/样式保持且重复同步无变化',()=>{
 const f=fixture(),plan=planOriginalPlatformLists(f.original,f.generated)
 assert.deepEqual(plan.columns,['25']);assert.match(plan.content,/originalHost:true/);assert.match(plan.content,/that.loadOriginalRemoteOptions\("25"\)/)
 assert(plan.content.includes('originalSelectChange(item){this.entity.targetId=item?item.id:""}'));assert(plan.content.includes('untouched(){return"保留手写"}'))
 assert.equal(parse(plan.content).descriptor.styles[0].content,parse(f.original).descriptor.styles[0].content)
 assert.equal(planOriginalPlatformLists(plan.content,f.generated).content,plan.content)
})
test('原标准success简写和无大括号单语句按相同语义处理',()=>{
 const f=fixture({short:true}),plan=planOriginalPlatformLists(f.original,f.generated)
 assert.match(plan.content,/loadOriginalRemoteOptions/)
 const unbraced=f.original.replace('{that.target_list=res.data || [];}','that.target_list=res.data || [];').replace('else{uni.showModal({showCancel:false,content:res.msg})}','else uni.showModal({showCancel:false,content:res.msg});')
 assert.match(planOriginalPlatformLists(unbraced,f.generated).content,/loadOriginalRemoteOptions/)
})
test('陈旧104路由只允许已证明的两页同字段映射，其它旧编号拒绝',()=>{
 for(const page of ['1868973685063311361','6191175925956608']){
  const prefix='/admin/auto/config_page/'+page+'/findNameIdYzAdminType',f=fixture({endpoint:prefix+'2200000000000000105',oldEndpoint:prefix+'2200000000000000104',id:'2200000000000000105'})
  const plan=planOriginalPlatformLists(f.original,f.generated);assert(!plan.content.includes('findNameIdYzAdminType2200000000000000104'));assert(plan.content.includes('findNameIdYzAdminType2200000000000000105'))
 }
 const f=fixture({oldEndpoint:'/admin/auto/source/201/findNameIdTarget24'});assert.throws(()=>planOriginalPlatformLists(f.original,f.generated),/请求缺失/)
})
test('额外业务回调、未知参数、重复请求和未知this绑定全部拒绝',()=>{
 for(const changes of [{callbackExtra:'that.businessSave(res);'},{extra:'complete(){this.businessSave()}'},{duplicate:true}]){
  const f=fixture(changes);assert.throws(()=>planOriginalPlatformLists(f.original,f.generated),/未知扩展|重复/)
 }
 const f=fixture();assert.throws(()=>planOriginalPlatformLists(f.original.replace('var that=this','var that=other'),f.generated),/this绑定未知/)
 assert.throws(()=>planOriginalPlatformLists(f.original.replace('formParameters:uni.getStorageSync(that.formParametersName)','filters:{admin:"other"}'),f.generated),/请求参数未知/)
})
test('专用组件不改成List；导入冲突、手工改远程属性和伪造mixin描述拒绝',()=>{
 const custom=fixture({tag:'yzp-platform-editor'});assert.equal(planOriginalPlatformLists(custom.original,custom.generated).content,custom.original)
 const f=fixture(),planned=planOriginalPlatformLists(f.original,f.generated).content
 assert.throws(()=>planOriginalPlatformLists(planned.replace('remote-loading','remote-unknown'),f.generated),/远程属性存在未知/)
 assert.throws(()=>planOriginalPlatformLists(planned.replace('"sameParent":false','"sameParent":true'),f.generated),/描述与当前元数据不同/)
 assert.throws(()=>planOriginalPlatformLists(f.original.replace('import yzp','import {createOriginalRemoteOptionsMixin} from "unknown";import yzp'),f.generated),/不同来源/)
})
test('所有原页先通过保护再写入，一个未知页不会导致其它页面先被覆盖',async()=>{
 const base=path.resolve(os.tmpdir(),'metarill-platform-list-tests');await mkdir(base,{recursive:true})
 const root=await mkdtemp(path.join(base,'case-')),originalDir=path.join(root,'frontend/metarill_uni_admin/pages/yzp'),generatedDir=path.join(root,'backend/generated/platform/frontend/pages/yzp')
 try{
  await mkdir(originalDir,{recursive:true});await mkdir(generatedDir,{recursive:true})
  const first=fixture(),second=fixture({callbackExtra:'that.extraBusiness();'})
  for(const[name,f]of [['a.vue',first],['b.vue',second]]){await writeFile(path.join(originalDir,name),f.original);await writeFile(path.join(generatedDir,name),f.generated)}
  await assert.rejects(syncOriginalPlatformLists(root),/未知扩展/)
  assert.equal(await readFile(path.join(originalDir,'a.vue'),'utf8'),first.original)
  await writeFile(path.join(originalDir,'b.vue'),first.original)
  assert.equal((await syncOriginalPlatformLists(root,{check:true})).changed,2);assert.equal(await readFile(path.join(originalDir,'a.vue'),'utf8'),first.original)
  assert.equal((await syncOriginalPlatformLists(root)).changed,2);assert.equal((await syncOriginalPlatformLists(root,{check:true})).changed,0)
 }finally{if(path.dirname(path.resolve(root))!==base)throw Error('临时目录越界，拒绝清理');await rm(root,{recursive:true,force:true})}
})
test('真实十二原配置页去掉精确新增属性后符合28a57a2冻结DOM和样式',async()=>{
 const frontend=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),root=path.resolve(frontend,'../..')
 const baseline=JSON.parse(await readFile(new URL('./fixtures/original-platform-list-baseline.json',import.meta.url),'utf8'))
 assert.equal(baseline.baseline,'28a57a2f771e0a1190271f938a6ad4ae5eff27e0');assert.equal(baseline.pages.length,12)
 const hash=value=>createHash('sha256').update(value.replaceAll('\r\n','\n')).digest('hex')
 let columns=0
 for(const item of baseline.pages){
  const original=await readFile(path.join(frontend,'pages/yzp',item.page),'utf8'),generated=await readFile(path.join(root,'backend/generated/platform/frontend/pages/yzp',item.page),'utf8')
  const plan=planOriginalPlatformLists(original,generated);assert.deepEqual(plan.columns,item.columns);columns+=plan.columns.length
  const {descriptor}=parse(plan.content);let template=descriptor.template.content
  function visit(node){
   if(node.tag==='zxz-uni-data-select'&&node.props.some(prop=>prop.type===6&&prop.name==='remote')){
    const approved=node.props.filter(prop=>prop.type===6&&prop.name==='remote'||prop.type===7&&(prop.name==='bind'&&['key','remote-selected','remote-loading','remote-has-more'].includes(prop.arg?.content)||prop.name==='on'&&['open','inputChange','loadMore'].includes(prop.arg?.content)))
    assert.equal(approved.length,8);const text=' '+approved.map(prop=>prop.loc.source).join(' ');assert(template.includes(text));template=template.replace(text,'')
   }
   for(const child of node.children||[])visit(child)
  }
  visit(descriptor.template.ast);assert.equal(hash(template),item.templateSha256,item.page+'原DOM/事件变化')
  assert.equal(hash(JSON.stringify(descriptor.styles.map(style=>({attrs:style.attrs,content:style.content.replaceAll('\r\n','\n')})))),item.styleSha256,item.page+'原样式变化')
 }
 assert.equal(columns,16)
})

test('请求或数据的重复键、自定义参数求值均拒绝，不删除潜在手写副作用',()=>{
 const f=fixture({extra:'method:"POST"'});assert.throws(()=>planOriginalPlatformLists(f.original,f.generated),/重复属性/)
 const clean=fixture()
 assert.throws(()=>planOriginalPlatformLists(clean.original.replace('uni.getStorageSync(that.formParametersName)','loadBusinessOptions()'),clean.generated),/参数值存在未知扩展/)
 assert.throws(()=>planOriginalPlatformLists(clean.original.replace('formParameters:uni.getStorageSync(that.formParametersName)','formParameters:uni.getStorageSync(that.formParametersName),formParameters:uni.getStorageSync(that.formParametersName)'),clean.generated),/重复属性/)
})

test('生成表单依赖只接明确字段和原表槽，空/重复/额外路径描述拒绝',()=>{
 const f=fixture(),fields=[{field:'allowed',table:'source'}]
 const withFields=value=>f.generated.replace('"readonlyField":"targetIdReadOnly"','"readonlyField":"targetIdReadOnly","formFields":'+JSON.stringify(value))
 const generated=withFields(fields),plan=planOriginalPlatformLists(f.original,generated)
 assert(plan.content.includes('"formFields":[{"field":"allowed","table":"source"}]'))
 assert.equal(planOriginalPlatformLists(plan.content,generated).content,plan.content)
 for(const value of [null,[],[fields[0],fields[0]],[{field:'x.y',table:'source'}],[{field:'allowed',table:'source',sql:'不接收'}],[{field:1,table:'source'}]])
  assert.throws(()=>planOriginalPlatformLists(f.original,withFields(value)),/生成表单依赖槽无效/)
})
