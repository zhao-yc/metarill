<template>
	<view class='detail-page'>
		<!-- #ifdef H5 -->
		<keep-alive>
			<component :is="asyncComponent" :home-visible="homeVisible && isHomeContent"></component>
		</keep-alive>
		<!-- #endif -->
		<!-- #ifndef H5 -->
		<!-- 非Web静态登记原组件；访问后保留实例，避免切换菜单丢失页面状态。 -->
		<main-content :home-visible="homeVisible && isHomeContent"></main-content>
		<!-- #endif -->
	</view>
</template>
<script>
	// #ifdef H5
	import { markRaw } from "vue";
	// #endif
	// #ifndef H5
	import MainContent from "@/pages/main/main.vue";
	// #endif
	export default {
		// #ifndef H5
		components: {MainContent},
		// #endif
		data() {
			return {
				asyncComponent: null,
				homeVisible: false,
				isHomeContent: true
			}
		},
		onShow() {
			// 动态内容显式接收页面进入事件，统一刷新配置搜索。
			this.homeVisible = true;
		},
		onHide() {
			this.homeVisible = false;
		},
		onUnload() {
			this.homeVisible = false;
		},
		onLoad: function(e) {
			var that = this;

			// 原工程首页插槽仅首次提供空main，业务内容由开发者维护；不夹带平台配置搜索或财务功能。
			// #ifdef H5
			import("@/pages/main/main").then(module => {
				that.asyncComponent = markRaw(module.default)
			})
			// #endif

		}
	}
</script>
<style>
</style>