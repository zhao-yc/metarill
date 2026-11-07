import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import vm from 'node:vm'

const runtimeSource = await readFile(new URL('../common/yzp_runtime/tinyCloudEditor.js', import.meta.url), 'utf8')
const { createTinyCloudEditorInit, validateTinyCloudConfiguration, uploadTinyCloudImage } = await import('data:text/javascript;base64,' + Buffer.from(runtimeSource).toString('base64'))
const source = await readFile(new URL('../pages/main/rtf/rtf.vue', import.meta.url), 'utf8')
const configuration = { enabled: true, apiKey: 'mock-key_123', cloudChannel: '8', scriptSrc: 'https://cdn.tiny.cloud/1/mock-key_123/tinymce/8/tinymce.min.js', language: 'zh-CN', premiumPlugins: [] }

/** 运行实际页面生命周期；配置只使用无效测试标识，不连接真实 Tiny Cloud。 */
function fixture(action = 'edit') {
 const requests = [], events = [], handlers = {}, timers = new Map(), removed = [], storage = { loginStateId: '测试登录态', sysParams: { yzProjectId: '测试项目' } }
 let counter = 0
 const channel = { on: (name, handler) => { handlers[name] = handler }, off: name => removed.push(name), emit: (name, value) => events.push({ name, value }) }
 const context = { Editor: {}, Set, createTinyCloudEditorInit, validateTinyCloudConfiguration, uploadTinyCloudImage,
  setTimeout: callback => { const id = ++counter; timers.set(id, callback); return id }, clearTimeout: id => timers.delete(id),
  uni: { getStorageSync: key => storage[key], setNavigationBarTitle() {} }, yzp: { request: options => { const task = { aborted: false, abort() { task.aborted = true } }; requests.push({ options, task }); return task } } }
 vm.runInNewContext(source.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*;$/gm, '').replace('export default', 'globalThis.options ='), context)
 const options = context.options, state = Object.assign(options.data(), options.methods, { getOpenerEventChannel: () => channel })
 options.onLoad.call(state, { action })
 return { options, state, requests, handlers, events, timers, removed, storage }
}

test('原预览仅回显HTML，不读取Cloud配置；原编辑使用认证公共请求', () => {
 const review = fixture('review'); review.handlers.sendRtf({ data: '<p>原内容</p>' }); assert.equal(review.state.rtf, '<p>原内容</p>'); assert.equal(review.requests.length, 0)
 review.options.onUnload.call(review.state); assert.equal(review.events.length, 0)
 const edit = fixture(); assert.equal(edit.requests[0].options.url, '/admin/sys/tiny-cloud/config'); assert.equal(edit.requests[0].options.method, 'POST'); assert.equal(edit.requests[0].options.sendSessionId, true)
})

test('缺配置和损坏配置不挂载编辑器，也不清空既有富文本', () => {
 for (const data of [{ enabled: false, message: '尚未配置 Tiny Cloud' }, { ...configuration, scriptSrc: 'https://example.invalid/editor.js' }]) {
  const f = fixture(); f.handlers.sendRtf({ data: '<p>保持</p>' }); f.requests[0].options.success({ code: 200, data })
  assert.equal(f.state.editorConfiguration, null); assert.equal(f.state.rtf, '<p>保持</p>'); assert.match(f.state.editorMessage, /未配置|配置无效/)
 }
 assert.equal(validateTinyCloudConfiguration({ ...configuration, apiKey: 'no-api-key' }), false)
})

test('配置通过后保留原菜单和远程中文，v-model接线与退出最新HTML回传相连', () => {
 const f = fixture(); f.handlers.sendRtf({ data: '<p>传入</p>' }); f.requests[0].options.success({ code: 200, data: configuration })
 assert.equal(f.state.loadState, 'configured'); assert.equal(f.state.init.language, 'zh-CN'); assert.equal(f.state.init.language_url, undefined)
 assert.match(f.state.init.toolbar, /fontfamily.*fontsize.*image.*table.*code.*preview/); assert.match(source, /v-model="rtf"/)
 f.state.onEditorInit({}, { getContent: () => '<p>最后一次输入</p>' }); assert.equal(f.state.loadState, 'ready'); assert.equal(f.timers.size, 0)
 f.state.rtf = '<p>v-model更新</p>'; f.options.onUnload.call(f.state); f.options.onUnload.call(f.state)
 assert.deepEqual(f.events, [{ name: 'onReceiveRtf', value: '<p>最后一次输入</p>' }]); assert.deepEqual(f.removed, ['sendRtf']); assert.equal(f.requests[0].task.aborted, true)
})

