<template>
  <view class='list-page main-page' >
    <!-- PC端 -->
    <uni-section title='索引字段' class='son-page-uni-section' type='line'><view class='section-view'>
    <view v-if='$deviceInfo.is_pc' class='list-page-pc'>
      
      <view class="list-page-pc-body">
          <uni-forms label-width="auto" class="pc-filters" :modelValue='filters' v-if="page_is_filter">
            
            <view class="pc-filters-buttons">
              <button class='cancel-btn' size="mini" @click='resetFilter'>重置</button>
              <button size="mini" type='primary' @click='filterList'>筛选</button>
            </view>
          </uni-forms>
          <view class="list-page-buttons">
            <button size="mini" type='primary' @click="toDetail('')" v-if="!readOnlyCondition && page_is_new">新增</button>
            
          </view>
          <uni-table class="list-page-pc-body-table" border  stripe emptyText="暂无更多数据"><uni-tr>
<!-- 操作列可见性与默认查看权限分离，行内按钮按自身规则显示。 --><uni-th align="center" v-if="page_is_edit" width="90">操作</uni-th>
<uni-th >所属索引</uni-th>
<uni-th >配置字段</uni-th>
<uni-th >物理列名</uni-th>
<uni-th sortable @sort-change="sortNoSortChange">字段顺序</uni-th>
<uni-th >排序方向</uni-th>
<uni-th >config_index_index_name</uni-th>
<uni-th >config_column_name</uni-th>
<uni-th >yz_project_name</uni-th>
<uni-th >import_no</uni-th>
<uni-th >insert_time</uni-th>
<uni-th >update_time</uni-th>
<uni-th >insert_manager_id</uni-th>
<uni-th >update_manager_id</uni-th>
<uni-th >insert_manager_real_name</uni-th>
<uni-th >update_manager_real_name</uni-th>
<uni-th >insert_user_name</uni-th>
<uni-th >update_user_name</uni-th>
</uni-tr>
<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''" v-for='(item, index) in list' :key='item.id'><uni-td v-if="page_is_edit" class="buttons"><view><view class='list-action-link' v-if="page_is_edit" @click='toDetail(item.id)'>查看</view></view></uni-td><uni-td><text>{{$yzp.relationDisplayText(item.configIndexIndexName)}}</text></uni-td>
<uni-td><text>{{$yzp.relationDisplayText(item.configColumnName)}}</text></uni-td>
<uni-td><text >{{item.columnName}}</text></uni-td>
<uni-td><text >{{item.sortNo}}</text></uni-td>
<uni-td>{{item.sortDirectionShow}}</uni-td>
<uni-td><text >{{item.configIndexIndexName}}</text></uni-td>
<uni-td><text >{{item.configColumnName}}</text></uni-td>
<uni-td><text >{{item.yzProjectName}}</text></uni-td>
<uni-td><text >{{item.importNo}}</text></uni-td>
<uni-td><text >{{item.insertTime}}</text></uni-td>
<uni-td><text >{{item.updateTime}}</text></uni-td>
<uni-td><text >{{item.insertManagerId}}</text></uni-td>
<uni-td><text >{{item.updateManagerId}}</text></uni-td>
<uni-td><text >{{item.insertManagerRealName}}</text></uni-td>
<uni-td><text >{{item.updateManagerRealName}}</text></uni-td>
<uni-td><text >{{item.insertUserName}}</text></uni-td>
<uni-td><text >{{item.updateUserName}}</text></uni-td>
</uni-tr></uni-table>
          <view v-if="list_error" role="alert" class="uni-pagination-box"><text>{{list_error}}</text><button size="mini" @click="retryListRequest">重试加载</button></view>
          <view v-if="page_size>0" class="uni-pagination-box">
            <uni-pagination :key="page_no + ':' + pagination_version" show-icon :page-size="page_size" :current="page_no" :total="rows_qty"
              @change="paginationChange" />
          </view>
          
        </view>
    </view>
    </view></uni-section>
    <!-- 移动端 -->
    <view v-if='!$deviceInfo.is_pc' class='list-page-mobile'>
      
      <!--<view class="sort-filter-view">
          <view class='sort-view'></view>
          <view class="filter-btn" @click="openFilterPopup('bottom')" v-if="page_is_filter">筛选</view>
      </view>-->
      <view class="yz-row-container">
            <view class='yz-row' v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
              <view class='row-item'><text>所属索引</text><text>{{$yzp.relationDisplayText(item.configIndexIndexName)}}</text></view><view class='row-item'><text>配置字段</text><text>{{$yzp.relationDisplayText(item.configColumnName)}}</text></view><view class='row-item'><text>物理列名</text><text >{{item.columnName}}</text></view><view class='row-item'><text>字段顺序</text><text >{{item.sortNo}}</text></view><view class='row-item'><text>排序方向</text>{{item.sortDirectionShow}}</view><view class='row-item'><text>config_index_index_name</text><text >{{item.configIndexIndexName}}</text></view><view class='row-item'><text>config_column_name</text><text >{{item.configColumnName}}</text></view><view class='row-item'><text>yz_project_name</text><text >{{item.yzProjectName}}</text></view><view class='row-item'><text>import_no</text><text >{{item.importNo}}</text></view><view class='row-item'><text>insert_time</text><text >{{item.insertTime}}</text></view><view class='row-item'><text>update_time</text><text >{{item.updateTime}}</text></view><view class='row-item'><text>insert_manager_id</text><text >{{item.insertManagerId}}</text></view><view class='row-item'><text>update_manager_id</text><text >{{item.updateManagerId}}</text></view><view class='row-item'><text>insert_manager_real_name</text><text >{{item.insertManagerRealName}}</text></view><view class='row-item'><text>update_manager_real_name</text><text >{{item.updateManagerRealName}}</text></view><view class='row-item'><text>insert_user_name</text><text >{{item.insertUserName}}</text></view><view class='row-item'><text>update_user_name</text><text >{{item.updateUserName}}</text></view>
            </view>
      </view>

      <uni-load-more @clickLoadMore='loadNextPage()' :status='load_more_status' :contentText="loadMoreText"></uni-load-more>
      <view class='bottom-buttons' v-if="!readOnlyCondition && page_is_new">
        <button type="primary" @click="toDetail('')">新增</button>
      </view>
    </view>
    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange"><view class="select-page-dialog-view"><uni-section title='索引字段' class='select-page-uni-section' type='line'><view class='section-view'><view class="child-detail-dialog-view" :class="{ 'is-mobile': !$deviceInfo.is_pc }"><config_index_column_detail_2076977554350190593 v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parent_id" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup"></config_index_column_detail_2076977554350190593></view></view></uni-section></view></yzp-popup>
    <yzp-popup class='filter-popup' ref="filter_popup" background-color="#fff">
      <uni-section title='筛选条件' type='line'>
        <view class='section-view'>
          <uni-forms :modelValue='filters'>
            
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
import config_index_column_detail_2076977554350190593 from '@/pages/yzp/config_index_column-detail-2076977554350190593.vue';
export default {mixins:[createOriginalListOptionsMixin({targetParentFields:[]}),createListPageMixin({url:"/admin/auto/config_index_column/2076977554350190593/page",pageSize:20,request(options){return yzp.request(options);},onParentChanged(){if(Array.isArray(this.selectedList)){this.selectedList=[];this.isSelectAll=false;}if(this.closeChildDetailPopup)this.closeChildDetailPopup();},onLoaded(){if(this.syncOriginalSelectionRows)this.syncOriginalSelectionRows();else if(this.selectedList){for(const item of this.list)item.isSelect=this.selectedList.some(row=>String(row.id)===String(item.id));this.checkIsAllSelect();}}}),],props:{parent_id:{type:String},parent_page_sn:{type:String},is_son_page:{type:Boolean,default:true},child_page_open_type:{type:Number,default:1},parentReadOnlyCondition:{type:Boolean,default:false}},components:{config_index_column_detail_2076977554350190593},
  data() {
    return {
      page_sn:"2076977554350190593",page_is_new:false,page_is_update:false,page_is_edit:true,page_is_filter:false,permissionVersion:0,childDetailPopupVisible:false,childDetailPopupId:'',childDetailPopupKey:0,
      
      
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
      uni.$emit('yzpMenuChanged', { pageId: '2076977554350190593', tableName: 'config_index_column' });
    },
    // 弹窗关闭先卸载子详情，重复打开不能沿用上个实体状态。
openChildDetailPopup(id){this.childDetailPopupId=id||'';this.childDetailPopupKey++;this.childDetailPopupVisible=true;const key=this.childDetailPopupKey,identity=this.originalListIdentity();this.$nextTick(()=>{if(this._originalListAlive&&this.childDetailPopupVisible&&this.childDetailPopupKey===key&&identity===this.originalListIdentity())this.$refs.child_detail_popup.open('center');});},
closeChildDetailPopup(){this.childDetailPopupVisible=false;this.childDetailPopupId='';this.$refs.child_detail_popup?.close();},
onChildDetailPopupChange(e){if(!e.show){this.childDetailPopupVisible=false;this.childDetailPopupId='';}},
handleChildDetailChange(change){this.handleDetailChange(change);},
    sortNoSortChange(e){this.orderBy.column="sortNo";this.orderBy.order=e.order;this.refreshData();},

    
    
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
if (id === '' && (this.readOnlyCondition || !this.page_is_new)) {    return;}if (id !== '' && !this.page_is_edit) {    return;}      // 子表弹窗配置只对 PC 端生效，移动端保持新页面打开。
      if (this.is_son_page && this.$deviceInfo.is_pc && this.child_page_open_type === 2 && this.openChildDetailPopup) {
        this.openChildDetailPopup(id);
        return;
      }
      uni.navigateTo({
        url: '/pages/yzp/config_index_column-detail-2076977554350190593?id=' + encodeURIComponent(id) + '&parent_id=' + encodeURIComponent(this.parent_id || '') + '&parent_page_sn=' + encodeURIComponent(this.parent_page_sn || '') + '&yzpMenuId=' + encodeURIComponent(this.yzpMenuId || ''),
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
