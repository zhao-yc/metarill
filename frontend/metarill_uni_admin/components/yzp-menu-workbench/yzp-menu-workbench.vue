<template>
	<view class="menu-workbench">
		<view class="workbench-heading">
			<view><text class="workbench-title">菜单配置</text><text class="workbench-description">为项目组织导航入口，选择页面即可完成关联。</text></view>
			<button class="quiet-button" size="mini" :disabled="busy" @click="refresh">刷新配置</button>
		</view>
		<view class="context-bar">
			<view class="context-field"><text class="field-label">项目</text>
				<zxz-uni-data-select :key="'project-' + selectorVersion" :model-value="projectId" :localdata="projects" dataKey="name" dataValue="id"
					filterable :clear="false" :disabled="busy" placeholder="选择项目" @change="changeProject" />
			</view>
			<view class="context-field"><text class="field-label">后台类型</text>
				<zxz-uni-data-select :key="'admin-' + selectorVersion" :model-value="adminTypeId" :localdata="adminTypes" dataKey="name" dataValue="id"
					:clear="false" :disabled="busy || !snapshot" placeholder="选择后台类型" @change="changeAdmin" />
			</view>
			<text v-if="snapshot" class="scope-note">{{ snapshot.canWrite ? '按节点保存' : '仅查看' }} · {{ flatNodes.length }} 个菜单</text>
		</view>
		<view v-if="loading" class="workbench-state"><text class="state-title">正在读取菜单配置…</text></view>
		<view v-else-if="loadError" class="workbench-state error-state">
			<text class="state-title">{{ selectedProject && selectedProject.isV8 === false ? '此项目继续使用原菜单配置' : '暂时无法读取菜单' }}</text>
			<text>{{ loadError }}</text><button size="mini" class="quiet-button" @click="refresh">重新读取</button>
		</view>
		<view v-else-if="!snapshot" class="workbench-state"><text class="state-title">选择项目，开始配置菜单</text><text>暂无可访问项目时，请先确认账号的项目配置。</text></view>
		<view v-else-if="!adminTypeId" class="workbench-state"><text class="state-title">这个项目还没有后台类型</text><text>配置后台类型后，即可为它创建菜单。</text></view>
		<view v-else class="workbench-layout">
			<view class="tree-panel">
				<view class="panel-heading"><text>导航结构</text><text class="tree-total">{{ flatNodes.length }}</text></view>
				<view class="tree-tools">
					<button size="mini" class="quiet-button" :disabled="busy || !allowed('first', 'create')" @click="addNode('FIRST_MENU')">＋ 分组</button>
					<button size="mini" class="quiet-button" :disabled="busy || !allowed('second', 'create') || !flatNodes.length" @click="addNode('GENERATED_PAGE')">＋ 菜单</button>
				</view>
				<input v-model="search" class="tree-search" placeholder="查找菜单" aria-label="查找菜单" />
				<scroll-view scroll-y class="tree-scroll">
					<view v-if="!visibleNodes.length" class="tree-empty">{{ search ? '没有匹配的菜单' : '还没有菜单，从新建分组开始。' }}</view>
					<view v-for="node in visibleNodes" :key="node.id" class="tree-row" :class="{ selected: draft && draft.id === node.id, 'first-row': node.isFirst }"
						:style="{ paddingLeft: (12 + node.depth * 18) + 'px' }" @click="selectNode(node)">
						<button v-if="node.children && node.children.length" class="branch-toggle" :aria-label="collapsed.includes(node.id) ? '展开' : '收起'" @click.stop="toggleBranch(node.id)">{{ collapsed.includes(node.id) ? '›' : '⌄' }}</button>
						<text v-else class="node-mark">{{ node.kind === 'DIRECTORY' ? '◇' : '·' }}</text>
						<view class="node-text"><text>{{ node.name }}</text><text v-if="search" class="node-path">{{ node.path.slice(0, -1).join(' / ') }}</text></view>
						<text v-if="node.hidden" class="node-tag">隐藏</text><text v-else-if="node.kind === 'DIRECTORY' && !node.isFirst" class="node-tag">目录</text>
					</view>
				</scroll-view>
				<view class="tree-footnote">分组 → 目录 / 页面，最多三级。</view>
			</view>
			<view class="editor-panel">
				<view v-if="!draft" class="editor-placeholder"><text class="placeholder-mark">＋</text><text class="state-title">选择一个菜单，查看或编辑</text><text>也可以从左侧新建分组和页面入口。</text></view>
				<template v-else>
					<view class="editor-heading"><view><text class="editor-kicker">{{ draft.id ? '编辑' : '新建' }}{{ kindLabel }}</text><text class="editor-title">{{ draft.name || '未命名菜单' }}</text></view><text v-if="dirty" class="dirty-tag">未保存</text></view>
					<view v-if="recovered" class="notice" :class="{ 'error-notice': staleDraft }">{{ staleDraft ? '配置已被更新，旧草稿仍保留供核对。请放弃草稿后，从最新配置重新编辑。' : '已恢复未保存的编辑，可继续修改或放弃。' }}</view>
					<view v-if="mutationError" class="notice error-notice">{{ mutationError }}</view>
					<view v-if="!canEdit && !staleDraft" class="notice">当前账号可以查看此菜单，暂不能修改。</view>
					<view class="editor-form">
						<view class="form-field"><text class="field-label">名称 <text class="required">*</text></text><input v-model="draft.name" :disabled="editorDisabled" maxlength="50" class="text-input" placeholder="例如：订单管理" /></view>
						<view v-if="draft.kind !== 'FIRST_MENU'" class="form-field"><text class="field-label">上级位置 <text class="required">*</text></text>
							<zxz-uni-data-select v-model="draft.parentId" :localdata="parentOptions" dataKey="text" dataValue="value" :clear="false" :disabled="editorDisabled" placeholder="选择分组或目录" />
							<text v-if="selectedNode && selectedNode.children.length" class="field-hint">带子项的目录可在当前分组内调整，跨分组请先移动子项。</text>
						</view>
						<view v-if="draft.kind !== 'FIRST_MENU'" class="form-field"><text class="field-label">菜单类型</text>
							<view class="kind-options"><button v-for="option in kindOptions" :key="option.value" size="mini" :class="{ active: draft.kind === option.value }"
								:disabled="editorDisabled || hasChildren" @click="changeKind(option.value)">{{ option.text }}</button></view>
						</view>
						<view v-if="['GENERATED_PAGE', 'CUSTOM_PAGE'].includes(draft.kind)" class="form-field"><text class="field-label">目标页面 <text class="required">*</text></text>
							<zxz-uni-data-select v-model="draft.targetPageId" :localdata="targetOptions" dataKey="name" dataValue="id" filterable :clear="false" :disabled="editorDisabled" placeholder="搜索并选择页面" @change="changeTarget" />
							<text class="field-hint">{{ targetOptions.length ? '仅列出当前后台可用的页面。' : '暂无可选择页面，请先完成页面配置。' }}</text>
							<view v-if="selectedTarget" class="target-preview"><text>{{ selectedTarget.name }}</text><text class="target-route">{{ selectedTarget.route }}</text></view>
						</view>
						<view v-if="draft.kind === 'LEGACY_PAGE'" class="notice">此入口保留原页面地址。重新选择页面类型后，可关联已配置的页面。</view>
						<view class="form-columns"><view class="form-field"><text class="field-label">排序</text><input v-model="draft.sortNo" :disabled="editorDisabled" type="number" class="text-input" /><text class="field-hint">数字越小，位置越靠前。</text></view>
							<view class="form-field"><text class="field-label">图标</text><input v-model="draft.icon" :disabled="editorDisabled" maxlength="50" class="text-input" placeholder="可选，图标名称" /></view></view>
						<view v-if="draft.kind === 'FIRST_MENU' && draft.hidden" class="notice">此分组保留原有隐藏设置，显示效果沿用项目现有规则。</view>
						<view v-if="draft.kind === 'GENERATED_PAGE' && selectedTarget && selectedTarget.showCountSupported" class="switch-field"><view><text class="field-label">显示数量角标</text><text class="field-hint">在菜单旁显示当前用户可查看的数据数量。</text></view><switch :checked="draft.showCount" :disabled="editorDisabled" color="#245a81" @change="draft.showCount = $event.detail.value" /></view>
					</view>
					<view class="editor-actions"><button v-if="draft.id && canDelete" size="mini" class="delete-button" :disabled="busy" @click="removeNode">删除菜单</button><view class="action-spacer"></view><button size="mini" class="quiet-button" :disabled="busy || !dirty" @click="discard">放弃修改</button><button v-if="canEdit" size="mini" class="save-button" :disabled="busy || !dirty || staleDraft" @click="save">{{ saving ? '正在保存…' : '保存菜单' }}</button></view>
				</template>
			</view>
		</view>
	</view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime'
