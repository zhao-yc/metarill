<template>
	<view class="menu-page" :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }">
		<uni-nav-bar v-if="!$deviceInfo.is_pc" fixed statusBar :border="false" title="菜单"></uni-nav-bar>
		<scroll-view class="sidebar" scroll-y="true"><yzp-menu /></scroll-view>
	</view>
</template>
<script>
import YzpMenu from '@/components/yzp-menu/yzp-menu.vue'
// 菜单请求、权限快照、导航定位与角标由 V8 公共运行时统一维护。
export default { components: { YzpMenu } }
</script>

<style lang="scss">
	.sidebar {
		position: fixed;
		width: var(--window-left);
		height: calc(100vh - (var(--top-window-height)));
		box-sizing: border-box;
		border-right: 1px solid #e5e5e5;
		background-color: #f8f8f8;
		padding-bottom: 10px;
	}

	.ismobile .sidebar {
		width: 100%;
		padding-bottom: calc(44px + (var(--window-bottom)));
	}

	/* #ifdef H5 */
	.sidebar ::-webkit-scrollbar {
		display: none;
	}

	/* #endif */

	.title {
		margin-left: 5px;
	}
</style>
