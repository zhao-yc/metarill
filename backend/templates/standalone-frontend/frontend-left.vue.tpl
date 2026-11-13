<template>
	<view class="left-page">
		<menuPage ref="menuPages"></menuPage>
	</view>
</template>

<script>
	import menuPage from '@/pages/main/menu.vue'
	export default {
		components: {
			menuPage
		},
		data() {
			return {

			}
		},
		created(e) {

		}
	}
</script>

<style lang="scss">
	.left-page {
		height: 100%;
		width: calc(var(--window-left) - 1px);
		border-right: solid 1px $uni-border-2;
		overflow-y: auto;
	}
</style>