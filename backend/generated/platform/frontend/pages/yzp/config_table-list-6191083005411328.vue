<template>
  <view class='list-page main-page' >
    <!-- PC端 -->
    
    <view v-if='$deviceInfo.is_pc' class='list-page-pc'>
      <uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="表"></uni-nav-bar>
      <view class="list-page-pc-body">
          <uni-forms label-width="auto" class="pc-filters" :modelValue='filters' v-if="page_is_filter">
            <uni-forms-item label='名称'>
<uni-easyinput primaryColor="#0ab3ad" v-model='filters.name' confirm-type='search' @confirm='filterList'  /></uni-forms-item>
<uni-forms-item label='表名'>
<uni-easyinput primaryColor="#0ab3ad" v-model='filters.tableName' confirm-type='search' @confirm='filterList' focus /></uni-forms-item>
<uni-forms-item label='数据库视图'>
<uni-data-checkbox v-model='filters.isView' :localdata='$checkboxRange' /></uni-forms-item>

            <view class="pc-filters-buttons">
              <button class='cancel-btn' size="mini" @click='resetFilter'>重置</button>
              <button size="mini" type='primary' @click='filterList'>筛选</button>
            </view>
          </uni-forms>
          <view class="list-page-buttons">
            <button size="mini" type='primary' @click="toDetail('')" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_new && $yzp.hasPagePermission(page_sn, 'is_new')">新增</button>
            
          </view>
          <uni-table class="list-page-pc-body-table" border  stripe emptyText="暂无更多数据"><uni-tr>
