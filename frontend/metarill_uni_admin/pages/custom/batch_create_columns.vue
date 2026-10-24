<template>
	<view class="field-workbench">
		<uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="字段配置"></uni-nav-bar>
		<view class="workbench-content">
			<view class="heading"><view><text class="page-title">字段配置工作台</text><text class="description">先检查字段，再保存配置。数据库同步和代码生成由后续操作完成。</text></view><text class="table-id">表 ID：{{ config_table_id || '未指定' }}</text></view>
			<view v-if="!config_table_id" class="notice error" role="alert">缺少表 ID，请从表配置重新进入。</view>
			<view v-if="savedResult" class="saved-result" role="status">
				<text class="section-title">最近一次保存：已新增 {{ savedResult.savedCount }} 个字段</text>
				<view class="status-list"><text class="status-pill done">配置已保存</text><text class="status-pill pending">数据库待同步</text><text class="status-pill pending">代码待生成</text></view>
				<text class="description">本次保存未执行数据库同步或代码生成；以上状态不代表后台实时状态。输入已保留，避免重复保存同一批字段。</text>
			</view>
			<view class="card" id="field-source">
				<view class="section-heading"><text class="section-title">1. 粘贴字段</text><text class="muted">{{ lineCount }} 行输入</text></view>
				<text class="description">每行一组“中文名称 英文字段名”，可直接粘贴两列内容。类型和控件由服务端推断，保存前请核对解析结果。</text>
				<view class="input-wrapper" :class="{ 'input-focused': focusedRow > 0 }">
					<textarea class="field-input" :value="value" @input="onInput" :disabled="saving" :focus="inputFocus" :selection-start="selectionStart" :selection-end="selectionEnd" :maxlength="-1" placeholder="合同名称 name&#10;合同金额 amount" aria-label="待创建字段原文" @blur="inputFocus = false" />
				</view>
				<view v-if="focusedRow" class="line-location">已定位解析行 {{ focusedRow }}。<text v-if="!isJsonInput">原文：{{ focusedLine }}</text><text v-else>JSON 输入请根据解析行号检查对应数组项。</text></view>
				<view class="advanced-note"><text class="note-label">高级输入</text><text>也支持 JSON 数组，可明确填写 name、columnName、columnType、columnLength、controlKind、controlDetail、joinTable。长度或关联参数有误时，会在预检中定位。</text></view>
				<view class="actions"><button type="primary" size="mini" :loading="previewing" :disabled="!canPreview" @click="previewFields">{{ previewing ? '正在检查' : '解析并检查' }}</button><text class="muted">检查不会保存字段或修改数据库。</text></view>
				<view v-if="error" class="notice error" role="alert">{{ error }}</view>
			</view>
			<view class="card" id="field-preview">
				<view class="section-heading"><text class="section-title">2. 核对解析结果</text><text v-if="preview" class="muted">{{ preview.rows.length }} 个字段</text></view>
				<view v-if="!preview" class="empty-state">粘贴字段后，点击“解析并检查”查看逐行结果。</view>
				<template v-else>
					<view v-if="!previewFresh" class="notice pending">输入已变化或保存结果待核对，请重新检查后再保存。下方为上一次解析结果。</view>
					<view v-else-if="preview.valid" class="notice success" role="status">检查通过。请核对字段类型、长度和控件后保存。</view>
					<view v-else class="notice error" role="alert">发现 {{ preview.errorCount }} 个问题，请点击行内错误定位并修改原文，再次检查。</view>
					<scroll-view scroll-x class="preview-scroll"><view class="preview-table">
						<view class="preview-row table-head"><text class="cell row-number">行</text><text class="cell label-cell">名称</text><text class="cell name-cell">字段名</text><text class="cell type-cell">类型 / 长度</text><text class="cell control-cell">控件</text><text class="cell problem-cell">检查结果</text></view>
						<view v-for="(row, index) in preview.rows" :key="index" :id="'preview-row-' + row.rowNumber" class="preview-row" :class="{ 'row-error': row.errors.length, 'row-selected': focusedRow === row.rowNumber }">
							<text class="cell row-number">{{ row.rowNumber }}</text><text class="cell label-cell">{{ row.name || '—' }}</text><text class="cell name-cell">{{ row.columnName || '—' }}</text><text class="cell type-cell">{{ row.columnType || '—' }}{{ row.columnLength ? ' / ' + row.columnLength : '' }}</text><text class="cell control-cell">{{ row.controlKind || '—' }}</text>
							<view class="cell problem-cell"><text v-if="!row.errors.length" class="check-ok">通过</text><button v-for="(problem, problemIndex) in row.errors" :key="problemIndex" class="error-link" :disabled="!previewFresh || saving" @click="locateRow(row.rowNumber)">{{ problem.message }}<text class="error-field">{{ problem.field || problem.code }}</text></button></view>
						</view>
					</view></scroll-view>
				</template>
				<view class="save-actions"><text class="description">仅保存当前表的字段配置；保存时服务端会再次校验。</text><button type="primary" size="mini" :loading="saving" :disabled="!canSave" @click="save">{{ saving ? '正在保存' : '保存字段配置' }}</button></view>
			</view>
			<view class="card">
				<view class="section-heading"><text class="section-title">已有字段与引用</text><button size="mini" :disabled="columnsLoading || saving || !config_table_id" @click="loadColumns">刷新列表</button></view>
				<text class="description">查找字段后查看页面、控件和索引引用，修改前先确认影响范围。</text>
				<uni-easyinput v-model="search" :clearable="true" placeholder="搜索名称或字段名" aria-label="搜索已有字段" />
				<view v-if="columnsError" class="notice error" role="alert">{{ columnsError }}</view>
				<view v-if="columnsLoading" class="empty-state">正在读取已有字段…</view>
				<view v-else-if="!filteredColumns.length && !columnsError" class="empty-state">{{ search ? '没有匹配的字段' : '当前表暂无字段' }}</view>
				<view v-else class="existing-fields"><button v-for="column in filteredColumns" :key="column.id" class="field-card" :class="{ selected: selectedColumn && selectedColumn.id === column.id }" @click="loadReferences(column)"><view class="field-card-head"><text class="field-label">{{ column.name || column.columnName }}</text><text class="reference-link">查看引用</text></view><text class="field-name">{{ column.columnName }} · {{ column.columnType || '未指定类型' }}</text><text class="field-meta">{{ column.controlKind || '未指定控件' }} · ID {{ column.id }}</text></button></view>
			</view>
			<view v-if="selectedColumn" class="card references" id="field-references">
				<view class="section-heading"><text class="section-title">{{ selectedColumn.name || selectedColumn.columnName }} 的引用</text><button size="mini" @click="closeReferences">收起</button></view>
				<text class="description">字段 {{ selectedColumn.columnName }} · ID {{ selectedColumn.id }}</text>
				<view v-if="referencesLoading" class="empty-state">正在查询引用…</view>
				<view v-else-if="referencesError" class="notice error" role="alert">{{ referencesError }}<button size="mini" @click="loadReferences(selectedColumn)">重试</button></view>
				<template v-else-if="references">
					<view class="reference-group"><text class="group-title">页面字段 · {{ references.pages.length }}</text><text v-if="!references.pages.length" class="empty-inline">没有页面字段引用</text><view v-for="item in references.pages" :key="item.pageColumnId" class="reference-item"><text class="reference-title">{{ item.pageName || '未命名页面' }} / {{ item.pageColumnName || selectedColumn.name }}</text><text class="reference-detail">控件：{{ item.controlKind || '继承字段配置' }}{{ item.controlDetailOverridden ? ' · 已覆盖控件参数' : '' }}</text><text v-if="item.detailSummary" class="reference-detail">{{ item.detailSummary }}</text><text class="reference-id" selectable>页面 ID {{ item.pageId }} · 页面字段 ID {{ item.pageColumnId }}</text></view></view>
					<view class="reference-group"><text class="group-title">控件关联 · {{ references.controls.length }}</text><text v-if="!references.controls.length" class="empty-inline">没有扫描到控件关联</text><view v-for="(item, index) in references.controls" :key="index" class="reference-item"><text class="reference-title">{{ item.tableName || '配置表' }} / {{ item.columnLabel || item.columnName }}{{ item.pageName ? ' / ' + item.pageName : '' }}</text><text class="reference-detail">{{ item.description || '控件参数引用' }} · {{ item.controlKind }}</text><text class="reference-detail">参数：{{ item.referencePath }}{{ item.referencedField ? ' → ' + item.referencedField : '' }}</text><text class="reference-id" selectable>表 ID {{ item.tableId }} · 字段 ID {{ item.columnId }}{{ item.pageId ? ' · 页面 ID ' + item.pageId : '' }}{{ item.pageColumnId ? ' · 页面字段 ID ' + item.pageColumnId : '' }}</text></view></view>
					<view class="reference-group"><text class="group-title">索引 · {{ references.indexes.length }}</text><text v-if="!references.indexes.length" class="empty-inline">没有索引引用</text><view v-for="item in references.indexes" :key="item.indexColumnId" class="reference-item"><text class="reference-title">{{ item.indexName }} · {{ item.enabled ? '已启用' : '已停用' }}</text><text class="reference-detail">{{ item.columnName }} · 顺序 {{ item.sortNo }} · {{ item.sortDirection }}</text><text class="reference-id" selectable>索引 ID {{ item.indexId }} · 索引字段 ID {{ item.indexColumnId }}</text></view></view>
					<view v-if="references.coverage.length" class="coverage"><text class="note-label">查询范围</text><text v-for="(item, index) in references.coverage" :key="index" class="description">{{ item }}</text></view>
				</template>
			</view>
		</view>
	</view>
