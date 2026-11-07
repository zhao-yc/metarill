const IMAGE_SUFFIXES = ['.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif']
const DEFAULT_OUTPUT_MIME_TYPE = 'image/jpeg'
const DEFAULT_OUTPUT_QUALITY = 0.92

let cropperModulePromise = null
let styleInstalled = false
let cropQueue = Promise.resolve()

/**
 * 判断当前运行时是否具备 H5 裁切所需 DOM 能力。
 */
export function isH5CropRuntime() {
	// 只在H5视图层运行DOM裁切；App逻辑层或小程序即使有同名全局对象也不进入。
	// #ifdef H5
	return typeof window !== 'undefined' && typeof document !== 'undefined' && !!document.body
	// #endif
	// #ifndef H5
	return false
	// #endif
}

/**
 * 识别 uni-file-picker、原生 File 和文件库统一后的图片文件对象。
 */
export function isImageUploadFile(file) {
	if (!file) {
		return false
	}
	const rawFile = file.rawFile || file.file || null
	const mimeType = String(file.mimeType || file.type || (rawFile && rawFile.type) || '').toLowerCase()
	if (mimeType.indexOf('image/') === 0) {
		return true
	}
	const name = String(file.name || file.path || file.tempFilePath || '').toLowerCase()
	return IMAGE_SUFFIXES.some((suffix) => name.endsWith(suffix))
}

/**
 * 裁切后的文件名统一追加 -crop，并按输出 MIME 修正后缀。
 */
export function buildCroppedFileName(name, mimeType) {
	const extension = mimeTypeToExtension(mimeType || DEFAULT_OUTPUT_MIME_TYPE)
	const safeName = String(name || '').trim() || 'image'
	const dotIndex = safeName.lastIndexOf('.')
	const baseName = dotIndex > 0 ? safeName.substring(0, dotIndex) : safeName
	return baseName + '-crop' + extension
}

/**
 * 对外统一入口：图片裁切默认暂停，只有显式开启且运行环境支持时才进入裁切流程。
 */
export async function cropImageFile(file, options = {}) {
	if (options.cropImages !== true || !isImageUploadFile(file) || !isH5CropRuntime()) {
		return file
	}
	// 兼容旧生成页并发调用 uploadFile 的情况，裁切弹窗始终按顺序打开。
	const cropTask = () => openCropDialog(file, options)
	const cropResult = cropQueue.then(cropTask, cropTask)
	cropQueue = cropResult.catch(() => null)
	return cropResult
}

/**
 * 加载 Cropper.js。这里用动态 import，避免非 H5 单元测试和小程序构建直接解析 DOM 库。
 */
async function loadCropper() {
	// H5专用动态分包不可进入App的IIFE构建，否则即使运行时未调用也无法编译。
	// #ifdef H5
	if (!cropperModulePromise) {
		cropperModulePromise = Promise.all([
			import('./vendor/cropperjs/cropper.esm.js'),
			import('./vendor/cropperjs/cropper.css').catch(() => null)
		]).then(([module]) => module.default || module.Cropper || (window && window.Cropper))
	}
	return cropperModulePromise
	// #endif
	// #ifndef H5
	return null
	// #endif
}

/**
 * 创建裁切弹窗并把用户操作转换成 Promise，便于上传链路顺序等待。
 */
