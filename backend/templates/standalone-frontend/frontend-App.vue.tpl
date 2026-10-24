<script>
	import {
		createAppEnvironment
	} from '@/common/environment'

	export default {
		globalData: {
			readOnly: false,
			windowInfo: {},
			environment: createAppEnvironment()
		},
		onLaunch: function() {
			var that = this;

			//获取设备屏幕宽度，用于处理在不同宽度时的样式
			uni.getSystemInfo({
				success: function(res) {
					//移动端的时候
					if (res.windowWidth < 768) {
						Object.keys(that.$imageStyles).forEach(function(key) {
							delete that.$imageStyles[key];
						});
						that.$deviceInfo.is_pc = false;
					}
				}
			});


			this.globalData.windowInfo = uni.getWindowInfo();
			console.log("windowInfo", this.globalData.windowInfo)
			uni.onWindowResize(function(res) {
				console.log("onWindowResize", res)
				that.globalData.windowInfo.windowWidth = res.size.windowWidth
				that.globalData.windowInfo.windowHeight = res.size.windowHeight
			})


			if (this.$deviceInfo.is_pc) {
				uni.$on('navigateToChangePassword', function() {
					uni.navigateTo({
						url: "/pages/main/change_password"
					})
				})
			}
		},
		onShow: function() {
			console.log('App Show')
		},
		onHide: function() {
			console.log('App Hide')
		}
	}
</script>

<style lang="scss">

	@import "@/common/yzp.scss";
</style>