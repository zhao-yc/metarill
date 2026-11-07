<template>
	<view class='detail-page'>
		<!-- #ifdef H5 -->
		<keep-alive>
			<component :is="asyncComponent" :home-visible="homeVisible && isHomeContent"></component>
		</keep-alive>
		<!-- #endif -->
		<!-- #ifndef H5 -->
		<!-- 非Web静态登记原组件；访问后保留实例，避免切换菜单丢失页面状态。 -->
		<main-content v-show="nativeContent === 'main'" :home-visible="homeVisible && isHomeContent"></main-content>
		<table-content v-if="nativeVisited.table" v-show="nativeContent === 'table'"></table-content>
		<page-content v-if="nativeVisited.page" v-show="nativeContent === 'page'"></page-content>
		<!-- #endif -->
	</view>
</template>
<script>
	// #ifdef H5
	import { markRaw } from "vue";
	// #endif
	// #ifndef H5
	import MainContent from "@/pages/main/main.vue";
	import TableContent from "@/pages/yzp/config_table-list-6191083005411328.vue";
	import PageContent from "@/pages/yzp/config_page-list-6191175925956608.vue";
	// #endif
	export default {
		// #ifndef H5
		components: {MainContent, TableContent, PageContent},
		// #endif
		data() {
			return {
				asyncComponent: null,
				// #ifndef H5
				nativeContent: 'main',
				nativeVisited: { table: false, page: false },
				// #endif
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
			// #ifdef H5
			import("@/pages/main/main").then(module => {
				that.asyncComponent = markRaw(module.default)
			})
			// #endif

			// 保存具名监听，避免反复进入首页后旧容器继续响应菜单事件。
			this.menuClickHandler = function(data) {
				if (data.path === "config_table-list-6191083005411328" || data.path === "config_page-list-6191175925956608") {
					that.isHomeContent = false;
				}

				// #ifndef H5
				// 原两个配置菜单仅切换可见实例，未知菜单不改当前页面。
				const target = data.path === "config_table-list-6191083005411328" ? "table" : data.path === "config_page-list-6191175925956608" ? "page" : null;
				if (target) { that.nativeVisited[target] = true; that.nativeContent = target; }
				// #endif
				if (data.path == "config_table-list-6191083005411328") {
			// #ifdef H5
					import("@/pages/yzp/config_table-list-6191083005411328").then(module => {
						that.asyncComponent = markRaw(module.default)
					})
			// #endif
				} else if (data.path == "config_page-list-6191175925956608") {
			// #ifdef H5
					import("@/pages/yzp/config_page-list-6191175925956608").then(module => {
						that.asyncComponent = markRaw(module.default)
					})
			// #endif
				}
			};
			uni.$on("onClickMenu", this.menuClickHandler);
		}
	}
</script>
<style>
</style>