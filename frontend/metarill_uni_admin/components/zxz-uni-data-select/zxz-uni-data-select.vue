<template>
 <view class="pending-control" role="status">选择控件待确认原组件许可后接入；现有字段值保持不变。</view>
</template>
<script>
/** 保留原组件接入名；许可确认前不提供替代交互，也不写回空值。 */
export default { inheritAttrs: false };
</script>
<style>
.pending-control { padding: 8px; color: #8a6824; background: #fff8e8; border: 1px solid #edddbd; border-radius: 4px; font-size: 12px; }
</style>
