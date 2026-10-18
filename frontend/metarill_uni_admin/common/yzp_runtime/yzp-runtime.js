import { generationDownloadRequest, downloadGeneration } from './generationDownload';
import utils from "../utils";
import {
	evaluateRule as evaluateButtonVisibleRuleCore
} from "./buttonVisibleRule";
import {
	cropImageFile
} from "./imageUploadCropper";
import * as XLSX from 'xlsx'; //官网https://docs.sheetjs.com/docs/api/parse-options
import { relationArray, relationDisplayText, selectedRelationRows, refreshSelectedRelationRows, relationWriteEntity } from './relationReadValues';

import pageWriteSchema from './page-write-schema.generated.json';
import { createPageWriteFilter } from './pageWritePayload';

const filterPageWriteData = createPageWriteFilter(pageWriteSchema);

const TAB_BAR_PAGES = [
	"/pages/main/index",
	"/pages/main/menu",
	"/pages/main/my"
];
const CURRENT_PERMISSIONS_KEY = "currentPermissions";
const CURRENT_PERMISSIONS_VERSION_KEY = "currentPermissionsVersion";

// 请求共用加载框：仅记录接口路径和耗时，不保存请求参数、响应内容或登录信息。
const pendingRequests = new Map();
const requestLoadingEvents = [];
let requestSequence = 0;
let requestLoadingTimer = null;
let requestLoadingHideTimer = null;
let requestLoadingVisible = false;
let requestLoadingShownAt = 0;

/** 记录有限的请求时序，便于排查偶发加载状态残留。 */
function recordRequestEvent(id, path, phase) {
	requestLoadingEvents.push({ id, path, phase, time: Date.now(), pending: pendingRequests.size });
	if (requestLoadingEvents.length > 80) {
		requestLoadingEvents.shift();
	}
}

/** 为每个请求取得独立凭据，只有最后一个请求结束时才关闭加载框。 */
function beginRequestLoading(path, showLoading) {
	const id = ++requestSequence;
	pendingRequests.set(id, { id, path, startedAt: Date.now(), showLoading });
	recordRequestEvent(id, path, 'start');
	if (showLoading) {
		clearTimeout(requestLoadingHideTimer);
		requestLoadingHideTimer = null;
		if (!requestLoadingVisible && requestLoadingTimer === null) {
			// 快速请求不显示；串联和并发请求共用同一个待显示任务。
			requestLoadingTimer = setTimeout(() => {
				requestLoadingTimer = null;
				requestLoadingVisible = true;
				requestLoadingShownAt = Date.now();
				uni.showLoading({ title: '加载中', mask: true });
				recordRequestEvent(id, path, 'show');
			}, 150);
		}
	}
	return {
		id,
		/** 幂等释放，避免 success、complete、超时同时到达时重复扣减。 */
		finish(phase) {
			if (!pendingRequests.delete(id)) {
				return;
			}
			recordRequestEvent(id, path, phase);
			if (Array.from(pendingRequests.values()).some(item => item.showLoading)) {
				return;
			}
			clearTimeout(requestLoadingTimer);
			requestLoadingTimer = null;
			if (requestLoadingVisible && requestLoadingHideTimer === null) {
				// H5 的显示内部还有异步渲染；留出显示时间，并让新请求取消待关闭任务。
				requestLoadingHideTimer = setTimeout(() => {
					requestLoadingHideTimer = null;
					requestLoadingVisible = false;
					uni.hideLoading();
					recordRequestEvent(id, path, 'hide');
				}, Math.max(0, 50 - (Date.now() - requestLoadingShownAt)));
			}
		}
	};
}

