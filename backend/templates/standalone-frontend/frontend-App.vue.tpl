<script>
import { createAppEnvironment } from '@/common/environment'
export default {
 globalData: { readOnly: false, windowInfo: {}, environment: createAppEnvironment() },
 /** 原有桌面/移动布局全局属性保持一致，独立宿主没有平台项目切换及账号管理路由。 */
 onLaunch() {
  uni.getSystemInfo({ success: info => { if (info.windowWidth < 768) { Object.keys(this.$imageStyles).forEach(key => delete this.$imageStyles[key]); this.$deviceInfo.is_pc = false } } })
  this.globalData.windowInfo = uni.getWindowInfo()
  uni.onWindowResize(event => { this.globalData.windowInfo = { ...this.globalData.windowInfo, ...event.size } })
 }
}
</script>
<style lang="scss">@import '@/common/yzp.scss';</style>
