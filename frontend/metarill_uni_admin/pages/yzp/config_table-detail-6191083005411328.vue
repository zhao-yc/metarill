<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="表" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''" ></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="表"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='id' name='id'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!idReadOnly' v-model='entity.id' maxlength='50'  />
<text v-if='idReadOnly'>{{entity.id}}</text>
</uni-forms-item>

<uni-forms-item label='后台类型' name='yzAdminTypeId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzAdminTypeIdReadOnly'>{{readonlyDisplayText(entity.yzAdminTypeName)}}</text><template v-if='!yzAdminTypeIdReadOnly'><zxz-uni-data-select v-model='entity.yzAdminTypeId' :localdata='yz_admin_type_list' filterable :disabled='yzAdminTypeIdReadOnly' dataKey='name' dataValue='id' @change='yz_admin_typeSelectChange' /></template>
</uni-forms-item>

<uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='表名' name='tableName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!tableNameReadOnly' v-model='entity.tableName' maxlength='50'  />
<text v-if='tableNameReadOnly'>{{entity.tableName}}</text>
</uni-forms-item>

<uni-forms-item label='存储模式' name='isRongyu' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<yzp-table-storage-mode :entity="entity" :record-id="id" :readonly="isRongyuReadOnly" @receive="receive1671029182696714241"></yzp-table-storage-mode>

</uni-forms-item>

<uni-forms-item label='功能表' name='isFunctionTable' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isFunctionTableReadOnly'>{{(($checkboxRange || [])[entity.isFunctionTable==null?0:entity.isFunctionTable] || {}).text || ''}}</text><template v-if='!isFunctionTableReadOnly'><uni-data-checkbox :disabled='isFunctionTableReadOnly' v-model='entity.isFunctionTable' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='数据库视图' name='isView' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isViewReadOnly'>{{(($checkboxRange || [])[entity.isView==null?0:entity.isView] || {}).text || ''}}</text><template v-if='!isViewReadOnly'><uni-data-checkbox :disabled='isViewReadOnly' v-model='entity.isView' :localdata='$checkboxRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>
      <view v-if="!is_props" class='bottom-buttons is-before-child' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>-->
<button size='mini' type='default' v-if='customerButtonShow1676828142707372033' @click='customerButtonHandler1676828142707372033'>列排序</button>
<button size='mini' type='default' v-if='customerButtonShow2076978804953321472' @click='customerButtonHandler2076978804953321472'>索引管理</button>
<button size='mini' type='default' v-if='customerButtonShow1861242035055382529' @click='customerButtonHandler1861242035055382529'>一键生成</button>
<button size='mini' type='default' v-if='customerButtonShow2062356006758912000' @click='customerButtonHandler2062356006758912000'>添加联接字段</button>
<button size='mini' type='default' v-if='customerButtonShow1676850201126719490' @click='customerButtonHandler1676850201126719490'>批量创建字段</button>
<button size='mini' type='default' v-if='customerButtonShow1696359699042168834' @click='customerButtonHandler1696359699042168834'>在数据库创建表</button>
<button size='mini' type='default' v-if='customerButtonShow1696795154426523650' @click='customerButtonHandler1696795154426523650'>在数据库删除表</button>
<button size='mini' type='default' v-if='customerButtonShow1697063177846710274' @click='customerButtonHandler1697063177846710274'>创建视图</button></view>
    </view>
    <view v-if="is_props"><view class='bottom-buttons is-before-child' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>-->
<button size='mini' type='default' v-if='customerButtonShow1676828142707372033' @click='customerButtonHandler1676828142707372033'>列排序</button>
<button size='mini' type='default' v-if='customerButtonShow2076978804953321472' @click='customerButtonHandler2076978804953321472'>索引管理</button>
<button size='mini' type='default' v-if='customerButtonShow1861242035055382529' @click='customerButtonHandler1861242035055382529'>一键生成</button>
<button size='mini' type='default' v-if='customerButtonShow2062356006758912000' @click='customerButtonHandler2062356006758912000'>添加联接字段</button>
<button size='mini' type='default' v-if='customerButtonShow1676850201126719490' @click='customerButtonHandler1676850201126719490'>批量创建字段</button>
<button size='mini' type='default' v-if='customerButtonShow1696359699042168834' @click='customerButtonHandler1696359699042168834'>在数据库创建表</button>
<button size='mini' type='default' v-if='customerButtonShow1696795154426523650' @click='customerButtonHandler1696795154426523650'>在数据库删除表</button>
<button size='mini' type='default' v-if='customerButtonShow1697063177846710274' @click='customerButtonHandler1697063177846710274'>创建视图</button></view></view>
        <config_page_1868973685063311361 :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='config_page_1868973685063311361'></config_page_1868973685063311361>        <config_column_6217715532627968 :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='config_column_6217715532627968'></config_column_6217715532627968>

  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";
