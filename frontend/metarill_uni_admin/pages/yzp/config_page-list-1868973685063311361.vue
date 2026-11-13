<template>
  <view class='list-page main-page' >
    <!-- PC端 -->
    <uni-section title='配置页面' class='son-page-uni-section' type='line'>    <view class='section-view'>
    <view v-if='$deviceInfo.is_pc' class='list-page-pc'>

      <view class="list-page-pc-body">
          <uni-forms label-width="auto" class="pc-filters" :modelValue='filters' v-if="page_is_filter">
            <uni-forms-item label='id'>
<uni-easyinput primaryColor="#007aff" v-model='filters.id' confirm-type='search' @confirm='filterList'  />
</uni-forms-item>
<uni-forms-item label='名称'>
<uni-easyinput primaryColor="#007aff" v-model='filters.name' confirm-type='search' @confirm='filterList'  />
</uni-forms-item>

            <view class="pc-filters-buttons">
              <button class='cancel-btn' size="mini" @click='resetFilter'>重置</button>
              <button size="mini" type='primary' @click='filterList'>筛选</button>
            </view>
          </uni-forms>
          <view class="list-page-buttons">
            <button size="mini" type='primary' @click="toDetail('')" v-if="!readOnlyCondition && page_is_new">新增</button>

          </view>
          <uni-table class="list-page-pc-body-table" border  stripe emptyText="暂无更多数据"><uni-tr>
<!-- 操作列可见性与默认查看权限分离，行内按钮按自身规则显示。 --><uni-th align="center" v-if="true" width="">操作</uni-th>
<uni-th >id</uni-th>
<uni-th >后台类型</uni-th>
<uni-th >名称</uni-th>
<uni-th >Where</uni-th>
<uni-th >精确数据管理匹配字段</uni-th>
<uni-th >页面类型</uni-th>
</uni-tr>
<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''" v-for='(item, index) in list' :key='item.id'><uni-td v-if="true" class="buttons"><view><view class='list-action-link' v-if="page_is_edit" @click='toDetail(item.id)'>查看</view>
<view class='list-action-link' v-if="customerButtonShow1868973686766198786(item)" @click='customerButtonHandler1868973686766198786(item)'>生成代码</view>
<view class='list-action-link' v-if="customerButtonShow1868973686812336129(item)" @click='customerButtonHandler1868973686812336129(item)'>列权限</view>
<view class='list-action-link' v-if="customerButtonShow2111122692747956224(item)" @click='customerButtonHandler2111122692747956224(item)'>详情布局</view></view></uni-td><uni-td><text >{{item.id}}</text>
</uni-td>
<uni-td><text>{{$yzp.relationDisplayText(item.yzAdminTypeName)}}</text></uni-td>
<uni-td><text class='list-column-important'>{{item.name}}</text>
</uni-td>
<uni-td><text >{{item.whereSql}}</text>
</uni-td>
<uni-td><text >{{item.exactDataManageMatchField}}</text>
</uni-td>
<uni-td>{{item.typeShow}}</uni-td>
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
      <uni-list>
        <uni-list-item clickable v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
          <template v-slot:body>
            <view class='row'>
              <view class='row-item'><text>id</text><text >{{item.id}}</text></view><view class='row-item'><text>后台类型</text><text>{{$yzp.relationDisplayText(item.yzAdminTypeName)}}</text></view><view class='row-item'><text>名称</text><text class='list-column-important'>{{item.name}}</text></view><view class='row-item'><text>Where</text><text >{{item.whereSql}}</text></view><view class='row-item'><text>精确数据管理匹配字段</text><text >{{item.exactDataManageMatchField}}</text></view><view class='row-item'><text>页面类型</text>{{item.typeShow}}</view>
            </view>
          </template>
        </uni-list-item>
      </uni-list>
      <uni-load-more @clickLoadMore='loadNextPage()' :status='load_more_status' :contentText="loadMoreText"></uni-load-more>
      <view class='bottom-buttons' v-if="!readOnlyCondition && page_is_new">
        <button type="primary" @click="toDetail('')">新增</button>
      </view>
    </view>
    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange"><view class="select-page-dialog-view"><uni-section title='配置页面' class='select-page-uni-section' type='line'><view class='section-view'><view class="child-detail-dialog-view" :class="{ 'is-mobile': !$deviceInfo.is_pc }"><config_page_detail_1868973685063311361 v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parent_id" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup"></config_page_detail_1868973685063311361></view></view></uni-section></view></yzp-popup>
    <yzp-popup class='filter-popup' ref="filter_popup" background-color="#fff">
      <uni-section title='筛选条件' type='line'>
        <view class='section-view'>
          <uni-forms :modelValue='filters'>
            <uni-forms-item label='id'>
