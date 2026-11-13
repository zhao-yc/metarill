<template>
	<view class="container">
		<uni-forms label-position="top" label-width="180">
			<uni-forms-item label="原始密码" required>
				<uni-easyinput v-model="password_old" placeholder="请输入原始密码" type="password" />
			</uni-forms-item>
			<uni-forms-item label="新密码" required>
				<uni-easyinput v-model="password_new" placeholder="请输入新密码" type="password" />
			</uni-forms-item>
			<uni-forms-item label="确认密码" required>
				<uni-easyinput v-model="password_confirm" placeholder="请输入确认密码" type="password" />
			</uni-forms-item>
		</uni-forms>
		<view class="button-sp-area">
			<button type="default" plain="true" @click="changePassword">修改</button>
		</view>
	</view>
</template>


<script>
	import utils from "@/common/utils.js";
	import yzp from "@/common/yzp.js";
	export default {
		data() {
			return {
				password_old: '',
				password_new: '',
				password_confirm: ''
			}
		},

		methods: {
			changePassword() {
				var that = this;
				if (utils.isBlank(this.password_new)) {
					uni.showModal({
						showCancel: false,
						content: '新密码不能为空'
					})
					return;
				}
				if (this.password_new != this.password_confirm) {
					uni.showModal({
						showCancel: false,
						content: '两次输入的密码不一致'
					})
					return;
				}
				yzp.request({
					url: '/admin/sys/yz_manager/edit_password',
					method: 'POST',
					data: {
						oldPwd: this.password_old,
						pwd: this.password_new
					},
					sendSessionId: true,
					success: function(res) {
						if (res.code == 200) {
							uni.showModal({
								showCancel: false,
								content: '修改成功',
								success() {
									uni.navigateBack()
								}
							})
						} else {
							uni.showModal({
								showCancel: false,
								content: res.msg
							})
						}
					}
				})
			}



		}
	}
</script>

<style>
	.container {
		padding: 20px;
		background-color: #FFFFFF;
	}

	.button-sp-area button {
		margin: 20px;
		background-color: #1684FC;
		border: none;
		border-radius: 10px;
		color: #FFFFFF;
	}
</style>
