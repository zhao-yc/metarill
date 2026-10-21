import http from 'node:http'
import { realpath, stat, readFile, appendFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

// 此工具只服务人工验收的构建产物；代理目标固定为同一私有会话的回环宿主。
export function validatePreview(config, now = Date.now()) {
 const frontend = new URL(config.frontendAddress), backend = new URL(config.backendAddress)
 for (const url of [frontend, backend]) {
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || !url.port || url.username || url.password || url.pathname !== '/' || url.search || url.hash) throw new Error('验收只允许固定回环HTTP地址')
 }
 const expires = Date.parse(config.expiresAt)
 if (!Number.isFinite(expires) || expires <= now || expires - now > 1800000 || !/^[a-f0-9]{32}$/.test(config.leaseId) || !path.isAbsolute(config.frontend)) throw new Error('验收会话标识、产物路径或时限无效')
 return { frontend, backend, expires }
}

const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.svg': 'image/svg+xml', '.jpg': 'image/jpeg', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.ico': 'image/x-icon' }

// 静态目录先解析真实路径；API原样转发，日志只留方法、路径和状态，不记录查询、正文或身份头。
export async function createManagementPreview(config, traceFile) {
 const lease = validatePreview(config), root = await realpath(path.join(config.frontend, 'unpackage/dist/build/web'))
 const trace = (request, pathname, status) => traceFile ? appendFile(traceFile, JSON.stringify({ method: request.method, pathname, status, at: new Date().toISOString() }) + '\n').catch(() => {}) : undefined
 return http.createServer(async (request, response) => {
  let pathname = ''
  try {
   pathname = decodeURIComponent(new URL(request.url, lease.frontend).pathname)
   if (Date.now() >= lease.expires) { response.writeHead(410).end('验收已到期'); return }
   if (pathname === '/api' || pathname.startsWith('/api/')) {
    const headers = { ...request.headers, host: lease.backend.host }; delete headers.connection
    const upstream = http.request({ hostname: lease.backend.hostname, port: lease.backend.port, path: request.url, method: request.method, headers }, result => {
     response.writeHead(result.statusCode, result.headers); result.pipe(response); trace(request, pathname, result.statusCode)
    })
    upstream.on('error', () => { if (!response.headersSent) response.writeHead(502); response.end('验收宿主不可用') })
    request.on('aborted', () => upstream.destroy()); request.pipe(upstream); return
   }
   if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405).end(); return }
   let file = path.resolve(root, '.' + pathname)
   if (file !== root && !file.startsWith(root + path.sep)) { response.writeHead(403).end(); return }
   if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html')
   file = await realpath(file)
   if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return }
   const data = await readFile(file)
   response.writeHead(200, { 'content-type': mime[path.extname(file)] || 'application/octet-stream', 'content-length': data.length, 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' }); response.end(request.method === 'HEAD' ? undefined : data)
  } catch { if (!response.headersSent) response.writeHead(404); response.end('未找到验收产物') }
 })
}

// CLI只读取不含口令的preview.json，固定监听会话预留端口；到期或退出信号关闭本工具服务器。
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
 const input = path.resolve(process.argv[2]), config = JSON.parse(await readFile(input, 'utf8')), lease = validatePreview(config)
 const server = await createManagementPreview(config, path.join(path.dirname(input), 'api-trace.jsonl'))
 const stop = () => { server.closeAllConnections(); server.close() }
 server.listen(Number(lease.frontend.port), lease.frontend.hostname, () => console.log('原管理页面私有预览：' + lease.frontend.origin))
 server.on('error', () => { console.error('预览端口启动失败，未替换其他服务'); process.exitCode = 1 })
 const timer = setTimeout(stop, Math.max(1, lease.expires - Date.now())); timer.unref()
 process.once('SIGINT', stop); process.once('SIGTERM', stop)
}
