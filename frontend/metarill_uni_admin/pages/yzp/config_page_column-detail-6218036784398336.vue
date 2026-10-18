<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="页面字段"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="页面字段"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='名称' name='name'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='列名称' name='configColumnColumnName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!configColumnColumnNameReadOnly' v-model='entity.configColumnColumnName' maxlength='50'  />
<text v-if='configColumnColumnNameReadOnly'>{{entity.configColumnColumnName}}</text>
</uni-forms-item>

<uni-forms-item label='排序号' name='configColumnSortNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!configColumnSortNoReadOnly' v-model='entity.configColumnSortNo'  type='number' />
<text v-if='configColumnSortNoReadOnly'>{{entity.configColumnSortNo}}</text>
</uni-forms-item>

<uni-forms-item label='着重显示' name='isImportant'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isImportantReadOnly'>{{(($checkboxRange || [])[entity.isImportant==null?0:entity.isImportant] || {}).text || ''}}</text><template v-if='!isImportantReadOnly'><uni-data-checkbox :disabled='isImportantReadOnly' v-model='entity.isImportant' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='列' name='configColumnId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='configColumnIdReadOnly'>{{readonlyDisplayText(entity.configColumnName)}}</text><template v-if='!configColumnIdReadOnly'><zxz-uni-data-select v-model='entity.configColumnId' :localdata='config_column_list' filterable :disabled='configColumnIdReadOnly' dataKey='name' dataValue='id' @change='config_columnSelectChange' /></template>
</uni-forms-item>

<uni-forms-item label='视图' name='configPageId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='configPageIdReadOnly'>{{readonlyDisplayText(entity.configPageName)}}</text><template v-if='!configPageIdReadOnly'><zxz-uni-data-select v-model='entity.configPageId' :localdata='config_page_list' filterable :disabled='configPageIdReadOnly' dataKey='name' dataValue='id' @change='config_pageSelectChange' /></template>
</uni-forms-item>

<uni-forms-item label='is_in_grid' name='isInGrid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isInGridReadOnly'>{{(($checkboxRange || [])[entity.isInGrid==null?0:entity.isInGrid] || {}).text || ''}}</text><template v-if='!isInGridReadOnly'><uni-data-checkbox :disabled='isInGridReadOnly' v-model='entity.isInGrid' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_in_filter' name='isInFilter'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isInFilterReadOnly'>{{(($checkboxRange || [])[entity.isInFilter==null?0:entity.isInFilter] || {}).text || ''}}</text><template v-if='!isInFilterReadOnly'><uni-data-checkbox :disabled='isInFilterReadOnly' v-model='entity.isInFilter' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_in_edit' name='isInEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isInEditReadOnly'>{{(($checkboxRange || [])[entity.isInEdit==null?0:entity.isInEdit] || {}).text || ''}}</text><template v-if='!isInEditReadOnly'><uni-data-checkbox :disabled='isInEditReadOnly' v-model='entity.isInEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='只读' name='isReadOnly'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isReadOnlyReadOnly'>{{(($checkboxRange || [])[entity.isReadOnly==null?0:entity.isReadOnly] || {}).text || ''}}</text><template v-if='!isReadOnlyReadOnly'><uni-data-checkbox :disabled='isReadOnlyReadOnly' v-model='entity.isReadOnly' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='width' name='width'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!widthReadOnly' v-model='entity.width' maxlength='50'  />
<text v-if='widthReadOnly'>{{entity.width}}</text>
</uni-forms-item>

<uni-forms-item label='默认值' name='defaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!defaultValueReadOnly' v-model='entity.defaultValue' maxlength='50'  />
<text v-if='defaultValueReadOnly'>{{entity.defaultValue}}</text>
</uni-forms-item>

<uni-forms-item label='匹配类型' name='matchType'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!matchTypeReadOnly' v-model='entity.matchType' maxlength='50'  />
<text v-if='matchTypeReadOnly'>{{entity.matchType}}</text>
</uni-forms-item>

