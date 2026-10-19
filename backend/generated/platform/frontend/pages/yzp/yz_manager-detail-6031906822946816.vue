<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="管理员" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="管理员"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='用户名' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'   /><text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>
<uni-forms-item label='角色' name='yzRoleIds'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='yzRoleIdsReadOnly'>{{readonlyDisplayText(entity.yzRoleIds)}}</text><template v-if='!yzRoleIdsReadOnly'><view><uni-data-checkbox multiple v-model='yzRoleIdsSelectAll' :disabled='yzRoleIdsReadOnly' :localdata='$multiCheckboxRange' @change='yzRoleIdsSelectAllChange' /></view><uni-data-checkbox multiple v-model='entity.yzRoleIds' :disabled='yzRoleIdsReadOnly' :localdata='yzRoleIdsList' @change='yzRoleIdsChange' /></template><uni-easyinput style='display: none;' v-model='entity.yzRoleIds' maxlength='500' />
</uni-forms-item>
<uni-forms-item label='真实姓名' name='realName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!realNameReadOnly' v-model='entity.realName' maxlength='50'   /><text v-if='realNameReadOnly'>{{entity.realName}}</text>
</uni-forms-item>
<uni-forms-item label='手机号' name='mobile'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!mobileReadOnly' v-model='entity.mobile' maxlength='50'   /><text v-if='mobileReadOnly'>{{entity.mobile}}</text>
</uni-forms-item>
<uni-forms-item label='锁定ip' name='lockIp'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!lockIpReadOnly' v-model='entity.lockIp' maxlength='50'   /><text v-if='lockIpReadOnly'>{{entity.lockIp}}</text>
</uni-forms-item>
<uni-forms-item label='登录时间' name='lastLoginTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!lastLoginTimeReadOnly' v-model='entity.lastLoginTime' /><text v-if='lastLoginTimeReadOnly'>{{entity.lastLoginTime}}</text>
</uni-forms-item>
<uni-forms-item label='登录ip' name='loginIp'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!loginIpReadOnly' v-model='entity.loginIp' maxlength='50'   /><text v-if='loginIpReadOnly'>{{entity.loginIp}}</text>
</uni-forms-item>
<uni-forms-item label='邮箱' name='email'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!emailReadOnly' v-model='entity.email' maxlength='50'   /><text v-if='emailReadOnly'>{{entity.email}}</text>
</uni-forms-item>
<uni-forms-item label='电话' name='phone'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!phoneReadOnly' v-model='entity.phone' maxlength='50'   /><text v-if='phoneReadOnly'>{{entity.phone}}</text>
</uni-forms-item>
<uni-forms-item label='启用' name='isDisabled'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isDisabledReadOnly'>{{(($checkboxRange || [])[entity.isDisabled==null?0:entity.isDisabled] || {}).text || ''}}</text><template v-if='!isDisabledReadOnly'><uni-data-checkbox :disabled='isDisabledReadOnly' v-model='entity.isDisabled' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='备注' name='notes'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!notesReadOnly' v-model='entity.notes' maxlength='50'   /><text v-if='notesReadOnly'>{{entity.notes}}</text>
</uni-forms-item>
<uni-forms-item label='已修改密码' name='isEditPwd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isEditPwdReadOnly'>{{(($checkboxRange || [])[entity.isEditPwd==null?0:entity.isEditPwd] || {}).text || ''}}</text><template v-if='!isEditPwdReadOnly'><uni-data-checkbox :disabled='isEditPwdReadOnly' v-model='entity.isEditPwd' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='修改密码时间' name='editPwdTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!editPwdTimeReadOnly' v-model='entity.editPwdTime' /><text v-if='editPwdTimeReadOnly'>{{entity.editPwdTime}}</text>
</uni-forms-item>
<uni-forms-item label='openid' name='openid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!openidReadOnly' v-model='entity.openid' maxlength='50'   /><text v-if='openidReadOnly'>{{entity.openid}}</text>
</uni-forms-item>
<uni-forms-item label='头像' name='avatar'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<image class='internet-img' mode='widthFix' :src='entity.avatar'></image>
</uni-forms-item>
<uni-forms-item label='钉钉用户ID' name='dingUserid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!dingUseridReadOnly' v-model='entity.dingUserid' maxlength='50'   /><text v-if='dingUseridReadOnly'>{{entity.dingUserid}}</text>
</uni-forms-item>
<uni-forms-item label='钉钉unionid' name='dingUnionid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!dingUnionidReadOnly' v-model='entity.dingUnionid' maxlength='50'   /><text v-if='dingUnionidReadOnly'>{{entity.dingUnionid}}</text>
</uni-forms-item>
<uni-forms-item label='登录时间起' name='loginRiseTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!loginRiseTimeReadOnly' v-model='entity.loginRiseTime' /><text v-if='loginRiseTimeReadOnly'>{{entity.loginRiseTime}}</text>
</uni-forms-item>
<uni-forms-item label='登录时间止' name='loginStopTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!loginStopTimeReadOnly' v-model='entity.loginStopTime' /><text v-if='loginStopTimeReadOnly'>{{entity.loginStopTime}}</text>
</uni-forms-item>
<uni-forms-item label='有效期' name='validityTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!validityTimeReadOnly' v-model='entity.validityTime' /><text v-if='validityTimeReadOnly'>{{entity.validityTime}}</text>
</uni-forms-item>
<uni-forms-item label='注册时间' name='insertTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!insertTimeReadOnly' v-model='entity.insertTime' /><text v-if='insertTimeReadOnly'>{{entity.insertTime}}</text>
</uni-forms-item>
<uni-forms-item label='后台类型' name='yzAdminTypeId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='yzAdminTypeIdReadOnly'>{{readonlyDisplayText(entity.yzAdminTypeId)}}</text><template v-if='!yzAdminTypeIdReadOnly'><zxz-uni-data-select v-model='entity.yzAdminTypeId' :localdata='yz_admin_type_list' filterable :disabled='yzAdminTypeIdReadOnly' dataKey='name' dataValue='id' @change='yzAdminTypeIdDetailChange' /></template>
</uni-forms-item>
<uni-forms-item label='项目' name='yzProjectId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectIdReadOnly' v-model='entity.yzProjectId'    /><text v-if='yzProjectIdReadOnly'>{{entity.yzProjectId}}</text>
</uni-forms-item>

        </uni-forms>
      </view>
      
    </view>
    
    
    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button></view></view>
  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";

