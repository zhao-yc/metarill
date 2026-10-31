import { readFile, writeFile } from 'node:fs/promises'
import { pathToFileURL } from 'node:url'
import { resolve } from 'node:path'

/** 在生成路由基础上确定性合并专用手写页面；重复执行不会重复添加或改写其他路由。 */
export function mergeCustomRoutes(document, routes) {
  if (!document || !Array.isArray(document.pages) || !Array.isArray(routes)) throw new Error('路由清单无效')
  const result=structuredClone(document), names=new Set()
  for (const route of routes) {
    if (!route || typeof route.path!=='string' || !/^pages\/custom\/[a-z_]+$/.test(route.path) || names.has(route.path)) throw new Error('专用路由无效或重复')
    names.add(route.path)
    const existing=result.pages.findIndex(item=>item.path===route.path)
    if(existing>=0) result.pages[existing]=structuredClone(route)
    else result.pages.push(structuredClone(route))
  }
  return result
}
/** 从原壳模板只恢复固定三页签的图标，页面集合、布局、主题及其它配置逐字值保留。 */
export function mergePlatformShellNavigation(document, shell) {
  const paths=['pages/main/index','pages/main/menu','pages/main/my']
  if (!document || !Array.isArray(document.pages) || !Array.isArray(document.tabBar?.list) || !Array.isArray(shell?.tabBar?.list)) throw new Error('平台导航壳配置无效')
  const source=shell.tabBar.list
  if(source.length!==paths.length || source.some(tab=>!paths.includes(tab?.pagePath)) || new Set(source.map(tab=>tab.pagePath)).size!==paths.length) throw new Error('原壳页签声明无效或重复')
  const result=structuredClone(document)
  for(const path of paths) {
    const original=source.find(tab=>tab.pagePath===path), targets=result.tabBar.list.filter(tab=>tab?.pagePath===path)
    if(targets.length!==1 || !result.pages.some(page=>page?.path===path)) throw new Error('平台页签缺少唯一已注册页面：'+path)
    for(const key of ['iconPath','selectedIconPath']) {
      const icon=original[key]
      if(typeof icon!=='string' || !/^static\/images\/[a-z-]+\.png$/.test(icon)) throw new Error('原壳页签图标路径无效：'+path)
      targets[0][key]=icon
    }
  }
  return result
}
/** 平台生成同步入口复用原壳模板，只落盘专用路由和固定图标的确定性合并结果。 */
export async function syncCustomRoutes() {
  const target=new URL('../pages.json',import.meta.url)
  const previous=await readFile(target,'utf8'), routes=JSON.parse(await readFile(new URL('./custom-routes.json',import.meta.url),'utf8'))
  const shell=JSON.parse(await readFile(new URL('../../../backend/templates/standalone-frontend/frontend-pages.json.tpl',import.meta.url),'utf8'))
  const output=JSON.stringify(mergePlatformShellNavigation(mergeCustomRoutes(JSON.parse(previous),routes),shell),null,2)+'\n'
  if(output!==previous) await writeFile(target,output)
  return {changed:output!==previous,routes:routes.length}
}
if(process.argv[1] && import.meta.url===pathToFileURL(resolve(process.argv[1])).href) console.log(JSON.stringify(await syncCustomRoutes()))
