<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="配置页面" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''" ></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="配置页面"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='id' name='id'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!idReadOnly' v-model='entity.id' maxlength='50'  />
<text v-if='idReadOnly'>{{entity.id}}</text>
</uni-forms-item>

<uni-forms-item label='后台类型' name='yzAdminTypeId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzAdminTypeIdReadOnly'>{{readonlyDisplayText(entity.yzAdminTypeName)}}</text><template v-if='!yzAdminTypeIdReadOnly'><zxz-uni-data-select v-model='entity.yzAdminTypeId' :localdata='yz_admin_type_list' filterable :disabled='yzAdminTypeIdReadOnly' dataKey='name' dataValue='id' @change='yz_admin_typeSelectChange'  remote :key="'original-options-2200000000000000105-'+originalRemoteOptions['2200000000000000105'].revision" :remote-selected="originalRemoteOptions['2200000000000000105'].selected" :remote-loading="originalRemoteOptions['2200000000000000105'].loading" :remote-has-more="originalRemoteOptions['2200000000000000105'].hasMore" @open="openOriginalRemoteOptions('2200000000000000105')" @inputChange="searchOriginalRemoteOptions('2200000000000000105',$event)" @loadMore="moreOriginalRemoteOptions('2200000000000000105')"/></template>
</uni-forms-item>

<uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='表名' name='configTableTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!configTableTableNameReadOnly' v-model='entity.configTableTableName' maxlength='50'  />
<text v-if='configTableTableNameReadOnly'>{{entity.configTableTableName}}</text>
</uni-forms-item>

<uni-forms-item label='order_by' name='orderBy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!orderByReadOnly' v-model='entity.orderBy' maxlength='500'  />
<text v-if='orderByReadOnly'>{{entity.orderBy}}</text>
</uni-forms-item>

<uni-forms-item label='Where' name='whereSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" :disabled='whereSqlReadOnly' type='textarea' maxlength='-1' v-model='entity.whereSql' />

</uni-forms-item>

<uni-forms-item label='精确数据管理匹配字段' name='exactDataManageMatchField'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!exactDataManageMatchFieldReadOnly' v-model='entity.exactDataManageMatchField' maxlength='50'  />
<text v-if='exactDataManageMatchFieldReadOnly'>{{entity.exactDataManageMatchField}}</text><view class='input-tips'>用于精确数据管理时，当前视图参与匹配的数据字段名</view>
</uni-forms-item>

<uni-forms-item label='page_size' name='pageSize'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!pageSizeReadOnly' v-model='entity.pageSize' maxlength='50'  />
<text v-if='pageSizeReadOnly'>{{entity.pageSize}}</text>
</uni-forms-item>

