<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="管理员"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="管理员"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='用户名' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='角色' name='yzRoleIds'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzRoleIdsReadOnly'>{{readonlyDisplayText(entity.yzRoleNames)}}</text><template v-if='!yzRoleIdsReadOnly'><view><uni-data-checkbox multiple v-model="yzRoleIdsSelectAll" :disabled="yzRoleIdsReadOnly" :localdata="$multiCheckboxRange" @change="yzRoleIdsSelectAllChange"></uni-data-checkbox></view><uni-data-checkbox multiple v-model="entity.yzRoleIds" :disabled="yzRoleIdsReadOnly" :localdata="yzRoleIdsList" @change="yzRoleIdsChange"></uni-data-checkbox></template><uni-easyinput style='display: none;' v-model='entity.yzRoleIds' />

</uni-forms-item>

<uni-forms-item label='密码' name='password' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!passwordReadOnly' v-model='entity.password' maxlength='50'  />
<text v-if='passwordReadOnly'>{{entity.password}}</text>
</uni-forms-item>

<uni-forms-item label='真实姓名' name='realName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!realNameReadOnly' v-model='entity.realName' maxlength='50'  />
<text v-if='realNameReadOnly'>{{entity.realName}}</text>
</uni-forms-item>

<uni-forms-item label='手机号' name='mobile'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!mobileReadOnly' v-model='entity.mobile' maxlength='50'  />
<text v-if='mobileReadOnly'>{{entity.mobile}}</text>
</uni-forms-item>

<uni-forms-item label='锁定ip' name='lockIp'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!lockIpReadOnly' v-model='entity.lockIp' maxlength='50'  />
<text v-if='lockIpReadOnly'>{{entity.lockIp}}</text>
</uni-forms-item>

<uni-forms-item label='登录时间' name='lastLoginTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!lastLoginTimeReadOnly' v-model='entity.lastLoginTime' /><text v-if='lastLoginTimeReadOnly'>{{entity.lastLoginTime}}</text>
</uni-forms-item>

<uni-forms-item label='登录ip' name='loginIp'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!loginIpReadOnly' v-model='entity.loginIp' maxlength='50'  />
<text v-if='loginIpReadOnly'>{{entity.loginIp}}</text>
</uni-forms-item>

<uni-forms-item label='邮箱' name='email'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!emailReadOnly' v-model='entity.email' maxlength='50'  />
<text v-if='emailReadOnly'>{{entity.email}}</text>
</uni-forms-item>

<uni-forms-item label='电话' name='phone'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!phoneReadOnly' v-model='entity.phone' maxlength='50'  />
<text v-if='phoneReadOnly'>{{entity.phone}}</text>
</uni-forms-item>

<uni-forms-item label='启用' name='isDisabled'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isDisabledReadOnly'>{{(($checkboxRange || [])[entity.isDisabled==null?0:entity.isDisabled] || {}).text || ''}}</text><template v-if='!isDisabledReadOnly'><uni-data-checkbox :disabled='isDisabledReadOnly' v-model='entity.isDisabled' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='密钥' name='salt'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!saltReadOnly' v-model='entity.salt' maxlength='50'  />
<text v-if='saltReadOnly'>{{entity.salt}}</text>
</uni-forms-item>

<uni-forms-item label='旧密码' name='oldPassword'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!oldPasswordReadOnly' v-model='entity.oldPassword' maxlength='50'  />
<text v-if='oldPasswordReadOnly'>{{entity.oldPassword}}</text>
</uni-forms-item>

<uni-forms-item label='备注' name='notes'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!notesReadOnly' v-model='entity.notes' maxlength='50'  />
<text v-if='notesReadOnly'>{{entity.notes}}</text>
</uni-forms-item>

<uni-forms-item label='已修改密码' name='isEditPwd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isEditPwdReadOnly'>{{(($checkboxRange || [])[entity.isEditPwd==null?0:entity.isEditPwd] || {}).text || ''}}</text><template v-if='!isEditPwdReadOnly'><uni-data-checkbox :disabled='isEditPwdReadOnly' v-model='entity.isEditPwd' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='修改密码时间' name='editPwdTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!editPwdTimeReadOnly' v-model='entity.editPwdTime' /><text v-if='editPwdTimeReadOnly'>{{entity.editPwdTime}}</text>
</uni-forms-item>

<uni-forms-item label='openid' name='openid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!openidReadOnly' v-model='entity.openid' maxlength='50'  />
<text v-if='openidReadOnly'>{{entity.openid}}</text>
</uni-forms-item>

<uni-forms-item label='头像' name='avatar'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<image class='internet-img' mode='widthFix' :src='entity.avatar'></image>
</uni-forms-item>

<uni-forms-item label='钉钉用户ID' name='dingUserid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!dingUseridReadOnly' v-model='entity.dingUserid' maxlength='50'  />
<text v-if='dingUseridReadOnly'>{{entity.dingUserid}}</text>
</uni-forms-item>

<uni-forms-item label='登录时间起' name='loginRiseTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!loginRiseTimeReadOnly' v-model='entity.loginRiseTime' /><text v-if='loginRiseTimeReadOnly'>{{entity.loginRiseTime}}</text>
</uni-forms-item>

<uni-forms-item label='钉钉unionid' name='dingUnionid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!dingUnionidReadOnly' v-model='entity.dingUnionid' maxlength='50'  />
<text v-if='dingUnionidReadOnly'>{{entity.dingUnionid}}</text>
</uni-forms-item>

