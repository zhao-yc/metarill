<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="子表关系"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="子表关系"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='父页面' name='parentConfigPageId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput style='display: none;' v-model='entity.parentConfigPageId' maxlength='50' />
<text v-if='parentConfigPageIdReadOnly'>{{readonlyDisplayText(entity.parentConfigPageName)}}</text><template v-if='!parentConfigPageIdReadOnly'><view class='select-page-input-row'>
<uni-easyinput disabled primaryColor="#007aff" :model-value='readonlyDisplayText(entity.parentConfigPageName)'/>
<button v-if='!parentConfigPageIdReadOnly' size='mini' @click='openSelect1679391145852018689()'>选择视图</button>
</view>
</template>
</uni-forms-item>

<uni-forms-item label='子页面' name='childConfigPageId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput style='display: none;' v-model='entity.childConfigPageId' maxlength='50' />
<text v-if='childConfigPageIdReadOnly'>{{readonlyDisplayText(entity.childConfigPageName)}}</text><template v-if='!childConfigPageIdReadOnly'><view class='select-page-input-row'>
<uni-easyinput disabled primaryColor="#007aff" :model-value='readonlyDisplayText(entity.childConfigPageName)'/>
<button v-if='!childConfigPageIdReadOnly' size='mini' @click='openSelect1679391146044956674()'>选择视图</button>
</view>
</template>
</uni-forms-item>

<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!sortNoReadOnly' v-model='entity.sortNo' maxlength='50'  />
<text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>

<uni-forms-item label='PC端子页面打开方式' name='childPageOpenType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='childPageOpenTypeReadOnly'>{{entity.childPageOpenTypeShow}}</text><template v-if='!childPageOpenTypeReadOnly'><uni-data-checkbox :disabled='childPageOpenTypeReadOnly' v-model='entity.childPageOpenType' :localdata='childPageOpenTypeRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>

    <yzp-popup ref="selectPageDialog1679391145852018689" type="center">
<view class="select-page-dialog-view">
<config_page_1869016706289577986 ref='1679391145852018689' @onPopSelect="onPopSelect1679391145852018689" @onPopClose="onPopClose1679391145852018689" :selectedIds="entity.parentConfigPageId" :selectedNames="entity.parentConfigPageName" selected-name-field="name"></config_page_1869016706289577986>
</view>
</yzp-popup>
<yzp-popup ref="selectPageDialog1679391146044956674" type="center">
<view class="select-page-dialog-view">
<config_page_1869016706289577986 ref='1679391146044956674' @onPopSelect="onPopSelect1679391146044956674" @onPopClose="onPopClose1679391146044956674" :selectedIds="entity.childConfigPageId" :selectedNames="entity.childConfigPageName" selected-name-field="name"></config_page_1869016706289577986>
</view>
</yzp-popup>

    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>--></view></view>
  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";
import config_page_1869016706289577986 from '@/pages/yzp/config_page-list-1869016706289577986.vue';
export default {
  components: {
    config_page_1869016706289577986,
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      page_sn: '1679390851298631682',
      parent_page_sn: '',
      formParametersName: 'formParameters1679390851298631682',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,

      childPageOpenTypeRange:[{"value":1,"text":"新页面"},{"value":2,"text":"弹出层"}],
      entity: {
        parentConfigPageId:'',childConfigPageId:'',sortNo:'',childPageOpenType:1,
      },
      entity_column_readonly: {
        parentConfigPageId:false,childConfigPageId:false,sortNo:false,childPageOpenType:false,
      },
      entity_rules: {
        parentConfigPageId: {  rules: [  {      required: true,     errorMessage: '父页面为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},childConfigPageId: {  rules: [  {      required: true,     errorMessage: '子页面为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},sortNo: {  rules: [  {      required: true,     errorMessage: '排序号为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},childPageOpenType: {  rules: [  {      required: true,     errorMessage: 'PC端子页面打开方式为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},parentConfigPageIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.parentConfigPageId;},childConfigPageIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.childConfigPageId;},sortNoReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.sortNo;},childPageOpenTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.childPageOpenType;},
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
      formParameters.configChild = {};
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


    openSelect1679391145852018689() {this.$refs.selectPageDialog1679391145852018689.open();},onPopClose1679391145852018689() {this.$refs.selectPageDialog1679391145852018689.close();},onPopSelect1679391145852018689(e) {this.entity.parentConfigPageId=e.id;this.entity.parentConfigPageName=e.name;this.$refs.selectPageDialog1679391145852018689.close();},openSelect1679391146044956674() {this.$refs.selectPageDialog1679391146044956674.open();},onPopClose1679391146044956674() {this.$refs.selectPageDialog1679391146044956674.close();},onPopSelect1679391146044956674(e) {this.entity.childConfigPageId=e.id;this.entity.childConfigPageName=e.name;this.$refs.selectPageDialog1679391146044956674.close();},
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
        url: '/admin/auto/config_child/1679390851298631682/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/config_child/1679390851298631682/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_child/1679390851298631682' + that.buildParentIdQuery(),
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