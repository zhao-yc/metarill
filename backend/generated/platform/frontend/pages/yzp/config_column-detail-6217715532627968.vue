<!-- 由 Metarill 元数据生成；未编辑字段不进入更新请求。 -->
<template>
  <view class="yzp-page uni-padding-wrap">
    <uni-forms ref="form" :modelValue="entity">
      <uni-forms-item label="名称" name="name"><uni-easyinput :modelValue="entity.name" @update:modelValue="change('name', $event)" /></uni-forms-item>
      <uni-forms-item label="关联表" name="joinTable"><uni-easyinput :modelValue="entity.joinTable" @update:modelValue="change('joinTable', $event)" /></uni-forms-item>
      <uni-forms-item label="is_hide_select_grid" name="isHideSelectGrid"><uni-easyinput :modelValue="entity.isHideSelectGrid" @update:modelValue="change('isHideSelectGrid', $event)" /></uni-forms-item>
      <uni-forms-item label="联接字段" name="isJoinColumn"><uni-data-select :modelValue="entity.isJoinColumn" @update:modelValue="change('isJoinColumn', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="数据类型" name="columnType"><uni-data-select :modelValue="entity.columnType" @update:modelValue="change('columnType', $event)" :localdata="[{&quot;text&quot;:&quot;\u5B57\u7B26&quot;,&quot;value&quot;:&quot;nvarchar&quot;},{&quot;text&quot;:&quot;\u6574\u6570&quot;,&quot;value&quot;:&quot;int&quot;},{&quot;text&quot;:&quot;\u5C0F\u6570&quot;,&quot;value&quot;:&quot;decimal&quot;},{&quot;text&quot;:&quot;\u65E5\u671F\u65F6\u95F4&quot;,&quot;value&quot;:&quot;datetime&quot;},{&quot;text&quot;:&quot;\u4E8C\u8FDB\u5236\u6570\u636E&quot;,&quot;value&quot;:&quot;varbinary&quot;},{&quot;text&quot;:&quot;Point&quot;,&quot;value&quot;:&quot;point&quot;}]" /></uni-forms-item>
      <uni-forms-item label="column_constrain" name="columnConstrain"><uni-easyinput :modelValue="entity.columnConstrain" @update:modelValue="change('columnConstrain', $event)" /></uni-forms-item>
      <uni-forms-item label="长度" name="columnLength"><uni-easyinput :modelValue="entity.columnLength" @update:modelValue="change('columnLength', $event)" /></uni-forms-item>
      <uni-forms-item label="列全名" name="columnFullName"><uni-easyinput :modelValue="entity.columnFullName" @update:modelValue="change('columnFullName', $event)" /></uni-forms-item>
      <uni-forms-item label="is_auto_post_back_query" name="isAutoPostBackQuery"><uni-data-select :modelValue="entity.isAutoPostBackQuery" @update:modelValue="change('isAutoPostBackQuery', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="保存选项" name="saveOption"><uni-easyinput :modelValue="entity.saveOption" @update:modelValue="change('saveOption', $event)" /></uni-forms-item>
      <uni-forms-item label="display_option" name="displayOption"><uni-easyinput :modelValue="entity.displayOption" @update:modelValue="change('displayOption', $event)" /></uni-forms-item>
      <uni-forms-item label="字段描述" name="description"><uni-easyinput :modelValue="entity.description" @update:modelValue="change('description', $event)" /></uni-forms-item>
      <uni-forms-item label="开发者备注" name="developerRemarks"><uni-easyinput :modelValue="entity.developerRemarks" @update:modelValue="change('developerRemarks', $event)" /></uni-forms-item>
      <uni-forms-item label="is_auto_update" name="isAutoUpdate"><uni-easyinput :modelValue="entity.isAutoUpdate" @update:modelValue="change('isAutoUpdate', $event)" /></uni-forms-item>
      <uni-forms-item label="复制" name="isCopy"><uni-easyinput :modelValue="entity.isCopy" @update:modelValue="change('isCopy', $event)" /></uni-forms-item>
      <uni-forms-item label="query_default_value" name="queryDefaultValue"><uni-easyinput :modelValue="entity.queryDefaultValue" @update:modelValue="change('queryDefaultValue', $event)" /></uni-forms-item>
      <uni-forms-item label="列名称" name="columnName"><uni-easyinput :modelValue="entity.columnName" @update:modelValue="change('columnName', $event)" /></uni-forms-item>
      <uni-forms-item label="匹配类型" name="matchType"><uni-data-select :modelValue="entity.matchType" @update:modelValue="change('matchType', $event)" :localdata="[{&quot;text&quot;:&quot;\u7CBE\u786E&quot;,&quot;value&quot;:&quot;P&quot;},{&quot;text&quot;:&quot;\u524D\u5339\u914D&quot;,&quot;value&quot;:&quot;L&quot;},{&quot;text&quot;:&quot;\u540E\u5339\u914D&quot;,&quot;value&quot;:&quot;R&quot;},{&quot;text&quot;:&quot;\u8303\u56F4&quot;,&quot;value&quot;:&quot;B&quot;},{&quot;text&quot;:&quot;\u591A\u884C&quot;,&quot;value&quot;:&quot;M&quot;},{&quot;text&quot;:&quot;\u5217\u8868\u8F93\u5165\u67E5\u8BE2&quot;,&quot;value&quot;:&quot;ListToText&quot;}]" /></uni-forms-item>
      <uni-forms-item label="width" name="width"><uni-easyinput :modelValue="entity.width" @update:modelValue="change('width', $event)" /></uni-forms-item>
      <uni-forms-item label="config_table_name" name="configTableName"><uni-easyinput :modelValue="entity.configTableName" @update:modelValue="change('configTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="is_in_grid" name="isInGrid"><uni-easyinput :modelValue="entity.isInGrid" @update:modelValue="change('isInGrid', $event)" /></uni-forms-item>
      <uni-forms-item label="is_in_filter" name="isInFilter"><uni-easyinput :modelValue="entity.isInFilter" @update:modelValue="change('isInFilter', $event)" /></uni-forms-item>
      <uni-forms-item label="is_in_edit" name="isInEdit"><uni-easyinput :modelValue="entity.isInEdit" @update:modelValue="change('isInEdit', $event)" /></uni-forms-item>
      <uni-forms-item label="控件类型" name="controlKind"><uni-data-select :modelValue="entity.controlKind" @update:modelValue="change('controlKind', $event)" :localdata="[{&quot;text&quot;:&quot;--&quot;,&quot;value&quot;:&quot;&quot;},{&quot;text&quot;:&quot;\u4E0B\u62C9\u5217\u8868&quot;,&quot;value&quot;:&quot;List&quot;},{&quot;text&quot;:&quot;\u5E38\u91CF\u5217\u8868&quot;,&quot;value&quot;:&quot;ConstList&quot;},{&quot;text&quot;:&quot;\u5E38\u91CF\u4E0B\u62C9\u5217\u8868&quot;,&quot;value&quot;:&quot;ConstDownList&quot;},{&quot;text&quot;:&quot;\u590D\u9009\u6846&quot;,&quot;value&quot;:&quot;CheckBox&quot;},{&quot;text&quot;:&quot;\u590D\u9009\u6846-\u6570\u636E\u5E93&quot;,&quot;value&quot;:&quot;MultiCheckBoxDatabase&quot;},{&quot;text&quot;:&quot;\u591A\u884C\u8F93\u5165\u6846&quot;,&quot;value&quot;:&quot;TextArea&quot;},{&quot;text&quot;:&quot;\u5BCC\u6587\u672C&quot;,&quot;value&quot;:&quot;RTF&quot;},{&quot;text&quot;:&quot;\u6587\u4EF6\u4E0A\u4F20&quot;,&quot;value&quot;:&quot;Upload&quot;},{&quot;text&quot;:&quot;\u89C6\u9891&quot;,&quot;value&quot;:&quot;Video&quot;},{&quot;text&quot;:&quot;\u56FE\u7247&quot;,&quot;value&quot;:&quot;Image&quot;},{&quot;text&quot;:&quot;Base64\u56FE\u7247&quot;,&quot;value&quot;:&quot;Base64Image&quot;},{&quot;text&quot;:&quot;\u591A\u56FE\u7247&quot;,&quot;value&quot;:&quot;MultiImage&quot;},{&quot;text&quot;:&quot;\u7F51\u7EDC\u56FE\u7247&quot;,&quot;value&quot;:&quot;IntenetImage&quot;},{&quot;text&quot;:&quot;\u9009\u62E9\u9875&quot;,&quot;value&quot;:&quot;SelectPage&quot;},{&quot;text&quot;:&quot;\u591A\u9009\u62E9\u9875&quot;,&quot;value&quot;:&quot;MultiSelectPage&quot;},{&quot;text&quot;:&quot;\u65E5\u671F&quot;,&quot;value&quot;:&quot;date&quot;},{&quot;text&quot;:&quot;\u5E74\u6708&quot;,&quot;value&quot;:&quot;month&quot;},{&quot;text&quot;:&quot;\u65F6\u95F4&quot;,&quot;value&quot;:&quot;time&quot;},{&quot;text&quot;:&quot;\u65E5\u671F\u65F6\u95F4&quot;,&quot;value&quot;:&quot;date_time&quot;},{&quot;text&quot;:&quot;\u5B9A\u5236\u63A7\u4EF6&quot;,&quot;value&quot;:&quot;CustomControl&quot;},{&quot;text&quot;:&quot;\u7EA7\u8FDE\u9009\u62E9&quot;,&quot;value&quot;:&quot;CascadeTree&quot;},{&quot;text&quot;:&quot;\u6587\u4EF6\u5E93&quot;,&quot;value&quot;:&quot;FileLibrary&quot;}]" /></uni-forms-item>
      <uni-forms-item label="is_read_only" name="isReadOnly"><uni-easyinput :modelValue="entity.isReadOnly" @update:modelValue="change('isReadOnly', $event)" /></uni-forms-item>
      <uni-forms-item label="控件明细" name="controlDetail"><yzp-control-detail-editor :entity="entity" :record-id="id" :readonly="false" @receive="applyCustom" /></uni-forms-item>
      <uni-forms-item label="is_must" name="isMust"><uni-easyinput :modelValue="entity.isMust" @update:modelValue="change('isMust', $event)" /></uni-forms-item>
      <uni-forms-item label="控件类型-筛选" name="filterControlKind"><uni-data-select :modelValue="entity.filterControlKind" @update:modelValue="change('filterControlKind', $event)" :localdata="[{&quot;text&quot;:&quot;--&quot;,&quot;value&quot;:&quot;&quot;},{&quot;text&quot;:&quot;\u4E0B\u62C9\u5217\u8868&quot;,&quot;value&quot;:&quot;List&quot;},{&quot;text&quot;:&quot;\u590D\u9009\u6846&quot;,&quot;value&quot;:&quot;CheckBox&quot;}]" /></uni-forms-item>
      <uni-forms-item label="link_config_table_name" name="linkConfigTableName"><uni-easyinput :modelValue="entity.linkConfigTableName" @update:modelValue="change('linkConfigTableName', $event)" /></uni-forms-item>
      <uni-forms-item label="link_config_page_name" name="linkConfigPageName"><uni-easyinput :modelValue="entity.linkConfigPageName" @update:modelValue="change('linkConfigPageName', $event)" /></uni-forms-item>
      <uni-forms-item label="排序号" name="sortNo"><uni-easyinput :modelValue="entity.sortNo" @update:modelValue="change('sortNo', $event)" /></uni-forms-item>
      <uni-forms-item label="默认值" name="defaultValue"><uni-easyinput :modelValue="entity.defaultValue" @update:modelValue="change('defaultValue', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_id" name="yzProjectId"><text>{{ entity.yzProjectId }}</text></uni-forms-item>
      <uni-forms-item label="新建初始值" name="initValue"><uni-easyinput :modelValue="entity.initValue" @update:modelValue="change('initValue', $event)" /></uni-forms-item>
      <uni-forms-item label="yz_project_name" name="yzProjectName"><text>{{ entity.yzProjectName }}</text></uni-forms-item>
      <uni-forms-item label="is_print" name="isPrint"><uni-easyinput :modelValue="entity.isPrint" @update:modelValue="change('isPrint', $event)" /></uni-forms-item>
      <uni-forms-item label="import_no" name="importNo"><uni-easyinput :modelValue="entity.importNo" @update:modelValue="change('importNo', $event)" /></uni-forms-item>
      <uni-forms-item label="control_width" name="controlWidth"><uni-easyinput :modelValue="entity.controlWidth" @update:modelValue="change('controlWidth', $event)" /></uni-forms-item>
      <uni-forms-item label="col_span" name="colSpan"><uni-easyinput :modelValue="entity.colSpan" @update:modelValue="change('colSpan', $event)" /></uni-forms-item>
      <uni-forms-item label="align" name="align"><uni-easyinput :modelValue="entity.align" @update:modelValue="change('align', $event)" /></uni-forms-item>
      <uni-forms-item label="is_auto_post_back" name="isAutoPostBack"><uni-data-select :modelValue="entity.isAutoPostBack" @update:modelValue="change('isAutoPostBack', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="动态显示规则" name="visibleCondition"><uni-easyinput :modelValue="entity.visibleCondition" @update:modelValue="change('visibleCondition', $event)" /></uni-forms-item>
      <uni-forms-item label="开启排序" name="isSort"><uni-data-select :modelValue="entity.isSort" @update:modelValue="change('isSort', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="sum_kind" name="sumKind"><uni-easyinput :modelValue="entity.sumKind" @update:modelValue="change('sumKind', $event)" /></uni-forms-item>
      <uni-forms-item label="虚拟字段" name="isVirtualColumn"><uni-data-select :modelValue="entity.isVirtualColumn" @update:modelValue="change('isVirtualColumn', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="逻辑类型" name="logicalType"><uni-easyinput :modelValue="entity.logicalType" @update:modelValue="change('logicalType', $event)" /></uni-forms-item>
      <uni-forms-item label="移动端列表页占列数" name="mobileListPageColSpan"><uni-data-select :modelValue="entity.mobileListPageColSpan" @update:modelValue="change('mobileListPageColSpan', $event)" :localdata="[{&quot;text&quot;:&quot;1&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;2&quot;,&quot;value&quot;:2}]" /></uni-forms-item>
      <uni-forms-item label="数值精度" name="precision"><uni-easyinput :modelValue="entity.precision" @update:modelValue="change('precision', $event)" /></uni-forms-item>
      <uni-forms-item label="数据库必填" name="isDbNotNull"><uni-data-select :modelValue="entity.isDbNotNull" @update:modelValue="change('isDbNotNull', $event)" :localdata="[{&quot;text&quot;:&quot;是&quot;,&quot;value&quot;:1},{&quot;text&quot;:&quot;否&quot;,&quot;value&quot;:0}]" /></uni-forms-item>
      <uni-forms-item label="小数位数" name="scale"><uni-easyinput :modelValue="entity.scale" @update:modelValue="change('scale', $event)" /></uni-forms-item>
    </uni-forms>
    <button v-if="id ? canSave : canCreate" :disabled="saving" @click="save">保存</button>
    <button v-if="id && canCopy" :disabled="saving" @click="copy">复制</button>
    <button v-if="id && canDelete" :disabled="saving" @click="remove">删除</button>
  </view>
