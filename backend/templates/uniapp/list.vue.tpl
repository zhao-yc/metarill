<!-- 由 Metarill 元数据生成，使用现有 uni-app 请求和组件体系。 -->
<template>
  <view class="yzp-page">
    <view class="uni-padding-wrap"><text>{{TITLE}}</text><button v-if="canCreate" size="mini" @click="openDetail('')">新增</button></view>
    <uni-table :loading="loading" border stripe emptyText="暂无数据">
      <uni-tr>{{HEADERS}}<uni-th>操作</uni-th></uni-tr>
      <uni-tr v-for="row in records" :key="row.id">{{CELLS}}<uni-td><button size="mini" @click="openDetail(row.id)">详情</button></uni-td></uni-tr>
    </uni-table>
    <view><button size="mini" :disabled="pageNo <= 1 || loading" @click="movePage(-1)">上一页</button><text>{{ pageNo }} / {{ pages }}</text><button size="mini" :disabled="pageNo >= pages || loading" @click="movePage(1)">下一页</button></view>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
export default {
  data() { return { records: [], pageNo: 1, pages: 1, loading: false, parentId: '', selecting: false, canCreate: {{CAN_CREATE}} }; },
  onLoad(options) { this.parentId = options.parentId || ''; this.selecting = options.select === '1'; },
  onShow() { this.loadPage(); },
  methods: {
    /** 分页请求沿用统一登录态和 R 响应。 */
    loadPage() {
      this.loading = true;
      yzp.request({ url: '{{API}}/page', method: 'POST', sendSessionId: true,
        data: { pageNo: this.pageNo, pageSize: {{PAGE_SIZE}}, filters: {}, parentId: this.parentId || null },
        success: res => { if (res.code === 200) { this.records = res.data.records; this.pages = Math.max(1, Number(res.data.pages)); } },
        complete: () => { this.loading = false; } });
    },
    /** 翻页仅调整当前页面，不拼接查询 SQL。 */
    movePage(delta) { this.pageNo += delta; this.loadPage(); },
    /** 跳转保持既有生成页面命名和父记录上下文。 */
    openDetail(id) { if (this.selecting && id) { const row = this.records.find(record => record.id === id); this.getOpenerEventChannel().emit('selected', row); uni.navigateBack(); return; } uni.navigateTo({ url: '{{DETAIL_ROUTE}}?id=' + encodeURIComponent(id) + '&parentId=' + encodeURIComponent(this.parentId) }); }
  }
};
</script>
