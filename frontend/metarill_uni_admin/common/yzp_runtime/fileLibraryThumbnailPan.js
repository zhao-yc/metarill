const CROP_TOLERANCE_PIXELS = 1
const MIN_DURATION_SECONDS = 8
const MAX_DURATION_SECONDS = 18
const PAN_SPEED_PIXELS_PER_SECOND = 24
const ENDPOINT_PAUSE_SECONDS = 2

/**
 * 按缩略图 cover 渲染规则计算裁切方向、渲染尺寸和自动浏览参数。
 */
export function calculateThumbnailPan(naturalWidth, naturalHeight, containerWidth, containerHeight) {
	const sourceWidth = normalizePositiveNumber(naturalWidth)
	const sourceHeight = normalizePositiveNumber(naturalHeight)
	const viewportWidth = normalizePositiveNumber(containerWidth)
	const viewportHeight = normalizePositiveNumber(containerHeight)
	if (!sourceWidth || !sourceHeight || !viewportWidth || !viewportHeight) {
		return createStaticResult()
	}

	const scale = Math.max(viewportWidth / sourceWidth, viewportHeight / sourceHeight)
	const renderedWidth = roundPixels(sourceWidth * scale)
	const renderedHeight = roundPixels(sourceHeight * scale)
	const overflowX = Math.max(renderedWidth - viewportWidth, 0)
	const overflowY = Math.max(renderedHeight - viewportHeight, 0)
	const direction = resolveDirection(overflowX, overflowY)
	if (direction === 'none') {
		return {
			direction,
			renderedWidth,
			renderedHeight,
			offsetPixels: 0,
			durationSeconds: 0
		}
	}

	const overflowPixels = direction === 'horizontal' ? overflowX : overflowY
	const offsetPixels = roundPixels(overflowPixels / 2)
	// 一轮动画从中心依次浏览两端并回到中心，总移动距离为四倍偏移量。
	const durationSeconds = clamp(
		(offsetPixels * 4 / PAN_SPEED_PIXELS_PER_SECOND) + ENDPOINT_PAUSE_SECONDS,
		MIN_DURATION_SECONDS,
		MAX_DURATION_SECONDS
	)
	return {
		direction,
		renderedWidth,
		renderedHeight,
		offsetPixels,
		durationSeconds: roundPixels(durationSeconds)
	}
}

function resolveDirection(overflowX, overflowY) {
	if (overflowX <= CROP_TOLERANCE_PIXELS && overflowY <= CROP_TOLERANCE_PIXELS) {
		return 'none'
	}
	return overflowX >= overflowY ? 'horizontal' : 'vertical'
}

function createStaticResult() {
	return {
		direction: 'none',
		renderedWidth: 0,
		renderedHeight: 0,
		offsetPixels: 0,
		durationSeconds: 0
	}
}

function normalizePositiveNumber(value) {
	const number = Number(value || 0)
	return Number.isFinite(number) && number > 0 ? number : 0
}

function roundPixels(value) {
	return Number(Number(value || 0).toFixed(2))
}

function clamp(value, min, max) {
	return Math.min(Math.max(value, min), max)
}

export default {
	calculateThumbnailPan
}
