<template>
 <view>
  <view v-if="action === 'edit'" class="pending-editor">富文本编辑待确认原组件许可后接入；已有内容保持不变。</view>
  <rich-text :nodes="rtf" />
 </view>
</template>
<script>
/** 保留富文本事件协议；许可确认前仅预览已有值，不用其他编辑器替换交互。 */
export default {
 data() { return { action: 'review', rtf: '' }; },
 onLoad(options) { this.action=options.action || 'review'; this.getOpenerEventChannel().on('sendRtf', data => { this.rtf=data.data || ''; }); },
 onUnload() { if(this.action==='edit') this.getOpenerEventChannel().emit('onReceiveRtf', this.rtf); }
};
</script>
<style>.pending-editor {padding:16px;background:#fff8e8;color:#8a6824;}</style>
