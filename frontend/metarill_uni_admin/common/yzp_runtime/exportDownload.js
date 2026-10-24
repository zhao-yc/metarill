/** 仅对当前 API 的导出端点附加身份，外部链接永远不进入认证下载通道。 */
export function exportDownloadRequest(url, baseUrl, loginStateId, sysParams) {
  const prefix = String(baseUrl || '').replace(/\/$/, '') + '/admin/sys/export/export?exportId='
  if (typeof url !== 'string' || !url.startsWith(prefix) || !/^[a-f0-9]{64}$/.test(url.slice(prefix.length))) return null
  if (!loginStateId) throw new Error('登录超时')
  return { url, header: { loginStateId, sysParams: encodeURIComponent(JSON.stringify(sysParams || {})) } }
}

/** 保留原导出交互，使用请求头传递身份；票据与登录令牌均不拼接到其他地址。 */
export async function downloadExport(request) {
  // #ifdef H5
  if (typeof window !== 'undefined') {
    const response = await fetch(request.url, { headers: request.header, redirect: 'error' })
    if (!response.ok || !(response.headers.get('content-type') || '').includes('spreadsheetml')) throw new Error('导出下载失败或已过期，请重新导出')
    const blob = await response.blob(), objectUrl = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = objectUrl; link.download = '导出.xlsx'; document.body.appendChild(link); link.click(); link.remove()
    setTimeout(() => URL.revokeObjectURL(objectUrl), 1000)
    return
  }
  // #endif
  // #ifndef H5
  await new Promise((resolve, reject) => uni.downloadFile({ ...request,
    success: result => result.statusCode === 200 ? uni.openDocument({ filePath: result.tempFilePath, fileType: 'xlsx', success: resolve, fail: reject }) : reject(new Error('导出下载失败或已过期')),
    fail: reject }))
  // #endif
}
