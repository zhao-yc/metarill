<template>
  <view class='list-page main-page' >
    <!-- PC端 -->
    
    <view v-if='$deviceInfo.is_pc' class='list-page-pc'>
      <uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="项目"></uni-nav-bar>
      <view class="list-page-pc-body">
          <uni-forms label-width="auto" class="pc-filters" :modelValue='filters' v-if="page_is_filter">
            <uni-forms-item label='名称'>
<uni-easyinput primaryColor="#0ab3ad" v-model='filters.name' confirm-type='search' @confirm='filterList'  /></uni-forms-item>

            <view class="pc-filters-buttons">
              <button class='cancel-btn' size="mini" @click='resetFilter'>重置</button>
              <button size="mini" type='primary' @click='filterList'>筛选</button>
            </view>
          </uni-forms>
          <view class="list-page-buttons">
            <button size="mini" type='primary' @click="toDetail('')" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_new && $yzp.hasPagePermission(page_sn, 'is_new')">新增</button>
            <button size='mini' type='primary' v-if="permissionVersion >= 0 && $yzp.hasButtonPermission('2069009429197623296')" @click='customerButtonHandler2069009429197623296'>全部项目同步运行时</button>
          </view>
          <uni-table class="list-page-pc-body-table" border  stripe emptyText="暂无更多数据"><uni-tr>
<!-- 操作列可见性与默认查看权限分离，行内按钮按自身规则显示。 --><uni-th align="center" v-if="page_is_edit" width="90">操作</uni-th>
<uni-th >名称</uni-th>
<uni-th >缩略名</uni-th>
<uni-th >数据库名称</uni-th>
<uni-th >java项目包名</uni-th>
<uni-th >yzp版本</uni-th>
<uni-th >数据库类型</uni-th>
<uni-th >PrimaryColor</uni-th>
<uni-th >简单项目</uni-th>
<uni-th >创建时间</uni-th>
<uni-th >修改时间</uni-th>
<uni-th >创建账号</uni-th>
<uni-th >修改账号</uni-th>
<uni-th >创建账号名</uni-th>
<uni-th >修改账号名</uni-th>
<uni-th >创建姓名</uni-th>
<uni-th >修改姓名</uni-th>
<uni-th >项目</uni-th>
</uni-tr>
<uni-tr :class="clicked_id == item.id ? 'uni-table-tr-clicked' : ''" v-for='(item, index) in list' :key='item.id'><uni-td v-if="page_is_edit" class="buttons"><view><view class='list-action-link' v-if="permissionVersion &gt;= 0 &amp;&amp; page_is_edit &amp;&amp; $yzp.hasPagePermission(page_sn, &apos;is_query&apos;)" @click='toDetail(item.id)'>查看</view></view></uni-td><uni-td><text >{{item.name}}</text></uni-td>
<uni-td><text >{{item.shortName}}</text></uni-td>
<uni-td><text >{{item.dbName}}</text></uni-td>
<uni-td><text >{{item.eclipseBaseName}}</text></uni-td>
<uni-td><text>{{$yzp.relationDisplayText(item.yzYzpVersionId)}}</text></uni-td>
<uni-td>{{item.dbTypeShow}}</uni-td>
<uni-td><text >{{item.primaryColor}}</text></uni-td>
<uni-td>{{item.isSmall === true || item.isSmall === 1 || item.isSmall === '1' ? '是' : '否'}}</uni-td>
<uni-td><text >{{item.insertTime}}</text></uni-td>
<uni-td><text >{{item.updateTime}}</text></uni-td>
<uni-td><text >{{item.insertManagerId}}</text></uni-td>
<uni-td><text >{{item.updateManagerId}}</text></uni-td>
<uni-td><text >{{item.insertUserName}}</text></uni-td>
<uni-td><text >{{item.updateUserName}}</text></uni-td>
<uni-td><text >{{item.insertManagerRealName}}</text></uni-td>
<uni-td><text >{{item.updateManagerRealName}}</text></uni-td>
<uni-td><text >{{item.yzProjectId}}</text></uni-td>
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
      <uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar :border="false" title="项目" rightText="筛选" @clickRight="openFilterPopup('bottom')"></uni-nav-bar>
      <!--<view class="sort-filter-view">
          <view class='sort-view'></view>
          <view class="filter-btn" @click="openFilterPopup('bottom')" v-if="page_is_filter">筛选</view>
      </view>-->
      <uni-list>
        <uni-list-item clickable v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
          <template v-slot:body>
            <view class='row'>
              <view class='row-item'><text>名称</text><text >{{item.name}}</text></view><view class='row-item'><text>缩略名</text><text >{{item.shortName}}</text></view><view class='row-item'><text>数据库名称</text><text >{{item.dbName}}</text></view><view class='row-item'><text>java项目包名</text><text >{{item.eclipseBaseName}}</text></view><view class='row-item'><text>yzp版本</text><text>{{$yzp.relationDisplayText(item.yzYzpVersionId)}}</text></view><view class='row-item'><text>数据库类型</text>{{item.dbTypeShow}}</view><view class='row-item'><text>PrimaryColor</text><text >{{item.primaryColor}}</text></view><view class='row-item'><text>简单项目</text>{{item.isSmall === true || item.isSmall === 1 || item.isSmall === '1' ? '是' : '否'}}</view><view class='row-item'><text>创建时间</text><text >{{item.insertTime}}</text></view><view class='row-item'><text>修改时间</text><text >{{item.updateTime}}</text></view><view class='row-item'><text>创建账号</text><text >{{item.insertManagerId}}</text></view><view class='row-item'><text>修改账号</text><text >{{item.updateManagerId}}</text></view><view class='row-item'><text>创建账号名</text><text >{{item.insertUserName}}</text></view><view class='row-item'><text>修改账号名</text><text >{{item.updateUserName}}</text></view><view class='row-item'><text>创建姓名</text><text >{{item.insertManagerRealName}}</text></view><view class='row-item'><text>修改姓名</text><text >{{item.updateManagerRealName}}</text></view><view class='row-item'><text>项目</text><text >{{item.yzProjectId}}</text></view>
            </view>
          </template>
        </uni-list-item>
      </uni-list>

      <uni-load-more @clickLoadMore='loadNextPage()' :status='load_more_status' :contentText="loadMoreText"></uni-load-more>
      <view class='bottom-buttons' v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_new && $yzp.hasPagePermission(page_sn, 'is_new')">
        <button type="primary" @click="toDetail('')">新增</button>
      </view>
    </view>
    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange"><view class="select-page-dialog-view"><uni-section title='项目' class='select-page-uni-section' type='line'><view class='section-view'><view class="child-detail-dialog-view" :class="{ 'is-mobile': !$deviceInfo.is_pc }"><yz_project_detail_6186303424888832 v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parent_id" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup"></yz_project_detail_6186303424888832></view></view></uni-section></view></yzp-popup>
    <yzp-popup class='filter-popup' ref="filter_popup" background-color="#fff">
      <uni-section title='筛选条件' type='line'>
        <view class='section-view'>
          <uni-forms :modelValue='filters'>
            <uni-forms-item label='名称'>
