<template>
	<view>
		<uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false"
			title="索引管理"></uni-nav-bar>
		<view class="index-page">
			<view class="page-summary">
				<view>
					<text class="summary-title">{{detail.configTableName || '配置表'}}</text>
					<text v-if="detail.tableName" class="summary-table">{{detail.tableName}}</text>
				</view>
				<view class="summary-actions">
					<text class="summary-count">共 {{indexes.length}} 个托管索引</text>
					<button size="mini" @click="addIndex">新增索引</button>
				</view>
			</view>

			<view class="ownership-tip">
				只维护 <text>yzp_idx_</text> 和 <text>yzp_uk_</text> 前缀索引；数据库中的其他索引不会被删除。
			</view>

			<view v-if="loading" class="empty-state">加载中</view>
			<view v-else-if="indexes.length == 0" class="empty-state">
				<text>当前表尚未配置业务索引</text>
				<button type="primary" size="mini" @click="addIndex">创建第一个索引</button>
			</view>

			<view v-else class="index-list">
				<view v-for="(item, index) in indexes" :key="item.clientKey" class="index-card">
					<view class="index-head">
						<view class="index-title-row">
							<text class="index-number">索引 {{index + 1}}</text>
							<text :class="['index-badge', item.indexType == 'UNIQUE' ? 'unique' : 'normal']">
								{{item.indexType == 'UNIQUE' ? '唯一索引' : '普通索引'}}
							</text>
							<text v-if="!toEnabled(item.isEnabled)" class="disabled-badge">已停用</text>
						</view>
						<button type="warn" plain size="mini" :disabled="saving" @click="removeIndex(index, item)">删除</button>
					</view>

					<view class="index-form-grid">
						<view class="form-item">
							<text class="form-label">索引类型</text>
							<zxz-uni-data-select v-model="item.indexType" :localdata="indexTypeOptions"
								dataKey="text" dataValue="value" :clear="false" @change="onIndexTypeChange(item)" />
						</view>
						<view class="form-item name-item">
							<text class="form-label">索引名称</text>
							<view class="name-input-row">
								<text class="name-prefix">{{prefixOf(item.indexType)}}</text>
								<uni-easyinput v-model="item.nameSuffix" :clearable="false" trim="both"
									placeholder="请输入英文、数字或下划线" @input="syncPhysicalName(item)" />
							</view>
							<text class="name-preview">物理名：{{physicalName(item) || '待填写'}}</text>
						</view>
						<view class="form-item enabled-item">
							<text class="form-label">启用</text>
							<switch :checked="toEnabled(item.isEnabled)" color="#2979ff"
								@change="item.isEnabled = $event.detail.value ? 1 : 0" />
						</view>
					</view>

					<view class="columns-head">
						<view>
							<text class="columns-title">联合索引字段</text>
							<text class="columns-note">字段排列即数据库索引顺序</text>
						</view>
						<button size="mini" @click="addColumn(item)">添加字段</button>
					</view>

					<view v-if="!item.columns || item.columns.length == 0" class="column-empty">请至少添加一个索引字段</view>
					<view v-else class="column-list">
						<view v-for="(column, columnIndex) in item.columns" :key="column.clientKey" class="column-row">
							<text class="column-order">{{columnIndex + 1}}</text>
							<view class="column-select">
								<zxz-uni-data-select v-model="column.columnName" :localdata="availableColumnOptions(item, column)"
									filterable dataKey="label" dataValue="columnName" :clear="false"
									@change="onColumnChange(column)" />
							</view>
							<view class="direction-select">
								<zxz-uni-data-select v-model="column.sortDirection" :localdata="directionOptions"
									dataKey="text" dataValue="value" :clear="false" />
							</view>
							<view class="column-actions">
								<button size="mini" :disabled="columnIndex == 0" @click="moveColumn(item, columnIndex, -1)">上移</button>
								<button size="mini" :disabled="columnIndex == item.columns.length - 1"
									@click="moveColumn(item, columnIndex, 1)">下移</button>
								<button type="warn" plain size="mini" @click="removeColumn(item, columnIndex)">移除</button>
							</view>
						</view>
					</view>

					<view v-if="item.columns && item.columns.length" class="definition-preview">
						<text>{{definitionPreview(item)}}</text>
					</view>
				</view>
			</view>

			<view class="page-actions">
				<button size="mini" :disabled="saving" @click="loadConfig">重新加载</button>
				<button type="primary" size="mini" :loading="saving" :disabled="saving" @click="save">保存索引配置</button>
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
					configTableName: "",
					tableName: "",
					columnOptions: [],
					indexes: []
				},
				indexes: [],
				loading: false,
				saving: false,
				clientSequence: 0,
				indexTypeOptions: [{
					text: "普通索引",
					value: "NORMAL"
				}, {
					text: "唯一索引",
					value: "UNIQUE"
				}],
				directionOptions: [{
					text: "升序 ASC",
					value: "ASC"
				}, {
					text: "降序 DESC",
					value: "DESC"
				}]
			}
		},
		onLoad: function(e) {
			this.config_table_id = e.config_table_id || e.id || "";
			this.loadConfig();
		},
		methods: {
			// 每个临时节点使用稳定键，避免排序时 Vue 复用错误的下拉组件。
			nextClientKey: function(prefix) {
				this.clientSequence++;
				return prefix + "-" + this.clientSequence;
			},
			loadConfig: function() {
				if (!this.config_table_id) {
					yzp.showErrorModal("缺少配置表ID");
					return;
				}
				var that = this;
				this.loading = true;
				yzp.request({
					url: "/admin/custom/config_table/index_config",
					sendSessionId: true,
					method: "POST",
					data: {
						configTableId: this.config_table_id
					},
					success: function(res) {
						if (res.code == 200) {
							that.detail = res.data || {};
							that.indexes = (that.detail.indexes || []).map(that.normalizeIndex);
						} else {
							yzp.showErrorModal(res.msg);
						}
					},
					complete: function() {
						that.loading = false;
					}
				});
			},
			// 后端保存完整物理名；页面只编辑后缀，避免用户破坏托管前缀。
			normalizeIndex: function(source) {
				var item = Object.assign({}, source || {});
				item.indexType = item.indexType == "UNIQUE" ? "UNIQUE" : "NORMAL";
				item.isEnabled = this.toEnabled(item.isEnabled) ? 1 : 0;
				item.nameSuffix = this.stripManagedPrefix(item.indexName || "");
				item.clientKey = item.id || this.nextClientKey("index");
				item.columns = (item.columns || []).map(function(sourceColumn) {
					var column = Object.assign({}, sourceColumn || {});
					column.sortDirection = column.sortDirection == "DESC" ? "DESC" : "ASC";
					column.clientKey = column.id || this.nextClientKey("column");
					return column;
				}, this);
				this.syncPhysicalName(item);
				return item;
			},
			toEnabled: function(value) {
				return value === true || value === 1 || value === "1";
			},
			prefixOf: function(indexType) {
				return indexType == "UNIQUE" ? "yzp_uk_" : "yzp_idx_";
			},
			stripManagedPrefix: function(indexName) {
				return String(indexName || "").replace(/^yzp_(idx|uk)_/i, "");
			},
			physicalName: function(item) {
				var suffix = String(item.nameSuffix || "").trim();
				return suffix ? this.prefixOf(item.indexType) + suffix : "";
			},
			syncPhysicalName: function(item) {
				item.indexName = this.physicalName(item);
			},
			onIndexTypeChange: function(item) {
				this.syncPhysicalName(item);
			},
			addIndex: function() {
				this.indexes.push(this.normalizeIndex({
					id: "",
					indexType: "NORMAL",
					indexName: "",
					isEnabled: 1,
					columns: []
				}));
			},
			removeIndex: function(index, item) {
				var that = this;
				var title = item.indexName || "未命名索引";
				uni.showModal({
					title: "确认删除",
					content: "将删除索引配置“" + title + "”。保存仅删除索引配置；物理索引需单独预览迁移方案，首期不会自动删除。",
					success: function(res) {
						if (res.confirm) {
							that.indexes.splice(index, 1);
						}
					}
				});
			},
			addColumn: function(item) {
				if (!item.columns) {
					item.columns = [];
				}
				var options = this.availableColumnOptions(item, {});
				item.columns.push({
					id: "",
					configColumnId: "",
					columnName: options.length ? options[0].columnName : "",
					sortDirection: "ASC",
					clientKey: this.nextClientKey("column")
				});
				this.onColumnChange(item.columns[item.columns.length - 1]);
			},
			removeColumn: function(item, index) {
				item.columns.splice(index, 1);
			},
			moveColumn: function(item, index, offset) {
				var target = index + offset;
				if (target < 0 || target >= item.columns.length) {
					return;
				}
				var nextColumns = item.columns.slice();
				var moved = nextColumns.splice(index, 1)[0];
				nextColumns.splice(target, 0, moved);
				item.columns = nextColumns;
			},
			// 已选择字段从同一索引的其他下拉中隐藏，当前值始终保留。
			availableColumnOptions: function(item, currentColumn) {
				var selected = {};
				(item.columns || []).forEach(function(column) {
					if (column !== currentColumn && column.columnName) {
						selected[column.columnName] = true;
					}
				});
				return (this.detail.columnOptions || []).filter(function(option) {
					return !selected[option.columnName];
				});
			},
			// 系统字段没有 config_column_id；普通字段由候选项反查并随请求提交。
			onColumnChange: function(column) {
				var options = this.detail.columnOptions || [];
				column.configColumnId = "";
				for (var i = 0; i < options.length; i++) {
					if (options[i].columnName == column.columnName) {
						column.configColumnId = options[i].configColumnId || "";
						return;
					}
				}
			},
			definitionPreview: function(item) {
				var type = item.indexType == "UNIQUE" ? "UNIQUE" : "INDEX";
				var fields = (item.columns || []).map(function(column) {
					return (column.columnName || "?") + " " + (column.sortDirection == "DESC" ? "DESC" : "ASC");
				});
				return (item.indexName || "待填写索引名") + "  " + type + " (" + fields.join(", ") + ")";
			},
			buildPayload: function() {
				return {
					configTableId: this.config_table_id,
					// 版本来自上次快照，防止旧草稿覆盖其他管理员的索引配置。
					revision: this.detail.revision || "",
					indexes: this.indexes.map(function(item) {
						this.syncPhysicalName(item);
						return {
							id: item.id || "",
							indexName: item.indexName,
							indexType: item.indexType,
							isEnabled: this.toEnabled(item.isEnabled) ? 1 : 0,
							columns: (item.columns || []).map(function(column, index) {
								return {
									id: column.id || "",
									configColumnId: column.configColumnId || "",
									columnName: column.columnName,
									sortNo: (index + 1) * 10,
									sortDirection: column.sortDirection == "DESC" ? "DESC" : "ASC"
								};
							})
						};
					}, this)
				};
			},
			validate: function() {
				var names = {};
				for (var i = 0; i < this.indexes.length; i++) {
					var item = this.indexes[i];
					this.syncPhysicalName(item);
					if (!item.nameSuffix || !String(item.nameSuffix).trim()) {
						return "第 " + (i + 1) + " 个索引名称不能为空";
					}
					if (!/^[A-Za-z0-9_]+$/.test(item.nameSuffix)) {
						return "索引名称只能包含英文、数字和下划线";
					}
					if (item.indexName.length > 64) {
						return "索引“" + item.indexName + "”超过 MySQL 64 字符限制";
					}
					var lowerName = item.indexName.toLowerCase();
					if (names[lowerName]) {
						return "索引名称不能重复：" + item.indexName;
					}
					names[lowerName] = true;
					if (!item.columns || item.columns.length == 0) {
						return "索引“" + item.indexName + "”至少需要一个字段";
					}
					var fields = {};
					for (var j = 0; j < item.columns.length; j++) {
						var columnName = item.columns[j].columnName;
						if (!columnName) {
							return "索引“" + item.indexName + "”第 " + (j + 1) + " 个字段不能为空";
						}
						if (fields[columnName]) {
							return "索引“" + item.indexName + "”不能重复选择字段“" + columnName + "”";
						}
						fields[columnName] = true;
					}
				}
				return "";
			},
			save: function() {
				var message = this.validate();
				if (message) {
					yzp.showErrorModal(message);
					return;
				}
				var that = this;
				this.saving = true;
				yzp.request({
					url: "/admin/custom/config_table/save_indexes",
					sendSessionId: true,
					method: "POST",
					data: this.buildPayload(),
					success: function(res) {
						if (res.code == 200) {
							yzp.showSuccessToast();
							that.detail = res.data || that.detail;
							that.indexes = (that.detail.indexes || []).map(that.normalizeIndex);
						} else {
							yzp.showErrorModal(res.msg);
						}
					},
					complete: function() {
						that.saving = false;
					}
				});
			}
		}
	}
