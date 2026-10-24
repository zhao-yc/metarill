<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="页面字段" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="页面字段"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='列名称' name='configColumnColumnName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configColumnColumnNameReadOnly' v-model='entity.configColumnColumnName' maxlength='50'   /><text v-if='configColumnColumnNameReadOnly'>{{entity.configColumnColumnName}}</text>
</uni-forms-item>
<uni-forms-item label='排序号' name='configColumnSortNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configColumnSortNoReadOnly' v-model='entity.configColumnSortNo'  type='number'  /><text v-if='configColumnSortNoReadOnly'>{{entity.configColumnSortNo}}</text>
</uni-forms-item>
<uni-forms-item label='着重显示' name='isImportant'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isImportantReadOnly'>{{(($checkboxRange || [])[entity.isImportant==null?0:entity.isImportant] || {}).text || ''}}</text><template v-if='!isImportantReadOnly'><uni-data-checkbox :disabled='isImportantReadOnly' v-model='entity.isImportant' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='名称' name='name'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'   /><text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>
<uni-forms-item label='列' name='configColumnId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='configColumnIdReadOnly'>{{readonlyDisplayText(entity.configColumnName)}}</text><template v-if='!configColumnIdReadOnly'><zxz-uni-data-select v-model='entity.configColumnId' :localdata='config_column_list' filterable :disabled='configColumnIdReadOnly' dataKey='name' dataValue='id' @change='configColumnIdDetailChange' /></template>
</uni-forms-item>
<uni-forms-item label='视图' name='configPageId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='configPageIdReadOnly'>{{readonlyDisplayText(entity.configPageName)}}</text><template v-if='!configPageIdReadOnly'><zxz-uni-data-select v-model='entity.configPageId' :localdata='config_page_list' filterable :disabled='configPageIdReadOnly' dataKey='name' dataValue='id' @change='configPageIdDetailChange' /></template>
</uni-forms-item>
<uni-forms-item label='is_in_grid' name='isInGrid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isInGridReadOnly'>{{(($checkboxRange || [])[entity.isInGrid==null?0:entity.isInGrid] || {}).text || ''}}</text><template v-if='!isInGridReadOnly'><uni-data-checkbox :disabled='isInGridReadOnly' v-model='entity.isInGrid' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_in_filter' name='isInFilter'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isInFilterReadOnly'>{{(($checkboxRange || [])[entity.isInFilter==null?0:entity.isInFilter] || {}).text || ''}}</text><template v-if='!isInFilterReadOnly'><uni-data-checkbox :disabled='isInFilterReadOnly' v-model='entity.isInFilter' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_in_edit' name='isInEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isInEditReadOnly'>{{(($checkboxRange || [])[entity.isInEdit==null?0:entity.isInEdit] || {}).text || ''}}</text><template v-if='!isInEditReadOnly'><uni-data-checkbox :disabled='isInEditReadOnly' v-model='entity.isInEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='只读' name='isReadOnly'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isReadOnlyReadOnly'>{{(($checkboxRange || [])[entity.isReadOnly==null?0:entity.isReadOnly] || {}).text || ''}}</text><template v-if='!isReadOnlyReadOnly'><uni-data-checkbox :disabled='isReadOnlyReadOnly' v-model='entity.isReadOnly' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='width' name='width'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!widthReadOnly' v-model='entity.width' maxlength='50'   /><text v-if='widthReadOnly'>{{entity.width}}</text>
</uni-forms-item>
<uni-forms-item label='默认值' name='defaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!defaultValueReadOnly' v-model='entity.defaultValue' maxlength='50'   /><text v-if='defaultValueReadOnly'>{{entity.defaultValue}}</text>
</uni-forms-item>
<uni-forms-item label='匹配类型' name='matchType'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!matchTypeReadOnly' v-model='entity.matchType' maxlength='50'   /><text v-if='matchTypeReadOnly'>{{entity.matchType}}</text>
</uni-forms-item>
<uni-forms-item label='必填' name='isMust'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isMustReadOnly'>{{(($checkboxRange || [])[entity.isMust==null?0:entity.isMust] || {}).text || ''}}</text><template v-if='!isMustReadOnly'><uni-data-checkbox :disabled='isMustReadOnly' v-model='entity.isMust' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='控件明细' name='controlDetail'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<yzp-control-detail-editor v-if='!controlDetailReadOnly' :entity="entity" :record-id="id" :readonly="controlDetailReadOnly" @receive="receive1676901026591059969" />
</uni-forms-item>
<uni-forms-item label='新建初始值' name='initValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!initValueReadOnly' v-model='entity.initValue' maxlength='50'   /><text v-if='initValueReadOnly'>{{entity.initValue}}</text>
</uni-forms-item>
<uni-forms-item label='query_default_value' name='queryDefaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!queryDefaultValueReadOnly' v-model='entity.queryDefaultValue' maxlength='50'   /><text v-if='queryDefaultValueReadOnly'>{{entity.queryDefaultValue}}</text>
</uni-forms-item>
<uni-forms-item label='default_save_value' name='defaultSaveValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!defaultSaveValueReadOnly' v-model='entity.defaultSaveValue' maxlength='50'   /><text v-if='defaultSaveValueReadOnly'>{{entity.defaultSaveValue}}</text>
</uni-forms-item>
<uni-forms-item label='combine_cell' name='combineCell'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!combineCellReadOnly' v-model='entity.combineCell' maxlength='50'   /><text v-if='combineCellReadOnly'>{{entity.combineCell}}</text>
</uni-forms-item>
<uni-forms-item label='hint' name='hint'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!hintReadOnly' v-model='entity.hint' maxlength='50'   /><text v-if='hintReadOnly'>{{entity.hint}}</text>
</uni-forms-item>
<uni-forms-item label='is_print' name='isPrint'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isPrintReadOnly'>{{(($checkboxRange || [])[entity.isPrint==null?0:entity.isPrint] || {}).text || ''}}</text><template v-if='!isPrintReadOnly'><uni-data-checkbox :disabled='isPrintReadOnly' v-model='entity.isPrint' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='筛选框自动焦点' name='isAutoFocus'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAutoFocusReadOnly'>{{(($checkboxRange || [])[entity.isAutoFocus==null?0:entity.isAutoFocus] || {}).text || ''}}</text><template v-if='!isAutoFocusReadOnly'><uni-data-checkbox :disabled='isAutoFocusReadOnly' v-model='entity.isAutoFocus' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='开发者备注' name='developerRemarks'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!developerRemarksReadOnly' v-model='entity.developerRemarks' maxlength='50'   /><text v-if='developerRemarksReadOnly'>{{entity.developerRemarks}}</text>
</uni-forms-item>
<uni-forms-item label='填写提示' name='description'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!descriptionReadOnly' v-model='entity.description' maxlength='50'   /><text v-if='descriptionReadOnly'>{{entity.description}}</text>
</uni-forms-item>
<uni-forms-item label='修改后自动保存' name='isAutoSaveAfterChange'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAutoSaveAfterChangeReadOnly'>{{(($checkboxRange || [])[entity.isAutoSaveAfterChange==null?0:entity.isAutoSaveAfterChange] || {}).text || ''}}</text><template v-if='!isAutoSaveAfterChangeReadOnly'><uni-data-checkbox :disabled='isAutoSaveAfterChangeReadOnly' v-model='entity.isAutoSaveAfterChange' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='config_column_name' name='configColumnName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configColumnNameReadOnly' v-model='entity.configColumnName' maxlength='50'   /><text v-if='configColumnNameReadOnly'>{{entity.configColumnName}}</text>
</uni-forms-item>
<uni-forms-item label='统计类型' name='sumKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='sumKindReadOnly'>{{entity.sumKindShow}}</text><template v-if='!sumKindReadOnly'><uni-data-checkbox :disabled='sumKindReadOnly' v-model='entity.sumKind' :localdata='sumKindRange' /></template>
</uni-forms-item>
<uni-forms-item label='config_page_name' name='configPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configPageNameReadOnly' v-model='entity.configPageName' maxlength='50'   /><text v-if='configPageNameReadOnly'>{{entity.configPageName}}</text>
</uni-forms-item>
<uni-forms-item label='link_config_table_id' name='linkConfigTableId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!linkConfigTableIdReadOnly' v-model='entity.linkConfigTableId' maxlength='50'   /><text v-if='linkConfigTableIdReadOnly'>{{entity.linkConfigTableId}}</text>
</uni-forms-item>
<uni-forms-item label='link_config_table_name' name='linkConfigTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!linkConfigTableNameReadOnly' v-model='entity.linkConfigTableName' maxlength='50'   /><text v-if='linkConfigTableNameReadOnly'>{{entity.linkConfigTableName}}</text>
</uni-forms-item>
<uni-forms-item label='link_config_page_id' name='linkConfigPageId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!linkConfigPageIdReadOnly' v-model='entity.linkConfigPageId' maxlength='50'   /><text v-if='linkConfigPageIdReadOnly'>{{entity.linkConfigPageId}}</text>
</uni-forms-item>
<uni-forms-item label='link_config_page_name' name='linkConfigPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!linkConfigPageNameReadOnly' v-model='entity.linkConfigPageName' maxlength='50'   /><text v-if='linkConfigPageNameReadOnly'>{{entity.linkConfigPageName}}</text>
</uni-forms-item>
<uni-forms-item label='combine_super_column_id' name='combineSuperColumnId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!combineSuperColumnIdReadOnly' v-model='entity.combineSuperColumnId' maxlength='50'   /><text v-if='combineSuperColumnIdReadOnly'>{{entity.combineSuperColumnId}}</text>
</uni-forms-item>
<uni-forms-item label='combine_super_column_name' name='combineSuperColumnName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!combineSuperColumnNameReadOnly' v-model='entity.combineSuperColumnName' maxlength='50'   /><text v-if='combineSuperColumnNameReadOnly'>{{entity.combineSuperColumnName}}</text>
</uni-forms-item>
<uni-forms-item label='import_no' name='importNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importNoReadOnly' v-model='entity.importNo' maxlength='45'   /><text v-if='importNoReadOnly'>{{entity.importNo}}</text>
</uni-forms-item>
<uni-forms-item label='项目' name='yzProjectId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectIdReadOnly' v-model='entity.yzProjectId'    /><text v-if='yzProjectIdReadOnly'>{{entity.yzProjectId}}</text>
</uni-forms-item>

        </uni-forms>
      </view>
      
    </view>
    
    
    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button></view></view>
  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";

