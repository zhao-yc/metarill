<template>
	<view>
		<uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="列排序"></uni-nav-bar>
		<view class="columns">
			<view class="column" v-for="(item ,index) in list">
				<text>
					{{item.name}} {{item.columnName}}
				</text>
				<uni-easyinput v-model='item.sortNo' type="number" :clearable="false"></uni-easyinput>
				<!-- <uni-icons type="arrow-up"></uni-icons>
				<uni-icons type="arrow-down"></uni-icons> -->
			</view>
		</view>
		<button @click="save">保存</button>
	</view>
</template>

<script>
	import utils from "@/common/utils";
	import yzp from "@/common/yzp";
	export default {
		data() {
			return {
				config_table_id: "",
				chkAll: {
					isInGrid: true,
					isInEdit: true,
					isMust: true,
					isReadOnly: true,
					isInFilter: true,
					isPrint: true
				},
				list: []
			}
		},
		onLoad: function(e) {
			var that = this;
			this.config_table_id = e.config_table_id;

			yzp.request({
				url: '/admin/custom/index/config_columns?configTableId=' + this.config_table_id,
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
		},
		methods: {
			save: function() {
				var that = this;
				yzp.request({
					url: '/admin/custom/index/save_config_columns',
					sendSessionId: true,
					data: this.list,
					method: "POST",
					success: function(res) {
						if (res.code == 200) {
							yzp.showSuccessToast();
							uni.navigateBack();
						} else {
							yzp.showErrorModal(res.msg);
						}
					},
				})
			}
		}
	}
</script>

<style>
	/* switch {
		transform: scale(0.7)
	} */
	.columns {}

	.columns .column {
		display: flex;
	}

	/* .columns .column uni-easyinput {
		width: 50px;
	} */
</style>