</template>

<script>
import yzp from '@/common/yzp';

export default {
	data() {
		return { config_table_id: '', value: '', revision: 0, previewRevision: -1, previewVersion: 0, preview: null, previewing: false,
			saving: false, savedRevision: -1, savedResult: null, error: '', disposed: false, previewTask: null,
			columns: [], columnsLoading: false, columnsError: '', columnsVersion: 0, columnsTask: null, search: '',
			selectedColumn: null, references: null, referencesLoading: false, referencesError: '', referencesVersion: 0, referencesTask: null,
			focusedRow: 0, inputFocus: false, selectionStart: -1, selectionEnd: -1 };
	},
	computed: {
		lineCount() { return this.value ? this.value.split(/\r?\n/).length : 0; },
		isJsonInput() { return this.value.trimStart().startsWith('['); },
		focusedLine() { return this.value.split(/\r?\n/)[this.focusedRow - 1] || ''; },
		previewFresh() { return this.previewRevision === this.revision; },
		canPreview() { return !!this.config_table_id && !!this.value.trim() && !this.previewing && !this.saving && !this.disposed; },
		canSave() { return !!this.config_table_id && !!this.preview && this.preview.valid === true && this.preview.rows.length > 0 && this.previewFresh && this.savedRevision !== this.revision && !this.previewing && !this.saving && !this.disposed; },
		filteredColumns() { const query = this.search.trim().toLowerCase(); return this.columns.filter(item => !query || String(item.name || '').toLowerCase().includes(query) || String(item.columnName || '').toLowerCase().includes(query)); }
	},
	onLoad(options) { this.config_table_id = options.config_table_id || ''; if (this.config_table_id) this.loadColumns(); },
	onUnload() {
		// 页面卸载后只取消读取；保存请求的结果可能已经生效，不能主动重发。
		this.disposed = true;
		this.previewVersion++; this.columnsVersion++; this.referencesVersion++;
		[this.previewTask, this.columnsTask, this.referencesTask].forEach(task => { if (task && task.abort) task.abort(); });
	},
	methods: {
		/** 编辑原文立即废弃预检授权，迟到响应不得恢复可保存状态。 */
		onInput(event) {
			if (this.saving) return;
			this.value = event.detail.value;
			this.revision++; this.previewVersion++; this.previewing = false; this.error = ''; this.focusedRow = 0;
			if (this.previewTask && this.previewTask.abort) this.previewTask.abort();
		},
		/** 统一表单请求，不弹通用对话框，让错误与用户输入一起保留。 */
		request(url, data, callbacks) {
			let handled = false;
			return yzp.request({ url, data, method: 'POST', sendSessionId: true, showLoading: false, showError: false,
				header: { 'content-type': 'application/x-www-form-urlencoded' }, ...callbacks,
				// 页面自己也核对时效，不能依赖底层传输永远遵守扩展守卫协议。
				success: res => { handled = true; if (callbacks.isCurrent()) callbacks.success(res); },
				fail: res => { handled = true; if (callbacks.isCurrent()) callbacks.fail(res); },
				complete: res => {
					// 公共封装可能拦截空响应或登录异常，仍需收尾界面并保留可恢复提示。
					if (!handled && callbacks.isCurrent()) callbacks.fail({ errMsg: '未收到可用的业务响应' });
					callbacks.complete(res);
				}
			});
		},
		/** 容错仅用于展示；没有明确 valid=true 的服务端结果绝不允许保存。 */
		normalizePreview(value) {
			if (!value || !Array.isArray(value.rows)) return null;
			const rows = value.rows.map(row => ({ ...row, errors: Array.isArray(row.errors) ? row.errors : [] }));
			return { ...value, valid: value.valid === true && !rows.some(row => row.errors.length), rows, errorCount: value.errorCount == null ? rows.reduce((sum, row) => sum + row.errors.length, 0) : value.errorCount };
		},
		/** 以当前输入快照预检；输入、下一次请求或页面卸载均会使响应失效。 */
		previewFields() {
			if (!this.canPreview) return;
			const version = ++this.previewVersion, revision = this.revision;
			this.previewing = true; this.error = ''; this.previewRevision = -1;
			this.previewTask = this.request('/admin/custom/index/preview_batch_create_columns', { config_table_id: this.config_table_id, content: this.value }, {
				isCurrent: () => !this.disposed && version === this.previewVersion && revision === this.revision,
				success: res => {
					const preview = this.normalizePreview(res.code == 200 ? res.data : res.map && res.map.preview);
					if (preview) { this.preview = { ...preview, valid: res.code == 200 && preview.valid }; this.previewRevision = revision; }
					if (res.code != 200 || !preview) this.error = res.msg || '字段检查失败，请修改输入后重试';
				},
				fail: () => { this.error = '无法完成字段检查，输入已保留，请检查网络后重试。'; },
				complete: () => { if (!this.disposed && version === this.previewVersion) this.previewing = false; }
			});
		},
		/** 只提交经过当前预检的原文；保存期间锁住输入和重复点击。 */
		save() {
			if (!this.canSave) return;
			const revision = this.revision;
			this.saving = true; this.error = '';
			this.request('/admin/custom/index/save_batch_create_columns', { config_table_id: this.config_table_id, content: this.value }, {
				isCurrent: () => !this.disposed,
				success: res => {
					if (res.code == 200 && res.data && Number.isInteger(res.data.savedCount)) {
						this.savedResult = res.data; this.savedRevision = revision; this.loadColumns();
						if (this.selectedColumn) this.loadReferences(this.selectedColumn);
					} else {
						this.previewRevision = -1;
						const preview = this.normalizePreview(res.map && res.map.preview);
						if (preview) { this.preview = { ...preview, valid: false }; this.previewRevision = revision; }
						this.error = res.msg || '保存结果未确认。输入已保留，请刷新已有字段并重新检查。';
					}
				},
				fail: () => { this.previewRevision = -1; this.error = '保存结果未确认，可能已在服务器生效。输入已保留，请刷新已有字段并重新检查，不要直接重复保存。'; },
				complete: () => { if (!this.disposed) this.saving = false; }
			});
		},
		/** 错误行映射到原文选择区域；JSON 使用解析项序号，避免假称物理行号。 */
		locateRow(rowNumber) {
			if (!this.previewFresh || this.saving) return;
			this.focusedRow = Number(rowNumber); this.inputFocus = false;
			if (this.isJsonInput) {
				// JSON 的数组项可能跨多行，只定位对应解析行，不猜测原文偏移。
				uni.pageScrollTo({ selector: '#preview-row-' + this.focusedRow, duration: 200 });
				return;
			}
			const lines = this.value.split('\n');
			const start = lines.slice(0, Math.max(0, this.focusedRow - 1)).reduce((sum, line) => sum + line.length + 1, 0);
			this.selectionStart = start;
			this.selectionEnd = start + (lines[this.focusedRow - 1] || '').replace(/\r$/, '').length;
			this.$nextTick(() => { if (!this.disposed) this.inputFocus = true; });
			uni.pageScrollTo({ selector: '#field-source', duration: 200 });
		},
		/** 从服务端读取本表已有字段，失败时明确提示旧列表可能过期。 */
		loadColumns() {
			if (!this.config_table_id || this.disposed) return;
			const version = ++this.columnsVersion;
			this.columnsLoading = true; this.columnsError = '';
			this.columnsTask = this.request('/admin/custom/config_column/workbench_columns', { config_table_id: this.config_table_id }, {
				isCurrent: () => !this.disposed && version === this.columnsVersion,
				success: res => { if (res.code == 200 && Array.isArray(res.data)) this.columns = res.data; else this.columnsError = res.msg || '无法读取已有字段，原列表可能已过期'; },
				fail: () => { this.columnsError = '无法读取已有字段，请检查网络后刷新；原列表可能已过期。'; },
				complete: () => { if (!this.disposed && version === this.columnsVersion) this.columnsLoading = false; }
			});
		},
		/** 切换字段先清空旧引用，通过请求序号隔离迟到结果。 */
		loadReferences(column) {
			if (!column || !column.id || this.disposed) return;
			const version = ++this.referencesVersion;
			this.selectedColumn = column; this.references = null; this.referencesLoading = true; this.referencesError = '';
			if (this.referencesTask && this.referencesTask.abort) this.referencesTask.abort();
			this.referencesTask = this.request('/admin/custom/config_column/references', { id: column.id }, {
				isCurrent: () => !this.disposed && version === this.referencesVersion,
				success: res => {
					if (res.code == 200 && res.data) this.references = { ...res.data, pages: Array.isArray(res.data.pages) ? res.data.pages : [], indexes: Array.isArray(res.data.indexes) ? res.data.indexes : [], controls: Array.isArray(res.data.controls) ? res.data.controls : [], coverage: Array.isArray(res.data.coverage) ? res.data.coverage : [] };
					else this.referencesError = res.msg || '无法读取字段引用';
				},
				fail: () => { this.referencesError = '无法读取字段引用，请检查网络后重试。'; },
				complete: () => { if (!this.disposed && version === this.referencesVersion) this.referencesLoading = false; }
			});
			this.$nextTick(() => { if (!this.disposed) uni.pageScrollTo({ selector: '#field-references', duration: 200 }); });
		},
		/** 收起引用时使在途请求失效，避免旧区域再次出现。 */
		closeReferences() { this.referencesVersion++; this.selectedColumn = null; this.references = null; this.referencesLoading = false; if (this.referencesTask && this.referencesTask.abort) this.referencesTask.abort(); }
	}
};
</script>

