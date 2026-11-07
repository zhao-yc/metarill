<template>
	<view class="detail-layout-page">
		<uni-nav-bar title="详情布局配置" left-icon="left" fixed statusBar :border="false" @clickLeft="back" />
		<view class="workbench">
			<view class="heading">
				<view><text class="title">{{ pageName || '详情布局' }}</text><text class="subtitle">集中维护标签和字段归属，未分组及新增字段默认在顶部常显。</text></view>
				<view class="actions"><button size="mini" :disabled="busy" @click="confirmRefresh">刷新</button><button size="mini" type="primary" :disabled="!ready || busy || conflicted || !dirty" :loading="saving" @click="save">保存布局</button></view>
			</view>
			<view v-if="error" class="notice error" role="alert">{{ error }}</view>
			<view v-if="savedMessage" class="notice success" role="status">{{ savedMessage }}</view>
			<view v-if="loading" class="empty">正在读取页面字段和布局…</view>
			<template v-if="ready">
				<view class="panel mode-row"><view><text class="panel-title">启用标签分组</text><text class="hint">停用后所有参与展示的字段常显，标签和归属会保留。</text></view><switch :checked="draft.detailLayout.mode === 'tabs'" :disabled="busy" @change="changeMode" /></view>
				<view class="editor-grid">
					<view class="panel groups-panel">
						<view class="section-heading"><text class="panel-title">标签顺序</text><button size="mini" :disabled="busy" @click="addGroup">新增标签</button></view>
						<text class="hint">标签从上到下依次显示，改名不会影响字段归属。</text>
						<view v-if="!draft.detailLayout.groups.length" class="empty small">暂无标签，可先新增标签再分配字段。</view>
						<view v-for="(group, index) in draft.detailLayout.groups" :key="group.key" class="group-row">
							<view class="group-label"><text class="ordinal">{{ index + 1 }}</text><input v-model="group.name" class="group-input" :disabled="busy" :maxlength="100" placeholder="填写标签名称" :aria-label="'第' + (index + 1) + '个标签名称'" /></view>
							<view class="group-actions"><button size="mini" :disabled="busy || index === 0" @click="moveGroup(index, -1)">上移</button><button size="mini" :disabled="busy || index === draft.detailLayout.groups.length - 1" @click="moveGroup(index, 1)">下移</button><button size="mini" class="delete-button" :disabled="busy" @click="confirmRemove(group)">删除</button></view>
						</view>
					</view>
					<view class="panel fields-panel">
						<view class="section-heading"><text class="panel-title">字段归属</text><text class="count">已选 {{ selectedIds.length }} / {{ draft.fields.length }}</text></view>
						<text class="hint">字段在常显区和标签内均保持原有顺序；未参与展示的字段也可预先配置归属。</text>
						<view class="assignment"><picker class="target-picker" :range="assignmentTargets" range-key="name" :value="targetIndex" :disabled="busy" @change="changeTarget"><view class="picker-value">分配到：{{ targetName }} <text>▾</text></view></picker><button size="mini" :disabled="busy || !selectedIds.length" @click="assignSelected">分配所选字段</button></view>
						<checkbox-group @change="selectAll"><label class="select-all"><checkbox value="all" :checked="allSelected" :disabled="busy || !draft.fields.length" /><text>全选字段</text></label></checkbox-group>
						<checkbox-group @change="selectFields"><label v-for="field in draft.fields" :key="field.pageColumnId" class="field-row"><checkbox :value="String(field.pageColumnId)" :checked="selectedIds.includes(String(field.pageColumnId))" :disabled="busy" /><view class="field-label"><text class="field-name">{{ field.name || field.columnName || '未命名字段' }}</text><text class="field-code">{{ field.columnName }}</text><text v-if="String(field.isInEdit) !== '1'" class="inactive">不参与详情展示（isInEdit ≠ 1）</text></view><text class="membership">{{ groupName(field.detailGroupKey) }}</text></label></checkbox-group>
						<view v-if="!draft.fields.length" class="empty small">当前页面没有可配置字段。</view>
					</view>
				</view>
				<view class="panel preview-panel">
					<view class="section-heading"><text class="panel-title">布局预览</text><text class="count">{{ draft.detailLayout.mode === 'tabs' ? '标签分组' : '全部常显' }}</text></view>
					<text class="hint">这里只预览标签和字段布局，不读取真实业务数据。保存后需要重新生成对应页面才会生效。</text>
					<view class="preview-section"><text class="preview-heading">{{ draft.detailLayout.mode === 'tabs' ? '顶部常显区' : '常显区' }}</text><view class="preview-fields"><view v-for="field in preview.common" :key="field.pageColumnId" class="preview-field"><text>{{ field.name || field.columnName }}</text><text class="placeholder">字段内容</text></view></view><text v-if="!preview.common.length" class="hint">暂无常显字段</text></view>
					<template v-if="preview.groups.length"><scroll-view scroll-x class="preview-tabs"><view class="tab-strip"><button v-for="group in preview.groups" :key="group.key" size="mini" :class="{ active: activePreviewKey === group.key }" @click="activePreviewKey = group.key">{{ group.name || '未命名标签' }}（{{ group.fields.length }}）</button></view></scroll-view><view v-if="activePreviewGroup" class="preview-section"><view class="preview-fields"><view v-for="field in activePreviewGroup.fields" :key="field.pageColumnId" class="preview-field"><text>{{ field.name || field.columnName }}</text><text class="placeholder">字段内容</text></view></view><text v-if="!activePreviewGroup.fields.length" class="hint">该标签没有参与展示的字段</text></view></template>
				</view>
				<text class="footer-hint">{{ dirty ? '当前有未保存的修改。' : '布局已与当前快照一致。' }} 保存仅更新布局和字段归属，不会自动生成页面。</text>
			</template>
		</view>
	</view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime';
