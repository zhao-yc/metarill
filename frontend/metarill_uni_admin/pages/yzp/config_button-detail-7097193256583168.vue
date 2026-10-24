<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="按钮"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="按钮"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='id' name='id'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!idReadOnly' v-model='entity.id' maxlength='50'  />
<text v-if='idReadOnly'>{{entity.id}}</text>
</uni-forms-item>

<uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='行为类型' name='actionType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='actionTypeReadOnly'>{{entity.actionTypeShow}}</text><template v-if='!actionTypeReadOnly'><uni-data-checkbox :disabled='actionTypeReadOnly' v-model='entity.actionType' :localdata='actionTypeRange' /></template>
</uni-forms-item>

<uni-forms-item label='sql' name='sql' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!sqlReadOnly' v-model='entity.sql' maxlength='500'  />
<text v-if='sqlReadOnly'>{{entity.sql}}</text><view class='input-tips'>/admin/custom/union_order/sure</view>
</uni-forms-item>

<uni-forms-item label='创建时间' name='insertTime'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-datetime-picker type='datetime'  v-if='!insertTimeReadOnly' v-model='entity.insertTime' /><text v-if='insertTimeReadOnly'>{{entity.insertTime}}</text>
</uni-forms-item>

<uni-forms-item label='位置' name='place' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='placeReadOnly'>{{entity.placeShow}}</text><template v-if='!placeReadOnly'><uni-data-checkbox :disabled='placeReadOnly' v-model='entity.place' :localdata='placeRange' /></template>
</uni-forms-item>

<uni-forms-item label='显示方案' name='visibleScheme' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='visibleSchemeReadOnly'>{{entity.visibleSchemeShow}}</text><template v-if='!visibleSchemeReadOnly'><uni-data-checkbox :disabled='visibleSchemeReadOnly' v-model='entity.visibleScheme' :localdata='visibleSchemeRange' /></template>
</uni-forms-item>

<uni-forms-item label='动态显示规则' name='visibleRuleJson'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<ButtonVisibleRuleEditor v-if='!visibleRuleJsonReadOnly' :entity="entity" :record-id="id" :readonly="visibleRuleJsonReadOnly" @receive="receive1774021578269784000"></ButtonVisibleRuleEditor>
<view class='input-tips'>支持当前记录字段的结构化显示条件</view>
</uni-forms-item>

<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!sortNoReadOnly' v-model='entity.sortNo'  type='number' />
<text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>

<uni-forms-item label='保存选项' name='saveOption' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='saveOptionReadOnly'>{{entity.saveOptionShow}}</text><template v-if='!saveOptionReadOnly'><uni-data-checkbox :disabled='saveOptionReadOnly' v-model='entity.saveOption' :localdata='saveOptionRange' /></template>
</uni-forms-item>

<uni-forms-item label='is_need_confirm' name='isNeedConfirm'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isNeedConfirmReadOnly'>{{(($checkboxRange || [])[entity.isNeedConfirm==null?0:entity.isNeedConfirm] || {}).text || ''}}</text><template v-if='!isNeedConfirmReadOnly'><uni-data-checkbox :disabled='isNeedConfirmReadOnly' v-model='entity.isNeedConfirm' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='confirm_msg' name='confirmMsg'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!confirmMsgReadOnly' v-model='entity.confirmMsg' maxlength='50'  />
<text v-if='confirmMsgReadOnly'>{{entity.confirmMsg}}</text>
</uni-forms-item>

<uni-forms-item label='类型' name='type'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='typeReadOnly'>{{entity.typeShow}}</text><template v-if='!typeReadOnly'><uni-data-checkbox :disabled='typeReadOnly' v-model='entity.type' :localdata='typeRange' /></template>
</uni-forms-item>

