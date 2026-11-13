export function createH5DocumentTitleSync(options = {}) {
	const fallbackProjectName = String(options.fallbackProjectName || '').trim()
	const h5ProjectName = typeof document !== 'undefined' && String(document.title || '').trim()
		? String(document.title || '').trim()
		: fallbackProjectName
	const h5DynamicTitleMap = Object.create(null)
	let hasPatchedH5NavigationBarTitle = false
	let hasBoundH5HistoryEvents = false

	function normalizeH5Path(value) {
		return String(value || '')
			.replace(/^[a-z]+:\/\/[^/]+/i, '')
			.replace(/^#/, '')
			.replace(/^\//, '')
			.split('?')[0]
			.split('#')[0]
			.replace(/\/+$/, '')
	}

	function normalizeH5QueryString(value) {
		if (!value) {
			return ''
		}
		const queryItems = []
		const searchParams = new URLSearchParams(String(value).replace(/^[?#]/, ''))
		searchParams.forEach((itemValue, itemKey) => {
			if (itemValue === '') {
				return
			}
			queryItems.push([itemKey, itemValue])
		})
		queryItems.sort((a, b) => {
			const keyCompareResult = a[0].localeCompare(b[0])
			if (keyCompareResult !== 0) {
				return keyCompareResult
			}
			return a[1].localeCompare(b[1])
		})
		return queryItems
			.map(([itemKey, itemValue]) => `${encodeURIComponent(itemKey)}=${encodeURIComponent(itemValue)}`)
			.join('&')
	}

	function buildSortedQueryString(query) {
		if (!query || typeof query !== 'object') {
			return ''
		}
		const queryItems = []
		Object.keys(query).forEach((key) => {
			const value = query[key]
			if (value === undefined || value === null || value === '') {
				return
			}
			if (Array.isArray(value)) {
				value.forEach((item) => {
					if (item === undefined || item === null || item === '') {
						return
					}
					queryItems.push([key, String(item)])
				})
				return
			}
			queryItems.push([key, String(value)])
		})
		queryItems.sort((a, b) => {
			const keyCompareResult = a[0].localeCompare(b[0])
			if (keyCompareResult !== 0) {
				return keyCompareResult
			}
			return a[1].localeCompare(b[1])
		})
		const searchParams = new URLSearchParams()
		queryItems.forEach(([key, value]) => {
			searchParams.append(key, value)
		})
		const queryString = searchParams.toString()
		return queryString ? `?${queryString}` : ''
	}

	function normalizeH5RouteKey(value) {
		const normalizedValue = String(value || '')
			.replace(/^[a-z]+:\/\/[^/]+/i, '')
			.replace(/^#/, '')
			.replace(/^\//, '')
			.split('#')[0]
		if (!normalizedValue) {
			return ''
		}
		const queryIndex = normalizedValue.indexOf('?')
		if (queryIndex < 0) {
			return normalizeH5Path(normalizedValue)
		}
		const routePath = normalizeH5Path(normalizedValue.slice(0, queryIndex))
		const normalizedQueryString = normalizeH5QueryString(normalizedValue.slice(queryIndex + 1))
		return normalizedQueryString ? `${routePath}?${normalizedQueryString}` : routePath
	}

	function getCurrentH5RouteState() {
		const pages = typeof getCurrentPages === 'function' ? getCurrentPages() : []
		const currentPage = pages.length ? pages[pages.length - 1] : null
		if (currentPage) {
			const pageInfo = currentPage.$page || {}
			const routePath = normalizeH5Path(
				currentPage.route ||
				pageInfo.route ||
				pageInfo.path ||
				pageInfo.fullPath
			)
			const routeKey = normalizeH5RouteKey(
				pageInfo.fullPath ||
				(routePath ? `${routePath}${buildSortedQueryString(pageInfo.options || currentPage.options)}` : '')
			)
			return {
				routePath,
				routeKey: routeKey || routePath
			}
		}
		if (typeof location !== 'undefined') {
			const fullPath = location.hash && location.hash !== '#'
				? location.hash.slice(1)
				: `${location.pathname || ''}${location.search || ''}`
			return {
				routePath: normalizeH5Path(fullPath),
				routeKey: normalizeH5RouteKey(fullPath)
			}
		}
		return {
			routePath: '',
			routeKey: ''
		}
	}

	function getH5LocationRouteState() {
		if (typeof location === 'undefined') {
			return {
				routePath: '',
				routeKey: ''
			}
		}
		const fullPath = location.hash && location.hash !== '#'
			? location.hash.slice(1)
			: `${location.pathname || ''}${location.search || ''}`
		return {
			routePath: normalizeH5Path(fullPath),
			routeKey: normalizeH5RouteKey(fullPath)
		}
	}

	function getH5RouteCandidatePaths(routeConfig) {
		return [
			routeConfig ? routeConfig.path : '',
			routeConfig ? routeConfig.alias : '',
			routeConfig && routeConfig.meta ? routeConfig.meta.route : ''
		].map((item) => normalizeH5Path(item)).filter(Boolean)
	}

	function getH5StaticPageTitle(routePath) {
		if (typeof __uniRoutes === 'undefined' || !Array.isArray(__uniRoutes)) {
			return ''
		}
		const normalizedRoutePath = normalizeH5Path(routePath)
		const routeConfig = __uniRoutes.find((item) => {
			return getH5RouteCandidatePaths(item).includes(normalizedRoutePath)
		})
		if (!routeConfig || !routeConfig.meta) {
			return ''
		}
		if (routeConfig.meta.navigationBar) {
			return String(routeConfig.meta.navigationBar.titleText || routeConfig.meta.navigationBarTitleText || '').trim()
		}
		return String(routeConfig.meta.navigationBarTitleText || '').trim()
	}

	function buildH5DocumentTitle(pageTitle) {
		const normalizedTitle = String(pageTitle || '').trim()
		return normalizedTitle ? `${normalizedTitle} - ${h5ProjectName}` : h5ProjectName
	}

	function syncH5DocumentTitle(preferredRouteKey, preferredTitle) {
		if (typeof document === 'undefined') {
			return
		}
		const routeState = getCurrentH5RouteState()
		const routeKey = normalizeH5RouteKey(preferredRouteKey) || routeState.routeKey
		const routePath = routeState.routeKey === routeKey ? routeState.routePath : normalizeH5Path(routeKey)
		let pageTitle = ''
		if (typeof preferredTitle === 'string') {
			pageTitle = preferredTitle.trim()
		} else if (routeKey && Object.prototype.hasOwnProperty.call(h5DynamicTitleMap, routeKey)) {
			pageTitle = h5DynamicTitleMap[routeKey]
		} else {
			pageTitle = getH5StaticPageTitle(routePath)
		}
		const nextTitle = buildH5DocumentTitle(pageTitle)
		if (document.title !== nextTitle) {
			document.title = nextTitle
		}
	}

	function queueH5DocumentTitleSync(preferredRouteKey, preferredTitle) {
		setTimeout(() => {
			syncH5DocumentTitle(preferredRouteKey, preferredTitle)
		}, 0)
	}

	return function setupH5DocumentTitleSync(registerMixin) {
		if (typeof registerMixin === 'function') {
			registerMixin({
				onLoad() {
					queueH5DocumentTitleSync()
				},
				onShow() {
					queueH5DocumentTitleSync()
				},
				onReady() {
					queueH5DocumentTitleSync()
				}
			})
		}
		if (!hasPatchedH5NavigationBarTitle && typeof uni !== 'undefined' && typeof uni.setNavigationBarTitle === 'function') {
			const originalSetNavigationBarTitle = uni.setNavigationBarTitle.bind(uni)
			uni.setNavigationBarTitle = function(options = {}) {
				const routeState = getCurrentH5RouteState()
				const routeKey = routeState.routeKey
				const title = typeof options.title === 'string' ? options.title.trim() : ''
				if (routeKey) {
					h5DynamicTitleMap[routeKey] = title
				}
				queueH5DocumentTitleSync(routeKey, title)
				return originalSetNavigationBarTitle(options)
			}
			hasPatchedH5NavigationBarTitle = true
		}
		if (!hasBoundH5HistoryEvents && typeof window !== 'undefined') {
			window.addEventListener('popstate', () => {
				const routeState = getH5LocationRouteState()
				queueH5DocumentTitleSync(routeState.routeKey)
			})
			window.addEventListener('hashchange', () => {
				const routeState = getH5LocationRouteState()
				queueH5DocumentTitleSync(routeState.routeKey)
			})
			hasBoundH5HistoryEvents = true
		}
		queueH5DocumentTitleSync()
	}
}
