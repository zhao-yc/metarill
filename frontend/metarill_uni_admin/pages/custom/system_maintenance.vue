<template>
	<view class="system-maintenance-page" :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }">
		<uni-nav-bar left-icon="left" @clickLeft="leavePage" fixed statusBar :border="false" title="项目配置与维护"></uni-nav-bar>

		<view class="system-maintenance-content">
			<view class="maintenance-tabs">
				<button size="mini" :class="{ selected: activeTab === 'menus' }" @click="changeTab('menus')">菜单配置</button>
				<button size="mini" :class="{ selected: activeTab === 'data' }" @click="changeTab('data')">数据维护</button>
			</view>
			<yzp-menu-workbench v-if="activeTab === 'menus'" ref="menuWorkbench" />
			<uni-section v-else class="base-info" title="回填联接字段值" type="line">
				<view class="section-view">
					<uni-forms label-width="96px">
						<uni-forms-item label="配置表" required>
							<zxz-uni-data-select v-model="configTableId" :localdata="tableList" filterable
								dataKey="name" dataValue="id" placeholder="请选择配置表" emptyTips="暂无冗余表"
								@change="handleTableChange" />
						</uni-forms-item>
					</uni-forms>

					<view v-if="selectedTable" class="maintenance-summary">
						<text>{{selectedTable.rawName}}</text>
						<text v-if="selectedTable.tableName"> / {{selectedTable.tableName}}</text>
					</view>

					<view v-if="result" class="maintenance-result">
						<view class="maintenance-summary">
							<text>联接字段 {{result.joinColumnCount || 0}} 个，影响行数 {{result.affectedRows || 0}}</text>
						</view>
						<uni-table border stripe emptyText="暂无回填明细">
							<uni-tr>
								<uni-th>联接字段</uni-th>
								<uni-th>来源</uni-th>
								<uni-th width="100">影响行数</uni-th>
							</uni-tr>
							<uni-tr v-for="item in resultDetails" :key="item.columnName">
								<uni-td>{{item.columnName}}</uni-td>
								<uni-td>{{item.targetTableName}}.{{item.targetColumnName}}</uni-td>
								<uni-td>{{item.affectedRows}}</uni-td>
							</uni-tr>
						</uni-table>
					</view>

					<view class="maintenance-actions">
						<button type="primary" size="mini" :disabled="!configTableId || backfilling"
							@click="confirmBackfill">{{backfilling ? '回填中' : '回填联接字段值'}}</button>
					</view>
				</view>
			</uni-section>
		</view>
	</view>
</template>

<script>
	import yzp from "@/common/yzp_runtime/yzp-runtime";
	import YzpMenuWorkbench from "@/components/yzp-menu-workbench/yzp-menu-workbench.vue";

	export default {
		components: { YzpMenuWorkbench },
		data() {
			return {
				activeTab: "data",
				tablesLoaded: false,
				configTableId: "",
				tableList: [],
				selectedTable: null,
				result: null,
				backfilling: false
			}
		},
		computed: {
			resultDetails() {
				return this.result && this.result.details ? this.result.details : [];
			}
		},
		onLoad(options) {
			this.activeTab = options && options.tab === 'menus' ? 'menus' : 'data';
			if (this.activeTab === 'data') this.loadTables();
		},
		methods: {
			/** 离开工作台前让组件保留草稿或确认放弃，避免切页签丢失编辑。 */
			async canLeaveMenus() {
				return !this.$refs.menuWorkbench || await this.$refs.menuWorkbench.canLeave();
			},
			async leavePage() { if (await this.canLeaveMenus()) yzp.back(); },
			async changeTab(tab) {
				if (tab === this.activeTab || !await this.canLeaveMenus()) return;
				this.activeTab = tab;
				if (tab === 'data' && !this.tablesLoaded) this.loadTables();
			},
			// 加载当前项目的冗余表，作为可筛选下拉的数据源。
			loadTables() {
				var that = this;
				yzp.request({
					url: "/admin/sys/system_maintenance/tables",
					sendSessionId: true,
					method: "POST",
					success: function(res) {
						if (res.code == 200) {
							that.tableList = res.data || [];
							that.tablesLoaded = true;
						} else {
							yzp.showErrorModal(res.msg);
						}
					}
				});
			},
			handleTableChange(item) {
				this.selectedTable = item || null;
				this.result = null;
			},
			confirmBackfill() {
				if (!this.configTableId) {
					uni.showToast({
						title: "请选择配置表",
						icon: "none"
					});
					return;
				}

				var that = this;
				uni.showModal({
					title: "确认回填",
					content: "将重算并覆盖当前表已有联接字段值",
					success: function(modalRes) {
						if (modalRes.confirm) {
							that.backfillJoinColumns();
						}
					}
				});
			},
			// 只触发元数据对应的物理字段回填，不修改表结构和配置。
			backfillJoinColumns() {
				var that = this;
				this.backfilling = true;
				yzp.request({
					url: "/admin/sys/system_maintenance/backfill_join_columns",
					sendSessionId: true,
					method: "POST",
					data: {
						configTableId: this.configTableId
					},
					success: function(res) {
						if (res.code == 200) {
							that.result = res.data || {};
							yzp.showSuccessToast();
						} else {
							yzp.showErrorModal(res.msg);
						}
					},
					complete: function() {
						that.backfilling = false;
					}
				});
			}
		}
	}
</script>

<style lang="scss">
	.maintenance-tabs { display: flex; gap: 8px; border-bottom: 1px solid #e4e9e5; padding-bottom: 14px; margin-bottom: 20px; }
	.maintenance-tabs button { margin: 0; background: #f5f7f5; color: #59655d; }
	.maintenance-tabs button.selected { background: #e3eee7; color: #1e6546; }
	.system-maintenance-page {
		min-height: 100vh;
		background: #f5f5f5;
	}

	.system-maintenance-content {
		/* 主内容区作为独立圆角容器，和页面背景形成清晰边界。 */
		margin: $uni-spacing-base;
		padding: $uni-spacing-base;
		background: $uni-white;
		border-radius: $uni-border-radius;
		min-height: 360px;
		box-sizing: border-box;
		overflow: hidden;
	}

	.maintenance-summary {
		margin-bottom: 12px;
		color: #606266;
		font-size: 14px;
		line-height: 24px;
	}

	.maintenance-result {
		margin-top: 12px;
	}

	.maintenance-actions {
		display: flex;
		justify-content: center;
		margin-top: 16px;
	}

	.maintenance-actions button {
		min-width: 132px;
		margin: 0;
	}
</style>
