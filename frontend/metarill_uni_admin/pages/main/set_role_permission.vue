<template>
	<view class="permission-page">
		<uni-nav-bar v-if="!$deviceInfo.is_pc" left-icon="left" @clickLeft="back" fixed statusBar :border="false" title="设置角色权限" />
		<yzp-permission-workbench ref="workbench" :initial-role-id="roleId">
			<template #unsupported><yzp-permission-legacy :initial-role-id="roleId" /></template>
		</yzp-permission-workbench>
	</view>
</template>

<script>
import YzpPermissionWorkbench from '@/components/yzp-permission-workbench/yzp-permission-workbench.vue'
import YzpPermissionLegacy from '@/components/yzp-permission-legacy/yzp-permission-legacy.vue'

export default {
	components: { YzpPermissionWorkbench, YzpPermissionLegacy },
	data() { return { roleId: '' } },
	/** 保留旧角色列表通过 id 进入授权页的导航协议。 */
	onLoad(options) { this.roleId = String(options && (options.roleId || options.id) || '') },
	/** 系统返回先确认未保存内容；已确认的返回不再拦截。 */
	onBackPress() {
		if (this._allowBack) return false
		this.back(); return true
	},
	methods: {
		/** 前端探测失败不开放旧写入口，只有 supported:false 才渲染旧授权组件。 */
		async back() {
			if (this._leaving) return
			this._leaving = true
			try {
				if (this.$refs.workbench && !(await this.$refs.workbench.canLeave())) return
				this._allowBack = true; this.$yzp.back()
			} finally { this._leaving = false }
		}
	}
}
</script>

<style scoped>
.permission-page { background: #fff; min-height: 100%; }
</style>
