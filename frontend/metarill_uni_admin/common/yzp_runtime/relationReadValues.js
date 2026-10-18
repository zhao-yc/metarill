/** 将关系数组或 JSON 字符串读取为副本，不改动服务端返回的 ID 顺序。 */
export function relationArray(value) {
	if (Array.isArray(value)) return value.slice()
	if (typeof value !== 'string' || !value.trim()) return []
	try {
		const parsed = JSON.parse(value)
		return Array.isArray(parsed) ? parsed : []
	} catch (_) {
		return []
	}
}

/** 名称数组保留缺失项的空位置，避免名称与关系 ID 错位。 */
export function relationDisplayText(value, joiner = '、') {
	if (value === null || value === undefined) return ''
	const array = Array.isArray(value) || (typeof value === 'string' && value.trim().startsWith('['))
	if (!array) return String(value)
	return relationArray(value).map(item => item === null || item === undefined ? '' : String(item)).join(joiner)
}

/** 已选缓存使用页面实际显示字段，同时兼容既有 name 约定。 */
export function selectedRelationRows(ids, names, nameField = 'name') {
	const selectedNames = relationArray(names)
	return relationArray(ids).map((id, index) => {
		const name = selectedNames[index] === undefined ? null : selectedNames[index]
		// nameField=id 时真实关系 ID 优先，缺失名称不能覆盖它。
		return { name, [nameField]: name, id }
	})
}

/** 用本次授权列表刷新已选记录，保持跨页已选顺序及失效 ID。 */
export function refreshSelectedRelationRows(selected, rows) {
	const byId = new Map((rows || []).map(row => [String(row.id), row]))
	return (selected || []).map(row => {
		const current = byId.get(String(row.id))
		return current ? Object.assign({}, current, { id: row.id }) : row
	})
}

/** 界面辅助值只用于显示，提交副本剔除非物理字段；最终信任边界仍在后端。 */
export function relationWriteEntity(entity, nonPhysicalFields) {
	const payload = Object.assign({}, entity || {})
	;(nonPhysicalFields || []).forEach(field => { delete payload[field] })
	return payload
}
