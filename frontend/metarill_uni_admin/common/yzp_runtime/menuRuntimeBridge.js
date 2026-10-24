import yzp from './yzp-runtime'
import { createMenuRuntime } from './menuRuntime'

let bridge

/** 普通页面保留查询参数；SDK 确认目标属于 tabBar 时改用 switchTab。 */
export function navigateMenu(options) {
	return uni.navigateTo({ ...options,
		fail(error) {
			if (/tabbar/i.test(error && error.errMsg || '')) {
				const query = options.url.split('?')[1] || ''
				const businessParams = query.split('&').filter(Boolean).some(part => !['yzpMenuId', 'isShowCount'].includes(part.split('=')[0]))
				if (businessParams || options.url.includes('#')) {
					const failure = { errMsg: '底部导航页面不支持入口参数，请调整菜单目标配置' }
					if (options.fail) options.fail(failure)
					if (options.complete) options.complete(failure)
					return
				}
				// uni 的 tabBar 不接收查询参数，选中仍由运行时的入口身份及实际路由恢复。
				const path = options.url.split(/[?#]/)[0]
				uni.switchTab({ ...options, url: path,
					success: result => options.success && options.success({ ...result, actualRoute: path }) })
			} else {
				if (options.fail) options.fail(error)
				if (options.complete) options.complete(error)
			}
		},
		success(result) {
			if (options.success) options.success(result)
			if (options.complete) options.complete(result)
		},
		complete() { /* 中间 navigateTo 失败不提前结束 switchTab 的导航状态。 */ }
	})
}

/** 读取真实页面栈，兼容 H5 刷新、返回和小程序页面参数。 */
export function currentMenuRoute() {
	const pages = typeof getCurrentPages === 'function' ? getCurrentPages() : []
	const page = pages[pages.length - 1]
	if (page) {
		const info = page.$page || {}
		return { fullPath: info.fullPath || info.route || page.route || '', options: page.options || info.options || {} }
	}
	// #ifdef H5
	if (typeof location !== 'undefined') return location.hash.replace(/^#/, '') || location.pathname + location.search
	// #endif
	return ''
}

/** 单例桥接只管理公共导航；客户端上下文用于隔离请求，授权仍由服务端判断。 */
export function getMenuRuntime() {
	if (bridge) return bridge
	bridge = createMenuRuntime({
		request: options => yzp.request(options),
		isAuthenticated: () => yzp.checkLogin(),
		getContext: () => ({ loginStateId: uni.getStorageSync('loginStateId'),
			user: uni.getStorageSync('userInfo') || {}, params: uni.getStorageSync('sysParams') || {} }),
		getCurrentRoute: currentMenuRoute,
		commitPermissions: permissions => yzp.saveCurrentPermissions(permissions),
		navigate: navigateMenu
	})
	return bridge
}

/** 应用启动时注册一次；页面或左右窗口反复挂载不会重复监听和请求。 */
export function installMenuRuntime(registerMixin) {
	if (installMenuRuntime.installed) return
	installMenuRuntime.installed = true
	const runtime = getMenuRuntime()
	const reload = () => runtime.load({ force: true })
	const syncRoute = () => runtime.syncRoute(currentMenuRoute())
	uni.$on('onLoginLoadMenu', reload)
	uni.$on('onChangeProject', reload)
	uni.$on('onLogout', () => runtime.clear())
	uni.$on('onMenuConfigurationChanged', change => {
		// 平台可编辑其他项目，只有当前导航所属后台改变才重载当前菜单。
		const state = runtime.getState()
		if (change && (!state.menus.length || state.menus.some(menu => String(menu.projectId) === String(change.projectId)
			&& String(menu.adminTypeId) === String(change.adminTypeId)))) reload()
	})
	uni.$on('yzpMenuChanged', change => runtime.invalidateCounts(change))
	// 旧 V8 生成页尚未再生成时仍可刷新角标，新生成页只发送带资源身份的写入通知。
	uni.$on('updateMenuCount', change => runtime.invalidateCounts(change))
	registerMixin({ onShow() { runtime.load(); syncRoute() } })
	// #ifdef H5
	if (typeof window !== 'undefined') {
		window.addEventListener('popstate', () => setTimeout(syncRoute, 0))
		window.addEventListener('hashchange', () => setTimeout(syncRoute, 0))
	}
	// #endif
}
