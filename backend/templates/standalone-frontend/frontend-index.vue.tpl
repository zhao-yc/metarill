<template>
	<view class='detail-page'>
		<keep-alive>
			<component :is="asyncComponent" :home-visible="homeVisible && isHomeContent"></component>
		</keep-alive>
	</view>
</template>
<script>
	import { markRaw } from "vue";
	export default {
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
			import("@/pages/main/main").then(module => {
				that.asyncComponent = markRaw(module.default)
			})

		}
	}
</script>
<style>
</style>