const originalRecordId=value=>typeof value==='string' && !['null','undefined'].includes(value.trim())?value.trim():'';

export default {
  
  components: {
    
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      yzpMenuId: '',
      page_sn: '6031906822946816',
      parent_page_sn: '',
      formParametersName: 'formParameters6031906822946816',
      loadEntitySuccess: false,
      yzRoleIdsList:[],yzRoleIdsSelectAll:[],yz_admin_type_list:[],originalButtons:[],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:true,page_is_copy:false,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        name:"",yzRoleIds:[],realName:"",mobile:"",lockIp:"",lastLoginTime:"",loginIp:"",email:"",phone:"",isDisabled:1,notes:"",isEditPwd:1,editPwdTime:"",openid:"",avatar:"",dingUserid:"",dingUnionid:"",loginRiseTime:"",loginStopTime:"",validityTime:"",insertTime:"",yzAdminTypeId:"",yzProjectId:"",
      },
      entity_column_readonly: {
        name:false,yzRoleIds:false,realName:false,mobile:false,lockIp:false,lastLoginTime:false,loginIp:false,email:false,phone:false,isDisabled:false,notes:false,isEditPwd:false,editPwdTime:false,openid:false,avatar:false,dingUserid:false,dingUnionid:false,loginRiseTime:false,loginStopTime:false,validityTime:false,insertTime:true,yzAdminTypeId:false,yzProjectId:true,
      },
      entity_rules: {
        name:{rules:[{"required":true,"errorMessage":"\u7528\u6237\u540D\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzRoleIds:{rules:[{"maxLength":500,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},realName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},mobile:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},lockIp:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},lastLoginTime:{rules:[]},loginIp:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},email:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},phone:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isDisabled:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},notes:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isEditPwd:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},editPwdTime:{rules:[]},openid:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},avatar:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},dingUserid:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},dingUnionid:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},loginRiseTime:{rules:[]},loginStopTime:{rules:[]},validityTime:{rules:[]},insertTime:{rules:[]},yzAdminTypeId:{rules:[{"required":true,"errorMessage":"\u540E\u53F0\u7C7B\u578B\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},yzProjectId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    nameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.name; },
yzRoleIdsReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzRoleIds; },
realNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.realName; },
mobileReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.mobile; },
lockIpReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.lockIp; },
lastLoginTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.lastLoginTime; },
loginIpReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.loginIp; },
emailReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.email; },
phoneReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.phone; },
isDisabledReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isDisabled; },
notesReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.notes; },
isEditPwdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isEditPwd; },
editPwdTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editPwdTime; },
openidReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.openid; },
avatarReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.avatar; },
dingUseridReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.dingUserid; },
dingUnionidReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.dingUnionid; },
loginRiseTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.loginRiseTime; },
loginStopTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.loginStopTime; },
validityTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.validityTime; },
insertTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.insertTime; },
yzAdminTypeIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId; },
yzProjectIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectId; },
hasVisibleCustomerButtons() { this.permissionVersion;return this.originalButtons.some(button=>this['customerButtonShow'+button.id]); },
parentId() { return this.parent_id || ''; },
originalChildrenReady() { return this.recordReady && this.permissionsReady && Boolean(this.id) && originalRecordId(this.entity.id)===this.id; },
readOnlyCondition() { this.permissionVersion;return !this.permissionsReady || Boolean(this.id && !this.recordReady) || Boolean(this.parentReadOnlyCondition || this.routeReadOnly || getApp().globalData.readOnly || (this.page_is_function_table ? !this.page_is_update : this.id ? !this.page_is_update : !this.page_is_new)) || !yzp.hasPagePermission(this.page_sn,this.page_is_function_table || this.id ? 'is_update' : 'is_new'); },

    
  },
  created() {
    // 独立范围观察不覆盖原AutoPostBack/AutoSave字段watcher，也不改变控件布局。
    
  },
  onUnload() { this.disposeOriginalDetail(); },
  beforeUnmount() { this.disposeOriginalDetail(); },
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
    // 成功写入只失效关联资源的角标，普通详情读取不产生刷新事件。
    notifyMenuMutation() {
      uni.$emit('yzpMenuChanged', { pageId: '6031906822946816', tableName: 'yz_manager' });
    },
    initPage(e = {}) {
      this.beginOriginalDetail(e);
      var that = this;
      // 功能表没有物理业务表，不能把菜单或 URL 参数当作实体 ID 使用。
      this.id = this.page_is_function_table ? '' : originalRecordId(e.id);
      this.parent_id = originalRecordId(e.parent_id || e.parentId);
      this.parent_page_sn = originalRecordId(e.parent_page_sn || e.parentPageId);
      // 入口标识仅用于导航定位，不参与后端权限判定。
      this.yzpMenuId = e.yzpMenuId || '';

      //全局只读
      if (getApp().globalData.readOnly) {
        this.page_is_update = false
      }

      //初始化成对象，不然会当字符串处理，会报错
      var formParameters = uni.getStorageSync(this.formParametersName) || {};
      formParameters.yzManager = {};
      uni.setStorageSync(this.formParametersName, formParameters);

      // 先刷新权限快照，再加载详情和候选项，避免只读判断使用空权限快照。
      const current = this.originalContext();
      this.refreshCurrentPermissions(function() {
        if (!current()) return;
        that.permissionsReady = true;
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
    /** 页面/弹层进入使用同一实例边界，旧回调不能写到下一条记录。 */
beginOriginalDetail(options) {
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={name:"",yzRoleIds:[],realName:"",mobile:"",lockIp:"",lastLoginTime:"",loginIp:"",email:"",phone:"",isDisabled:1,notes:"",isEditPwd:1,editPwdTime:"",openid:"",avatar:"",dingUserid:"",dingUnionid:"",loginRiseTime:"",loginStopTime:"",validityTime:"",insertTime:"",yzAdminTypeId:"",yzProjectId:"",};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
},
/** 卸载只取消本详情的任务与订阅，不触发保存或父表刷新。 */
disposeOriginalDetail() {this._alive=false;this._detailEpoch=(this._detailEpoch||0)+1;(this._detailTasks||[]).forEach(task=>task?.abort?.());this._detailTasks=[];this.disposeMultiSelections?.();},
/** 当前身份用于迟到隔离，不能替代后端真实授权。 */
currentIdentity() {return JSON.stringify([uni.getStorageSync('loginStateId')||'',uni.getStorageSync('sysParams')||{}]);},
/** 原props及URL始终使用字符串ID，父/项目切换即使watcher未执行也失效。 */
originalContext() {const epoch=this._detailEpoch,id=this.id,parent=this.parent_id,identity=this.currentIdentity();return ()=>Boolean(this._alive&&epoch===this._detailEpoch&&id===this.id&&parent===this.parent_id&&identity===this.currentIdentity());},
/** 不裁剪、不补null；原整entity由固定页C#归一器兼容处理。 */
originalRequest(options) {
 const current=this.originalContext(),epoch=this._detailEpoch,parent=this.parent_id,identity=this.currentIdentity(),method=String(options.method||'GET').toUpperCase(),route=String(options.url||'').split('?')[0];
 const mutation=route==="/admin/auto/yz_manager/6031906822946816"&&method==='DELETE'||method==='POST'&&["/admin/auto/yz_manager/6031906822946816"+'/add',"/admin/auto/yz_manager/6031906822946816"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/yz_manager/6031906822946816"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/yz_manager/6031906822946816"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["name","yzRoleIds","realName","mobile","lockIp","lastLoginTime","loginIp","email","phone","isDisabled","notes","isEditPwd","editPwdTime","openid","avatar","dingUserid","dingUnionid","loginRiseTime","loginStopTime","validityTime","yzAdminTypeId"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/yz_manager/6031906822946816"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
/** 按原Save/NotSave、确认、固定接口或页面动作执行，不接受调用方改写动作。 */
runOriginalButton(id) {
 const button=this.originalButtons.find(item=>item.id===id);if(!button||!this['customerButtonShow'+id])return;const current=this.originalContext(),that=this;
 const execute=()=>{that.permissionVersion++;if(!current()||!that['customerButtonShow'+id])return;const action=()=>{
  if(button.action===1)that.originalRequest({url:'/admin/sys/config_button/execute',method:'POST',data:{buttonId:id,entityId:that.id,entity:that.entity},sendSessionId:true,success(res){if(res.code===200)that.notifyMenuMutation();yzp.customButtonHandler(res,that,button.close,'detail');}});
  else if(button.action===2){const handler=project['customerButtonHandler'+id];if(typeof handler!=='function')throw new Error('项目未提供原按钮扩展：'+id);uni.navigateTo({url:'/'+button.target,success(){if(that._alive)handler(that.entity);}});}
  else if(button.action===3){if(typeof window==='undefined')throw new Error('原新窗口按钮只支持Web端。');window.open(location.href.split('pages')[0]+button.target+'?id='+that.id,'_blank');}
 };if(button.save==='NotSave')action();else that.saveEntity(()=>{if(that._alive&&that.currentIdentity()===identity)action();});};
 const identity=this.currentIdentity();if(button.confirm)uni.showModal({content:button.confirmMsg||'您确定'+button.name+'吗？',success(modal){if(modal.confirm)execute();}});else execute();
},

    loadOriginalOptions1692528723801219073() {const that=this;that.yzRoleIdsList=[];(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["1692528723801219073"]||0)+1;that._originalOptions["1692528723801219073"]=optionSequence;const optionParent=null;that.originalRequest({url:"/admin/auto/yz_manager/6031906822946816/findNameIdYzRole1692528723801219073",isCurrent:()=>optionSequence===that._originalOptions["1692528723801219073"]&&true,data:{parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName),...(that.readOnlyCondition?{id:that.entity.yzRoleIds}:{})},method:'POST',sendSessionId:true,success(res){if(res.code===200){that.yzRoleIdsList=res.data||[];that.yzRoleIdsChange();}else uni.showModal({showCancel:false,content:res.msg});}});})();},
yzRoleIdsSelectAllChange() { if(this.yzRoleIdsReadOnly) return; this.entity.yzRoleIds=this.yzRoleIdsSelectAll==1 ? this.yzRoleIdsList.map(item=>item.id) : []; },
yzRoleIdsChange() { this.yzRoleIdsSelectAll=JSON.stringify(this.yzRoleIdsList.map(item=>String(item.id)).sort())===JSON.stringify(yzp.relationArray(this.entity.yzRoleIds).map(String).sort()) ? [1] : []; },
loadOriginalOptions1679698329655750657() {const that=this;that.yz_admin_type_list=[];(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["1679698329655750657"]||0)+1;that._originalOptions["1679698329655750657"]=optionSequence;const optionParent=null;that.originalRequest({url:"/admin/auto/yz_manager/6031906822946816/findNameIdYzAdminType1679698329655750657",isCurrent:()=>optionSequence===that._originalOptions["1679698329655750657"]&&true,data:{parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName),...(that.readOnlyCondition?{id:that.entity.yzAdminTypeId}:{})},method:'POST',sendSessionId:true,success(res){if(res.code===200){that.yz_admin_type_list=res.data||[];}else uni.showModal({showCancel:false,content:res.msg});}});})();},
yzAdminTypeIdDetailChange(item) { if(this.yzAdminTypeIdReadOnly) return; this.entity.yzAdminTypeId=item ? item.id : '';  },
showCustomerButtons() { const current=this.originalContext(),actions=this.originalButtons.filter(button=>this['customerButtonShow'+button.id]);if(!actions.length){uni.showToast({title:'暂无可用操作',icon:'none'});return;}uni.showActionSheet({itemList:actions.map(button=>button.name),success:res=>{if(current()&&actions[res.tapIndex])this['customerButtonHandler'+actions[res.tapIndex].id]();}}); },

    
    
    
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      that.loadOriginalOptions1692528723801219073();
that.loadOriginalOptions1679698329655750657();

    },
    refreshSonsData: function () {
      // this.getEntity();
      if (!this.originalChildrenReady) return;
      
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
      that.originalRequest({
        url: '/admin/auto/yz_manager/6031906822946816/detail/' + that.id + that.buildParentIdQuery(),
        sendSessionId: true,
        success: function (res) {
          if (res.code == 200) {
            // 既有记录必须由当前请求回读同一字符串ID，错误VO不能成为下一次整entity写入对象。
            if (!res.data || Array.isArray(res.data) || originalRecordId(res.data.id) !== that.id) {
              that.recordReady = false;
              that.loadEntitySuccess = false;
              uni.showModal({ showCancel: false, content: '详情返回记录与当前页面不一致，请刷新后重试' });
              return;
            }
            that.entity = res.data;
            
            that.markOriginalRecordReady();
            that.initLists();
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
      this.permissionVersion++;
      if (!this._alive || this.saving || this.readOnlyCondition || (!this.page_is_function_table && (this.id ? !this.page_is_save : !this.page_is_new))) return;
      var that = this;
      if (this.page_is_function_table) {
        // 功能表不走标准 add/update，只校验表单后交给自定义按钮继续执行。
        const validationCurrent = this.originalContext();
      this.$refs.entityForm.validate().then(valid_res => {
        if (!validationCurrent() || this.readOnlyCondition || this.saving) return;
          if (successFunction) {
            successFunction({ code: 200, data: that.entity });
          }
        }).catch(err => {
          
          console.log('表单错误信息：', err);
        })
        return;
      }
      const validationCurrent = this.originalContext();
      this.$refs.entityForm.validate().then(valid_res => {
        if (!validationCurrent() || this.readOnlyCondition || this.saving) return;
        //console.log('表单数据信息：', valid_res);
        var method = 'add';
        if (!utils.isBlank(this.id)) {
          method = 'update';
        }
        
        that.originalRequest({
          url: '/admin/auto/yz_manager/6031906822946816/' + method + that.buildParentIdQuery(),
          data: that.entity,
          sendSessionId: true,
          method: 'POST',
          success: function (res) {
            if (res.code == 200) {
              if (!res.data || !originalRecordId(res.data.id) || method === 'update' && originalRecordId(res.data.id) !== that.id) {
                that.recordReady = false;
                uni.showModal({ showCancel: false, content: '保存返回记录与当前页面不一致，请重新读取后确认' });
                return;
              }
              that.notifyMenuMutation();
              that.id = originalRecordId(res.data.id);
              that.entity = res.data;
              that.markOriginalRecordReady();
              // 新建后先确认真实记录ID，再继续原“先保存再执行”按钮动作。
              if (successFunction) {
                successFunction(res);
              }
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
      if (!this._alive || this.saving || this.readOnlyCondition || !this.page_is_delete || !yzp.hasPagePermission(this.page_sn, 'is_delete')) return;
      const current = this.originalContext();
      var that = this;
      uni.showModal({
        title: '确定要删除吗？',
        confirmColor: '#0ab3ad',
        success(modal) {
          if (modal.confirm && current() && !that.readOnlyCondition) {
            that.originalRequest({
              url: '/admin/auto/yz_manager/6031906822946816' + that.buildParentIdQuery(),
              method: 'DELETE',
              data: that.entity,
              sendSessionId: true,
              success: function (res) {
                if (res.code == 200) {
                  that.notifyMenuMutation();
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
