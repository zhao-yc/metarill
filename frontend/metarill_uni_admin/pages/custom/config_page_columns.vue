<template>
	<view>
		<uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="列权限"></uni-nav-bar>
		<!-- {{config_page_id}} -->
		<uni-table border stripe emptyText="暂无更多数据">
			<!-- 表头行 -->
			<uni-tr>
				<uni-th>名称
				</uni-th>
				<!-- <uni-th>排序</uni-th> -->
				<uni-th>列表
					<checkbox :checked="chkAll.isInGrid" @click="chkAllChange('isInGrid')" />
				</uni-th>
				<!-- <uni-th>排序
					<checkbox :checked="chkAll.isSort" @click="chkAllChange('isSort')" />
				</uni-th> -->
				<uni-th>编辑
					<checkbox :checked="chkAll.isInEdit" @click="chkAllChange('isInEdit')" />
				</uni-th>
				<uni-th>必填
					<checkbox :checked="chkAll.isMust" @click="chkAllChange('isMust')" />
				</uni-th>
				<uni-th>只读
					<checkbox :checked="chkAll.isReadOnly" @click="chkAllChange('isReadOnly')" />
				</uni-th>
				<uni-th>查询
					<checkbox :checked="chkAll.isInFilter" @click="chkAllChange('isInFilter')" />
				</uni-th>
				<uni-th>着重
					<checkbox :checked="chkAll.isImportant" @click="chkAllChange('isImportant')" />
				</uni-th>
				<uni-th>打印
					<checkbox :checked="chkAll.isPrint" @click="chkAllChange('isPrint')" />
				</uni-th>
			</uni-tr>
			<!-- 表格数据行 -->
			<uni-tr v-for='(item, index) in list' :key='item.id'>
				<uni-td>
					{{item.configColumnName}} {{item.configColumnColumnName}}
				</uni-td>
				<!-- <uni-td>
					<uni-easyinput v-model='item.sortNo' type="number" style="width: 60px;"
						:clearable="false"></uni-easyinput>
				</uni-td> -->
				<uni-td>
					<checkbox :checked="item.isInGrid" @click="chkChange(index,'isInGrid')" />
				</uni-td>
				<!-- <uni-td>
					<checkbox :checked="item.isSort" @click="chkChange(index,'isSort')" />
				</uni-td> -->
				<uni-td>
					<checkbox :checked="item.isInEdit" @click="chkChange(index,'isInEdit')" />
				</uni-td>
				<uni-td>
					<checkbox :checked="item.isMust" @click="chkChange(index,'isMust')" />
				</uni-td>
				<uni-td>
					<checkbox :checked="item.isReadOnly" @click="chkChange(index,'isReadOnly')" />
				</uni-td>
				<uni-td>
					<checkbox :checked="item.isInFilter" @click="chkChange(index,'isInFilter')" />
				</uni-td>
				<uni-td>
					<checkbox :checked="item.isImportant" @click="chkChange(index,'isImportant')" />
				</uni-td>
				<uni-td>
					<checkbox :checked="item.isPrint" @click="chkChange(index,'isPrint')" />
				</uni-td>
			</uni-tr>
		</uni-table>
		<button @click="save">保存</button>
	</view>
</template>

<script>
	import utils from "@/common/utils";
	import yzp from "@/common/yzp";
	import { pageFieldPatchPayload } from '@/common/metadataWorkbenchPayload';
	export default {
		data() {
			return {
				config_page_id: "",
				chkAll: {
					isInGrid: true,
					// isSort: true,
					isInEdit: true,
					isMust: true,
					isReadOnly: true,
					isInFilter: true,
					isImportant: true,
					isPrint: true
				},
				revision: '',
				list: []
			}
		},
		onLoad: function(e) {
			var that = this;
			this.config_page_id = e.config_page_id;

			yzp.request({
				url: '/admin/custom/index/config_page_columns?configPageId=' + this.config_page_id,
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
			chkAllChange: function(key) {
				var val = false;
				switch (key) {
					case "isInGrid":
						val = this.chkAll.isInGrid = this.changeValue(this.chkAll.isInGrid);
						break;
						// case "isSort":
						// 	val = this.chkAll.isSort = this.changeValue(this.chkAll.isSort);
						// 	break;
					case "isInEdit":
						val = this.chkAll.isInEdit = this.changeValue(this.chkAll.isInEdit);
						break;
					case "isMust":
						val = this.chkAll.isMust = this.changeValue(this.chkAll.isMust);
						break;
					case "isReadOnly":
						val = this.chkAll.isReadOnly = this.changeValue(this.chkAll.isReadOnly);
						break;
					case "isInFilter":
						val = this.chkAll.isInFilter = this.changeValue(this.chkAll.isInFilter);
						break;
					case "isImporant":
						val = this.chkAll.isImporant = this.changeValue(this.chkAll.isImporant);
						break;
					case "isPrint":
						val = this.chkAll.isPrint = this.changeValue(this.chkAll.isPrint);
						break;
				}
				console.log(this.chkAll);

				for (var i = 0; i < this.list.length; i++) {
					this.chkChange(i, key, val);
				}
			},
			chkChange: function(index, key, val) {
				console.log(index);
				console.log(this.list[index])
				switch (key) {
					case "isInGrid":
						this.list[index].isInGrid = val != undefined ? val : this.changeValue(this.list[index]
							.isInGrid);
						break;
						// case "isSort":
						// 	this.list[index].isSort = val != undefined ? val : this.changeValue(this.list[index]
						// 		.isSort);
						// 	break;
					case "isInEdit":
						this.list[index].isInEdit = val != undefined ? val : this.changeValue(this.list[index]
							.isInEdit);
						break;
					case "isMust":
						this.list[index].isMust = val != undefined ? val : this.changeValue(this.list[index].isMust);
						break;
					case "isReadOnly":
						this.list[index].isReadOnly = val != undefined ? val : this.changeValue(this.list[index]
							.isReadOnly);
						break;
					case "isInFilter":
						this.list[index].isInFilter = val != undefined ? val : this.changeValue(this.list[index]
							.isInFilter);
						break;
					case "isImportant":
						this.list[index].isImportant = val != undefined ? val : this.changeValue(this.list[index]
							.isImportant);
						break;
					case "isPrint":
						this.list[index].isPrint = val != undefined ? val : this.changeValue(this.list[index].isPrint);
						break;
				}
			},
			save: function() {
				var that = this;
				// 并发版本来自本次读取快照；缺失时禁止覆盖服务端配置。
				if (!this.revision) { yzp.showErrorModal('请重新进入此页面读取最新配置后保存'); return; }
				yzp.request({
					url: '/admin/custom/index/save_config_page_columns',
					sendSessionId: true,
					data: pageFieldPatchPayload(this.list),
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
			},
			changeValue: function(oldVal) {
				if (oldVal == 1) {
					return 0;
				} else {
					return 1;
				}
			}
		}
	}
</script>

<style>
	/* switch {
		transform: scale(0.7)
	} */
</style>