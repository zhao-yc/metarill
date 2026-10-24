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
			uni.$off("onClickMenu", this.menuClickHandler);
		},
		onLoad: function(e) {
			var that = this;

			//默认加载main页
			import("@/pages/main/main").then(module => {
				that.asyncComponent = markRaw(module.default)
			})

			// 保存具名监听，避免反复进入首页后旧容器继续响应菜单事件。
			this.menuClickHandler = function(data) {
				if (data.path === "config_table-list-6191083005411328" || data.path === "config_page-list-6191175925956608") {
					that.isHomeContent = false;
				}

				if (data.path == "config_table-list-6191083005411328") {
					import("@/pages/yzp/config_table-list-6191083005411328").then(module => {
						that.asyncComponent = markRaw(module.default)
					})
				} else if (data.path == "config_page-list-6191175925956608") {
					import("@/pages/yzp/config_page-list-6191175925956608").then(module => {
						that.asyncComponent = markRaw(module.default)
					})
				}
			};
			uni.$on("onClickMenu", this.menuClickHandler);
		}
	}
</script>
<style>
</style>