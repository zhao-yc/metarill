import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import { mergePlatformShellNavigation } from '../scripts/merge-custom-routes.mjs'

const app=new URL('../',import.meta.url)
const read=(path)=>readFile(new URL(path,app),'utf8')
const hash=(value)=>createHash('sha256').update(value).digest('hex')
const shell=JSON.parse(await readFile(new URL('../../backend/templates/standalone-frontend/frontend-pages.json.tpl',app),'utf8'))
const document=JSON.parse(await read('pages.json'))

/** 摘要冻结自原YZP公开壳：忽略HTML注释及标签间排版，CSS只统一平台行尾。 */
function dom(source) {
 return source.match(/<template>([\s\S]*?)<\/template>/)[1].replace(/<!--[\s\S]*?-->/g,'').replace(/>\s+</g,'><').trim()
}
function style(source) { return source.replace(/\r\n?/g,'\n').match(/<style[^>]*>[\s\S]*?<\/style>/)[0] }

test('平台顶栏与我的恢复原图像DOM和style，保留Metarill品牌及已授权菜单工作台',async()=>{
 const top=await read('pages/main/top.vue'),my=await read('pages/main/my.vue')
 assert.equal(hash(dom(top).replace('Metarill 管理平台','YZP后台管理系统')),'9a5d8e166920d80cdb16700c03fa7c8a983ce63abe7a7dd3597fd52f53d72366')
 assert.equal(hash(dom(my)),'4ac009fa2dcff5524e3d384dabe2435458740b6772e31db6be2c2e63d4902801')
 assert.equal(hash(style(top)),'50044106e2fc3bc750439d126a0fc1893e315006ae0ebcecd6224970d55669b9')
 assert.equal(hash(style(my)),'dcd8aceff17b6add0e058648051e1d0e85421a4daf641dcb7ef447c5c5f734aa')
 assert.match(top,/Metarill 管理平台/)
 assert.match(top,/url: "\/pages\/custom\/menu_workbench"/)
 assert.doesNotMatch(top,/system_maintenance\?tab=menus/)
 assert.doesNotMatch(top+my,/person-filled/)
})

test('原logo六页签PNG和用户授权新头像保持真实字节，不复制旧水印头像',async()=>{
 const assets={
  'static/logo.png':'0ba2334220a5c2921b2cb4451686ac8d458660d6aed1580f9cd5a69f241afcdf',
  'static/images/home.png':'8d689e3db188ca990e98e1e97a3c8d4695c317de8b2db0e60f79c86504b12078',
  'static/images/home-selected.png':'83c12672442f5bf9e42015889b0bd92eb3efb01697909945136083c89bdd87af',
  'static/images/menu.png':'956582638fd2a54119d5d71999361bb0b1d69289862bb477fc63516b1934eb18',
  'static/images/menu-selected.png':'414e607ffc02299212d530108ee55c739d1fee0f56dea25d5684d82ee0fb74b0',
  'static/images/my.png':'57925af35265074b4a6ad7a7aa67d096b301668dcc156468ef95a4bf6970ab21',
  'static/images/my-selected.png':'abedbf5a314c29b975a4aa68f70dff9d71671946c36334023ff3ac2001a515f2',
  'static/images/avatar.png':'e4829fa7be2628c6a103b246bfa2a4205b6fbdb7479cb890bf55f11f85eef96c'
 }
 for(const [path,expected] of Object.entries(assets))assert.equal(hash(await readFile(new URL(path,app))),expected,path)
 for(const tab of document.tabBar.list)for(const key of ['iconPath','selectedIconPath'])assert.ok(assets[tab[key]],key+':'+tab.pagePath)
})

test('平台图标同步只更改六字段，全部现有及未来自定义/生成路由和主题保留且幂等',()=>{
 const input=structuredClone(document)
 input.pages.push({path:'pages/custom/owned_test',style:{leftWindow:false,keep:'手写'}},{path:'pages/yzp/owned-list-9999999999999999999',style:{navigationStyle:'custom',keep:'生成'}})
 input.tabBar.selectedColor='#137abc';input.topWindow.style.height='61px';input.globalStyle.keep='元数据主题不覆盖'
 for(const tab of input.tabBar.list){delete tab.iconPath;delete tab.selectedIconPath;tab.keep={local:tab.pagePath}}
 input.tabBar.list.reverse()
 const before=structuredClone(input),result=mergePlatformShellNavigation(input,shell)
 assert.deepEqual(input,before)
 assert.deepEqual(result.pages,before.pages)
 assert.deepEqual(result.topWindow,before.topWindow)
 assert.deepEqual(result.leftWindow,before.leftWindow)
 assert.deepEqual(result.globalStyle,before.globalStyle)
 assert.equal(result.tabBar.selectedColor,'#137abc')
 assert.deepEqual(result.tabBar.list.map(tab=>tab.pagePath),before.tabBar.list.map(tab=>tab.pagePath))
 for(const tab of result.tabBar.list){const expected=shell.tabBar.list.find(item=>item.pagePath===tab.pagePath);assert.equal(tab.iconPath,expected.iconPath);assert.equal(tab.selectedIconPath,expected.selectedIconPath);delete tab.iconPath;delete tab.selectedIconPath}
 assert.deepEqual(result,before)
 const first=mergePlatformShellNavigation(input,shell)
 assert.deepEqual(mergePlatformShellNavigation(first,shell),first)
 assert.deepEqual(mergePlatformShellNavigation(document,shell),document)
})

test('缺少或重复固定页签、未注册页面与越界素材声明在写入前拒绝',()=>{
 const cases=[]
 const missing=structuredClone(document);missing.tabBar.list.pop();cases.push([missing,shell])
 const duplicate=structuredClone(document);duplicate.tabBar.list.push(duplicate.tabBar.list[0]);cases.push([duplicate,shell])
 const noRoute=structuredClone(document);noRoute.pages=noRoute.pages.filter(page=>page.path!=='pages/main/menu');cases.push([noRoute,shell])
 const badSource=structuredClone(shell);badSource.tabBar.list[0].iconPath='../private.png';cases.push([document,badSource])
 const badTabs=structuredClone(shell);badTabs.tabBar.list[1].pagePath=badTabs.tabBar.list[0].pagePath;cases.push([document,badTabs])
 for(const [input,source] of cases){const before=structuredClone(input);assert.throws(()=>mergePlatformShellNavigation(input,source),/无效|重复|唯一/);assert.deepEqual(input,before)}
})
