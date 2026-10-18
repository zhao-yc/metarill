<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="项目" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''" ></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="项目"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='数据库连接字符串' name='mysqlConnStr' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!mysqlConnStrReadOnly' v-model='entity.mysqlConnStr'   />
<text v-if='mysqlConnStrReadOnly'>{{entity.mysqlConnStr}}</text>
</uni-forms-item>

<uni-forms-item label='缩略名' name='shortName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!shortNameReadOnly' v-model='entity.shortName'   />
<text v-if='shortNameReadOnly'>{{entity.shortName}}</text>
</uni-forms-item>

<uni-forms-item label='数据库名称' name='dbName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!dbNameReadOnly' v-model='entity.dbName'   />
<text v-if='dbNameReadOnly'>{{entity.dbName}}</text>
</uni-forms-item>

<uni-forms-item label='api路径' name='contextRoot' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!contextRootReadOnly' v-model='entity.contextRoot'   />
<text v-if='contextRootReadOnly'>{{entity.contextRoot}}</text>
</uni-forms-item>

<uni-forms-item label='x5adminName' name='x5AdminName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!x5AdminNameReadOnly' v-model='entity.x5AdminName'   />
<text v-if='x5AdminNameReadOnly'>{{entity.x5AdminName}}</text>
</uni-forms-item>

<uni-forms-item label='java项目包名' name='eclipseBaseName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!eclipseBaseNameReadOnly' v-model='entity.eclipseBaseName'   />
<text v-if='eclipseBaseNameReadOnly'>{{entity.eclipseBaseName}}</text>
</uni-forms-item>

<uni-forms-item label='yzp版本' name='yzYzpVersionId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='yzYzpVersionIdReadOnly'>{{readonlyDisplayText(entity.yzYzpVersionName)}}</text><template v-if='!yzYzpVersionIdReadOnly'><zxz-uni-data-select v-model='entity.yzYzpVersionId' :localdata='yz_yzp_version_list' filterable :disabled='yzYzpVersionIdReadOnly' dataKey='name' dataValue='id' @change='yz_yzp_versionSelectChange' /></template><view class='input-tips'>新项目默认 v8；旧项目保留原版本。已有生成目录不能直接切换版本。</view>
</uni-forms-item>

<uni-forms-item label='数据库类型' name='dbType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='dbTypeReadOnly'>{{entity.dbTypeShow}}</text><template v-if='!dbTypeReadOnly'><uni-data-checkbox :disabled='dbTypeReadOnly' v-model='entity.dbType' :localdata='dbTypeRange' /></template>
</uni-forms-item>

<uni-forms-item label='隐藏' name='isHide'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!isHideReadOnly' v-model='entity.isHide'   />
<text v-if='isHideReadOnly'>{{entity.isHide}}</text>
</uni-forms-item>

<uni-forms-item label='数据库连接' name='dbUrl'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!dbUrlReadOnly' v-model='entity.dbUrl' maxlength='500'  />
<text v-if='dbUrlReadOnly'>{{entity.dbUrl}}</text>
</uni-forms-item>

<uni-forms-item label='数据库账号' name='dbUsername'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!dbUsernameReadOnly' v-model='entity.dbUsername' maxlength='50'  />
<text v-if='dbUsernameReadOnly'>{{entity.dbUsername}}</text>
</uni-forms-item>

<uni-forms-item label='数据库密码' name='dbPassword'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!dbPasswordReadOnly' v-model='entity.dbPassword' maxlength='50'  />
<text v-if='dbPasswordReadOnly'>{{entity.dbPassword}}</text>
</uni-forms-item>

<uni-forms-item label='PrimaryColor' name='primaryColor' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!primaryColorReadOnly' v-model='entity.primaryColor' maxlength='50'  />
<text v-if='primaryColorReadOnly'>{{entity.primaryColor}}</text>
</uni-forms-item>

<uni-forms-item label='简单项目' name='isSmall'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isSmallReadOnly'>{{(($checkboxRange || [])[entity.isSmall==null?0:entity.isSmall] || {}).text || ''}}</text><template v-if='!isSmallReadOnly'><uni-data-checkbox :disabled='isSmallReadOnly' v-model='entity.isSmall' :localdata='$checkboxRange' /></template><view class='input-tips'>移动端后台不生成菜单tabBar，菜单放在首页</view>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>

    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn, 'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="permissionVersion >= 0 && !readOnlyCondition && !page_is_function_table && $yzp.hasPagePermission(page_sn, id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>--><button type='primary' size='mini' v-if="permissionVersion >= 0 && page_is_copy && $yzp.hasPagePermission(page_sn, 'is_all')" @click='copyEntity()'>复制</button>