</script>

<style>
	.index-page {
		max-width: 1180px;
		margin: 0 auto;
		padding: 18px 20px 34px;
		box-sizing: border-box;
	}

	.page-summary,
	.index-head,
	.index-title-row,
	.summary-actions,
	.columns-head,
	.page-actions,
	.column-actions {
		display: flex;
		align-items: center;
	}

	.page-summary,
	.index-head,
	.columns-head {
		justify-content: space-between;
	}

	.summary-title {
		font-size: 20px;
		font-weight: 600;
		color: #1f2937;
	}

	.summary-table,
	.summary-count,
	.columns-note,
	.name-preview {
		color: #6b7280;
		font-size: 12px;
	}

	.summary-table {
		margin-left: 10px;
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
	}

	.summary-actions,
	.index-title-row,
	.column-actions,
	.page-actions {
		gap: 8px;
	}

	.ownership-tip {
		margin: 14px 0;
		padding: 10px 12px;
		border-left: 3px solid #2979ff;
		background: #f4f8ff;
		color: #4b5563;
		font-size: 13px;
	}

	.ownership-tip text {
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		color: #1d4ed8;
	}

	.empty-state {
		min-height: 180px;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 14px;
		color: #6b7280;
		background: #fff;
		border: 1px dashed #d1d5db;
		border-radius: 10px;
	}

	.index-list {
		display: flex;
		flex-direction: column;
		gap: 14px;
	}

	.index-card {
		padding: 16px;
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 10px;
		box-shadow: 0 2px 8px rgba(15, 23, 42, 0.04);
	}

	.index-head {
		padding-bottom: 12px;
		border-bottom: 1px solid #eef0f3;
	}

	.index-number,
	.columns-title {
		font-weight: 600;
		color: #1f2937;
	}

	.index-badge,
	.disabled-badge {
		padding: 2px 7px;
		border-radius: 999px;
		font-size: 11px;
	}

	.index-badge.normal {
		background: #eef6ff;
		color: #2563eb;
	}

	.index-badge.unique {
		background: #fff7ed;
		color: #c2410c;
	}

	.disabled-badge {
		background: #f3f4f6;
		color: #6b7280;
	}

	.index-form-grid {
		display: grid;
		grid-template-columns: 180px minmax(320px, 1fr) 90px;
		gap: 14px;
		padding: 14px 0;
	}

	.form-item {
		min-width: 0;
	}

	.form-label {
		display: block;
		margin-bottom: 6px;
		font-size: 13px;
		color: #374151;
	}

	.name-input-row {
		display: flex;
		align-items: stretch;
	}

	.name-prefix {
		display: flex;
		align-items: center;
		padding: 0 9px;
		border: 1px solid #dcdfe6;
		border-right: 0;
		border-radius: 4px 0 0 4px;
		background: #f5f7fa;
		color: #606266;
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 12px;
	}

	.name-input-row uni-easyinput {
		flex: 1;
		min-width: 0;
	}

	.name-preview {
		display: block;
		margin-top: 5px;
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
	}

	.enabled-item switch {
		transform: scale(0.82);
		transform-origin: left center;
	}

	.columns-head {
		margin-top: 2px;
		padding: 12px 0 8px;
	}

	.columns-note {
		margin-left: 8px;
	}

	.column-empty {
		padding: 16px;
		text-align: center;
		color: #b45309;
		background: #fffbeb;
		border: 1px dashed #fcd34d;
		border-radius: 6px;
	}

	.column-list {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.column-row {
		display: grid;
		grid-template-columns: 32px minmax(240px, 1fr) 150px auto;
		gap: 8px;
		align-items: center;
	}

	.column-order {
		width: 26px;
		height: 26px;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 50%;
		background: #f3f4f6;
		color: #4b5563;
		font-size: 12px;
	}

	.definition-preview {
		margin-top: 12px;
		padding: 10px 12px;
		border-radius: 6px;
		background: #111827;
		color: #d1fae5;
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 12px;
		word-break: break-all;
	}

	.page-actions {
		justify-content: flex-end;
		margin-top: 18px;
		padding-bottom: 4px;
	}

	/* uni-button 在 PC 上默认可拉伸，此页操作按钮保持内容宽度。 */
	.page-actions button,
	.page-actions uni-button,
	.summary-actions button,
	.summary-actions uni-button,
	.index-head button,
	.index-head uni-button,
	.columns-head button,
	.columns-head uni-button,
	.column-actions button,
	.column-actions uni-button,
	.empty-state button,
	.empty-state uni-button {
		width: auto;
		min-width: 72px;
		margin: 0;
	}

	@media screen and (max-width: 768px) {
		.index-page {
			padding: 12px;
		}

		.page-summary,
		.index-head,
		.columns-head {
			align-items: flex-start;
			gap: 10px;
		}

		.page-summary,
		.index-head {
			flex-direction: column;
		}

		.index-form-grid,
		.column-row {
			grid-template-columns: 1fr;
		}

		.column-order {
			display: none;
		}

		.column-actions {
			justify-content: flex-end;
			flex-wrap: wrap;
		}

		.name-prefix {
			padding: 0 6px;
		}

		.page-actions {
			position: sticky;
			bottom: 0;
			padding: 10px 0;
			background: rgba(255, 255, 255, 0.96);
		}
	}
</style>
