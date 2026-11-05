import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'

const component = await readFile(new URL('../components/yzp-file-library/yzp-file-library.vue', import.meta.url), 'utf8')
const wrapper = await readFile(new URL('../../../backend/templates/standalone-frontend/frontend-yzp.js.tpl', import.meta.url), 'utf8')

/** 在同一运行时对象执行实际组件和独立包适配，只模拟请求与弹窗。 */
function fixture() {
 const dialogs = [], requests = []
 const yzp = { request: options => requests.push(options) }
 const context = vm.createContext({ yzp, uni: { showModal: options => dialogs.push(options) } })
 vm.runInContext(wrapper.replace(/^import .*$/m, '').replace('export default yzp', ''), context)
 const script = component.match(/<script>([\s\S]*?)<\/script>/)[1]
 const { methods } = vm.runInContext(script.replace(/^import\s+[\s\S]+? from '[^']+'\s*$/gm, '').replace('export default {', '({') + ')', context)
 const record = { fileId: '9007199254740993' }
 const state = { ...methods, files: [record], dialogFiles: [record], libraryFiles: [record] }
 state.normalizeFileItem = item => item
 return { dialogs, requests, state, record }
}

const tick = () => new Promise(resolve => setImmediate(resolve))

test('三个后台解析在拒权时保留选项且不弹窗，下次仍可解析', async () => {
 const { dialogs, requests, state, record } = fixture()
 state.resolveFiles()
 state.resolveDialogFiles()
 state.resolveLibraryFiles()
 assert.equal(requests.length, 3)
 for (const request of requests) {
  assert.equal(request.url, '/admin/sys/file-library/resolve')
  assert.equal(request.showError, false)
  assert.equal(request.data.fileIds[0], record.fileId)
  request.success({ code: 403, msg: '没有当前操作权限。' })
 }
 await tick()
 assert.equal(dialogs.length, 0)
 assert.equal(state.resolving, false)
 for (const list of [state.files, state.dialogFiles, state.libraryFiles]) assert.equal(list[0], record)

 state.mergeResolvedAccess = (file, resolved) => ({ ...file, url: resolved.url })
 state.resolveFiles()
 assert.equal(requests.length, 4)
 requests[3].success({ code: 200, data: [{ fileId: record.fileId, url: '/protected' }] })
 await tick()
 assert.equal(state.files[0].url, '/protected')
 assert.equal(state.resolving, false)
})

test('主动预览仍使用原提示，和后台解析共用同一地址', () => {
 const { dialogs, requests, state, record } = fixture()
 // 主动预览原有 Promise 由页面调用，这里捕获其拒绝以检查提示。
 let rejection
 const request = state.request
 state.request = function(options) {
  const result = request.call(this, options)
  return result.catch(error => { rejection = error; return [] })
 }
 state.showToast = () => {}
 state.previewLibraryFile(record)
 assert.equal(requests[0].url, '/admin/sys/file-library/resolve')
 assert.equal(requests[0].showError, undefined)
 requests[0].success({ code: 403, msg: '没有当前操作权限。' })
 assert.equal(dialogs.length, 1)
 assert.equal(dialogs[0].content, '没有当前操作权限。')
 return tick().then(() => assert.equal(rejection.message, '没有当前操作权限。'))
})
