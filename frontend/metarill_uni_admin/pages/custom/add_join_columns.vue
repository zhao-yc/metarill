<template>
	<view>
		<uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false"
			title="添加联接字段"></uni-nav-bar>
		<view class="join-page">
			<view class="join-summary">
				<text>{{detail.configTableName || '配置表'}}</text>
				<text v-if="detail.tableName">{{detail.tableName}}</text>
				<text>已选 {{selectedCount}} 项</text>
			</view>

			<view v-if="loading" class="join-empty">加载中</view>
			<view v-else-if="groups.length == 0" class="join-empty">暂无可添加的联接字段</view>
			<view v-else>
				<view class="join-tags">
					<uni-tag v-for="group in groups" :key="group.sourceColumnId" size="small" circle
						:type="isActiveGroup(group) ? 'primary' : 'default'" :inverted="!isActiveGroup(group)"
						:text="buildGroupTagText(group)" @click="setActiveGroup(group)"></uni-tag>
				</view>

				<view v-if="activeGroup">
					<view class="join-group-head">
						<text>{{activeGroup.sourceName || activeGroup.sourceColumnName}}</text>
						<text class="join-meta">{{activeGroup.sourceColumnName}} -> {{activeGroup.targetTableName || '未识别'}}</text>
						<button size="mini" class="group-button" :disabled="!activeGroup.fields || activeGroup.fields.length == 0"
							@click="toggleGroup(activeGroup)">{{isGroupAllSelected(activeGroup) ? '取消' : '全选'}}</button>
					</view>

					<view v-if="activeGroup.resolveMessage" class="join-warning">{{activeGroup.resolveMessage}}</view>
					<view v-else-if="!activeGroup.fields || activeGroup.fields.length == 0" class="join-empty">无可添加字段</view>
					<uni-table v-else border stripe emptyText="暂无更多数据">
						<uni-tr>
							<uni-th width="70">选择</uni-th>
							<uni-th>名称</uni-th>
							<uni-th>字段</uni-th>
							<uni-th>类型</uni-th>
						</uni-tr>
						<uni-tr v-for="field in activeGroup.fields" :key="field.targetColumnId"
							@click="toggleField(activeGroup, field)">
							<uni-td>
								<checkbox :checked="isSelected(activeGroup, field)"
									@click.stop="toggleField(activeGroup, field)" />
							</uni-td>
							<uni-td>{{field.name || field.columnName}}</uni-td>
							<uni-td>{{field.columnName}} -> {{field.joinColumnName}}</uni-td>
							<uni-td>{{field.columnType}}{{field.columnLength ? '(' + field.columnLength + ')' : ''}}</uni-td>
						</uni-tr>
					</uni-table>
				</view>
			</view>

			<view class="join-actions">
				<button type="primary" size="mini" :disabled="selectedCount == 0 || saving" @click="save">添加</button>
			</view>
		</view>
	</view>
</template>

