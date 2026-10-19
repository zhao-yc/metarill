<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="子表关系" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="子表关系"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='父页面' name='parentConfigPageId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput style='display: none;' v-model='entity.parentConfigPageId' maxlength='' /><text v-if='parentConfigPageIdReadOnly'>{{readonlyDisplayText(entity.parentConfigPageName)}}</text><template v-if='!parentConfigPageIdReadOnly'><view class='select-page-input-row'><uni-easyinput disabled primaryColor="#0ab3ad" :model-value='readonlyDisplayText(entity.parentConfigPageName)'/><button v-if='!parentConfigPageIdReadOnly' size='mini' @click='openSelect1679391145852018689()'>选择视图</button></view></template>
</uni-forms-item>
<uni-forms-item label='子页面' name='childConfigPageId' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput style='display: none;' v-model='entity.childConfigPageId' maxlength='50' /><text v-if='childConfigPageIdReadOnly'>{{readonlyDisplayText(entity.childConfigPageName)}}</text><template v-if='!childConfigPageIdReadOnly'><view class='select-page-input-row'><uni-easyinput disabled primaryColor="#0ab3ad" :model-value='readonlyDisplayText(entity.childConfigPageName)'/><button v-if='!childConfigPageIdReadOnly' size='mini' @click='openSelect1679391146044956674()'>选择视图</button></view></template>
</uni-forms-item>
<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sortNoReadOnly' v-model='entity.sortNo'  type='number'  /><text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>
<uni-forms-item label='PC端子页面打开方式' name='childPageOpenType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='childPageOpenTypeReadOnly'>{{entity.childPageOpenTypeShow}}</text><template v-if='!childPageOpenTypeReadOnly'><uni-data-checkbox :disabled='childPageOpenTypeReadOnly' v-model='entity.childPageOpenType' :localdata='childPageOpenTypeRange' /></template>
</uni-forms-item>
<uni-forms-item label='parent_config_page_name' name='parentConfigPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!parentConfigPageNameReadOnly' v-model='entity.parentConfigPageName' maxlength='50'   /><text v-if='parentConfigPageNameReadOnly'>{{entity.parentConfigPageName}}</text>
</uni-forms-item>
<uni-forms-item label='child_config_page_name' name='childConfigPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!childConfigPageNameReadOnly' v-model='entity.childConfigPageName' maxlength='50'   /><text v-if='childConfigPageNameReadOnly'>{{entity.childConfigPageName}}</text>
</uni-forms-item>
<uni-forms-item label='yz_project_name' name='yzProjectName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectNameReadOnly' v-model='entity.yzProjectName' maxlength='50'   /><text v-if='yzProjectNameReadOnly'>{{entity.yzProjectName}}</text>
</uni-forms-item>
<uni-forms-item label='import_no' name='importNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importNoReadOnly' v-model='entity.importNo' maxlength='50'   /><text v-if='importNoReadOnly'>{{entity.importNo}}</text>
</uni-forms-item>

        </uni-forms>
      </view>
      
    </view>
    
    <yzp-popup ref="selectPageDialog1679391145852018689" type="center" @change="!$event.show && onPopClose1679391145852018689()"><view class="select-page-dialog-view"><OriginalSelection1679391145852018689 v-if="originalSelection1679391145852018689" ref='1679391145852018689' @onPopSelect="onPopSelect1679391145852018689" @onPopClose="onPopClose1679391145852018689" :selectedIds="entity.parentConfigPageId" :selectedNames="entity.parentConfigPageName" selected-name-field="name" :parent_id="''" :parentReadOnlyCondition="parentConfigPageIdReadOnly" /></view></yzp-popup>
<yzp-popup ref="selectPageDialog1679391146044956674" type="center" @change="!$event.show && onPopClose1679391146044956674()"><view class="select-page-dialog-view"><OriginalSelection1679391146044956674 v-if="originalSelection1679391146044956674" ref='1679391146044956674' @onPopSelect="onPopSelect1679391146044956674" @onPopClose="onPopClose1679391146044956674" :selectedIds="entity.childConfigPageId" :selectedNames="entity.childConfigPageName" selected-name-field="name" :parent_id="''" :parentReadOnlyCondition="childConfigPageIdReadOnly" /></view></yzp-popup>

    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button></view></view>
  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";
import OriginalSelection1679391145852018689 from "@/pages/yzp/config_page-list-1869016706289577986.vue";
import OriginalSelection1679391146044956674 from "@/pages/yzp/config_page-list-1869016706289577986.vue";

const originalRecordId=value=>typeof value==='string' && !['null','undefined'].includes(value.trim())?value.trim():'';

