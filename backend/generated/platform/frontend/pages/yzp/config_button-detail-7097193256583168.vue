<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="按钮" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="按钮"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'   /><text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>
<uni-forms-item label='行为类型' name='actionType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='actionTypeReadOnly'>{{entity.actionTypeShow}}</text><template v-if='!actionTypeReadOnly'><uni-data-checkbox :disabled='actionTypeReadOnly' v-model='entity.actionType' :localdata='actionTypeRange' /></template>
</uni-forms-item>
<uni-forms-item label='sql' name='sql' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sqlReadOnly' v-model='entity.sql' maxlength='500'   /><text v-if='sqlReadOnly'>{{entity.sql}}</text>
<view class='input-tips'>/admin/custom/union_order/sure</view>
</uni-forms-item>
<uni-forms-item label='创建时间' name='insertTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-datetime-picker type='datetime' v-if='!insertTimeReadOnly' v-model='entity.insertTime' /><text v-if='insertTimeReadOnly'>{{entity.insertTime}}</text>
</uni-forms-item>
<uni-forms-item label='位置' name='place' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='placeReadOnly'>{{entity.placeShow}}</text><template v-if='!placeReadOnly'><uni-data-checkbox :disabled='placeReadOnly' v-model='entity.place' :localdata='placeRange' /></template>
</uni-forms-item>
<uni-forms-item label='显示方案' name='visibleScheme' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='visibleSchemeReadOnly'>{{entity.visibleSchemeShow}}</text><template v-if='!visibleSchemeReadOnly'><uni-data-checkbox :disabled='visibleSchemeReadOnly' v-model='entity.visibleScheme' :localdata='visibleSchemeRange' /></template>
</uni-forms-item>
<uni-forms-item label='动态显示规则' name='visibleRuleJson'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<yzp-button-visible-rule-editor v-if='!visibleRuleJsonReadOnly' :entity="entity" :record-id="id" :readonly="visibleRuleJsonReadOnly" @receive="receive1774021578269784000" />
<view class='input-tips'>支持当前记录字段的结构化显示条件</view>
</uni-forms-item>
<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sortNoReadOnly' v-model='entity.sortNo'  type='number'  /><text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>
<uni-forms-item label='保存选项' name='saveOption' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='saveOptionReadOnly'>{{entity.saveOptionShow}}</text><template v-if='!saveOptionReadOnly'><uni-data-checkbox :disabled='saveOptionReadOnly' v-model='entity.saveOption' :localdata='saveOptionRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_need_confirm' name='isNeedConfirm'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isNeedConfirmReadOnly'>{{(($checkboxRange || [])[entity.isNeedConfirm==null?0:entity.isNeedConfirm] || {}).text || ''}}</text><template v-if='!isNeedConfirmReadOnly'><uni-data-checkbox :disabled='isNeedConfirmReadOnly' v-model='entity.isNeedConfirm' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='config_table_name' name='configTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configTableNameReadOnly' v-model='entity.configTableName' maxlength='50'   /><text v-if='configTableNameReadOnly'>{{entity.configTableName}}</text>
</uni-forms-item>
<uni-forms-item label='config_page_name' name='configPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configPageNameReadOnly' v-model='entity.configPageName' maxlength='50'   /><text v-if='configPageNameReadOnly'>{{entity.configPageName}}</text>
</uni-forms-item>
<uni-forms-item label='import_no' name='importNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importNoReadOnly' v-model='entity.importNo' maxlength='50'   /><text v-if='importNoReadOnly'>{{entity.importNo}}</text>
</uni-forms-item>
<uni-forms-item label='confirm_msg' name='confirmMsg'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!confirmMsgReadOnly' v-model='entity.confirmMsg' maxlength='50'   /><text v-if='confirmMsgReadOnly'>{{entity.confirmMsg}}</text>
</uni-forms-item>
<uni-forms-item label='yz_project_name' name='yzProjectName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectNameReadOnly' v-model='entity.yzProjectName' maxlength='50'   /><text v-if='yzProjectNameReadOnly'>{{entity.yzProjectName}}</text>
</uni-forms-item>
<uni-forms-item label='类型' name='type'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='typeReadOnly'>{{entity.typeShow}}</text><template v-if='!typeReadOnly'><uni-data-checkbox :disabled='typeReadOnly' v-model='entity.type' :localdata='typeRange' /></template>
</uni-forms-item>
<uni-forms-item label='多选按钮' name='isBatchSelectButton'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isBatchSelectButtonReadOnly'>{{(($checkboxRange || [])[entity.isBatchSelectButton==null?0:entity.isBatchSelectButton] || {}).text || ''}}</text><template v-if='!isBatchSelectButtonReadOnly'><uni-data-checkbox :disabled='isBatchSelectButtonReadOnly' v-model='entity.isBatchSelectButton' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='执行后自动关闭' name='isAutoCloseAfterAction' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAutoCloseAfterActionReadOnly'>{{(($checkboxRange || [])[entity.isAutoCloseAfterAction==null?0:entity.isAutoCloseAfterAction] || {}).text || ''}}</text><template v-if='!isAutoCloseAfterActionReadOnly'><uni-data-checkbox :disabled='isAutoCloseAfterActionReadOnly' v-model='entity.isAutoCloseAfterAction' :localdata='$checkboxRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>
      
    </view>
    
    
    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><button type='primary' size='mini' v-if="page_is_copy && !readOnlyCondition && $yzp.hasPagePermission(page_sn,'is_all')" @click='copyEntity()'>复制</button>