<style scoped>
.field-workbench { min-height: 100vh; background: #f4f6f9; color: #263445; }
.workbench-content { max-width: 1240px; margin: 0 auto; padding: 28px 24px 48px; box-sizing: border-box; }
.heading, .section-heading, .field-card-head { display: flex; justify-content: space-between; align-items: center; gap: 16px; }
.heading { align-items: flex-start; margin-bottom: 24px; }
.page-title { display: block; font-size: 25px; font-weight: 600; margin-bottom: 8px; }
.description { display: block; font-size: 14px; line-height: 23px; color: #637184; }
.table-id { font-size: 12px; color: #748093; flex-shrink: 0; margin-top: 8px; }
.card { padding: 24px; background: #fff; border: 1px solid #e3e8ef; border-radius: 12px; margin-bottom: 20px; }
.section-heading { margin-bottom: 12px; }
.section-title { font-size: 18px; font-weight: 600; }
.muted { color: #748093; font-size: 13px; line-height: 22px; }
.input-wrapper { border: 1px solid #ced7e2; border-radius: 8px; padding: 14px 16px; margin-top: 16px; background: #fcfdff; }
.input-focused { border-color: #6388bd; box-shadow: 0 0 0 2px #edf3fd; }
.field-input { width: 100%; height: 190px; font-size: 14px; line-height: 26px; font-family: Consolas, monospace; color: #263445; }
.advanced-note { display: flex; gap: 10px; margin-top: 12px; color: #718096; font-size: 12px; line-height: 21px; overflow-wrap: anywhere; }
.note-label { font-weight: 600; white-space: nowrap; }
.actions, .save-actions { display: flex; align-items: center; gap: 14px; margin-top: 20px; }
.save-actions { justify-content: space-between; padding-top: 16px; border-top: 1px solid #edf0f5; }
button { margin: 0; flex-shrink: 0; }
.notice { margin-top: 16px; padding: 12px 14px; border-radius: 7px; font-size: 14px; line-height: 23px; }
.error { background: #fff2f0; color: #a14035; }
.pending { background: #fff8e9; color: #93651d; }
.success { background: #edf7f1; color: #2f7652; }
.saved-result { padding: 20px 24px; margin-bottom: 20px; background: #f4fbf7; border: 1px solid #cde4d7; border-radius: 12px; }
.status-list { display: flex; flex-wrap: wrap; gap: 8px; margin: 14px 0; }
.status-pill { padding: 5px 10px; border-radius: 5px; font-size: 13px; }
.done { background: #dff0e5; color: #2f7652; }
.line-location { margin-top: 10px; font-size: 13px; color: #496d9a; overflow-wrap: anywhere; }
.preview-scroll { width: 100%; margin-top: 16px; }
.preview-table { min-width: 850px; border: 1px solid #e4e9ef; border-radius: 6px; overflow: hidden; }
.preview-row { display: flex; align-items: stretch; border-top: 1px solid #e8edf3; font-size: 13px; line-height: 22px; }
.table-head { border-top: 0; background: #f5f7fa; color: #5d6c7f; font-weight: 600; }
.cell { padding: 11px 12px; box-sizing: border-box; overflow-wrap: anywhere; }
.row-number { width: 50px; flex-shrink: 0; color: #738195; }
.label-cell { width: 130px; flex-shrink: 0; }
.name-cell { width: 150px; flex-shrink: 0; }
.type-cell { width: 140px; flex-shrink: 0; }
.control-cell { width: 130px; flex-shrink: 0; }
.problem-cell { flex: 1; min-width: 240px; }
.row-error { background: #fffafa; }
.row-selected { background: #fff3df; }
.check-ok { color: #36805a; }
.error-link { padding: 0; margin-bottom: 5px; color: #ad4337; text-align: left; font-size: 13px; line-height: 21px; background: transparent; border-radius: 0; white-space: normal; }
.error-link::after, .field-card::after { border: none; }
.error-link[disabled] { background: transparent; color: #9c8581; }
.error-field { display: block; color: #97746f; font-size: 11px; }
.empty-state { text-align: center; padding: 32px 12px; color: #7d8999; font-size: 14px; }
.existing-fields { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-top: 16px; }
.field-card { padding: 14px 16px; background: #fcfdff; border: 1px solid #e1e7ef; border-radius: 8px; text-align: left; line-height: 22px; min-width: 0; }
.field-card.selected { border-color: #8aa7d0; background: #f3f7fd; }
.field-card-head { gap: 8px; }
.field-label { font-size: 14px; font-weight: 600; overflow-wrap: anywhere; }
.reference-link { font-size: 12px; color: #496d9a; white-space: nowrap; }
.field-name { display: block; font-size: 13px; margin-top: 7px; color: #586b82; overflow-wrap: anywhere; }
.field-meta { display: block; font-size: 11px; color: #7e8999; overflow-wrap: anywhere; }
.reference-group { margin-top: 22px; }
.group-title { display: block; font-size: 15px; font-weight: 600; margin-bottom: 10px; }
.reference-item { padding: 13px 16px; border-left: 3px solid #dce5f0; background: #f8fafc; margin-top: 9px; border-radius: 0 6px 6px 0; overflow-wrap: anywhere; }
.reference-title { display: block; font-size: 14px; line-height: 24px; }
.reference-detail, .reference-id, .empty-inline { display: block; font-size: 13px; line-height: 22px; color: #65758a; }
.reference-id { font-size: 12px; color: #7d899a; margin-top: 5px; }
.coverage { margin-top: 24px; padding: 14px 16px; background: #f4f6f9; font-size: 13px; line-height: 23px; color: #637184; }
.coverage .description { font-size: 12px; }
@media (max-width: 900px) { .existing-fields { grid-template-columns: repeat(2, minmax(0, 1fr)); } .heading { flex-direction: column; gap: 0; } }
@media (max-width: 560px) { .workbench-content { padding: 20px 12px 32px; } .card { padding: 18px 14px; } .saved-result { padding: 18px 14px; } .existing-fields { grid-template-columns: minmax(0, 1fr); } .page-title { font-size: 22px; } .section-title { font-size: 16px; } .actions, .save-actions { align-items: flex-start; flex-direction: column; gap: 10px; } .advanced-note { flex-direction: column; gap: 0; } }
</style>
