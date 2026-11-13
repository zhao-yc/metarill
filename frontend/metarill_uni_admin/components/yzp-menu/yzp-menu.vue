<template>
	<view class="yzp-menu" aria-label="应用菜单">
		<view v-if="state.status === 'loading'" class="menu-state">正在加载菜单…</view>
		<view v-else-if="state.status === 'error'" class="menu-state">
			<text>{{ state.error }}</text><button size="mini" @click="retry">重新加载</button>
		</view>
		<view v-else-if="state.status === 'empty'" class="menu-state">暂无可用菜单</view>
		<view v-for="row in rows" :key="row.node.menu_id" class="menu-row"
			role="button" tabindex="0" :aria-expanded="row.directory ? !!expanded[row.node.menu_id] : undefined"
			:aria-current="state.activeMenuId === row.node.menu_id ? 'page' : undefined"
			@keydown.enter="select(row)" @keydown.space.prevent="select(row)"
			:class="{ active: state.activeMenuId === row.node.menu_id, directory: row.directory }"
			:style="{ paddingLeft: (16 + row.depth * 18) + 'px' }" @click="select(row)">
			<uni-icons v-if="row.node.icon" :type="row.node.icon" size="17" class="menu-icon" />
			<text class="menu-name">{{ row.node.text }}</text>
			<text v-if="row.node.countError" class="count-error" @click.stop="retryCount(row.node)">重试</text>
			<text v-else-if="row.node.count !== undefined" class="menu-count" :class="{ pending: row.node.countLoading }">{{ row.node.count }}</text>
			<uni-icons v-if="row.directory" :type="expanded[row.node.menu_id] ? 'bottom' : 'right'" size="13" />
		</view>
		<view v-if="state.navigationError" class="navigation-error">{{ state.navigationError }}</view>
	</view>
</template>

<script>
import { getMenuRuntime } from '@/common/yzp_runtime/menuRuntimeBridge'

export default {
	name: 'YzpMenu',
	data() {
		return { state: { menus: [], status: 'idle', expandedMenuIds: [] }, expanded: {}, unsubscribe: null, lastActive: '' }
	},
	computed: {
		/** 将展开节点摊平成展示行，任意合法层级使用同一套交互。 */
		rows() {
			const rows = []
			const visit = (nodes, depth) => (nodes || []).forEach(node => {
				const directory = node.nodeType === 'DIRECTORY' || !node.target || !!(node.children && node.children.length)
				rows.push({ node, depth, directory })
				if (directory && this.expanded[node.menu_id]) visit(node.children, depth + 1)
			})
			visit(this.state.menus, 0)
			return rows
		}
	},
	mounted() {
		const runtime = getMenuRuntime()
		this.unsubscribe = runtime.subscribe(state => {
			if (state.status === 'idle' || state.status === 'loading') this.expanded = {}
			if (state.activeMenuId !== this.lastActive || this.state.menus !== state.menus && !Object.keys(this.expanded).length) {
				const expanded = { ...this.expanded }
				state.expandedMenuIds.forEach(id => { expanded[id] = true })
				// 初次进入时展开顶层，空目录仍可见，当前路由的父级总是自动展开。
				if (!Object.keys(expanded).length) state.menus.forEach(node => { expanded[node.menu_id] = true })
				this.expanded = expanded
			}
			this.lastActive = state.activeMenuId
			this.state = state
		})
		runtime.load()
	},
	beforeUnmount() { this.release() },
	beforeDestroy() { this.release() },
	methods: {
		/** 组件卸载只释放自己的订阅，其他菜单窗口继续共享请求状态。 */
		release() { if (this.unsubscribe) this.unsubscribe(); this.unsubscribe = null },
		/** 目录展开与实际页面选中分开处理，跳转失败不会提前改变高亮。 */
		select(row) {
			if (row.directory) this.expanded = { ...this.expanded, [row.node.menu_id]: !this.expanded[row.node.menu_id] }
			else getMenuRuntime().navigateTo(row.node.menu_id)
		},
		retry() { getMenuRuntime().retry() },
		retryCount(node) { getMenuRuntime().invalidateCounts(node.target) }
	}
}
</script>

<style lang="scss" scoped>
.yzp-menu { padding: 10px 0 24px; color: #35443c; font-size: 14px; }
.menu-row { display: flex; align-items: center; gap: 9px; min-height: 44px; padding-right: 15px; cursor: pointer; box-sizing: border-box; border-left: 3px solid transparent; }
.menu-row:hover, .menu-row:focus-visible { background: mix($uni-primary, #fff, 7%); }
.menu-row.active { background: mix($uni-primary, #fff, 12%); color: $uni-primary; border-left-color: $uni-primary; font-weight: 600; }
.menu-row.directory { color: #59665e; font-size: 13px; font-weight: 600; }
.menu-name { flex: 1; min-width: 0; overflow-wrap: anywhere; line-height: 20px; }
.menu-count { font-size: 11px; background: #e7ece8; color: #657169; padding: 1px 6px; border-radius: 8px; }
.menu-count.pending { opacity: .5; }
.count-error { color: #986235; font-size: 11px; }
.menu-state { padding: 24px 16px; color: #737d76; font-size: 13px; line-height: 22px; }
.menu-state button { display: block; margin: 14px 0 0; }
.navigation-error { margin: 12px 16px; color: #a43d34; font-size: 12px; line-height: 20px; }
</style>
