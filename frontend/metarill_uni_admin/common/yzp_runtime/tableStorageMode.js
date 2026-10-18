/** 空值采用创建默认值；未知值保持无效，不能将错误配置静默归入另一模式。 */
export function tableStorageMode(value) {
	if (value === null || value === undefined || value === '') return 1
	if (value === 0 || value === '0') return 0
	if (value === 1 || value === '1') return 1
	return null
}

/** 详情路由 ID 或返回实体 ID 任一存在，均表示配置已保存。 */
export function isSavedTableConfig(entity, recordId) {
	const validId = value => value !== null && value !== undefined && value !== ''
		&& value !== 'undefined' && value !== 'null'
	return validId(recordId) || validId(entity && entity.id)
}

/** 仅 V8 使用首次保存冻结；能力未确认时禁止切换，V7 沿用原冗余开关。 */
export function isTableStorageModeLocked(entity, recordId, readonly, policy) {
	if (readonly || !policy || typeof policy.isV8 !== 'boolean') return true
	return policy.isV8 && isSavedTableConfig(entity, recordId)
}

/** 按项目能力写入副本；V7 的旧冗余标志不代表启用 V8 非冗余能力。 */
export function changeTableStorageMode(entity, recordId, readonly, value, policy) {
	if (isTableStorageModeLocked(entity, recordId, readonly, policy)) return null
	const mode = tableStorageMode(value)
	if (mode === null || (policy.isV8 && mode === 0 && !policy.supportsNonRedundant)) return null
	return Object.assign({}, entity || {}, { isRongyu: mode })
}