<!-- 操作列可见性与默认查看权限分离，行内按钮按自身规则显示。 --><uni-th align="center" v-if="true" width="">操作</uni-th>
<uni-th >名称</uni-th>
<uni-th >表名</uni-th>
<uni-th >sql</uni-th>
<uni-th >oracle_sql</uni-th>
<uni-th >where_sql</uni-th>
<uni-th >order_by</uni-th>
<uni-th >group_by</uni-th>
<uni-th >page_size</uni-th>
<uni-th >is_edit</uni-th>
<uni-th >is_new</uni-th>
<uni-th >is_save</uni-th>
<uni-th >is_delete</uni-th>
<uni-th >is_search</uni-th>
<uni-th >before_delete_sql</uni-th>
<uni-th >after_save_sql</uni-th>
<uni-th >after_import_sql</uni-th>
<uni-th >is_import</uni-th>
<uni-th >is_export</uni-th>
<uni-th >is_multi_select</uni-th>
<uni-th >is_lazy_query</uni-th>
<uni-th >grid_edit</uni-th>
<uni-th >title</uni-th>
<uni-th >title_2</uni-th>
<uni-th >title_3</uni-th>
<uni-th >is_sum</uni-th>
<uni-th >grid_width</uni-th>
<uni-th >is_print_preview</uni-th>
<uni-th >is_batch_delete</uni-th>
<uni-th >import_method</uni-th>
<uni-th >read_only_condition</uni-th>
<uni-th >pop_window_width</uni-th>
<uni-th >pop_window_height</uni-th>
<uni-th >is_export_edit</uni-th>
<uni-th >is_no_save_and_close</uni-th>
<uni-th >is_update</uni-th>
<uni-th >is_head_group</uni-th>
<uni-th >is_modify_log</uni-th>
<uni-th >layout</uni-th>
<uni-th >conn_string</uni-th>
<uni-th >export_method</uni-th>
<uni-th >import_no</uni-th>
<uni-th >insert_time</uni-th>
<uni-th >update_time</uni-th>
<uni-th >insert_manager_id</uni-th>
<uni-th >insert_user_name</uni-th>
<uni-th >update_manager_id</uni-th>
<uni-th >insert_manager_real_name</uni-th>
<uni-th >update_manager_real_name</uni-th>
<uni-th >update_user_name</uni-th>
<uni-th >copy_method</uni-th>
<uni-th >sql_more</uni-th>
<uni-th >authorize_scheme</uni-th>
<uni-th >clear_method</uni-th>
<uni-th >is_raw</uni-th>
<uni-th >same_edit_code</uni-th>
<uni-th >sql_grid</uni-th>
<uni-th >data_src</uni-th>
<uni-th >main_column</uni-th>
<uni-th >batch_new_page_name</uni-th>
<uni-th >edit_table_id</uni-th>
<uni-th >edit_table_name</uni-th>
<uni-th >edit_page_id</uni-th>
<uni-th >edit_page_name</uni-th>
<uni-th >edit_key_column</uni-th>
<uni-th >button_visible</uni-th>
<uni-th >auto_save</uni-th>
<uni-th >child_place</uni-th>
<uni-th >super_column_name</uni-th>
<uni-th >edit_page_url</uni-th>
<uni-th >display_option</uni-th>
<uni-th >bpm_step_kind</uni-th>
<uni-th >document_ready</uni-th>
<uni-th >help</uni-th>
<uni-th >edit_width</uni-th>
<uni-th >yz_project_name</uni-th>
<uni-th >is_platform</uni-th>
<uni-th >is_sys</uni-th>
<uni-th >is_repeat_pwd</uni-th>
<uni-th >所属后台</uni-th>
<uni-th >数据归属模式</uni-th>
</uni-tr>
<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''" v-for='(item, index) in list' :key='item.id'><uni-td v-if="true" class="buttons"><view><view class='list-action-link' v-if="permissionVersion &gt;= 0 &amp;&amp; page_is_edit &amp;&amp; $yzp.hasPagePermission(page_sn, &apos;is_query&apos;)" @click='toDetail(item.id)'>查看</view>
<view class='list-action-link' v-if="customerButtonShow1676828142707372033(item)" @click='customerButtonHandler1676828142707372033(item)'>列排序</view>
<view class='list-action-link' v-if="customerButtonShow2076978804953321472(item)" @click='customerButtonHandler2076978804953321472(item)'>索引管理</view>
<view class='list-action-link' v-if="customerButtonShow1861242035055382529(item)" @click='customerButtonHandler1861242035055382529(item)'>一键生成</view></view></uni-td><uni-td><text class='list-column-important'>{{item.name}}</text></uni-td>
<uni-td><text >{{item.tableName}}</text></uni-td>
<uni-td><text >{{item.sql}}</text></uni-td>
<uni-td><text >{{item.oracleSql}}</text></uni-td>
<uni-td><text >{{item.whereSql}}</text></uni-td>
<uni-td><text >{{item.orderBy}}</text></uni-td>
<uni-td><text >{{item.groupBy}}</text></uni-td>
<uni-td><text >{{item.pageSize}}</text></uni-td>
<uni-td><text >{{item.isEdit}}</text></uni-td>
<uni-td><text >{{item.isNew}}</text></uni-td>
<uni-td><text >{{item.isSave}}</text></uni-td>
<uni-td><text >{{item.isDelete}}</text></uni-td>
<uni-td><text >{{item.isSearch}}</text></uni-td>
<uni-td><text >{{item.beforeDeleteSql}}</text></uni-td>
<uni-td><text >{{item.afterSaveSql}}</text></uni-td>
<uni-td><text >{{item.afterImportSql}}</text></uni-td>
<uni-td><text >{{item.isImport}}</text></uni-td>
<uni-td><text >{{item.isExport}}</text></uni-td>
<uni-td><text >{{item.isMultiSelect}}</text></uni-td>
<uni-td><text >{{item.isLazyQuery}}</text></uni-td>
<uni-td><text >{{item.gridEdit}}</text></uni-td>
<uni-td><text >{{item.title}}</text></uni-td>
<uni-td><text >{{item.title2}}</text></uni-td>
<uni-td><text >{{item.title3}}</text></uni-td>
<uni-td><text >{{item.isSum}}</text></uni-td>
<uni-td><text >{{item.gridWidth}}</text></uni-td>
<uni-td><text >{{item.isPrintPreview}}</text></uni-td>
<uni-td><text >{{item.isBatchDelete}}</text></uni-td>
<uni-td><text >{{item.importMethod}}</text></uni-td>
<uni-td><text >{{item.readOnlyCondition}}</text></uni-td>
<uni-td><text >{{item.popWindowWidth}}</text></uni-td>
<uni-td><text >{{item.popWindowHeight}}</text></uni-td>
<uni-td><text >{{item.isExportEdit}}</text></uni-td>
<uni-td><text >{{item.isNoSaveAndClose}}</text></uni-td>
<uni-td><text >{{item.isUpdate}}</text></uni-td>
<uni-td><text >{{item.isHeadGroup}}</text></uni-td>
<uni-td><text >{{item.isModifyLog}}</text></uni-td>
<uni-td><text >{{item.layout}}</text></uni-td>
<uni-td><text >{{item.connString}}</text></uni-td>
<uni-td><text >{{item.exportMethod}}</text></uni-td>
<uni-td><text >{{item.importNo}}</text></uni-td>
<uni-td><text >{{item.insertTime}}</text></uni-td>
<uni-td><text >{{item.updateTime}}</text></uni-td>
<uni-td><text >{{item.insertManagerId}}</text></uni-td>
<uni-td><text >{{item.insertUserName}}</text></uni-td>
<uni-td><text >{{item.updateManagerId}}</text></uni-td>
<uni-td><text >{{item.insertManagerRealName}}</text></uni-td>
<uni-td><text >{{item.updateManagerRealName}}</text></uni-td>
<uni-td><text >{{item.updateUserName}}</text></uni-td>
<uni-td><text >{{item.copyMethod}}</text></uni-td>
<uni-td><text >{{item.sqlMore}}</text></uni-td>
<uni-td><text >{{item.authorizeScheme}}</text></uni-td>
<uni-td><text >{{item.clearMethod}}</text></uni-td>
<uni-td><text >{{item.isRaw}}</text></uni-td>
<uni-td><text >{{item.sameEditCode}}</text></uni-td>
<uni-td><text >{{item.sqlGrid}}</text></uni-td>
<uni-td><text >{{item.dataSrc}}</text></uni-td>
<uni-td><text >{{item.mainColumn}}</text></uni-td>
<uni-td><text >{{item.batchNewPageName}}</text></uni-td>
<uni-td><text >{{item.editTableId}}</text></uni-td>
<uni-td><text >{{item.editTableName}}</text></uni-td>
<uni-td><text >{{item.editPageId}}</text></uni-td>
<uni-td><text >{{item.editPageName}}</text></uni-td>
<uni-td><text >{{item.editKeyColumn}}</text></uni-td>
<uni-td><text >{{item.buttonVisible}}</text></uni-td>
<uni-td><text >{{item.autoSave}}</text></uni-td>
<uni-td><text >{{item.childPlace}}</text></uni-td>
<uni-td><text >{{item.superColumnName}}</text></uni-td>
<uni-td><text >{{item.editPageUrl}}</text></uni-td>
<uni-td><text >{{item.displayOption}}</text></uni-td>
<uni-td><text >{{item.bpmStepKind}}</text></uni-td>
<uni-td><text >{{item.documentReady}}</text></uni-td>
<uni-td><text >{{item.help}}</text></uni-td>
<uni-td><text >{{item.editWidth}}</text></uni-td>
<uni-td><text >{{item.yzProjectName}}</text></uni-td>
<uni-td><text >{{item.isPlatform}}</text></uni-td>
<uni-td><text >{{item.isSys}}</text></uni-td>
<uni-td><text >{{item.isRepeatPwd}}</text></uni-td>
<uni-td><text>{{$yzp.relationDisplayText(item.yzAdminTypeId)}}</text></uni-td>
<uni-td><text >{{item.scopeKind}}</text></uni-td>
</uni-tr></uni-table>
          <view v-if="list_error" role="alert" class="uni-pagination-box"><text>{{list_error}}</text><button size="mini" @click="retryListRequest">重试加载</button></view>
          <view v-if="page_size>0" class="uni-pagination-box">
            <uni-pagination :key="page_no + ':' + pagination_version" show-icon :page-size="page_size" :current="page_no" :total="rows_qty"
              @change="paginationChange" />
          </view>
          
        </view>
    </view>
    
    <!-- 移动端 -->
    <view v-if='!$deviceInfo.is_pc' class='list-page-mobile'>
      <uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="表" rightText="筛选" @clickRight="openFilterPopup('bottom')"></uni-nav-bar>
      <!--<view class="sort-filter-view">
          <view class='sort-view'></view>
          <view class="filter-btn" @click="openFilterPopup('bottom')" v-if="page_is_filter">筛选</view>
      </view>-->
      <uni-list>
        <uni-list-item clickable v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
          <template v-slot:body>
            <view class='row'>
              <view class='row-item'><text>名称</text><text class='list-column-important'>{{item.name}}</text></view><view class='row-item'><text>表名</text><text >{{item.tableName}}</text></view><view class='row-item'><text>sql</text><text >{{item.sql}}</text></view><view class='row-item'><text>oracle_sql</text><text >{{item.oracleSql}}</text></view><view class='row-item'><text>where_sql</text><text >{{item.whereSql}}</text></view><view class='row-item'><text>order_by</text><text >{{item.orderBy}}</text></view><view class='row-item'><text>group_by</text><text >{{item.groupBy}}</text></view><view class='row-item'><text>page_size</text><text >{{item.pageSize}}</text></view><view class='row-item'><text>is_edit</text><text >{{item.isEdit}}</text></view><view class='row-item'><text>is_new</text><text >{{item.isNew}}</text></view><view class='row-item'><text>is_save</text><text >{{item.isSave}}</text></view><view class='row-item'><text>is_delete</text><text >{{item.isDelete}}</text></view><view class='row-item'><text>is_search</text><text >{{item.isSearch}}</text></view><view class='row-item'><text>before_delete_sql</text><text >{{item.beforeDeleteSql}}</text></view><view class='row-item'><text>after_save_sql</text><text >{{item.afterSaveSql}}</text></view><view class='row-item'><text>after_import_sql</text><text >{{item.afterImportSql}}</text></view><view class='row-item'><text>is_import</text><text >{{item.isImport}}</text></view><view class='row-item'><text>is_export</text><text >{{item.isExport}}</text></view><view class='row-item'><text>is_multi_select</text><text >{{item.isMultiSelect}}</text></view><view class='row-item'><text>is_lazy_query</text><text >{{item.isLazyQuery}}</text></view><view class='row-item'><text>grid_edit</text><text >{{item.gridEdit}}</text></view><view class='row-item'><text>title</text><text >{{item.title}}</text></view><view class='row-item'><text>title_2</text><text >{{item.title2}}</text></view><view class='row-item'><text>title_3</text><text >{{item.title3}}</text></view><view class='row-item'><text>is_sum</text><text >{{item.isSum}}</text></view><view class='row-item'><text>grid_width</text><text >{{item.gridWidth}}</text></view><view class='row-item'><text>is_print_preview</text><text >{{item.isPrintPreview}}</text></view><view class='row-item'><text>is_batch_delete</text><text >{{item.isBatchDelete}}</text></view><view class='row-item'><text>import_method</text><text >{{item.importMethod}}</text></view><view class='row-item'><text>read_only_condition</text><text >{{item.readOnlyCondition}}</text></view><view class='row-item'><text>pop_window_width</text><text >{{item.popWindowWidth}}</text></view><view class='row-item'><text>pop_window_height</text><text >{{item.popWindowHeight}}</text></view><view class='row-item'><text>is_export_edit</text><text >{{item.isExportEdit}}</text></view><view class='row-item'><text>is_no_save_and_close</text><text >{{item.isNoSaveAndClose}}</text></view><view class='row-item'><text>is_update</text><text >{{item.isUpdate}}</text></view><view class='row-item'><text>is_head_group</text><text >{{item.isHeadGroup}}</text></view><view class='row-item'><text>is_modify_log</text><text >{{item.isModifyLog}}</text></view><view class='row-item'><text>layout</text><text >{{item.layout}}</text></view><view class='row-item'><text>conn_string</text><text >{{item.connString}}</text></view><view class='row-item'><text>export_method</text><text >{{item.exportMethod}}</text></view><view class='row-item'><text>import_no</text><text >{{item.importNo}}</text></view><view class='row-item'><text>insert_time</text><text >{{item.insertTime}}</text></view><view class='row-item'><text>update_time</text><text >{{item.updateTime}}</text></view><view class='row-item'><text>insert_manager_id</text><text >{{item.insertManagerId}}</text></view><view class='row-item'><text>insert_user_name</text><text >{{item.insertUserName}}</text></view><view class='row-item'><text>update_manager_id</text><text >{{item.updateManagerId}}</text></view><view class='row-item'><text>insert_manager_real_name</text><text >{{item.insertManagerRealName}}</text></view><view class='row-item'><text>update_manager_real_name</text><text >{{item.updateManagerRealName}}</text></view><view class='row-item'><text>update_user_name</text><text >{{item.updateUserName}}</text></view><view class='row-item'><text>copy_method</text><text >{{item.copyMethod}}</text></view><view class='row-item'><text>sql_more</text><text >{{item.sqlMore}}</text></view><view class='row-item'><text>authorize_scheme</text><text >{{item.authorizeScheme}}</text></view><view class='row-item'><text>clear_method</text><text >{{item.clearMethod}}</text></view><view class='row-item'><text>is_raw</text><text >{{item.isRaw}}</text></view><view class='row-item'><text>same_edit_code</text><text >{{item.sameEditCode}}</text></view><view class='row-item'><text>sql_grid</text><text >{{item.sqlGrid}}</text></view><view class='row-item'><text>data_src</text><text >{{item.dataSrc}}</text></view><view class='row-item'><text>main_column</text><text >{{item.mainColumn}}</text></view><view class='row-item'><text>batch_new_page_name</text><text >{{item.batchNewPageName}}</text></view><view class='row-item'><text>edit_table_id</text><text >{{item.editTableId}}</text></view><view class='row-item'><text>edit_table_name</text><text >{{item.editTableName}}</text></view><view class='row-item'><text>edit_page_id</text><text >{{item.editPageId}}</text></view><view class='row-item'><text>edit_page_name</text><text >{{item.editPageName}}</text></view><view class='row-item'><text>edit_key_column</text><text >{{item.editKeyColumn}}</text></view><view class='row-item'><text>button_visible</text><text >{{item.buttonVisible}}</text></view><view class='row-item'><text>auto_save</text><text >{{item.autoSave}}</text></view><view class='row-item'><text>child_place</text><text >{{item.childPlace}}</text></view><view class='row-item'><text>super_column_name</text><text >{{item.superColumnName}}</text></view><view class='row-item'><text>edit_page_url</text><text >{{item.editPageUrl}}</text></view><view class='row-item'><text>display_option</text><text >{{item.displayOption}}</text></view><view class='row-item'><text>bpm_step_kind</text><text >{{item.bpmStepKind}}</text></view><view class='row-item'><text>document_ready</text><text >{{item.documentReady}}</text></view><view class='row-item'><text>help</text><text >{{item.help}}</text></view><view class='row-item'><text>edit_width</text><text >{{item.editWidth}}</text></view><view class='row-item'><text>yz_project_name</text><text >{{item.yzProjectName}}</text></view><view class='row-item'><text>is_platform</text><text >{{item.isPlatform}}</text></view><view class='row-item'><text>is_sys</text><text >{{item.isSys}}</text></view><view class='row-item'><text>is_repeat_pwd</text><text >{{item.isRepeatPwd}}</text></view><view class='row-item'><text>所属后台</text><text>{{$yzp.relationDisplayText(item.yzAdminTypeId)}}</text></view><view class='row-item'><text>数据归属模式</text><text >{{item.scopeKind}}</text></view>
            </view>
          </template>
        </uni-list-item>
      </uni-list>

      <uni-load-more @clickLoadMore='loadNextPage()' :status='load_more_status' :contentText="loadMoreText"></uni-load-more>
      <view class='bottom-buttons' v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_new && $yzp.hasPagePermission(page_sn, 'is_new')">
        <button type="primary" @click="toDetail('')">新增</button>
      </view>
    </view>
    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange"><view class="select-page-dialog-view"><uni-section title='表' class='select-page-uni-section' type='line'><view class='section-view'><view class="child-detail-dialog-view" :class="{ 'is-mobile': !$deviceInfo.is_pc }"><config_table_detail_6191083005411328 v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parent_id" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup"></config_table_detail_6191083005411328></view></view></uni-section></view></yzp-popup>
    <yzp-popup class='filter-popup' ref="filter_popup" background-color="#fff">
      <uni-section title='筛选条件' type='line'>
        <view class='section-view'>
          <uni-forms :modelValue='filters'>
            <uni-forms-item label='名称'>
