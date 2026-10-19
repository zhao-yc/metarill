<template>
  <view class='list-page main-page' ##auto_hide##>
    <!-- PC端 -->
    ##pc_top_uni_section##
    <view v-if='$deviceInfo.is_pc' class='list-page-pc'>
      ##pc_nav_bar##
      <view class="list-page-pc-body">
          <uni-forms label-width="auto" class="pc-filters" :modelValue='filters' v-if="page_is_filter">
            ##filter_columns##
            <view class="pc-filters-buttons">
              <button class='cancel-btn' size="mini" @click='resetFilter'>重置</button>
              <button size="mini" type='primary' @click='filterList'>筛选</button>
            </view>
          </uni-forms>
          <view class="list-page-buttons">
            <button size="mini" type='primary' @click="toDetail('')" v-if="##listNewButtonVisibleCondition##">新增</button>
            ##list_buttons##
          </view>
          ##list_main##
          <view v-if="page_size>0" class="uni-pagination-box">
            <uni-pagination show-icon :page-size="page_size" :current="page_no" :total="rows_qty"
              @change="paginationChange" />
          </view>
          ##multi_select_buttons##
        </view>
    </view>
    ##pc_end_uni_section##
    <!-- 移动端 -->
    <view v-if='!$deviceInfo.is_pc' class='list-page-mobile'>
      ##mobile_nav_bar##
      <!--<view class="sort-filter-view">
          <view class='sort-view'></view>
          <view class="filter-btn" @click="openFilterPopup('bottom')" v-if="page_is_filter">筛选</view>
      </view>-->
##mobile_list##
      <uni-load-more @clickLoadMore='loadNextPage()' :status='load_more_status' :contentText="loadMoreText"></uni-load-more>
      <view class='bottom-buttons' v-if="##listNewButtonVisibleCondition##">
        <button type="primary" @click="toDetail('')">新增</button>
      </view>
    </view>
    ##child_detail_popup##
    <yzp-popup class='filter-popup' ref="filter_popup" background-color="#fff">
      <uni-section title='筛选条件' type='line'>
        <view class='section-view'>
          <uni-forms :modelValue='filters'>
            ##filter_columns##
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
  ##script_top##
  data() {
    return {
      //##other_return_data##
      //##lists_list_and_index##
      //##const_list_column_ranges##
      load_more_status: 'more',
      isShowCount: false,
      yzpMenuId: '',
      list: [],
      //menu_id: '',
      page_no: 1,
      page_size: ##page_size##,
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
    //##mounted_scripts##

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
  onPullDownRefresh: function () {
    this.refreshData();
  },
  methods: {
    // 成功写入只刷新关联页面和表的角标，不依赖当前入口是否显示角标。
    notifyMenuMutation() {
      uni.$emit('yzpMenuChanged', { pageId: '##page_sn##', tableName: '##table_name##' });
    },
    //##popup_detail_methods##
    //##customer_button_handlers##
    //##list_buttons_handler##
    //##lists_select_change##
    // 刷新权限快照后递增版本，触发按钮显示重新计算。
    refreshCurrentPermissions: function() {
      var that = this;
      yzp.refreshCurrentPermissions(function() { that.permissionVersion++; }, function() { that.permissionVersion++; });
    },
    initLists: function () {
      var that = this;
      //##init_lists##
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
##toDetailNewGuard##    return;}##toDetailQueryGuard##    return;}      // 子表弹窗配置只对 PC 端生效，移动端保持新页面打开。
      if (this.is_son_page && this.$deviceInfo.is_pc && this.child_page_open_type === 2 && this.openChildDetailPopup) {
        this.openChildDetailPopup(id);
        return;
      }
      uni.navigateTo({
        url: '/pages/yzp/##table_name##-detail-##page_id##?id=' + id + '&parent_id=' + this.parent_id + '&parent_page_sn=' + this.parent_page_sn + '&yzpMenuId=' + encodeURIComponent(this.yzpMenuId || ''),
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
      ##selected_only_empty_guard##

      that.load_more_status = 'loading';
      yzp.request({
        url: '/admin/auto/##table_name##/##page_sn##/page',
        data: {
          orderBy: this.orderBy,
          pageNo: this.page_no,
          pageSize: this.page_size,
          filters: this.filters,
          parentId: this.parent_id,
          ##selected_ids_request_param##
        },
        sendSessionId: true,
        method: 'POST',
        success: function (res) {
          if (res.code == 200) {
            var newList = res.data.records;
            that.statistics = res.map.statistics;
            if (that.$deviceInfo.is_pc) {
              that.list = newList;
              ##multi_select_page_mark_selected##
            } else {
              that.list = that.list.concat(newList);
            }
            that.pages_qty = res.data.pages;
            that.rows_qty = res.data.total;
            //that.dialogClose();
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
      //##reset_columns##
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