import { flattenMenuTree, createMenuDraft, menuDraftFingerprint, menuParentOptions, validateMenuDraft,
	buildMenuSaveRequest, createWorkbenchRequestLane, readWorkbenchIdentity } from '@/common/menuWorkbench'

const kindNames = { FIRST_MENU: '分组', DIRECTORY: '目录', GENERATED_PAGE: '生成页面', CUSTOM_PAGE: '自定义页面', LEGACY_PAGE: '历史入口' }

export default {
	props: { initialProjectId: { type: String, default: '' } },
	emits: ['dirty-change', 'saved'],
	data() {
		return { projects: [], projectId: '', adminTypeId: '', snapshot: null, loading: true, saving: false,
			loadError: '', mutationError: '', draft: null, baseline: '', search: '', collapsed: [], recovered: false, staleDraft: false, selectorVersion: 0 }
	},
	computed: {
		busy() { return this.loading || this.saving },
		selectedProject() { return this.projects.find(item => String(item.id) === this.projectId) },
		adminTypes() { return this.snapshot ? this.snapshot.adminTypes || [] : [] },
		flatNodes() { return flattenMenuTree(this.snapshot && this.snapshot.tree) },
		selectedNode() { return this.draft && this.flatNodes.find(item => item.id === this.draft.id) },
		hasChildren() { return !!(this.selectedNode && this.selectedNode.children && this.selectedNode.children.length) },
		dirty() { return !!this.draft && menuDraftFingerprint(this.draft) !== this.baseline },
		kindLabel() { return this.draft ? kindNames[this.draft.kind] || '菜单' : '' },
		parentOptions() { return this.draft ? menuParentOptions(this.snapshot.tree, this.draft) : [] },
		targetOptions() { return (this.snapshot && this.snapshot.targets || []).filter(item => this.draft && item.kind === this.draft.kind) },
		selectedTarget() { return this.targetOptions.find(item => String(item.id) === this.draft.targetPageId) },
		canEdit() { return !!this.draft && this.allowed(this.draft.kind === 'FIRST_MENU' ? 'first' : 'second', this.draft.id ? 'update' : 'create') },
		canDelete() { return !!this.draft && this.allowed(this.draft.kind === 'FIRST_MENU' ? 'first' : 'second', 'delete') },
		editorDisabled() { return this.busy || !this.canEdit || this.staleDraft },
		kindOptions() {
			const options = [{ value: 'DIRECTORY', text: '目录' }, { value: 'GENERATED_PAGE', text: '生成页面' }, { value: 'CUSTOM_PAGE', text: '自定义页面' }]
			if (this.selectedNode && this.selectedNode.kind === 'LEGACY_PAGE') options.push({ value: 'LEGACY_PAGE', text: '历史入口' })
			return options
		},
		/** 搜索直接展示完整匹配路径，折叠只影响普通浏览。 */
		visibleNodes() {
			const query = this.search.trim().toLowerCase()
			if (query) return this.flatNodes.filter(node => node.path.join(' / ').toLowerCase().includes(query))
			const hidden = new Set()
			return this.flatNodes.filter(node => {
				if (hidden.has(String(node.parentId))) { hidden.add(node.id); return false }
				if (this.collapsed.includes(node.id)) hidden.add(node.id)
				return true
			})
		}
	},
	watch: {
		dirty(value) { this.$emit('dirty-change', value) },
		draft: { deep: true, handler() { this.persistDraft() } }
	},
	/** 请求通道与页面事件保持实例私有，组件卸载时一并失效。 */
	created() {
		const context = { getContext: () => readWorkbenchIdentity(key => uni.getStorageSync(key)), onContextChanged: () => this.invalidateIdentity() }
		this._catalogLane = createWorkbenchRequestLane(options => yzp.request(options), context)
		this._snapshotLane = createWorkbenchRequestLane(options => yzp.request(options), context)
		this._mutationLane = createWorkbenchRequestLane(options => yzp.request(options), context)
		this._loadVersion = 0
		this._contextChanged = () => { this.persistDraft(); this.initialize() }
		this._loggedOut = () => this.invalidateIdentity()
		uni.$on('onChangeProject', this._contextChanged)
		uni.$on('onLoginLoadMenu', this._contextChanged)
		uni.$on('onLogout', this._loggedOut)
		// #ifdef H5
		this._beforeUnload = event => { if (this.dirty || this.saving) { event.preventDefault(); event.returnValue = '' } }
		this._storageChanged = () => this.ensureIdentity()
		window.addEventListener('beforeunload', this._beforeUnload)
		window.addEventListener('storage', this._storageChanged)
		// #endif
		this.initialize()
	},
	/** 草稿按项目、后台和账号隔离，页面离开后再次进入仍可恢复。 */
	beforeUnmount() {
		this.persistDraft()
		this._loadVersion++
		this._catalogLane.invalidate(); this._snapshotLane.invalidate(); this._mutationLane.invalidate()
		uni.$off('onChangeProject', this._contextChanged)
		uni.$off('onLoginLoadMenu', this._contextChanged)
		uni.$off('onLogout', this._loggedOut)
		// #ifdef H5
		window.removeEventListener('beforeunload', this._beforeUnload)
		window.removeEventListener('storage', this._storageChanged)
		// #endif
	},
	methods: {
		/** 能力由服务端返回；未知权限一律只读。 */
		allowed(group, action) { return !!(this.snapshot && this.snapshot.isV8 === true && this.snapshot.canWrite && this.snapshot.capabilities && this.snapshot.capabilities[group] && this.snapshot.capabilities[group][action]) },
		/** 项目列表来自当前认证身份的可访问范围，客户端不自行拼接项目。 */
		async initialize() {
			const version = ++this._loadVersion
			this._snapshotLane.invalidate(); this._mutationLane.invalidate()
			this._draftOwnerId = String((uni.getStorageSync('userInfo') || {}).id || '')
			this._editIdentity = readWorkbenchIdentity(key => uni.getStorageSync(key))
			this._identityInvalid = false
			this.loading = true; this.saving = false; this.snapshot = null; this.draft = null; this.loadError = ''; this.mutationError = ''
			try {
				const result = await this._catalogLane.run('/projects')
				if (!result.current || version !== this._loadVersion) return
				this.projects = (result.data || []).map(item => ({ ...item, id: String(item.id) }))
				const current = this.initialProjectId || (uni.getStorageSync('sysParams') || {}).yzProjectId || this.projectId
				this.projectId = this.projects.some(item => item.id === String(current)) ? String(current) : (this.projects[0] || {}).id || ''
				this.adminTypeId = ''
				if (this.projectId) await this.loadSnapshot('')
				else this.loading = false
			} catch (error) { if (version === this._loadVersion) { this.loadError = error.message; this.loading = false } }
		},
		/** 登出或身份先于事件变化时立即清空编辑区，禁止把旧草稿写回新账号。 */
		invalidateIdentity() {
			this._identityInvalid = true; this._loadVersion++
			this._catalogLane.invalidate(); this._snapshotLane.invalidate(); this._mutationLane.invalidate()
			this.snapshot = null; this.draft = null; this.projects = []; this.projectId = ''; this.adminTypeId = ''
			this.loading = false; this.saving = false; this.loadError = '登录状态或项目上下文已变化，请重新读取菜单配置。'
		},
		/** 提交前也核对创建编辑区时的身份，防止新账号替旧账号提交尚未刷新的草稿。 */
		ensureIdentity() {
			if (this._identityInvalid) return false
			if (readWorkbenchIdentity(key => uni.getStorageSync(key)) === this._editIdentity) return true
			this.invalidateIdentity()
			return false
		},
		/** 切换项目、后台或刷新时清除旧快照，过期响应不能写回新编辑区。 */
		async loadSnapshot(adminTypeId = this.adminTypeId) {
			const version = ++this._loadVersion
			this.loading = true; this.loadError = ''; this.mutationError = ''; this.snapshot = null; this.draft = null
			try {
				const result = await this._snapshotLane.run('/snapshot', { projectId: this.projectId, adminTypeId })
				if (!result.current || version !== this._loadVersion) return
				this.applySnapshot(result.data)
				this.restoreDraft()
			} catch (error) { if (version === this._loadVersion) this.loadError = error.message }
			finally { if (version === this._loadVersion) this.loading = false }
		},
		/** 所有写操作使用服务端回读结果，不在浏览器中乐观修改菜单树。 */
		applySnapshot(snapshot, preferredId = '') {
			this.snapshot = snapshot
			this.adminTypeId = String(snapshot.adminTypeId || '')
			this.recovered = false; this.staleDraft = false
			const selectedId = String(snapshot.selectedId || preferredId || '')
			const selected = this.flatNodes.find(item => item.id === selectedId) || this.flatNodes[0]
			this.setDraft(selected ? createMenuDraft(selected) : null)
		},
		/** 明确放弃后才切换上下文或选择其他节点。 */
		async confirmDiscard() {
			if (!this.ensureIdentity()) return false
			if (this.saving) return false
			if (!this.dirty) return true
			return new Promise(resolve => uni.showModal({ title: '有未保存的修改', content: '离开当前编辑会放弃这些修改。', confirmText: '放弃修改', cancelText: '继续编辑',
				success: result => { if (result.confirm) { this.clearDraft(); this.baseline = menuDraftFingerprint(this.draft) } resolve(!!result.confirm) }, fail: () => resolve(false) }))
		},
		/** 宿主可在返回或切换页签前调用，避免静默丢失编辑。 */
		async canLeave() { return this._identityInvalid ? true : this.confirmDiscard() },
		/** 项目选择被取消时重建下拉状态，防止显示值与真实编辑上下文不一致。 */
		async changeProject(item) { if (!item || String(item.id) === this.projectId) return; if (!(await this.confirmDiscard())) { this.selectorVersion++; return }; this.projectId = String(item.id); this.adminTypeId = ''; await this.loadSnapshot('') },
		/** 后台选择遵守相同的未保存确认流程。 */
		async changeAdmin(item) { if (!item || String(item.id) === this.adminTypeId) return; if (!(await this.confirmDiscard())) { this.selectorVersion++; return }; this.adminTypeId = String(item.id); await this.loadSnapshot() },
		/** 已失效身份先重新读取项目范围；正常刷新需明确放弃未保存修改。 */
		async refresh() { if (this.busy) return; if (this._identityInvalid) { await this.initialize(); return }; if (!(await this.confirmDiscard())) return; if (this.projects.length && this.projectId) await this.loadSnapshot(); else await this.initialize() },
		/** 选中节点仅建立草稿，不在树上直接编辑。 */
		async selectNode(node) { if (this.busy || this.draft && this.draft.id === node.id || !(await this.confirmDiscard())) return; this.setDraft(createMenuDraft(node)); this.mutationError = '' },
		/** 新建分组与页面共用编辑器，默认放在当前选中位置的有效目录下。 */
		async addNode(kind) {
			if (this.busy || !(await this.confirmDiscard())) return
			const current = this.selectedNode
			const parents = menuParentOptions(this.snapshot.tree, createMenuDraft(null))
			const preferred = current && (current.kind === 'DIRECTORY' ? current.id : current.parentId)
			const parentId = parents.some(item => item.value === preferred) ? preferred : (parents[0] || {}).value || ''
			const siblings = this.flatNodes.filter(item => kind === 'FIRST_MENU' ? item.isFirst : item.parentId === parentId)
			const draft = createMenuDraft(null, parentId, siblings.reduce((max, item) => Math.max(max, item.sortNo || 0), 0) + 10)
			draft.kind = kind
			this.setDraft(draft, true); this.mutationError = ''
		},
		/** 切换类型清除不适用的目标字段，历史地址由服务端按原节点决定是否保留。 */
		changeKind(kind) { if (this.editorDisabled || this.hasChildren || kind === this.draft.kind) return; this.draft.kind = kind; this.draft.targetPageId = ''; this.draft.showCount = false },
		/** 目标不支持计数时自动关闭旧角标选项。 */
		changeTarget(item) { if (!item || !item.showCountSupported) this.draft.showCount = false },
		/** 展开状态只影响当前工作台展示，不修改项目菜单配置。 */
		toggleBranch(id) { this.collapsed = this.collapsed.includes(id) ? this.collapsed.filter(item => item !== id) : [...this.collapsed, id] },
		/** 更新草稿基线时同步清除恢复标记，新增节点始终视为待保存。 */
		setDraft(draft, isNew = false) { this.baseline = isNew ? '' : menuDraftFingerprint(draft); this.draft = draft; this.recovered = false; this.staleDraft = false },
		/** 放弃后恢复服务端快照中的同一节点，新建草稿则关闭编辑区。 */
		async discard() { if (!(await this.confirmDiscard())) return; const node = this.selectedNode; this.setDraft(node ? createMenuDraft(node) : null); this.mutationError = '' },
		/** 对本次草稿做语义检查，再携带快照版本保存；冲突时保留编辑供用户核对。 */
		async save() {
			if (!this.ensureIdentity() || this.editorDisabled || !this.dirty) return
			this.mutationError = validateMenuDraft(this.snapshot, this.draft)
			if (this.mutationError) return
			await this.mutate('/save', buildMenuSaveRequest(this.snapshot, this.draft))
		},
		/** 删除必须确认，子菜单和授权引用是否允许删除交由服务端统一校验。 */
		removeNode() {
			if (!this.ensureIdentity() || this.busy || !this.canDelete) return
			const node = this.selectedNode
			const request = { projectId: this.snapshot.projectId, adminTypeId: this.snapshot.adminTypeId, revision: this.snapshot.revision, id: this.draft.id }
			uni.showModal({ title: '删除菜单', content: '确定删除“' + (node && node.name || this.draft.name) + '”？存在子菜单或授权引用时不能删除。', confirmText: '确认删除',
				success: result => { if (result.confirm && this.ensureIdentity()) this.mutate('/delete', request) } })
		},
		/** 保存期间禁止切换上下文，响应必须仍属于本次加载代次。 */
		async mutate(path, data) {
			if (!this.ensureIdentity()) return
			const version = this._loadVersion
			this.persistDraft()
			this.saving = true; this.mutationError = ''
			try {
				const result = await this._mutationLane.run(path, data)
				if (!result.current || version !== this._loadVersion) return
				this.clearDraft(); this.applySnapshot(result.data, data.id)
				uni.showToast({ title: path === '/delete' ? '菜单已删除' : '菜单已保存', icon: 'success' })
				uni.$emit('onMenuConfigurationChanged', { projectId: data.projectId, adminTypeId: data.adminTypeId })
				this.$emit('saved', result.data)
			} catch (error) { if (version === this._loadVersion) this.mutationError = error.message }
			finally { if (version === this._loadVersion) this.saving = false }
		},
		/** 草稿缓存按用户和菜单上下文隔离，不保存会话凭据。 */
		draftKey() { return 'yzp-menu-draft:' + String(this._draftOwnerId || '') + ':' + this.projectId + ':' + this.adminTypeId },
		/** 仅保存未提交且仍有效的草稿，旧版本缓存保持原版本号供冲突核对。 */
		persistDraft() {
			if (!this.snapshot || !this.adminTypeId || !this._draftOwnerId || this._identityInvalid || this.loading || this.saving || this.staleDraft) return
			try { if (this.dirty) uni.setStorageSync(this.draftKey(), { revision: this.snapshot.revision, draft: { ...this.draft }, baseline: this.baseline })
				else uni.removeStorageSync(this.draftKey()) } catch (_) { /* 本地空间不可用时仍保留当前页面中的草稿。 */ }
		},
		/** 用户主动放弃或服务端保存成功后清除对应上下文的草稿。 */
		clearDraft() { try { uni.removeStorageSync(this.draftKey()) } catch (_) { /* 清理缓存失败不影响服务端确认的保存结果。 */ } },
		/** 远端版本改变时只恢复为只读草稿，不能绕过并发校验覆盖其他人的修改。 */
		restoreDraft() {
			if (!this._draftOwnerId || this._identityInvalid) return
			let stored
			try { stored = uni.getStorageSync(this.draftKey()) } catch (_) { return }
			if (!stored || !stored.draft) return
			this.baseline = stored.baseline; this.draft = stored.draft; this.recovered = true
			this.staleDraft = stored.revision !== this.snapshot.revision
		}
	}
}
</script>