</template>
<script>
import yzp from '@/common/yzp';
const writableFields = ["name","joinTable","isHideSelectGrid","isJoinColumn","columnType","columnConstrain","columnLength","columnFullName","isAutoPostBackQuery","saveOption","displayOption","description","developerRemarks","isAutoUpdate","isCopy","queryDefaultValue","columnName","matchType","width","configTableName","isInGrid","isInFilter","isInEdit","controlKind","isReadOnly","controlDetail","isMust","filterControlKind","linkConfigTableName","linkConfigPageName","sortNo","defaultValue","initValue","isPrint","importNo","controlWidth","colSpan","align","isAutoPostBack","visibleCondition","isSort","sumKind","isVirtualColumn","logicalType","mobileListPageColSpan","precision","isDbNotNull","scale"];
export default {
  data() { return { id: '', parentId: '', entity: {}, changes: {}, options: {}, saving: false, canCreate: false, canSave: true, canDelete: true, canCopy: false }; },
  onLoad(options) { this.id = options.id || ''; this.parentId = options.parentId || ''; this.loadOptions(); if (this.id) this.load(); },
  methods: {
    /** POST复制仅提交源标识，服务端重建新记录与审计身份。 */
    copy() { uni.showModal({ title: '复制确认', content: '确定复制当前记录？', success: modal => { if (!modal.confirm) return; yzp.request({ url: '/admin/auto/config_column/6217715532627968/copy/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', sendSessionId: true, success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '复制成功', icon: 'success' }); } } }); } }); },
    /** 关系选项由当前页接口实施范围和权限校验。 */
    loadOptions() {

    },
    /** 定制编辑器只能回填本页允许提交的字段。 */
    applyCustom(values) { for (const field of writableFields) { if (Object.prototype.hasOwnProperty.call(values, field) && JSON.stringify(values[field]) !== JSON.stringify(this.entity[field])) this.change(field, values[field]); } },
    /** 选择页复用生成列表，返回标识而非扩大写入字段。 */
    selectPage(field, path) { uni.navigateTo({ url: path + '?select=1', events: { selected: row => this.change(field, row.id) } }); },
    /** 读取明细只接收服务端页级 VO。 */
    load() { yzp.request({ url: '/admin/auto/config_column/6217715532627968/detail/' + encodeURIComponent(this.id) + '?parentId=' + encodeURIComponent(this.parentId), method: 'GET', sendSessionId: true,
      success: res => { if (res.code === 200) { this.entity = res.data; this.changes = {}; } } }); },
    /** 只有用户修改过的字段进入更新对象，允许显式 null。 */
    change(field, value) { this.entity[field] = value; this.changes[field] = value; },
    /** 新增和修改均只发送元数据允许编辑的字段。 */
    save() {
      const data = {};
      for (const field of writableFields) { if (this.id ? Object.prototype.hasOwnProperty.call(this.changes, field) : Object.prototype.hasOwnProperty.call(this.entity, field)) data[field] = this.id ? this.changes[field] : this.entity[field]; }
      if (this.id) data.id = this.id;
      this.saving = true;
      yzp.request({ url: '/admin/auto/config_column/6217715532627968/' + (this.id ? 'update' : 'add') + '?parentId=' + encodeURIComponent(this.parentId), method: 'POST', data, sendSessionId: true,
        success: res => { if (res.code === 200) { this.id = res.data.id; this.entity = res.data; this.changes = {}; uni.showToast({ title: '保存成功', icon: 'success' }); } }, complete: () => { this.saving = false; } });
    },
    /** 删除确认后沿用 DELETE 请求体协议。 */
    remove() { uni.showModal({ title: '删除确认', content: '确定删除当前记录？', success: modal => { if (!modal.confirm) return;
      yzp.request({ url: '/admin/auto/config_column/6217715532627968?parentId=' + encodeURIComponent(this.parentId), method: 'DELETE', data: { id: this.id }, sendSessionId: true,
        success: res => { if (res.code === 200) uni.navigateBack(); } }); } }); }
  }
};
</script>