const originalRecordId=value=>typeof value==='string' && !['null','undefined'].includes(value.trim())?value.trim():'';

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
      config_column_list:[],config_page_list:[],sumKindRange:[{"text":"\u4E0D\u7EDF\u8BA1","value":0},{"text":"\u5408\u8BA1","value":1},{"text":"\u6982\u7387","value":2},{"text":"\u5E73\u5747\u6570","value":3}],originalButtons:[],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:false,page_is_copy:false,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        configColumnColumnName:"",configColumnSortNo:"",isImportant:0,name:"",configColumnId:"",configPageId:"",isInGrid:"",isInFilter:"",isInEdit:"",isReadOnly:"",width:"",defaultValue:"",matchType:"",isMust:"",controlDetail:"",initValue:"",queryDefaultValue:"",defaultSaveValue:"",combineCell:"",hint:"",isPrint:"",isAutoFocus:"",developerRemarks:"",description:"",isAutoSaveAfterChange:0,configColumnName:"",sumKind:"",configPageName:"",linkConfigTableId:"",linkConfigTableName:"",linkConfigPageId:"",linkConfigPageName:"",combineSuperColumnId:"",combineSuperColumnName:"",importNo:"",yzProjectId:"",
      },
      entity_column_readonly: {
        configColumnColumnName:false,configColumnSortNo:false,isImportant:false,name:false,configColumnId:false,configPageId:false,isInGrid:false,isInFilter:false,isInEdit:false,isReadOnly:false,width:false,defaultValue:false,matchType:false,isMust:false,controlDetail:false,initValue:false,queryDefaultValue:false,defaultSaveValue:false,combineCell:false,hint:false,isPrint:false,isAutoFocus:false,developerRemarks:false,description:false,isAutoSaveAfterChange:false,configColumnName:false,sumKind:false,configPageName:false,linkConfigTableId:false,linkConfigTableName:false,linkConfigPageId:false,linkConfigPageName:false,combineSuperColumnId:false,combineSuperColumnName:false,importNo:false,yzProjectId:true,
      },
      entity_rules: {
        configColumnColumnName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},configColumnSortNo:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isImportant:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},name:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},configColumnId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},configPageId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isInGrid:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isInFilter:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isInEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isReadOnly:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},width:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},defaultValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},matchType:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isMust:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},controlDetail:{rules:[{"maxLength":500,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},initValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},queryDefaultValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},defaultSaveValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},combineCell:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},hint:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isPrint:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isAutoFocus:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},developerRemarks:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},description:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAutoSaveAfterChange:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},configColumnName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},sumKind:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},configPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},linkConfigTableId:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},linkConfigTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},linkConfigPageId:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},linkConfigPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},combineSuperColumnId:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},combineSuperColumnName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},importNo:{rules:[{"maxLength":45,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzProjectId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    configColumnColumnNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configColumnColumnName; },
configColumnSortNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configColumnSortNo; },
isImportantReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isImportant; },
nameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.name; },
configColumnIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configColumnId; },
configPageIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configPageId; },
isInGridReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isInGrid; },
isInFilterReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isInFilter; },
isInEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isInEdit; },
isReadOnlyReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isReadOnly; },
widthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.width; },
defaultValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.defaultValue; },
matchTypeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.matchType; },
isMustReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isMust; },
controlDetailReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.controlDetail; },
initValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.initValue; },
queryDefaultValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.queryDefaultValue; },
defaultSaveValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.defaultSaveValue; },
combineCellReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.combineCell; },
hintReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.hint; },
isPrintReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isPrint; },
isAutoFocusReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoFocus; },
developerRemarksReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.developerRemarks; },
descriptionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.description; },
isAutoSaveAfterChangeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoSaveAfterChange; },
configColumnNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configColumnName; },
sumKindReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sumKind; },
configPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configPageName; },
linkConfigTableIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.linkConfigTableId; },
linkConfigTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.linkConfigTableName; },
linkConfigPageIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.linkConfigPageId; },
linkConfigPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.linkConfigPageName; },
combineSuperColumnIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.combineSuperColumnId; },
combineSuperColumnNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.combineSuperColumnName; },
importNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importNo; },
yzProjectIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectId; },
hasVisibleCustomerButtons() { this.permissionVersion;return this.originalButtons.some(button=>this['customerButtonShow'+button.id]); },
parentId() { return this.parent_id || ''; },
originalChildrenReady() { return this.recordReady && this.permissionsReady && Boolean(this.id) && originalRecordId(this.entity.id)===this.id; },
readOnlyCondition() { this.permissionVersion;return !this.permissionsReady || Boolean(this.id && !this.recordReady) || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.page_is_function_table ? !this.page_is_update : this.id ? !this.page_is_update : !this.page_is_new)) || !yzp.hasPagePermission(this.page_sn,this.page_is_function_table || this.id ? 'is_update' : 'is_new'); },

    
  },
  created() {
    // 独立范围观察不覆盖原AutoPostBack/AutoSave字段watcher，也不改变控件布局。
    
  },
  onUnload() { this.disposeOriginalDetail(); },
  beforeUnmount() { this.disposeOriginalDetail(); },
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
    initPage(e = {}) {
      this.beginOriginalDetail(e);
      var that = this;
      // 功能表没有物理业务表，不能把菜单或 URL 参数当作实体 ID 使用。
      this.id = this.page_is_function_table ? '' : originalRecordId(e.id);
      this.parent_id = originalRecordId(e.parent_id || e.parentId);
      this.parent_page_sn = originalRecordId(e.parent_page_sn || e.parentPageId);
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
      const current = this.originalContext();
      this.refreshCurrentPermissions(function() {
        if (!current()) return;
        that.permissionsReady = true;
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
    /** 页面/弹层进入使用同一实例边界，旧回调不能写到下一条记录。 */
beginOriginalDetail(options) {
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={configColumnColumnName:"",configColumnSortNo:"",isImportant:0,name:"",configColumnId:"",configPageId:"",isInGrid:"",isInFilter:"",isInEdit:"",isReadOnly:"",width:"",defaultValue:"",matchType:"",isMust:"",controlDetail:"",initValue:"",queryDefaultValue:"",defaultSaveValue:"",combineCell:"",hint:"",isPrint:"",isAutoFocus:"",developerRemarks:"",description:"",isAutoSaveAfterChange:0,configColumnName:"",sumKind:"",configPageName:"",linkConfigTableId:"",linkConfigTableName:"",linkConfigPageId:"",linkConfigPageName:"",combineSuperColumnId:"",combineSuperColumnName:"",importNo:"",yzProjectId:"",};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
},
/** 卸载只取消本详情的任务与订阅，不触发保存或父表刷新。 */
disposeOriginalDetail() {this._alive=false;this._detailEpoch=(this._detailEpoch||0)+1;(this._detailTasks||[]).forEach(task=>task?.abort?.());this._detailTasks=[];this.disposeMultiSelections?.();},
/** 当前身份用于迟到隔离，不能替代后端真实授权。 */
currentIdentity() {return JSON.stringify([uni.getStorageSync('loginStateId')||'',uni.getStorageSync('sysParams')||{}]);},
/** 原props及URL始终使用字符串ID，父/项目切换即使watcher未执行也失效。 */
originalContext() {const epoch=this._detailEpoch,id=this.id,parent=this.parent_id,identity=this.currentIdentity();return ()=>Boolean(this._alive&&epoch===this._detailEpoch&&id===this.id&&parent===this.parent_id&&identity===this.currentIdentity());},
/** 不裁剪、不补null；原整entity由固定页C#归一器兼容处理。 */
originalRequest(options) {
 const current=this.originalContext(),epoch=this._detailEpoch,parent=this.parent_id,identity=this.currentIdentity(),method=String(options.method||'GET').toUpperCase(),route=String(options.url||'').split('?')[0];
 const mutation=route==="/admin/auto/config_page_column/6218036784398336"&&method==='DELETE'||method==='POST'&&["/admin/auto/config_page_column/6218036784398336"+'/add',"/admin/auto/config_page_column/6218036784398336"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/config_page_column/6218036784398336"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/config_page_column/6218036784398336"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["isImportant","name","configColumnId","configPageId","isInGrid","isInFilter","isInEdit","isReadOnly","width","defaultValue","matchType","isMust","controlDetail","initValue","queryDefaultValue","defaultSaveValue","combineCell","hint","isPrint","isAutoFocus","developerRemarks","description","isAutoSaveAfterChange","configColumnName","sumKind","configPageName","linkConfigTableId","linkConfigTableName","linkConfigPageId","linkConfigPageName","combineSuperColumnId","combineSuperColumnName","importNo"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/config_page_column/6218036784398336"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
/** 按原Save/NotSave、确认、固定接口或页面动作执行，不接受调用方改写动作。 */
runOriginalButton(id) {
 const button=this.originalButtons.find(item=>item.id===id);if(!button||!this['customerButtonShow'+id])return;const current=this.originalContext(),that=this;
 const execute=()=>{that.permissionVersion++;if(!current()||!that['customerButtonShow'+id])return;const action=()=>{
  if(button.action===1)that.originalRequest({url:'/admin/sys/config_button/execute',method:'POST',data:{buttonId:id,entityId:that.id,entity:that.entity},sendSessionId:true,success(res){if(res.code===200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,button.close,'detail');}});
  else if(button.action===2){const handler=project['customerButtonHandler'+id];if(typeof handler!=='function')throw new Error('项目未提供原按钮扩展：'+id);uni.navigateTo({url:'/'+button.target,success(){if(that._alive)handler(that.entity);}});}
  else if(button.action===3){if(typeof window==='undefined')throw new Error('原新窗口按钮只支持Web端。');window.open(location.href.split('pages')[0]+button.target+'?id='+that.id,'_blank');}
 };if(button.save==='NotSave')action();else that.saveEntity(()=>{if(that._alive&&that.currentIdentity()===identity)action();});};
 const identity=this.currentIdentity();if(button.confirm)uni.showModal({content:button.confirmMsg||'您确定'+button.name+'吗？',success(modal){if(modal.confirm)execute();}});else execute();
},

    loadOriginalOptions1676901024774926338() {const that=this;that.config_column_list=[];(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["1676901024774926338"]||0)+1;that._originalOptions["1676901024774926338"]=optionSequence;const optionParent=null;that.originalRequest({url:"/admin/auto/config_page_column/6218036784398336/findNameIdConfigColumn1676901024774926338",isCurrent:()=>optionSequence===that._originalOptions["1676901024774926338"]&&true,data:{parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName),...(that.readOnlyCondition?{id:that.entity.configColumnId}:{})},method:'POST',sendSessionId:true,success(res){if(res.code===200){that.config_column_list=res.data||[];}else uni.showModal({showCancel:false,content:res.msg});}});})();},
configColumnIdDetailChange(item) { if(this.configColumnIdReadOnly) return; this.entity.configColumnId=item ? item.id : ''; this.entity.configColumnName=item ? item.name : ''; },
loadOriginalOptions1676901024972058625() {const that=this;that.config_page_list=[];(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["1676901024972058625"]||0)+1;that._originalOptions["1676901024972058625"]=optionSequence;const optionParent=null;that.originalRequest({url:"/admin/auto/config_page_column/6218036784398336/findNameIdConfigPage1676901024972058625",isCurrent:()=>optionSequence===that._originalOptions["1676901024972058625"]&&true,data:{parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName),...(that.readOnlyCondition?{id:that.entity.configPageId}:{})},method:'POST',sendSessionId:true,success(res){if(res.code===200){that.config_page_list=res.data||[];}else uni.showModal({showCancel:false,content:res.msg});}});})();},
configPageIdDetailChange(item) { if(this.configPageIdReadOnly) return; this.entity.configPageId=item ? item.id : ''; this.entity.configPageName=item ? item.name : ''; },
receive1676901026591059969(newEntity) { if(this.controlDetailReadOnly) return; this.entity=newEntity; },
showCustomerButtons() { const current=this.originalContext(),actions=this.originalButtons.filter(button=>this['customerButtonShow'+button.id]);if(!actions.length){uni.showToast({title:'暂无可用操作',icon:'none'});return;}uni.showActionSheet({itemList:actions.map(button=>button.name),success:res=>{if(current()&&actions[res.tapIndex])this['customerButtonHandler'+actions[res.tapIndex].id]();}}); },

    
    
    
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      that.loadOriginalOptions1676901024774926338();
that.loadOriginalOptions1676901024972058625();

    },
    refreshSonsData: function () {
      // this.getEntity();
      if (!this.originalChildrenReady) return;
      
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
      that.originalRequest({
        url: '/admin/auto/config_page_column/6218036784398336/detail/' + that.id + that.buildParentIdQuery(),
        sendSessionId: true,
        success: function (res) {
          if (res.code == 200) {
            // 既有记录必须由当前请求回读同一字符串ID，错误VO不能成为下一次整entity写入对象。
            if (!res.data || Array.isArray(res.data) || originalRecordId(res.data.id) !== that.id) {
              that.recordReady = false;
              that.loadEntitySuccess = false;
              uni.showModal({ showCancel: false, content: '详情返回记录与当前页面不一致，请刷新后重试' });
              return;
            }
            that.entity = res.data;
            
            that.markOriginalRecordReady();
            that.initLists();
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
      this.permissionVersion++;
      if (!this._alive || this.saving || this.readOnlyCondition || (!this.page_is_function_table && (this.id ? !this.page_is_save : !this.page_is_new))) return;
      var that = this;
      if (this.page_is_function_table) {
        // 功能表不走标准 add/update，只校验表单后交给自定义按钮继续执行。
        const validationCurrent = this.originalContext();
      this.$refs.entityForm.validate().then(valid_res => {
        if (!validationCurrent() || this.readOnlyCondition || this.saving) return;
          if (successFunction) {
            successFunction({ code: 200, data: that.entity });
          }
        }).catch(err => {
          
          console.log('表单错误信息：', err);
        })
        return;
      }
      const validationCurrent = this.originalContext();
      this.$refs.entityForm.validate().then(valid_res => {
        if (!validationCurrent() || this.readOnlyCondition || this.saving) return;
        //console.log('表单数据信息：', valid_res);
        var method = 'add';
        if (!utils.isBlank(this.id)) {
          method = 'update';
        }
        that.entity.configPageId=that.parent_id;
        that.originalRequest({
          url: '/admin/auto/config_page_column/6218036784398336/' + method + that.buildParentIdQuery(),
          data: that.entity,
          sendSessionId: true,
          method: 'POST',
          success: function (res) {
            if (res.code == 200) {
              if (!res.data || !originalRecordId(res.data.id) || method === 'update' && originalRecordId(res.data.id) !== that.id) {
                that.recordReady = false;
                uni.showModal({ showCancel: false, content: '保存返回记录与当前页面不一致，请重新读取后确认' });
                return;
              }
              that.notifyMenuMutation();
              that.id = originalRecordId(res.data.id);
              that.entity = res.data;
              that.markOriginalRecordReady();
              // 新建后先确认真实记录ID，再继续原“先保存再执行”按钮动作。
              if (successFunction) {
                successFunction(res);
              }
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
      if (!this._alive || this.saving || this.readOnlyCondition || !this.page_is_delete || !yzp.hasPagePermission(this.page_sn, 'is_delete')) return;
      const current = this.originalContext();
      var that = this;
      uni.showModal({
        title: '确定要删除吗？',
        confirmColor: '#0ab3ad',
        success(modal) {
          if (modal.confirm && current() && !that.readOnlyCondition) {
            that.originalRequest({
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
