import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const require = createRequire(new URL('../../../frontend/metarill_uni_admin/package.json', import.meta.url))
const Vue = require('vue')
const { parse } = require('@vue/compiler-sfc')
const frontend = join(process.argv[2], 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/summary_form-detail-102.vue'), 'utf8')
const librarySource = await readFile(join(frontend, 'components/yzp-file-library/yzp-file-library.vue'), 'utf8')
const treeSource = await readFile(join(frontend, 'common/yzp_runtime/fileLibraryFolderTree.js'), 'utf8')
const { descriptor, errors } = parse(librarySource)
assert.deepEqual(errors, [])
const tree = new Function(treeSource.replace(/^export function /gm, 'function ').replace(/^export default /m, 'return '))()
const requests = [], uploads = [], controls = []
const uploadedId = '9007199254740993', selectedId = '9007199254740994'
const uploaded = { fileId: uploadedId, originalName: '新附件.txt', mimeType: 'text/plain', sizeBytes: 9, status: 'ready' }
const available = { fileId: selectedId, originalName: '已有附件.txt', mimeType: 'text/plain', sizeBytes: 11, status: 'ready' }
const yzp = { request(options) {
 requests.push(options)
 const data = options.url.endsWith('/upload-plan') ? { fileId: uploadedId, plan: {
  uploadMode: 'SERVER_MULTIPART', uploadUrl: '/admin/sys/file-library/local-upload/' + uploadedId
 }} : options.url.endsWith('/complete') ? uploaded : options.url.endsWith('/list') && !options.url.endsWith('/folders/list') ? [available] : []
 // 刚完成的保护文件尚无真实引用，后台解析保留选择。已有文件继续使用真实引用决定读取权。
 if (options.url.endsWith('/resolve') && options.data.fileIds.includes(uploadedId))
  options.success({ code: 403, msg: '没有当前文件的业务记录读取权限。' })
 else if (options.url.endsWith('/resolve'))
  options.success({ code: 200, data: [{ ...available, mode: 'LOCAL_STREAM', url: '/admin/sys/file-library/protected-content/' + selectedId }] })
 else options.success({ code: 200, data })
} }
const uni = {
 getStorageSync: key => key === 'loginStateId' ? '合成会话' : key === 'sysParams' ? { yzProjectId: '100' } : '',
 setStorageSync() {},
 uploadFile(options) { uploads.push(options); options.success({ data: { code: 200 } }) },
 showToast() {}
}
const getApp = () => ({ globalData: { environment: { base_url: '/api' } } })
const script = descriptor.script.content.replace(/^import\s+[\s\S]+? from '[^']+'\s*$/gm, '').replace('export default', 'return')
const library = new Function('yzp', 'uni', 'getApp', 'cropImageFile', ...Object.keys(tree), script)(yzp, uni, getApp, async input => input, ...Object.values(tree))
library.render = Vue.compile(descriptor.template.content, {
 isCustomElement: tag => ['view', 'text', 'scroll-view', 'uni-icons', 'uni-load-more', 'yzp-auto-pan-image'].includes(tag)
})
library.mounted = function () { controls.push(this) }
const form = mountGenerated(source, {}, {}, {
 runtimeDirectory: join(frontend, 'common/yzp_runtime'),
 components: { 'yzp-file-library': library }
})
let failure
try {
 await form.flush()
 const file = controls.at(-1)
 assert.ok(file)
 assert.equal(file.pageId, '102')
 assert.equal(file.tableId, '2')
 assert.equal(file.configColumnId, '37')
 assert.equal(file.recordId, '')
 assert.equal(file.readonly, false)

 file.openManagerDialog()
 await form.flush()
 assert.equal(requests.find(options => options.url.endsWith('/folders/list')).data.configColumnId, '37')
 assert.equal(requests.find(options => options.url.endsWith('/file-library/list')).data.configColumnId, '37')
 file.toggleDialogFile(available)
 await file.uploadOneFile({ path: '/synthetic/test.txt', name: '新附件.txt', size: 9 })
 await form.flush()
 const plan = requests.find(options => options.url.endsWith('/upload-plan'))
 assert.deepEqual(plain(plan.data), { configColumnId: '37', originalName: '新附件.txt', mimeType: '', sizeBytes: 9, folderId: '' })
 assert.equal(uploads[0].url, '/api/admin/sys/file-library/local-upload/' + uploadedId)
 assert.equal(uploads[0].header.loginStateId, '合成会话')
 assert.deepEqual(plain(requests.find(options => options.url.endsWith('/complete')).data), { fileId: uploadedId })
 assert.deepEqual(plain(form.state.entity.attachments), [])

 file.confirmManagerDialog()
 await form.flush()
 form.state.customerButtonHandler63()
 form.dialogs.at(-1).success({ confirm: true })
 await form.flush()
 const execute = form.requests.find(entry => entry.options.url === '/admin/sys/config_button/execute')
 assert.ok(execute)
 const values = plain(execute.options.data.entity.attachments)
 assert.deepEqual(values, [
  { fileId: selectedId, name: '已有附件.txt', mimeType: 'text/plain', sizeBytes: 11 },
  { fileId: uploadedId, name: '新附件.txt', mimeType: 'text/plain', sizeBytes: 9 }
 ])
 assert.equal(form.writes().length, 0)
 assert.equal(requests.some(options => options.url.endsWith('/sync-refs')), false)
} catch (error) {
 failure = error
 throw error
} finally {
 try { form.app.unmount() } catch (error) { if (!failure) throw error }
}
