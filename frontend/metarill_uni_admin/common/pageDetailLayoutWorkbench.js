/** 创建独立草稿；字段顺序完全沿用服务端快照，不按分组重新排序。 */
export function detailLayoutDraft(snapshot) {
	const layout = snapshot.detailLayout || { version: 1, mode: 'none', groups: [] };
	if (layout.version !== 1 || !['none', 'tabs'].includes(layout.mode)) throw new Error('当前布局版本暂不支持，请升级工作台后再编辑');
	const groups = (layout.groups || []).map(group => ({ key: group.key, name: group.name }));
	const keys = new Set(groups.map(group => group.key));
	return { detailLayout: { version: 1, mode: layout.mode, groups },
		fields: (snapshot.fields || []).map(field => ({ ...field, detailGroupKey: keys.has(field.detailGroupKey) ? field.detailGroupKey : '' })) };
}

/** 布局保存只提交页面字段标识和归属，不夹带名称、展示开关等其他配置。 */
export function detailLayoutPayload(pageId, revision, draft) {
	return { pageId, revision, detailLayout: { version: 1, mode: draft.detailLayout.mode,
		groups: draft.detailLayout.groups.map(group => ({ key: group.key, name: group.name.trim() })) },
		fields: draft.fields.map(field => ({ pageColumnId: field.pageColumnId, detailGroupKey: field.detailGroupKey || '' })) };
}

/** 提交前明确指出不完整名称，避免保存时默默丢弃分组。 */
export function detailLayoutIssue(draft) {
	const names = draft.detailLayout.groups.map(group => group.name.trim());
	if (names.some(name => !name)) return '请填写每个标签的名称';
	if (names.some(name => name.length > 100)) return '标签名称最多为100个字符';
	if (new Set(names).size !== names.length) return '标签名称不能重复';
	return '';
}

/** 自动生成稳定标识，标签改名和调整顺序都不改变标识。 */
export function createDetailGroupKey(groups) {
	let key;
	do { key = 'group_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 10); }
	while (groups.some(group => group.key === key));
	return key;
}

/** 删除标签时将其字段放回常显区，不改变字段的既有顺序。 */
export function removeDetailGroup(draft, key) {
	draft.detailLayout.groups = draft.detailLayout.groups.filter(group => group.key !== key);
	draft.fields.forEach(field => { if (field.detailGroupKey === key) field.detailGroupKey = ''; });
}

/** 批量分配仅修改所选字段的归属；字段数组顺序始终保持不变。 */
export function assignDetailFields(draft, ids, key) {
	if (key && !draft.detailLayout.groups.some(group => group.key === key)) return;
	const selected = new Set(ids.map(String));
	draft.fields.forEach(field => { if (selected.has(String(field.pageColumnId))) field.detailGroupKey = key || ''; });
}

/** 停用分组时所有可编辑字段常显，分组定义和归属仍保留在草稿中。 */
export function detailLayoutPreview(draft) {
	const fields = draft.fields.filter(field => String(field.isInEdit) === '1');
	if (draft.detailLayout.mode === 'none') return { common: fields, groups: [] };
	const keys = new Set(draft.detailLayout.groups.map(group => group.key));
	return { common: fields.filter(field => !keys.has(field.detailGroupKey)),
		groups: draft.detailLayout.groups.map(group => ({ ...group, fields: fields.filter(field => field.detailGroupKey === group.key) })) };
}