export default {
  
  components: {
    OriginalSelection1679391145852018689,OriginalSelection1679391146044956674,
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      yzpMenuId: '',
      page_sn: '1679390851298631682',
      parent_page_sn: '',
      formParametersName: 'formParameters1679390851298631682',
      loadEntitySuccess: false,
      originalSelection1679391145852018689:false,originalSelection1679391146044956674:false,childPageOpenTypeRange:[{"text":"\u65B0\u9875\u9762","value":1},{"text":"\u5F39\u51FA\u5C42","value":2}],originalButtons:[],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:true,page_is_copy:false,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        parentConfigPageId:"",childConfigPageId:"",sortNo:"",childPageOpenType:1,parentConfigPageName:"",childConfigPageName:"",yzProjectName:"",importNo:"",
      },
      entity_column_readonly: {
        parentConfigPageId:false,childConfigPageId:false,sortNo:false,childPageOpenType:false,parentConfigPageName:false,childConfigPageName:false,yzProjectName:true,importNo:false,
      },
      entity_rules: {
        parentConfigPageId:{rules:[{"required":true,"errorMessage":"\u7236\u9875\u9762\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},childConfigPageId:{rules:[{"required":true,"errorMessage":"\u5B50\u9875\u9762\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},sortNo:{rules:[{"required":true,"errorMessage":"\u6392\u5E8F\u53F7\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},childPageOpenType:{rules:[{"required":true,"errorMessage":"PC\u7AEF\u5B50\u9875\u9762\u6253\u5F00\u65B9\u5F0F\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},parentConfigPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},childConfigPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzProjectName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},importNo:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    parentConfigPageIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.parentConfigPageId; },
childConfigPageIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.childConfigPageId; },
sortNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sortNo; },
childPageOpenTypeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.childPageOpenType; },
parentConfigPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.parentConfigPageName; },
childConfigPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.childConfigPageName; },
yzProjectNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectName; },
importNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importNo; },
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
      uni.$emit('yzpMenuChanged', { pageId: '1679390851298631682', tableName: 'config_child' });
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
      formParameters.configChild = {};
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
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={parentConfigPageId:"",childConfigPageId:"",sortNo:"",childPageOpenType:1,parentConfigPageName:"",childConfigPageName:"",yzProjectName:"",importNo:"",};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
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
 const mutation=route==="/admin/auto/config_child/1679390851298631682"&&method==='DELETE'||method==='POST'&&["/admin/auto/config_child/1679390851298631682"+'/add',"/admin/auto/config_child/1679390851298631682"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/config_child/1679390851298631682"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/config_child/1679390851298631682"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["parentConfigPageId","childConfigPageId","sortNo","childPageOpenType","parentConfigPageName","childConfigPageName","importNo"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/config_child/1679390851298631682"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
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

    openSelect1679391145852018689() { if(this.parentConfigPageIdReadOnly)return; this._selection1679391145852018689=this.originalContext();this.originalSelection1679391145852018689=true;this.$nextTick(()=>{if(this._selection1679391145852018689?.())this.$refs.selectPageDialog1679391145852018689.open();}); },
onPopClose1679391145852018689() { this.originalSelection1679391145852018689=false;this._selection1679391145852018689=null;this.$refs.selectPageDialog1679391145852018689?.close(); },
onPopSelect1679391145852018689(row) { this.permissionVersion++;if(this.parentConfigPageIdReadOnly || !this._selection1679391145852018689?.() || !row || !originalRecordId(row.id))return;this.entity.parentConfigPageId=row.id;this.entity.parentConfigPageName=row.name; this.onPopClose1679391145852018689(); },
openSelect1679391146044956674() { if(this.childConfigPageIdReadOnly)return; this._selection1679391146044956674=this.originalContext();this.originalSelection1679391146044956674=true;this.$nextTick(()=>{if(this._selection1679391146044956674?.())this.$refs.selectPageDialog1679391146044956674.open();}); },
onPopClose1679391146044956674() { this.originalSelection1679391146044956674=false;this._selection1679391146044956674=null;this.$refs.selectPageDialog1679391146044956674?.close(); },
onPopSelect1679391146044956674(row) { this.permissionVersion++;if(this.childConfigPageIdReadOnly || !this._selection1679391146044956674?.() || !row || !originalRecordId(row.id))return;this.entity.childConfigPageId=row.id;this.entity.childConfigPageName=row.name; this.onPopClose1679391146044956674(); },
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
        url: '/admin/auto/config_child/1679390851298631682/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/config_child/1679390851298631682/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_child/1679390851298631682' + that.buildParentIdQuery(),
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
