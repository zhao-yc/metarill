<template>
	<view class="top">
		<view class="top-bg">
			<view class="top-glow top-glow-left"></view>
			<view class="top-glow top-glow-right"></view>
			<view class="top-grid"></view>
		</view>
		<view class="top-content">
			<view style="margin-left:20px;">
				<image class="slot-image" src="/static/logo.png" mode="widthFix"></image>
			</view>
			<view style="flex: 1;">
				<text class="slot-box slot-text">{{ projectName }}（{{ enevName }}）</text>
			</view>

			<view class="top-right">
				<button v-if="isYzp" size="mini" @click="configureMenus">菜单配置</button>
				<button size="mini" @click="toIndex">首页</button>
				<button v-if="isYzp" size="mini" @click="changeProject">【{{ currentSelectionText }}】切换设备/项目</button>
				<view class="top-right-img" @click="showActions">
					<image class="slot-image" src="/static/images/avatar.png" mode="widthFix"></image>
					<view style="margin-right: 5px;">{{userInfo.merchantName}}[{{userInfo.name}}]</view>
					<uni-icons type="down"></uni-icons>
				</view>
			</view>
		</view>
	</view>
</template>

<script>
	import utils from "@/common/utils.js";
	import yzp from "@/common/yzp.js";
	import application from "@/common/application.generated.json";
	// var app = getApp();
	export default {
		data() {
			return {
				enevName: '',
				projectName: application.name,
				userInfo: {},
				sysParams: {}
			}
		},
		computed: {
			/** 普通业务工程没有平台配置/设备项目切换能力，保留原入口节点但明确按包能力门禁。 */
			isYzp() { return application.capabilities?.platformManagement === true; },
			currentSelectionText() {
				var deviceName = this.sysParams.yzManagerDevicesName || "未选设备";
				var projectName = this.sysParams.yzProjectName || "未选项目";
				return deviceName + " / " + projectName;
			}
		},
		mounted() {
			var that = this;
			this.enevName = getApp().globalData.environment.name
			this.sysParams = uni.getStorageSync("sysParams");
			uni.$on("onChangeProject", function() {
				that.sysParams = uni.getStorageSync("sysParams");
			})

			var is_login = yzp.checkLogin();
			if (is_login) {
				that.userInfo = uni.getStorageSync("userInfo");
			}

			uni.$on("onLoginLoadTop", function() {
				that.userInfo = uni.getStorageSync("userInfo");
				that.sysParams = uni.getStorageSync("sysParams");
			})
		},
		// onShow() {
		// 	this.sysParams = uni.getStorageSync("sysParams");
		// },
		methods: {
			showActions() {
				const that = this
				uni.showActionSheet({
					itemList: ['修改密码', '退出'],
					popover: {
						width: 100,
						top: uni.getTopWindowStyle().height.replace("px", ""),
						left: getApp().globalData.windowInfo.windowWidth - 130
					},
					success: (e) => {
						console.log(e.tapIndex);
						//修改密码
						if (e.tapIndex == 0) {
							that.editPwd();
						} else if (e.tapIndex == 1) {
							that.exitLogin();
						}
					}
				})
			},
			// 平台工作台按目标项目版本校验，维护各个 V8 项目的菜单元数据。
			configureMenus() {
				uni.navigateTo({ url: "/pages/custom/menu_workbench" });
			},
			toIndex() {
				uni.reLaunch({
					url: "/pages/main/index"
				})
			},
			changeProject: function() {
				uni.navigateTo({
					url: "/pages/custom/select_project"
				})
			},
			exitLogin: function() {
				yzp.exitLogin();
			},
			editPwd: function() {
				uni.$emit('navigateToChangePassword');
			}
		}
	}
</script>

<style lang="scss">
	.top {
		height: calc(var(--top-window-height) - 1px);
		position: relative;
		overflow: hidden;
		border-bottom: solid 1px $uni-border-2;
		background: linear-gradient(135deg, #f8fbf8 0%, #f0f6f1 50%, #e8f0ea 100%);
		box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.72);
	}

	.top-bg {
		position: absolute;
		inset: 0;
		pointer-events: none;
		overflow: hidden;
	}

	.top-glow {
		position: absolute;
		border-radius: 999px;
		filter: blur(4px);
		opacity: 0.75;
	}

	.top-glow-left {
		left: -70px;
		top: -60px;
		width: 220px;
		height: 160px;
		background: radial-gradient(circle at center, rgba(87, 162, 111, 0.18) 0%, rgba(87, 162, 111, 0.06) 42%, rgba(87, 162, 111, 0) 74%);
	}

	.top-glow-right {
		right: -40px;
		top: -80px;
		width: 260px;
		height: 190px;
		background: radial-gradient(circle at center, rgba(148, 184, 118, 0.22) 0%, rgba(148, 184, 118, 0.08) 45%, rgba(148, 184, 118, 0) 76%);
	}

	.top-grid {
		position: absolute;
		inset: 0;
		background-image:
			linear-gradient(90deg, rgba(74, 111, 84, 0.05) 1px, transparent 1px),
			linear-gradient(180deg, rgba(74, 111, 84, 0.03) 1px, transparent 1px);
		background-size: 120px 100%, 100% 120px;
		opacity: 0.45;
	}

	.top-content {
		position: relative;
		z-index: 1;
		height: 100%;
		display: flex;
		align-items: center;
		justify-content: space-between;
	}

	.top-content>view {
		display: flex;
		justify-content: center;
		align-items: center;
		background-color: transparent;
	}

	.top-right {
		display: flex;
		align-items: center;
		font-size: 14px;
		color: #666;
	}

	.top-right button {
		margin-left: 10px;
	}

	.top-right-img {
		cursor: pointer;
		margin: auto 20px;
		display: flex;
		align-items: center;
	}


	.slot-image {
		/* #ifndef APP-NVUE */
		display: block;
		/* #endif */
		margin-right: 10px;
		width: 30px;
		height: 30px;
	}

	.slot-text {
		flex: 1;
		font-size: 14px;
		color: #666666;
		margin-right: 10px;
	}
</style>