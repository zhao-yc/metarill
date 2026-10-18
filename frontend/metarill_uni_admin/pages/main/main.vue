<template>
	<view class="list-page main-home-page">
		<!-- 原有按钮 -->
		<view class="action-buttons">
			<button @click="generateProjectConfig" size="mini">生成项目配置</button>
			<button @click="syncRuntime" size="mini">同步运行时</button>
			<button @click="generateAllView" size="mini">生成全部页面</button>
		</view>
		<!-- 聚焦搜索 -->
		<view class="search-box">
			<view class="search-input-wrapper">
				<uni-easyinput v-model="keyword" placeholder="搜索配置表或配置页面（ID、名称、表名）" confirm-type="search"
					@confirm="doSearch" prefixIcon="search" />
			</view>
			<button size="mini" type="primary" @click="doSearch" class="search-btn">搜索</button>
		</view>

		<!-- 搜索结果 -->
		<view class="search-results" v-if="hasSearched">
			<!-- 配置表结果 -->
			<uni-section title="配置表" type="line" v-if="configTables.length > 0">
				<uni-table class="list-page-pc-body-table" border stripe emptyText="暂无更多数据">
					<uni-tr>
						<uni-th align="center" width="">操作</uni-th>
						<uni-th>id</uni-th>
						<uni-th>一级菜单</uni-th>
						<uni-th>名称</uni-th>
						<uni-th>表名</uni-th>
						<uni-th>默认冗余</uni-th>
						<uni-th>功能表</uni-th>
					</uni-tr>
					<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''"
						v-for="(item, index) in configTables" :key="item.id">
						<uni-td class="buttons">
							<view>
								<view class="list-action-link" @click="toConfigTableDetail(item.id)">查看</view>
								<view class="list-action-link" @click="configTableColumnSort(item)">列排序</view>
								<view class="list-action-link" @click="configTableGenerateAll(item)">一键生成</view>
							</view>
						</uni-td>
						<uni-td><text>{{ item.id }}</text></uni-td>
						<uni-td><text>{{ item.configFirstMenuName }}</text></uni-td>
						<uni-td><text class="list-column-important">{{ item.name }}</text></uni-td>
						<uni-td><text>{{ item.tableName }}</text></uni-td>
						<uni-td>{{ $checkboxRange[item.isRongyu == null ? 0 : item.isRongyu].text }}</uni-td>
						<uni-td>{{ $checkboxRange[item.isFunctionTable == null ? 0 : item.isFunctionTable].text
							}}</uni-td>
					</uni-tr>
				</uni-table>
			</uni-section>

			<!-- 配置页面结果 -->
			<uni-section title="配置页面" type="line" v-if="configPages.length > 0">
				<uni-table class="list-page-pc-body-table" border stripe emptyText="暂无更多数据">
					<uni-tr>
						<uni-th align="center" width="">操作</uni-th>
						<uni-th>id</uni-th>
						<uni-th>后台类型</uni-th>
						<uni-th>名称</uni-th>
						<uni-th>表名</uni-th>
						<uni-th>页面类型</uni-th>
					</uni-tr>
					<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''"
						v-for="(item, index) in configPages" :key="item.id">
						<uni-td class="buttons">
							<view>
								<view class="list-action-link" @click="toConfigPageDetail(item.id)">查看</view>
								<view class="list-action-link" @click="configPageGenerateAll(item)">生成代码</view>
								<view class="list-action-link" @click="configPageColumns(item)">列权限</view>
								<view class="list-action-link" @click="configPageToTable(item)">配置表</view>
							</view>
						</uni-td>
						<uni-td><text>{{ item.id }}</text></uni-td>
						<uni-td><text>{{ item.yzAdminTypeName }}</text></uni-td>
						<uni-td><text class="list-column-important">{{ item.name }}</text></uni-td>
						<uni-td><text>{{ item.configTableTableName }}</text></uni-td>
						<uni-td>{{ item.typeShow }}</uni-td>
					</uni-tr>
				</uni-table>
			</uni-section>

			<!-- 无结果 -->
			<view v-if="configTables.length === 0 && configPages.length === 0" class="no-result">
				<text>未找到匹配的结果</text>
			</view>
		</view>
	</view>
</template>

