<!-- 由 Metarill 元数据生成，使用现有 uni-app 请求和组件体系。 -->
<template>
  <view class="yzp-page">
    <view class="uni-padding-wrap"><text>选择配置页面</text><button v-if="canCreate" size="mini" @click="openDetail('')">新增</button><button v-if="canImport" size="mini" @click="initImport">导入</button><button v-if="canExport" size="mini" @click="initExport">导出</button></view>
    <view v-if="filterFields.length" class="uni-padding-wrap">
      <uni-forms :modelValue="draftFilters">
<uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="draftFilters.name" @update:modelValue="setFilter('name', $event)" placeholder="请输入名称" /></uni-forms-item>
      </uni-forms>
      <button size="mini" @click="applyFilters">查询</button><button size="mini" @click="resetFilters">重置</button>
    </view>
    <view v-for="message in filterGaps" :key="message" class="uni-padding-wrap"><text>{{ message }}</text></view>
    <view v-if="sortFields.length" class="uni-padding-wrap">
      <uni-data-select :modelValue="sortField" :localdata="sortFields" placeholder="排序字段" @update:modelValue="selectSortField" />
      <uni-data-select :modelValue="sortDirection" :localdata="sortDirections" :clear="false" placeholder="默认排序" @update:modelValue="selectSortDirection" />
    </view>
    <text v-if="loadError">{{ loadError }}</text>
    <uni-table :loading="loading" border stripe emptyText="暂无数据">
      <uni-tr><uni-th>名称</uni-th><uni-th>Where</uni-th><uni-th>精确数据管理匹配字段</uni-th><uni-th>config_table_name</uni-th><uni-th>parent_config_table_id</uni-th><uni-th>parent_config_table_name</uni-th><uni-th>页面类型</uni-th><uni-th>parent_config_page_id</uni-th><uni-th>parent_config_page_name</uni-th><uni-th>yz_project_name</uni-th><uni-th>import_no</uni-th><uni-th>insert_time</uni-th><uni-th>update_time</uni-th><uni-th>insert_manager_id</uni-th><uni-th>update_manager_id</uni-th><uni-th>insert_manager_real_name</uni-th><uni-th>update_manager_real_name</uni-th><uni-th>insert_user_name</uni-th><uni-th>update_user_name</uni-th><uni-th>所属后台</uni-th><uni-th>操作</uni-th></uni-tr>
      <uni-tr v-for="row in records" :key="row.id"><uni-td>{{ displayValue(row, 'name') }}</uni-td><uni-td>{{ displayValue(row, 'whereSql') }}</uni-td><uni-td>{{ displayValue(row, 'exactDataManageMatchField') }}</uni-td><uni-td>{{ displayValue(row, 'configTableName') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigTableId') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigTableName') }}</uni-td><uni-td>{{ displayValue(row, 'type') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigPageId') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigPageName') }}</uni-td><uni-td>{{ displayValue(row, 'yzProjectName') }}</uni-td><uni-td>{{ displayValue(row, 'importNo') }}</uni-td><uni-td>{{ displayValue(row, 'insertTime') }}</uni-td><uni-td>{{ displayValue(row, 'updateTime') }}</uni-td><uni-td>{{ displayValue(row, 'insertManagerId') }}</uni-td><uni-td>{{ displayValue(row, 'updateManagerId') }}</uni-td><uni-td>{{ displayValue(row, 'insertManagerRealName') }}</uni-td><uni-td>{{ displayValue(row, 'updateManagerRealName') }}</uni-td><uni-td>{{ displayValue(row, 'insertUserName') }}</uni-td><uni-td>{{ displayValue(row, 'updateUserName') }}</uni-td><uni-td>{{ displayValue(row, 'yzAdminTypeId') }}</uni-td><uni-td><button size="mini" @click="openDetail(row.id)">详情</button></uni-td></uni-tr>
    </uni-table>
    <view><button size="mini" :disabled="pageNo <= 1 || loading" @click="movePage(-1)">上一页</button><text>{{ pageNo }} / {{ pages }}</text><button size="mini" :disabled="pageNo >= pages || loading" @click="movePage(1)">下一页</button></view>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