<uni-forms-item label='多选按钮' name='isBatchSelectButton'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isBatchSelectButtonReadOnly'>{{(($checkboxRange || [])[entity.isBatchSelectButton==null?0:entity.isBatchSelectButton] || {}).text || ''}}</text><template v-if='!isBatchSelectButtonReadOnly'><uni-data-checkbox :disabled='isBatchSelectButtonReadOnly' v-model='entity.isBatchSelectButton' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='执行后自动关闭' name='isAutoCloseAfterAction' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoCloseAfterActionReadOnly'>{{(($checkboxRange || [])[entity.isAutoCloseAfterAction==null?0:entity.isAutoCloseAfterAction] || {}).text || ''}}</text><template v-if='!isAutoCloseAfterActionReadOnly'><uni-data-checkbox :disabled='isAutoCloseAfterActionReadOnly' v-model='entity.isAutoCloseAfterAction' :localdata='$checkboxRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>


    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>--><button type='primary' size='mini' v-if="page_is_copy" @click='copyEntity()'>复制</button></view></view>
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
      page_sn: '7097193256583168',
      parent_page_sn: '',
      formParametersName: 'formParameters7097193256583168',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  true ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,

      actionTypeRange:[{"value":1,"text":"调接口"},{"value":2,"text":"页面跳转"},{"value":3,"text":"页面跳转(新页面)"},{"value":4,"text":"自定义方法"}], placeRange:[{"value":"Edit","text":"编辑界面"},{"value":"List","text":"列表上"},{"value":"EditAndGrid","text":"编辑界面及表格"},{"value":"Grid","text":"表格"}], visibleSchemeRange:[{"value":"Normal","text":"显示"},{"value":"Hidden","text":"隐藏"},{"value":"AlwaysVisible","text":"总是可见"},{"value":"VisibleWhenWritable","text":"可写显示只读隐藏"},{"value":"OnlyDeve","text":"仅开发用户可见"}], saveOptionRange:[{"value":"Save","text":"保存"},{"value":"NotSave","text":"不保存"}], typeRange:[{"value":0,"text":"default"},{"value":1,"text":"primary"},{"value":2,"text":"warn"}],
      entity: {
        id:'',name:'',actionType:'',sql:'',insertTime:'',place:'Edit',visibleScheme:'Normal',visibleRuleJson:'null',sortNo:99,saveOption:'Save',isNeedConfirm:0,confirmMsg:'',type:0,isBatchSelectButton:0,isAutoCloseAfterAction:0,
      },
      entity_column_readonly: {
        id:true,name:false,actionType:false,sql:false,insertTime:true,place:false,visibleScheme:false,visibleRuleJson:false,sortNo:false,saveOption:false,isNeedConfirm:false,confirmMsg:false,type:false,isBatchSelectButton:false,isAutoCloseAfterAction:false,
      },
      entity_rules: {
        id: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},name: {  rules: [  {      required: true,     errorMessage: '名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},actionType: {  rules: [  {      required: true,     errorMessage: '行为类型为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},sql: {  rules: [  {      required: true,     errorMessage: 'sql为必填项',  },  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},insertTime: {  rules: [  ]},place: {  rules: [  {      required: true,     errorMessage: '位置为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},visibleScheme: {  rules: [  {      required: true,     errorMessage: '显示方案为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},visibleRuleJson: {  rules: [  {      maxLength: 2000,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},sortNo: {  rules: [  {      required: true,     errorMessage: '排序号为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},saveOption: {  rules: [  {      required: true,     errorMessage: '保存选项为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isNeedConfirm: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},confirmMsg: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},type: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isBatchSelectButton: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isAutoCloseAfterAction: {  rules: [  {      required: true,     errorMessage: '执行后自动关闭为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    return false;},idReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.id;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},actionTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.actionType;},sqlReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.sql;},insertTimeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.insertTime;},placeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.place;},visibleSchemeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.visibleScheme;},visibleRuleJsonReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.visibleRuleJson;},sortNoReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.sortNo;},saveOptionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.saveOption;},isNeedConfirmReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isNeedConfirm;},confirmMsgReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.confirmMsg;},typeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.type;},isBatchSelectButtonReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isBatchSelectButton;},isAutoCloseAfterActionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoCloseAfterAction;},
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
      formParameters.configButton = {};
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
    receive1774021578269784000(newEntity) {    this.entity = newEntity;},copyEntity: function() {var that = this;uni.showModal({title: '确定要复制吗？',success(modal) {	if (modal.confirm) {		yzp.request({url: '/admin/auto/config_button/7097193256583168/copy/'+that.id + that.buildParentIdQuery(),method: 'POST',			sendSessionId:true,			success: function(res) {				if (res.code == 200) {					that.getOpenerEventChannel().emit('onChange', 'copy');					uni.navigateBack();				} else {					uni.showModal({						showCancel: false,						content: res.msg					})				}			},		})	}}})},showCustomerButtons: function() {var that = this;var actions = [];if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},



    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;

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
        url: '/admin/auto/config_button/7097193256583168/detail/' + that.id + that.buildParentIdQuery(),
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
        that.entity.configPageId = that.parent_id;
        yzp.request({
          url: '/admin/auto/config_button/7097193256583168/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_button/7097193256583168' + that.buildParentIdQuery(),
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