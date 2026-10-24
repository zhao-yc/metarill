import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const helperSource = await readFile(new URL('../common/pageDetailLayoutWorkbench.js', import.meta.url), 'utf8');
const pageSource = await readFile(new URL('../pages/custom/page_detail_layout.vue', import.meta.url), 'utf8');
const helper = await import('data:text/javascript;base64,' + Buffer.from(helperSource).toString('base64'));

/** 快照故意交错归属及隐藏字段，用于验证分组不会改变字段顺序。 */
function snapshot(revision = '版本一') {
	return { pageId: '页面甲', pageName: '订单', revision,
		detailLayout: { version: 1, mode: 'tabs', groups: [{ key: 'group_a', name: '基础' }, { key: 'group_b', name: '扩展' }] },
		fields: [
			{ pageColumnId: '字段甲', columnId: '列甲', columnName: 'a', name: '甲', isInEdit: 1, detailGroupKey: 'group_b' },
			{ pageColumnId: '字段乙', columnId: '列乙', columnName: 'b', name: '乙', isInEdit: '1', detailGroupKey: '' },
			{ pageColumnId: '字段丙', columnId: '列丙', columnName: 'c', name: '丙', isInEdit: 0, detailGroupKey: 'group_a' },
			{ pageColumnId: '字段丁', columnId: '列丁', columnName: 'd', name: '丁', isInEdit: 1, detailGroupKey: 'group_b' }
		] };
}

/** 用真实页面方法和计算属性模拟网络回调，只替换宿主组件与请求。 */
function fixture() {
	const calls = [], dialogs = [];
	let backed = 0;
	const context = vm.createContext({ uni: { showModal: options => dialogs.push(options) },
		yzp: { request: options => { calls.push(options); return { abort() {} }; }, back: () => backed++ } });
	vm.runInContext(helperSource.replace(/export /g, ''), context);
	vm.runInContext(pageSource.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/^import .*$/gm, '').replace('export default', 'globalThis.definition ='), context);
	const definition = context.definition, page = definition.data();
	Object.entries(definition.methods).forEach(([name, method]) => { page[name] = method.bind(page); });
	Object.entries(definition.computed).forEach(([name, getter]) => { Object.defineProperty(page, name, { get: getter.bind(page) }); });
	const respond = (index, response) => { if (calls[index].isCurrent()) calls[index].success(response); calls[index].complete(); };
	return { page, calls, dialogs, respond, load: options => definition.onLoad.call(page, options), unload: () => definition.onUnload.call(page), backed: () => backed };
}

test('新增或未知归属默认常显，预览过滤不展示字段并保持原有顺序', () => {
	const value = snapshot(); value.fields.push({ pageColumnId: '字段戊', isInEdit: 1, detailGroupKey: '已删除标签' });
	const draft = helper.detailLayoutDraft(value);
	assert.equal(draft.fields[4].detailGroupKey, '');
	assert.deepEqual(helper.detailLayoutPreview(draft).common.map(field => field.pageColumnId), ['字段乙', '字段戊']);
	assert.deepEqual(helper.detailLayoutPreview(draft).groups[1].fields.map(field => field.pageColumnId), ['字段甲', '字段丁']);
	assert.deepEqual(helper.detailLayoutPreview(draft).groups[0].fields, []);
	assert.equal(value.fields[4].detailGroupKey, '已删除标签');
});

test('停用保留定义和归属，再启用恢复分组；删除标签字段回常显', () => {
	const draft = helper.detailLayoutDraft(snapshot());
	draft.detailLayout.mode = 'none';
	assert.deepEqual(helper.detailLayoutPreview(draft).common.map(field => field.pageColumnId), ['字段甲', '字段乙', '字段丁']);
	assert.equal(draft.fields[0].detailGroupKey, 'group_b');
	draft.detailLayout.mode = 'tabs';
	helper.removeDetailGroup(draft, 'group_b');
	assert.deepEqual(draft.fields.map(field => field.pageColumnId), ['字段甲', '字段乙', '字段丙', '字段丁']);
	assert.equal(draft.fields[0].detailGroupKey, '');
	assert.equal(draft.fields[3].detailGroupKey, '');
});