import config_page_1868973685063311361 from '@/pages/yzp/config_page-list-1868973685063311361.vue';import config_column_6217715532627968 from '@/pages/yzp/config_column-list-6217715532627968.vue';
export default {
  components: {
    config_page_1868973685063311361,config_column_6217715532627968,
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      page_sn: '6191083005411328',
      parent_page_sn: '',
      formParametersName: 'formParameters6191083005411328',
      yz_admin_type_list: [],
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,


      entity: {
        id:'',yzAdminTypeId:'',name:'',tableName:'',isRongyu:1,isFunctionTable:0,isView:0,
      },
      entity_column_readonly: {
        id:true,yzAdminTypeId:false,name:false,tableName:false,isRongyu:false,isFunctionTable:false,isView:false,
      },
      entity_rules: {
        id: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},yzAdminTypeId: {  rules: [  {      required: true,     errorMessage: '后台类型为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},name: {  rules: [  {      required: true,     errorMessage: '名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},tableName: {  rules: [  {      required: true,     errorMessage: '表名为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isRongyu: {  rules: [  {      required: true,     errorMessage: '存储模式为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isFunctionTable: {  rules: [  {      required: true,     errorMessage: '功能表为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isView: {  rules: [  {      required: true,     errorMessage: '数据库视图为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},idReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.id;},yzAdminTypeIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},tableNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.tableName;},isRongyuReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isRongyu;},isFunctionTableReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isFunctionTable;},isViewReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isView;},
    customerButtonShow1676828142707372033() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1676828142707372033",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow2076978804953321472() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2076978804953321472",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1861242035055382529() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1861242035055382529",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow2062356006758912000() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2062356006758912000",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1676850201126719490() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1676850201126719490",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1696359699042168834() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1696359699042168834",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1696795154426523650() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1696795154426523650",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1697063177846710274() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1697063177846710274",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},hasVisibleCustomerButtons() {this.permissionVersion;return this.customerButtonShow1676828142707372033 || this.customerButtonShow2076978804953321472 || this.customerButtonShow1861242035055382529 || this.customerButtonShow2062356006758912000 || this.customerButtonShow1676850201126719490 || this.customerButtonShow1696359699042168834 || this.customerButtonShow1696795154426523650 || this.customerButtonShow1697063177846710274;},
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
      formParameters.configTable = {};
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
    receive1671029182696714241(newEntity) {    this.entity = newEntity;},customerButtonHandler1676828142707372033: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1676828142707372033", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler2076978804953321472: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2076978804953321472", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1861242035055382529: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1861242035055382529", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler2062356006758912000: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2062356006758912000", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1676850201126719490: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1676850201126719490", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1696359699042168834: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1696359699042168834", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1696795154426523650: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1696795154426523650", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1697063177846710274: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1697063177846710274", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},showCustomerButtons: function() {var that = this;var actions = [];if (that.customerButtonShow1676828142707372033) {actions.push({name: "列排序", handler: function() { that.customerButtonHandler1676828142707372033(); }});}if (that.customerButtonShow2076978804953321472) {actions.push({name: "索引管理", handler: function() { that.customerButtonHandler2076978804953321472(); }});}if (that.customerButtonShow1861242035055382529) {actions.push({name: "一键生成", handler: function() { that.customerButtonHandler1861242035055382529(); }});}if (that.customerButtonShow2062356006758912000) {actions.push({name: "添加联接字段", handler: function() { that.customerButtonHandler2062356006758912000(); }});}if (that.customerButtonShow1676850201126719490) {actions.push({name: "批量创建字段", handler: function() { that.customerButtonHandler1676850201126719490(); }});}if (that.customerButtonShow1696359699042168834) {actions.push({name: "在数据库创建表", handler: function() { that.customerButtonHandler1696359699042168834(); }});}if (that.customerButtonShow1696795154426523650) {actions.push({name: "在数据库删除表", handler: function() { that.customerButtonHandler1696795154426523650(); }});}if (that.customerButtonShow1697063177846710274) {actions.push({name: "创建视图", handler: function() { that.customerButtonHandler1697063177846710274(); }});}if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},


    /** 后台归属只写ID与展示名，候选范围由服务端按当前项目校验。 */
    yz_admin_typeSelectChange(item) { this.entity.yzAdminTypeId=item ? item.id : ''; this.entity.yzAdminTypeName=item ? item.name : ''; },
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      // 候选路径按当前元数据列ID生成，页面不再依赖旧菜单选择页。
      yzp.request({ url: '/admin/auto/config_table/6191083005411328/findNameIdYzAdminType2200000000000000087', method: 'POST', sendSessionId: true,
        data: { formParameters: uni.getStorageSync(that.formParametersName) },
        success(res) { if (res.code == 200) that.yz_admin_type_list=res.data || []; else uni.showModal({ showCancel: false, content: res.msg }); }
      });

    },
    refreshSonsData: function () {
      // this.getEntity();
      this.$refs.config_page_1868973685063311361.refreshData();this.$refs.config_column_6217715532627968.refreshData();
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
        url: '/admin/auto/config_table/6191083005411328/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/config_table/6191083005411328/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_table/6191083005411328' + that.buildParentIdQuery(),
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