<template><view class="top"><text>{{ application.name }}</text><button size="mini" @click="profile">我的</button></view></template>
<script>import application from '@/common/application.generated.json'; export default { data(){return{application}}, methods:{ /** 开发账号没有平台账号管理入口。 */ profile(){uni.switchTab({url:'/pages/main/my'})} } }</script>
<style scoped>.top{display:flex;align-items:center;justify-content:space-between;padding:12px 24px;border-bottom:1px solid #e1e8e3}</style>