</view></view>
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
      page_sn: '7097193256583168',
      parent_page_sn: '',
      formParametersName: 'formParameters7097193256583168',
      loadEntitySuccess: false,
      actionTypeRange:[{"text":"\u8C03\u63A5\u53E3","value":1},{"text":"\u9875\u9762\u8DF3\u8F6C","value":2},{"text":"\u9875\u9762\u8DF3\u8F6C(\u65B0\u9875\u9762)","value":3},{"text":"\u81EA\u5B9A\u4E49\u65B9\u6CD5","value":4}],placeRange:[{"text":"\u7F16\u8F91\u754C\u9762","value":"Edit"},{"text":"\u5217\u8868\u4E0A","value":"List"},{"text":"\u7F16\u8F91\u754C\u9762\u53CA\u8868\u683C","value":"EditAndGrid"},{"text":"\u8868\u683C","value":"Grid"}],visibleSchemeRange:[{"text":"\u663E\u793A","value":"Normal"},{"text":"\u9690\u85CF","value":"Hidden"},{"text":"\u603B\u662F\u53EF\u89C1","value":"AlwaysVisible"},{"text":"\u53EF\u5199\u663E\u793A\u53EA\u8BFB\u9690\u85CF","value":"VisibleWhenWritable"},{"text":"\u4EC5\u5F00\u53D1\u7528\u6237\u53EF\u89C1","value":"OnlyDeve"}],saveOptionRange:[{"text":"\u4FDD\u5B58","value":"Save"},{"text":"\u4E0D\u4FDD\u5B58","value":"NotSave"}],typeRange:[{"text":"default","value":0},{"text":"primary","value":1},{"text":"warn","value":2}],originalButtons:[],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:true,page_is_copy:true,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        name:"",actionType:"",sql:"",insertTime:"",place:"Edit",visibleScheme:"Normal",visibleRuleJson:"",sortNo:99,saveOption:"Save",isNeedConfirm:0,configTableName:"",configPageName:"",importNo:"",confirmMsg:"",yzProjectName:"",type:0,isBatchSelectButton:0,isAutoCloseAfterAction:0,
      },
      entity_column_readonly: {
        name:false,actionType:false,sql:false,insertTime:true,place:false,visibleScheme:false,visibleRuleJson:false,sortNo:false,saveOption:false,isNeedConfirm:false,configTableName:false,configPageName:false,importNo:false,confirmMsg:false,yzProjectName:true,type:false,isBatchSelectButton:false,isAutoCloseAfterAction:false,
      },
      entity_rules: {
        name:{rules:[{"required":true,"errorMessage":"\u540D\u79F0\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},actionType:{rules:[{"required":true,"errorMessage":"\u884C\u4E3A\u7C7B\u578B\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},sql:{rules:[{"required":true,"errorMessage":"sql\u4E3A\u5FC5\u586B\u9879"},{"maxLength":500,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},insertTime:{rules:[]},place:{rules:[{"required":true,"errorMessage":"\u4F4D\u7F6E\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},visibleScheme:{rules:[{"required":true,"errorMessage":"\u663E\u793A\u65B9\u6848\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},visibleRuleJson:{rules:[{"maxLength":2000,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},sortNo:{rules:[{"required":true,"errorMessage":"\u6392\u5E8F\u53F7\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},saveOption:{rules:[{"required":true,"errorMessage":"\u4FDD\u5B58\u9009\u9879\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isNeedConfirm:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},configTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},configPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},importNo:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},confirmMsg:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzProjectName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},type:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isBatchSelectButton:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isAutoCloseAfterAction:{rules:[{"required":true,"errorMessage":"\u6267\u884C\u540E\u81EA\u52A8\u5173\u95ED\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    nameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.name; },
actionTypeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.actionType; },
sqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sql; },
insertTimeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.insertTime; },
placeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.place; },
visibleSchemeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.visibleScheme; },
visibleRuleJsonReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.visibleRuleJson; },
sortNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sortNo; },
saveOptionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.saveOption; },
isNeedConfirmReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isNeedConfirm; },
configTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configTableName; },
configPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configPageName; },
importNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importNo; },
confirmMsgReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.confirmMsg; },
yzProjectNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectName; },
typeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.type; },
isBatchSelectButtonReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isBatchSelectButton; },
isAutoCloseAfterActionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoCloseAfterAction; },
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
      uni.$emit('yzpMenuChanged', { pageId: '7097193256583168', tableName: 'config_button' });
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
      formParameters.configButton = {};
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
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={name:"",actionType:"",sql:"",insertTime:"",place:"Edit",visibleScheme:"Normal",visibleRuleJson:"",sortNo:99,saveOption:"Save",isNeedConfirm:0,configTableName:"",configPageName:"",importNo:"",confirmMsg:"",yzProjectName:"",type:0,isBatchSelectButton:0,isAutoCloseAfterAction:0,};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
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
 const mutation=route==="/admin/auto/config_button/7097193256583168"&&method==='DELETE'||method==='POST'&&["/admin/auto/config_button/7097193256583168"+'/add',"/admin/auto/config_button/7097193256583168"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/config_button/7097193256583168"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/config_button/7097193256583168"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["name","actionType","sql","place","visibleScheme","visibleRuleJson","sortNo","saveOption","isNeedConfirm","configTableName","configPageName","importNo","confirmMsg","type","isBatchSelectButton","isAutoCloseAfterAction"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/config_button/7097193256583168"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
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

    receive1774021578269784000(newEntity) { if(this.visibleRuleJsonReadOnly) return; this.entity=newEntity; },
showCustomerButtons() { const current=this.originalContext(),actions=this.originalButtons.filter(button=>this['customerButtonShow'+button.id]);if(!actions.length){uni.showToast({title:'暂无可用操作',icon:'none'});return;}uni.showActionSheet({itemList:actions.map(button=>button.name),success:res=>{if(current()&&actions[res.tapIndex])this['customerButtonHandler'+actions[res.tapIndex].id]();}}); },

    
    
    
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      
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
        url: '/admin/auto/config_button/7097193256583168/detail/' + that.id + that.buildParentIdQuery(),
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
        that.entity.configPageId=that.parent_id;
        that.originalRequest({
          url: '/admin/auto/config_button/7097193256583168/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_button/7097193256583168' + that.buildParentIdQuery(),
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
