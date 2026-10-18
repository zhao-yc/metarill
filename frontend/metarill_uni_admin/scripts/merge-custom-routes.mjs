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
export async function syncCustomRoutes() {
  const target=new URL('../pages.json',import.meta.url)
  const previous=await readFile(target,'utf8'), routes=JSON.parse(await readFile(new URL('./custom-routes.json',import.meta.url),'utf8'))
  const output=JSON.stringify(mergeCustomRoutes(JSON.parse(previous),routes),null,2)+'\n'
  if(output!==previous) await writeFile(target,output)
  return {changed:output!==previous,routes:routes.length}
}
if(process.argv[1] && import.meta.url===pathToFileURL(resolve(process.argv[1])).href) console.log(JSON.stringify(await syncCustomRoutes()))
