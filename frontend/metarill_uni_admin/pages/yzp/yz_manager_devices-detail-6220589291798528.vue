<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="管理员信息扩展" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''" ></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="管理员信息扩展"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='管理员' name='yzManagerId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzManagerIdReadOnly'>{{readonlyDisplayText(entity.yzManagerName)}}</text><template v-if='!yzManagerIdReadOnly'><zxz-uni-data-select v-model='entity.yzManagerId' :localdata='yz_manager_list' filterable :disabled='yzManagerIdReadOnly' dataKey='name' dataValue='id' @change='yz_managerSelectChange'  remote :key="'original-options-1679383180512325633-'+originalRemoteOptions['1679383180512325633'].revision" :remote-selected="originalRemoteOptions['1679383180512325633'].selected" :remote-loading="originalRemoteOptions['1679383180512325633'].loading" :remote-has-more="originalRemoteOptions['1679383180512325633'].hasMore" @open="openOriginalRemoteOptions('1679383180512325633')" @inputChange="searchOriginalRemoteOptions('1679383180512325633',$event)" @loadMore="moreOriginalRemoteOptions('1679383180512325633')"/></template>
</uni-forms-item>

<uni-forms-item label='名称' name='name'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='id' name='id'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!idReadOnly' v-model='entity.id' maxlength='50'  />
<text v-if='idReadOnly'>{{entity.id}}</text>
</uni-forms-item>

<uni-forms-item label='IsMac' name='isMac'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isMacReadOnly'>{{(($checkboxRange || [])[entity.isMac==null?0:entity.isMac] || {}).text || ''}}</text><template v-if='!isMacReadOnly'><uni-data-checkbox :disabled='isMacReadOnly' v-model='entity.isMac' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='Token' name='token'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!tokenReadOnly' v-model='entity.token' maxlength='500'  />
<text v-if='tokenReadOnly'>{{entity.token}}</text>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>

            <yz_manager_devices_project_6220608155484160 :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='yz_manager_devices_project_6220608155484160'></yz_manager_devices_project_6220608155484160>
    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>-->
<button size='mini' type='primary' v-if='customerButtonShow2034487360028282880' @click='customerButtonHandler2034487360028282880'>生成或重置 Token</button>
<button size='mini' type='warn' v-if='customerButtonShow2034487360028282881' @click='customerButtonHandler2034487360028282881'>停用 Token</button></view></view>
  </view>
</template>
<script>
import { createOriginalRemoteOptionsMixin } from '@/common/yzp_runtime/originalRemoteOptionsMixin'; // 元数据同步的原List接线

import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";
import yz_manager_devices_project_6220608155484160 from '@/pages/yzp/yz_manager_devices_project-list-6220608155484160.vue';
export default {
  // 原List分页由当前元数据同步；不覆盖原页面与事件。
  mixins:[createOriginalRemoteOptionsMixin([{"id":"1679383180512325633","field":"yzManagerId","list":"yz_manager_list","endpoint":"/admin/auto/yz_manager_devices/6220589291798528/findNameIdYzManager1679383180512325633","requireParent":false,"requireTargetParent":false,"sameParent":false,"targetParentField":null,"readonlyField":"yzManagerIdReadOnly"}],"entity",{originalHost:true})],
  components: {
    yz_manager_devices_project_6220608155484160,
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      page_sn: '6220589291798528',
      parent_page_sn: '',
      formParametersName: 'formParameters6220589291798528',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,
      yz_manager_list:[],

      entity: {
        yzManagerId:'',name:'',id:'',isMac:'',token:'',
      },
      entity_column_readonly: {
        yzManagerId:false,name:false,id:true,isMac:false,token:true,
      },
      entity_rules: {
        yzManagerId: {  rules: [  {      required: true,     errorMessage: '管理员为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},name: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},id: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isMac: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},token: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},yzManagerIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzManagerId;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},idReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.id;},isMacReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isMac;},tokenReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.token;},
    customerButtonShow2034487360028282880() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2034487360028282880",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow2034487360028282881() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2034487360028282881",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},hasVisibleCustomerButtons() {this.permissionVersion;return this.customerButtonShow2034487360028282880 || this.customerButtonShow2034487360028282881;},
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
      formParameters.yzManagerDevices = {};
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
    customerButtonHandler2034487360028282880: function() {var that = this;uni.showModal({content: "继续后旧 Token 会立即失效，是否继续？",success(modal) {if (modal.confirm) {yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2034487360028282880", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})}}})},customerButtonHandler2034487360028282881: function() {var that = this;uni.showModal({content: "停用后当前设备的 MCP 连接会失效，是否继续？",success(modal) {if (modal.confirm) {yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2034487360028282881", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})}}})},showCustomerButtons: function() {var that = this;var actions = [];if (that.customerButtonShow2034487360028282880) {actions.push({name: "生成或重置 Token", handler: function() { that.customerButtonHandler2034487360028282880(); }});}if (that.customerButtonShow2034487360028282881) {actions.push({name: "停用 Token", handler: function() { that.customerButtonHandler2034487360028282881(); }});}if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},


    yz_managerSelectChange: function(item) {this.entity.yzManagerId = item ? item.id : '';this.entity.yzManagerName = item ? item.name : '';},
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      that.loadOriginalRemoteOptions("1679383180512325633");
    },
    refreshSonsData: function () {
      // this.getEntity();
      this.$refs.yz_manager_devices_project_6220608155484160.refreshData();
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
        url: '/admin/auto/yz_manager_devices/6220589291798528/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/yz_manager_devices/6220589291798528/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/yz_manager_devices/6220589291798528' + that.buildParentIdQuery(),
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