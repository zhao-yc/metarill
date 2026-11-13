import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const modulePath = new URL('../common/yzp_runtime/fileLibraryThumbnailPan.js', import.meta.url)

/**
 * 通过 data URL 加载 uni-app ES Module，避免修改当前前端 package.json 模块类型。
 */
async function loadThumbnailPanModule() {
	const source = await readFile(modulePath, 'utf8')
	return import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
}

test('横图放入方形容器时水平滚动', async () => {
	const { calculateThumbnailPan } = await loadThumbnailPanModule()

	const result = calculateThumbnailPan(200, 100, 100, 100)

	assert.equal(result.direction, 'horizontal')
	assert.equal(result.renderedWidth, 200)
	assert.equal(result.renderedHeight, 100)
	assert.equal(result.offsetPixels, 50)
})

test('竖图放入宽幅容器时垂直滚动', async () => {
	const { calculateThumbnailPan } = await loadThumbnailPanModule()

	const result = calculateThumbnailPan(100, 200, 200, 100)

	assert.equal(result.direction, 'vertical')
	assert.equal(result.renderedWidth, 200)
	assert.equal(result.renderedHeight, 400)
	assert.equal(result.offsetPixels, 150)
})

test('图片与容器比例一致时保持静态', async () => {
	const { calculateThumbnailPan } = await loadThumbnailPanModule()

	const result = calculateThumbnailPan(200, 100, 100, 50)

	assert.equal(result.direction, 'none')
	assert.equal(result.offsetPixels, 0)
})

test('无效尺寸保持静态且不产生渲染尺寸', async () => {
	const { calculateThumbnailPan } = await loadThumbnailPanModule()

	assert.deepEqual(calculateThumbnailPan(0, 100, 100, 100), {
		direction: 'none',
		renderedWidth: 0,
		renderedHeight: 0,
		offsetPixels: 0,
		durationSeconds: 0
	})
})

test('动画时长限制在八到十八秒', async () => {
	const { calculateThumbnailPan } = await loadThumbnailPanModule()

	assert.equal(calculateThumbnailPan(110, 100, 100, 100).durationSeconds, 8)
	assert.equal(calculateThumbnailPan(1000, 100, 100, 100).durationSeconds, 18)
})
