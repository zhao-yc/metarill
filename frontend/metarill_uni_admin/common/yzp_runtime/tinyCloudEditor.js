/** 保持原富文本菜单、排版和字体；TinyMCE 8 的按钮及属性名称使用官方现行名称。 */
export function createTinyCloudEditorInit(configuration, uploadImage) {
 const premium = Array.isArray(configuration.premiumPlugins) ? configuration.premiumPlugins.filter(item => ['mergetags', 'checklist'].includes(item)) : []
 return {
  height: 600, branding: false,
  contextmenu: 'copy paste | link image inserttable | cell row column deletetable',
  style_formats: [{ title: '首行缩进', block: 'p', styles: { 'text-indent': '2em' } }], style_formats_merge: true,
  plugins: ['preview anchor autolink charmap codesample emoticons image link lists media searchreplace table visualblocks wordcount fullscreen code', ...premium].join(' '),
  toolbar: 'undo redo | styles | fontfamily | fontsize | bold italic underline strikethrough forecolor backcolor | link image media table mergetags | align lineheight | checklist numlist bullist indent outdent | emoticons charmap | removeformat fullscreen code preview',
  font_family_formats: "微软雅黑='微软雅黑';宋体='宋体';黑体='黑体';仿宋='仿宋';楷体='楷体';隶书='隶书';幼圆='幼圆';Andale Mono=andale mono,times;Arial=arial,helvetica,sans-serif;Arial Black=arial black,avant garde;Book Antiqua=book antiqua,palatino;Comic Sans MS=comic sans ms,sans-serif;Courier New=courier new,courier;Georgia=georgia,palatino;Helvetica=helvetica;Impact=impact,chicago;Symbol=symbol;Tahoma=tahoma,arial,helvetica,sans-serif;Terminal=terminal,monaco;Times New Roman=times new roman,times;Trebuchet MS=trebuchet ms,geneva;Verdana=verdana,geneva;Webdings=webdings;Wingdings=wingdings",
  convert_urls: false, language: 'zh-CN',
  // 中文资源由当前 Tiny Cloud 账户远程提供，不分发原来源不明的语言包。
  images_upload_handler: uploadImage
 }
}

/** 配置必须完整匹配官方 Cloud 地址，避免损坏响应触发任意脚本源或 no-api-key 回退。 */
export function validateTinyCloudConfiguration(value) {
 if (!value || value.enabled !== true || typeof value.apiKey !== 'string' || !/^[A-Za-z0-9_-]{1,256}$/.test(value.apiKey) || value.apiKey === 'no-api-key') return false
 if (typeof value.cloudChannel !== 'string' || !/^8(?:\.[0-9]{1,3})?$/.test(value.cloudChannel) || value.language !== 'zh-CN') return false
 return value.scriptSrc === `https://cdn.tiny.cloud/1/${encodeURIComponent(value.apiKey)}/tinymce/${value.cloudChannel}/tinymce.min.js`
}

/** 图片仍使用原公开上传协议；会话只在请求头发送，失效页面不接收迟到的上传地址。 */
export function uploadTinyCloudImage(blobInfo, progress, context) {
 return new Promise((resolve, reject) => {
  if (!context.isCurrent() || !context.headers.loginStateId) { reject(new Error('登录或编辑页面已失效，请重新进入。')); return }
  if (typeof File === 'undefined') { reject(new Error('当前环境不支持此富文本图片上传。')); return }
  const blob = blobInfo.blob(), file = new File([blob], blobInfo.filename(), { type: blob.type })
  let task, completed = false
  task = context.uploadFile({ url: context.baseUrl.replace(/\/$/, '') + '/admin/sys/upload/uploadImage', name: 'file', file, header: context.headers,
   success: response => {
    if (!context.isCurrent()) { reject(new Error('编辑页面已失效。')); return }
    let body
    try { body = typeof response.data === 'string' ? JSON.parse(response.data) : response.data } catch { reject(new Error('图片上传响应无效。')); return }
    if (response.statusCode !== 200 || body?.code !== 200 || typeof body.location !== 'string') { reject(new Error(body?.msg || '图片上传失败。')); return }
    try {
     const location = new URL(body.location, context.baseUrl.replace(/\/$/, '') + '/')
     if (!['http:', 'https:'].includes(location.protocol) || location.username || location.password) throw new Error()
     resolve(location.href)
    } catch { reject(new Error('图片上传地址无效。')) }
   }, fail: () => reject(new Error('图片上传失败，请重新选择。')),
   complete: () => { completed = true; if (task) context.onComplete(task) } })
  if (!completed) context.onStart(task)
  if (task && typeof task.onProgressUpdate === 'function') task.onProgressUpdate(event => { if (context.isCurrent()) progress(event.progress) })
 })
}