<button size='mini' type='default' v-if='customerButtonShow1696360770502291457' @click='customerButtonHandler1696360770502291457'>初始化数据库</button>
<button size='mini' type='default' v-if='customerButtonShow2069009428958547968' @click='customerButtonHandler2069009428958547968'>同步运行时</button></view></view>
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
      page_sn: '6186303424888832',
      parent_page_sn: '',
      formParametersName: 'formParameters6186303424888832',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  true ,page_is_copy:  true ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,
      yz_yzp_version_list:[],
      dbTypeRange:[{"value":1,"text":"MicroSoftSQL"},{"value":2,"text":"MySQL"},{"value":3,"text":"ORACLE"}],
      entity: {
        name:'',mysqlConnStr:'',shortName:'',dbName:'',contextRoot:'',x5AdminName:'',eclipseBaseName:'',yzYzpVersionId:'2104907095303213057',dbType:'',isHide:'',dbUrl:'',dbUsername:'',dbPassword:'',primaryColor:'#007aff',isSmall:0,
      },
      entity_column_readonly: {
        name:false,mysqlConnStr:false,shortName:false,dbName:false,contextRoot:false,x5AdminName:false,eclipseBaseName:false,yzYzpVersionId:false,dbType:false,isHide:false,dbUrl:false,dbUsername:false,dbPassword:false,primaryColor:false,isSmall:false,
      },
      entity_rules: {
        name: {  rules: [  {      required: true,     errorMessage: '名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},mysqlConnStr: {  rules: [  {      required: true,     errorMessage: '数据库连接字符串为必填项',  },  ]},shortName: {  rules: [  {      required: true,     errorMessage: '缩略名为必填项',  },  ]},dbName: {  rules: [  {      required: true,     errorMessage: '数据库名称为必填项',  },  ]},contextRoot: {  rules: [  {      required: true,     errorMessage: 'api路径为必填项',  },  ]},x5AdminName: {  rules: [  ]},eclipseBaseName: {  rules: [  {      required: true,     errorMessage: 'java项目包名为必填项',  },  ]},yzYzpVersionId: {  rules: [  {      required: true,     errorMessage: 'yzp版本为必填项',  },  ]},dbType: {  rules: [  {      required: true,     errorMessage: '数据库类型为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isHide: {  rules: [  ]},dbUrl: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},dbUsername: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},dbPassword: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},primaryColor: {  rules: [  {      required: true,     errorMessage: 'PrimaryColor为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isSmall: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    this.permissionVersion;    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        if (!yzp.hasPagePermission(this.page_sn, 'is_update')) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    var writePermissionValue = this.id ? 'is_update' : 'is_new';    if (!yzp.hasPagePermission(this.page_sn, writePermissionValue)) {        return true;    }    return false;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},mysqlConnStrReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.mysqlConnStr;},shortNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.shortName;},dbNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dbName;},contextRootReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.contextRoot;},x5AdminNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.x5AdminName;},eclipseBaseNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.eclipseBaseName;},yzYzpVersionIdReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.yzYzpVersionId;},dbTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dbType;},isHideReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isHide;},dbUrlReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dbUrl;},dbUsernameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dbUsername;},dbPasswordReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.dbPassword;},primaryColorReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.primaryColor;},isSmallReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isSmall;},
    customerButtonShow1696360770502291457() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1696360770502291457",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow2069009428958547968() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2069009428958547968",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},hasVisibleCustomerButtons() {this.permissionVersion;return this.customerButtonShow1696360770502291457 || this.customerButtonShow2069009428958547968;},
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
      formParameters.yzProject = {};
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
    copyEntity: function() {var that = this;uni.showModal({title: '确定要复制吗？',success(modal) {	if (modal.confirm) {		yzp.request({url: '/admin/auto/yz_project/6186303424888832/copy/'+that.id + that.buildParentIdQuery(),method: 'POST',			sendSessionId:true,			success: function(res) {				if (res.code == 200) {					that.getOpenerEventChannel().emit('onChange', 'copy');					uni.navigateBack();				} else {					uni.showModal({						showCancel: false,						content: res.msg					})				}			},		})	}}})},customerButtonHandler1696360770502291457: function() {var that = this;uni.showModal({content: "您确定初始化数据库吗？",success(modal) {if (modal.confirm) {yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1696360770502291457", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})}}})},customerButtonHandler2069009428958547968: function() {var that = this;uni.showModal({content: "您确定要同步运行时吗？",success(modal) {if (modal.confirm) {yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2069009428958547968", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})}}})},showCustomerButtons: function() {var that = this;var actions = [];if (that.customerButtonShow1696360770502291457) {actions.push({name: "初始化数据库", handler: function() { that.customerButtonHandler1696360770502291457(); }});}if (that.customerButtonShow2069009428958547968) {actions.push({name: "同步运行时", handler: function() { that.customerButtonHandler2069009428958547968(); }});}if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},


    yz_yzp_versionSelectChange: function(item) {this.entity.yzYzpVersionId = item ? item.id : '';this.entity.yzYzpVersionName = item ? item.name : '';},
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      yzp.request({    url: '/admin/auto/yz_project/6186303424888832/findNameIdYzYzpVersion1669606903854936065',    data: {       formParameters: uni.getStorageSync(that.formParametersName)    },    sendSessionId: true,    method: "POST",    success: function (res) {        if (res.code == 200) {            that.yz_yzp_version_list = res.data || [];        } else {            uni.showModal({                showCancel: false,                content: res.msg            })        }    },});
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
        url: '/admin/auto/yz_project/6186303424888832/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/yz_project/6186303424888832/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/yz_project/6186303424888832' + that.buildParentIdQuery(),
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