<uni-forms-item label='登录时间止' name='loginStopTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!loginStopTimeReadOnly' v-model='entity.loginStopTime' /><text v-if='loginStopTimeReadOnly'>{{entity.loginStopTime}}</text>
</uni-forms-item>

<uni-forms-item label='有效期' name='validityTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!validityTimeReadOnly' v-model='entity.validityTime' /><text v-if='validityTimeReadOnly'>{{entity.validityTime}}</text>
</uni-forms-item>

<uni-forms-item label='注册时间' name='insertTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!insertTimeReadOnly' v-model='entity.insertTime' /><text v-if='insertTimeReadOnly'>{{entity.insertTime}}</text>
</uni-forms-item>

<uni-forms-item label='后台类型' name='yzAdminTypeId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
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
      page_sn: '6031906822946816',
      parent_page_sn: '',
      formParametersName: 'formParameters6031906822946816',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,
      yzRoleIdsList:[],yzRoleIdsSelectAll: [],yz_admin_type_list:[],

      entity: {
        name:'',yzRoleIds:[],password:'',realName:'',mobile:'',lockIp:'',lastLoginTime:'',loginIp:'',email:'',phone:'',isDisabled:1,salt:'',oldPassword:'',notes:'',isEditPwd:1,editPwdTime:'',openid:'',avatar:'',dingUserid:'null',loginRiseTime:'',dingUnionid:'null',loginStopTime:'',validityTime:'',insertTime:'',yzAdminTypeId:'',
      },
      entity_column_readonly: {
        name:false,yzRoleIds:false,password:false,realName:false,mobile:false,lockIp:false,lastLoginTime:false,loginIp:false,email:false,phone:false,isDisabled:false,salt:false,oldPassword:false,notes:false,isEditPwd:false,editPwdTime:false,openid:false,avatar:false,dingUserid:false,loginRiseTime:false,dingUnionid:false,loginStopTime:false,validityTime:false,insertTime:true,yzAdminTypeId:false,
      },
      entity_rules: {
        name: {  rules: [  {      required: true,     errorMessage: '用户名为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},yzRoleIds: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},password: {  rules: [  {      required: true,     errorMessage: '密码为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},realName: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},mobile: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},lockIp: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},lastLoginTime: {  rules: [  ]},loginIp: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},email: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},phone: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isDisabled: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},salt: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},oldPassword: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},notes: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isEditPwd: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},editPwdTime: {  rules: [  ]},openid: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},avatar: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},dingUserid: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},loginRiseTime: {  rules: [  ]},dingUnionid: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},loginStopTime: {  rules: [  ]},validityTime: {  rules: [  ]},insertTime: {  rules: [  ]},yzAdminTypeId: {  rules: [  {      required: true,     errorMessage: '后台类型为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},yzRoleIdsReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzRoleIds;},passwordReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.password;},realNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.realName;},mobileReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.mobile;},lockIpReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.lockIp;},lastLoginTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.lastLoginTime;},loginIpReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.loginIp;},emailReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.email;},phoneReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.phone;},isDisabledReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isDisabled;},saltReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.salt;},oldPasswordReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.oldPassword;},notesReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.notes;},isEditPwdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isEditPwd;},editPwdTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.editPwdTime;},openidReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.openid;},avatarReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.avatar;},dingUseridReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dingUserid;},loginRiseTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.loginRiseTime;},dingUnionidReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dingUnionid;},loginStopTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.loginStopTime;},validityTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.validityTime;},insertTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.insertTime;},yzAdminTypeIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId;},
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
      formParameters.yzManager = {};
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


    yzRoleIdsSelectAllChange: function(e) {    var that = this;    this.entity.yzRoleIds = [];if (this.yzRoleIdsSelectAll == 1) {this.yzRoleIdsList.forEach(function(item, index) {	    that.entity.yzRoleIds.push(item.id);})}},yzRoleIdsChange: function(e) {var tempAry = [];for (var i = 0; i < this.yzRoleIdsList.length; i++) {    tempAry.push(this.yzRoleIdsList[i].id);}if (tempAry.slice().sort().join() == yzp.relationArray(this.entity.yzRoleIds).sort().join()) {    this.yzRoleIdsSelectAll = [1];} else {    this.yzRoleIdsSelectAll = [];}},yz_admin_typeSelectChange: function(item) {this.entity.yzAdminTypeId = item ? item.id : '';this.entity.yzAdminTypeName = item ? item.name : '';},
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      yzp.request({    url: '/admin/auto/yz_manager/6031906822946816/findNameIdYzRole1692528723801219073',    data: {       formParameters: uni.getStorageSync(that.formParametersName)    },    sendSessionId: true,    method: "POST",    success: function (res) {        if (res.code == 200) {            that.yzRoleIdsList = res.data;            that.yzRoleIdsChange();        } else {            uni.showModal({                showCancel: false,                content: res.msg            })        }    },});yzp.request({    url: '/admin/auto/yz_manager/6031906822946816/findNameIdYzAdminType1679698329655750657',    data: {       formParameters: uni.getStorageSync(that.formParametersName)    },    sendSessionId: true,    method: "POST",    success: function (res) {        if (res.code == 200) {            that.yz_admin_type_list = res.data || [];        } else {            uni.showModal({                showCancel: false,                content: res.msg            })        }    },});
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
        url: '/admin/auto/yz_manager/6031906822946816/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/yz_manager/6031906822946816/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/yz_manager/6031906822946816' + that.buildParentIdQuery(),
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