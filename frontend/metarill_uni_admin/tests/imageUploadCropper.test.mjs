import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const modulePath = new URL('../common/yzp_runtime/imageUploadCropper.js', import.meta.url)

/**
 * 通过 data URL 加载 uni-app ES Module，避免修改当前前端 package.json 模块类型。
 */
async function loadImageCropperModule() {
	const source = await readFile(modulePath, 'utf8')
	return import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
}

test('图片文件可以通过 MIME 或常见后缀识别', async () => {
	const { isImageUploadFile } = await loadImageCropperModule()

	assert.equal(isImageUploadFile({ name: 'avatar.JPG', mimeType: '' }), true)
	assert.equal(isImageUploadFile({ name: '合同.pdf', mimeType: 'application/pdf' }), false)
	assert.equal(isImageUploadFile({ name: 'blob', mimeType: 'image/png' }), true)
})

test('裁切后文件名按目标 MIME 补正确后缀', async () => {
	const { buildCroppedFileName } = await loadImageCropperModule()

	assert.equal(buildCroppedFileName('avatar.png', 'image/jpeg'), 'avatar-crop.jpg')
	assert.equal(buildCroppedFileName('海报', 'image/png'), '海报-crop.png')
	assert.equal(buildCroppedFileName('', 'image/webp'), 'image-crop.webp')
})

test('非 H5 环境图片裁切直通原文件', async () => {
	const { cropImageFile } = await loadImageCropperModule()
	const file = { name: 'avatar.jpg', path: '/tmp/avatar.jpg', mimeType: 'image/jpeg' }

	assert.equal(await cropImageFile(file), file)
})

test('显式关闭裁切时直通原文件', async () => {
	const { cropImageFile } = await loadImageCropperModule()
	const file = { name: 'avatar.jpg', path: '/tmp/avatar.jpg', mimeType: 'image/jpeg' }

	assert.equal(await cropImageFile(file, { cropImages: false }), file)
})

test('H5 环境未显式开启裁切时直通原文件', async () => {
	const { cropImageFile } = await loadImageCropperModule()
	const previousWindow = globalThis.window
	const previousDocument = globalThis.document
	const file = { name: 'avatar.jpg', path: '/tmp/avatar.jpg', mimeType: 'image/jpeg' }

	try {
		globalThis.window = {}
		globalThis.document = { body: {} }

		assert.equal(await cropImageFile(file), file)
	} finally {
		if (previousWindow === undefined) {
			delete globalThis.window
		} else {
			globalThis.window = previousWindow
		}
		if (previousDocument === undefined) {
			delete globalThis.document
		} else {
			globalThis.document = previousDocument
		}
	}
})

test('只有显式开启裁切时才进入裁切流程', async () => {
	const source = await readFile(modulePath, 'utf8')

	assert.match(source, /options\.cropImages\s*!==\s*true/)
})

test('裁切默认框选整张图片', async () => {
	const source = await readFile(modulePath, 'utf8')

	assert.match(source, /autoCropArea:\s*1\b/)
})

test('裁切编辑区限制 Cropper 容器不越过底部操作栏', async () => {
	const source = await readFile(modulePath, 'utf8')

	assert.match(source, /<div class="yzp-image-cropper__stage">[\s\S]*<img class="yzp-image-cropper__image"/)
	assert.match(source, /\.yzp-image-cropper__body\s*\{[^}]*overflow:\s*hidden;/s)
	assert.match(source, /\.yzp-image-cropper__stage\s*\{[^}]*width:\s*100%;[^}]*height:\s*100%;[^}]*overflow:\s*visible;/s)
	assert.match(source, /\.yzp-image-cropper\s+\.cropper-crop-box\s*\{[^}]*z-index:\s*3;/s)
	assert.match(source, /\.yzp-image-cropper\s+\.cropper-line,[\s\S]*\.yzp-image-cropper\s+\.cropper-point\s*\{[^}]*z-index:\s*4;/s)
})
