/** 菜单入口标识只用于恢复导航状态，不能作为页面或操作授权依据。 */
export const MENU_ID_QUERY = 'yzpMenuId'

/** 统一识别后端布尔值与 URL 参数，避免字符串 false 被当作真值。 */
export function menuBoolean(value) {
	return value === true || value === 1 || value === '1' || value === 'true'
}

/** 上下文按键排序序列化，调用方每次创建同值对象也不会误判为切换身份。 */
function stable(value) {
	if (value === undefined) return null
	if (value === null || typeof value !== 'object') return value
	if (Array.isArray(value)) return value.map(stable)
	return Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])]))
}

/** 路由参数容忍历史编码错误，但不把外部地址当作内部页面导航。 */
function decode(value) {
	try { return decodeURIComponent(value.replace(/\+/g, ' ')) } catch (_) { return value }
}

/** 兼容 uni 页面对象和 H5 路由对象，查询参数与路径分别参与菜单匹配。 */
function parseRoute(value) {
	const object = value && typeof value === 'object' ? value : {}
	const raw = typeof value === 'string' ? value : object.fullPath || object.path || object.route || ''
	const [withoutHash, hash = ''] = String(raw).split('#', 2)
	const mark = withoutHash.indexOf('?')
	let path = mark < 0 ? withoutHash : withoutHash.slice(0, mark)
	if (path.startsWith('pages/')) path = '/' + path
	const query = {}
	if (mark >= 0) withoutHash.slice(mark + 1).split('&').filter(Boolean).forEach(part => {
		const separator = part.indexOf('=')
		const key = decode(separator < 0 ? part : part.slice(0, separator))
		query[key] = decode(separator < 0 ? '' : part.slice(separator + 1))
	})
	Object.entries(object.options || object.query || {}).forEach(([key, item]) => {
		if (item !== undefined && item !== null) query[key] = String(item)
	})
	return { path, query, hash }
}

/** 只接收平台内部页面，避免畸形配置经客户端跳转到其他来源。 */
function internalRoute(route) {
	return route.path.startsWith('/pages/') && !/[\s\\:]/.test(route.path)
		&& !route.path.includes('..') && !route.path.includes('//')
}

/** 合并入口参数时保留原有查询与片段；重复的入口标识只保留当前菜单。 */
function routeUrl(route) {
	const query = Object.entries(route.query).map(([key, value]) => encodeURIComponent(key) + '=' + encodeURIComponent(value)).join('&')
	return route.path + (query ? '?' + query : '') + (route.hash ? '#' + route.hash : '')
}

/** 显式目录和带子节点的兼容数据始终作为目录，不能同时触发页面跳转。 */
function pageNode(node) {
	return node.nodeType !== 'DIRECTORY' && !(Array.isArray(node.children) && node.children.length)
}

