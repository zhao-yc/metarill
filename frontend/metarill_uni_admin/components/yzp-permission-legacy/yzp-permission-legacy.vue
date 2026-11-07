<template>
	<view>
		<view class="default-page">
			<uni-section title="第一步:选择角色" type="line">
				<!-- <uni-data-select v-model="value" :localdata="yzRoles" @change="change"></uni-data-select> -->
				<zxz-uni-data-select v-model="value" filterable dataKey="text" dataValue="value" :localdata="yzRoles"
					@change="change"></zxz-uni-data-select>
			</uni-section>
			<uni-section title="第二步:设置角色权限" type="line">
				<view class="first-menu" v-for='(item, index) in setRolePermissionConfigFirstMenus'>
					<view class="top">
						<view class="title">{{item.name}}</view>
						<view>
							<button size="mini" @click="selectAll(item)">全选</button>
							<button size="mini" @click="notSelectAll(item)">全不选</button>
						</view>
					</view>
					<view class="second-menu">
						<view class="second-menu-item" v-for='(item2, index2) in item.setRolePermissionConfigSecondMenus'>
							<view class="title second-menu-title">{{item2.name}}</view>
							<view class="select-all">
								<uni-tag text="全功能" type="success" @click="selectAllRow(item2)"></uni-tag>
							</view>
							<view class="role-permission-options">
								<!-- {{item2.permissionValues}} -->
								<uni-data-checkbox multiple @change="checkBoxChange" v-model="item2.permissionValues"
									:localdata="item2.setRolePermissionPermissions"></uni-data-checkbox>
							</view>
						</view>
					</view>
				</view>
			</uni-section>
			<button @click="save">保存</button>
		</view>
	</view>
</template>

<script>
	import yzp from "@/common/yzp_runtime/yzp-runtime";
	import { permissionContext } from "@/common/yzp_runtime/permissionWorkbench";
	export default {
		props: { initialRoleId: { type: String, default: "" } },
		data() {
			return {
				yzRoleId: '',
				value: '',
				yzRoles: [],
				setRolePermissionConfigFirstMenus: []
			}
		},
		/** 仅在服务端明确不支持独立授权时挂载，保持旧接口与保存协议。 */
		mounted: function() {
			this._alive = true;
			this._identity = permissionContext(key => uni.getStorageSync(key));
			this.initYzRoles();
		},
		beforeUnmount() { this._alive = false; },
		beforeDestroy() { this._alive = false; },
		methods: {
			/** 旧协议维持原样，仅隔离卸载或切换身份后的回调。 */
			requestLegacy(options) {
				const current = () => this._alive && this._identity === permissionContext(key => uni.getStorageSync(key));
				if (current()) return yzp.request({ ...options, isCurrent: current });
			},
			/** 保持旧版按行全选语义。 */
			/** 保持旧版分组全选语义。 */
			selectAllRow(item2) {
				item2.permissionValues = item2.setRolePermissionPermissions.map(item => item.value);
			},
			/** 旧复选框事件不改变保存协议。 */
			checkBoxChange(e) {
				console.log(e)
			},
			selectAll(item) {
				var that = this;
				item.setRolePermissionConfigSecondMenus.forEach(
					item2 => that.selectAllRow(item2)
				)
			},
			/** 仅清空当前分组的旧授权勾选。 */
			notSelectAll(item) {
				item.setRolePermissionConfigSecondMenus.forEach(
					item2 => item2.permissionValues = []
				)
			},
			/** 选择角色后读取旧版授权目录。 */
			change: function(e) {
				this.yzRoleId = e.value;
				this.initMenus();
			},
			/** 服务端明确为旧模式时继续使用原保存接口。 */
			save: function() {
				var that = this;
				this.requestLegacy({
					url: '/admin/sys/yz_manager/saveRolePermission?yzRoleId=' + that.yzRoleId,
					sendSessionId: true,
					method: "POST",
					data: that.setRolePermissionConfigFirstMenus,
					success: function(res) {
						if (res.code == 200) {
							yzp.showSuccessToast();
						} else {
							uni.showModal({
								showCancel: false,
								content: res.msg
							})
						}
					},
				})
			},
			/** 使用原角色范围接口加载可配置角色。 */
			initYzRoles: function() {
				var that = this;
				this.requestLegacy({
					url: '/admin/sys/yz_manager/findNameIdYzRole',
					sendSessionId: true,
					success: function(res) {
						if (res.code == 200) {
							that.yzRoles = res.data;
							if (that.initialRoleId && (res.data || []).some(role => String(role.value) === that.initialRoleId)) {
								that.value = that.initialRoleId;
								that.yzRoleId = that.initialRoleId;
								that.initMenus();
							}
						} else {
							uni.showModal({
								showCancel: false,
								content: res.msg
							})
						}
					},
				})
			},
			/** 使用旧目录协议展示已有授权。 */
			initMenus: function() {
				var that = this;
				this.requestLegacy({
					url: '/admin/sys/yz_manager/initMenus?yzRoleId=' + this.yzRoleId,
					sendSessionId: true,
					success: function(res) {
						if (res.code == 200) {
							that.setRolePermissionConfigFirstMenus = res.data;
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

<style lang="scss">
	.default-page {
		padding: $uni-spacing-base;
	}

	.first-menu {
		padding: 10px;
		padding-top: 0px;
	}

	.first-menu .title {
		margin-right: 20px;
		width: 100px;
		flex-shrink: 0;
	}


	.first-menu .second-menu .title {
		color: #989898;
	}

	.first-menu .second-menu .select-all {
		margin-right: 20px;
		flex-shrink: 0;
		white-space: nowrap;
	}


	.first-menu .top {
		display: flex;
		align-items: center;
	}

	.second-menu {
		/* display: flex; */
	}

	.second-menu-item,
	.second-menu>uni-view {
		display: flex;
		align-items: flex-start;
		margin: 5px auto;
	}

	.role-permission-options {
		flex: 1;
		min-width: 0;
	}

	/* 权限项可以按项换行，但单个选项文字不能被拆成两行。 */
	.role-permission-options .uni-data-checklist .checklist-group .checklist-box {
		flex: 0 0 auto;
	}

	.role-permission-options .uni-data-checklist .checklist-group .checklist-box .checklist-content {
		flex: 0 0 auto;
	}

	.role-permission-options .uni-data-checklist .checklist-group .checklist-box .checklist-content .checklist-text {
		white-space: nowrap;
		word-break: keep-all;
	}
</style>