<uni-easyinput primaryColor="#0ab3ad" v-model='filters.name' confirm-type='search' @confirm='filterList'  /></uni-forms-item>

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
import yz_project_detail_6186303424888832 from '@/pages/yzp/yz_project-detail-6186303424888832.vue';
export default {mixins:[createOriginalListOptionsMixin({targetParentFields:[],optionFields:[]}),createListPageMixin({canLoad(){return !!uni.getStorageSync('loginStateId');},url:"/admin/auto/yz_project/6186303424888832/page",pageSize:20,request(options){return yzp.request(options);},onParentChanged(){if(Array.isArray(this.selectedList)){this.selectedList=[];this.isSelectAll=false;}if(this.closeChildDetailPopup)this.closeChildDetailPopup();},onLoaded(){if(this.syncOriginalSelectionRows)this.syncOriginalSelectionRows();else if(this.selectedList){for(const item of this.list)item.isSelect=this.selectedList.some(row=>String(row.id)===String(item.id));this.checkIsAllSelect();}}}),],props:{parent_id:{type:String},parent_page_sn:{type:String},is_son_page:{type:Boolean,default:false},child_page_open_type:{type:Number,default:1},parentReadOnlyCondition:{type:Boolean,default:false}},components:{yz_project_detail_6186303424888832},
  data() {
    return {
      page_sn:"6186303424888832",page_is_new:true,page_is_update:true,page_is_edit:true,page_is_filter:true,permissionVersion:0,childDetailPopupVisible:false,childDetailPopupId:'',childDetailPopupKey:0,
      
      
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
      uni.$emit('yzpMenuChanged', { pageId: '6186303424888832', tableName: 'yz_project' });
    },
    // 弹窗关闭先卸载子详情，重复打开不能沿用上个实体状态。
openChildDetailPopup(id){this.childDetailPopupId=id||'';this.childDetailPopupKey++;this.childDetailPopupVisible=true;const key=this.childDetailPopupKey,identity=this.originalListIdentity();this.$nextTick(()=>{if(this._originalListAlive&&this.childDetailPopupVisible&&this.childDetailPopupKey===key&&identity===this.originalListIdentity())this.$refs.child_detail_popup.open('center');});},
closeChildDetailPopup(){this.childDetailPopupVisible=false;this.childDetailPopupId='';this.$refs.child_detail_popup?.close();},
onChildDetailPopupChange(e){if(!e.show){this.childDetailPopupVisible=false;this.childDetailPopupId='';}},
handleChildDetailChange(change){this.handleDetailChange(change);},
    customerButtonHandler2069009429197623296(){var that=this;uni.showModal({content:"\u60A8\u786E\u5B9A\u8981\u540C\u6B65\u5F53\u524D\u8BBE\u5907\u5168\u90E8\u9879\u76EE\u8FD0\u884C\u65F6\u5417\uFF1F",success(modal){if(modal.confirm){yzp.request({url:"/admin/custom/index/sync_runtime_all_projects?parentId="+that.parent_id,sendSessionId:true,method:'POST',success(res){if(res.code==200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,false,'list');}});}}});},
    
    
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
        url: '/pages/yzp/yz_project-detail-6186303424888832?id=' + encodeURIComponent(id) + '&parent_id=' + encodeURIComponent(this.parent_id || '') + '&parent_page_sn=' + encodeURIComponent(this.parent_page_sn || '') + '&yzpMenuId=' + encodeURIComponent(this.yzpMenuId || ''),
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