var yzp = {
	/** 登录失效和主动退出复用同一清理入口，宿主可保留纯界面偏好。 */
	clearLoginStorage() {
		uni.clearStorageSync();
	},
	// 关系派生值与持久化载荷分离，所有生成页面复用相同数组与空值语义。
	relationArray,
	relationDisplayText,
	selectedRelationRows,
	refreshSelectedRelationRows,
	relationWriteEntity,
	normalizePageUrl(url) {
		if (utils.isBlank(url)) {
			return "";
		}
		var normalizedUrl = String(url).trim();
		if (normalizedUrl.indexOf("#") === 0) {
			normalizedUrl = normalizedUrl.substring(1);
		}
		if (normalizedUrl.indexOf("/") !== 0) {
			normalizedUrl = "/" + normalizedUrl;
		}
		return normalizedUrl;
	},
	buildQueryString(params) {
		if (!params || typeof params !== "object") {
			return "";
		}
		var queryItems = [];
		Object.keys(params).forEach((key) => {
			var value = params[key];
			if (value === undefined || value === null || value === "") {
				return;
			}
			queryItems.push(encodeURIComponent(key) + "=" + encodeURIComponent(String(value)));
		});
		return queryItems.length > 0 ? "?" + queryItems.join("&") : "";
	},
	isTabBarPage(url) {
		var normalizedUrl = this.normalizePageUrl(url);
		var routePath = normalizedUrl.split("?")[0];
		return TAB_BAR_PAGES.indexOf(routePath) >= 0;
	},
	getCurrentPageRedirectUrl() {
		var currentPages = typeof getCurrentPages === "function" ? getCurrentPages() : [];
		if (!currentPages || currentPages.length === 0) {
			return "";
		}
		var currentPage = currentPages[currentPages.length - 1] || {};
		var route = currentPage.route || currentPage.__route__ || "";
		if (utils.isBlank(route)) {
			return "";
		}
		var normalizedRoute = this.normalizePageUrl(route);
		if (normalizedRoute === "/pages/login") {
			return "";
		}
		var queryString = this.buildQueryString(currentPage.options || {});
		return normalizedRoute + queryString;
	},
	goLoginWithRedirect(redirectUrl) {
		var targetUrl = redirectUrl || this.getCurrentPageRedirectUrl();
		var loginUrl = "/pages/login";
		if (!utils.isBlank(targetUrl)) {
			loginUrl += "?redirect=" + encodeURIComponent(this.normalizePageUrl(targetUrl));
		}
		this.clearLoginStorage();
		// 登录失效先清除共享菜单，防止旧身份响应继续提交。
		uni.$emit("onLogout");
		uni.reLaunch({
			url: loginUrl
		});
	},
	navigateAfterLogin(redirectUrl) {
		var normalizedUrl = this.normalizePageUrl(redirectUrl);
		if (utils.isBlank(normalizedUrl) || normalizedUrl === "/pages/login") {
			uni.switchTab({
				url: "/pages/main/index"
			});
			return;
		}
		if (this.isTabBarPage(normalizedUrl)) {
			uni.switchTab({
				url: normalizedUrl
			});
			return;
		}
		uni.switchTab({
			url: "/pages/main/index",
			success: () => {
				setTimeout(() => {
					uni.navigateTo({
						url: normalizedUrl
					});
				}, 100);
			}
		});
	},
	importSuccessAction(res, that) {
		if (res.code == 200) {
			uni.showModal({
				showCancel: false,
				title: "导入成功",
				content: res.data,
				success(modal) {
					if (modal.confirm) {
						that.refreshData();
					}
				}
			})
		} else {
			uni.showModal({
				showCancel: false,
				content: res.msg
			})
		}
	},
	chooseImportFile(onSelect) {
		uni.chooseFile({
			count: 1, //默认100
			extension: ['.xls', '.xlsx', '.csv'],
			success: function (res) {
				this.filePath = res.tempFiles[0].path
				this.fileName = res.tempFiles[0].name.split('.')[0]
				// 获取文件流
				const fileReader = new FileReader();
				fileReader.onload = ev => {
					try {
						const data = ev.target.result;

						// const workbook = XLSX.read(data, {
						// 	type: "binary",
						// 	cellText: false,
						// 	cellDates: true,
						// });
						const workbook = XLSX.read(data, {
							type: "binary",
							// UTC: false,
							cellDates: true
							// raw: false,
							// dateNF: "yyyy-mm-dd"
						});

						// 获取第一个工作表
						const firstSheetName = workbook.SheetNames[0];
						const worksheet = workbook.Sheets[firstSheetName];

						// 转换为 JSON
						var jsonData = XLSX.utils.sheet_to_json(worksheet, {
							// raw: false,
							// dateNF: "yyyy-mm-dd"
						});
						onSelect(jsonData)
					} catch (e) {
						console.error("上传失败", e)
					}
				}
				const blob = new Blob([res.tempFiles[0]]);
				fileReader.readAsBinaryString(blob);
			}
		});
	},
	back: function () {
		var currentPages = getCurrentPages();
		if (currentPages.length == 1) {
			uni.reLaunch({
				url: "/pages/main/index"
			})
		} else {
			uni.navigateBack();
		}
	},
	showDetailPageSuccessToast: function (action) {
		var title = '保存成功';
		if (action == 'delete') {
			title = '删除成功';
		} else if (action == 'copy') {
			title = '复制成功';
		}
		//延迟100毫秒显示，否则会随着详情页被关掉
		setTimeout(function () {
			uni.showToast({
				title: title,
				icon: 'success'
			})
		}, 100);
	},
	requestSuccessHandler: function (res) {
		if (res.code == 200) {
			this.showSuccessToast();
		} else {
			this.showErrorModal(res.msg);
		}
	},
	showSuccessToast: function () {
		uni.showToast({
			title: '执行成功',
			icon: 'success'
		})
	},
	showErrorModal: function (msg) {
		uni.showModal({
			title: "错误提示",
			showCancel: false,
			content: msg
		})
	},
	//全局自定义按钮事件
	customButtonHandler: function (res, that, isAutoCloseAfterAction, type) {
		if (res.code == 200) {
			if (res.data && res.data.type === 'metarillGenerate') {
				// 生成包只从当前 API 认证下载，下载完成前不显示生成成功。
				const context = () => JSON.stringify([uni.getStorageSync('loginStateId'), uni.getStorageSync('sysParams')]);
				const original = context();
				try {
					const request = generationDownloadRequest(res.data, getApp().globalData.environment.base_url, uni.getStorageSync('loginStateId'), uni.getStorageSync('sysParams'));
					downloadGeneration(request, () => context() === original).then(() => uni.showToast({ title: '生成包已开始下载', icon: 'none' })).catch(error => this.showErrorModal(error.message));
				} catch (error) { this.showErrorModal(error.message); }
				return;
			} else if (res.data && res.data.type && res.data.type == "path") {
				if (res.data.path.indexOf("?") >= 0) {
					uni.navigateTo({
						url: res.data.path
					})
				} else {
					uni.navigateTo({
						url: res.data.path + "?id=" + that.id
					})
				}
			} else if (res.data && res.data.type && res.data.type == "url") {
				utils.openUrl(res.data.url);
			} else {
				if (type && type == "list") {
					that.getList();
				} else {
					var isFunctionTableDetail = that && that.page_is_function_table;
					// 详情页按钮默认不关闭，刷新当前详情页和子表；配置“执行后自动关闭”时才关闭当前页
					if (isAutoCloseAfterAction) {
						if (typeof that.emitDetailChange === 'function') {
							that.emitDetailChange('save');
						}
						if (typeof that.closeDetail === 'function') {
							that.closeDetail();
						} else {
							uni.navigateBack();
						}
					} else if (isFunctionTableDetail) {
						// 功能表没有物理实体，按钮成功后不能调用标准详情刷新。
					} else {
						if (typeof that.getEntity === 'function') {
							that.getEntity();
						}
						if (typeof that.refreshSonsData === 'function') {
							that.refreshSonsData();
						}
					}

					var dataType = Object.prototype.toString.call(res.data);
					if (dataType == "[object Object]") {
						that.entity = res.data;
					}
				}

				setTimeout(() => {
					this.showSuccessToast();
				}, 500);
			}
		} else {
			setTimeout(() => {
				uni.hideToast();
			}, 500);
			this.showErrorModal(res.msg);
		}
	},
	evaluateButtonVisibleRule(ruleJson, entity) {
		return evaluateButtonVisibleRuleCore(ruleJson, entity || {});
	},
	evaluateColumnVisibleRule(ruleJson, entity) {
		return evaluateButtonVisibleRuleCore(ruleJson, entity || {});
	},
	normalizeCurrentPermissions(permissions) {
		// 统一权限快照结构，缺失字段一律按空权限处理。
		const currentPermissions = permissions || {};
		return {
			isSuper: currentPermissions.isSuper === true,
			pagePermissions: currentPermissions.pagePermissions || {},
			buttonPermissions: Array.isArray(currentPermissions.buttonPermissions) ? currentPermissions.buttonPermissions : []
		};
	},
	saveCurrentPermissions(permissions) {
		// 权限快照只用于前端按钮隐藏，真实权限仍以后端拦截器为准。
		const normalizedPermissions = this.normalizeCurrentPermissions(permissions);
		uni.setStorageSync(CURRENT_PERMISSIONS_KEY, normalizedPermissions);
		const nextVersion = Number(uni.getStorageSync(CURRENT_PERMISSIONS_VERSION_KEY) || 0) + 1;
		uni.setStorageSync(CURRENT_PERMISSIONS_VERSION_KEY, nextVersion);
		return normalizedPermissions;
	},
	getCurrentPermissions() {
		return this.normalizeCurrentPermissions(uni.getStorageSync(CURRENT_PERMISSIONS_KEY));
	},
	refreshCurrentPermissions(success, fail) {
		// 刷新失败时清空快照，避免接口异常时放开需授权按钮。
		var that = this;
		// 页面自身也会刷新权限；账号或项目已切换时不得覆盖新菜单提交的权限快照。
		const readContext = () => JSON.stringify([uni.getStorageSync('loginStateId'), uni.getStorageSync('userInfo'), uni.getStorageSync('sysParams')]);
		const context = readContext();
		this.request({
			url: '/admin/sys/yz_manager/getCurrentPermissions',
			sendSessionId: true,
			isCurrent: () => context === readContext(),
			success: function(res) {
				if (res.code == 200) {
					const permissions = that.saveCurrentPermissions(res.data);
					if (success) {
						success(permissions);
					}
					return;
				}
				that.saveCurrentPermissions(null);
				if (fail) {
					fail(res);
				}
			},
			fail: function(res) {
				that.saveCurrentPermissions(null);
				if (fail) {
					fail(res);
				}
			}
		});
	},
	hasPagePermission(pageId, permissionValue) {
		// 标准页面权限允许 is_all 覆盖新增、修改、删除、查询和导出。
		const permissions = this.getCurrentPermissions();
		if (permissions.isSuper) {
			return true;
		}
		if (utils.isBlank(pageId) || utils.isBlank(permissionValue)) {
			return false;
		}
		const pagePermissions = permissions.pagePermissions || {};
		const values = pagePermissions[String(pageId)] || [];
		if (!Array.isArray(values)) {
			return false;
		}
		if (permissionValue === 'is_all') {
			return values.indexOf('is_all') >= 0;
		}
		return values.indexOf('is_all') >= 0 || values.indexOf(permissionValue) >= 0;
	},
	hasButtonPermission(buttonId) {
		// 自定义按钮只认具体 config_button.id，普通角色的 is_all 不自动覆盖。
		const permissions = this.getCurrentPermissions();
		if (permissions.isSuper) {
			return true;
		}
		if (utils.isBlank(buttonId)) {
			return false;
		}
		return (permissions.buttonPermissions || []).indexOf(String(buttonId)) >= 0;
	},
	isButtonVisible(config, entity, readOnlyCondition) {
		const currentConfig = config || {};
		// 先校验按钮授权，再执行原有显示方案和动态显示规则。
		if (!utils.isBlank(currentConfig.buttonId) && !this.hasButtonPermission(currentConfig.buttonId)) {
			return false;
		}
		const visibleScheme = currentConfig.visibleScheme || 'Normal';
		if (visibleScheme === 'Hidden') {
			return false;
		}
		if (visibleScheme === 'OnlyDeve') {
			return false;
		}
		if (visibleScheme === 'VisibleWhenWritable' && readOnlyCondition) {
			return false;
		}
		if (visibleScheme === 'Normal') {
			if (readOnlyCondition || utils.isBlank(entity && entity.id)) {
				return false;
			}
		}
		return evaluateButtonVisibleRuleCore(currentConfig.visibleRuleJson, entity || {});
	},
	isColumnVisible(config, entity) {
		const currentConfig = config || {};
		return evaluateButtonVisibleRuleCore(currentConfig.visibleCondition, entity || {});
	},
	showUploadSuccess: function () {
		uni.hideLoading();
		uni.showToast({
			title: "上传成功",
			icon: "success"
		})
	},
	showUploadToast: function () {
		uni.showLoading({
			title: "上传中",
			mask: true
		});
	},
	// 把uni-file-picker组件的数据格式转换为数据库存储时的格式
	imgComponentToDBFormat: function (imgAry) {
		var result = [];
		for (let i = 0; i < imgAry.length; i++) {
			var obj = {};
			obj.n = imgAry[i].name;
			obj.e = imgAry[i].extname;
			obj.p = imgAry[i].relative_path;
			result.push(obj);
		}
		return result;
	},
	/**
	 * 兼容 uni-file-picker、H5 原生 File 和历史字符串路径，统一转成上传对象。
	 */
	normalizeUploadFileInput: function (fileOrPath) {
		if (typeof fileOrPath === "string") {
			return {
				path: fileOrPath,
				name: fileOrPath.substring(fileOrPath.lastIndexOf("/") + 1),
				size: 0,
				mimeType: ""
			};
		}
		var file = fileOrPath || {};
		var rawFile = file.rawFile || file.file || null;
		var path = file.path || file.tempFilePath || file.tempFile || "";
		var objectUrl = "";
		if (!path && rawFile && typeof URL !== "undefined" && typeof URL.createObjectURL === "function") {
			objectUrl = URL.createObjectURL(rawFile);
			path = objectUrl;
		}
		return {
			path: path,
			name: file.name || (rawFile && rawFile.name) || path.substring(path.lastIndexOf("/") + 1) || "未命名文件",
			size: file.size || (rawFile && rawFile.size) || 0,
			mimeType: file.mimeType || file.type || (rawFile && rawFile.type) || "",
			rawFile: rawFile,
			nativeObjectUrl: file.nativeObjectUrl || objectUrl,
			isCroppedUploadFile: file.isCroppedUploadFile === true,
			shouldRevokeUploadObjectUrl: file.shouldRevokeUploadObjectUrl === true || !!objectUrl
		};
	},
	/**
	 * 把 uni-file-picker 的 select 事件整理为顺序上传需要的文件对象。
	 */
	normalizeSelectedUploadFiles: function (e) {
		if (e && Array.isArray(e.tempFiles) && e.tempFiles.length) {
			return e.tempFiles.map((file) => this.normalizeUploadFileInput(file));
		}
		return ((e && e.tempFilePaths) || []).map((path) => this.normalizeUploadFileInput(path));
	},
	/**
	 * 回收裁切过程生成的临时对象 URL，避免 H5 多图上传后占用内存。
	 */
	revokeCroppedUploadFile: function (file) {
		if (!file || (!file.isCroppedUploadFile && !file.shouldRevokeUploadObjectUrl) || !file.nativeObjectUrl) {
			return;
		}
		if (typeof URL !== "undefined" && typeof URL.revokeObjectURL === "function") {
			URL.revokeObjectURL(file.nativeObjectUrl);
		}
	},
	/**
	 * 标准图片/附件控件的批量上传入口：默认原图直传，显式开启时才逐个裁切。
	 */
	uploadSelectedFiles: async function (e, options, onComplete) {
		if (typeof options === "function") {
			onComplete = options;
			options = {};
		}
		var uploadOptions = options || {};
		var selectedFiles = this.normalizeSelectedUploadFiles(e);
		if (!selectedFiles.length) {
			return [];
		}
		var preparedFiles = [];
		var uploadedResults = [];
		try {
			for (let i = 0; i < selectedFiles.length; i++) {
				var preparedFile = uploadOptions.cropImages === true ? await cropImageFile(selectedFiles[i], uploadOptions) : selectedFiles[i];
				if (preparedFile) {
					preparedFiles.push(preparedFile);
				}
			}
			if (!preparedFiles.length) {
				return [];
			}
			this.showUploadToast();
			for (let i = 0; i < preparedFiles.length; i++) {
				var res = await this.uploadFile(preparedFiles[i], null, Object.assign({}, uploadOptions, {
					cropImages: false,
					showLoading: false,
					throwOnError: true
				}));
				if (res && res.data) {
					uploadedResults.push(res.data);
				}
			}
			if (uploadedResults.length) {
				this.showUploadSuccess();
			} else {
				uni.hideLoading();
			}
			if (onComplete) {
				onComplete(uploadedResults);
			}
			return uploadedResults;
		} catch (e) {
			uni.hideLoading();
			uni.showModal({
				title: "上传失败",
				content: e.message || "上传失败",
				showCancel: false
			});
			return [];
		} finally {
			preparedFiles.forEach((file) => this.revokeCroppedUploadFile(file));
		}
	},
	uploadFile: async function (fileOrPath, success, options) {
		var that = this;
		var uploadOptions = options || {};
		var uploadFile = this.normalizeUploadFileInput(fileOrPath);
		var preparedFile = uploadFile;

		try {
			if (uploadOptions.cropImages === true) {
				preparedFile = await cropImageFile(uploadFile, uploadOptions);
				if (!preparedFile) {
					return null;
				}
			}
		} catch (e) {
			if (uploadOptions.throwOnError) {
				throw e;
			}
			uni.showModal({
				title: "裁切失败",
				content: e.message || "图片裁切失败",
				showCancel: false
			});
			return null;
		}

		var header = {};
		if (uni.getStorageSync("loginStateId")) {
			header.loginStateId = uni.getStorageSync("loginStateId");
		} else {
			that.relogin(that.getCurrentPageRedirectUrl());
			return null;
		}

		// 旧上传沿用普通请求的设备和项目选择，服务端仍须核对真实授权。
		if (uni.getStorageSync("sysParams")) {
			header.sysParams = encodeURIComponent(JSON.stringify(uni.getStorageSync("sysParams")));
		}

		if (uploadOptions.showLoading !== false) {
			uni.showLoading({
				title: '加载中',
				mask: true
			});
		}

		return new Promise(function (resolve, reject) {
			var requestOptions = {
				url: getApp().globalData.environment.base_url + '/admin/sys/upload/uploadImage',
				name: 'file',
				header: header,
				formData: {
					modelName: '',
					column: '',
					file: preparedFile.path
				},
				success: (uploadFileRes) => {
					try {
						// 只在后端业务成功时触发页面成功回调，避免 code=500 仍提示上传成功。
						if (typeof (uploadFileRes.data) == "string") {
							uploadFileRes.data = JSON.parse(uploadFileRes.data);
						}
						if (uploadFileRes.data.code != 200) {
							throw new Error(uploadFileRes.data.msg || "上传失败");
						}
						if (success) {
							success(uploadFileRes.data);
						}
						resolve(uploadFileRes.data);
					} catch (e) {
						if (uploadOptions.throwOnError) {
							reject(e);
							return;
						}
						uni.showModal({
							title: "上传失败",
							content: e.message || "上传失败",
							showCancel: false
						});
						resolve(null);
					}
				},
				fail: (uploadFileRes) => {
					var error = new Error((uploadFileRes && uploadFileRes.errMsg) || "上传失败");
					if (uploadOptions.throwOnError) {
						reject(error);
						return;
					}
					uni.showToast({
						icon: "error",
						title: "上传失败"
					});
					resolve(null);
				},
				complete: () => {
					if (uploadOptions.showLoading !== false) {
						uni.hideLoading();
					}
					that.revokeCroppedUploadFile(preparedFile);
				}
			};
			// H5 原生 File 和裁切后的 File 走 file；小程序/App 继续走 filePath。
			if (preparedFile.rawFile && (preparedFile.nativeObjectUrl || (typeof File !== "undefined" && preparedFile.rawFile instanceof File))) {
				requestOptions.file = preparedFile.rawFile;
			}
			if (preparedFile.path) {
				requestOptions.filePath = preparedFile.path;
			}
			uni.uploadFile(requestOptions);
		});
	},
	exitLogin: function () {
		var that = this;
		uni.showModal({
			content: "您确认要退出登录吗？",
			success: function (res) {
				if (res.confirm) {
					that.clearLoginStorage();
					// 主动登出也先清除共享菜单，防止旧身份响应继续提交。
					uni.$emit("onLogout");
					// uni.removeStorageSync("sessionId");
					// uni.removeStorageSync("loginStateId");
					// uni.removeStorageSync("user_info");
					uni.reLaunch({
						url: "/pages/login"
					})
				}
			}
		})
	},
	checkLogin() {
		if (uni.getStorageSync("loginStateId")) {
			return true;
		} else {
			return false;
		}
	},
	relogin(redirectUrl) {
		uni.showModal({
			showCancel: false,
			content: "尚未登录或已过期，请重新登录",
			success: () => {
				this.goLoginWithRedirect(redirectUrl);
			}
		})
	},
	/** 返回加载框诊断快照，调用方无法修改内部请求状态。 */
	getRequestLoadingState() {
		return {
			visible: requestLoadingVisible,
			pending: Array.from(pendingRequests.values()).map(item => ({
				...item, elapsed: Date.now() - item.startedAt
			})),
			events: requestLoadingEvents.map(item => ({ ...item }))
		};
	},
	/** 统一请求入口：保留原有业务回调，并保证每个请求只收尾一次。 */
	request(obj) {
		const url = getApp().globalData.environment.base_url + obj.url;
		const path = String(obj.url).split(/[?#]/)[0];
		const header = Object.assign({}, obj.header || {});
		const timeout = Number.isFinite(Number(obj.timeout)) && Number(obj.timeout) > 0 ? Number(obj.timeout) : 60000;
		if (uni.getStorageSync("sysParams")) {
			header.sysParams = encodeURIComponent(JSON.stringify(uni.getStorageSync("sysParams")));
		}
		if (obj.sendSessionId) {
			if (uni.getStorageSync("loginStateId")) {
				header.loginStateId = uni.getStorageSync("loginStateId");
			} else if (!obj.sendSessionIdFalseNotReturn) {
				this.relogin(this.getCurrentPageRedirectUrl());
				return;
			}
		}

		const loading = beginRequestLoading(path, obj.showLoading !== false);
		let settled = false;
		let requestTask;
		let watchdog;
		/** 隔离业务回调异常，确保 complete 和加载凭据释放仍会执行。 */
		const callCallback = (callback, value) => {
			if (typeof callback !== 'function') return;
			try {
				callback.call(obj, value);
			} catch (error) {
				recordRequestEvent(loading.id, path, 'callback-error');
				console.error('[YZP请求] 回调执行失败：' + path, error);
			}
		};
		/** 可选的时效守卫在全局提示之前执行；未接入的旧调用保持原有行为。 */
		const isCurrent = () => {
			if (typeof obj.isCurrent !== 'function') return true;
			try {
				return obj.isCurrent.call(obj) !== false;
			} catch (error) {
				// 守卫无法确定时效时不提交副作用，但仍完成请求的资源释放。
				recordRequestEvent(loading.id, path, 'guard-error');
				console.error('[YZP请求] 时效检查失败：' + path, error);
				return false;
			}
		};
		/** 成功、失败、同步异常和超时统一收尾，忽略取消后的迟到响应。 */
		const settle = (phase, res = {}) => {
			if (settled) return;
			res = res || {};
			settled = true;
			clearTimeout(watchdog);
			try {
				if (!isCurrent()) {
					recordRequestEvent(loading.id, path, 'stale');
					return;
				}
				if (phase === 'success') {
					const data = res.data;
					if (data == null) {
						if (obj.showError !== false) uni.showModal({ title: '请求失败', content: '接口返回内容为空', showCancel: false });
					} else if (data.status == 500) {
						if (obj.showError !== false) uni.showModal({ title: '请求失败', content: data.message || '服务器处理失败', showCancel: false });
					} else if (data.code == 500 && data.msg === '登录超时') {
						this.relogin(this.getCurrentPageRedirectUrl());
					} else {
						callCallback(obj.success, data);
					}
				} else {
					// 网络错误通常只有 errMsg，不能再取 res.data 丢掉错误信息。
					if (typeof obj.fail === 'function') {
						callCallback(obj.fail, res);
					} else {
						const message = /timeout/.test(res.errMsg || '') ? '请求超时，请稍后重试' : '网络请求失败，请稍后重试';
						if (obj.showError !== false) uni.showModal({ title: '请求失败', content: message, showCancel: false });
					}
				}
			} finally {
				// 旧响应也释放凭据并通知 complete；调用方自行按请求序号决定是否收尾界面。
				// 先运行业务回调，再释放凭据，串联的新请求会继续持有加载框。
				loading.finish(phase);
				callCallback(obj.complete, res.data === undefined ? res : res.data);
			}
		};
		/** 先主动收尾再调用 SDK，取消无回调的平台也不会留下加载凭据或超时任务。 */
		const abortRequest = () => {
			if (settled) return;
			settle('abort', { errMsg: 'request:fail abort' });
			if (requestTask && typeof requestTask.abort === 'function') {
				return requestTask.abort();
			}
		};

		// SDK 超时之外再做兜底，防止异常平台路径没有触发任何完成回调。
		watchdog = setTimeout(() => {
			try {
				settle('timeout', { errMsg: 'request:fail timeout' });
			} finally {
				if (requestTask && typeof requestTask.abort === 'function') {
					requestTask.abort();
				}
			}
		}, timeout + 1000);
		try {
			requestTask = uni.request({
				url,
				// 生成清单裁剪标准写入 DTO；客户端适配不替代服务端白名单与权限校验。
				data: filterPageWriteData(path, obj.method || 'GET', utils.isBlank(obj.data) ? {} : obj.data),
				dataType: obj.dataType || 'json',
				header,
				method: obj.method || 'GET',
				timeout,
				success: res => settle('success', res),
				fail: res => settle('fail', res),
				complete: res => {
					// 常规路径已收尾；仍兼容平台仅触发 complete 的异常路径。
					if (!settled) settle('fail', res);
				}
			});
			// 包装保留任务成员的读取与调用，原生方法仍以原任务为 this，避免破坏 SDK 内部状态。
			return new Proxy({}, {
				get(target, key) {
					if (key === 'abort') return abortRequest;
					const value = requestTask && requestTask[key];
					return typeof value === 'function' ? value.bind(requestTask) : value;
				},
				set(target, key, value) {
					if (!requestTask) return false;
					requestTask[key] = value;
					return true;
				}
			});
		} catch (error) {
			settle('start-error', { errMsg: 'request:fail ' + (error.message || String(error)) });
		}
	}
};

export default yzp;