<uni-forms-item label='is_edit' name='isEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isEditReadOnly'>{{(($checkboxRange || [])[entity.isEdit==null?0:entity.isEdit] || {}).text || ''}}</text><template v-if='!isEditReadOnly'><uni-data-checkbox :disabled='isEditReadOnly' v-model='entity.isEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_new' name='isNew'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isNewReadOnly'>{{(($checkboxRange || [])[entity.isNew==null?0:entity.isNew] || {}).text || ''}}</text><template v-if='!isNewReadOnly'><uni-data-checkbox :disabled='isNewReadOnly' v-model='entity.isNew' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_update' name='isUpdate'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isUpdateReadOnly'>{{(($checkboxRange || [])[entity.isUpdate==null?0:entity.isUpdate] || {}).text || ''}}</text><template v-if='!isUpdateReadOnly'><uni-data-checkbox :disabled='isUpdateReadOnly' v-model='entity.isUpdate' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_delete' name='isDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isDeleteReadOnly'>{{(($checkboxRange || [])[entity.isDelete==null?0:entity.isDelete] || {}).text || ''}}</text><template v-if='!isDeleteReadOnly'><uni-data-checkbox :disabled='isDeleteReadOnly' v-model='entity.isDelete' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_search' name='isSearch'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isSearchReadOnly'>{{(($checkboxRange || [])[entity.isSearch==null?0:entity.isSearch] || {}).text || ''}}</text><template v-if='!isSearchReadOnly'><uni-data-checkbox :disabled='isSearchReadOnly' v-model='entity.isSearch' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_sum' name='isSum'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isSumReadOnly'>{{(($checkboxRange || [])[entity.isSum==null?0:entity.isSum] || {}).text || ''}}</text><template v-if='!isSumReadOnly'><uni-data-checkbox :disabled='isSumReadOnly' v-model='entity.isSum' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_batch_delete' name='isBatchDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isBatchDeleteReadOnly'>{{(($checkboxRange || [])[entity.isBatchDelete==null?0:entity.isBatchDelete] || {}).text || ''}}</text><template v-if='!isBatchDeleteReadOnly'><uni-data-checkbox :disabled='isBatchDeleteReadOnly' v-model='entity.isBatchDelete' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_import' name='isImport'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isImportReadOnly'>{{(($checkboxRange || [])[entity.isImport==null?0:entity.isImport] || {}).text || ''}}</text><template v-if='!isImportReadOnly'><uni-data-checkbox :disabled='isImportReadOnly' v-model='entity.isImport' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_export' name='isExport'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isExportReadOnly'>{{(($checkboxRange || [])[entity.isExport==null?0:entity.isExport] || {}).text || ''}}</text><template v-if='!isExportReadOnly'><uni-data-checkbox :disabled='isExportReadOnly' v-model='entity.isExport' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_multi_select' name='isMultiSelect'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isMultiSelectReadOnly'>{{(($checkboxRange || [])[entity.isMultiSelect==null?0:entity.isMultiSelect] || {}).text || ''}}</text><template v-if='!isMultiSelectReadOnly'><uni-data-checkbox :disabled='isMultiSelectReadOnly' v-model='entity.isMultiSelect' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_export_edit' name='isExportEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isExportEditReadOnly'>{{(($checkboxRange || [])[entity.isExportEdit==null?0:entity.isExportEdit] || {}).text || ''}}</text><template v-if='!isExportEditReadOnly'><uni-data-checkbox :disabled='isExportEditReadOnly' v-model='entity.isExportEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='export_method' name='exportMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!exportMethodReadOnly' v-model='entity.exportMethod' maxlength='50'  />
<text v-if='exportMethodReadOnly'>{{entity.exportMethod}}</text>
</uni-forms-item>

<uni-forms-item label='复制方法' name='isCopy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isCopyReadOnly'>{{(($checkboxRange || [])[entity.isCopy==null?0:entity.isCopy] || {}).text || ''}}</text><template v-if='!isCopyReadOnly'><uni-data-checkbox :disabled='isCopyReadOnly' v-model='entity.isCopy' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='read_only_condition' name='readOnlyCondition'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!readOnlyConditionReadOnly' v-model='entity.readOnlyCondition' maxlength='50'  />
<text v-if='readOnlyConditionReadOnly'>{{entity.readOnlyCondition}}</text>
</uni-forms-item>

<uni-forms-item label='import_method' name='importMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!importMethodReadOnly' v-model='entity.importMethod' maxlength='50'  />
<text v-if='importMethodReadOnly'>{{entity.importMethod}}</text>
</uni-forms-item>

<uni-forms-item label='button_visible' name='buttonVisible'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!buttonVisibleReadOnly' v-model='entity.buttonVisible' maxlength='50'  />
<text v-if='buttonVisibleReadOnly'>{{entity.buttonVisible}}</text>
</uni-forms-item>

<uni-forms-item label='is_after_add' name='isAfterAdd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAfterAddReadOnly'>{{(($checkboxRange || [])[entity.isAfterAdd==null?0:entity.isAfterAdd] || {}).text || ''}}</text><template v-if='!isAfterAddReadOnly'><uni-data-checkbox :disabled='isAfterAddReadOnly' v-model='entity.isAfterAdd' :localdata='$checkboxRange' /></template><view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>

<uni-forms-item label='is_after_save' name='isAfterSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAfterSaveReadOnly'>{{(($checkboxRange || [])[entity.isAfterSave==null?0:entity.isAfterSave] || {}).text || ''}}</text><template v-if='!isAfterSaveReadOnly'><uni-data-checkbox :disabled='isAfterSaveReadOnly' v-model='entity.isAfterSave' :localdata='$checkboxRange' /></template><view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>

<uni-forms-item label='is_before_save' name='isBeforeSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isBeforeSaveReadOnly'>{{(($checkboxRange || [])[entity.isBeforeSave==null?0:entity.isBeforeSave] || {}).text || ''}}</text><template v-if='!isBeforeSaveReadOnly'><uni-data-checkbox :disabled='isBeforeSaveReadOnly' v-model='entity.isBeforeSave' :localdata='$checkboxRange' /></template><view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>

