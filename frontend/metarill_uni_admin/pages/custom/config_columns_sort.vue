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
	import { columnOrderPatchPayload } from '@/common/metadataWorkbenchPayload';
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
				revision: '',
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
						that.revision = res.map && res.map.revision || '';
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
				// 并发版本来自本次读取快照；缺失时禁止覆盖服务端配置。
				if (!this.revision) { yzp.showErrorModal('请重新进入此页面读取最新配置后保存'); return; }
				yzp.request({
					url: '/admin/custom/index/save_config_columns',
					sendSessionId: true,
					data: columnOrderPatchPayload(this.list),
					header: { 'X-Metarill-Metadata-Revision': this.revision },
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