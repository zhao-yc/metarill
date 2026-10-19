<template><view class="uni-padding-wrap"><text>{{ account.name || account.username || '开发账号' }}</text><view>仅开发认证；正式部署需替换服务端认证实现。</view><button @click="logout">退出登录</button></view></template>
<script>
import yzp from '@/common/yzp'
export default { data(){return{account:uni.getStorageSync('userInfo')||{}}}, methods:{ /** 先撤销真实开发会话，再清理客户端缓存和共享菜单。 */ logout(){yzp.request({url:'/admin/sys/yz_manager/exit_login',method:'POST',sendSessionId:true,data:{},success:response=>{if(response.code===200)yzp.goLoginWithRedirect()}})} } }
</script>
<style>.uni-padding-wrap{padding:24px}.uni-padding-wrap view{margin:20px 0}</style>