<uni-forms-item label='子模块位置' name='childPlace'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='childPlaceReadOnly'>{{entity.childPlaceShow}}</text><template v-if='!childPlaceReadOnly'><uni-data-checkbox :disabled='childPlaceReadOnly' v-model='entity.childPlace' :localdata='childPlaceRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_after_delete' name='isAfterDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAfterDeleteReadOnly'>{{(($checkboxRange || [])[entity.isAfterDelete==null?0:entity.isAfterDelete] || {}).text || ''}}</text><template v-if='!isAfterDeleteReadOnly'><uni-data-checkbox :disabled='isAfterDeleteReadOnly' v-model='entity.isAfterDelete' :localdata='$checkboxRange' /></template><view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>

<uni-forms-item label='is_repeat_pwd' name='isRepeatPwd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isRepeatPwdReadOnly'>{{(($checkboxRange || [])[entity.isRepeatPwd==null?0:entity.isRepeatPwd] || {}).text || ''}}</text><template v-if='!isRepeatPwdReadOnly'><uni-data-checkbox :disabled='isRepeatPwdReadOnly' v-model='entity.isRepeatPwd' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='save_auto_close' name='saveAutoClose'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!saveAutoCloseReadOnly' v-model='entity.saveAutoClose' maxlength='50'  />
<text v-if='saveAutoCloseReadOnly'>{{entity.saveAutoClose}}</text>
</uni-forms-item>

<uni-forms-item label='child_style' name='childStyle'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!childStyleReadOnly' v-model='entity.childStyle' maxlength='50'  />
<text v-if='childStyleReadOnly'>{{entity.childStyle}}</text>
</uni-forms-item>

<uni-forms-item label='is_before_delete' name='isBeforeDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isBeforeDeleteReadOnly'>{{(($checkboxRange || [])[entity.isBeforeDelete==null?0:entity.isBeforeDelete] || {}).text || ''}}</text><template v-if='!isBeforeDeleteReadOnly'><uni-data-checkbox :disabled='isBeforeDeleteReadOnly' v-model='entity.isBeforeDelete' :localdata='$checkboxRange' /></template><view class='input-tips'>仅 v7：控制页面写入钩子；v8 写入规则统一由 Service 生命周期执行，与本开关无关。</view>
</uni-forms-item>

<uni-forms-item label='自动隐藏' name='isAutoHide'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoHideReadOnly'>{{(($checkboxRange || [])[entity.isAutoHide==null?0:entity.isAutoHide] || {}).text || ''}}</text><template v-if='!isAutoHideReadOnly'><uni-data-checkbox :disabled='isAutoHideReadOnly' v-model='entity.isAutoHide' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='列表页编辑' name='isListEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isListEditReadOnly'>{{(($checkboxRange || [])[entity.isListEdit==null?0:entity.isListEdit] || {}).text || ''}}</text><template v-if='!isListEditReadOnly'><uni-data-checkbox :disabled='isListEditReadOnly' v-model='entity.isListEdit' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='页面类型' name='type'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='typeReadOnly'>{{entity.typeShow}}</text><template v-if='!typeReadOnly'><uni-data-checkbox :disabled='typeReadOnly' v-model='entity.type' :localdata='typeRange' /></template>
</uni-forms-item>

<uni-forms-item label='列表页查询前' name='isBeforeQuery'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isBeforeQueryReadOnly'>{{(($checkboxRange || [])[entity.isBeforeQuery==null?0:entity.isBeforeQuery] || {}).text || ''}}</text><template v-if='!isBeforeQueryReadOnly'><uni-data-checkbox :disabled='isBeforeQueryReadOnly' v-model='entity.isBeforeQuery' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='详情页查询前' name='isBeforeGet'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isBeforeGetReadOnly'>{{(($checkboxRange || [])[entity.isBeforeGet==null?0:entity.isBeforeGet] || {}).text || ''}}</text><template v-if='!isBeforeGetReadOnly'><uni-data-checkbox :disabled='isBeforeGetReadOnly' v-model='entity.isBeforeGet' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='不生成详情页' name='isNotGenerateDetailPage'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isNotGenerateDetailPageReadOnly'>{{(($checkboxRange || [])[entity.isNotGenerateDetailPage==null?0:entity.isNotGenerateDetailPage] || {}).text || ''}}</text><template v-if='!isNotGenerateDetailPageReadOnly'><uni-data-checkbox :disabled='isNotGenerateDetailPageReadOnly' v-model='entity.isNotGenerateDetailPage' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='移动端列表页样式' name='mobileListPageStyleType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='mobileListPageStyleTypeReadOnly'>{{entity.mobileListPageStyleTypeShow}}</text><template v-if='!mobileListPageStyleTypeReadOnly'><uni-data-checkbox :disabled='mobileListPageStyleTypeReadOnly' v-model='entity.mobileListPageStyleType' :localdata='mobileListPageStyleTypeRange' /></template>
</uni-forms-item>