<uni-forms-item label='必填' name='isMust'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isMustReadOnly'>{{(($checkboxRange || [])[entity.isMust==null?0:entity.isMust] || {}).text || ''}}</text><template v-if='!isMustReadOnly'><uni-data-checkbox :disabled='isMustReadOnly' v-model='entity.isMust' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='控件明细' name='controlDetail'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<yzp-control-detail-editor v-if='!controlDetailReadOnly' :entity="entity" :record-id="id" :readonly="controlDetailReadOnly" @receive="receive1676901026591059969"></yzp-control-detail-editor>

</uni-forms-item>

<uni-forms-item label='新建初始值' name='initValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!initValueReadOnly' v-model='entity.initValue' maxlength='50'  />
<text v-if='initValueReadOnly'>{{entity.initValue}}</text>
</uni-forms-item>

<uni-forms-item label='query_default_value' name='queryDefaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!queryDefaultValueReadOnly' v-model='entity.queryDefaultValue' maxlength='50'  />
<text v-if='queryDefaultValueReadOnly'>{{entity.queryDefaultValue}}</text>
</uni-forms-item>

<uni-forms-item label='default_save_value' name='defaultSaveValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!defaultSaveValueReadOnly' v-model='entity.defaultSaveValue' maxlength='50'  />
<text v-if='defaultSaveValueReadOnly'>{{entity.defaultSaveValue}}</text>
</uni-forms-item>

<uni-forms-item label='combine_cell' name='combineCell'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!combineCellReadOnly' v-model='entity.combineCell' maxlength='50'  />
<text v-if='combineCellReadOnly'>{{entity.combineCell}}</text>
</uni-forms-item>

<uni-forms-item label='hint' name='hint'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!hintReadOnly' v-model='entity.hint' maxlength='50'  />
<text v-if='hintReadOnly'>{{entity.hint}}</text>
</uni-forms-item>

<uni-forms-item label='is_print' name='isPrint'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isPrintReadOnly'>{{(($checkboxRange || [])[entity.isPrint==null?0:entity.isPrint] || {}).text || ''}}</text><template v-if='!isPrintReadOnly'><uni-data-checkbox :disabled='isPrintReadOnly' v-model='entity.isPrint' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='筛选框自动焦点' name='isAutoFocus'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoFocusReadOnly'>{{(($checkboxRange || [])[entity.isAutoFocus==null?0:entity.isAutoFocus] || {}).text || ''}}</text><template v-if='!isAutoFocusReadOnly'><uni-data-checkbox :disabled='isAutoFocusReadOnly' v-model='entity.isAutoFocus' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='开发者备注' name='developerRemarks'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!developerRemarksReadOnly' v-model='entity.developerRemarks' maxlength='50'  />
<text v-if='developerRemarksReadOnly'>{{entity.developerRemarks}}</text>
</uni-forms-item>

<uni-forms-item label='填写提示' name='description'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!descriptionReadOnly' v-model='entity.description' maxlength='50'  />
<text v-if='descriptionReadOnly'>{{entity.description}}</text>
</uni-forms-item>

<uni-forms-item label='修改后自动保存' name='isAutoSaveAfterChange'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoSaveAfterChangeReadOnly'>{{(($checkboxRange || [])[entity.isAutoSaveAfterChange==null?0:entity.isAutoSaveAfterChange] || {}).text || ''}}</text><template v-if='!isAutoSaveAfterChangeReadOnly'><uni-data-checkbox :disabled='isAutoSaveAfterChangeReadOnly' v-model='entity.isAutoSaveAfterChange' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='统计类型' name='sumKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='sumKindReadOnly'>{{entity.sumKindShow}}</text><template v-if='!sumKindReadOnly'><uni-data-checkbox :disabled='sumKindReadOnly' v-model='entity.sumKind' :localdata='sumKindRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>


    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>--></view></view>
  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";


