<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="选择配置页面" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="选择配置页面"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'   /><text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>
<uni-forms-item label='表名' name='configTableTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configTableTableNameReadOnly' v-model='entity.configTableTableName' maxlength='50'   /><text v-if='configTableTableNameReadOnly'>{{entity.configTableTableName}}</text>
</uni-forms-item>
<uni-forms-item label='order_by' name='orderBy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!orderByReadOnly' v-model='entity.orderBy' maxlength='500'   /><text v-if='orderByReadOnly'>{{entity.orderBy}}</text>
</uni-forms-item>
<uni-forms-item label='Where' name='whereSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" :disabled='whereSqlReadOnly' type='textarea' maxlength='-1' v-model='entity.whereSql' />
</uni-forms-item>
<uni-forms-item label='精确数据管理匹配字段' name='exactDataManageMatchField'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!exactDataManageMatchFieldReadOnly' v-model='entity.exactDataManageMatchField' maxlength='50'   /><text v-if='exactDataManageMatchFieldReadOnly'>{{entity.exactDataManageMatchField}}</text>
<view class='input-tips'>用于精确数据管理时，当前视图参与匹配的数据字段名</view>
</uni-forms-item>
<uni-forms-item label='page_size' name='pageSize'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!pageSizeReadOnly' v-model='entity.pageSize'  type='number'  /><text v-if='pageSizeReadOnly'>{{entity.pageSize}}</text>
</uni-forms-item>
<uni-forms-item label='is_edit' name='isEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isEditReadOnly'>{{(($checkboxRange || [])[entity.isEdit==null?0:entity.isEdit] || {}).text || ''}}</text><template v-if='!isEditReadOnly'><uni-data-checkbox :disabled='isEditReadOnly' v-model='entity.isEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_new' name='isNew' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isNewReadOnly'>{{(($checkboxRange || [])[entity.isNew==null?0:entity.isNew] || {}).text || ''}}</text><template v-if='!isNewReadOnly'><uni-data-checkbox :disabled='isNewReadOnly' v-model='entity.isNew' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_update' name='isUpdate' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isUpdateReadOnly'>{{(($checkboxRange || [])[entity.isUpdate==null?0:entity.isUpdate] || {}).text || ''}}</text><template v-if='!isUpdateReadOnly'><uni-data-checkbox :disabled='isUpdateReadOnly' v-model='entity.isUpdate' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_delete' name='isDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isDeleteReadOnly'>{{(($checkboxRange || [])[entity.isDelete==null?0:entity.isDelete] || {}).text || ''}}</text><template v-if='!isDeleteReadOnly'><uni-data-checkbox :disabled='isDeleteReadOnly' v-model='entity.isDelete' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_search' name='isSearch'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isSearchReadOnly'>{{(($checkboxRange || [])[entity.isSearch==null?0:entity.isSearch] || {}).text || ''}}</text><template v-if='!isSearchReadOnly'><uni-data-checkbox :disabled='isSearchReadOnly' v-model='entity.isSearch' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_sum' name='isSum'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isSumReadOnly'>{{(($checkboxRange || [])[entity.isSum==null?0:entity.isSum] || {}).text || ''}}</text><template v-if='!isSumReadOnly'><uni-data-checkbox :disabled='isSumReadOnly' v-model='entity.isSum' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_batch_delete' name='isBatchDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isBatchDeleteReadOnly'>{{(($checkboxRange || [])[entity.isBatchDelete==null?0:entity.isBatchDelete] || {}).text || ''}}</text><template v-if='!isBatchDeleteReadOnly'><uni-data-checkbox :disabled='isBatchDeleteReadOnly' v-model='entity.isBatchDelete' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_import' name='isImport'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isImportReadOnly'>{{(($checkboxRange || [])[entity.isImport==null?0:entity.isImport] || {}).text || ''}}</text><template v-if='!isImportReadOnly'><uni-data-checkbox :disabled='isImportReadOnly' v-model='entity.isImport' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_export' name='isExport'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isExportReadOnly'>{{(($checkboxRange || [])[entity.isExport==null?0:entity.isExport] || {}).text || ''}}</text><template v-if='!isExportReadOnly'><uni-data-checkbox :disabled='isExportReadOnly' v-model='entity.isExport' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_multi_select' name='isMultiSelect'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isMultiSelectReadOnly'>{{(($checkboxRange || [])[entity.isMultiSelect==null?0:entity.isMultiSelect] || {}).text || ''}}</text><template v-if='!isMultiSelectReadOnly'><uni-data-checkbox :disabled='isMultiSelectReadOnly' v-model='entity.isMultiSelect' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_export_edit' name='isExportEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isExportEditReadOnly'>{{(($checkboxRange || [])[entity.isExportEdit==null?0:entity.isExportEdit] || {}).text || ''}}</text><template v-if='!isExportEditReadOnly'><uni-data-checkbox :disabled='isExportEditReadOnly' v-model='entity.isExportEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='export_method' name='exportMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!exportMethodReadOnly' v-model='entity.exportMethod' maxlength='50'   /><text v-if='exportMethodReadOnly'>{{entity.exportMethod}}</text>
</uni-forms-item>
<uni-forms-item label='复制方法' name='isCopy' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isCopyReadOnly'>{{(($checkboxRange || [])[entity.isCopy==null?0:entity.isCopy] || {}).text || ''}}</text><template v-if='!isCopyReadOnly'><uni-data-checkbox :disabled='isCopyReadOnly' v-model='entity.isCopy' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='read_only_condition' name='readOnlyCondition'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!readOnlyConditionReadOnly' v-model='entity.readOnlyCondition' maxlength='50'   /><text v-if='readOnlyConditionReadOnly'>{{entity.readOnlyCondition}}</text>
</uni-forms-item>
<uni-forms-item label='import_method' name='importMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importMethodReadOnly' v-model='entity.importMethod' maxlength='50'   /><text v-if='importMethodReadOnly'>{{entity.importMethod}}</text>
</uni-forms-item>
<uni-forms-item label='button_visible' name='buttonVisible'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!buttonVisibleReadOnly' v-model='entity.buttonVisible' maxlength='50'   /><text v-if='buttonVisibleReadOnly'>{{entity.buttonVisible}}</text>
</uni-forms-item>
<uni-forms-item label='is_after_add' name='isAfterAdd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAfterAddReadOnly'>{{(($checkboxRange || [])[entity.isAfterAdd==null?0:entity.isAfterAdd] || {}).text || ''}}</text><template v-if='!isAfterAddReadOnly'><uni-data-checkbox :disabled='isAfterAddReadOnly' v-model='entity.isAfterAdd' :localdata='$checkboxRange' /></template>
<view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>
<uni-forms-item label='is_after_save' name='isAfterSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAfterSaveReadOnly'>{{(($checkboxRange || [])[entity.isAfterSave==null?0:entity.isAfterSave] || {}).text || ''}}</text><template v-if='!isAfterSaveReadOnly'><uni-data-checkbox :disabled='isAfterSaveReadOnly' v-model='entity.isAfterSave' :localdata='$checkboxRange' /></template>
<view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>
<uni-forms-item label='is_before_save' name='isBeforeSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isBeforeSaveReadOnly'>{{(($checkboxRange || [])[entity.isBeforeSave==null?0:entity.isBeforeSave] || {}).text || ''}}</text><template v-if='!isBeforeSaveReadOnly'><uni-data-checkbox :disabled='isBeforeSaveReadOnly' v-model='entity.isBeforeSave' :localdata='$checkboxRange' /></template>
<view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>
<uni-forms-item label='子模块位置' name='childPlace'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='childPlaceReadOnly'>{{entity.childPlaceShow}}</text><template v-if='!childPlaceReadOnly'><uni-data-checkbox :disabled='childPlaceReadOnly' v-model='entity.childPlace' :localdata='childPlaceRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_after_delete' name='isAfterDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAfterDeleteReadOnly'>{{(($checkboxRange || [])[entity.isAfterDelete==null?0:entity.isAfterDelete] || {}).text || ''}}</text><template v-if='!isAfterDeleteReadOnly'><uni-data-checkbox :disabled='isAfterDeleteReadOnly' v-model='entity.isAfterDelete' :localdata='$checkboxRange' /></template>
<view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>
<uni-forms-item label='is_repeat_pwd' name='isRepeatPwd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isRepeatPwdReadOnly'>{{(($checkboxRange || [])[entity.isRepeatPwd==null?0:entity.isRepeatPwd] || {}).text || ''}}</text><template v-if='!isRepeatPwdReadOnly'><uni-data-checkbox :disabled='isRepeatPwdReadOnly' v-model='entity.isRepeatPwd' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='save_auto_close' name='saveAutoClose'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!saveAutoCloseReadOnly' v-model='entity.saveAutoClose' maxlength='50'   /><text v-if='saveAutoCloseReadOnly'>{{entity.saveAutoClose}}</text>
</uni-forms-item>
<uni-forms-item label='child_style' name='childStyle'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!childStyleReadOnly' v-model='entity.childStyle' maxlength='50'   /><text v-if='childStyleReadOnly'>{{entity.childStyle}}</text>
</uni-forms-item>
<uni-forms-item label='is_before_delete' name='isBeforeDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isBeforeDeleteReadOnly'>{{(($checkboxRange || [])[entity.isBeforeDelete==null?0:entity.isBeforeDelete] || {}).text || ''}}</text><template v-if='!isBeforeDeleteReadOnly'><uni-data-checkbox :disabled='isBeforeDeleteReadOnly' v-model='entity.isBeforeDelete' :localdata='$checkboxRange' /></template>
<view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>
<uni-forms-item label='config_table_name' name='configTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configTableNameReadOnly' v-model='entity.configTableName' maxlength='50'   /><text v-if='configTableNameReadOnly'>{{entity.configTableName}}</text>
</uni-forms-item>
<uni-forms-item label='自动隐藏' name='isAutoHide'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAutoHideReadOnly'>{{(($checkboxRange || [])[entity.isAutoHide==null?0:entity.isAutoHide] || {}).text || ''}}</text><template v-if='!isAutoHideReadOnly'><uni-data-checkbox :disabled='isAutoHideReadOnly' v-model='entity.isAutoHide' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='列表页编辑' name='isListEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isListEditReadOnly'>{{(($checkboxRange || [])[entity.isListEdit==null?0:entity.isListEdit] || {}).text || ''}}</text><template v-if='!isListEditReadOnly'><uni-data-checkbox :disabled='isListEditReadOnly' v-model='entity.isListEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='parent_config_table_id' name='parentConfigTableId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!parentConfigTableIdReadOnly' v-model='entity.parentConfigTableId'    /><text v-if='parentConfigTableIdReadOnly'>{{entity.parentConfigTableId}}</text>
</uni-forms-item>
<uni-forms-item label='parent_config_table_name' name='parentConfigTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!parentConfigTableNameReadOnly' v-model='entity.parentConfigTableName' maxlength='50'   /><text v-if='parentConfigTableNameReadOnly'>{{entity.parentConfigTableName}}</text>
</uni-forms-item>
<uni-forms-item label='页面类型' name='type'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='typeReadOnly'>{{entity.typeShow}}</text><template v-if='!typeReadOnly'><uni-data-checkbox :disabled='typeReadOnly' v-model='entity.type' :localdata='typeRange' /></template>
</uni-forms-item>
<uni-forms-item label='parent_config_page_id' name='parentConfigPageId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!parentConfigPageIdReadOnly' v-model='entity.parentConfigPageId'    /><text v-if='parentConfigPageIdReadOnly'>{{entity.parentConfigPageId}}</text>
</uni-forms-item>
<uni-forms-item label='parent_config_page_name' name='parentConfigPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!parentConfigPageNameReadOnly' v-model='entity.parentConfigPageName' maxlength='50'   /><text v-if='parentConfigPageNameReadOnly'>{{entity.parentConfigPageName}}</text>
</uni-forms-item>
<uni-forms-item label='yz_project_name' name='yzProjectName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectNameReadOnly' v-model='entity.yzProjectName' maxlength='50'   /><text v-if='yzProjectNameReadOnly'>{{entity.yzProjectName}}</text>
</uni-forms-item>
<uni-forms-item label='import_no' name='importNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importNoReadOnly' v-model='entity.importNo' maxlength='45'   /><text v-if='importNoReadOnly'>{{entity.importNo}}</text>
</uni-forms-item>
<uni-forms-item label='所属后台' name='yzAdminTypeId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='yzAdminTypeIdReadOnly'>{{readonlyDisplayText(entity.yzAdminTypeId)}}</text><template v-if='!yzAdminTypeIdReadOnly'><zxz-uni-data-select v-model='entity.yzAdminTypeId' :localdata='yz_admin_type_list' filterable :disabled='yzAdminTypeIdReadOnly' dataKey='name' dataValue='id' @change='yzAdminTypeIdDetailChange' /></template>
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
      page_sn: '1869016706289577986',
      parent_page_sn: '',
      formParametersName: 'formParameters1869016706289577986',
      loadEntitySuccess: false,
      childPlaceRange:[{"text":"\u4E00\u62D6N","value":"OneWithMany"},{"text":"\u6807\u7B7E\u663E\u793A","value":"Tab"},{"text":"\u65B0\u9875\u9762\u6253\u5F00","value":"NewPage"}],typeRange:[{"text":"\u9ED8\u8BA4","value":0},{"text":"\u9009\u62E9\u9875","value":1},{"text":"\u591A\u9009\u9875","value":2}],yz_admin_type_list:[],originalButtons:[],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:false,page_is_copy:false,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        name:"",configTableTableName:"",orderBy:"",whereSql:"",exactDataManageMatchField:"",pageSize:"",isEdit:"",isNew:"",isUpdate:"",isDelete:"",isSearch:"",isSum:"",isBatchDelete:"",isImport:"",isExport:"",isMultiSelect:"",isExportEdit:"",exportMethod:"",isCopy:"",readOnlyCondition:"",importMethod:"",buttonVisible:"",isAfterAdd:"",isAfterSave:"",isBeforeSave:"",childPlace:"",isAfterDelete:"",isRepeatPwd:"",saveAutoClose:"",childStyle:"",isBeforeDelete:"",configTableName:"",isAutoHide:"",isListEdit:"",parentConfigTableId:"",parentConfigTableName:"",type:"",parentConfigPageId:"",parentConfigPageName:"",yzProjectName:"",importNo:"",yzAdminTypeId:"",
      },
      entity_column_readonly: {
        name:false,configTableTableName:false,orderBy:false,whereSql:false,exactDataManageMatchField:false,pageSize:false,isEdit:false,isNew:false,isUpdate:false,isDelete:false,isSearch:false,isSum:false,isBatchDelete:false,isImport:false,isExport:false,isMultiSelect:false,isExportEdit:false,exportMethod:false,isCopy:false,readOnlyCondition:false,importMethod:false,buttonVisible:false,isAfterAdd:false,isAfterSave:false,isBeforeSave:false,childPlace:false,isAfterDelete:false,isRepeatPwd:false,saveAutoClose:false,childStyle:false,isBeforeDelete:false,configTableName:false,isAutoHide:false,isListEdit:false,parentConfigTableId:false,parentConfigTableName:false,type:false,parentConfigPageId:false,parentConfigPageName:false,yzProjectName:true,importNo:false,yzAdminTypeId:false,
      },
      entity_rules: {
        name:{rules:[{"required":true,"errorMessage":"\u540D\u79F0\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},configTableTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},orderBy:{rules:[{"maxLength":500,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},whereSql:{rules:[]},exactDataManageMatchField:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},pageSize:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isNew:{rules:[{"required":true,"errorMessage":"is_new\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isUpdate:{rules:[{"required":true,"errorMessage":"is_update\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isDelete:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isSearch:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isSum:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isBatchDelete:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isImport:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isExport:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isMultiSelect:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isExportEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},exportMethod:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isCopy:{rules:[{"required":true,"errorMessage":"\u590D\u5236\u65B9\u6CD5\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},readOnlyCondition:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},importMethod:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},buttonVisible:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAfterAdd:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isAfterSave:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isBeforeSave:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},childPlace:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAfterDelete:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isRepeatPwd:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},saveAutoClose:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},childStyle:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isBeforeDelete:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},configTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAutoHide:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isListEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},parentConfigTableId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},parentConfigTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},type:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},parentConfigPageId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},parentConfigPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzProjectName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},importNo:{rules:[{"maxLength":45,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzAdminTypeId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    nameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.name; },
configTableTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configTableTableName; },
orderByReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.orderBy; },
whereSqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.whereSql; },
exactDataManageMatchFieldReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.exactDataManageMatchField; },
pageSizeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.pageSize; },
isEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isEdit; },
isNewReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isNew; },
isUpdateReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isUpdate; },
isDeleteReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isDelete; },
isSearchReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSearch; },
isSumReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSum; },
isBatchDeleteReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isBatchDelete; },
isImportReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isImport; },
isExportReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isExport; },
isMultiSelectReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isMultiSelect; },
isExportEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isExportEdit; },
exportMethodReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.exportMethod; },
isCopyReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isCopy; },
readOnlyConditionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.readOnlyCondition; },
importMethodReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importMethod; },
buttonVisibleReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.buttonVisible; },
isAfterAddReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAfterAdd; },
isAfterSaveReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAfterSave; },
isBeforeSaveReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isBeforeSave; },
childPlaceReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.childPlace; },
isAfterDeleteReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAfterDelete; },
isRepeatPwdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isRepeatPwd; },
saveAutoCloseReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.saveAutoClose; },
childStyleReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.childStyle; },
isBeforeDeleteReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isBeforeDelete; },
configTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configTableName; },
isAutoHideReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoHide; },
isListEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isListEdit; },
parentConfigTableIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.parentConfigTableId; },
parentConfigTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.parentConfigTableName; },
typeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.type; },
parentConfigPageIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.parentConfigPageId; },
parentConfigPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.parentConfigPageName; },
yzProjectNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectName; },
importNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importNo; },
yzAdminTypeIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId; },
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
      uni.$emit('yzpMenuChanged', { pageId: '1869016706289577986', tableName: 'config_page' });
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
      formParameters.configPage = {};
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
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={name:"",configTableTableName:"",orderBy:"",whereSql:"",exactDataManageMatchField:"",pageSize:"",isEdit:"",isNew:"",isUpdate:"",isDelete:"",isSearch:"",isSum:"",isBatchDelete:"",isImport:"",isExport:"",isMultiSelect:"",isExportEdit:"",exportMethod:"",isCopy:"",readOnlyCondition:"",importMethod:"",buttonVisible:"",isAfterAdd:"",isAfterSave:"",isBeforeSave:"",childPlace:"",isAfterDelete:"",isRepeatPwd:"",saveAutoClose:"",childStyle:"",isBeforeDelete:"",configTableName:"",isAutoHide:"",isListEdit:"",parentConfigTableId:"",parentConfigTableName:"",type:"",parentConfigPageId:"",parentConfigPageName:"",yzProjectName:"",importNo:"",yzAdminTypeId:"",};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
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
 const mutation=route==="/admin/auto/config_page/1869016706289577986"&&method==='DELETE'||method==='POST'&&["/admin/auto/config_page/1869016706289577986"+'/add',"/admin/auto/config_page/1869016706289577986"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/config_page/1869016706289577986"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/config_page/1869016706289577986"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["name","orderBy","whereSql","exactDataManageMatchField","pageSize","isEdit","isNew","isUpdate","isDelete","isSearch","isSum","isBatchDelete","isImport","isExport","isMultiSelect","isExportEdit","exportMethod","isCopy","readOnlyCondition","importMethod","buttonVisible","isAfterAdd","isAfterSave","isBeforeSave","childPlace","isAfterDelete","isRepeatPwd","saveAutoClose","childStyle","isBeforeDelete","configTableName","isAutoHide","isListEdit","parentConfigTableId","parentConfigTableName","type","parentConfigPageId","parentConfigPageName","importNo","yzAdminTypeId"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/config_page/1869016706289577986"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
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

    loadOriginalOptions2200000000000000105() {const that=this;that.yz_admin_type_list=[];(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["2200000000000000105"]||0)+1;that._originalOptions["2200000000000000105"]=optionSequence;const optionParent=null;that.originalRequest({url:"/admin/auto/config_page/1869016706289577986/findNameIdYzAdminType2200000000000000105",isCurrent:()=>optionSequence===that._originalOptions["2200000000000000105"]&&true,data:{parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName),...(that.readOnlyCondition?{id:that.entity.yzAdminTypeId}:{})},method:'POST',sendSessionId:true,success(res){if(res.code===200){that.yz_admin_type_list=res.data||[];}else uni.showModal({showCancel:false,content:res.msg});}});})();},
yzAdminTypeIdDetailChange(item) { if(this.yzAdminTypeIdReadOnly) return; this.entity.yzAdminTypeId=item ? item.id : '';  },
showCustomerButtons() { const current=this.originalContext(),actions=this.originalButtons.filter(button=>this['customerButtonShow'+button.id]);if(!actions.length){uni.showToast({title:'暂无可用操作',icon:'none'});return;}uni.showActionSheet({itemList:actions.map(button=>button.name),success:res=>{if(current()&&actions[res.tapIndex])this['customerButtonHandler'+actions[res.tapIndex].id]();}}); },

    
    
    
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      that.loadOriginalOptions2200000000000000105();

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
        url: '/admin/auto/config_page/1869016706289577986/detail/' + that.id + that.buildParentIdQuery(),
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
        
        that.originalRequest({
          url: '/admin/auto/config_page/1869016706289577986/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_page/1869016706289577986' + that.buildParentIdQuery(),
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