<uni-forms-item label='移动端详情页保存后自动返回列表页' name='isAutoBackOnMobileSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoBackOnMobileSaveReadOnly'>{{(($checkboxRange || [])[entity.isAutoBackOnMobileSave==null?0:entity.isAutoBackOnMobileSave] || {}).text || ''}}</text><template v-if='!isAutoBackOnMobileSaveReadOnly'><uni-data-checkbox :disabled='isAutoBackOnMobileSaveReadOnly' v-model='entity.isAutoBackOnMobileSave' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='PC详情页每行列数' name='cols' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!colsReadOnly' v-model='entity.cols'  type='number' />
<text v-if='colsReadOnly'>{{entity.cols}}</text>
</uni-forms-item>

<uni-forms-item label='仅更新编辑页字段' name='isUpdateEditColumnsOnly' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isUpdateEditColumnsOnlyReadOnly'>{{(($checkboxRange || [])[entity.isUpdateEditColumnsOnly==null?0:entity.isUpdateEditColumnsOnly] || {}).text || ''}}</text><template v-if='!isUpdateEditColumnsOnlyReadOnly'><uni-data-checkbox :disabled='isUpdateEditColumnsOnlyReadOnly' v-model='entity.isUpdateEditColumnsOnly' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='详情页按钮位置' name='detailPageButtonsPosition' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='detailPageButtonsPositionReadOnly'>{{entity.detailPageButtonsPositionShow}}</text><template v-if='!detailPageButtonsPositionReadOnly'><uni-data-checkbox :disabled='detailPageButtonsPositionReadOnly' v-model='entity.detailPageButtonsPosition' :localdata='detailPageButtonsPositionRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>

            <config_page_column_6218036784398336 :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='config_page_column_6218036784398336'></config_page_column_6218036784398336>        <config_button_7097193256583168 :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='config_button_7097193256583168'></config_button_7097193256583168>
    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>-->
<button size='mini' type='default' v-if='customerButtonShow1868973686766198786' @click='customerButtonHandler1868973686766198786'>生成代码</button>
<button size='mini' type='default' v-if='customerButtonShow1868973686812336129' @click='customerButtonHandler1868973686812336129'>列权限</button>
<button size='mini' type='default' v-if='customerButtonShow1868973686887833602' @click='customerButtonHandler1868973686887833602'>列排序</button>
<button size='mini' type='default' v-if='customerButtonShow1868973686984302593' @click='customerButtonHandler1868973686984302593'>复制页面</button>
<button size='mini' type='default' v-if='customerButtonShow1868973686946553857' @click='customerButtonHandler1868973686946553857'>创建菜单</button>
<button size='mini' type='default' v-if='customerButtonShow2111122692747956224' @click='customerButtonHandler2111122692747956224'>详情布局</button></view></view>
  </view>
</template>
<script>
import { createOriginalRemoteOptionsMixin } from '@/common/yzp_runtime/originalRemoteOptionsMixin'; // 元数据同步的原List接线

import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";

