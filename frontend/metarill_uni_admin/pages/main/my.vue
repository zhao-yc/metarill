<template>
	<view class="container">
		<uni-nav-bar v-if='!$deviceInfo.is_pc' fixed statusBar :border="false" title="我的"></uni-nav-bar>
		<view class="user">
			<view class="head">

				<uni-icons type="person-filled" size="28" color="#0ab3ad" />

			</view>
			<view class="sign">
				<text class="text getUserBtn">{{userInfo.merchantName}}[{{userInfo.name}}]</text>
			</view>
			<!-- <view style="flex: 1;text-align: right;">
				<uni-icons type="forward" size="25" color="#FFFFFF" @click='personalData'></uni-icons>
			</view> -->
		</view>



		<uni-list>
			<!-- <uni-list-item :show-extra-icon="true" to="/pages/user/member_card/detail" showArrow clickable
				:extra-icon="{type: 'vip',color:'#FFBB47'}" title="开通会员" />
			<uni-list-item :show-extra-icon="true" to="collect/collect" showArrow clickable
				:extra-icon="{type: 'iconfont iconfont icon-wodeshoucang',color:'#FF5656'}" title="我的收藏" />
			<uni-list-item :show-extra-icon="true" to="coupon/my_coupon" showArrow clickable
				:extra-icon="{type: 'iconfont iconfont icon-youhuiquan',color:'#48D8FF'}" title="优惠券" /> -->
			<!-- <uni-list-item :show-extra-icon="true" to="giftcard/giftcard" showArrow clickable
				:extra-icon="{type: 'iconfont iconfont icon-qianbao1'}" title="礼品卡" />
			<uni-list-item :show-extra-icon="true" to="address/list" showArrow clickable
				:extra-icon="{type: 'iconfont iconfont icon-dizhi-copy',color:'#e9a7fd'}" title="收货地址" />
			<uni-list-item :show-extra-icon="true" @click="initScan" showArrow clickable
				v-if="userInfo.is_shop_manager == 1" :extra-icon="{type: 'scan',color:'#478CFF'}" title="门店核销" />
			<uni-list-item :show-extra-icon="true" to="../index/shopList?isView=true" showArrow clickable
				class="list-shop" :extra-icon="{type: 'iconfont iconfont icon-dianpu1',color:'#FF9595'}" title="自提点" /> -->
			<!-- <uni-list-item :show-extra-icon="true" to="helpCenter" showArrow clickable
				:extra-icon="{type: 'help',color:'#478CFF'}" title="帮助中心" />
			<uni-list-item :show-extra-icon="true" to="aboutUs" showArrow clickable
				:extra-icon="{type: 'phone',color:'#47C9FF'}" title="联系我们" /> -->

			<uni-list-item :show-extra-icon="true" to="change_password" showArrow clickable class="list-shop"
				:extra-icon="{type: 'iconfont iconfont icon-dianpu1',color:'#FF9595'}" title="修改密码" />
		</uni-list>
		<view class="space-10"></view>

		<!-- #ifdef MP-WEIXIN -->
		<uni-list class="contact">
			<button open-type="contact">
				<uni-list-item :show-extra-icon="true" showArrow clickable
					:extra-icon="{type: 'iconfont iconfont icon-kefu'}" title="联系客服" />
			</button>
		</uni-list>
		<!-- #endif -->

		<!-- #ifndef MP-WEIXIN -->
		<uni-list class="exit-login">
			<uni-list-item :show-extra-icon="true" @click="exitLogin" showArrow clickable :extra-icon="{type: 'clear'}"
				title="退出登录" />
		</uni-list>
		<!-- #endif -->

		<view class="copy-right">UID：{{userInfo.id}}</view>

		<!-- <view class="copy-right">@双拓系统 版权所有</view>
		<view class="space-30"></view> -->
	</view>
</template>

<script>
	import utils from "@/common/utils.js";
	import yzp from "@/common/yzp.js";
	var app = getApp();
	export default {
		data() {
			return {
				userInfo: {

				},
			};
		},
		onLoad: function(options) {},
		onShow: function() {
			var that = this;
			if (uni.getStorageSync('userInfo')) {
				this.userInfo = uni.getStorageSync('userInfo');
			}
		},
		methods: {
			exitLogin: function() {
				yzp.exitLogin();
			},

			//设置
			setting: function() {
				var that = this;
				uni.openSetting({
					success: (res) => {

					}
				});
			},

			personalData() {
				uni.navigateTo({
					url: 'personalData'
				})
			},

			gotoDistributionMember: function() {
				if (this.levelId) {
					uni.navigateTo({
						url: 'vip?content=' + this.leveContent
					});
				}
			},


			getUser: function(e) {
				// #ifdef MP-WEIXIN
				var that = this;
				uni.getUserProfile({
					desc: '获取你的昵称、头像、地区及性别',
					success: (getUserRes) => {
						uni.request({
							url: getApp().globalData.environment.base_url + '/user/update_info',
							method: "POST",
							data: {
								sessionId: uni.getStorageSync('sessionId'),
								nick_name: getUserRes.userInfo.nickName,
								avatar_url: getUserRes.userInfo.avatarUrl
							},
							success: function(res) {
								if (res.data.code == 200) {
									uni.setStorageSync('userInfo', res.data
										.userInfo);
									that.userInfo = res.data.userInfo;
								}
							}
						});
					}
				});
				// #endif

				// #ifndef MP-WEIXIN
				uni.navigateTo({
					url: 'login'
				})
				// #endif
			},

		}
	};
</script>

<style>
	page {
		background-color: #F4F4F4;
	}

	.contact button {
		width: 100%;
		padding: 0px;
		text-align: left;
		line-height: inherit;
	}

	.user {
		padding-top: 50rpx;
		background-color: #2979ff;
		position: relative;
	}

	.head .tupian {
		width: 100%;
	}

	.getUserBtn {
		background: transparent;
		box-shadow: 0;
		line-height: 1;
		width: 90px;
		text-align: center;
		padding: 5px 0;
		color: #fff;
		font-size: 18px;
	}

	.getUserBtn::after {
		box-shadow: 0;
		border: 0;
	}

	.head image {
		width: 130rpx;
		height: 130rpx;
		/* background: #e5e5e5; */
		-moz-border-radius: 60px;
		-webkit-border-radius: 60px;
		border-radius: 60px;
		margin: 0 auto;
		border: 1px solid #fff;
	}

	.user {
		display: flex;
		padding: 0px 30px;
		align-items: center;
		height: 140px;
	}

	.user .sign {
		text-align: left;
		padding: 30rpx 0;
		color: #fff;
		font-size: 36rpx;
		margin-left: 10px;
	}

	.copy-right {
		font-size: 14px;
		text-align: center;
		color: #737373;
		margin: 30px auto;
	}

	.exit-login {
		margin-top: 20px;
	}
</style>