import test from 'node:test'
import assert from 'node:assert/strict'
import { exportDownloadRequest } from '../common/yzp_runtime/exportDownload.js'
test('导出仅向当前API携带身份并保留项目上下文', () => {
  const base = 'https://example.test/api', url = base + '/admin/sys/export/export?exportId=' + 'a'.repeat(64)
  const params = { yzProjectId: '2200000000000100000' }
  const request = exportDownloadRequest(url, base, 'test-session', params)
  assert.equal(request.header.loginStateId, 'test-session')
  assert.deepEqual(JSON.parse(decodeURIComponent(request.header.sysParams)), params)
  assert.equal(exportDownloadRequest(url.replace('example.test', 'evil.test'), base, 'test-session', params), null)
  assert.equal(exportDownloadRequest(url + '&redirect=evil', base, 'test-session', params), null)
  assert.throws(() => exportDownloadRequest(url, base, '', params), /登录超时/)
})