async function openCropDialog(file, options) {
	const Cropper = await loadCropper()
	if (!Cropper) {
		return file
	}
	installDialogStyle()

	const source = buildImageSource(file)
	if (!source.src) {
		return file
	}

	return new Promise((resolve, reject) => {
		if (typeof uni !== 'undefined' && uni.hideLoading) {
			// 旧生成页会先显示“上传中”，这里先关掉，避免遮挡裁切弹窗。
			uni.hideLoading()
		}
		let cropper = null
		const modal = document.createElement('div')
		modal.className = 'yzp-image-cropper'
		modal.innerHTML = [
			'<div class="yzp-image-cropper__panel">',
			'<div class="yzp-image-cropper__header">',
			'<span>图片裁切</span>',
			'<button type="button" class="yzp-image-cropper__icon" data-action="cancel">×</button>',
			'</div>',
			'<div class="yzp-image-cropper__body">',
			'<div class="yzp-image-cropper__stage">',
			'<img class="yzp-image-cropper__image" alt="待裁切图片">',
			'</div>',
			'</div>',
			'<div class="yzp-image-cropper__footer">',
			'<button type="button" class="yzp-image-cropper__plain" data-action="original">原图上传</button>',
			'<button type="button" class="yzp-image-cropper__plain" data-action="cancel">取消</button>',
			'<button type="button" class="yzp-image-cropper__primary" data-action="crop">确认裁切</button>',
			'</div>',
			'</div>'
		].join('')

		const image = modal.querySelector('.yzp-image-cropper__image')
		image.src = source.src
		document.body.appendChild(modal)

		const cleanup = () => {
			if (cropper && typeof cropper.destroy === 'function') {
				cropper.destroy()
			}
			if (source.revoke && typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') {
				URL.revokeObjectURL(source.src)
			}
			if (modal.parentNode) {
				modal.parentNode.removeChild(modal)
			}
		}

		image.onload = () => {
			cropper = new Cropper(image, {
				viewMode: 1,
				// 默认框选整张图片，用户需要缩小时再手动调整裁切框。
				autoCropArea: 1,
				// 弹窗外层已经有遮罩，关闭 Cropper 内部暗色蒙层，避免盖住裁切区域下方内容。
				modal: false,
				background: false,
				responsive: true,
				checkOrientation: true,
				aspectRatio: options.aspectRatio || NaN
			})
		}

		modal.addEventListener('click', (event) => {
			const action = event.target && event.target.getAttribute('data-action')
			if (!action) {
				return
			}
			if (action === 'cancel') {
				cleanup()
				resolve(null)
				return
			}
			if (action === 'original') {
				cleanup()
				resolve(file)
				return
			}
			if (action === 'crop') {
				if (!cropper) {
					return
				}
				createCroppedFile(cropper, file, options).then((croppedFile) => {
					cleanup()
					resolve(croppedFile)
				}).catch((error) => {
					cleanup()
					reject(error)
				})
			}
		})
	})
}

/**
 * 原生 File 优先转对象 URL；已有 path 时直接用于图片预览。
 */
function buildImageSource(file) {
	const rawFile = file && (file.rawFile || file.file)
	if (rawFile && typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
		return {
			src: URL.createObjectURL(rawFile),
			revoke: true
		}
	}
	return {
		src: file && (file.path || file.tempFilePath || ''),
		revoke: false
	}
}

/**
 * 将 Cropper 输出的 canvas 转成上传链路可识别的 File/Blob 对象。
 */
function createCroppedFile(cropper, file, options) {
	const outputMimeType = options.outputMimeType || inferOutputMimeType(file)
	const canvas = cropper.getCroppedCanvas({
		maxWidth: options.maxWidth || 4096,
		maxHeight: options.maxHeight || 4096,
		imageSmoothingEnabled: true,
		imageSmoothingQuality: 'high'
	})
	if (!canvas) {
		return Promise.reject(new Error('图片裁切失败'))
	}
	return new Promise((resolve, reject) => {
		canvas.toBlob((blob) => {
			if (!blob) {
				reject(new Error('图片裁切失败'))
				return
			}
			const name = buildCroppedFileName(file && file.name, outputMimeType)
			const rawFile = typeof File !== 'undefined' ? new File([blob], name, {
				type: outputMimeType,
				lastModified: Date.now()
			}) : blob
			const objectUrl = typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function' ? URL.createObjectURL(rawFile) : ''
			resolve({
				...file,
				path: objectUrl || (file && file.path) || '',
				name,
				size: rawFile.size || blob.size || 0,
				mimeType: outputMimeType,
				rawFile,
				nativeObjectUrl: objectUrl,
				isCroppedUploadFile: true
			})
		}, outputMimeType, Number(options.outputQuality || DEFAULT_OUTPUT_QUALITY))
	})
}