test('页面退出、工作区变化和旧请求回调均不能再挂载编辑器', () => {
 const departed = fixture(); departed.options.onUnload.call(departed.state); departed.requests[0].options.success({ code: 200, data: configuration }); assert.equal(departed.state.editorConfiguration, null)
 let removed = 0; departed.state.onEditorInit({}, { remove: () => removed++ }); assert.equal(removed, 1)
 const changed = fixture(); changed.storage.loginStateId = '另一个登录态'; changed.requests[0].options.success({ code: 200, data: configuration }); assert.equal(changed.state.editorConfiguration, null)
 const renewed = fixture(); renewed.state.loadEditorConfiguration(); renewed.requests[0].options.success({ code: 200, data: configuration }); assert.equal(renewed.state.editorConfiguration, null); assert.equal(renewed.requests[0].task.aborted, true)
 renewed.requests[1].options.success({ code: 200, data: configuration }); assert.equal(renewed.state.editorConfiguration.apiKey, 'mock-key_123')
})

test('加载失败和超时显示明确中文状态，不改内容；付费插件只按部署声明开启', () => {
 const f = fixture(); f.handlers.sendRtf({ data: '<p>保留</p>' }); f.requests[0].options.fail({}); assert.equal(f.state.loadState, 'error'); assert.equal(f.state.rtf, '<p>保留</p>')
 f.state.loadEditorConfiguration(); f.requests[1].options.success({ code: 200, data: configuration }); Array.from(f.timers.values())[0](); assert.match(f.state.editorMessage, /网络、账户授权和域名/)
 const init = createTinyCloudEditorInit({ premiumPlugins: ['mergetags', 'checklist', '随意外部插件'] }, () => {})
 assert.match(init.plugins, /mergetags checklist$/); assert.doesNotMatch(init.plugins, /随意/); assert.equal(init.images_upload_url, undefined)
})

/** 创建真实浏览器File的上传参数，模拟uni.uploadFile返回，不写实际文件。 */
function uploadFixture() {
 let request, task, current = true, progressCallback
 const started = [], completed = [], progress = []
 const context = { baseUrl: 'https://metarill.example/api', headers: { loginStateId: '仅测试会话', sysParams: '%7B%7D' }, isCurrent: () => current,
  onStart: value => started.push(value), onComplete: value => completed.push(value), uploadFile: options => { request = options; task = { onProgressUpdate: callback => { progressCallback = callback } }; return task } }
 const pending = uploadTinyCloudImage({ blob: () => new Blob(['图片'], { type: 'image/png' }), filename: () => '图片.png' }, value => progress.push(value), context)
 return { pending, get request() { return request }, get task() { return task }, started, completed, progress, changed: () => { current = false }, progressEvent: value => progressCallback(value) }
}

test('图片通过原multipart入口和会话头上传，只有成功的公开location才插入', async () => {
 const f = uploadFixture(); assert.equal(f.request.url, 'https://metarill.example/api/admin/sys/upload/uploadImage'); assert.equal(f.request.name, 'file'); assert.equal(f.request.file.name, '图片.png'); assert.equal(f.request.header.loginStateId, '仅测试会话'); assert.doesNotMatch(f.request.url, /loginStateId|sysParams/)
 f.progressEvent({ progress: 40 }); assert.deepEqual(f.progress, [40])
 f.request.success({ statusCode: 200, data: JSON.stringify({ code: 200, location: '/api/admin/sys/file-library/public-content/123', data: { url: '不使用此备用字段' } }) }); f.request.complete()
 assert.equal(await f.pending, 'https://metarill.example/api/admin/sys/file-library/public-content/123'); assert.deepEqual(f.completed, [f.task])
})

test('图片失败、非法地址和编辑页失效均拒绝插入，绝不回退伪URL', async () => {
 for (const response of [{ statusCode: 200, data: { code: 500, msg: '图片验证失败' } }, { statusCode: 200, data: { code: 200, location: 'javascript:alert(1)' } }, { statusCode: 200, data: { code: 200, data: { url: 'https://example.invalid' } } }]) {
  const f = uploadFixture(); f.request.success(response); await assert.rejects(f.pending)
 }
 const expired = uploadFixture(); expired.changed(); expired.request.success({ statusCode: 200, data: { code: 200, location: 'https://example.invalid/image.png' } }); await assert.rejects(expired.pending, /页面已失效/)
})

/** TinyMCE初始化可规范化HTML，模拟真实dirty标记区分用户输入，不访问Cloud。 */
test('只打开编辑器不编辑时保留原始HTML，真实编辑才采用最新内容', () => {
 for (const original of ['<P class="raw">原文&nbsp;</P>', '']) {
  const clean = fixture(); clean.handlers.sendRtf({ data: original }); clean.requests[0].options.success({ code: 200, data: configuration })
  clean.state.onEditorInit({}, { isDirty: () => false, getContent: () => '<p>初始化规范化</p>' }); clean.state.rtf = '<p>初始化规范化</p>'
  clean.options.onUnload.call(clean.state); assert.deepEqual(clean.events, [{ name: 'onReceiveRtf', value: original }])
 }
 const dirty = fixture(); dirty.handlers.sendRtf({ data: '<p>原文</p>' }); dirty.requests[0].options.success({ code: 200, data: configuration })
 dirty.state.onEditorInit({}, { isDirty: () => true, getContent: () => '<p>用户修改</p>' }); dirty.options.onUnload.call(dirty.state)
 assert.deepEqual(dirty.events, [{ name: 'onReceiveRtf', value: '<p>用户修改</p>' }])
})