import { detailLayoutDraft, detailLayoutPayload, detailLayoutIssue, createDetailGroupKey, removeDetailGroup, assignDetailFields, detailLayoutPreview } from '@/common/pageDetailLayoutWorkbench';

export default {
	data() { return { pageId: '', pageName: '', revision: '', draft: { detailLayout: { version: 1, mode: 'none', groups: [] }, fields: [] }, ready: false, loading: false, saving: false, error: '', savedMessage: '', baseline: '', conflicted: false, selectedIds: [], targetKey: '', activePreviewKey: '', disposed: false, requestVersion: 0, requestTask: null }; },
	computed: {
		busy() { return this.loading || this.saving; },
		dirty() { return this.ready && this.fingerprint() !== this.baseline; },
		assignmentTargets() { return [{ key: '', name: '顶部常显区' }, ...this.draft.detailLayout.groups.map(group => ({ key: group.key, name: group.name || '未命名标签' }))]; },
		targetIndex() { return Math.max(0, this.assignmentTargets.findIndex(group => group.key === this.targetKey)); },
		targetName() { return this.assignmentTargets[this.targetIndex].name; },
		allSelected() { return this.draft.fields.length > 0 && this.selectedIds.length === this.draft.fields.length; },
		preview() { return detailLayoutPreview(this.draft); },
		activePreviewGroup() { return this.preview.groups.find(group => group.key === this.activePreviewKey) || this.preview.groups[0]; }
	},
	onLoad(options) {
		// URL 只接受页面标识，权限和项目归属始终由服务端校验。
		this.pageId = options && options.pageId ? String(options.pageId) : '';
		if (!this.pageId) { this.error = '缺少页面标识，请从页面配置入口打开详情布局'; return; }
		this.loadSnapshot();
	},
	onUnload() {
		// 卸载后忽略迟到响应，避免请求完成后继续修改页面状态。
		this.disposed = true;
		this.requestVersion++;
		if (this.requestTask && this.requestTask.abort) this.requestTask.abort();
	},
	methods: {
		/** 草稿指纹只比较实际提交的布局与归属，勾选和预览不算修改。 */
		fingerprint() { return JSON.stringify(detailLayoutPayload(this.pageId, '', this.draft)); },
		/** 原子接收快照，解析失败时保留原有草稿。 */
		applySnapshot(snapshot) {
			const next = detailLayoutDraft(snapshot);
			if (!snapshot.revision) throw new Error('服务端未返回布局版本，请刷新后重试');
			this.draft = next; this.revision = snapshot.revision; this.pageName = snapshot.pageName || '';
			this.selectedIds = []; this.targetKey = ''; this.activePreviewKey = next.detailLayout.groups[0]?.key || '';
			this.ready = true; this.conflicted = false; this.baseline = this.fingerprint();
		},
		/** 独立读取快照；失败不会清空已有草稿。 */
		loadSnapshot() {
			if (this.busy || !this.pageId) return;
			const version = ++this.requestVersion;
			this.loading = true; this.error = ''; this.savedMessage = '';
			this.requestTask = yzp.request({ url: '/admin/custom/page_detail_layout/snapshot', method: 'POST', data: { pageId: this.pageId }, sendSessionId: true, showError: false,
				isCurrent: () => !this.disposed && version === this.requestVersion,
				success: res => { if (res.code == 200 && res.data) { try { this.applySnapshot(res.data); } catch (error) { this.error = error.message; } } else this.error = res.msg || '无法读取详情布局，请刷新重试'; },
				fail: () => { this.error = '无法读取详情布局，请检查网络后刷新；已有草稿仍保留'; },
				complete: () => { if (!this.disposed && version === this.requestVersion) this.loading = false; }
			});
		},
		/** 刷新会替换草稿，存在修改时先明确确认。 */
		confirmRefresh() { if (this.busy) return; if (!this.dirty) { this.loadSnapshot(); return; } uni.showModal({ title: '刷新详情布局', content: '刷新成功后将替换当前未保存的修改，是否继续？', success: result => { if (result.confirm && !this.disposed) this.loadSnapshot(); } }); },
		/** 启停只切换模式，完整保留分组及字段归属。 */
		changeMode(event) { if (!this.busy) this.draft.detailLayout.mode = event.detail.value ? 'tabs' : 'none'; },
		/** 新标签使用自动标识，用户只需填写展示名称。 */
		addGroup() { if (this.busy) return; const key = createDetailGroupKey(this.draft.detailLayout.groups); this.draft.detailLayout.groups.push({ key, name: '' }); this.activePreviewKey = key; },
		/** 标签顺序与字段顺序独立，只移动指定标签。 */
		moveGroup(index, delta) { if (this.busy) return; const groups = this.draft.detailLayout.groups; const next = index + delta; if (next < 0 || next >= groups.length) return; const group = groups.splice(index, 1)[0]; groups.splice(next, 0, group); },
		/** 删除前告知字段回到常显区，确认后只修改当前草稿。 */
		confirmRemove(group) { if (this.busy) return; uni.showModal({ title: '删除标签', content: '删除“' + (group.name || '未命名标签') + '”后，其字段将回到顶部常显区。', success: result => { if (result.confirm && !this.disposed && !this.busy) { removeDetailGroup(this.draft, group.key); if (this.targetKey === group.key) this.targetKey = ''; if (this.activePreviewKey === group.key) this.activePreviewKey = this.draft.detailLayout.groups[0]?.key || ''; } } }); },
		/** 勾选仅用于批量操作，不直接改变字段归属。 */
		selectFields(event) { if (!this.busy) this.selectedIds = event.detail.value.map(String); },
		/** 全选覆盖页面全部字段，含当前未参与展示的字段。 */
		selectAll(event) { if (!this.busy) this.selectedIds = event.detail.value.includes('all') ? this.draft.fields.map(field => String(field.pageColumnId)) : []; },
		/** 选择分配目标时保存稳定标识，避免标签移动导致目标错位。 */
		changeTarget(event) { if (!this.busy) this.targetKey = this.assignmentTargets[Number(event.detail.value)]?.key || ''; },
		/** 分配后保留勾选，方便继续调整；停用分组时也可预先维护归属。 */
		assignSelected() { if (this.busy) return; assignDetailFields(this.draft, this.selectedIds, this.targetKey); },
		/** 未分组或未知归属显示常显，新增字段无需额外分配。 */
		groupName(key) { return this.draft.detailLayout.groups.find(group => group.key === key)?.name || (key ? '未命名标签' : '顶部常显区'); },
		/** 保存采用当前快照版本；冲突后禁止再次提交，必须显式刷新。 */
		save() {
			if (!this.ready || this.busy || this.conflicted || !this.dirty) return;
			const issue = detailLayoutIssue(this.draft);
			if (issue) { this.error = issue; return; }
			const version = ++this.requestVersion;
			this.saving = true; this.error = ''; this.savedMessage = '';
			this.requestTask = yzp.request({ url: '/admin/custom/page_detail_layout/save', method: 'POST', data: detailLayoutPayload(this.pageId, this.revision, this.draft), sendSessionId: true, showError: false,
				isCurrent: () => !this.disposed && version === this.requestVersion,
				success: res => {
					if (res.code == 200) {
						if (res.data && Array.isArray(res.data.fields)) { try { this.applySnapshot(res.data); } catch (error) { this.error = '布局已保存，但读取新版本失败，请刷新后继续编辑'; this.conflicted = true; } }
						else if (res.data && res.data.revision) { this.revision = res.data.revision; this.baseline = this.fingerprint(); }
						else { this.conflicted = true; this.error = '布局已保存，请刷新取得新版本后再继续编辑'; }
						this.savedMessage = '布局已保存，请重新生成对应页面后查看效果。';
					} else {
						const message = res.msg || '保存失败';
							if (res.code == 409 || /revision|版本冲突|版本已|已变更|已被修改|已发生变化|已变化/i.test(message)) { this.conflicted = true; this.error = '页面配置已被其他操作修改，当前草稿已保留。请先核对草稿，再点击刷新读取最新配置并重新调整、保存。'; }
						else this.error = message + '；当前草稿已保留';
					}
				},
				fail: () => { this.error = '保存结果未确认，当前草稿已保留。请刷新核对服务端状态后再保存'; this.conflicted = true; },
				complete: () => { if (!this.disposed && version === this.requestVersion) this.saving = false; }
			});
		},
		/** 有未保存修改时提示返回将丢弃草稿。 */
		back() { if (this.busy) return; if (!this.dirty) { yzp.back(); return; } uni.showModal({ title: '离开布局配置', content: '当前修改尚未保存，是否离开？', success: result => { if (result.confirm && !this.disposed) yzp.back(); } }); }
	}
};
</script>

