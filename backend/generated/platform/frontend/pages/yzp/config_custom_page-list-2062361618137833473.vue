<!-- 由 Metarill 元数据生成，使用现有 uni-app 请求和组件体系。 -->
<template>
  <view class="yzp-page">
    <view class="uni-padding-wrap"><text>自定义页面</text><button v-if="canCreate" size="mini" @click="openDetail('')">新增</button><button v-if="canImport" size="mini" @click="initImport">导入</button><button v-if="canExport" size="mini" @click="initExport">导出</button></view>
    <uni-table :loading="loading" border stripe emptyText="暂无数据">
      <uni-tr><uni-th>页面名称</uni-th><uni-th>页面路径</uni-th><uni-th>导航样式</uni-th><uni-th>下拉刷新</uni-th><uni-th>显示左侧</uni-th><uni-th>显示顶部</uni-th><uni-th>创建时间</uni-th><uni-th>排序号</uni-th><uni-th>修改时间</uni-th><uni-th>创建账号</uni-th><uni-th>修改账号</uni-th><uni-th>创建账号名</uni-th><uni-th>修改账号名</uni-th><uni-th>创建姓名</uni-th><uni-th>修改姓名</uni-th><uni-th>操作</uni-th></uni-tr>
      <uni-tr v-for="row in records" :key="row.id"><uni-td>{{ row.name }}</uni-td><uni-td>{{ row.pagePath }}</uni-td><uni-td>{{ row.navigationStyle }}</uni-td><uni-td>{{ row.enablePullDownRefresh }}</uni-td><uni-td>{{ row.isShowLeft }}</uni-td><uni-td>{{ row.isShowTop }}</uni-td><uni-td>{{ row.insertTime }}</uni-td><uni-td>{{ row.sortNo }}</uni-td><uni-td>{{ row.updateTime }}</uni-td><uni-td>{{ row.insertManagerId }}</uni-td><uni-td>{{ row.updateManagerId }}</uni-td><uni-td>{{ row.insertUserName }}</uni-td><uni-td>{{ row.updateUserName }}</uni-td><uni-td>{{ row.insertManagerRealName }}</uni-td><uni-td>{{ row.updateManagerRealName }}</uni-td><uni-td><button size="mini" @click="openDetail(row.id)">详情</button></uni-td></uni-tr>
    </uni-table>
    <view><button size="mini" :disabled="pageNo <= 1 || loading" @click="movePage(-1)">上一页</button><text>{{ pageNo }} / {{ pages }}</text><button size="mini" :disabled="pageNo >= pages || loading" @click="movePage(1)">下一页</button></view>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
import utils from '@/common/utils';
export default {
  data() { return { records: [], pageNo: 1, pages: 1, loading: false, parentId: '', selecting: false, canCreate: true, canImport: false, canExport: false }; },
  onLoad(options) { this.parentId = options.parentId || ''; this.selecting = options.select === '1'; },
  onShow() { this.loadPage(); },
  methods: {
    /** 导入保留客户端读取Excel并提交中文标题JSON的协议。 */
    initImport() { yzp.chooseImportFile(rows => { yzp.request({ url: '/admin/auto/config_custom_page/2062361618137833473/importDatas2062361618137833473', method: 'POST', sendSessionId: true, data: { requestJson: rows, parentId: this.parentId || null }, success: res => yzp.importSuccessAction(res, this) }); }); },
    /** 导出票据与原下载入口兼容，字段和范围全部由后端决定。 */
    initExport() { yzp.request({ url: '/admin/auto/config_custom_page/2062361618137833473/initExport', method: 'POST', sendSessionId: true, data: { filters: {}, parentId: this.parentId || null }, success: res => { if (res.code === 200) utils.openUrl(getApp().globalData.environment.base_url + '/admin/sys/export/export?exportId=' + encodeURIComponent(res.data)); } }); },
    /** 批次导入成功后回到第一页重新读取真实数据。 */
    refreshData() { this.pageNo = 1; this.loadPage(); },
    /** 分页请求沿用统一登录态和 R 响应。 */
    loadPage() {
      this.loading = true;
      yzp.request({ url: '/admin/auto/config_custom_page/2062361618137833473/page', method: 'POST', sendSessionId: true,
        data: { pageNo: this.pageNo, pageSize: 20, filters: {}, parentId: this.parentId || null },
        success: res => { if (res.code === 200) { this.records = res.data.records; this.pages = Math.max(1, Number(res.data.pages)); } },
        complete: () => { this.loading = false; } });
    },
    /** 翻页仅调整当前页面，不拼接查询 SQL。 */
    movePage(delta) { this.pageNo += delta; this.loadPage(); },
    /** 跳转保持既有生成页面命名和父记录上下文。 */
    openDetail(id) { if (this.selecting && id) { const row = this.records.find(record => record.id === id); this.getOpenerEventChannel().emit('selected', row); uni.navigateBack(); return; } uni.navigateTo({ url: '/pages/yzp/config_custom_page-detail-2062361618137833473?id=' + encodeURIComponent(id) + '&parentId=' + encodeURIComponent(this.parentId) }); }
  }
};
</script>