import config_page_column_6218036784398336 from '@/pages/yzp/config_page_column-list-6218036784398336.vue';import config_button_7097193256583168 from '@/pages/yzp/config_button-list-7097193256583168.vue';
export default {
  // 原List分页由当前元数据同步；不覆盖原页面与事件。
  mixins:[createOriginalRemoteOptionsMixin([{"id":"2200000000000000105","field":"yzAdminTypeId","list":"yz_admin_type_list","endpoint":"/admin/auto/config_page/1868973685063311361/findNameIdYzAdminType2200000000000000105","requireParent":true,"requireTargetParent":false,"sameParent":false,"targetParentField":null,"readonlyField":"yzAdminTypeIdReadOnly"}],"entity",{originalHost:true})],

  components: {
    config_page_column_6218036784398336,config_button_7097193256583168,
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      yzpMenuId: '',
      page_sn: '1868973685063311361',
      parent_page_sn: '',
      formParametersName: 'formParameters1868973685063311361',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  false ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,
      yz_admin_type_list:[],
      childPlaceRange:[{"value":"OneWithMany","text":"一拖N"},{"value":"Tab","text":"标签显示"},{"value":"NewPage","text":"新页面打开"}], typeRange:[{"value":0,"text":"默认"},{"value":1,"text":"选择页"},{"value":2,"text":"多选页"}], mobileListPageStyleTypeRange:[{"value":1,"text":"传统样式"},{"value":2,"text":"Grid样式"},{"value":3,"text":"Grid带头像"}], detailPageButtonsPositionRange:[{"value":0,"text":"固定底部"},{"value":1,"text":"子表上面"}],
      entity: {
        id:'',yzAdminTypeId:'',name:'',configTableTableName:'',orderBy:'',whereSql:'',exactDataManageMatchField:'null',pageSize:'',isEdit:'',isNew:'',isUpdate:'',isDelete:'',isSearch:'',isSum:'',isBatchDelete:'',isImport:'',isExport:'',isMultiSelect:'',isExportEdit:'',exportMethod:'',isCopy:'',readOnlyCondition:'',importMethod:'',buttonVisible:'',isAfterAdd:'',isAfterSave:'',isBeforeSave:'',childPlace:'',isAfterDelete:'',isRepeatPwd:'',saveAutoClose:'',childStyle:'',isBeforeDelete:'',isAutoHide:'',isListEdit:'',type:'',isBeforeQuery:0,isBeforeGet:'',isNotGenerateDetailPage:'',mobileListPageStyleType:'',isAutoBackOnMobileSave:0,cols:2,isUpdateEditColumnsOnly:'',detailPageButtonsPosition:'',
      },
      entity_column_readonly: {
        id:true,yzAdminTypeId:false,name:false,configTableTableName:false,orderBy:false,whereSql:false,exactDataManageMatchField:false,pageSize:false,isEdit:false,isNew:false,isUpdate:false,isDelete:false,isSearch:false,isSum:false,isBatchDelete:false,isImport:false,isExport:false,isMultiSelect:false,isExportEdit:false,exportMethod:false,isCopy:false,readOnlyCondition:false,importMethod:false,buttonVisible:false,isAfterAdd:false,isAfterSave:false,isBeforeSave:false,childPlace:false,isAfterDelete:false,isRepeatPwd:false,saveAutoClose:false,childStyle:false,isBeforeDelete:false,isAutoHide:false,isListEdit:false,type:false,isBeforeQuery:false,isBeforeGet:false,isNotGenerateDetailPage:false,mobileListPageStyleType:false,isAutoBackOnMobileSave:false,cols:false,isUpdateEditColumnsOnly:false,detailPageButtonsPosition:false,
      },
      entity_rules: {
        id: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},yzAdminTypeId: {  rules: [  {      required: true,     errorMessage: '后台类型为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},name: {  rules: [  {      required: true,     errorMessage: '名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},configTableTableName: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},orderBy: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},whereSql: {  rules: [  ]},exactDataManageMatchField: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},pageSize: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isEdit: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isNew: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isUpdate: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isDelete: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isSearch: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isSum: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isBatchDelete: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isImport: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isExport: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isMultiSelect: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isExportEdit: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},exportMethod: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isCopy: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},readOnlyCondition: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},importMethod: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},buttonVisible: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isAfterAdd: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isAfterSave: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isBeforeSave: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},childPlace: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isAfterDelete: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isRepeatPwd: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},saveAutoClose: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},childStyle: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isBeforeDelete: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isAutoHide: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isListEdit: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},type: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isBeforeQuery: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isBeforeGet: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isNotGenerateDetailPage: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},mobileListPageStyleType: {  rules: [  {      required: true,     errorMessage: '移动端列表页样式为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isAutoBackOnMobileSave: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},cols: {  rules: [  {      required: true,     errorMessage: 'PC详情页每行列数为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isUpdateEditColumnsOnly: {  rules: [  {      required: true,     errorMessage: '仅更新编辑页字段为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},detailPageButtonsPosition: {  rules: [  {      required: true,     errorMessage: '详情页按钮位置为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    return false;},idReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.id;},yzAdminTypeIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},configTableTableNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.configTableTableName;},orderByReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.orderBy;},whereSqlReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.whereSql;},exactDataManageMatchFieldReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.exactDataManageMatchField;},pageSizeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.pageSize;},isEditReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isEdit;},isNewReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isNew;},isUpdateReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isUpdate;},isDeleteReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isDelete;},isSearchReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isSearch;},isSumReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isSum;},isBatchDeleteReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isBatchDelete;},isImportReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isImport;},isExportReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isExport;},isMultiSelectReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isMultiSelect;},isExportEditReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isExportEdit;},exportMethodReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.exportMethod;},isCopyReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isCopy;},readOnlyConditionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.readOnlyCondition;},importMethodReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.importMethod;},buttonVisibleReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.buttonVisible;},isAfterAddReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAfterAdd;},isAfterSaveReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAfterSave;},isBeforeSaveReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isBeforeSave;},childPlaceReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.childPlace;},isAfterDeleteReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAfterDelete;},isRepeatPwdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isRepeatPwd;},saveAutoCloseReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.saveAutoClose;},childStyleReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.childStyle;},isBeforeDeleteReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isBeforeDelete;},isAutoHideReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoHide;},isListEditReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isListEdit;},typeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.type;},isBeforeQueryReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isBeforeQuery;},isBeforeGetReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isBeforeGet;},isNotGenerateDetailPageReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isNotGenerateDetailPage;},mobileListPageStyleTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.mobileListPageStyleType;},isAutoBackOnMobileSaveReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoBackOnMobileSave;},colsReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.cols;},isUpdateEditColumnsOnlyReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isUpdateEditColumnsOnly;},detailPageButtonsPositionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.detailPageButtonsPosition;},
    customerButtonShow1868973686766198786() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686766198786",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1868973686812336129() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686812336129",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1868973686887833602() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686887833602",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1868973686984302593() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686984302593",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1868973686946553857() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686946553857",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow2111122692747956224() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2111122692747956224",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},hasVisibleCustomerButtons() {this.permissionVersion;return this.customerButtonShow1868973686766198786 || this.customerButtonShow1868973686812336129 || this.customerButtonShow1868973686887833602 || this.customerButtonShow1868973686984302593 || this.customerButtonShow1868973686946553857 || this.customerButtonShow2111122692747956224;},
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
      uni.$emit('yzpMenuChanged', { pageId: '1868973685063311361', tableName: 'config_page' });
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
      formParameters.configPage = {};
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
    customerButtonHandler1868973686766198786: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1868973686766198786", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1868973686812336129: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1868973686812336129", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1868973686887833602: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1868973686887833602", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1868973686984302593: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1868973686984302593", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1868973686946553857: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1868973686946553857", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler2111122692747956224: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2111122692747956224", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'detail');}})},showCustomerButtons: function() {var that = this;var actions = [];if (that.customerButtonShow1868973686766198786) {actions.push({name: "生成代码", handler: function() { that.customerButtonHandler1868973686766198786(); }});}if (that.customerButtonShow1868973686812336129) {actions.push({name: "列权限", handler: function() { that.customerButtonHandler1868973686812336129(); }});}if (that.customerButtonShow1868973686887833602) {actions.push({name: "列排序", handler: function() { that.customerButtonHandler1868973686887833602(); }});}if (that.customerButtonShow1868973686984302593) {actions.push({name: "复制页面", handler: function() { that.customerButtonHandler1868973686984302593(); }});}if (that.customerButtonShow1868973686946553857) {actions.push({name: "创建菜单", handler: function() { that.customerButtonHandler1868973686946553857(); }});}if (that.customerButtonShow2111122692747956224) {actions.push({name: "详情布局", handler: function() { that.customerButtonHandler2111122692747956224(); }});}if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},


    yz_admin_typeSelectChange: function(item) {this.entity.yzAdminTypeId = item ? item.id : '';this.entity.yzAdminTypeName = item ? item.name : '';},
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      that.loadOriginalRemoteOptions("2200000000000000105");
    },
    refreshSonsData: function () {
      // this.getEntity();
      this.$refs.config_page_column_6218036784398336.refreshData();this.$refs.config_button_7097193256583168.refreshData();
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
        url: '/admin/auto/config_page/1868973685063311361/detail/' + that.id + that.buildParentIdQuery(),
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
        that.entity.configTableId = that.parent_id;
        yzp.request({
          url: '/admin/auto/config_page/1868973685063311361/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_page/1868973685063311361' + that.buildParentIdQuery(),
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