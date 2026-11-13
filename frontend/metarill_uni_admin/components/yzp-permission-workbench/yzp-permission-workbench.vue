<template>
	<view class="permission-workbench">
		<slot v-if="state.status === 'unsupported'" name="unsupported"><view class="permission-state">当前项目继续使用原角色权限配置。</view></slot>
		<template v-else>
			<view class="permission-heading"><view><text class="permission-title">角色权限</text><text class="permission-subtitle">分别配置导航入口、页面操作和按钮权限。</text></view><button size="mini" :disabled="state.busy" @click="refresh">重新读取</button></view>
			<view class="role-bar"><text class="field-label">选择角色</text><zxz-uni-data-select :key="selectorVersion" :model-value="state.roleId" :localdata="state.roles" dataKey="name" dataValue="id" :clear="false" filterable :disabled="state.busy" placeholder="选择角色" @change="changeRole" /><text v-if="state.snapshot" class="mode-tag">{{ legacy ? '旧授权预览' : '独立授权已启用' }}</text></view>
			<view v-if="state.status === 'loading'" class="permission-state">正在读取角色与授权配置…</view>
			<view v-else-if="state.status === 'error'" class="permission-state error-message"><text>{{ state.error }}</text><button size="mini" @click="refresh">重试</button></view>
			<view v-else-if="!state.snapshot" class="permission-state">暂无可配置的角色，请先完成角色配置。</view>
			<template v-else>
				<view v-if="superRole" class="permission-notice">此角色具有超级权限。修改下方授权不会撤销它的超级权限。</view>
				<view v-if="legacy" class="migration-notice"><view><text class="notice-title">先核对现有授权，再启用独立配置</text><text>下方展示旧授权转换后的结果。启用前保持原授权方式；启用后，菜单可见性与页面、按钮权限分别维护。</text></view><button size="mini" class="primary-button" :disabled="state.busy || issues.length > 0" @click="activate">确认并启用</button></view>
				<view v-if="issues.length" class="issues-box"><text class="notice-title">{{ legacy ? '请先处理以下问题，暂不能启用' : '配置需要核对' }}</text><text v-for="(issue, index) in issues" :key="index">{{ index + 1 }}. {{ issue }}</text></view>
				<view v-if="!legacy && invalidGrants.length" class="issues-box"><text class="notice-title">失效授权</text><text>可从当前草稿移除，保存后才正式撤销。</text><view v-for="item in invalidGrants" :key="item.key" class="invalid-grant"><text>{{ item.text }}</text><button size="mini" :disabled="state.busy" @click="removeInvalid(item)">从草稿移除</button></view></view>
				<view v-if="state.mutationError" class="permission-notice error-message">{{ state.mutationError }}。修改仍保留；重新读取会先确认放弃当前修改。</view>
				<view class="permission-tabs"><button v-for="tab in tabs" :key="tab.id" :class="{ active: activeTab === tab.id }" @click="activeTab = tab.id">{{ tab.name }} <text>{{ tab.count }}</text></button><text v-if="dirty" class="dirty-label">未保存</text></view>
				<view class="permission-content">
					<input v-model="search" class="permission-search" placeholder="搜索名称" aria-label="搜索授权项目" />
					<template v-if="activeTab === 'menus'">
						<text class="section-description">只控制导航入口是否可见。勾选菜单或目录，不会授予页面操作和按钮权限。</text>
						<view v-if="!menuRows.length" class="permission-empty">没有匹配的菜单。</view>
						<view v-for="row in menuRows" :key="row.id" class="menu-grant-row" :class="{ group: !row.grantable }" :style="{ paddingLeft: (12 + row.depth * 18) + 'px' }">
							<text v-if="!row.grantable">{{ row.name }}</text><checkbox-group v-else @change="changeMenu(row.id, $event)"><label><checkbox :value="row.id" :checked="state.grants.menuIds.includes(row.id)" :disabled="readOnly" color="#245a81" /><text>{{ row.name }}</text><text v-if="row.kind === 'DIRECTORY'" class="resource-note">目录入口</text></label></checkbox-group>
						</view>
					</template>
					<template v-else-if="activeTab === 'pages'">
						<text class="section-description">页面权限独立于菜单入口。未配置菜单的页面也在此维护，多个菜单指向同一页面时共用页面权限。</text>
						<view v-if="!pages.length" class="permission-empty">没有匹配的页面。</view>
						<view v-for="page in pages" :key="page.id" class="resource-row"><view class="resource-label"><text>{{ page.name }}</text><text class="resource-note">{{ page.tableName }}</text><text v-if="!hasMenu(page.id)" class="no-menu-tag">无菜单入口</text></view>
							<view class="page-grants"><checkbox-group class="full-permission" @change="changeFullPage(page.id, $event)"><label><checkbox value="is_all" :checked="hasFullPermission(page.id)" :disabled="readOnly" color="#245a81" />全功能（含复制/导入）</label><text class="resource-note">不包含按钮授权</text></checkbox-group>
							<checkbox-group class="action-options" @change="changePage(page.id, $event)"><label v-for="action in pageActions(page)" :key="action"><checkbox :value="action" :checked="checkedActions(page).includes(action)" :disabled="readOnly || hasFullPermission(page.id)" color="#245a81" />{{ actionNames[action] || action }}</label></checkbox-group><text v-if="!pageActions(page).length" class="resource-note">此页面未开放标准操作</text></view>
						</view>
					</template>
					<template v-else>
						<text class="section-description">按钮授权独立保存。操作仍需满足后端业务规则。</text>
						<view v-if="!buttons.length" class="permission-empty">没有匹配的按钮。</view>
						<view v-for="button in buttons" :key="button.id" class="resource-row"><checkbox-group @change="changeButton(button.id, $event)"><label><checkbox :value="String(button.id)" :checked="state.grants.buttonIds.includes(String(button.id))" :disabled="readOnly" color="#245a81" /><text>{{ button.name }}</text><text class="resource-note">{{ pageName(button.pageId) }}</text></label></checkbox-group></view>
					</template>
				</view>
				<view v-if="!legacy" class="permission-actions"><text>授权以服务端保存结果为准。</text><button size="mini" :disabled="state.busy || !dirty" @click="discard">放弃修改</button><button size="mini" class="primary-button" :disabled="state.busy || !dirty" @click="save">{{ state.busy ? '正在保存…' : '保存权限' }}</button></view>
			</template>
		</template>
	</view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime'
