/** 菜单工作台仅维护平台元数据，不参与生成项目的导航运行时。 */
export const MENU_WORKBENCH_API = '/admin/custom/menu_workbench'

/** 会话和设备选择仅用于识别响应是否过期，不作为客户端授权依据，也不写入草稿。 */
export function readWorkbenchIdentity(readStorage) {
	const params = readStorage('sysParams') || {}
	return JSON.stringify([readStorage('loginStateId') || '', (readStorage('userInfo') || {}).id || '',
		Object.keys(params).sort().map(key => [key, params[key]])])
}

/** 将服务端树展开为带层级的展示数据，使用字符串标识避免长整型精度丢失。 */
export function flattenMenuTree(tree) {
	const result = []
	const seen = new Set()
	const visit = (nodes, depth, parentPath) => (nodes || []).forEach(node => {
		const id = String(node.id || '')
		if (!id || seen.has(id)) return
		seen.add(id)
		const path = [...parentPath, node.name || '未命名菜单']
		result.push({ ...node, id, depth, path, isFirst: depth === 0 })
		visit(node.children, depth + 1, path)
	})
	visit(tree, 0, [])
	return result
}

/** 草稿只包含可编辑字段，避免把页面路由和项目归属作为客户端可写元数据。 */
export function createMenuDraft(node, parentId = '', sortNo = 10) {
	return {
		id: node ? String(node.id) : '',
		kind: node ? (node.isFirst || !node.parentId ? 'FIRST_MENU' : node.kind) : 'GENERATED_PAGE',
		parentId: node && node.parentId ? String(node.parentId) : parentId,
		name: node ? node.name || '' : '',
		icon: node ? node.icon || '' : '',
		sortNo: node ? Number(node.sortNo || 0) : sortNo,
		hidden: !!(node && node.hidden),
		targetPageId: node ? String(node.targetPageId || '') : '',
		showCount: !!(node && node.showCount),
		visibleScheme: node ? node.visibleScheme || '' : ''
	}
}

/** 使用固定字段序列比较草稿，忽略 Vue 响应对象和树的展示字段。 */
export function menuDraftFingerprint(draft) {
	if (!draft) return ''
	return JSON.stringify(['id', 'kind', 'parentId', 'name', 'icon', 'sortNo', 'hidden', 'targetPageId',
		'showCount', 'visibleScheme'].map(key => draft[key]))
}

/** 限制循环、超过三级和带子项跨分组移动；最终约束仍由服务端确认。 */
export function menuParentOptions(tree, draft) {
	const flat = flattenMenuTree(tree)
	const current = flat.find(node => node.id === draft.id)
	const descendants = new Set(current ? flattenMenuTree(current.children).map(node => node.id) : [])
	const childDepth = current && current.children && current.children.length
		? Math.max(...flattenMenuTree(current.children).map(node => node.depth)) + 1 : 0
	return flat.filter(node => node.kind === 'DIRECTORY' && node.id !== draft.id && !descendants.has(node.id)
		&& node.depth + 1 + childDepth <= 2
		&& (!descendants.size || node.firstMenuId === current.firstMenuId))
		.map(node => ({ value: node.id, text: node.path.join(' / ') }))
}

/** 保存前给出就地反馈，不从路由文本推断页面身份或角标端点。 */
export function validateMenuDraft(snapshot, draft) {
	if (!snapshot || snapshot.isV8 !== true || snapshot.canWrite !== true) return '当前项目不可编辑菜单'
	if (!draft || !draft.name.trim()) return '请输入菜单名称'
	if (draft.name.trim().length > 50) return '菜单名称不能超过 50 个字符'
	if (!['FIRST_MENU', 'DIRECTORY', 'GENERATED_PAGE', 'CUSTOM_PAGE', 'LEGACY_PAGE'].includes(draft.kind)) return '请选择有效的菜单类型'
	if (String(draft.sortNo).trim() === '' || !Number.isSafeInteger(Number(draft.sortNo)) || Number(draft.sortNo) > 2147483647 || Number(draft.sortNo) < -2147483648) return '排序请输入有效范围内的整数'
	if (draft.kind !== 'FIRST_MENU' && !menuParentOptions(snapshot.tree, draft).some(item => item.value === draft.parentId)) {
		return '请选择有效的上级分组或目录'
	}
	if (['GENERATED_PAGE', 'CUSTOM_PAGE'].includes(draft.kind)) {
		const target = (snapshot.targets || []).find(item => String(item.id) === draft.targetPageId && item.kind === draft.kind)
		if (!target) return '请选择当前后台的目标页面'
		if (draft.showCount && !target.showCountSupported) return '所选页面不支持数量角标'
	}
	if (draft.kind === 'LEGACY_PAGE' && !draft.id) return '新菜单请选择已配置的页面'
	return ''
}

/** 请求携带原始版本号，服务端检测并发冲突；显式清空可写字段以支持改绑页面。 */
export function buildMenuSaveRequest(snapshot, draft) {
	return {
		projectId: snapshot.projectId, adminTypeId: snapshot.adminTypeId, revision: snapshot.revision,
		...createMenuDraft({ ...draft, parentId: draft.kind === 'FIRST_MENU' ? null : draft.parentId,
			isFirst: draft.kind === 'FIRST_MENU' }),
		name: draft.name.trim(), sortNo: Number(draft.sortNo),
		targetPageId: ['GENERATED_PAGE', 'CUSTOM_PAGE'].includes(draft.kind) ? draft.targetPageId : '',
		showCount: draft.kind === 'GENERATED_PAGE' && !!draft.showCount
	}
}

/** 每条读取通道只接收最新响应；即使取消后的回调迟到，也不能覆盖当前项目。 */
export function createWorkbenchRequestLane(request, { getContext = () => '', onContextChanged = () => {} } = {}) {
	let active = null
	const invalidate = () => {
		const previous = active
		active = null
		if (previous) {
			previous.resolve({ current: false })
			if (previous.task && typeof previous.task.abort === 'function') previous.task.abort()
		}
	}
	return {
		invalidate,
		run(path, data = {}) {
			invalidate()
			return new Promise((resolve, reject) => {
				const call = { resolve, task: null, context: getContext() }
				active = call
				const current = () => {
					if (active !== call) return false
					if (getContext() === call.context) return true
					active = null
					resolve({ current: false })
					onContextChanged()
					return false
				}
				const settle = callback => { if (current()) { active = null; callback() } }
				try {
					call.task = request({
						url: MENU_WORKBENCH_API + path, method: 'POST', data,
						sendSessionId: true, showLoading: false, showError: false, isCurrent: current,
						success: response => settle(() => {
							if (response && Number(response.code) === 200) resolve({ current: true, data: response.data })
							else reject(new Error(response && (response.msg || response.message) || '菜单配置请求失败'))
						}),
						fail: () => settle(() => reject(new Error('网络暂不可用，请重试'))),
						// 空响应、HTTP 错误和登录过期可能仅触发 complete，仍须结束工作台加载状态。
						complete: response => settle(() => reject(new Error(response && (response.msg || response.message)
							|| '未取得有效菜单配置，请重试')))
					})
					// 无登录凭据时请求封装不创建任务，也不调用回调；同步成功已收尾的调用不受影响。
					if (!call.task) settle(() => reject(new Error('请求未发起，请确认登录状态后重试')))
				} catch (error) { settle(() => reject(error)) }
			})
		}
	}
}
