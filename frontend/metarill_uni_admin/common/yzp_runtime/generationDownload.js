/** 生成入口固定在当前 API，响应中的任意地址和命名空间均不能替换下载目标。 */
export function generationDownloadRequest(command, baseUrl, loginStateId, sysParams) {
  if (!command || command.type !== 'metarillGenerate' || !/^[1-9][0-9]*$/.test(command.projectId || '')) throw new Error('生成响应无效')
  if (!loginStateId) throw new Error('登录超时')
  if (String(sysParams && sysParams.yzProjectId || '') !== command.projectId) throw new Error('当前项目已变化，请重新生成')
  return { url: String(baseUrl || '').replace(/\/$/, '') + '/admin/sys/generation/generate', method: 'POST',
    header: { loginStateId, sysParams: encodeURIComponent(JSON.stringify(sysParams || {})), 'Content-Type': 'application/json' },
    body: JSON.stringify({ projectId: command.projectId }) }
}

/** H5 通过认证请求下载完整生成包；ZIP 内容通过后才触发浏览器保存，业务错误不能当作成功。 */
export async function downloadGeneration(request, isCurrent = () => true) {
  // #ifdef H5
  if (typeof window !== 'undefined') {
    const response = await fetch(request.url, { method: request.method, headers: request.header, body: request.body, redirect: 'error' })
    if (!response.ok || !(response.headers.get('content-type') || '').includes('application/zip')) {
      let message = '生成下载失败，请检查项目配置后重试'
      try { const error = await response.json(); if (error.msg) message = error.msg } catch { /* 非 JSON 失败响应使用安全提示。 */ }
      throw new Error(message)
    }
    const blob = await response.blob()
    if (!isCurrent()) throw new Error('当前登录或项目已变化，生成包未保存，请重新生成')
    const objectUrl = URL.createObjectURL(blob), link = document.createElement('a')
    link.href = objectUrl; link.download = 'metarill-application.zip'; document.body.appendChild(link)
    try { link.click() } finally { link.remove(); setTimeout(() => URL.revokeObjectURL(objectUrl), 1000) }
    return
  }
  // #endif
  // #ifndef H5
  throw new Error('当前平台暂未接入生成 ZIP 保存，请在 Web 管理端下载')
  // #endif
}