import { PERMISSION_ACTIONS, createPermissionWorkbench, permissionContext, permissionFingerprint,
	permissionMenuRows, selectedPageActions, invalidPermissionGrants } from '@/common/yzp_runtime/permissionWorkbench'

export default {
	name: 'YzpPermissionWorkbench',
	props: { initialRoleId: { type: String, default: '' } },
	emits: ['dirty-change', 'saved'],
	data() { return { state: { status: 'idle', roles: [], roleId: '', snapshot: null, grants: { menuIds: [], pagePermissions: {}, buttonIds: [] }, baseline: '', busy: false }, activeTab: 'menus', search: '', selectorVersion: 0, actionNames: PERMISSION_ACTIONS } },
	computed: {
		legacy() { return this.state.snapshot && this.state.snapshot.mode === 'LEGACY' },
		readOnly() { return this.state.busy || this.legacy },
		dirty() { return !!this.state.snapshot && permissionFingerprint(this.state.grants) !== this.state.baseline },
		catalog() { return this.state.snapshot && this.state.snapshot.catalog || {} },
		issues() { return [...new Set([...(this.state.snapshot && this.state.snapshot.issues || []), ...(this.catalog.issues || [])])] },
		invalidGrants() { return invalidPermissionGrants(this.catalog, this.state.grants) },
		superRole() { const role = this.state.roles.find(item => item.id === this.state.roleId); return !!role && [true, 1, '1'].includes(role.isSuper) },
		menuRows() { return permissionMenuRows(this.catalog).filter(row => this.matches(row.name)) },
		pages() { return (this.catalog.pages || []).filter(page => this.matches(page.name, page.tableName)) },
		buttons() { return (this.catalog.buttons || []).filter(button => this.matches(button.name, this.pageName(button.pageId))) },
		tabs() { return [{ id: 'menus', name: '菜单可见', count: permissionMenuRows(this.catalog).filter(row => row.grantable).length }, { id: 'pages', name: '页面操作', count: (this.catalog.pages || []).length }, { id: 'buttons', name: '按钮权限', count: (this.catalog.buttons || []).length }] }
	},
	watch: {
		initialRoleId(value) { if (this._workbench) this._workbench.loadRoles(value) },
		dirty(value) { this.$emit('dirty-change', value) }
	},
	/** 公共组件只持有自己的状态机，版本支持和角色范围均由服务端确认。 */
	created() {
		this._workbench = createPermissionWorkbench({ request: options => yzp.request(options), getContext: () => permissionContext(key => uni.getStorageSync(key)),
			onChange: state => { this.state = state }, onSaved: snapshot => {
				this._contextSignature = permissionContext(key => uni.getStorageSync(key))
				this.$emit('saved', snapshot); uni.$emit('onLoginLoadMenu')
			} })
		this._contextSignature = permissionContext(key => uni.getStorageSync(key))
		this._contextChanged = () => {
			const next = permissionContext(key => uni.getStorageSync(key))
			if (next === this._contextSignature) return
			this._contextSignature = next; this._workbench.loadRoles(this.initialRoleId)
		}
		this._logout = () => { this._contextSignature = ''; this._workbench.clear() }
		uni.$on('onChangeProject', this._contextChanged); uni.$on('onLoginLoadMenu', this._contextChanged); uni.$on('onLogout', this._logout)
		// #ifdef H5
		this._beforeUnload = event => { if (this.dirty || this.state.busy) { event.preventDefault(); event.returnValue = '' } }
		window.addEventListener('beforeunload', this._beforeUnload)
		// #endif
		this._workbench.loadRoles(this.initialRoleId)
	},
	beforeUnmount() { this.release() },
	beforeDestroy() { this.release() },
	methods: {
		/** 名称过滤只影响展示，不影响隐藏行已配置的授权。 */
		matches(...values) { const query = this.search.trim().toLowerCase(); return !query || values.some(value => String(value || '').toLowerCase().includes(query)) },
		/** 未配置导航入口的页面仍然保留在资源目录中。 */
		hasMenu(id) { return permissionMenuRows(this.catalog).some(row => String(row.targetPageId || '') === String(id)) },
		/** 按钮只显示所属页面名称，不隐式继承页面操作。 */
		pageName(id) { const page = (this.catalog.pages || []).find(item => String(item.id) === String(id)); return page ? page.name : '未关联页面' },
		/** 页面动作由目录决定，兼容全功能值不作为新的勾选快捷项。 */
		pageActions(page) { return (page.actions || []).filter(action => action !== 'is_all') },
		/** 全功能授权展示全部普通动作，独立开关承担明确授予和撤销。 */
		checkedActions(page) { return selectedPageActions(this.state.grants, page) },
		/** 全功能不会由普通动作全选自动产生。 */
		hasFullPermission(id) { return (this.state.grants.pagePermissions[String(id)] || []).includes('is_all') },
		/** 菜单勾选仅改变菜单授权集合。 */
		changeMenu(id, event) { this._workbench.change('menu', id, event.detail.value.includes(String(id))) },
		/** 页面勾选只替换当前页面的动作集合。 */
		changePage(id, event) { this._workbench.change('page', id, event.detail.value) },
		/** 复制和导入保留原全功能要求，开关只改变该页面的 is_all。 */
		changeFullPage(id, event) { this._workbench.change('pageAll', id, event.detail.value.includes('is_all')) },
		/** 按钮勾选不联动菜单或页面，避免暗中扩大权限。 */
		changeButton(id, event) { this._workbench.change('button', id, event.detail.value.includes(String(id))) },
		/** 仅撤销用户点选的失效授权，其他维度和仍有效的动作保持原样。 */
		removeInvalid(item) {
			if (item.type === 'pageAction') this._workbench.change('page', item.id, (this.state.grants.pagePermissions[item.id] || []).filter(action => action !== item.action))
			else this._workbench.change(item.type, item.id, item.type === 'page' ? [] : false)
		},
		/** 宿主返回和角色切换共用未保存确认，不持久化敏感授权草稿。 */
		async canLeave() {
			if (this.state.busy) return false
			if (!this.dirty) return true
			return new Promise(resolve => uni.showModal({ title: '有未保存的权限修改', content: '离开将放弃当前角色的修改。', confirmText: '放弃修改', cancelText: '继续编辑',
				success: result => { if (result.confirm) this._workbench.discard(); resolve(!!result.confirm) }, fail: () => resolve(false) }))
		},
		/** 取消角色切换时恢复下拉显示，保持编辑上下文一致。 */
		async changeRole(role) { if (!role || role.id === this.state.roleId) return; if (!(await this.canLeave())) { this.selectorVersion++; return }; this.search = ''; await this._workbench.loadRole(role.id) },
		/** 冲突只提示重新读取，绝不自动用新版本号覆盖旧草稿。 */
		async refresh() { if (await this.canLeave()) await this._workbench.loadRoles(this.state.roleId || this.initialRoleId) },
		/** 放弃恢复最近一次确认的服务端授权。 */
		async discard() { if (await this.canLeave()) this._workbench.discard() },
		/** 显式确认后激活服务端预览，不在浏览器内重算授权迁移。 */
		activate() {
			if (this.state.busy || this.issues.length || !this.legacy) return
			const role = this.state.roles.find(item => item.id === this.state.roleId)
			uni.showModal({ title: '启用独立授权', content: '确认以当前预览结果启用“' + (role && role.name || '') + '”的独立授权？后台首次启用后会统一校验页面、按钮和真实父子关系；其他角色仍读取旧授权数据，但不再沿用旧子表直接放行。', confirmText: '确认启用',
				success: async result => { if (result.confirm && await this._workbench.activate()) uni.showToast({ title: '已启用独立授权', icon: 'success' }) } })
		},
		/** 保存成功使用回读结果，失败及冲突保留当前修改。 */
		async save() { if (await this._workbench.save()) uni.showToast({ title: '权限已保存', icon: 'success' }) },
		/** 多端卸载钩子可能重复，事件及请求只释放一次。 */
		release() {
			if (this._released) return; this._released = true
			this._workbench.dispose()
			uni.$off('onChangeProject', this._contextChanged); uni.$off('onLoginLoadMenu', this._contextChanged); uni.$off('onLogout', this._logout)
			// #ifdef H5
			window.removeEventListener('beforeunload', this._beforeUnload)
			// #endif
		}
	}
}
</script>