<style scoped>
.menu-workbench { color: #253444; font-size: 14px; --menu-ink: #245a81; }
.workbench-heading, .panel-heading, .editor-heading, .editor-actions, .switch-field { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.workbench-heading { margin-bottom: 24px; align-items: flex-start; }
.workbench-title, .workbench-description, .editor-title, .editor-kicker, .field-label, .field-hint, .target-route { display: block; }
.workbench-title { font-size: 24px; font-weight: 650; letter-spacing: 1px; }
.workbench-description { color: #758391; margin-top: 8px; line-height: 1.6; }
.menu-workbench button { margin: 0; border-radius: 5px; font-size: 13px; line-height: 32px; }
.menu-workbench button::after { border: 0; }
.quiet-button { color: #3f5568; background: #f4f7f9; border: 1px solid #dce4eb; }
.menu-workbench button[disabled] { opacity: .45; }
.context-bar { display: flex; align-items: flex-end; gap: 18px; padding: 18px 20px; background: #f4f7f9; border: 1px solid #e3e9ee; border-radius: 7px; margin-bottom: 20px; }
.context-field { width: 280px; max-width: 42%; }
.field-label { color: #435365; font-size: 13px; font-weight: 600; margin-bottom: 8px; }
.scope-note { color: #7b8996; font-size: 12px; margin-left: auto; padding-bottom: 10px; white-space: nowrap; }
.workbench-layout { display: flex; border: 1px solid #dde5eb; border-radius: 7px; overflow: hidden; min-height: 550px; }
.tree-panel { width: 310px; flex-shrink: 0; background: #f9fbfc; border-right: 1px solid #dde5eb; display: flex; flex-direction: column; }
.panel-heading { padding: 19px 18px 10px; font-weight: 650; letter-spacing: .5px; }
.tree-total { color: #7f90a0; font-size: 12px; font-weight: 400; }
.tree-tools { display: flex; gap: 8px; padding: 4px 16px 12px; }
.tree-tools button { flex: 1; }
.tree-search { height: 34px; background: white; border: 1px solid #dce4eb; border-radius: 5px; margin: 0 16px 12px; padding: 0 10px; font-size: 13px; }
.tree-scroll { height: 470px; flex: 1; }
.tree-row { display: flex; align-items: center; gap: 7px; padding: 11px 12px; min-height: 22px; border-left: 3px solid transparent; cursor: pointer; }
.tree-row:hover { background: #eff4f8; }
.tree-row.selected { background: #e9f1f7; color: #1e5277; border-left-color: #245a81; }
.first-row { font-weight: 650; }
.branch-toggle, .node-mark { width: 18px; flex-shrink: 0; text-align: center; color: #718495; }
.menu-workbench .branch-toggle { padding: 0; background: transparent; font-size: 18px; line-height: 22px; }
.node-text { flex: 1; min-width: 0; overflow-wrap: anywhere; line-height: 1.5; }
.node-path { display: block; font-size: 11px; color: #7a8b99; font-weight: 400; }
.node-tag { font-size: 10px; color: #81919e; font-weight: 400; white-space: nowrap; }
.tree-footnote { color: #8a98a4; font-size: 11px; border-top: 1px solid #e5ebef; padding: 13px 16px; }
.tree-empty { color: #81909e; padding: 30px 18px; line-height: 1.8; }
.editor-panel { background: white; flex: 1; min-width: 0; display: flex; flex-direction: column; }
.editor-heading { padding: 25px 28px 21px; border-bottom: 1px solid #edf1f4; }
.editor-kicker { font-size: 11px; color: #84929f; margin-bottom: 7px; letter-spacing: 1px; }
.editor-title { font-size: 20px; font-weight: 650; overflow-wrap: anywhere; }
.dirty-tag { color: #987329; font-size: 11px; padding: 4px 8px; background: #fbf5e7; border-radius: 4px; white-space: nowrap; }
.editor-form { padding: 25px 28px; max-width: 760px; width: 100%; box-sizing: border-box; flex: 1; }
.form-field { margin-bottom: 22px; min-width: 0; }
.required { color: #bb6656; }
.text-input { height: 38px; border: 1px solid #dce4eb; border-radius: 5px; padding: 0 12px; font-size: 14px; background: white; }
.text-input:focus { border-color: #7197b3; }
.text-input[disabled] { background: #f7f9fb; }
.field-hint { color: #8b97a2; font-size: 12px; margin-top: 8px; line-height: 1.6; font-weight: 400; }
.kind-options { display: flex; gap: 8px; flex-wrap: wrap; }
.kind-options button { color: #647484; background: #f5f7f9; border: 1px solid #e2e8ed; }
.kind-options button.active { color: #245a81; background: #edf4f9; border-color: #a3bfd2; }
.target-preview { border-left: 2px solid #a8bece; background: #f5f8fa; padding: 11px 13px; margin-top: 12px; font-size: 13px; color: #4e6678; }
.target-route { color: #91a0ac; font-family: monospace; font-size: 11px; margin-top: 5px; overflow-wrap: anywhere; }
.form-columns { display: flex; gap: 20px; }.form-columns > view { flex: 1; }
.switch-field { border-top: 1px solid #edf1f4; padding: 17px 0 0; margin-bottom: 18px; }.switch-field .field-label { margin-bottom: 0; }
.editor-actions { border-top: 1px solid #edf1f4; padding: 18px 28px; gap: 10px; }
.action-spacer { flex: 1; }.save-button { color: white; background: #245a81; min-width: 100px; }.delete-button { color: #b86657; background: transparent; padding: 0; }
.notice { background: #f4f7fa; color: #61778a; font-size: 12px; line-height: 1.8; margin: 18px 28px 0; padding: 11px 14px; border-radius: 4px; }
.error-notice { background: #fff4ee; color: #aa6046; }
.editor-form .notice { margin: 0 0 22px; }
.workbench-state, .editor-placeholder { color: #8996a2; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; min-height: 280px; text-align: center; padding: 24px; line-height: 1.7; }
.editor-placeholder { flex: 1; }.state-title { color: #596d7e; font-weight: 600; font-size: 16px; }.placeholder-mark { font-size: 30px; color: #b0bfca; margin-bottom: 4px; }.error-state { color: #a86f54; }
@media (max-width: 900px) { .tree-panel { width: 250px; }.editor-heading, .editor-form { padding: 20px; }.editor-actions { padding: 16px 20px; }.notice { margin-left: 20px; margin-right: 20px; }.scope-note { display: none; }.context-field { flex: 1; max-width: none; } }
@media (max-width: 600px) { .workbench-heading { margin-bottom: 18px; gap: 8px; }.workbench-title { font-size: 20px; }.workbench-description { font-size: 12px; }.workbench-heading button { white-space: nowrap; }.context-bar { flex-direction: column; align-items: stretch; padding: 14px; gap: 12px; }.context-field { width: 100%; }.workbench-layout { flex-direction: column; }.tree-panel { width: 100%; border-right: 0; border-bottom: 1px solid #dde5eb; }.tree-scroll { height: 200px; flex: none; }.tree-footnote { display: none; }.panel-heading { padding-top: 14px; }.tree-tools { padding-top: 0; }.editor-heading, .editor-form { padding: 18px 16px; }.editor-actions { padding: 14px 16px; flex-wrap: wrap; }.editor-actions button { font-size: 12px; }.form-columns { gap: 12px; }.notice { margin-left: 16px; margin-right: 16px; }.text-input { font-size: 16px; }.kind-options button { padding: 0 10px; } }
</style>
