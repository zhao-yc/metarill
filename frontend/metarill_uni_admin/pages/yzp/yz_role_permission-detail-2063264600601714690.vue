<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="角色权限"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="角色权限"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='二级菜单' name='configSecondMenuId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='configSecondMenuIdReadOnly'>{{readonlyDisplayText(entity.configSecondMenuName)}}</text><template v-if='!configSecondMenuIdReadOnly'><zxz-uni-data-select v-model='entity.configSecondMenuId' :localdata='config_second_menu_list' filterable :disabled='configSecondMenuIdReadOnly' dataKey='name' dataValue='id' @change='config_second_menuSelectChange' /></template>
</uni-forms-item>

<uni-forms-item label='角色' name='yzRoleId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzRoleIdReadOnly'>{{readonlyDisplayText(entity.yzRoleName)}}</text><template v-if='!yzRoleIdReadOnly'><zxz-uni-data-select v-model='entity.yzRoleId' :localdata='yz_role_list' filterable :disabled='yzRoleIdReadOnly' dataKey='name' dataValue='id' @change='yz_roleSelectChange'  remote :key="'original-options-2063264600941453314-'+originalRemoteOptions['2063264600941453314'].revision" :remote-selected="originalRemoteOptions['2063264600941453314'].selected" :remote-loading="originalRemoteOptions['2063264600941453314'].loading" :remote-has-more="originalRemoteOptions['2063264600941453314'].hasMore" @open="openOriginalRemoteOptions('2063264600941453314')" @inputChange="searchOriginalRemoteOptions('2063264600941453314',$event)" @loadMore="moreOriginalRemoteOptions('2063264600941453314')"/></template>
</uni-forms-item>

<uni-forms-item label='权限值' name='permissionValues'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!permissionValuesReadOnly' v-model='entity.permissionValues' maxlength='max'  />
<text v-if='permissionValuesReadOnly'>{{entity.permissionValues}}</text>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>


    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>--></view></view>
  </view>
</template>
<script>
import { createOriginalRemoteOptionsMixin } from '@/common/yzp_runtime/originalRemoteOptionsMixin'; // 元数据同步的原List接线

import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";

export default {
  // 原List分页由当前元数据同步；不覆盖原页面与事件。
  mixins:[createOriginalRemoteOptionsMixin([{"id":"2063264600941453314","field":"yzRoleId","list":"yz_role_list","endpoint":"/admin/auto/yz_role_permission/2063264600601714690/findNameIdYzRole2063264600941453314","requireParent":false,"requireTargetParent":false,"sameParent":false,"targetParentField":null,"readonlyField":"yzRoleIdReadOnly"}],"entity",{originalHost:true})],
  components: {

  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      page_sn: '2063264600601714690',
      parent_page_sn: '',
      formParametersName: 'formParameters2063264600601714690',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,
      config_second_menu_list:[],yz_role_list:[],

      entity: {
        configSecondMenuId:'',yzRoleId:'',permissionValues:'',
      },
      entity_column_readonly: {
        configSecondMenuId:false,yzRoleId:false,permissionValues:false,
      },
      entity_rules: {
        configSecondMenuId: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},yzRoleId: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},permissionValues: {  rules: [  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},configSecondMenuIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.configSecondMenuId;},yzRoleIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzRoleId;},permissionValuesReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.permissionValues;},
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
      formParameters.yzRolePermission = {};
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


    config_second_menuSelectChange: function(item) {this.entity.configSecondMenuId = item ? item.id : '';this.entity.configSecondMenuName = item ? item.name : '';},yz_roleSelectChange: function(item) {this.entity.yzRoleId = item ? item.id : '';this.entity.yzRoleName = item ? item.name : '';},
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      yzp.request({    url: '/admin/auto/yz_role_permission/2063264600601714690/findNameIdConfigSecondMenu2063264600681406465',    data: {       formParameters: uni.getStorageSync(that.formParametersName)    },    sendSessionId: true,    method: "POST",    success: function (res) {        if (res.code == 200) {            that.config_second_menu_list = res.data || [];        } else {            uni.showModal({                showCancel: false,                content: res.msg            })        }    },});that.loadOriginalRemoteOptions("2063264600941453314");
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
        url: '/admin/auto/yz_role_permission/2063264600601714690/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/yz_role_permission/2063264600601714690/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/yz_role_permission/2063264600601714690' + that.buildParentIdQuery(),
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