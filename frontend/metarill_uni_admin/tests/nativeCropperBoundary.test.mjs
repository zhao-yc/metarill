import assert from 'node:assert/strict'
import test from 'node:test'
import vm from 'node:vm'
import { readFile } from 'node:fs/promises'

const source = await readFile(new URL('../common/yzp_runtime/imageUploadCropper.js', import.meta.url), 'utf8')

/** 按实际H5条件剔除动态库导入，验证App和小程序逻辑层不加载DOM裁切。 */
function scriptFor(h5) {
 const active = [], lines = []
 for (const line of source.split(/\r?\n/)) {
  const condition = line.match(/^\s*\/\/ #(ifdef|ifndef) H5\s*$/)
  if (condition) { active.push(condition[1] === 'ifdef' ? h5 : !h5); continue }
  if (/^\s*\/\/ #endif\s*$/.test(line)) { assert.ok(active.length); active.pop(); continue }
  if (active.every(Boolean)) lines.push(line)
 }
 assert.equal(active.length, 0)
 return lines.join('\n')
}

for (const platform of ['App逻辑层', '小程序']) {
 test(platform + '编译后无动态分包并继续原文件上传，即使存在同名DOM全局', async () => {
  const script = scriptFor(false)
  assert.doesNotMatch(script, /import\s*\(/)
  const context = { window: {}, document: { body: {}, createElement() { throw new Error('不应进入DOM裁切') } } }
  vm.runInNewContext(script.replace(/^export /gm, '') + '\nglobalThis.api = { isH5CropRuntime, cropImageFile }', context)
  assert.equal(context.api.isH5CropRuntime(), false)
  const file = { name: '原图.png', type: 'image/png' }
  assert.equal(await context.api.cropImageFile(file, { cropImages: true }), file)
 })
}

test('H5继续按需加载原Cropper与CSS，无DOM时仍保留原文件', async () => {
 const script = scriptFor(true)
 assert.match(script, /import\('\.\/vendor\/cropperjs\/cropper\.esm\.js'\)/)
 assert.match(script, /import\('\.\/vendor\/cropperjs\/cropper\.css'\)/)
 const context = {}
 vm.runInNewContext(script.replace(/^export /gm, '') + '\nglobalThis.api = { isH5CropRuntime, cropImageFile }', context)
 assert.equal(context.api.isH5CropRuntime(), false)
 const file = { name: '原图.png', type: 'image/png' }
 assert.equal(await context.api.cropImageFile(file, { cropImages: true }), file)
})