export default {

  components: {

  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      yzpMenuId: '',
      page_sn: '6218036784398336',
      parent_page_sn: '',
      formParametersName: 'formParameters6218036784398336',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  false ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,
      config_column_list:[],config_page_list:[],
      sumKindRange:[{"value":0,"text":"不统计"},{"value":1,"text":"合计"},{"value":2,"text":"概率"},{"value":3,"text":"平均数"}],
      entity: {
        name:'',configColumnColumnName:'',configColumnSortNo:'',isImportant:0,configColumnId:'',configPageId:'',isInGrid:'',isInFilter:'',isInEdit:'',isReadOnly:'',width:'',defaultValue:'',matchType:'',isMust:'',controlDetail:'',initValue:'',queryDefaultValue:'',defaultSaveValue:'',combineCell:'',hint:'',isPrint:'',isAutoFocus:'',developerRemarks:'',description:'',isAutoSaveAfterChange:0,sumKind:'',
      },
      entity_column_readonly: {
        name:false,configColumnColumnName:false,configColumnSortNo:false,isImportant:false,configColumnId:false,configPageId:false,isInGrid:false,isInFilter:false,isInEdit:false,isReadOnly:false,width:false,defaultValue:false,matchType:false,isMust:false,controlDetail:false,initValue:false,queryDefaultValue:false,defaultSaveValue:false,combineCell:false,hint:false,isPrint:false,isAutoFocus:false,developerRemarks:false,description:false,isAutoSaveAfterChange:false,sumKind:false,
      },
      entity_rules: {
        name: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},configColumnColumnName: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},configColumnSortNo: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isImportant: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},configColumnId: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},configPageId: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isInGrid: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isInFilter: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isInEdit: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isReadOnly: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},width: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},defaultValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},matchType: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isMust: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},controlDetail: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},initValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},queryDefaultValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},defaultSaveValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},combineCell: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},hint: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isPrint: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isAutoFocus: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},developerRemarks: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},description: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isAutoSaveAfterChange: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},sumKind: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    return false;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},configColumnColumnNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.configColumnColumnName;},configColumnSortNoReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.configColumnSortNo;},isImportantReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isImportant;},configColumnIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.configColumnId;},configPageIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.configPageId;},isInGridReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isInGrid;},isInFilterReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isInFilter;},isInEditReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isInEdit;},isReadOnlyReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isReadOnly;},widthReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.width;},defaultValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.defaultValue;},matchTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.matchType;},isMustReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isMust;},controlDetailReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.controlDetail;},initValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.initValue;},queryDefaultValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.queryDefaultValue;},defaultSaveValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.defaultSaveValue;},combineCellReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.combineCell;},hintReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.hint;},isPrintReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isPrint;},isAutoFocusReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoFocus;},developerRemarksReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.developerRemarks;},descriptionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.description;},isAutoSaveAfterChangeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoSaveAfterChange;},sumKindReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.sumKind;},
    hasVisibleCustomerButtons() {this.permissionVersion;return false;},
  },
  mounted: function () {
    if (this.is_props) {
      var e = {};
      e.id = this.props_id;
      e.parent_id = this.props_parent_id;
      e.parent_page_sn = this.props_parent_page_sn;
      this.initPage(e);
    }
  },
  onLoad: function (e) {
    this.initPage(e);
  },
  methods: {
    // 成功写入只失效关联资源的角标，普通详情读取不产生刷新事件。
    notifyMenuMutation() {
      uni.$emit('yzpMenuChanged', { pageId: '6218036784398336', tableName: 'config_page_column' });
    },
    initPage(e) {
      var that = this;
      // 功能表没有物理业务表，不能把菜单或 URL 参数当作实体 ID 使用。
      this.id = this.page_is_function_table ? '' : e.id;
      this.parent_id = e.parent_id;
      this.parent_page_sn = e.parent_page_sn;
      // 入口标识仅用于导航定位，不参与后端权限判定。
      this.yzpMenuId = e.yzpMenuId || '';

      //全局只读
      if (getApp().globalData.readOnly) {
        this.page_is_update = false
      }

      //初始化成对象，不然会当字符串处理，会报错
      var formParameters = uni.getStorageSync(this.formParametersName) || {};
      formParameters.configPageColumn = {};
      uni.setStorageSync(this.formParametersName, formParameters);

      // 先刷新权限快照，再加载详情和候选项，避免只读判断使用空权限快照。
      this.refreshCurrentPermissions(function() {
        that.initPageAfterPermissions();
      });
    },
    initPageAfterPermissions: function () {
      // 功能表只作为工具表单承载自定义按钮，不查询标准实体详情。
      if (this.page_is_function_table) {
        this.initLists();
        setTimeout(() => this.loadEntitySuccess = true, 300);
      } else if (!utils.isBlank(this.id)) {
        this.getEntity();
      } else {
        this.initLists();
      }
    },
    buildParentIdQuery: function () {
      // 单条接口需要父级范围时才追加 parentId，普通页面和历史 undefined 字符串都不传。
      if (utils.isBlank(this.parent_id) || this.parent_id === 'undefined' || this.parent_id === 'null') {
        return '';
      }
      return '?parentId=' + this.parent_id;
    },
    readonlyDisplayText: function (value, joiner) {
      // 只读态统一展示文本，兼容数组、JSON 数组字符串和普通字符串。
      var separator = joiner || '、';
      if (value === null || value === undefined) {
        return '';
      }
      var normalizedValue = value;
      if (typeof normalizedValue === 'string') {
        var text = normalizedValue.trim();
        if (!text) {
          return '';
        }
        if (text.indexOf('[') === 0 && text.lastIndexOf(']') === text.length - 1) {
          try {
            normalizedValue = JSON.parse(text);
          } catch (e) {
            return normalizedValue;
          }
        } else {
          return normalizedValue;
        }
      }
      if (Array.isArray(normalizedValue)) {
        return normalizedValue.map(function (item) {
          if (item === null || item === undefined) {
            return '';
          }
          if (typeof item === 'object') {
            return item.text || item.name || item.realName || item.value || item.id || '';
          }
          return String(item);
        }).filter(function (item) {
          return item !== '';
        }).join(separator);
      }
      if (typeof normalizedValue === 'object') {
        return normalizedValue.text || normalizedValue.name || normalizedValue.realName || normalizedValue.value || normalizedValue.id || '';
      }
      return String(normalizedValue);
    },
    receive1676901026591059969(newEntity) {    this.entity = newEntity;},showCustomerButtons: function() {var that = this;var actions = [];if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},


    config_columnSelectChange: function(item) {this.entity.configColumnId = item ? item.id : '';this.entity.configColumnName = item ? item.name : '';},config_pageSelectChange: function(item) {this.entity.configPageId = item ? item.id : '';this.entity.configPageName = item ? item.name : '';},
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      yzp.request({    url: '/admin/auto/config_page_column/6218036784398336/findNameIdConfigColumn1676901024774926338',    data: {       formParameters: uni.getStorageSync(that.formParametersName)    },    sendSessionId: true,    method: "POST",    success: function (res) {        if (res.code == 200) {            that.config_column_list = res.data || [];        } else {            uni.showModal({                showCancel: false,                content: res.msg            })        }    },});yzp.request({    url: '/admin/auto/config_page_column/6218036784398336/findNameIdConfigPage1676901024972058625',    data: {       formParameters: uni.getStorageSync(that.formParametersName)    },    sendSessionId: true,    method: "POST",    success: function (res) {        if (res.code == 200) {            that.config_page_list = res.data || [];        } else {            uni.showModal({                showCancel: false,                content: res.msg            })        }    },});
    },
    refreshSonsData: function () {
      // this.getEntity();

    },
    initDynamicLists: function () {
      // 整页只读时不响应联动候选项加载。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;

    },
    emitDetailChange(action, entity, method) {
      // 仅修改保存时携带最新实体；新增、删除等动作继续使用旧字符串事件。
      var payload = action;
      if (action == 'save' && method == 'update' && entity && entity.id) {
        payload = {
          action: action,
          method: method,
          entity: entity
        };
      }
      if (this.is_props) {
        this.$emit('onChange', payload);
        return;
      }
      const openerEventChannel = this.getOpenerEventChannel ? this.getOpenerEventChannel() : null;
      if (openerEventChannel && openerEventChannel.emit) {
        openerEventChannel.emit('onChange', payload);
      }
    },
    closeDetail() {
      if (this.is_props) {
        this.$emit('onPopClose');
        return;
      }
      // 独立路由页关闭时复用全局返回兜底，避免没有历史栈时停留空操作。
      yzp.back();
    },
    getEntity: function () {
      if (this.page_is_function_table) {
        // 功能表没有物理业务表，刷新时只重载候选数据，避免访问标准详情接口。
        this.initLists();
        setTimeout(() => this.loadEntitySuccess = true, 300);
        return;
      }
      var that = this;
      yzp.request({
        url: '/admin/auto/config_page_column/6218036784398336/detail/' + that.id + that.buildParentIdQuery(),
        sendSessionId: true,
        success: function (res) {
          if (res.code == 200) {
            that.entity = res.data;

            that.initLists();
            setTimeout(() => that.loadEntitySuccess = true, 300);
          } else {
            uni.showModal({
              showCancel: false,
              content: res.msg,
              success(modalRes) {
                  if (modalRes.confirm) uni.navigateBack();
              }
            })
          }
        },
      })
    },
    saveEntity: function (successFunction, close) {
      var that = this;
      if (this.page_is_function_table) {
        // 功能表不走标准 add/update，只校验表单后交给自定义按钮继续执行。
        this.$refs.entityForm.validate().then(valid_res => {
          if (successFunction) {
            successFunction({ code: 200, data: that.entity });
          }
        }).catch(err => {

          console.log('表单错误信息：', err);
        })
        return;
      }
      this.$refs.entityForm.validate().then(valid_res => {
        //console.log('表单数据信息：', valid_res);
        var method = 'add';
        if (!utils.isBlank(this.id)) {
          method = 'update';
        }
        that.entity.configPageId = that.parent_id;
        yzp.request({
          url: '/admin/auto/config_page_column/6218036784398336/' + method + that.buildParentIdQuery(),
          data: that.entity,
          sendSessionId: true,
          method: 'POST',
          success: function (res) {
            if (res.code == 200) {
              that.notifyMenuMutation();
              if (successFunction) {
                successFunction(res);
              }
              that.id = res.data.id;
              that.entity = res.data;
              that.emitDetailChange('save', res.data, method);
              if (that.is_props) {
                that.closeDetail();
              } else if (close || (!that.$deviceInfo.is_pc && false)) {
                that.closeDetail();
              } else {
                that.refreshSonsData();
              }
            } else {
              uni.showModal({
                showCancel: false,
                content: res.msg
              })
            }
          },
        })
      }).catch(err => {

        console.log('表单错误信息：', err);
      })
    },
    deleteEntity: function () {
      var that = this;
      uni.showModal({
        title: '确定要删除吗？',
        confirmColor: '#007aff',
        success(modal) {
          if (modal.confirm) {
            yzp.request({
              url: '/admin/auto/config_page_column/6218036784398336' + that.buildParentIdQuery(),
              method: 'DELETE',
              data: that.entity,
              sendSessionId: true,
              success: function (res) {
                if (res.code == 200) {
                  that.notifyMenuMutation();
                  that.emitDetailChange('delete');
                  that.closeDetail();
                } else {
                  uni.showModal({
                    showCancel: false,
                    content: res.msg
                  })
                }
              },
            })
          }
        }
      })
    }
  }
}
</script>
<style></style>