import utils from '@/common/utils';
const filterFields = [{"field":"name","type":"string?","name":"\u540D\u79F0"}];
const sortFields = [];
const displayFields = {"name":{"alias":null,"optionField":null},"configTableTableName":{"alias":null,"optionField":null},"orderBy":{"alias":null,"optionField":null},"whereSql":{"alias":null,"optionField":null},"exactDataManageMatchField":{"alias":null,"optionField":null},"pageSize":{"alias":null,"optionField":null},"isEdit":{"alias":null,"optionField":null},"isNew":{"alias":null,"optionField":null},"isUpdate":{"alias":null,"optionField":null},"isDelete":{"alias":null,"optionField":null},"isSearch":{"alias":null,"optionField":null},"isSum":{"alias":null,"optionField":null},"isBatchDelete":{"alias":null,"optionField":null},"isImport":{"alias":null,"optionField":null},"isExport":{"alias":null,"optionField":null},"isMultiSelect":{"alias":null,"optionField":null},"isExportEdit":{"alias":null,"optionField":null},"exportMethod":{"alias":null,"optionField":null},"isCopy":{"alias":null,"optionField":null},"readOnlyCondition":{"alias":null,"optionField":null},"importMethod":{"alias":null,"optionField":null},"buttonVisible":{"alias":null,"optionField":null},"isAfterAdd":{"alias":null,"optionField":null},"isAfterSave":{"alias":null,"optionField":null},"isBeforeSave":{"alias":null,"optionField":null},"childPlace":{"alias":"childPlaceShow","optionField":null},"isAfterDelete":{"alias":null,"optionField":null},"isRepeatPwd":{"alias":null,"optionField":null},"saveAutoClose":{"alias":null,"optionField":null},"childStyle":{"alias":null,"optionField":null},"isBeforeDelete":{"alias":null,"optionField":null},"configTableName":{"alias":null,"optionField":null},"isAutoHide":{"alias":null,"optionField":null},"isListEdit":{"alias":null,"optionField":null},"parentConfigTableId":{"alias":null,"optionField":null},"parentConfigTableName":{"alias":null,"optionField":null},"type":{"alias":"typeShow","optionField":null},"parentConfigPageId":{"alias":null,"optionField":null},"parentConfigPageName":{"alias":null,"optionField":null},"yzProjectName":{"alias":null,"optionField":null},"importNo":{"alias":null,"optionField":null},"insertTime":{"alias":null,"optionField":null},"updateTime":{"alias":null,"optionField":null},"insertManagerId":{"alias":null,"optionField":null},"updateManagerId":{"alias":null,"optionField":null},"insertManagerRealName":{"alias":null,"optionField":null},"updateManagerRealName":{"alias":null,"optionField":null},"insertUserName":{"alias":null,"optionField":null},"updateUserName":{"alias":null,"optionField":null},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId"},"id":{"alias":null,"optionField":null}};
const rtfFields = [];
export default {
  data() { return { records: [], pageNo: 1, pages: 1, loading: false, loadError: '', parentId: '', selecting: false, options: {}, draftFilters: {}, appliedFilters: {}, orderBy: null, sortField: '', sortDirection: '', filterFields, filterGaps: [], sortFields,
    sortDirections: [{text: '默认排序', value: ''}, {text: '升序', value: 'ascending'}, {text: '降序', value: 'descending'}], canCreate: false, canImport: false, canExport: false }; },
  /** 父记录和选择页上下文在筛选、排序及翻页期间保持不变。 */
  onLoad(options) { this._alive = true; this._requestNo = 0; this._optionRequests = []; this.parentId = options.parentId || ''; this.selecting = options.select === '1'; this.loadOptions(); },
  onShow() { this.loadPage(); },
  /** 先失效所有回调，再取消请求，离开的页面不会接收迟到数据。 */
  onUnload() { this._alive = false; this._requestNo++; this._request?.abort?.(); this._optionRequests.forEach(task => task?.abort?.()); },
  methods: {
    /** 导入保留客户端读取Excel并提交中文标题JSON的协议。 */
    initImport() { yzp.chooseImportFile(rows => { yzp.request({ url: '/admin/auto/config_page/1869016706289577986/importDatas1869016706289577986', method: 'POST', sendSessionId: true, data: { requestJson: rows, parentId: this.parentId || null }, success: res => yzp.importSuccessAction(res, this) }); }); },
    /** 导出使用已提交筛选、排序和父记录，不混入尚未点击查询的草稿。 */
    initExport() { yzp.request({ url: '/admin/auto/config_page/1869016706289577986/initExport', method: 'POST', sendSessionId: true, data: this.pageRequest(this.pageNo), success: res => { if (res.code === 200) utils.openUrl(getApp().globalData.environment.base_url + '/admin/sys/export/export?exportId=' + encodeURIComponent(res.data)); } }); },
    /** 导入后只重读第一页，已提交查询和父上下文继续保留。 */
    refreshData() { this.loadPage(1); },
    /** 编辑筛选草稿不立即改变正在展示的查询快照。 */
    setFilter(field, value) { if (filterFields.some(item => item.field === field)) this.draftFilters[field] = Array.isArray(value) ? value.slice() : value; },
    /** 仅提交生成白名单中的有效值，保留 false/0，空值与未填写字段都省略。 */
    compileFilters() {
      const filters = {};
      for (const rule of filterFields) {
        const value = this.draftFilters[rule.field];
        if (value === undefined || value === null || typeof value === 'string' && !value.trim() || Array.isArray(value) && value.length === 0) continue;
        if (Array.isArray(value)) {
          if (rule.type !== 'DateTime?' || value.length !== 2 || value.some(item => typeof item !== 'string' || !item.trim())) throw new Error('请选择完整日期范围：' + rule.name);
          const start = Date.parse(value[0].replace(' ', 'T')), end = Date.parse(value[1].replace(' ', 'T'));
          if (!Number.isFinite(start) || !Number.isFinite(end) || start > end) throw new Error('日期范围无效：' + rule.name);
          filters[rule.field] = value.slice();
        } else if (typeof value === 'object') throw new Error('筛选值无效：' + rule.name);
        else {
          if (rule.type === 'bool?' && typeof value !== 'boolean') throw new Error('请选择有效布尔值：' + rule.name);
          if (['long', 'long?', 'int?'].includes(rule.type) && !/^[+-]?\d+$/.test(String(value))) throw new Error('请输入整数：' + rule.name);
          if (rule.type === 'int?' && (!Number.isSafeInteger(Number(value)) || Number(value) < -2147483648 || Number(value) > 2147483647)) throw new Error('整数超出范围：' + rule.name);
          if (['decimal?', 'double?'].includes(rule.type) && (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(String(value)) || !Number.isFinite(Number(value)))) throw new Error('请输入有效数值：' + rule.name);
          if (rule.type === 'string?' && typeof value !== 'string') throw new Error('请输入文本：' + rule.name);
          filters[rule.field] = value;
        }
      }
      return filters;
    },
    /** 查询确认时冻结草稿；无效条件不覆盖先前已提交查询。 */
    applyFilters() { try { this.appliedFilters = this.compileFilters(); this.loadPage(1); } catch (error) { uni.showModal({ showCancel: false, content: error.message }); } },
    /** 重置只清理筛选，父记录、选择模式和用户排序继续保留。 */
    resetFilters() { this.draftFilters = {}; this.appliedFilters = {}; this.loadPage(1); },
    /** 表头按真实 PageRequest 的 column/order 传递排序，不发送 SQL。 */
    changeSort(field, event) {
      if (!sortFields.some(item => item.value === field) || !event || !['ascending', 'descending', null].includes(event.order)) return;
      this.sortField = event.order ? field : ''; this.sortDirection = event.order || ''; this.orderBy = event.order ? { column: field, order: event.order } : null; this.loadPage(1);
    },
    /** 下拉排序覆盖移动端和不在列表中展示的开放排序字段。 */
    selectSortField(field) { if (field && !sortFields.some(item => item.value === field)) return; this.sortField = field || ''; this.sortDirection = field ? this.sortDirection || 'ascending' : ''; this.orderBy = field ? { column: field, order: this.sortDirection } : null; this.loadPage(1); },
    /** 默认排序清空显式 orderBy，升降序要求先选择已开放字段。 */
    selectSortDirection(order) { if (!['', 'ascending', 'descending'].includes(order)) return; if (order && !this.sortField) { uni.showToast({ title: '请先选择排序字段', icon: 'none' }); return; } this.sortDirection = order; this.orderBy = order ? { column: this.sortField, order } : null; this.loadPage(1); },
    /** 每次请求复制已提交查询，防止后续输入或翻页修改正在发送的对象。 */
    pageRequest(pageNo) { return { pageNo, pageSize: 10, filters: JSON.parse(JSON.stringify(this.appliedFilters)), parentId: this.parentId || null, orderBy: this.orderBy ? { ...this.orderBy } : null }; },
    /** 真实登录及工作区快照用于隔离迟到的读响应，不能替代后端授权。 */
    currentIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]); },
    /** 最新查询完成后才提交页码和行；失败保留已确认数据，乱序响应不可回退列表。 */
    loadPage(target = this.pageNo) {
      if (!this._alive) return;
      const identity = this.currentIdentity();
      if (this._listIdentity && this._listIdentity !== identity) { this.records = []; this.pages = 1; this.pageNo = 1; target = 1; }
      this._listIdentity = identity;
      const sequence = ++this._requestNo, data = this.pageRequest(target), parentId = this.parentId;
      this._request?.abort?.(); this.loading = true; this.loadError = '';
      const current = () => this._alive && sequence === this._requestNo && parentId === this.parentId && identity === this.currentIdentity();
      this._request = yzp.request({ url: '/admin/auto/config_page/1869016706289577986/page', method: 'POST', sendSessionId: true, data, isCurrent: current,
        success: res => { if (!current()) return; if (res.code === 200 && res.data && Array.isArray(res.data.records)) { this.records = res.data.records; this.pages = Math.max(1, Number(res.data.pages) || 1); this.pageNo = target; } else this.loadError = res.msg || '读取列表失败。'; },
        fail: () => { if (current()) this.loadError = '读取列表失败，请重试。'; }, complete: () => { if (current()) this.loading = false; } });
      if (!this._request && current()) this.loading = false;
    },
    /** 翻页只调整请求页码，不改变筛选、排序和父记录。 */
    movePage(delta) { if (this.loading) return; const target = Math.max(1, Math.min(this.pages, this.pageNo + delta)); if (target !== this.pageNo) this.loadPage(target); },
    /** 关系选项来自当前页已编译端点，父记录或身份变化后不接受旧选项。 */
    loadOptions() {
      const parentId = this.parentId || null, identity = this.currentIdentity();
      const current = () => this._alive && parentId === (this.parentId || null) && identity === this.currentIdentity();
      this._optionRequests.push(yzp.request({ url: '/admin/auto/config_page/1869016706289577986/findNameIdYzAdminType2200000000000000105', method: 'POST', data: { parentId }, sendSessionId: true, isCurrent: current, success: res => { if (current() && res.code === 200 && Array.isArray(res.data)) this.options.yzAdminTypeId = res.data; } }));
    },
    /** 优先显示后端真实别名，再从合法选项映射名称，缺失名称时保留原 ID。 */
    displayValue(row, field) {
      const definition = displayFields[field];
      if (definition?.alias && row[definition.alias] !== undefined && row[definition.alias] !== null && row[definition.alias] !== '') return row[definition.alias];
      if (definition?.optionField && row[field] !== null && row[field] !== undefined) { const option = (this.options[definition.optionField] || []).find(item => String(item.value ?? item.id) === String(row[field])); if (option && (option.text ?? option.name) !== null && (option.text ?? option.name) !== undefined && (option.text ?? option.name) !== '') return option.text ?? option.name; }
      return row[field] ?? '';
    },
    /** 列表富文本复用原预览页与 sendRtf 事件，不把 HTML 当普通文本字段编辑。 */
    previewRtf(row, field) { if (!rtfFields.includes(field)) return; uni.navigateTo({ url: '/pages/main/rtf/rtf?action=review', success: result => result.eventChannel.emit('sendRtf', { data: row[field] || '' }) }); },
    /** 跳转保持既有生成页面命名和父记录上下文。 */
    openDetail(id) { if (this.selecting && id) { const row = this.records.find(record => record.id === id); this.getOpenerEventChannel().emit('selected', row); uni.navigateBack(); return; } uni.navigateTo({ url: '/pages/yzp/config_page-detail-1869016706289577986?id=' + encodeURIComponent(id) + '&parentId=' + encodeURIComponent(this.parentId) }); }
  }
};
</script>
