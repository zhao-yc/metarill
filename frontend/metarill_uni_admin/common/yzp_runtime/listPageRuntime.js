/** 复制并冻结本次请求的普通数据，筛选框后续编辑不能改变已经发出的查询。 */
function snapshot(value) {
	if (value === null || typeof value !== 'object') return value
	if (value instanceof Date) return value.toJSON()
	const copy = Array.isArray(value) ? value.map(snapshot) : Object.fromEntries(
		Object.keys(value).sort().map(key => [key, snapshot(value[key])]))
	return Object.freeze(copy)
}

/** 登录和项目上下文先变化、事件尚未发出时也拒绝旧回执；只存内部快照，不进入HTTP请求。 */
function queryIdentity() {
	const read = typeof uni !== 'undefined' && typeof uni.getStorageSync === 'function' ? key => uni.getStorageSync(key) : () => null
	return JSON.stringify(snapshot([read('loginStateId') || '', read('sysParams') || {}]))
}

/** 子表没有父记录时不能请求全表，数字零仍是有效的父记录标识。 */
function missingParent(page) {
	return page.is_son_page && (page.parent_id === null || page.parent_id === undefined || String(page.parent_id).trim() === '')
}

/** 读取服务端页数和条数，缺省或非法数字按零展示。 */
function count(value) {
	const number = Number(value)
	return Number.isFinite(number) && number >= 0 ? number : 0
}