<script>
	import yzp from "@/common/yzp";
	export default {
		data() {
			return {
				config_table_id: "",
				detail: {
					groups: []
				},
				selectedMap: {},
				activeSourceColumnId: "",
				loading: false,
				saving: false
			}
		},
		computed: {
			groups() {
				return this.detail && this.detail.groups ? this.detail.groups : [];
			},
			activeGroup() {
				if (this.groups.length == 0) {
					return null;
				}
				for (var i = 0; i < this.groups.length; i++) {
					if (this.groups[i].sourceColumnId == this.activeSourceColumnId) {
						return this.groups[i];
					}
				}
				return this.groups[0];
			},
			selectedCount() {
				return Object.keys(this.selectedMap).length;
			}
		},
		onLoad: function(e) {
			this.config_table_id = e.config_table_id;
			this.loadCandidates();
		},
		methods: {
			// 加载候选项时清空已选状态，避免刷新后提交旧字段。
			loadCandidates: function() {
				var that = this;
				this.loading = true;
				yzp.request({
					url: '/admin/custom/config_table/join_column_candidates',
					sendSessionId: true,
					data: {
						configTableId: this.config_table_id
					},
					method: "POST",
					success: function(res) {
						if (res.code == 200) {
							that.detail = res.data || {
								groups: []
							};
							that.selectedMap = {};
							that.ensureActiveGroup();
						} else {
							yzp.showErrorModal(res.msg);
						}
					},
					complete: function() {
						that.loading = false;
					}
				})
			},
			selectionKey: function(group, field) {
				return group.sourceColumnId + ":" + field.targetColumnId;
			},
			// 候选刷新后固定当前标签，原标签不存在时回到第一个关系字段。
			ensureActiveGroup: function() {
				if (this.groups.length == 0) {
					this.activeSourceColumnId = "";
					return;
				}
				for (var i = 0; i < this.groups.length; i++) {
					if (this.groups[i].sourceColumnId == this.activeSourceColumnId) {
						return;
					}
				}
				this.activeSourceColumnId = this.groups[0].sourceColumnId;
			},
			setActiveGroup: function(group) {
				this.activeSourceColumnId = group.sourceColumnId;
			},
			isActiveGroup: function(group) {
				return this.activeGroup && this.activeGroup.sourceColumnId == group.sourceColumnId;
			},
			groupSelectedCount: function(group) {
				if (!group.fields || group.fields.length == 0) {
					return 0;
				}
				var count = 0;
				for (var i = 0; i < group.fields.length; i++) {
					if (this.isSelected(group, group.fields[i])) {
						count++;
					}
				}
				return count;
			},
			// 标签文案只保留关系字段名和已选数量，避免多个关系字段同时铺开。
			buildGroupTagText: function(group) {
				var title = group.sourceName || group.sourceColumnName || "关系字段";
				var count = this.groupSelectedCount(group);
				var total = group.fields ? group.fields.length : 0;
				return title + " " + count + "/" + total;
			},
			isSelected: function(group, field) {
				return !!this.selectedMap[this.selectionKey(group, field)];
			},
			isGroupAllSelected: function(group) {
				if (!group.fields || group.fields.length == 0) {
					return false;
				}
				for (var i = 0; i < group.fields.length; i++) {
					if (!this.isSelected(group, group.fields[i])) {
						return false;
					}
				}
				return true;
			},
			// 通过整体替换对象触发响应式更新，兼容 H5 运行时。
			toggleField: function(group, field) {
				var key = this.selectionKey(group, field);
				var nextMap = Object.assign({}, this.selectedMap);
				if (nextMap[key]) {
					delete nextMap[key];
				} else {
					nextMap[key] = {
						sourceColumnId: group.sourceColumnId,
						targetColumnId: field.targetColumnId
					};
				}
				this.selectedMap = nextMap;
			},
			toggleGroup: function(group) {
				var nextMap = Object.assign({}, this.selectedMap);
				var isAllSelected = this.isGroupAllSelected(group);
				for (var i = 0; i < group.fields.length; i++) {
					var field = group.fields[i];
					var key = this.selectionKey(group, field);
					if (isAllSelected) {
						delete nextMap[key];
					} else {
						nextMap[key] = {
							sourceColumnId: group.sourceColumnId,
							targetColumnId: field.targetColumnId
						};
					}
				}
				this.selectedMap = nextMap;
			},
			getSelections: function() {
				var result = [];
				var keys = Object.keys(this.selectedMap);
				for (var i = 0; i < keys.length; i++) {
					result.push(this.selectedMap[keys[i]]);
				}
				return result;
			},
			save: function() {
				var that = this;
				var selections = this.getSelections();
				if (selections.length == 0) {
					uni.showToast({
						title: '请先选择字段',
						icon: 'none'
					});
					return;
				}

				uni.showModal({
					title: "确认添加",
					content: "将添加 " + selections.length + " 个联接字段",
					success(modalRes) {
						if (!modalRes.confirm) {
							return;
						}
						that.saving = true;
						yzp.request({
							url: '/admin/custom/config_table/save_join_columns',
							sendSessionId: true,
							data: {
								configTableId: that.config_table_id,
								selections: selections
							},
							method: "POST",
							success: function(res) {
								if (res.code == 200) {
									yzp.showSuccessToast();
									uni.navigateBack();
								} else {
									yzp.showErrorModal(res.msg);
								}
							},
							complete: function() {
								that.saving = false;
							}
						})
					}
				})
			}
		}
	}
</script>

<style>
	.join-page {
		padding: 16px 20px 28px;
	}

	.join-summary,
	.join-tags,
	.join-group-head {
		display: flex;
		align-items: center;
		flex-wrap: wrap;
		gap: 8px;
		margin-bottom: 12px;
	}

	.join-meta {
		color: #666;
		font-size: 12px;
	}

	.join-empty,
	.join-warning {
		padding: 20px;
		text-align: center;
	}

	.join-warning {
		color: #f0ad4e;
	}

	.join-actions {
		display: flex;
		align-items: center;
		justify-content: flex-end;
		margin-top: 16px;
		padding-bottom: 4px;
	}

	/* 只限制操作区按钮尺寸，避免 uni-button 在 PC 上铺满整行。 */
	.join-actions button,
	.join-actions uni-button {
		min-width: 90px;
		width: auto;
		margin: 0;
	}

	@media screen and (max-width: 768px) {
		.join-page {
			padding: 12px;
		}
	}
</style>
