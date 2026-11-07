/** V8 独立授权的公共前端协议，不读取平台专用元数据或自行判断授权。 */
export const PERMISSION_ACTIONS = { is_query: '查看', is_new: '新增', is_update: '修改', is_delete: '删除', is_export: '导出', is_all: '全功能（兼容）' }

/** 去重并稳定排序，用字符串保留长标识，避免勾选顺序导致虚假的未保存状态。 */
function ids(values) { return [...new Set((Array.isArray(values) ? values : []).map(String))].sort() }

/** 复制授权草稿；菜单、页面和按钮保持独立，不能相互推导或自动补权。 */
export function copyPermissionGrants(grants = {}) {
	const pagePermissions = {}
	Object.keys(grants.pagePermissions || {}).sort().forEach(id => {
		const actions = ids(grants.pagePermissions[id])
		if (actions.length) pagePermissions[id] = actions
	})
	return { menuIds: ids(grants.menuIds), pagePermissions, buttonIds: ids(grants.buttonIds) }
}

/** 只有授权内容变化才视为未保存。 */
export function permissionFingerprint(grants) { return JSON.stringify(copyPermissionGrants(grants)) }

/** 目录仅展示层级，一级分组不作为授权资源；同页多入口保留独立 ID。 */
export function permissionMenuRows(catalog = {}) {
	const rows = [], seen = new Set()
	const visit = (nodes, depth) => (nodes || []).forEach(node => {
		if (!node || !node.id || seen.has(String(node.id))) return
		seen.add(String(node.id)); rows.push({ ...node, id: String(node.id), depth, grantable: depth > 0 })
		visit(node.children, depth + 1)
	})
	visit(catalog.menus && catalog.menus.roots, 0)
	return rows
}

/** 修改单项授权时保留其他维度，页面动作只更新当前页面。 */
export function changePermissionGrant(grants, type, id, value) {
	const next = copyPermissionGrants(grants)
	if (type === 'page' || type === 'pageAll') {
		const previous = next.pagePermissions[String(id)] || []
		const values = type === 'page' ? ids(value) : value ? ids([...previous, 'is_all']) : previous.filter(action => action !== 'is_all')
		if (values.length) next.pagePermissions[String(id)] = values
		else delete next.pagePermissions[String(id)]
	} else if (type === 'menu' || type === 'button') {
		const key = type === 'menu' ? 'menuIds' : 'buttonIds'
		next[key] = value ? ids([...next[key], id]) : next[key].filter(item => item !== String(id))
	}
	return next
}

/** 全功能值展示该页所有普通动作，但复制/导入所需的 is_all 必须单独明确授权。 */
export function selectedPageActions(grants, page) {
	const values = grants.pagePermissions && grants.pagePermissions[String(page.id)] || []
	return values.includes('is_all') ? (page.actions || []).filter(action => action !== 'is_all') : values
}

/** 显式列出已失效授权供用户撤销，不在读取或保存前静默删掉旧授权。 */
export function invalidPermissionGrants(catalog = {}, grants = {}) {
	const result = [], menus = new Set(permissionMenuRows(catalog).filter(row => row.grantable).map(row => row.id))
	const pages = new Map((catalog.pages || []).map(page => [String(page.id), page]))
	const buttons = new Set((catalog.buttons || []).map(button => String(button.id)))
	copyPermissionGrants(grants).menuIds.filter(id => !menus.has(id)).forEach(id => result.push({ key: 'menu:' + id, type: 'menu', id, text: '已失效菜单：' + id }))
	Object.entries(grants.pagePermissions || {}).forEach(([id, actions]) => {
		const page = pages.get(id)
		if (!page) result.push({ key: 'page:' + id, type: 'page', id, text: '已失效页面：' + id })
		else (actions || []).filter(action => action !== 'is_all' && !(page.actions || []).includes(action)).forEach(action => {
			result.push({ key: 'action:' + id + ':' + action, type: 'pageAction', id, action, text: page.name + '：' + (PERMISSION_ACTIONS[action] || action) + '（已关闭）' })
		})
	})
	copyPermissionGrants(grants).buttonIds.filter(id => !buttons.has(id)).forEach(id => result.push({ key: 'button:' + id, type: 'button', id, text: '已失效按钮：' + id }))
	return result
}

/** 身份和设备选择用于隔离过期响应，绝不作为权限来源。 */
export function permissionContext(read) {
	const params = read('sysParams') || {}
	return JSON.stringify([read('loginStateId') || '', (read('userInfo') || {}).id || '',
		Object.keys(params).sort().map(key => [key, params[key]])])
}

/** 授权配置独立请求通道；所有完成路径收尾，未知接口不能降级到旧写入口。 */
export function createPermissionRequestLane(request, getContext = () => '', invalidated = () => {}) {
	let active = null
	const cancel = () => {
		const previous = active; active = null
		if (previous) { previous.resolve({ current: false }); if (previous.task && previous.task.abort) previous.task.abort() }
	}
	return { cancel,
		run(path, data, method = 'POST') {
			cancel()
			return new Promise((resolve, reject) => {
				const call = { resolve, context: getContext(), task: null }; active = call
				const current = () => {
					if (active !== call) return false
					if (getContext() === call.context) return true
					active = null; resolve({ current: false }); invalidated(); return false
				}
				const settle = callback => { if (current()) { active = null; callback() } }
				const failure = response => {
					if (response && (response.status === 404 || response.statusCode === 404)) return '授权接口不可用，请先更新后端后重试'
					return response && (response.msg || response.message) || '未取得有效授权配置，请重试'
				}
				try {
					call.task = request({ url: '/admin/sys/yzp_permission/' + path, method, data,
						sendSessionId: true, showLoading: false, showError: false, isCurrent: current,
						success: response => settle(() => {
							if (response && Number(response.code) === 200 && response.data && typeof response.data.supported === 'boolean') resolve({ current: true, data: response.data })
							else reject(new Error(failure(response)))
						}),
						fail: () => settle(() => reject(new Error('网络暂不可用，请重试'))),
						complete: response => settle(() => reject(new Error(failure(response)))) })
					if (!call.task) settle(() => reject(new Error('请求未发起，请确认登录状态后重试')))
				} catch (error) { settle(() => reject(error)) }
			})
		}
	}
}