/** 创建普通列表与子表共用的状态机；页面仍负责首次加载和自己的交互。 */
export function createListPageMixin({ url, pageSize = 15, request, onLoaded, onParentChanged, additionalData, emptyResult, canLoad } = {}) {
	// 请求任务、代次和查询快照不进入 Vue 响应式对象，也不在不同组件间共享。
	const instances = new WeakMap()

	/** 按组件实例惰性建立请求状态。 */
	function state(page) {
		if (!instances.has(page)) instances.set(page, {
			sequence: 0, active: null, disposed: false, scope: null,
			scopeKey: '', confirmed: false, retry: null, pullPending: false
		})
		return instances.get(page)
	}

	/** 只在提交查询时读取筛选；编辑但未提交的筛选不影响在途响应和下一页。 */
	function queryScope(page) {
		const scope = { identity: queryIdentity(), orderBy: page.orderBy || {}, filters: page.filters || {},
			parentId: page.parent_id, pageSize: page.page_size, isSonPage: !!page.is_son_page }
		// 原type=2页也使用相同请求隔离；附加的已选ID必须进入冻结范围及漂移判定。
		if (typeof additionalData === 'function') scope.additionalData = snapshot(additionalData.call(page) || {})
		return snapshot(scope)
	}

	/** 所有回调同时校验代次、组件存活和父记录，覆盖 watcher 执行前的窗口。 */
	function current(page, pending) {
		const value = state(page)
		return !value.disposed && value.active === pending && value.sequence === pending.sequence
			&& page.parent_id === pending.scope.parentId && !!page.is_son_page === pending.scope.isSonPage
			&& queryIdentity() === pending.scope.identity
	}

	/** 先让旧回调失效再取消网络，兼容 abort 同步触发 fail/complete 的请求实现。 */
	function invalidate(page) {
		const value = state(page)
		const previous = value.active
		value.sequence++
		value.active = null
		if (previous && previous.task && typeof previous.task.abort === 'function') {
			try { previous.task.abort() } catch (_) { /* 请求失效不依赖底层取消是否成功。 */ }
		}
	}

	/** 查询范围变化时立即清掉旧父表或旧筛选结果。 */
	function clearRows(page) {
		page.list = []
		page.page_no = 1
		page.pages_qty = 0
		page.rows_qty = 0
		page.statistics = {}
		page.list_error = ''
		page.pagination_version++
		state(page).confirmed = false
	}

	/** 身份退出仅清理请求与展示范围，不永久销毁实例，也不发出新的请求。 */
	function resetContext(page) {
		const value = state(page)
		if (value.disposed) return
		invalidate(page)
		clearRows(page)
		value.scope = null
		value.scopeKey = ''
		value.retry = null
		page.load_more_status = 'no-more'
		stopPull(page)
	}

	/** 失败保留重试入口；没有分页时成功一次便结束追加。 */
	function status(page) {
		const value = state(page)
		if (value.active) return 'loading'
		if (page.list_error || !value.confirmed) return 'more'
		return Number(page.page_size) <= 0 || page.pages_qty === 0 || page.page_no >= page.pages_qty ? 'no-more' : 'more'
	}

	/** 仅结束本实例发起的下拉刷新，普通刷新和卸载不关闭其他页面的下拉状态。 */
	function stopPull(page) {
		const value = state(page)
		if (!value.disposed && value.pullPending) {
			value.pullPending = false
			uni.stopPullDownRefresh()
		}
	}

	/** 当前请求统一收尾；旧请求的 complete 不得修改新请求的加载状态。 */
	function complete(page, pending) {
		if (!current(page, pending)) return
		if (!pending.handled) fail(page, pending, '列表请求未完成，请重试')
		state(page).active = null
		// 即使 loading 与完成发生于同一 Vue 更新周期，也让分页控件回到已确认页。
		page.pagination_version++
		page.load_more_status = status(page)
		stopPull(page)
	}

	/** 记录失败页但不推进已确认页码；错误在加载更多提示中可见。 */
	function fail(page, pending, message, showModal = false) {
		if (!current(page, pending) || pending.handled) return
		pending.handled = true
		page.list_error = message || '列表加载失败，请重试'
		state(page).retry = { pageNo: pending.pageNo, append: pending.append, scope: pending.scope }
		if (showModal) uni.showModal({ showCancel: false, content: page.list_error })
	}

	/** 发起一个请求代次，同范围保留已确认行，范围变化或父记录缺失立即清空。 */
	function load(page, pageNo, append, scope, force = false) {
		const value = state(page)
		if (value.disposed) return
		if (typeof canLoad === 'function' && canLoad.call(page) === false) { resetContext(page); return }
		if (scope.identity !== queryIdentity()) return refresh(page)
		if (missingParent(page)) {
			invalidate(page)
			clearRows(page)
			value.scope = null
			value.scopeKey = ''
			value.retry = null
			page.load_more_status = 'no-more'
			stopPull(page)
			return
		}
		// 原多选页仅看已选但草稿为空时只显示空结果，不能把空ID条件当成查询全部。
		if (typeof emptyResult === 'function' && emptyResult.call(page, scope)) {
			invalidate(page); clearRows(page)
			value.scope = scope; value.scopeKey = JSON.stringify(scope); value.retry = null; value.confirmed = true
			page.load_more_status = 'no-more'; stopPull(page); return
		}
		const scopeKey = JSON.stringify(scope)
		if (!force && value.active && current(page, value.active)
			&& value.scopeKey === scopeKey && value.active.pageNo === pageNo && value.active.append === append) {
			return value.active.task
		}
		invalidate(page)
		if (value.scopeKey !== scopeKey) clearRows(page)
		value.scope = scope
		value.scopeKey = scopeKey
		value.retry = null
		page.list_error = ''
		page.load_more_status = 'loading'
		// 请求开始和完成均重建分页 UI，覆盖控件自己先改页码的行为。
		page.pagination_version++
		const pending = { sequence: value.sequence, pageNo, append, scope, task: null, handled: false }
		value.active = pending
		const data = Object.freeze({ ...(scope.additionalData || {}), orderBy: scope.orderBy, pageNo, pageSize: scope.pageSize,
			filters: scope.filters, parentId: scope.parentId })
		try {
			const task = request({ url, data, sendSessionId: true, method: 'POST',
				isCurrent: () => current(page, pending),
				success(res) {
					if (!current(page, pending) || pending.handled) return
					if (!res || res.code != 200) {
						fail(page, pending, res && res.msg, true)
						return
					}
					if (!res.data || !Array.isArray(res.data.records)) {
						fail(page, pending, '列表返回数据格式不正确，请重试', true)
						return
					}
					pending.handled = true
					page.list = append ? page.list.concat(res.data.records) : res.data.records.slice()
					page.page_no = pageNo
					page.pages_qty = count(res.data.pages)
					page.rows_qty = count(res.data.total)
					page.statistics = res.map && res.map.statistics || {}
					page.list_error = ''
					value.confirmed = true
					value.retry = null
					if (typeof onLoaded === 'function') onLoaded.call(page, res)
				},
				fail(error) { fail(page, pending, error && (error.errMsg || error.message)) },
				complete() { complete(page, pending) }
			})
			pending.task = task
			// 缺少登录凭据时旧请求封装直接返回，不能让列表永久停留在 loading。
			if (!task && current(page, pending)) {
				fail(page, pending, '请求未发出，请确认登录状态后重试')
				complete(page, pending)
			}
			return task
		} catch (error) {
			fail(page, pending, error && error.message || '列表请求失败，请重试')
			complete(page, pending)
		}
	}

	/** 明确刷新总是建立新快照和新代次，不能复用保存之前的旧请求。 */
	function refresh(page, keepCurrentPage = false) {
		const value = state(page)
		if (value.disposed) return
		try {
			const scope = queryScope(page)
			// 旧 PC 按钮调用 getList 后留在已确认页；新筛选和移动列表仍从第一页替换。
			const retainPage = keepCurrentPage && page.$deviceInfo && page.$deviceInfo.is_pc
				&& value.confirmed && value.scopeKey === JSON.stringify(scope)
			return load(page, retainPage ? page.page_no : 1, false, scope, true)
		}
		catch (error) {
			invalidate(page)
			page.pagination_version++
			page.list_error = error.message || '筛选条件无法读取，请重试'
			page.load_more_status = status(page)
			stopPull(page)
		}
	}

	/** 父上下文改变时先交给页面清理选择和弹窗，再加载新的列表范围。 */
	function parentChanged(page) {
		if (state(page).disposed) return
		if (typeof onParentChanged === 'function') onParentChanged.call(page)
		return refresh(page)
	}

	/** 页面卸载和组件销毁可以重复调用，之后所有迟到回调均无副作用。 */
	function dispose(page) {
		const value = state(page)
		if (value.disposed) return
		value.disposed = true
		value.pullPending = false
		invalidate(page)
	}

	return {
		/** 列表状态统一由运行时维护，配置的零或负数分页大小原样传递。 */
		data() {
			return { list: [], page_no: 1, page_size: pageSize, pages_qty: 0, rows_qty: 0, pagination_version: 0,
				statistics: {}, load_more_status: 'more', list_error: '' }
		},
		computed: {
			/** 失败提示同时提供首屏和追加重试入口。 */
			loadMoreText() {
				const summary = '共' + this.rows_qty + '条数据，当前' + this.page_no + '/' + this.pages_qty + '页'
				return { contentnomore: summary + '，已全部加载',
					contentdown: this.list_error ? this.list_error + '，点击重试' : summary + '，点击显示更多' }
			}
		},
		watch: {
			/** 父记录变化立即切换查询范围，空父记录只清空而不发起请求。 */
			parent_id() { parentChanged(this) },
			/** 列表切换普通页与子表模式时，同样废弃旧上下文交互状态。 */
			is_son_page() { parentChanged(this) }
		},
		/** 下拉刷新才登记关闭权限，重复刷新由当前最新请求统一收尾。 */
		onPullDownRefresh() {
			if (state(this).disposed) return
			state(this).pullPending = true
			return refresh(this)
		},
		/** uni-app 页面销毁入口。 */
		onUnload() { dispose(this) },
		/** Vue 子表组件销毁入口。 */
		beforeUnmount() { dispose(this) },
		methods: {
			/** 只清理当前身份的旧行、分页、统计与在途请求；下一次登录仍可刷新。 */
			resetListContext() { resetContext(this) },
			/** 兼容旧按钮：PC 重取当前已确认页，移动端及查询范围变化从第一页替换。 */
			getList() { return refresh(this, true) },
			/** 保存、筛选和按钮刷新复用同一个刷新入口。 */
			refreshData() { return refresh(this) },
			/** PC 翻页替换列表；成功后才更新已经确认的页码。 */
			paginationChange(event) {
				const pageNo = Number(event && event.current)
				if (!Number.isInteger(pageNo) || pageNo < 1 || state(this).disposed) return
				return load(this, pageNo, false, queryScope(this))
			},
			/** 追加沿用已提交筛选；连续点击复用在途任务，失败重试同一目标页。 */
			loadNextPage() {
				const value = state(this)
				if (value.disposed) return
				if (missingParent(this) || !value.scope || this.parent_id !== value.scope.parentId) return refresh(this)
				if (value.active && current(this, value.active)) return value.active.task
				if (value.retry) return load(this, value.retry.pageNo, value.retry.append, value.retry.scope)
				if (!value.confirmed) return load(this, 1, false, value.scope)
				if (Number(this.page_size) <= 0 || this.page_no >= this.pages_qty) return
				return load(this, this.page_no + 1, !(this.$deviceInfo && this.$deviceInfo.is_pc), value.scope)
			},
			/** 错误提示重试原目标页，PC 翻页失败不能退回第一页或改成追加。 */
			retryListRequest() {
				const value = state(this)
				if (value.disposed) return
				if (missingParent(this) || !value.scope || this.parent_id !== value.scope.parentId) return refresh(this)
				if (value.active && current(this, value.active)) return value.active.task
				if (value.retry) return load(this, value.retry.pageNo, value.retry.append, value.retry.scope)
				return refresh(this)
			},
			/** 保存回读仅合并当前行；先取消旧查询，避免旧响应覆盖刚保存的数据。 */
			applyUpdatedEntityFromDetail(change) {
				if (state(this).disposed || !change || typeof change === 'string'
					|| change.action != 'save' || change.method != 'update' || !change.entity || !change.entity.id) return false
				const index = this.list.findIndex(item => item && item.id == change.entity.id)
				if (index < 0) return false
				invalidate(this)
				this.pagination_version++
				state(this).retry = null
				this.list_error = ''
				this.list.splice(index, 1, Object.assign({}, this.list[index], change.entity))
				this.load_more_status = status(this)
				stopPull(this)
				return true
			}
		}
	}
}
