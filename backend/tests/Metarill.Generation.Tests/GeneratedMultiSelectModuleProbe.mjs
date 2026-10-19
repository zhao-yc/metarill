import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { SourceTextModule, createContext } from 'node:vm'
const root = resolve(process.argv[2], 'frontend')
const context = createContext({ console, uni: {}, getApp: () => ({ globalData: {} }) })
const modules = new Map()
/** 链接实际生成脚本的全部Vue导入；外部运行时只替代API，不替代页面模块或组件注册。 */
async function moduleAt(path) {
 if (modules.has(path)) return modules.get(path)
 let text
 if (path.endsWith('.vue')) text = (await readFile(path, 'utf8')).match(/<script>([\s\S]*?)<\/script>/)[1]
 else text = 'export default {}; export const createSelectionPageMixin = () => ({}); export const createMultiSelectFieldMixin = () => ({}); export const createRelationOptionsMixin = () => ({});'
 const module = new SourceTextModule(text, { context, identifier: path }); modules.set(path, module); return module
}
const pages = (await readdir(join(root, 'pages/yzp'))).filter(path => path.endsWith('.vue'))
for (const path of pages) {
 const entry = await moduleAt(join(root, 'pages/yzp', path))
 if (entry.status === 'unlinked') await entry.link((specifier, reference) => moduleAt(specifier.startsWith('@/') ? resolve(root, specifier.slice(2)) : resolve(reference.identifier, '..', specifier)))
 if (entry.status !== 'evaluated') await entry.evaluate()
 assert.equal(typeof entry.namespace.default, 'object')
}
assert.ok([...modules.keys()].filter(path => path.endsWith('.vue')).length >= 6)
console.log('通过：自选/互选实际生成Vue模块图全部链接及求值，选择列表不依赖不可达CRUD详情，普通列表保留详情组件')
