<template><view class="uni-padding-wrap"><view class="title">{{ application.name }}</view><text>从菜单进入业务页面。</text><button @click="menu">打开菜单</button></view></template>
<script>
import application from '@/common/application.generated.json'
export default { data() { return { application } }, onShow() { if (!this.$yzp.checkLogin()) this.$yzp.goLoginWithRedirect() }, methods: { /** 移动端与桌面共用相同菜单和生成页面。 */ menu() { uni.switchTab({ url: '/pages/main/menu' }) } } }
</script>
<style>.title{font-size:24px;margin:28px 0 16px}.uni-padding-wrap{padding:24px}</style>