/** 公共状态机统一预览、激活、保存和冲突恢复；视图只负责用户确认与勾选。 */
export function createPermissionWorkbench({ request, getContext, onChange = () => {}, onSaved = () => {} }) {
	let generation = 0, identity = '', state
	const publish = patch => { state = { ...state, ...patch }; onChange(state) }
	const empty = () => ({ status: 'idle', roles: [], roleId: '', snapshot: null, grants: copyPermissionGrants(), baseline: '', busy: false, error: '', mutationError: '' })
	state = empty()
	const clear = () => { generation++; lane.cancel(); publish({ ...empty(), status: 'error', error: '登录状态或项目上下文已变化，请重新读取授权配置' }) }
	const lane = createPermissionRequestLane(request, getContext, clear)
	const currentIdentity = () => { if (getContext() === identity) return true; clear(); return false }
	const accept = value => {
		if (!value.supported) { publish({ ...empty(), status: 'unsupported' }); return }
		const grantsValid = value.grants && typeof value.grants === 'object' && !Array.isArray(value.grants)
			&& Array.isArray(value.grants.menuIds) && Array.isArray(value.grants.buttonIds)
			&& value.grants.pagePermissions && typeof value.grants.pagePermissions === 'object' && !Array.isArray(value.grants.pagePermissions)
			&& Object.values(value.grants.pagePermissions).every(Array.isArray)
		if (!value.catalog || !grantsValid || !['LEGACY', 'RESOURCE'].includes(value.mode) || !value.revision) throw new Error('授权配置不完整，请更新后端后重试')
		const grants = copyPermissionGrants(value.grants)
		publish({ status: 'ready', snapshot: value, roleId: String(value.roleId), grants, baseline: permissionFingerprint(grants), mutationError: '' })
	}
	const loadRole = async roleId => {
		if (!currentIdentity()) return
		const version = ++generation
		publish({ status: 'loading', roleId: String(roleId), snapshot: null, grants: copyPermissionGrants(), baseline: '', busy: true, error: '', mutationError: '' })
		try { const result = await lane.run('snapshot', { roleId: String(roleId) }); if (result.current && version === generation) accept(result.data) }
		catch (error) { if (version === generation) publish({ status: 'error', error: error.message }) }
		finally { if (version === generation) publish({ busy: false }) }
	}
	/** 激活发送预览版本，不提交可变草稿；保存只接受已经激活的角色。 */
	const mutate = async operation => {
		if (!currentIdentity() || state.busy || !state.snapshot) return false
		if (operation === 'activate' && (state.snapshot.mode !== 'LEGACY' || (state.snapshot.issues || []).length || (state.snapshot.catalog.issues || []).length)) return false
		if (operation === 'save' && state.snapshot.mode !== 'RESOURCE') return false
		const version = generation
		const body = { roleId: state.roleId, revision: state.snapshot.revision }
		if (operation === 'save') body.grants = copyPermissionGrants(state.grants)
		publish({ busy: true, mutationError: '' })
		try {
			const result = await lane.run(operation, body)
			if (!result.current || version !== generation) return false
			accept(result.data); onSaved(result.data); return true
		} catch (error) { if (version === generation) publish({ mutationError: error.message }); return false }
		finally { if (version === generation) publish({ busy: false }) }
	}
	return {
		getState: () => state, loadRole, clear,
		/** 探测只接受服务端明确的不支持；404、空响应和网络错误都保留失败状态。 */
		async loadRoles(preferredRoleId = '') {
			identity = getContext(); const version = ++generation
			publish({ ...empty(), status: 'loading', busy: true })
			try {
				const result = await lane.run('roles', undefined, 'GET')
				if (!result.current || version !== generation) return
				if (!result.data.supported) { publish({ status: 'unsupported', busy: false }); return }
				const roles = (result.data.roles || []).map(role => ({ ...role, id: String(role.id) }))
				publish({ roles, status: 'ready', busy: false })
				const selected = roles.find(role => role.id === String(preferredRoleId)) || roles[0]
				if (selected) await loadRole(selected.id)
			} catch (error) { if (version === generation) publish({ status: 'error', error: error.message, busy: false }) }
		},
		/** 独立授权启用后才接受修改，切换菜单不会自动添加页面或按钮权限。 */
		change(type, id, value) { if (currentIdentity() && !state.busy && state.snapshot && state.snapshot.mode === 'RESOURCE') publish({ grants: changePermissionGrant(state.grants, type, id, value) }) },
		activate: () => mutate('activate'), save: () => mutate('save'),
		/** 放弃只恢复最近服务端快照，不自动刷新并覆盖其他人的修改。 */
		discard() { if (state.snapshot) publish({ grants: copyPermissionGrants(state.snapshot.grants), mutationError: '' }) },
		/** 卸载使所有回调失效，不再向已销毁组件推送状态。 */
		dispose() { generation++; lane.cancel() }
	}
}
