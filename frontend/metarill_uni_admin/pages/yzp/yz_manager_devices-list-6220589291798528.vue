<template>
  <view class='list-page main-page' >
    <!-- PC端 -->

    <view v-if='$deviceInfo.is_pc' class='list-page-pc'>
      <uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="管理员信息扩展"></uni-nav-bar>
      <view class="list-page-pc-body">
          <uni-forms label-width="auto" class="pc-filters" :modelValue='filters' v-if="page_is_filter">

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
<uni-th >管理员</uni-th>
<uni-th >名称</uni-th>
<uni-th >id</uni-th>
<uni-th >IsMac</uni-th>
</uni-tr>
<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''" v-for='(item, index) in list' :key='item.id'><uni-td v-if="true" class="buttons"><view><view class='list-action-link' v-if="permissionVersion >= 0 && page_is_edit && $yzp.hasPagePermission(page_sn, 'is_query')" @click='toDetail(item.id)'>查看</view>
<view class='list-action-link' v-if="customerButtonShow2034487360028282880(item)" @click='customerButtonHandler2034487360028282880(item)'>生成或重置 Token</view>
<view class='list-action-link list-action-link--warn' v-if="customerButtonShow2034487360028282881(item)" @click='customerButtonHandler2034487360028282881(item)'>停用 Token</view></view></uni-td><uni-td><text>{{$yzp.relationDisplayText(item.yzManagerName)}}</text></uni-td>
<uni-td><text >{{item.name}}</text>
</uni-td>
<uni-td><text >{{item.id}}</text>
</uni-td>
<uni-td>{{(($checkboxRange || [])[item.isMac==null?0:item.isMac] || {}).text || ''}}</uni-td>
</uni-tr></uni-table>
          <view v-if="page_size>0" class="uni-pagination-box">
            <uni-pagination show-icon :page-size="page_size" :current="page_no" :total="rows_qty"
              @change="paginationChange" />
          </view>

        </view>
    </view>

    <!-- 移动端 -->
    <view v-if='!$deviceInfo.is_pc' class='list-page-mobile'>
      <uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="管理员信息扩展" rightText="筛选" @clickRight="openFilterPopup('bottom')"></uni-nav-bar>
      <!--<view class="sort-filter-view">
          <view class='sort-view'></view>
          <view class="filter-btn" @click="openFilterPopup('bottom')" v-if="page_is_filter">筛选</view>
      </view>-->
      <uni-list>
        <uni-list-item clickable v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
          <template v-slot:body>
            <view class='row'>
              <view class='row-item'><text>管理员</text><text>{{$yzp.relationDisplayText(item.yzManagerName)}}</text></view><view class='row-item'><text>名称</text><text >{{item.name}}</text></view><view class='row-item'><text>id</text><text >{{item.id}}</text></view><view class='row-item'><text>IsMac</text>{{(($checkboxRange || [])[item.isMac==null?0:item.isMac] || {}).text || ''}}</view>
            </view>
          </template>
        </uni-list-item>
      </uni-list>
      <uni-load-more @clickLoadMore='loadNextPage()' :status='load_more_status' :contentText="loadMoreText"></uni-load-more>
      <view class='bottom-buttons' v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_new && $yzp.hasPagePermission(page_sn, 'is_new')">
        <button type="primary" @click="toDetail('')">新增</button>
      </view>
    </view>
    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange"><view class="select-page-dialog-view"><uni-section title='管理员信息扩展' class='select-page-uni-section' type='line'><view class='section-view'><view class="child-detail-dialog-view" :class="{ 'is-mobile': !$deviceInfo.is_pc }"><yz_manager_devices_detail_6220589291798528 v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parent_id" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup"></yz_manager_devices_detail_6220589291798528></view></view></uni-section></view></yzp-popup>
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
  import utils from "@/common/utils";import yzp from "@/common/yzp_runtime/yzp-runtime";import yz_manager_devices_detail_6220589291798528 from '@/pages/yzp/yz_manager_devices-detail-6220589291798528.vue';export default {props: {    parent_id: {        type: String    },    parent_page_sn: {        type: String    },    is_son_page: {        type: Boolean,        default: false    },    child_page_open_type: {        type: Number,        default: 1    },    parentReadOnlyCondition: {        type: Boolean,        default: false    }},components: {yz_manager_devices_detail_6220589291798528},
  data() {
    return {
      page_sn: '6220589291798528',page_is_new:  true,page_is_update:  true,page_is_edit:  true,permissionVersion: 0,page_is_filter: false,childDetailPopupVisible: false,childDetailPopupId: '',childDetailPopupKey: 0,


      load_more_status: 'more',
      isShowCount: false,
      list: [],
      //menu_id: '',
      page_no: 1,
      page_size: 15,
      pages_qty: 0,
      rows_qty: 0,
      filters: {},
      clicked_id: '',
      orderBy: {},
      statistics: {},
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
    loadMoreText() {
      return {
        contentnomore: "共" + this.rows_qty + "条数据，当前" + this.page_no + "/" + this.pages_qty + "页，已全部加载",
        contentdown: "共" + this.rows_qty + "条数据，当前" + this.page_no + "/" + this.pages_qty + "页，点击显示更多"
      }
    }
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
    this.isShowCount = e.isShowCount;
  },
  onReachBottom() {
    //this.loadNextPage();
  },
  onPullDownRefresh: function () {
    this.refreshData();
  },
  methods: {
    openChildDetailPopup(id) {  this.childDetailPopupId = id || '';  this.childDetailPopupKey = this.childDetailPopupKey + 1;  this.childDetailPopupVisible = true;  this.$nextTick(() => {    this.$refs.child_detail_popup.open('center');  });},closeChildDetailPopup() {  this.childDetailPopupVisible = false;  this.childDetailPopupId = '';  if (this.$refs.child_detail_popup) {    this.$refs.child_detail_popup.close();  }},onChildDetailPopupChange(e) {  if (!e.show) {    this.childDetailPopupVisible = false;    this.childDetailPopupId = '';  }},handleChildDetailChange(change) {  this.handleDetailChange(change);},
    customerButtonShow2034487360028282880: function(item) {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2034487360028282880",visibleScheme:"Normal",visibleRuleJson:""}, item, this.readOnlyCondition);},customerButtonHandler2034487360028282880: function(item) {
var that = this;
this.clicked_id = item.id;uni.showModal({content: "继续后旧 Token 会立即失效，是否继续？",success(modal) {if (modal.confirm) {yzp.request({
url: "/admin/sys/config_button/execute",
data: {buttonId: "2034487360028282880", entityId: item.id, visibleScheme: "Normal", visibleRuleJson: ""},
sendSessionId:true,
method: "POST",
success: function(res) {
yzp.customButtonHandler(res, that, false, 'list');
}
})
}}})},customerButtonShow2034487360028282881: function(item) {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2034487360028282881",visibleScheme:"Normal",visibleRuleJson:""}, item, this.readOnlyCondition);},customerButtonHandler2034487360028282881: function(item) {
var that = this;
this.clicked_id = item.id;uni.showModal({content: "停用后当前设备的 MCP 连接会失效，是否继续？",success(modal) {if (modal.confirm) {yzp.request({
url: "/admin/sys/config_button/execute",
data: {buttonId: "2034487360028282881", entityId: item.id, visibleScheme: "Normal", visibleRuleJson: ""},
sendSessionId:true,
method: "POST",
success: function(res) {
yzp.customButtonHandler(res, that, false, 'list');
}
})
}}})},


    // 刷新权限快照后递增版本，触发按钮显示重新计算。
    refreshCurrentPermissions: function() {
      var that = this;
      yzp.refreshCurrentPermissions(function() { that.permissionVersion++; }, function() { that.permissionVersion++; });
    },
    initLists: function () {
      var that = this;

    },
    loadNextPage: function () {
      if (!this.$deviceInfo.is_pc) {
        if (this.load_more_status != 'no-more') {
          this.page_no = this.page_no + 1;
          this.getList();
        }
      }
    },
    paginationChange: function (e) {
      this.page_no = e.current;
      this.getList();
    },
    filterList() {
      this.refreshData();
      this.closeFilterPopup();
    },
    refreshData() {
      this.page_no = 1;
      this.list = [];
      this.getList();
    },
    handleDetailChange(change) {
      var action = typeof change === 'string' ? change : (change && change.action);
      yzp.showDetailPageSuccessToast(action);
      if (this.applyUpdatedEntityFromDetail(change)) {
        return;
      }
      this.refreshData();
    },
    applyUpdatedEntityFromDetail(change) {
      // 仅修改保存可精准替换当前列表行；新增和其它动作保持刷新列表。
      if (!change || typeof change === 'string' || change.action != 'save' || change.method != 'update') {
        return false;
      }
      var entity = change.entity;
      if (!entity || !entity.id) {
        return false;
      }
      var index = this.list.findIndex(function (item) {
        return item && item.id == entity.id;
      });
      if (index < 0) {
        return false;
      }
      this.list.splice(index, 1, Object.assign({}, this.list[index], entity));
      return true;
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
if (id === '' && (!this.page_is_new || !yzp.hasPagePermission(this.page_sn, 'is_new'))) {    return;}if (id !== '' && (!this.page_is_edit || !yzp.hasPagePermission(this.page_sn, 'is_query'))) {    return;}      // 子表弹窗配置只对 PC 端生效，移动端保持新页面打开。
      if (this.is_son_page && this.$deviceInfo.is_pc && this.child_page_open_type === 2 && this.openChildDetailPopup) {
        this.openChildDetailPopup(id);
        return;
      }
      uni.navigateTo({
        url: '/pages/yzp/yz_manager_devices-detail-6220589291798528?id=' + id + '&parent_id=' + this.parent_id + '&parent_page_sn=' + this.parent_page_sn,
        events: {
          onChange: function (change) {
            that.handleDetailChange(change);
          }
        }
      })
    },
    getList(e) {
      var that = this;

      if (this.is_son_page && utils.isBlank(this.parent_id)) {
        return;
      }


      that.load_more_status = 'loading';
      yzp.request({
        url: '/admin/auto/yz_manager_devices/6220589291798528/page',
        data: {
          orderBy: this.orderBy,
          pageNo: this.page_no,
          pageSize: this.page_size,
          filters: this.filters,
          parentId: this.parent_id,

        },
        sendSessionId: true,
        method: 'POST',
        success: function (res) {
          if (res.code == 200) {
            var newList = res.data.records;
            that.statistics = res.map.statistics;
            if (that.$deviceInfo.is_pc) {
              that.list = newList;

            } else {
              that.list = that.list.concat(newList);
            }
            that.pages_qty = res.data.pages;
            that.rows_qty = res.data.total;
            //that.dialogClose();
            if (that.isShowCount) {
                uni.$emit("updateMenuCount")
            }
          } else {
            uni.showModal({
              showCancel: false,
              content: res.msg
            })
          }
        },
        complete() {
          if (that.pages_qty == 0 || that.page_no == that.pages_qty) {
            that.load_more_status = 'no-more';
          } else {
            that.load_more_status = 'more';
          }
          uni.stopPullDownRefresh();
        }
      });
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