test('批量分配不重排，保存只包含布局与页面字段归属，名称校验生效', () => {
	const draft = helper.detailLayoutDraft(snapshot());
	helper.assignDetailFields(draft, ['字段甲', '字段丙'], 'group_a');
	helper.assignDetailFields(draft, ['字段丁'], '不存在');
	const payload = helper.detailLayoutPayload('页面甲', '版本一', draft);
	assert.deepEqual(payload.fields, [
		{ pageColumnId: '字段甲', detailGroupKey: 'group_a' }, { pageColumnId: '字段乙', detailGroupKey: '' },
		{ pageColumnId: '字段丙', detailGroupKey: 'group_a' }, { pageColumnId: '字段丁', detailGroupKey: 'group_b' }
	]);
	assert.equal(helper.detailLayoutIssue(draft), '');
	draft.detailLayout.groups[0].name = ' 扩展 ';
	assert.match(helper.detailLayoutIssue(draft), /重复/);
	draft.detailLayout.groups[0].name = ' ';
	assert.match(helper.detailLayoutIssue(draft), /填写/);
	draft.detailLayout.groups[0].name = '字'.repeat(101);
	assert.match(helper.detailLayoutIssue(draft), /100/);
	assert.match(helper.createDetailGroupKey(draft.detailLayout.groups), /^[A-Za-z][A-Za-z0-9_-]{0,63}$/);
});

test('页面拒绝缺失标识与不支持版本，加载失败不覆盖已有草稿', () => {
	const f = fixture(); f.load({}); assert.equal(f.calls.length, 0); assert.match(f.page.error, /缺少/);
	f.load({ pageId: '页面甲' }); f.respond(0, { code: 200, data: snapshot() });
	f.page.draft.detailLayout.groups[0].name = '草稿名';
	f.page.confirmRefresh(); assert.equal(f.dialogs.length, 1); f.dialogs[0].success({ confirm: true });
	f.respond(1, { code: 200, data: { ...snapshot(), detailLayout: { version: 2, mode: 'tabs', groups: [] } } });
	assert.equal(f.page.draft.detailLayout.groups[0].name, '草稿名');
	assert.match(f.page.error, /暂不支持/);
});

test('版本冲突保留草稿并阻止重复保存，显式刷新确认后使用最新版本', () => {
	const f = fixture(); f.load({ pageId: '页面甲' }); f.respond(0, { code: 200, data: snapshot() });
	f.page.draft.detailLayout.groups[0].name = '草稿名'; f.page.save();
	assert.equal(f.calls[1].data.revision, '版本一');
	f.respond(1, { code: 500, msg: '布局已变化，请刷新后重试' });
	assert.equal(f.page.draft.detailLayout.groups[0].name, '草稿名');
	assert.equal(f.page.conflicted, true); assert.match(f.page.error, /草稿已保留/);
	f.page.save(); assert.equal(f.calls.length, 2);
	f.page.confirmRefresh(); f.dialogs[0].success({ confirm: true }); f.respond(2, { code: 200, data: snapshot('版本二') });
	assert.equal(f.page.revision, '版本二'); assert.equal(f.page.conflicted, false); assert.equal(f.page.dirty, false);
});

test('普通失败保留草稿；成功采用新快照并提示重新生成，不触发生成请求', () => {
	const f = fixture(); f.load({ pageId: '页面甲' }); f.respond(0, { code: 200, data: snapshot() });
	f.page.draft.detailLayout.groups[0].name = '新名称'; f.page.save();
	f.respond(1, { code: 500, msg: '没有权限' });
	assert.equal(f.page.dirty, true); assert.equal(f.page.conflicted, false);
	f.page.save(); const saved = snapshot('版本二'); saved.detailLayout.groups[0].name = '新名称';
	f.respond(2, { code: 200, data: saved });
	assert.equal(f.page.dirty, false); assert.equal(f.page.revision, '版本二'); assert.match(f.page.savedMessage, /重新生成/);
	assert.deepEqual(f.calls.map(call => call.url), ['/admin/custom/page_detail_layout/snapshot', '/admin/custom/page_detail_layout/save', '/admin/custom/page_detail_layout/save']);
});

test('网络结果不明需刷新核对，卸载后的迟到回调不写入页面', () => {
	const f = fixture(); f.load({ pageId: '页面甲' }); f.respond(0, { code: 200, data: snapshot() });
	f.page.draft.detailLayout.groups[0].name = '新名称'; f.page.save(); f.calls[1].fail(); f.calls[1].complete();
	assert.equal(f.page.conflicted, true); assert.equal(f.page.dirty, true);
	f.page.confirmRefresh(); f.dialogs[0].success({ confirm: true }); f.unload();
	f.respond(2, { code: 200, data: snapshot('迟到版本') });
	assert.equal(f.page.revision, '版本一'); assert.equal(f.page.draft.detailLayout.groups[0].name, '新名称');
});
