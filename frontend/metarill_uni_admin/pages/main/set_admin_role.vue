<template>
	<view>
		<uni-section title="第一步:选择管理员" type="line">
			<uni-data-select v-model="value" :localdata="list" @change="change"></uni-data-select>
		</uni-section>
		<uni-section title="第二步:选择该管理员的角色" type="line">

		</uni-section>

		<button @click="save">保存</button>
	</view>
</template>

<script>
	import utils from "@/common/utils.js";
	import yzp from "@/common/yzp.js";
	export default {
		data() {
			return {
				list: []
			}
		},
		onLoad: function(e) {
			this.initLists();
		},
		methods: {
			change: function(e) {
				var temp = this.list.find(function(item) {
					return item.value == e;
				});

			},
			initLists: function() {
				var that = this;
				yzp.request({
					url: '/admin/sys/index/findNameIdYzManager',
					sendSessionId: true,
					success: function(res) {
						if (res.code == 200) {
							that.list = res.data;
						} else {
							uni.showModal({
								showCancel: false,
								content: res.msg
							})
						}
					},
				})

			}
		}
	}
</script>

<style>

</style>