import test from 'node:test'
import assert from 'node:assert/strict'
import { generationDownloadRequest,downloadGeneration } from '../common/yzp_runtime/generationDownload.js'
import { mergeCustomRoutes } from '../scripts/merge-custom-routes.mjs'
test('生成固定POST当前API并携带登录/项目头，响应URL不能控制下载目标',()=>{
 const request=generationDownloadRequest({type:'metarillGenerate',projectId:'777',url:'https://evil.test'},'https://example.test/api/','session',{yzProjectId:'777'})
 assert.equal(request.url,'https://example.test/api/admin/sys/generation/generate');assert.equal(request.method,'POST');assert.deepEqual(JSON.parse(request.body),{projectId:'777'});assert.equal(request.header.loginStateId,'session')
 assert.throws(()=>generationDownloadRequest({type:'metarillGenerate',projectId:'777'},'/api','session',{yzProjectId:'778'}),/已变化/)
 assert.throws(()=>generationDownloadRequest({type:'metarillGenerate',projectId:'777'},'/api','',{yzProjectId:'777'}),/登录超时/)
})
test('非Web平台不虚报ZIP已保存',async()=>{await assert.rejects(downloadGeneration({}),/Web/)} )
test('生成业务错误不会触发保存，下载后上下文变化也不保存',async()=>{
 const previousWindow=globalThis.window,previousFetch=globalThis.fetch;globalThis.window={}
 try {
  globalThis.fetch=async()=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>({code:500,msg:'配置不完整'})})
  await assert.rejects(downloadGeneration({url:'/api',method:'POST',header:{},body:'{}'}),/配置不完整/)
  globalThis.fetch=async()=>({ok:true,headers:{get:()=> 'application/zip'},blob:async()=>new Blob(['zip'])})
  await assert.rejects(downloadGeneration({url:'/api'},()=>false),/已变化/)
 } finally {if(previousWindow===undefined)delete globalThis.window;else globalThis.window=previousWindow;globalThis.fetch=previousFetch}
})
test('专用路由合并幂等，保留生成路由且拒绝重复声明',()=>{
 const base={pages:[{path:'pages/yzp/existing',style:{x:1}}],globalStyle:{keep:true}},routes=[{path:'pages/custom/schema_sync',style:{navigationBarTitleText:'预览'}}]
 const merged=mergeCustomRoutes(base,routes);assert.deepEqual(mergeCustomRoutes(merged,routes),merged);assert.deepEqual(merged.pages[0],base.pages[0]);assert.equal(base.pages.length,1);assert.throws(()=>mergeCustomRoutes(base,[...routes,...routes]),/重复/)
})