<uni-easyinput primaryColor="#007aff" v-model='filters.id' confirm-type='search' @confirm='filterList'  />
</uni-forms-item>
<uni-forms-item label='名称'>
<uni-easyinput primaryColor="#007aff" v-model='filters.name' confirm-type='search' @confirm='filterList'  />
</uni-forms-item>

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
  import utils from "@/common/utils";import yzp from "@/common/yzp_runtime/yzp-runtime";import { menuBoolean } from '@/common/yzp_runtime/menuRuntime';import config_page_detail_1868973685063311361 from '@/pages/yzp/config_page-detail-1868973685063311361.vue';import { createListPageMixin } from '@/common/yzp_runtime/listPageRuntime';export default {mixins: [createListPageMixin({url: "/admin/auto/config_page/1868973685063311361/page",pageSize: 14,request(options) { return yzp.request(options); },onParentChanged() {if (Array.isArray(this.selectedList)) { this.selectedList = []; this.isSelectAll = false; }this.closeChildDetailPopup();},onLoaded() { var that = this;if (this.$deviceInfo.is_pc) {}}})],props: {    parent_id: {        type: String    },    parent_page_sn: {        type: String    },    is_son_page: {        type: Boolean,        default: false    },    child_page_open_type: {        type: Number,        default: 1    },    parentReadOnlyCondition: {        type: Boolean,        default: false    }},components: {config_page_detail_1868973685063311361},
  data() {
    return {
      page_sn: '1868973685063311361',page_is_new:  false,page_is_update:  true,page_is_edit:  true,permissionVersion: 0,page_is_filter: false,childDetailPopupVisible: false,childDetailPopupId: '',childDetailPopupKey: 0,


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
		if (!this.page_is_update || this.parentReadOnlyCondition) {
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
      uni.$emit('yzpMenuChanged', { pageId: '1868973685063311361', tableName: 'config_page' });
    },
    openChildDetailPopup(id) {  this.childDetailPopupId = id || '';  this.childDetailPopupKey = this.childDetailPopupKey + 1;  this.childDetailPopupVisible = true;  this.$nextTick(() => {    this.$refs.child_detail_popup.open('center');  });},closeChildDetailPopup() {  this.childDetailPopupVisible = false;  this.childDetailPopupId = '';  if (this.$refs.child_detail_popup) {    this.$refs.child_detail_popup.close();  }},onChildDetailPopupChange(e) {  if (!e.show) {    this.childDetailPopupVisible = false;    this.childDetailPopupId = '';  }},handleChildDetailChange(change) {  this.handleDetailChange(change);},
    customerButtonShow1868973686766198786: function(item) {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686766198786",visibleScheme:"Normal",visibleRuleJson:""}, item, this.readOnlyCondition);},customerButtonHandler1868973686766198786: function(item) {
var that = this;
this.clicked_id = item.id;yzp.request({
url: "/admin/sys/config_button/execute",
data: {buttonId: "1868973686766198786", entityId: item.id, visibleScheme: "Normal", visibleRuleJson: ""},
sendSessionId:true,
method: "POST",
success: function(res) {
if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'list');
}
})
},customerButtonShow1868973686812336129: function(item) {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1868973686812336129",visibleScheme:"Normal",visibleRuleJson:""}, item, this.readOnlyCondition);},customerButtonHandler1868973686812336129: function(item) {
var that = this;
this.clicked_id = item.id;yzp.request({
url: "/admin/sys/config_button/execute",
data: {buttonId: "1868973686812336129", entityId: item.id, visibleScheme: "Normal", visibleRuleJson: ""},
sendSessionId:true,
method: "POST",
success: function(res) {
if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'list');
}
})
},customerButtonShow2111122692747956224: function(item) {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2111122692747956224",visibleScheme:"Normal",visibleRuleJson:""}, item, this.readOnlyCondition);},customerButtonHandler2111122692747956224: function(item) {
var that = this;
this.clicked_id = item.id;yzp.request({
url: "/admin/sys/config_button/execute",
data: {buttonId: "2111122692747956224", entityId: item.id, visibleScheme: "Normal", visibleRuleJson: ""},
sendSessionId:true,
method: "POST",
success: function(res) {
if (res.code == 200) { that.notifyMenuMutation(); }yzp.customButtonHandler(res, that, false, 'list');
}
})
},


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
      this.clicked_id = id;
      if (this.is_son_page && utils.isBlank(this.parent_id)) {
        uni.showModal({
          showCancel: false,
          content: "为避免数据丢失，请先保存当前页面的数据"
        });
        return;
      }
if (id === '' && !this.page_is_new) {    return;}if (id !== '' && !this.page_is_edit) {    return;}      // 子表弹窗配置只对 PC 端生效，移动端保持新页面打开。
      if (this.is_son_page && this.$deviceInfo.is_pc && this.child_page_open_type === 2 && this.openChildDetailPopup) {
        this.openChildDetailPopup(id);
        return;
      }
      uni.navigateTo({
        url: '/pages/yzp/config_page-detail-1868973685063311361?id=' + id + '&parent_id=' + this.parent_id + '&parent_page_sn=' + this.parent_page_sn + '&yzpMenuId=' + encodeURIComponent(this.yzpMenuId || ''),
        events: {
          onChange: function (change) {
            that.handleDetailChange(change);
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