<uni-easyinput primaryColor="#0ab3ad" v-model='filters.name' confirm-type='search' @confirm='filterList'  /></uni-forms-item>
<uni-forms-item label='表名'>
<uni-easyinput primaryColor="#0ab3ad" v-model='filters.tableName' confirm-type='search' @confirm='filterList' focus /></uni-forms-item>
<uni-forms-item label='数据库视图'>
<uni-data-checkbox v-model='filters.isView' :localdata='$checkboxRange' /></uni-forms-item>

          </uni-forms>
        </view>
      </uni-section>
      <view class="popup-footer">
        <!--<button class='cancel-btn' @click='resetFilter'>重置</button>-->
        <button type="primary" @click='filterList'>确定</button>
      </view>
    </yzp-popup>
  </view>
</template>
<script>
  import utils from '@/common/utils';
import yzp from '@/common/yzp_runtime/yzp-runtime';
import project from '@/common/project';
import { menuBoolean } from '@/common/yzp_runtime/menuRuntime';
import { createListPageMixin } from '@/common/yzp_runtime/listPageRuntime';
import { createOriginalListOptionsMixin } from '@/common/yzp_runtime/originalListOptionsMixin';
import config_table_detail_6191083005411328 from '@/pages/yzp/config_table-detail-6191083005411328.vue';
export default {mixins:[createOriginalListOptionsMixin({targetParentFields:[],optionFields:[]}),createListPageMixin({canLoad(){return !!uni.getStorageSync('loginStateId');},url:"/admin/auto/config_table/6191083005411328/page",pageSize:14,request(options){return yzp.request(options);},onParentChanged(){if(Array.isArray(this.selectedList)){this.selectedList=[];this.isSelectAll=false;}if(this.closeChildDetailPopup)this.closeChildDetailPopup();},onLoaded(){if(this.syncOriginalSelectionRows)this.syncOriginalSelectionRows();else if(this.selectedList){for(const item of this.list)item.isSelect=this.selectedList.some(row=>String(row.id)===String(item.id));this.checkIsAllSelect();}}}),],props:{parent_id:{type:String},parent_page_sn:{type:String},is_son_page:{type:Boolean,default:false},child_page_open_type:{type:Number,default:1},parentReadOnlyCondition:{type:Boolean,default:false}},components:{config_table_detail_6191083005411328},
  data() {
    return {
      page_sn:"6191083005411328",page_is_new:true,page_is_update:true,page_is_edit:true,page_is_filter:true,permissionVersion:0,childDetailPopupVisible:false,childDetailPopupId:'',childDetailPopupKey:0,
      
      
      isShowCount: false,
      yzpMenuId: '',
      //menu_id: '',
      filters: {},
      clicked_id: '',
      orderBy: {},
      yzManager: uni.getStorageSync("userInfo")
    }
  },
  computed: {
	readOnlyCondition() {
		if (this.parentReadOnlyCondition || getApp().globalData.readOnly) {
			return true;
		}
		return false;
	},
  },
  //作为子表时，onload不执行，所以放在这
  mounted: function (e) {
    //this.menu_id = e.menu_id;
    

    //全局只读
    if (getApp().globalData.readOnly) { this.page_is_new = false; this.page_is_update = false; }

    // 刷新权限快照后递增版本，触发按钮显示重新计算。
    this.refreshCurrentPermissions();
    this.getList();
    this.initLists();
  },
  onLoad: function (e) {
    // URL 布尔值必须明确解析，入口标识只用于详情导航定位。
    this.isShowCount = menuBoolean(e && e.isShowCount);
    this.yzpMenuId = e && e.yzpMenuId || '';
  },
  onReachBottom() {
    //this.loadNextPage();
  },
  methods: {
    // 成功写入只刷新关联页面和表的角标，不依赖当前入口是否显示角标。
    notifyMenuMutation() {
      uni.$emit('yzpMenuChanged', { pageId: '6191083005411328', tableName: 'config_table' });
    },
    // 弹窗关闭先卸载子详情，重复打开不能沿用上个实体状态。
openChildDetailPopup(id){this.childDetailPopupId=id||'';this.childDetailPopupKey++;this.childDetailPopupVisible=true;const key=this.childDetailPopupKey,identity=this.originalListIdentity();this.$nextTick(()=>{if(this._originalListAlive&&this.childDetailPopupVisible&&this.childDetailPopupKey===key&&identity===this.originalListIdentity())this.$refs.child_detail_popup.open('center');});},
closeChildDetailPopup(){this.childDetailPopupVisible=false;this.childDetailPopupId='';this.$refs.child_detail_popup?.close();},
onChildDetailPopupChange(e){if(!e.show){this.childDetailPopupVisible=false;this.childDetailPopupId='';}},
handleChildDetailChange(change){this.handleDetailChange(change);},
    customerButtonShow1676828142707372033(item){this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1676828142707372033","visibleScheme":"Normal","visibleRuleJson":""},item,this.readOnlyCondition);},customerButtonHandler1676828142707372033(item){var that=this;this.clicked_id=item.id;yzp.request({url:'/admin/sys/config_button/execute',data:{buttonId:"1676828142707372033",entityId:item.id,visibleScheme:"Normal",visibleRuleJson:""},sendSessionId:true,method:'POST',success(res){if(res.code==200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,false,'list');}});},customerButtonShow2076978804953321472(item){this.permissionVersion;return yzp.isButtonVisible({"buttonId":"2076978804953321472","visibleScheme":"Normal","visibleRuleJson":""},item,this.readOnlyCondition);},customerButtonHandler2076978804953321472(item){var that=this;this.clicked_id=item.id;yzp.request({url:'/admin/sys/config_button/execute',data:{buttonId:"2076978804953321472",entityId:item.id,visibleScheme:"Normal",visibleRuleJson:""},sendSessionId:true,method:'POST',success(res){if(res.code==200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,false,'list');}});},customerButtonShow1861242035055382529(item){this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1861242035055382529","visibleScheme":"Normal","visibleRuleJson":""},item,this.readOnlyCondition);},customerButtonHandler1861242035055382529(item){var that=this;this.clicked_id=item.id;yzp.request({url:'/admin/sys/config_button/execute',data:{buttonId:"1861242035055382529",entityId:item.id,visibleScheme:"Normal",visibleRuleJson:""},sendSessionId:true,method:'POST',success(res){if(res.code==200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,false,'list');}});},
    
    
    // 刷新权限快照后递增版本，触发按钮显示重新计算。
    refreshCurrentPermissions: function() {
      var that = this;
      yzp.refreshCurrentPermissions(function() { that.permissionVersion++; }, function() { that.permissionVersion++; });
    },
    initLists: function () {
      var that = this;
      
    },
    filterList() {
      this.refreshData();
      this.closeFilterPopup();
    },
    handleDetailChange(change) {
      var action = typeof change === 'string' ? change : (change && change.action);
      yzp.showDetailPageSuccessToast(action);
      if (this.applyUpdatedEntityFromDetail(change)) {
        return;
      }
      this.refreshData();
    },
    selectThis(item){
	    this.$emit('onPopSelect', item);
    },
    toDetail(id) {
      var that = this;
      this.clicked_id = id;const detailIdentity=this.originalListIdentity();const detailCurrent=()=>this._originalListAlive && detailIdentity===this.originalListIdentity();
      if (this.is_son_page && utils.isBlank(this.parent_id)) {
        uni.showModal({
          showCancel: false,
          content: "为避免数据丢失，请先保存当前页面的数据"
        });
        return;
      }
if (id === '' && (this.readOnlyCondition || !this.page_is_new || !yzp.hasPagePermission(this.page_sn, 'is_new'))) {    return;}if (id !== '' && (!this.page_is_edit || !yzp.hasPagePermission(this.page_sn, 'is_query'))) {    return;}      // 子表弹窗配置只对 PC 端生效，移动端保持新页面打开。
      if (this.is_son_page && this.$deviceInfo.is_pc && this.child_page_open_type === 2 && this.openChildDetailPopup) {
        this.openChildDetailPopup(id);
        return;
      }
      uni.navigateTo({
        url: '/pages/yzp/config_table-detail-6191083005411328?id=' + encodeURIComponent(id) + '&parent_id=' + encodeURIComponent(this.parent_id || '') + '&parent_page_sn=' + encodeURIComponent(this.parent_page_sn || '') + '&yzpMenuId=' + encodeURIComponent(this.yzpMenuId || ''),
        events: {
          onChange: function (change) {
            if(detailCurrent())that.handleDetailChange(change);
          }
        }
      })
    },
    openFilterPopup(type) {
      this.$refs['filter_popup'].open(type);
    },
    closeFilterPopup() {
      this.$refs['filter_popup'].close();
    },
    resetFilter() {
      this.filters = {};
      
    }
  }
}
</script>
<style>
.navigation {
  display: flex;
  align-items: center;
  border-bottom: 1px solid #EEEEEE;
  background-color: #FFFFFF;
  margin-top: 5px;
}

.navigation-right {
  padding: 0px 20px;
  border-left: 1px solid #EEEEEE;
}
</style>
