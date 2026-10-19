<template>
 <view class="container">
  <rich-text v-if="action === 'review'" :nodes="rtf" />
  <view v-else class="page-body">
   <view v-if="loadState !== 'ready'" class="editor-state" role="status">{{ editorMessage }}</view>
   <tinymce-editor v-if="editorConfiguration" v-model="rtf" :api-key="editorConfiguration.apiKey"
    :cloud-channel="editorConfiguration.cloudChannel" :tinymce-script-src="editorConfiguration.scriptSrc"
    :init="init" @init="onEditorInit" />
  </view>
 </view>
</template>
<script>
import Editor from '@tinymce/tinymce-vue';
import yzp from '@/common/yzp';
import { createTinyCloudEditorInit, validateTinyCloudConfiguration, uploadTinyCloudImage } from '@/common/yzp_runtime/tinyCloudEditor';

/** 保留 sendRtf/onReceiveRtf 和原 HTML 双向编辑，只从已认证部署配置加载 Tiny Cloud。 */
export default {
 components: { 'tinymce-editor': Editor },
 data() { return { action: 'review', rtf: '', loadState: 'idle', editorMessage: '', editorConfiguration: null, init: {} }; },
 /** 原预览不请求 Cloud；编辑模式先读取部署配置，已有内容始终保留。 */
 onLoad(options) {
  this._alive = true; this._revision = 0; this._uploads = new Set(); this._channel = this.getOpenerEventChannel();
  this.action = options.action === 'edit' ? 'edit' : 'review';
  this._receiveRtf = data => { if (this._alive) this.rtf = typeof data?.data === 'string' ? data.data : ''; };
  this._channel.on('sendRtf', this._receiveRtf);
  if (this.action === 'review') uni.setNavigationBarTitle({ title: '预览' });
  else this.loadEditorConfiguration();
 },
 /** 退出前读取编辑器最新 HTML，再回传一次；终止未完成请求并移除事件订阅。 */
 onUnload() {
  if (!this._alive) return;
  if (this.action === 'edit') {
   // 编辑器已经销毁时保留最后一次双向绑定值，仍须完成回传与清理。
   try { if (this._editor && typeof this._editor.getContent === 'function') this.rtf = this._editor.getContent(); } catch { /* 保留已有 HTML。 */ }
   this._channel.emit('onReceiveRtf', this.rtf);
  }
  this._alive = false; this._revision++; clearTimeout(this._loadTimer);
  this._request?.abort?.(); this._uploads.forEach(task => task?.abort?.()); this._uploads.clear();
  this._channel.off?.('sendRtf', this._receiveRtf); this._editor = null; this.editorConfiguration = null;
 },
 methods: {
  /** 新请求先使旧回调失效，部署配置中只允许官方 CDN，缺配置不挂载编辑器。 */
  loadEditorConfiguration() {
   const revision = ++this._revision; clearTimeout(this._loadTimer); this._request?.abort?.(); this.editorConfiguration = null;
   this.loadState = 'loading'; this.editorMessage = '正在读取富文本编辑配置…';
   this._identity = this.currentIdentity();
   const current = () => this._alive && revision === this._revision && this._identity === this.currentIdentity();
   this._request = yzp.request({ url: '/admin/sys/tiny-cloud/config', method: 'POST', sendSessionId: true, data: {}, isCurrent: current,
    success: response => {
     if (!current()) return;
     if (response.code !== 200) { this.loadState = 'error'; this.editorMessage = response.msg || '读取富文本配置失败。'; return; }
     if (response.data?.enabled === false) { this.loadState = 'unconfigured'; this.editorMessage = response.data.message || '尚未配置 Tiny Cloud，请联系管理员。'; return; }
     if (!validateTinyCloudConfiguration(response.data)) { this.loadState = 'error'; this.editorMessage = '富文本配置无效，请联系管理员。'; return; }
     this.init = createTinyCloudEditorInit(response.data, (blobInfo, progress) => this.uploadImage(blobInfo, progress));
     this.editorConfiguration = response.data; this.loadState = 'configured'; this.editorMessage = '正在加载 Tiny Cloud 编辑器…';
     this._loadTimer = setTimeout(() => { if (current() && this.loadState !== 'ready') { this.loadState = 'error'; this.editorMessage = 'Tiny Cloud 加载未完成，请检查网络、账户授权和域名配置。'; } }, 30000);
    },
    fail: () => { if (current()) { this.loadState = 'error'; this.editorMessage = '读取富文本配置失败，请重新进入。'; } } });
  },
  /** 对比完整登录/工作区快照，避免账号或项目切换后采用旧响应。 */
  currentIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]); },
  /** Vue wrapper 负责常规销毁；已离开的页面收到迟到 init 时立即清理编辑器。 */
  onEditorInit(event, editor) {
   if (!this._alive || this._identity !== this.currentIdentity()) { editor?.remove?.(); return; }
   clearTimeout(this._loadTimer); this._editor = editor; this.loadState = 'ready'; this.editorMessage = '';
  },
  /** 原公开图片上传沿用登录头和项目头，不把会话拼到 URL。 */
  uploadImage(blobInfo, progress) {
   const identity = this.currentIdentity(), headers = { loginStateId: uni.getStorageSync('loginStateId') || '' };
   const selection = uni.getStorageSync('sysParams'); if (selection) headers.sysParams = encodeURIComponent(JSON.stringify(selection));
   return uploadTinyCloudImage(blobInfo, progress, { baseUrl: getApp().globalData.environment.base_url, headers,
    uploadFile: options => uni.uploadFile(options), isCurrent: () => this._alive && identity === this.currentIdentity(),
    onStart: task => this._uploads.add(task), onComplete: task => this._uploads.delete(task) });
  }
 }
};
</script>
<style>.editor-state {padding:16px;background:#fff8e8;color:#8a6824;}</style>