<style lang="scss" scoped>
.detail-layout-page { min-height: calc(100vh - var(--top-window-height, 0px)); background: #f4f6f8; color: #263445; }
.workbench { max-width: 1320px; margin: 0 auto; padding: 24px; box-sizing: border-box; }
.heading, .actions, .section-heading, .mode-row, .group-label, .group-actions, .assignment, .field-row, .select-all { display: flex; align-items: center; gap: 12px; }
.heading, .section-heading, .mode-row { justify-content: space-between; }
.heading { margin-bottom: 20px; align-items: flex-start; }
.title { display: block; font-size: 24px; font-weight: 600; line-height: 34px; }
.subtitle, .hint, .footer-hint { display: block; color: #657386; font-size: 13px; line-height: 22px; }
.subtitle { margin-top: 6px; }
button { margin: 0; flex-shrink: 0; }
.actions { flex-shrink: 0; }
.panel { padding: 20px; background: #fff; border: 1px solid #e0e6ee; border-radius: 12px; box-sizing: border-box; margin-bottom: 18px; min-width: 0; }
.panel-title { display: block; font-size: 17px; font-weight: 600; }
.section-heading { margin-bottom: 8px; }
.count { color: #657386; font-size: 12px; }
.editor-grid { display: grid; grid-template-columns: minmax(280px, 0.8fr) minmax(0, 1.5fr); gap: 18px; align-items: start; }
.group-row { padding: 14px 0; border-bottom: 1px solid #eef1f5; }
.group-label { gap: 8px; }
.ordinal { font-size: 12px; background: #edf3ff; color: #3863a4; width: 24px; line-height: 24px; text-align: center; border-radius: 5px; flex-shrink: 0; }
.group-input { flex: 1; min-width: 0; font-size: 14px; height: 36px; border: 1px solid #d7dfe8; border-radius: 6px; padding: 0 9px; }
.group-actions { margin-top: 10px; justify-content: flex-end; gap: 7px; }
.delete-button { color: #b34640; }
.assignment { margin-top: 14px; padding: 12px; background: #f5f7fa; border-radius: 8px; flex-wrap: wrap; }
.target-picker { flex: 1; min-width: 160px; }
.picker-value { display: flex; justify-content: space-between; gap: 12px; padding: 8px 10px; background: #fff; border: 1px solid #d7dfe8; border-radius: 6px; font-size: 13px; overflow-wrap: anywhere; }
.select-all { padding: 14px 0 10px; color: #59697d; font-size: 13px; }
checkbox { transform: scale(0.82); flex-shrink: 0; }
.field-row { border-top: 1px solid #edf0f4; padding: 12px 0; align-items: flex-start; gap: 8px; }
.field-label { flex: 1; min-width: 0; }
.field-name { display: block; font-size: 14px; overflow-wrap: anywhere; line-height: 23px; }
.field-code, .inactive { display: block; font-size: 12px; color: #788698; line-height: 20px; overflow-wrap: anywhere; }
.inactive { color: #a86a22; }
.membership { max-width: 32%; color: #47628a; border-radius: 4px; background: #eef3fa; font-size: 12px; padding: 4px 7px; overflow-wrap: anywhere; }
.notice { padding: 13px 16px; margin-bottom: 18px; border-radius: 8px; font-size: 14px; line-height: 24px; }
.error { color: #a23831; background: #fff0ee; }
.success { color: #247747; background: #eaf7ef; }
.empty { padding: 32px 12px; text-align: center; color: #788698; font-size: 14px; }
.empty.small { padding: 22px 0; }
.preview-panel { margin-top: 0; }
.preview-section { padding: 18px 0 8px; }
.preview-heading { font-size: 14px; font-weight: 600; display: block; margin-bottom: 12px; }
.preview-fields { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px 20px; }
.preview-field { min-width: 0; display: flex; flex-direction: column; gap: 6px; font-size: 13px; overflow-wrap: anywhere; }
.placeholder { display: block; color: #9aa5b3; padding: 9px 11px; background: #f6f8fb; border: 1px solid #e5eaf0; border-radius: 5px; }
.preview-tabs { width: 100%; margin-top: 16px; border-bottom: 1px solid #e4e9f0; white-space: nowrap; }
.tab-strip { display: inline-flex; gap: 8px; padding-bottom: 10px; }
.tab-strip button { background: #f3f5f8; color: #627289; }
.tab-strip button.active { background: #e9f0ff; color: #2f62ad; }
.footer-hint { margin-bottom: 20px; }
@media (max-width: 900px) { .editor-grid { grid-template-columns: 1fr; gap: 0; } .preview-fields { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 540px) { .workbench { padding: 18px 12px; } .heading { flex-direction: column; } .actions { width: 100%; justify-content: flex-end; } .title { font-size: 21px; } .panel { padding: 16px 12px; } .mode-row { gap: 10px; } .preview-fields { grid-template-columns: 1fr; } .membership { max-width: 28%; } }
</style>