<script>
import utils from "@/common/utils.js";
import yzp from "@/common/yzp.js";
export default {
	props: {
		// 首页容器负责传递页面显隐，兼容异步组件首次挂载及详情返回。
		homeVisible: { type: Boolean, default: false }
	},
	data() {
		return {
			keyword: '',
			hasSearched: false,
			configTables: [],
			configPages: [],
			clicked_id: '',
			searchRequestVersion: 0
		}
	},
	watch: {
		homeVisible: {
			immediate: true,
			handler(visible) {
				if (visible) this.enterHome();
				else this.leaveHome();
			}
		}
	},
	beforeUnmount() {
		this.leaveHome();
	},
	methods: {
		/** 进入时重置配置搜索，并使旧响应失效。 */
		enterHome() { this.searchRequestVersion++; this.keyword=''; this.hasSearched=false; this.configTables=[]; this.configPages=[]; },
		/** 离开时使未返回的搜索响应失效。 */
		leaveHome() { this.searchRequestVersion++; },
		doSearch: function () {
			var that = this;
			const searchVersion = ++this.searchRequestVersion;
			if (utils.isBlank(this.keyword)) {
				that.hasSearched = false;
				that.configTables = [];
				that.configPages = [];
				return;
			}
			yzp.request({
				url: '/admin/custom/index/search',
				data: {
					keyword: this.keyword
				},
				sendSessionId: true,
				method: 'POST',
				success: function (res) {
					if (!that.homeVisible || searchVersion !== that.searchRequestVersion) return;
					if (res.code == 200) {
						that.hasSearched = true;
						that.configTables = res.data.configTables || [];
						that.configPages = res.data.configPages || [];
					} else {
						uni.showModal({
							showCancel: false,
							content: res.msg
						})
					}
				}
			});
		},
		// 配置表 - 查看详情
		toConfigTableDetail: function (id) {
			this.clicked_id = id;
			uni.navigateTo({
				url: '/pages/yzp/config_table-detail-6191083005411328?id=' + id
			})
		},
		// 配置表 - 列排序
		configTableColumnSort: function (item) {
			var that = this;
			this.clicked_id = item.id;
			yzp.request({
				url: "/admin/custom/config_table/config_columns_sort?id=" + item.id,
				sendSessionId: true,
				method: "POST",
				success: function (res) {
					yzp.customButtonHandler(res, that, false, 'list');
				}
			})
		},
		// 配置表 - 一键生成
		configTableGenerateAll: function (item) {
			var that = this;
			this.clicked_id = item.id;
			yzp.request({
				url: "/admin/custom/config_table/generate_all?id=" + item.id,
				sendSessionId: true,
				method: "POST",
				success: function (res) {
					yzp.customButtonHandler(res, that, false, 'list');
				}
			})
		},
		// 配置页面 - 查看详情
		toConfigPageDetail: function (id) {
			this.clicked_id = id;
			uni.navigateTo({
				url: '/pages/yzp/config_page-detail-6191175925956608?id=' + id
			})
		},
		// 配置页面 - 生成代码
		configPageGenerateAll: function (item) {
			var that = this;
			this.clicked_id = item.id;
			yzp.request({
				url: "/admin/custom/config_page/generate_all?id=" + item.id,
				sendSessionId: true,
				method: "POST",
				success: function (res) {
					yzp.customButtonHandler(res, that, false, 'list');
				}
			})
		},
		// 配置页面 - 列权限
		configPageColumns: function (item) {
			var that = this;
			this.clicked_id = item.id;
			yzp.request({
				url: "/admin/custom/config_page/config_page_columns?id=" + item.id,
				sendSessionId: true,
				method: "POST",
				success: function (res) {
					yzp.customButtonHandler(res, that, false, 'list');
				}
			})
		},
		// 配置页面 - 跳转配置表
		configPageToTable: function (item) {
			var that = this;
			this.clicked_id = item.id;
			yzp.request({
				url: "/admin/custom/config_page/to_config_table?id=" + item.id,
				sendSessionId: true,
				method: "POST",
				success: function (res) {
					yzp.customButtonHandler(res, that, false, 'list');
				}
			})
		},
		// 生成项目配置
		generateProjectConfig: function () {
			uni.showModal({
				title: "您确定要生成项目配置吗？",
				success(modalRes) {
					if (modalRes.confirm) {
						yzp.request({
							url: '/admin/custom/index/generate_project_config',
							sendSessionId: true,
							success: function (res) {
								yzp.requestSuccessHandler(res);
							}
						});
					}
				}
			})
		},
		// 同步运行时
		syncRuntime: function () {
			uni.showModal({
				title: "您确定要同步运行时吗？",
				success(modalRes) {
					if (modalRes.confirm) {
						yzp.request({
							url: '/admin/custom/index/sync_runtime',
							sendSessionId: true,
							success: function (res) {
								yzp.requestSuccessHandler(res);
							}
						});
					}
				}
			})
		},
		// 生成全部页面
		generateAllView: function () {
			uni.showModal({
				title: "您确定要生成全部页面吗？",
				success(modalRes) {
					if (modalRes.confirm) {
						yzp.request({
							url: '/admin/custom/index/generate_all_page',
							sendSessionId: true,
							success: function (res) {
								yzp.requestSuccessHandler(res);
							}
						});
					}
				}
			})
		}
	}
}
</script>

<style lang="scss">
.search-box {
	display: flex;
	align-items: center;
	padding: 15px;
	gap: 10px;
}

.search-input-wrapper {
	flex: 1;
}

.search-btn {
	flex-shrink: 0;
}

.search-results {
	padding: 0 15px 15px;
}

.no-result {
	text-align: center;
	padding: 30px 0;
	color: #999;
	font-size: 14px;
}

.action-buttons {
	display: flex;
	gap: 10px;
	padding: 15px;
	border-top: 1px solid #eee;

	uni-button {
		margin: 0;
	}
}



</style>
