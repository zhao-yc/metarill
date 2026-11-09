<template>
  <!-- #ifdef H5 -->
  <view class="rtf-review" v-html="safeHtml"></view>
  <!-- #endif -->
  <!-- #ifndef H5 -->
  <rich-text class="rtf-review" :nodes="html || ''" />
  <!-- #endif -->
</template>

<script>
// #ifdef H5
import createDOMPurify from 'dompurify';

const purifier = typeof window === 'undefined' ? null : createDOMPurify(window);
// 保留富文本原有的新窗口链接，同时隔离被链接页面与当前后台。
purifier?.addHook('afterSanitizeAttributes', node => {
  if (node.nodeName === 'A' && node.getAttribute('target')?.toLowerCase() === '_blank') {
    const rel = (node.getAttribute('rel') || '').split(/\s+/).filter(value => value && value.toLowerCase() !== 'opener');
    if (!rel.some(value => value.toLowerCase() === 'noopener')) rel.push('noopener');
    node.setAttribute('rel', rel.join(' '));
  }
});
// #endif

export default {
  props: { html: { type: String, default: '' } },
  computed: {
    /** 只过滤进入页面的 HTML，保存和打开编辑器仍使用原字段。 */
    safeHtml() {
      // #ifdef H5
      return purifier?.isSupported
        ? purifier.sanitize(this.html || '', { USE_PROFILES: { html: true }, ADD_ATTR: ['target'], FORBID_TAGS: ['style'] })
        : '';
      // #endif
      // #ifndef H5
      return '';
      // #endif
    }
  }
};
</script>
