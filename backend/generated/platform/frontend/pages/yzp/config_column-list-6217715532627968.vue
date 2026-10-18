<!-- 由 Metarill 元数据生成，使用现有 uni-app 请求和组件体系。 -->
<template>
  <view class="yzp-page">
    <view class="uni-padding-wrap"><text>列</text><button v-if="canCreate" size="mini" @click="openDetail('')">新增</button><button v-if="canImport" size="mini" @click="initImport">导入</button><button v-if="canExport" size="mini" @click="initExport">导出</button></view>
    <uni-table :loading="loading" border stripe emptyText="暂无数据">
      <uni-tr><uni-th>名称</uni-th><uni-th>数据类型</uni-th><uni-th>长度</uni-th><uni-th>列名称</uni-th><uni-th>config_table_name</uni-th><uni-th>is_in_grid</uni-th><uni-th>is_in_filter</uni-th><uni-th>is_in_edit</uni-th><uni-th>is_read_only</uni-th><uni-th>is_must</uni-th><uni-th>link_config_table_name</uni-th><uni-th>link_config_page_name</uni-th><uni-th>排序号</uni-th><uni-th>yz_project_id</uni-th><uni-th>yz_project_name</uni-th><uni-th>is_print</uni-th><uni-th>import_no</uni-th><uni-th>insert_time</uni-th><uni-th>update_time</uni-th><uni-th>insert_manager_id</uni-th><uni-th>update_manager_id</uni-th><uni-th>insert_manager_real_name</uni-th><uni-th>update_manager_real_name</uni-th><uni-th>insert_user_name</uni-th><uni-th>update_user_name</uni-th><uni-th>开启排序</uni-th><uni-th>sum_kind</uni-th><uni-th>虚拟字段</uni-th><uni-th>逻辑类型</uni-th><uni-th>数值精度</uni-th><uni-th>小数位数</uni-th><uni-th>操作</uni-th></uni-tr>
      <uni-tr v-for="row in records" :key="row.id"><uni-td>{{ row.name }}</uni-td><uni-td>{{ row.columnType }}</uni-td><uni-td>{{ row.columnLength }}</uni-td><uni-td>{{ row.columnName }}</uni-td><uni-td>{{ row.configTableName }}</uni-td><uni-td>{{ row.isInGrid }}</uni-td><uni-td>{{ row.isInFilter }}</uni-td><uni-td>{{ row.isInEdit }}</uni-td><uni-td>{{ row.isReadOnly }}</uni-td><uni-td>{{ row.isMust }}</uni-td><uni-td>{{ row.linkConfigTableName }}</uni-td><uni-td>{{ row.linkConfigPageName }}</uni-td><uni-td>{{ row.sortNo }}</uni-td><uni-td>{{ row.yzProjectId }}</uni-td><uni-td>{{ row.yzProjectName }}</uni-td><uni-td>{{ row.isPrint }}</uni-td><uni-td>{{ row.importNo }}</uni-td><uni-td>{{ row.insertTime }}</uni-td><uni-td>{{ row.updateTime }}</uni-td><uni-td>{{ row.insertManagerId }}</uni-td><uni-td>{{ row.updateManagerId }}</uni-td><uni-td>{{ row.insertManagerRealName }}</uni-td><uni-td>{{ row.updateManagerRealName }}</uni-td><uni-td>{{ row.insertUserName }}</uni-td><uni-td>{{ row.updateUserName }}</uni-td><uni-td>{{ row.isSort }}</uni-td><uni-td>{{ row.sumKind }}</uni-td><uni-td>{{ row.isVirtualColumn }}</uni-td><uni-td>{{ row.logicalType }}</uni-td><uni-td>{{ row.precision }}</uni-td><uni-td>{{ row.scale }}</uni-td><uni-td><button size="mini" @click="openDetail(row.id)">详情</button></uni-td></uni-tr>
    </uni-table>
    <view><button size="mini" :disabled="pageNo <= 1 || loading" @click="movePage(-1)">上一页</button><text>{{ pageNo }} / {{ pages }}</text><button size="mini" :disabled="pageNo >= pages || loading" @click="movePage(1)">下一页</button></view>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
import utils from '@/common/utils';
export default {
  data() { return { records: [], pageNo: 1, pages: 1, loading: false, parentId: '', selecting: false, canCreate: false, canImport: false, canExport: true }; },
  onLoad(options) { this.parentId = options.parentId || ''; this.selecting = options.select === '1'; },
  onShow() { this.loadPage(); },
  methods: {
    /** 导入保留客户端读取Excel并提交中文标题JSON的协议。 */
    initImport() { yzp.chooseImportFile(rows => { yzp.request({ url: '/admin/auto/config_column/6217715532627968/importDatas6217715532627968', method: 'POST', sendSessionId: true, data: { requestJson: rows, parentId: this.parentId || null }, success: res => yzp.importSuccessAction(res, this) }); }); },
    /** 导出票据与原下载入口兼容，字段和范围全部由后端决定。 */
    initExport() { yzp.request({ url: '/admin/auto/config_column/6217715532627968/initExport', method: 'POST', sendSessionId: true, data: { filters: {}, parentId: this.parentId || null }, success: res => { if (res.code === 200) utils.openUrl(getApp().globalData.environment.base_url + '/admin/sys/export/export?exportId=' + encodeURIComponent(res.data)); } }); },
    /** 批次导入成功后回到第一页重新读取真实数据。 */
    refreshData() { this.pageNo = 1; this.loadPage(); },
    /** 分页请求沿用统一登录态和 R 响应。 */
    loadPage() {
      this.loading = true;
      yzp.request({ url: '/admin/auto/config_column/6217715532627968/page', method: 'POST', sendSessionId: true,
        data: { pageNo: this.pageNo, pageSize: 20, filters: {}, parentId: this.parentId || null },
        success: res => { if (res.code === 200) { this.records = res.data.records; this.pages = Math.max(1, Number(res.data.pages)); } },
        complete: () => { this.loading = false; } });
    },
    /** 翻页仅调整当前页面，不拼接查询 SQL。 */
    movePage(delta) { this.pageNo += delta; this.loadPage(); },
    /** 跳转保持既有生成页面命名和父记录上下文。 */
    openDetail(id) { if (this.selecting && id) { const row = this.records.find(record => record.id === id); this.getOpenerEventChannel().emit('selected', row); uni.navigateBack(); return; } uni.navigateTo({ url: '/pages/yzp/config_column-detail-6217715532627968?id=' + encodeURIComponent(id) + '&parentId=' + encodeURIComponent(this.parentId) }); }
  }
};
</script>