<style scoped>
.permission-workbench { padding: 22px; color: #35443c; font-size: 14px; }
.permission-heading, .role-bar, .migration-notice, .permission-tabs, .permission-actions { display: flex; align-items: center; gap: 16px; }
.permission-heading { justify-content: space-between; margin-bottom: 24px; }
.permission-title, .permission-subtitle, .section-description, .notice-title, .issues-box > text { display: block; }
.permission-title { font-size: 24px; font-weight: 650; }.permission-subtitle { color: #79867d; font-size: 13px; margin-top: 8px; line-height: 1.6; }
.permission-workbench button { margin: 0; font-size: 13px; border-radius: 5px; background: #f2f5f3; color: #56695d; line-height: 34px; }.permission-workbench button::after { border: 0; }.permission-workbench button[disabled] { opacity: .45; }
.permission-workbench .primary-button { background: #245a81; color: white; white-space: nowrap; }.role-bar { padding: 18px; border: 1px solid #e1e7e3; border-radius: 6px; background: #f7f9f7; }.role-bar > :nth-child(2) { flex: 1; max-width: 400px; }.field-label { font-size: 13px; white-space: nowrap; }.mode-tag { color: #68816c; font-size: 12px; margin-left: auto; }
.permission-notice, .migration-notice, .issues-box { padding: 16px 18px; margin-top: 16px; font-size: 13px; line-height: 1.8; border-radius: 5px; }.permission-notice { background: #f5f7f4; color: #6a7c69; }.migration-notice { background: #eff5f9; color: #627d91; justify-content: space-between; }.notice-title { font-weight: 600; margin-bottom: 5px; }.issues-box { background: #fff6eb; color: #976b37; }.error-message { color: #af6048; }
.invalid-grant { display: flex; gap: 12px; align-items: center; justify-content: space-between; margin-top: 10px; }.invalid-grant > text { overflow-wrap: anywhere; }.invalid-grant button { flex-shrink: 0; }
.page-grants { flex: 1; min-width: 260px; }.full-permission { border-bottom: 1px solid #edf1ed; padding-bottom: 6px; margin-bottom: 7px; }
.permission-tabs { border-bottom: 1px solid #dce5df; gap: 0; margin-top: 22px; }.permission-tabs button { border-radius: 0; background: transparent; line-height: 48px; border-bottom: 2px solid transparent; padding: 0 22px; }.permission-tabs button.active { border-bottom-color: #245a81; color: #245a81; font-weight: 600; }.permission-tabs button text { margin-left: 7px; color: #8e9b92; font-size: 11px; }.dirty-label { color: #aa7a35; font-size: 12px; margin-left: auto; }
.permission-content { border: 1px solid #e2e8e4; border-top: 0; padding: 18px; min-height: 250px; }.permission-search { max-width: 340px; border: 1px solid #dce5df; height: 36px; border-radius: 5px; padding: 0 12px; font-size: 14px; }.section-description { color: #829187; font-size: 12px; line-height: 1.7; margin: 14px 0 18px; }.menu-grant-row { min-height: 42px; display: flex; align-items: center; border-bottom: 1px solid #f0f3f0; }.menu-grant-row.group { font-weight: 600; background: #f7f9f7; }.permission-content label { display: inline-flex; align-items: center; gap: 6px; min-height: 36px; cursor: pointer; }.permission-content checkbox { transform: scale(.82); }.resource-note { color: #96a198; font-size: 11px; margin-left: 10px; font-weight: 400; }.resource-row { display: flex; align-items: flex-start; flex-wrap: wrap; gap: 12px; padding: 16px 0; border-bottom: 1px solid #edf1ed; }.resource-label { width: 250px; flex-shrink: 0; line-height: 1.8; }.resource-label > text { display: block; }.resource-label .resource-note { margin-left: 0; }.action-options { display: flex; flex-wrap: wrap; gap: 12px; flex: 1; min-width: 260px; }.no-menu-tag { color: #7c8ca0; font-size: 11px; }.permission-state { padding: 55px 20px; display: flex; align-items: center; flex-direction: column; gap: 16px; color: #7e8d83; text-align: center; line-height: 1.7; }.permission-empty { padding: 40px 12px; color: #8c9b91; }.permission-actions { justify-content: flex-end; padding-top: 18px; }.permission-actions > text { margin-right: auto; color: #8a978e; font-size: 12px; }
@media (max-width: 650px) { .permission-workbench { padding: 14px; }.permission-heading { gap: 10px; }.permission-title { font-size: 20px; }.permission-heading button { white-space: nowrap; }.role-bar { flex-wrap: wrap; padding: 12px; }.role-bar > :nth-child(2) { min-width: 170px; }.mode-tag { width: 100%; margin-left: 0; }.migration-notice { flex-direction: column; align-items: stretch; }.permission-tabs button { padding: 0 10px; font-size: 12px; }.permission-tabs { flex-wrap: wrap; }.dirty-label { padding: 7px 0; }.permission-content { padding: 12px; }.permission-search { font-size: 16px; }.resource-label { width: 100%; }.action-options { min-width: 0; gap: 4px 10px; }.permission-actions { flex-wrap: wrap; }.permission-actions > text { width: 100%; } }
</style>