/** 创建 V8 公共菜单状态机；宿主只需提供请求、身份上下文与导航适配。 */
export function createMenuRuntime({ request, getContext = () => '', isAuthenticated = () => true,
	getCurrentRoute = () => '', navigate, onChange, commitPermissions = () => {},
	menuEndpoint = '/admin/sys/yz_manager/getMenu', countConcurrency = 3, countDebounceMs = 80,
	schedule = setTimeout, cancelScheduled = clearTimeout } = {}) {
	if (typeof request !== 'function') throw new TypeError('菜单运行时需要请求适配器')
	const listeners = new Set(typeof onChange === 'function' ? [onChange] : [])
	const concurrency = Math.max(1, Math.min(8, Math.floor(Number(countConcurrency) || 3)))
	let contextKey, generation = 0, menuPending = null, navigationPending = null, disposed = false
	let baseMenus = [], nodes = new Map(), groups = new Map(), countTimer = null, pumping = false
	let currentRoute = '', lastMenuId = ''
	let state = { menus: [], permissions: null, status: 'idle', error: '', activeMenuId: '',
		activeUrl: '', expandedMenuIds: [], navigationError: '', navigating: false }

	/** 回调执行前重新读取真实身份，覆盖宿主切换事件尚未分发的时间窗口。 */
	function readContext() {
		try { return isAuthenticated() ? JSON.stringify(stable(getContext())) : null } catch (_) { return null }
	}

	/** 每次发布递归创建展示副本，不让 Vue 响应式对象持有内部请求状态。 */
	function displayMenus(menus) {
		return menus.map(node => {
			const group = node.target && groups.get(node.target.countEndpoint)
			return { ...node, target: node.target ? { ...node.target } : undefined,
				...(group && menuBoolean(node.isShowCount) ? { count: group.count, countLoading: !!group.active || group.dirty, countError: group.error } : {}),
				...(Array.isArray(node.children) ? { children: displayMenus(node.children) } : {}) }
		})
	}

	/** 菜单和权限先组成完整快照，再一次通知所有视图。 */
	function publish(patch = {}) {
		state = { ...state, ...patch, menus: displayMenus(baseMenus) }
		if (!disposed) listeners.forEach(listener => listener(state))
	}

	/** 取消是否成功不影响逻辑失效，兼容同步触发 fail/complete 的底层请求。 */
	function abort(pending) {
		if (pending && pending.task && typeof pending.task.abort === 'function') {
			try { pending.task.abort() } catch (_) { /* 代次检查负责隔离迟到回调。 */ }
		}
	}

	/** 切换上下文或重载时先作废旧任务，再取消网络与角标调度。 */
	function invalidateTasks() {
		generation++
		const previous = menuPending
		menuPending = null
		navigationPending = null
		const counts = Array.from(groups.values()).map(group => group.active)
		groups = new Map()
		if (countTimer !== null) cancelScheduled(countTimer)
		countTimer = null
		abort(previous)
		counts.forEach(abort)
	}

	/** 身份变化立即清空旧菜单与权限，避免等待新请求期间仍可见旧上下文入口。 */
	function reset(nextContext = readContext()) {
		invalidateTasks()
		contextKey = nextContext
		baseMenus = []
		nodes = new Map()
		lastMenuId = ''
		currentRoute = ''
		commitPermissions(null)
		publish({ permissions: null, status: 'idle', error: '', activeMenuId: '', activeUrl: '',
			expandedMenuIds: [], navigationError: '', navigating: false })
	}

	/** 所有公开入口都复核身份；宿主忘记发送事件时也不会继续使用旧状态。 */
	function ensureContext() {
		if (disposed) return false
		const next = readContext()
		if (contextKey !== next) reset(next)
		return next !== null
	}

	/** 菜单和角标请求共同遵守上下文与代次边界。 */
	function current(pending) {
		return !disposed && pending.generation === generation && contextKey !== null && contextKey === readContext()
	}

	/** 校验并复制后端结构，保留历史节点字段，不从 value 推测页面或计数接口。 */
	function indexMenus(menus) {
		const indexed = new Map()
		const visit = (items, ancestors = []) => items.map(source => {
			if (!source || typeof source !== 'object') throw new Error('菜单节点格式不正确，请重试')
			const id = String(source.menu_id || source.id || '')
			if (!id || indexed.has(id)) throw new Error('菜单标识缺失或重复，请检查配置')
			const node = { ...source, menu_id: id, target: source.target ? { ...source.target } : undefined }
			indexed.set(id, { node, ancestors })
			if (source.children !== undefined) {
				if (!Array.isArray(source.children)) throw new Error('菜单子节点格式不正确，请重试')
				node.children = visit(source.children, ancestors.concat(id))
			}
			return node
		})
		return { menus: visit(menus), indexed }
	}

	/** 同一计数接口只请求一次，返回数量同时供多个菜单入口使用。 */
	function buildCounts() {
		nodes.forEach(({ node }) => {
			const endpoint = node.target && node.target.countEndpoint
			if (!pageNode(node) || !menuBoolean(node.isShowCount) || typeof endpoint !== 'string' || !/^\/(?!\/)/.test(endpoint)) return
			if (!groups.has(endpoint)) groups.set(endpoint, { endpoint, targets: [], version: 1,
				dirty: true, active: null, count: undefined, error: '' })
			groups.get(endpoint).targets.push(node.target)
		})
	}

	/** 详情没有独立菜单时，使用明确的表身份关联对应入口。 */
	function sameDetailTable(node, route) {
		const match = route.path.match(/^\/pages\/yzp\/(.+)-detail-([^/]+)$/)
		return !!match && pageNode(node) && node.nodeType === 'GENERATED_PAGE' && !!node.target
			&& !!node.target.tableName && node.target.tableName === match[1]
	}

	/** 参数不同的同页面入口独立匹配，细分参数更多的入口优先。 */
	function routeScore(node, route) {
		if (!pageNode(node) || !node.target || !node.target.route) return -1
		const target = parseRoute(node.target.route)
		if (target.path !== route.path) return -1
		const keys = Object.keys(target.query).filter(key => key !== MENU_ID_QUERY && key !== 'isShowCount')
		return keys.every(key => target.query[key] === route.query[key]) ? keys.length : -1
	}

	/** 根据实际页面恢复选中和展开，返回与刷新均走同一条规则。 */
	function alignRoute(value) {
		currentRoute = value === undefined ? getCurrentRoute() : value
		const route = parseRoute(currentRoute)
		const explicit = nodes.get(route.query[MENU_ID_QUERY])
		let selected = explicit && (routeScore(explicit.node, route) >= 0 || sameDetailTable(explicit.node, route)) ? explicit : null
		if (!selected) {
			const candidates = Array.from(nodes.values()).map(entry => ({ entry, score: routeScore(entry.node, route) }))
				.filter(item => item.score >= 0).sort((left, right) => right.score - left.score
					|| Number(right.entry.node.menu_id === lastMenuId) - Number(left.entry.node.menu_id === lastMenuId))
			selected = candidates.length ? candidates[0].entry : null
		}
		if (!selected) {
			const previous = nodes.get(lastMenuId)
			selected = previous && sameDetailTable(previous.node, route) ? previous
				: Array.from(nodes.values()).find(entry => sameDetailTable(entry.node, route))
		}
		if (selected) lastMenuId = selected.node.menu_id
		return { activeMenuId: selected ? selected.node.menu_id : '',
			activeUrl: selected ? selected.node.target.route : '', expandedMenuIds: selected ? selected.ancestors.slice() : [] }
	}

	/** 同上下文失败清空过期权限与菜单，保留可重试状态，不展示陈旧授权入口。 */
	function menuFail(pending, message) {
		if (!current(pending) || menuPending !== pending || pending.handled) return
		pending.handled = true
		baseMenus = []
		nodes = new Map()
		groups = new Map()
		commitPermissions(null)
		publish({ permissions: null, status: 'error', error: message || '菜单加载失败，请重试',
			activeMenuId: '', activeUrl: '', expandedMenuIds: [] })
	}

	/** 缺失业务回调也会结束 loading，避免登录失效或异常响应使菜单永久等待。 */
	function menuComplete(pending) {
		if (!current(pending) || menuPending !== pending) return
		if (!pending.handled) menuFail(pending, '菜单请求未完成，请重试')
		menuPending = null
	}

	/** 页面挂载与 onShow 复用已确认快照；明确刷新才重查菜单并重建角标。 */
	function load({ force = false } = {}) {
		if (!ensureContext()) return
		if (!force && menuPending && current(menuPending)) return menuPending.task
		if (!force && (state.status === 'ready' || state.status === 'empty')) return
		invalidateTasks()
		publish({ status: 'loading', error: '', navigating: false, navigationError: '' })
		const pending = { generation, task: null, handled: false }
		menuPending = pending
		try {
			pending.task = request({ url: menuEndpoint, method: 'GET', sendSessionId: true, showLoading: false, showError: false,
				isCurrent: () => current(pending) && menuPending === pending,
				success(response) {
					if (!current(pending) || menuPending !== pending || pending.handled) return
					if (!response || response.code != 200) return menuFail(pending, response && response.msg)
					if (!Array.isArray(response.menu) || !response.permissions || typeof response.permissions !== 'object' || Array.isArray(response.permissions)) {
						return menuFail(pending, '菜单或权限返回格式不正确，请重试')
					}
					try {
						const parsed = indexMenus(response.menu)
						const permissions = stable(response.permissions)
						commitPermissions(permissions)
						if (!current(pending) || menuPending !== pending) return
						baseMenus = parsed.menus
						nodes = parsed.indexed
						pending.handled = true
						buildCounts()
						publish({ permissions, status: baseMenus.length ? 'ready' : 'empty', error: '', ...alignRoute(getCurrentRoute()) })
						scheduleCounts()
					} catch (error) { menuFail(pending, error.message || '菜单加载失败，请重试') }
				},
				fail(error) { menuFail(pending, error && (error.errMsg || error.message)) },
				complete() { menuComplete(pending) }
			})
			if (!pending.task && menuPending === pending) menuComplete(pending)
			return pending.task
		} catch (error) {
			menuFail(pending, error.message)
			menuComplete(pending)
		}
	}

	/** 单个角标失败只标记该角标，不能清空导航或影响其他正在刷新的资源。 */
	function countFail(group, pending, message) {
		if (!current(pending) || groups.get(group.endpoint) !== group || group.active !== pending || pending.handled) return
		pending.handled = true
		if (pending.version === group.version) group.error = message || '数量加载失败'
	}

	/** 变更发生于计数请求途中时丢弃旧结果，收尾后补一轮最新请求。 */
	function countComplete(group, pending) {
		if (!current(pending) || groups.get(group.endpoint) !== group || group.active !== pending) return
		if (!pending.handled) countFail(group, pending, '数量请求未完成')
		group.active = null
		publish()
		pumpCounts()
	}

	/** 计数始终静默请求；接口由统一目标提供，不解析路由字符串。 */
	function startCount(group) {
		group.dirty = false
		group.error = ''
		const pending = { generation, version: group.version, task: null, handled: false }
		group.active = pending
		try {
			pending.task = request({ url: group.endpoint, data: { filters: {}, parentId: '' }, method: 'POST',
				sendSessionId: true, showLoading: false, showError: false,
				isCurrent: () => current(pending) && groups.get(group.endpoint) === group && group.active === pending && pending.version === group.version,
				success(response) {
					if (!current(pending) || groups.get(group.endpoint) !== group || group.active !== pending || pending.handled) return
					const raw = response && response.data
					const number = typeof raw === 'number' || typeof raw === 'string' && raw.trim() !== '' ? Number(raw) : NaN
					if (!response || response.code != 200 || !Number.isInteger(number) || number < 0) return countFail(group, pending, response && response.msg || '数量返回格式不正确')
					pending.handled = true
					if (pending.version === group.version) { group.count = String(number); group.error = '' }
				},
				fail(error) { countFail(group, pending, error && (error.errMsg || error.message)) },
				complete() { countComplete(group, pending) }
			})
			if (!pending.task && group.active === pending) countComplete(group, pending)
		} catch (error) {
			countFail(group, pending, error.message)
			countComplete(group, pending)
		}
	}

	/** 以接口为单位限制并发，同步返回的测试或缓存适配器不会递归耗尽调用栈。 */
	function pumpCounts() {
		if (pumping || !ensureContext()) return
		pumping = true
		try {
			while (Array.from(groups.values()).filter(group => group.active).length < concurrency) {
				const next = Array.from(groups.values()).find(group => group.dirty && !group.active)
				if (!next) break
				startCount(next)
			}
			publish()
		} finally { pumping = false }
	}

	/** 集中防抖兼容旧的无参数刷新事件，同时合并同一轮多个写入通知。 */
	function scheduleCounts() {
		if (countTimer !== null || !Array.from(groups.values()).some(group => group.dirty)) return
		countTimer = schedule(() => { countTimer = null; pumpCounts() }, Math.max(0, Number(countDebounceMs) || 0))
	}

	/** 只失效相关页面或表，空参数保留旧事件兼容；读取与分页事件明确忽略。 */
	function invalidateCounts(change) {
		if (!ensureContext() || change && ['read', 'load', 'pagination'].includes(change.reason || change.type)) return
		const all = change === undefined || change === null || change.all === true
		groups.forEach(group => {
			if (all || group.targets.some(target => ['pageId', 'tableId', 'tableName'].some(key => change[key] !== undefined && change[key] !== null && String(change[key]) !== '' && String(change[key]) === String(target[key])))) {
				group.version++
				group.dirty = true
			}
		})
		scheduleCounts()
	}

	/** 统一生成页与自定义页导航；成功后才更新高亮，失败保留当前页面的状态。 */
	function navigateTo(menuId) {
		if (!ensureContext()) return
		const entry = nodes.get(String(menuId))
		if (!entry || !pageNode(entry.node) || !entry.node.target || !entry.node.target.route || typeof navigate !== 'function') return
		const route = parseRoute(entry.node.target.route)
		if (!internalRoute(route)) { publish({ navigationError: '菜单目标不是有效的内部页面' }); return }
		route.query[MENU_ID_QUERY] = entry.node.menu_id
		if (entry.node.nodeType === 'GENERATED_PAGE') route.query.isShowCount = String(menuBoolean(entry.node.isShowCount))
		const url = routeUrl(route)
		if (navigationPending && navigationPending.url === url && current(navigationPending)) return navigationPending.task
		const pending = { generation, url, handled: false, task: null }
		navigationPending = pending
		publish({ navigating: true, navigationError: '' })
		const valid = () => current(pending) && navigationPending === pending
		const fail = error => {
			if (!valid() || pending.handled) return
			pending.handled = true
			publish({ navigationError: error && (error.errMsg || error.message) || '页面打开失败，请重试', navigating: false })
		}
		try {
			pending.task = navigate({ url,
				success(result) {
					if (!valid() || pending.handled) return
					pending.handled = true
					// tabBar 不接受查询参数，宿主可以报告实际路由，避免保留未传递的入口参数。
					publish({ ...alignRoute(result && result.actualRoute || url), navigating: false, navigationError: '' })
				},
				fail,
				complete() {
					if (!valid()) return
					if (!pending.handled) fail({ message: '页面打开未完成，请重试' })
					navigationPending = null
				}
			})
			return pending.task
		} catch (error) { fail(error); navigationPending = null }
	}

	return {
		load,
		/** 失败重试总是请求最新上下文。 */
		retry() { return load({ force: true }) },
		/** 主动退出登录或宿主销毁身份时立即清除所有可见状态。 */
		clear() { if (!disposed) reset(readContext()) },
		/** 页面 onShow、浏览器返回和路由刷新共用的入口。 */
		syncRoute(route) { if (ensureContext()) publish(alignRoute(route)) },
		navigateTo,
		invalidateCounts,
		/** 返回最新公共快照，调用方不可修改内部任务和计数队列。 */
		getState() { ensureContext(); return state },
		/** 多个菜单视图共享一份运行时；取消订阅不销毁其他视图使用的实例。 */
		subscribe(listener) {
			if (disposed || typeof listener !== 'function') return () => {}
			ensureContext()
			listeners.add(listener)
			listener(state)
			return () => listeners.delete(listener)
		},
		/** 销毁只取消本实例资源，权限清理必须由明确的退出或上下文变更触发。 */
		dispose() {
			if (disposed) return
			disposed = true
			invalidateTasks()
			listeners.clear()
		}
	}
}