function mimeTypeToExtension(mimeType) {
	const normalized = String(mimeType || '').toLowerCase()
	if (normalized === 'image/png') {
		return '.png'
	}
	if (normalized === 'image/webp') {
		return '.webp'
	}
	return '.jpg'
}

function inferOutputMimeType(file) {
	const rawFile = file && (file.rawFile || file.file)
	const mimeType = String((file && (file.mimeType || file.type)) || (rawFile && rawFile.type) || '').toLowerCase()
	if (mimeType === 'image/png' || mimeType === 'image/webp' || mimeType === 'image/jpeg') {
		return mimeType
	}
	const name = String(file && file.name || '').toLowerCase()
	if (name.endsWith('.png')) {
		return 'image/png'
	}
	if (name.endsWith('.webp')) {
		return 'image/webp'
	}
	return DEFAULT_OUTPUT_MIME_TYPE
}

/**
 * 弹窗壳层样式由运行时注入，避免每个生成页都手动引入样式。
 */
function installDialogStyle() {
	if (styleInstalled || !isH5CropRuntime()) {
		return
	}
	styleInstalled = true
	const style = document.createElement('style')
	style.textContent = `
.yzp-image-cropper {
	position: fixed;
	z-index: 99999;
	inset: 0;
	display: flex;
	align-items: center;
	justify-content: center;
	background: rgba(15, 23, 42, 0.62);
}
.yzp-image-cropper__panel {
	width: min(920px, calc(100vw - 32px));
	height: min(720px, calc(100vh - 32px));
	display: flex;
	flex-direction: column;
	background: #fff;
	border-radius: 8px;
	overflow: hidden;
	box-shadow: 0 22px 70px rgba(15, 23, 42, 0.28);
}
.yzp-image-cropper__header,
.yzp-image-cropper__footer {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 12px 16px;
	border-bottom: 1px solid #e5e7eb;
}
.yzp-image-cropper__footer {
	justify-content: flex-end;
	gap: 10px;
	border-top: 1px solid #e5e7eb;
	border-bottom: 0;
}
.yzp-image-cropper__body {
	flex: 1;
	min-height: 0;
	padding: 14px;
	box-sizing: border-box;
	display: flex;
	align-items: center;
	justify-content: center;
	overflow: hidden;
	background: #f8fafc;
}
.yzp-image-cropper__stage {
	width: 100%;
	height: 100%;
	min-height: 0;
	display: flex;
	align-items: center;
	justify-content: center;
	/* Cropper 的顶部/底部边线会向外偏移几像素，stage 不能裁掉这些控制线。 */
	overflow: visible;
	position: relative;
}
.yzp-image-cropper__image {
	display: block;
	width: auto;
	height: auto;
	max-width: 100%;
	max-height: 100%;
}
.yzp-image-cropper .cropper-modal {
	background: transparent;
	opacity: 0;
}
.yzp-image-cropper .cropper-crop-box {
	z-index: 3;
}
.yzp-image-cropper .cropper-line,
.yzp-image-cropper .cropper-point {
	z-index: 4;
}
.yzp-image-cropper__icon,
.yzp-image-cropper__plain,
.yzp-image-cropper__primary {
	border: 0;
	border-radius: 6px;
	cursor: pointer;
}
.yzp-image-cropper__icon {
	width: 32px;
	height: 32px;
	background: transparent;
	font-size: 24px;
	line-height: 30px;
	color: #64748b;
}
.yzp-image-cropper__plain,
.yzp-image-cropper__primary {
	padding: 0 14px;
	height: 32px;
	font-size: 14px;
}
.yzp-image-cropper__plain {
	background: #eef2f7;
	color: #334155;
}
.yzp-image-cropper__primary {
	background: #007aff;
	color: #fff;
}
`
	document.head.appendChild(style)
}
