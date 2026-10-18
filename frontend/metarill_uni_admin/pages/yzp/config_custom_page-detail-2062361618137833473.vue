<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="自定义页面"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="自定义页面"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='页面名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='页面路径' name='pagePath' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!pagePathReadOnly' v-model='entity.pagePath' maxlength='200'  />
<text v-if='pagePathReadOnly'>{{entity.pagePath}}</text>
</uni-forms-item>

<uni-forms-item label='导航样式' name='navigationStyle'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!navigationStyleReadOnly' v-model='entity.navigationStyle' maxlength='20'  />
<text v-if='navigationStyleReadOnly'>{{entity.navigationStyle}}</text>
</uni-forms-item>

<uni-forms-item label='下拉刷新' name='enablePullDownRefresh'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='enablePullDownRefreshReadOnly'>{{(($checkboxRange || [])[entity.enablePullDownRefresh==null?0:entity.enablePullDownRefresh] || {}).text || ''}}</text><template v-if='!enablePullDownRefreshReadOnly'><uni-data-checkbox :disabled='enablePullDownRefreshReadOnly' v-model='entity.enablePullDownRefresh' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='显示左侧' name='isShowLeft'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isShowLeftReadOnly'>{{(($checkboxRange || [])[entity.isShowLeft==null?0:entity.isShowLeft] || {}).text || ''}}</text><template v-if='!isShowLeftReadOnly'><uni-data-checkbox :disabled='isShowLeftReadOnly' v-model='entity.isShowLeft' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='显示顶部' name='isShowTop'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isShowTopReadOnly'>{{(($checkboxRange || [])[entity.isShowTop==null?0:entity.isShowTop] || {}).text || ''}}</text><template v-if='!isShowTopReadOnly'><uni-data-checkbox :disabled='isShowTopReadOnly' v-model='entity.isShowTop' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!sortNoReadOnly' v-model='entity.sortNo'  type='number' />
<text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>

<!-- 所属后台沿用平台List选择器与只读展示；许可占位组件不替换交互。 -->
<uni-forms-item label='所属后台' name='yzAdminTypeId' v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzAdminTypeIdReadOnly'>{{readonlyDisplayText(entity.yzAdminTypeName)}}</text><template v-if='!yzAdminTypeIdReadOnly'><zxz-uni-data-select v-model='entity.yzAdminTypeId' :localdata='yz_admin_type_list' filterable :disabled='yzAdminTypeIdReadOnly' dataKey='name' dataValue='id' @change='yz_admin_typeSelectChange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>


    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>--></view></view>
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
      page_sn: '2062361618137833473',
      parent_page_sn: '',
      formParametersName: 'formParameters2062361618137833473',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,


      yz_admin_type_list: [],
      entity: {
        name:'',pagePath:'',navigationStyle:'',enablePullDownRefresh:'',isShowLeft:'',isShowTop:'',sortNo:99,yzAdminTypeId:'',yzAdminTypeName:'',
      },
      entity_column_readonly: {
        name:false,pagePath:false,navigationStyle:false,enablePullDownRefresh:false,isShowLeft:false,isShowTop:false,sortNo:false,yzAdminTypeId:false,
      },
      entity_rules: {
        name: {  rules: [  {      required: true,     errorMessage: '页面名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},pagePath: {  rules: [  {      required: true,     errorMessage: '页面路径为必填项',  },  {      maxLength: 200,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},navigationStyle: {  rules: [  {      maxLength: 20,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},enablePullDownRefresh: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isShowLeft: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isShowTop: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},sortNo: {  rules: [  {      required: true,     errorMessage: '排序号为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},pagePathReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.pagePath;},navigationStyleReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.navigationStyle;},enablePullDownRefreshReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.enablePullDownRefresh;},isShowLeftReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isShowLeft;},isShowTopReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isShowTop;},sortNoReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.sortNo;},
    yzAdminTypeIdReadOnly(){ return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId; },
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
    initPage(e) {
      var that = this;
      // 功能表没有物理业务表，不能把菜单或 URL 参数当作实体 ID 使用。
      this.id = this.page_is_function_table ? '' : e.id;
      this.parent_id = e.parent_id;
      this.parent_page_sn = e.parent_page_sn;

      //全局只读
      if (getApp().globalData.readOnly) {
        this.page_is_update = false
      }

      //初始化成对象，不然会当字符串处理，会报错
      var formParameters = uni.getStorageSync(this.formParametersName) || {};
      formParameters.configCustomPage = {};
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
    showCustomerButtons: function() {var that = this;var actions = [];if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},



    /** List选择回调同时维护标识与展示名，清空时不遗留旧后台名称。 */
    yz_admin_typeSelectChange: function(item) {
      this.entity.yzAdminTypeId = item ? item.id : '';
      this.entity.yzAdminTypeName = item ? item.name : '';
    },
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      // 由后台归属列的生成候选入口限定当前项目，保留原formParameters载荷。
      yzp.request({
        url: '/admin/auto/config_custom_page/2062361618137833473/findNameIdYzAdminType2200000000000101010',
        data: { formParameters: uni.getStorageSync(that.formParametersName) },
        sendSessionId: true,
        method: 'POST',
        success: function(res) {
          if (res.code == 200) that.yz_admin_type_list = res.data || [];
          else uni.showModal({ showCancel: false, content: res.msg });
        }
      });
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
        url: '/admin/auto/config_custom_page/2062361618137833473/detail/' + that.id + that.buildParentIdQuery(),
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

        yzp.request({
          url: '/admin/auto/config_custom_page/2062361618137833473/' + method + that.buildParentIdQuery(),
          data: that.entity,
          sendSessionId: true,
          method: 'POST',
          success: function (res) {
            if (res.code == 200) {
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
              url: '/admin/auto/config_custom_page/2062361618137833473' + that.buildParentIdQuery(),
              method: 'DELETE',
              data: that.entity,
              sendSessionId: true,
              success: function (res) {
                if (res.code == 200) {
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