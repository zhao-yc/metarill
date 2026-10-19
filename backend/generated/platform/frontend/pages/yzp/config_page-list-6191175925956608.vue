<!-- 由 Metarill 元数据生成，使用现有 uni-app 请求和组件体系。 -->
<template>
  <view class="yzp-page">
    <view class="uni-padding-wrap"><text>配置页面</text><button v-if="!multiSelecting && canCreateRecord" size="mini" @click="openDetail('')">新增</button><button v-if="!multiSelecting && canImportRecords" size="mini" @click="initImport">导入</button><button v-if="!multiSelecting && canExport" size="mini" @click="initExport">导出</button></view>
    <view v-if="filterFields.length" class="uni-padding-wrap">
      <uni-forms :modelValue="draftFilters">
<uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="draftFilters.name" @update:modelValue="setFilter('name', $event)" placeholder="请输入名称" /></uni-forms-item>
<uni-forms-item label="表名" name="configTableTableName"><uni-easyinput :modelValue="draftFilters.configTableTableName" @update:modelValue="setFilter('configTableTableName', $event)" placeholder="请输入表名" /></uni-forms-item>
      </uni-forms>
      <button size="mini" @click="applyFilters">查询</button><button size="mini" @click="resetFilters">重置</button>
    </view>
    <view v-for="message in filterGaps" :key="message" class="uni-padding-wrap"><text>{{ message }}</text></view>
    <view v-if="sortFields.length" class="uni-padding-wrap">
      <uni-data-select :modelValue="sortField" :localdata="sortFields" placeholder="排序字段" @update:modelValue="selectSortField" />
      <uni-data-select :modelValue="sortDirection" :localdata="sortDirections" :clear="false" placeholder="默认排序" @update:modelValue="selectSortDirection" />
    </view>
    <text v-if="loadError">{{ loadError }}</text>
    <view v-if="multiSelecting" class="multi-select-page-buttons"><text v-if="selectionError">{{ selectionError }}</text><button size="mini" @click="toggleSelectedOnly">{{ showSelectedOnly ? '查看全部' : '仅看已选' }}</button><button type="primary" size="mini" :disabled="!selectionCanConfirm" @click="sureSelect">选好了（{{ selectedDraftIds.length }}）</button><button size="mini" @click="sureClose">关闭</button></view>
    <uni-table :loading="loading" border stripe emptyText="暂无数据">
      <uni-tr><uni-th v-if="multiSelecting"><button size="mini" :disabled="selectionBusy || readOnlyCondition" @click="selectAll">{{ selectionAllOnPage ? '取消本页' : '全选本页' }}</button></uni-th><uni-th>名称</uni-th><uni-th>表名</uni-th><uni-th>config_table_name</uni-th><uni-th>parent_config_table_id</uni-th><uni-th>parent_config_table_name</uni-th><uni-th>parent_config_page_id</uni-th><uni-th>parent_config_page_name</uni-th><uni-th>yz_project_name</uni-th><uni-th>import_no</uni-th><uni-th>insert_time</uni-th><uni-th>update_time</uni-th><uni-th>insert_manager_id</uni-th><uni-th>update_manager_id</uni-th><uni-th>insert_manager_real_name</uni-th><uni-th>update_manager_real_name</uni-th><uni-th>insert_user_name</uni-th><uni-th>update_user_name</uni-th><uni-th>所属后台</uni-th><uni-th v-if="!multiSelecting">操作</uni-th></uni-tr>
      <uni-tr v-for="row in records" :key="row.id"><uni-td v-if="multiSelecting"><checkbox :checked="selectionHas(row.id)" :disabled="selectionBusy || readOnlyCondition" @click="toggleSelection(row)" /></uni-td><uni-td>{{ displayValue(row, 'name') }}</uni-td><uni-td>{{ displayValue(row, 'configTableTableName') }}</uni-td><uni-td>{{ displayValue(row, 'configTableName') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigTableId') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigTableName') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigPageId') }}</uni-td><uni-td>{{ displayValue(row, 'parentConfigPageName') }}</uni-td><uni-td>{{ displayValue(row, 'yzProjectName') }}</uni-td><uni-td>{{ displayValue(row, 'importNo') }}</uni-td><uni-td>{{ displayValue(row, 'insertTime') }}</uni-td><uni-td>{{ displayValue(row, 'updateTime') }}</uni-td><uni-td>{{ displayValue(row, 'insertManagerId') }}</uni-td><uni-td>{{ displayValue(row, 'updateManagerId') }}</uni-td><uni-td>{{ displayValue(row, 'insertManagerRealName') }}</uni-td><uni-td>{{ displayValue(row, 'updateManagerRealName') }}</uni-td><uni-td>{{ displayValue(row, 'insertUserName') }}</uni-td><uni-td>{{ displayValue(row, 'updateUserName') }}</uni-td><uni-td>{{ displayValue(row, 'yzAdminTypeId') }}</uni-td><uni-td v-if="!multiSelecting"><button size="mini" @click="openDetail(row.id)">详情</button></uni-td></uni-tr>
    </uni-table>
    <view><button size="mini" :disabled="pageNo <= 1 || loading" @click="movePage(-1)">上一页</button><text>{{ pageNo }} / {{ pages }}</text><button size="mini" :disabled="pageNo >= pages || loading" @click="movePage(1)">下一页</button></view>
    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange">
      <view class="select-page-dialog-view"><uni-section title="配置页面" type="line"><view class="section-view child-detail-dialog-view">
        <PageDetail v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parentId" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup" />
        <button size="mini" @click="closeChildDetailPopup">关闭</button>
      </view></uni-section></view>
    </yzp-popup>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
import { createRelationOptionsMixin } from '@/common/yzp_runtime/relationOptionsMixin';

import utils from '@/common/utils';
import PageDetail from '@/pages/yzp/config_page-detail-6191175925956608.vue';
const filterFields = [{"field":"name","type":"string?","name":"\u540D\u79F0"},{"field":"configTableTableName","type":"string?","name":"\u8868\u540D"}];
const sortFields = [];
const displayFields = {"name":{"alias":null,"optionField":null,"multiNames":false},"configTableTableName":{"alias":null,"optionField":null,"multiNames":false},"orderBy":{"alias":null,"optionField":null,"multiNames":false},"whereSql":{"alias":null,"optionField":null,"multiNames":false},"configTableId":{"alias":"configTableName","optionField":"configTableId","multiNames":false},"exactDataManageMatchField":{"alias":null,"optionField":null,"multiNames":false},"pageSize":{"alias":null,"optionField":null,"multiNames":false},"isEdit":{"alias":null,"optionField":null,"multiNames":false},"isNew":{"alias":null,"optionField":null,"multiNames":false},"isUpdate":{"alias":null,"optionField":null,"multiNames":false},"isDelete":{"alias":null,"optionField":null,"multiNames":false},"isSearch":{"alias":null,"optionField":null,"multiNames":false},"isSum":{"alias":null,"optionField":null,"multiNames":false},"isBatchDelete":{"alias":null,"optionField":null,"multiNames":false},"isImport":{"alias":null,"optionField":null,"multiNames":false},"isExport":{"alias":null,"optionField":null,"multiNames":false},"isMultiSelect":{"alias":null,"optionField":null,"multiNames":false},"isExportEdit":{"alias":null,"optionField":null,"multiNames":false},"exportMethod":{"alias":null,"optionField":null,"multiNames":false},"isCopy":{"alias":null,"optionField":null,"multiNames":false},"readOnlyCondition":{"alias":null,"optionField":null,"multiNames":false},"importMethod":{"alias":null,"optionField":null,"multiNames":false},"buttonVisible":{"alias":null,"optionField":null,"multiNames":false},"isAfterAdd":{"alias":null,"optionField":null,"multiNames":false},"isAfterSave":{"alias":null,"optionField":null,"multiNames":false},"isBeforeSave":{"alias":null,"optionField":null,"multiNames":false},"childPlace":{"alias":"childPlaceShow","optionField":null,"multiNames":false},"isAfterDelete":{"alias":null,"optionField":null,"multiNames":false},"isRepeatPwd":{"alias":null,"optionField":null,"multiNames":false},"saveAutoClose":{"alias":null,"optionField":null,"multiNames":false},"childStyle":{"alias":null,"optionField":null,"multiNames":false},"isBeforeDelete":{"alias":null,"optionField":null,"multiNames":false},"configTableName":{"alias":null,"optionField":null,"multiNames":false},"isAutoHide":{"alias":null,"optionField":null,"multiNames":false},"isListEdit":{"alias":null,"optionField":null,"multiNames":false},"parentConfigTableId":{"alias":null,"optionField":null,"multiNames":false},"parentConfigTableName":{"alias":null,"optionField":null,"multiNames":false},"type":{"alias":"typeShow","optionField":null,"multiNames":false},"isBeforeQuery":{"alias":null,"optionField":null,"multiNames":false},"parentConfigPageId":{"alias":null,"optionField":null,"multiNames":false},"isBeforeGet":{"alias":null,"optionField":null,"multiNames":false},"parentConfigPageName":{"alias":null,"optionField":null,"multiNames":false},"isNotGenerateDetailPage":{"alias":null,"optionField":null,"multiNames":false},"yzProjectName":{"alias":null,"optionField":null,"multiNames":false},"importNo":{"alias":null,"optionField":null,"multiNames":false},"mobileListPageStyleType":{"alias":"mobileListPageStyleTypeShow","optionField":null,"multiNames":false},"isAutoBackOnMobileSave":{"alias":null,"optionField":null,"multiNames":false},"cols":{"alias":null,"optionField":null,"multiNames":false},"insertTime":{"alias":null,"optionField":null,"multiNames":false},"updateTime":{"alias":null,"optionField":null,"multiNames":false},"insertManagerId":{"alias":null,"optionField":null,"multiNames":false},"updateManagerId":{"alias":null,"optionField":null,"multiNames":false},"insertManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"updateManagerRealName":{"alias":null,"optionField":null,"multiNames":false},"insertUserName":{"alias":null,"optionField":null,"multiNames":false},"isUpdateEditColumnsOnly":{"alias":null,"optionField":null,"multiNames":false},"updateUserName":{"alias":null,"optionField":null,"multiNames":false},"detailPageButtonsPosition":{"alias":"detailPageButtonsPositionShow","optionField":null,"multiNames":false},"yzAdminTypeId":{"alias":null,"optionField":"yzAdminTypeId","multiNames":false},"id":{"alias":null,"optionField":null,"multiNames":false}};
const rtfFields = [];
/** 父键保持字符串精度，空值与历史 null/undefined 字面量均视为未保存。 */
const recordId = value => typeof value === 'string' && !['undefined', 'null'].includes(value.trim()) ? value.trim() : '';
export default {
  mixins: [createRelationOptionsMixin([{"field":"configTableId","endpoint":"/admin/auto/config_page/6191175925956608/findNameIdConfigTable1671100304320462849","requireParent":false,"requireTargetParent":false,"sameParent":false,"targetParentField":null,"unresolvedTarget":false},{"field":"yzAdminTypeId","endpoint":"/admin/auto/config_page/6191175925956608/findNameIdYzAdminType2200000000000000105","requireParent":false,"requireTargetParent":false,"sameParent":false,"targetParentField":null,"unresolvedTarget":false}])],
  components: { PageDetail },
  props: { parent_id: { type: String, default: '' }, parent_page_sn: { type: String, default: '' }, is_son_page: { type: Boolean, default: false }, child_page_open_type: { type: Number, default: 1 }, parentReadOnlyCondition: { type: Boolean, default: false } },
  data() { return { multiSelecting: false, permissionsReady: false, permissionVersion: 0, canUpdate: true, childDetailPopupVisible: false, childDetailPopupId: '', childDetailPopupKey: 0, records: [], pageNo: 1, pages: 1, loading: false, loadError: '', parentId: '', selecting: false, options: {}, draftFilters: {}, appliedFilters: {}, orderBy: null, sortField: '', sortDirection: '', filterFields, filterGaps: [], sortFields,
    sortDirections: [{text: '默认排序', value: ''}, {text: '升序', value: 'ascending'}, {text: '降序', value: 'descending'}], canCreate: true, canImport: false, canExport: false }; },
  computed: {
    /** 子列表沿用父页只读；前端权限快照只负责隐藏操作，后端仍验证真实身份。 */
    readOnlyCondition() { this.permissionVersion; return !this.permissionsReady || Boolean(this.parentReadOnlyCondition || getApp().globalData.readOnly); },
    /** 新增、修改、导入分别受本页能力和对应操作权限控制，不能用修改权限统管。 */
    canCreateRecord() { this.permissionVersion; return !this.readOnlyCondition && this.canCreate && yzp.hasPagePermission('6191175925956608', 'is_new'); },
    canEditRecord() { this.permissionVersion; return !this.readOnlyCondition && this.canUpdate && yzp.hasPagePermission('6191175925956608', 'is_update'); },
    canImportRecords() { this.permissionVersion; return !this.readOnlyCondition && this.canImport && yzp.hasPagePermission('6191175925956608', 'is_all'); }
  },
  watch: {
    /** 父记录改变时取消旧读取并重建子列表，不触发父表保存。 */
    parent_id() { if ((this.is_son_page || this.multiSelecting) && this._alive) { this.initializeList({ parentId: this.parent_id }); this.loadPage(1); } }
  },
  mounted() { if (this.is_son_page || this.multiSelecting && !this._alive) { this.initializeList({ parentId: this.parent_id }); this.loadPage(1); } },
  /** 路由和嵌入组件分别初始化，避免嵌入模式依赖不会触发的 onLoad。 */
  onLoad(options) { if (!this.is_son_page) { this._listRouted = true; this.initializeList(options); } },
  onShow() { if (!this.is_son_page) this.loadPage(); },
  onUnload() { this.disposeList(); },
  beforeUnmount() { this.disposeList(); },
  methods: {
    /** 保留查询与排序，换父记录只清空旧数据和旧选项；未保存父键不发出任何查询。 */
    initializeList(options = {}) {
      this.disposeList(); this._alive = true; this._optionRequests = []; this.parentId = recordId(options.parentId || options.parent_id); this.selecting = options.select === '1';
      this.records = []; this.pageNo = 1; this.pages = 1; this.options = {}; this.loading = false; this.loadError = ''; this.permissionsReady = false;
      const epoch = this._epoch, ready = () => { if (this._alive && epoch === this._epoch) { this.permissionsReady = true; this.permissionVersion++; } };
      if (this.is_son_page) ready(); else yzp.refreshCurrentPermissions(ready, ready);
      if (!(false || this.is_son_page) || this.parentId) this.loadOptions();
      this.initializeSelection?.();
    },
    /** 卸载或换父记录时失效列表、候选项和弹层回调。 */
    disposeList() { this.disposeSelection?.(); this._alive = false; this._epoch = (this._epoch || 0) + 1; this._requestNo = (this._requestNo || 0) + 1; this._request?.abort?.(); (this._optionRequests || []).forEach(task => task?.abort?.()); this.closeChildDetailPopup(); },
    /** 导入保留客户端读取Excel并提交中文标题JSON的协议。 */
    initImport() { if (!this.canImportRecords || (false || this.is_son_page) && !this.parentId) return; const parentId = this.parentId, identity = this.currentIdentity(); yzp.chooseImportFile(rows => { if (!this._alive || !this.canImportRecords || parentId !== this.parentId || identity !== this.currentIdentity()) return; yzp.request({ url: '/admin/auto/config_page/6191175925956608/importDatas6191175925956608', method: 'POST', sendSessionId: true, data: { requestJson: rows, parentId: this.parentId || null }, success: res => yzp.importSuccessAction(res, this) }); }); },
    /** 导出使用已提交筛选、排序和父记录，不混入尚未点击查询的草稿。 */
    initExport() { yzp.request({ url: '/admin/auto/config_page/6191175925956608/initExport', method: 'POST', sendSessionId: true, data: this.pageRequest(this.pageNo), success: res => { if (res.code === 200) utils.openUrl(getApp().globalData.environment.base_url + '/admin/sys/export/export?exportId=' + encodeURIComponent(res.data)); } }); },
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
    pageRequest(pageNo) { return { ...(this.multiSelecting ? { selectedIds: this.showSelectedOnly ? this.selectedDraftIds.slice() : [] } : {}), pageNo, pageSize: 14, filters: JSON.parse(JSON.stringify(this.appliedFilters)), parentId: this.parentId || null, orderBy: this.orderBy ? { ...this.orderBy } : null }; },
    /** 真实登录及工作区快照用于隔离迟到的读响应，不能替代后端授权。 */
    currentIdentity() { return JSON.stringify([uni.getStorageSync('loginStateId') || '', uni.getStorageSync('sysParams') || {}]); },
    /** 最新查询完成后才提交页码和行；失败保留已确认数据，乱序响应不可回退列表。 */
    loadPage(target = this.pageNo) {
      if (!this._alive) return;
      if ((false || this.is_son_page) && !this.parentId) { this.loadError = '为避免数据丢失，请先保存当前页面的数据。'; return; }
      if (this.multiSelecting && this.showSelectedOnly && this.selectedDraftIds.length === 0) { this._requestNo++; this._request?.abort?.(); this.records = []; this.pages = 1; this.pageNo = 1; this.loading = false; this.loadError = ''; return; }
      const identity = this.currentIdentity();
      if (this._listIdentity && this._listIdentity !== identity) { this.records = []; this.pages = 1; this.pageNo = 1; target = 1; }
      this._listIdentity = identity;
      const sequence = ++this._requestNo, data = this.pageRequest(target), parentId = this.parentId;
      this._request?.abort?.(); this.loading = true; this.loadError = '';
      const current = () => this._alive && sequence === this._requestNo && parentId === this.parentId && identity === this.currentIdentity();
      this._request = yzp.request({ url: '/admin/auto/config_page/6191175925956608/page', method: 'POST', sendSessionId: true, data, isCurrent: current,
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
      this.refreshRelationLabels?.();
    },
    /** 优先显示后端真实别名，再从合法选项映射名称，缺失名称时保留原 ID。 */
    displayValue(row, field) {
      const definition = displayFields[field];
      // 多选名称仍按原逗号文本展示，HTTP数组与历史JSON文本都不回写实体。
      if (definition?.multiNames) { const value = definition.alias && row[definition.alias] != null ? row[definition.alias] : row[field]; try { const values = typeof value === 'string' ? JSON.parse(value) : value; if (Array.isArray(values)) return values.map(item => item == null ? '' : String(item)).join('，'); } catch {} return value ?? ''; }
      if (definition?.alias && row[definition.alias] !== undefined && row[definition.alias] !== null && row[definition.alias] !== '') return row[definition.alias];
      if (definition?.optionField && row[field] !== null && row[field] !== undefined) { const option = (this.options[definition.optionField] || []).find(item => String(item.value ?? item.id) === String(row[field])); if (option && (option.text ?? option.name) !== null && (option.text ?? option.name) !== undefined && (option.text ?? option.name) !== '') return option.text ?? option.name; }
      return row[field] ?? '';
    },
    /** 列表富文本复用原预览页与 sendRtf 事件，不把 HTML 当普通文本字段编辑。 */
    previewRtf(row, field) { if (!rtfFields.includes(field)) return; uni.navigateTo({ url: '/pages/main/rtf/rtf?action=review', success: result => result.eventChannel.emit('sendRtf', { data: row[field] || '' }) }); },
    /** 子记录变更只刷新本列表，保留父键、筛选与排序，不调用父表单写入口。 */
    handleDetailChange(change) { const action = typeof change === 'string' ? change : change?.action; if (this._alive && ['save', 'delete', 'copy'].includes(action)) this.loadPage(this.pageNo); },
    /** 弹层事件不再向祖先冒泡，关闭后的旧详情事件忽略。 */
    handleChildDetailChange(change) { if (this.childDetailPopupVisible) this.handleDetailChange(change); },
    /** PC 子表弹层保留原组件及 props 合同；移动端仍由 openDetail 导航。 */
    openChildDetailPopup(id) {
      if (!this._alive || !this.parentId || ![1, 2].includes(this.child_page_open_type)) return;
      this.childDetailPopupId = id || ''; this.childDetailPopupKey++; this.childDetailPopupVisible = true;
      const key = this.childDetailPopupKey, parentId = this.parentId, identity = this.currentIdentity();
      this.$nextTick(() => { if (this._alive && this.childDetailPopupVisible && key === this.childDetailPopupKey && parentId === this.parentId && identity === this.currentIdentity()) this.$refs.child_detail_popup?.open('center'); });
    },
    /** 关闭只销毁当前子详情，不影响父记录及查询上下文。 */
    closeChildDetailPopup() { this.childDetailPopupVisible = false; this.childDetailPopupId = ''; this.$refs?.child_detail_popup?.close(); },
    onChildDetailPopupChange(event) { if (!event.show) { this.childDetailPopupVisible = false; this.childDetailPopupId = ''; } },
    /** 子页必须绑定已保存父键；打开方式只影响 PC，URL 的父键继续交给既有后端 Scope 校验。 */
    openDetail(id) {
      if (!this._alive) return;
      if ((false || this.is_son_page) && !this.parentId) { uni.showModal({ showCancel: false, content: '为避免数据丢失，请先保存当前页面的数据' }); return; }
      if (this.multiSelecting) return;
      if (this.selecting && id) { const row = this.records.find(record => record.id === id); this.getOpenerEventChannel().emit('selected', row); uni.navigateBack(); return; }
      if (!id && !this.canCreateRecord) return;
      if (this.is_son_page && ![1, 2].includes(this.child_page_open_type)) { uni.showModal({ showCancel: false, content: '子页面打开方式未支持。' }); return; }
      if (this.is_son_page && this.$deviceInfo?.is_pc && this.child_page_open_type === 2) { this.openChildDetailPopup(id); return; }
      const parentId = this.parentId, epoch = this._epoch, identity = this.currentIdentity();
      const current = () => this._alive && epoch === this._epoch && parentId === this.parentId && identity === this.currentIdentity();
      uni.navigateTo({ url: '/pages/yzp/config_page-detail-6191175925956608?id=' + encodeURIComponent(id) + '&parentId=' + encodeURIComponent(parentId) + '&parentPageId=' + encodeURIComponent(this.parent_page_sn) + '&parentReadOnly=' + (this.readOnlyCondition ? '1' : '0'), events: { onChange: change => { if (current()) this.handleDetailChange(change); } } });
    }
  }
};
</script>
