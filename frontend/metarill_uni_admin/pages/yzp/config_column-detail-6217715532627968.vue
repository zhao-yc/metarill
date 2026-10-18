<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="列" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''" ></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="列"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">

<uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'  />
<text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>

<uni-forms-item label='关联表' name='joinTable'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!joinTableReadOnly' v-model='entity.joinTable' maxlength='50'  />
<text v-if='joinTableReadOnly'>{{entity.joinTable}}</text>
</uni-forms-item>

<uni-forms-item label='is_hide_select_grid' name='isHideSelectGrid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!isHideSelectGridReadOnly' v-model='entity.isHideSelectGrid'  type='number' />
<text v-if='isHideSelectGridReadOnly'>{{entity.isHideSelectGrid}}</text>
</uni-forms-item>

<uni-forms-item label='联接字段' name='isJoinColumn'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isJoinColumnReadOnly'>{{(($checkboxRange || [])[entity.isJoinColumn==null?0:entity.isJoinColumn] || {}).text || ''}}</text><template v-if='!isJoinColumnReadOnly'><uni-data-checkbox :disabled='isJoinColumnReadOnly' v-model='entity.isJoinColumn' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='数据类型' name='columnType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='columnTypeReadOnly'>{{entity.columnTypeShow}}</text><template v-if='!columnTypeReadOnly'><uni-data-checkbox :disabled='columnTypeReadOnly' v-model='entity.columnType' :localdata='columnTypeRange' /></template>
</uni-forms-item>

<uni-forms-item label='column_constrain' name='columnConstrain'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!columnConstrainReadOnly' v-model='entity.columnConstrain' maxlength='50'  />
<text v-if='columnConstrainReadOnly'>{{entity.columnConstrain}}</text>
</uni-forms-item>

<uni-forms-item label='长度' name='columnLength'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!columnLengthReadOnly' v-model='entity.columnLength' maxlength='50'  />
<text v-if='columnLengthReadOnly'>{{entity.columnLength}}</text>
</uni-forms-item>

<uni-forms-item label='列全名' name='columnFullName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!columnFullNameReadOnly' v-model='entity.columnFullName' maxlength='50'  />
<text v-if='columnFullNameReadOnly'>{{entity.columnFullName}}</text>
</uni-forms-item>

<uni-forms-item label='is_auto_post_back_query' name='isAutoPostBackQuery'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoPostBackQueryReadOnly'>{{(($checkboxRange || [])[entity.isAutoPostBackQuery==null?0:entity.isAutoPostBackQuery] || {}).text || ''}}</text><template v-if='!isAutoPostBackQueryReadOnly'><uni-data-checkbox :disabled='isAutoPostBackQueryReadOnly' v-model='entity.isAutoPostBackQuery' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='保存选项' name='saveOption'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!saveOptionReadOnly' v-model='entity.saveOption' maxlength='50'  />
<text v-if='saveOptionReadOnly'>{{entity.saveOption}}</text>
</uni-forms-item>

<uni-forms-item label='display_option' name='displayOption'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!displayOptionReadOnly' v-model='entity.displayOption' maxlength='50'  />
<text v-if='displayOptionReadOnly'>{{entity.displayOption}}</text>
</uni-forms-item>

<uni-forms-item label='字段描述' name='description'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!descriptionReadOnly' v-model='entity.description' maxlength='500'  />
<text v-if='descriptionReadOnly'>{{entity.description}}</text>
</uni-forms-item>

<uni-forms-item label='开发者备注' name='developerRemarks'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!developerRemarksReadOnly' v-model='entity.developerRemarks' maxlength='500'  />
<text v-if='developerRemarksReadOnly'>{{entity.developerRemarks}}</text>
</uni-forms-item>

<uni-forms-item label='is_auto_update' name='isAutoUpdate'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!isAutoUpdateReadOnly' v-model='entity.isAutoUpdate'  type='number' />
<text v-if='isAutoUpdateReadOnly'>{{entity.isAutoUpdate}}</text>
</uni-forms-item>

<uni-forms-item label='复制' name='isCopy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!isCopyReadOnly' v-model='entity.isCopy' maxlength='50'  />
<text v-if='isCopyReadOnly'>{{entity.isCopy}}</text>
</uni-forms-item>

<uni-forms-item label='query_default_value' name='queryDefaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!queryDefaultValueReadOnly' v-model='entity.queryDefaultValue' maxlength='50'  />
<text v-if='queryDefaultValueReadOnly'>{{entity.queryDefaultValue}}</text>
</uni-forms-item>

<uni-forms-item label='列名称' name='columnName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!columnNameReadOnly' v-model='entity.columnName' maxlength='50'  />
<text v-if='columnNameReadOnly'>{{entity.columnName}}</text>
</uni-forms-item>

<uni-forms-item label='匹配类型' name='matchType'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='matchTypeReadOnly'>{{entity.matchTypeShow}}</text><template v-if='!matchTypeReadOnly'><uni-data-checkbox :disabled='matchTypeReadOnly' v-model='entity.matchType' :localdata='matchTypeRange' /></template>
</uni-forms-item>

<uni-forms-item label='width' name='width'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!widthReadOnly' v-model='entity.width' maxlength='50'  />
<text v-if='widthReadOnly'>{{entity.width}}</text>
</uni-forms-item>

<uni-forms-item label='控件类型' name='controlKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='controlKindReadOnly'>{{entity.controlKindShow}}</text><template v-if='!controlKindReadOnly'><uni-data-checkbox :disabled='controlKindReadOnly' v-model='entity.controlKind' :localdata='controlKindRange' /></template>
</uni-forms-item>

<uni-forms-item label='控件明细' name='controlDetail'  style='grid-column: span 2;' v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<yzp-control-detail-editor v-if='!controlDetailReadOnly' :entity="entity" :record-id="id" :readonly="controlDetailReadOnly" @receive="receive1670726645322010626"></yzp-control-detail-editor>

</uni-forms-item>

<uni-forms-item label='控件类型-筛选' name='filterControlKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='filterControlKindReadOnly'>{{entity.filterControlKindShow}}</text><template v-if='!filterControlKindReadOnly'><uni-data-checkbox :disabled='filterControlKindReadOnly' v-model='entity.filterControlKind' :localdata='filterControlKindRange' /></template>
</uni-forms-item>

<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!sortNoReadOnly' v-model='entity.sortNo'  type='number' />
<text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>

<uni-forms-item label='默认值' name='defaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!defaultValueReadOnly' v-model='entity.defaultValue' maxlength='50'  />
<text v-if='defaultValueReadOnly'>{{entity.defaultValue}}</text>
</uni-forms-item>

<uni-forms-item label='新建初始值' name='initValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!initValueReadOnly' v-model='entity.initValue' maxlength='50'  />
<text v-if='initValueReadOnly'>{{entity.initValue}}</text>
</uni-forms-item>

<uni-forms-item label='control_width' name='controlWidth'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!controlWidthReadOnly' v-model='entity.controlWidth' maxlength='50'  />
<text v-if='controlWidthReadOnly'>{{entity.controlWidth}}</text>
</uni-forms-item>

<uni-forms-item label='col_span' name='colSpan' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!colSpanReadOnly' v-model='entity.colSpan'  type='number' />
<text v-if='colSpanReadOnly'>{{entity.colSpan}}</text>
</uni-forms-item>

<uni-forms-item label='align' name='align'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!alignReadOnly' v-model='entity.align' maxlength='50'  />
<text v-if='alignReadOnly'>{{entity.align}}</text>
</uni-forms-item>

<uni-forms-item label='is_auto_post_back' name='isAutoPostBack'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isAutoPostBackReadOnly'>{{(($checkboxRange || [])[entity.isAutoPostBack==null?0:entity.isAutoPostBack] || {}).text || ''}}</text><template v-if='!isAutoPostBackReadOnly'><uni-data-checkbox :disabled='isAutoPostBackReadOnly' v-model='entity.isAutoPostBack' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='动态显示规则' name='visibleCondition'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<uni-easyinput primaryColor="#007aff" v-if='!visibleConditionReadOnly' v-model='entity.visibleCondition' maxlength='2000'  />
<text v-if='visibleConditionReadOnly'>{{entity.visibleCondition}}</text>
</uni-forms-item>

<uni-forms-item label='开启排序' name='isSort'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isSortReadOnly'>{{(($checkboxRange || [])[entity.isSort==null?0:entity.isSort] || {}).text || ''}}</text><template v-if='!isSortReadOnly'><uni-data-checkbox :disabled='isSortReadOnly' v-model='entity.isSort' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='虚拟字段' name='isVirtualColumn' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isVirtualColumnReadOnly'>{{(($checkboxRange || [])[entity.isVirtualColumn==null?0:entity.isVirtualColumn] || {}).text || ''}}</text><template v-if='!isVirtualColumnReadOnly'><uni-data-checkbox :disabled='isVirtualColumnReadOnly' v-model='entity.isVirtualColumn' :localdata='$checkboxRange' /></template>
</uni-forms-item>

<uni-forms-item label='移动端列表页占列数' name='mobileListPageColSpan' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='mobileListPageColSpanReadOnly'>{{entity.mobileListPageColSpanShow}}</text><template v-if='!mobileListPageColSpanReadOnly'><uni-data-checkbox :disabled='mobileListPageColSpanReadOnly' v-model='entity.mobileListPageColSpan' :localdata='mobileListPageColSpanRange' /></template>
</uni-forms-item>

<uni-forms-item label='数据库必填' name='isDbNotNull' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: ""}, entity)'>
<text v-if='isDbNotNullReadOnly'>{{(($checkboxRange || [])[entity.isDbNotNull==null?0:entity.isDbNotNull] || {}).text || ''}}</text><template v-if='!isDbNotNullReadOnly'><uni-data-checkbox :disabled='isDbNotNullReadOnly' v-model='entity.isDbNotNull' :localdata='$checkboxRange' /></template>
</uni-forms-item>

        </uni-forms>
      </view>

    </view>


    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><!--<button type='primary' size="mini" v-if="!readOnlyCondition" @click='saveEntity(null, true)'>保存并关闭</button>-->
<button size='mini' type='default' v-if='customerButtonShow1764267649526751234' @click='customerButtonHandler1764267649526751234'>往数据库创建字段</button>
<button size='mini' type='default' v-if='customerButtonShow1764274114228662273' @click='customerButtonHandler1764274114228662273'>从数据库删除字段</button>
<button size='mini' type='default' v-if='customerButtonShow2040725488384864258' @click='customerButtonHandler2040725488384864258'>清空视图控件明细配置</button></view></view>
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
      page_sn: '6217715532627968',
      parent_page_sn: '',
      formParametersName: 'formParameters6217715532627968',
      loadEntitySuccess: false,
      page_is_update:  true ,page_is_delete:  true ,page_is_new:  false ,page_is_copy:  false ,page_is_function_table:  false ,detailGridColumns: 2,permissionVersion: 0,

      columnTypeRange:[{"value":"nvarchar","text":"字符"},{"value":"int","text":"整数"},{"value":"decimal","text":"小数"},{"value":"datetime","text":"日期时间"},{"value":"varbinary","text":"二进制数据"},{"value":"point","text":"Point"}], matchTypeRange:[{"value":"P","text":"精确"},{"value":"L","text":"前匹配"},{"value":"R","text":"后匹配"},{"value":"B","text":"范围"},{"value":"M","text":"多行"},{"value":"ListToText","text":"列表输入查询"}], controlKindRange:[{"value":"","text":"--"},{"value":"List","text":"下拉列表"},{"value":"ConstList","text":"常量列表"},{"value":"ConstDownList","text":"常量下拉列表"},{"value":"CheckBox","text":"复选框"},{"value":"MultiCheckBoxDatabase","text":"复选框-数据库"},{"value":"TextArea","text":"多行输入框"},{"value":"RTF","text":"富文本"},{"value":"Upload","text":"文件上传"},{"value":"Video","text":"视频"},{"value":"Image","text":"图片"},{"value":"Base64Image","text":"Base64图片"},{"value":"MultiImage","text":"多图片"},{"value":"IntenetImage","text":"网络图片"},{"value":"SelectPage","text":"选择页"},{"value":"MultiSelectPage","text":"多选择页"},{"value":"date","text":"日期"},{"value":"month","text":"年月"},{"value":"time","text":"时间"},{"value":"date_time","text":"日期时间"},{"value":"CustomControl","text":"定制控件"},{"value":"CascadeTree","text":"级连选择"},{"value":"FileLibrary","text":"文件库"}], filterControlKindRange:[{"value":"","text":"--"},{"value":"List","text":"下拉列表"},{"value":"CheckBox","text":"复选框"}], mobileListPageColSpanRange:[{"value":1,"text":"1"},{"value":2,"text":"2"}],
      entity: {
        name:'',joinTable:'',isHideSelectGrid:'',isJoinColumn:'',columnType:'',columnConstrain:'',columnLength:'',columnFullName:'',isAutoPostBackQuery:'',saveOption:'',displayOption:'',description:'',developerRemarks:'',isAutoUpdate:'',isCopy:'',queryDefaultValue:'',columnName:'',matchType:'',width:'',controlKind:'',controlDetail:'',filterControlKind:'',sortNo:'',defaultValue:'',initValue:'',controlWidth:'',colSpan:1,align:'',isAutoPostBack:'',visibleCondition:'',isSort:'',isVirtualColumn:0,mobileListPageColSpan:'',isDbNotNull:0,
      },
      entity_column_readonly: {
        name:false,joinTable:false,isHideSelectGrid:false,isJoinColumn:false,columnType:false,columnConstrain:false,columnLength:false,columnFullName:false,isAutoPostBackQuery:false,saveOption:false,displayOption:false,description:false,developerRemarks:false,isAutoUpdate:false,isCopy:false,queryDefaultValue:false,columnName:false,matchType:false,width:false,controlKind:false,controlDetail:false,filterControlKind:false,sortNo:false,defaultValue:false,initValue:false,controlWidth:false,colSpan:false,align:false,isAutoPostBack:false,visibleCondition:false,isSort:false,isVirtualColumn:false,mobileListPageColSpan:false,isDbNotNull:false,
      },
      entity_rules: {
        name: {  rules: [  {      required: true,     errorMessage: '名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},joinTable: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isHideSelectGrid: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isJoinColumn: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},columnType: {  rules: [  {      required: true,     errorMessage: '数据类型为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},columnConstrain: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},columnLength: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},columnFullName: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isAutoPostBackQuery: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},saveOption: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},displayOption: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},description: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},developerRemarks: {  rules: [  {      maxLength: 500,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isAutoUpdate: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isCopy: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},queryDefaultValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},columnName: {  rules: [  {      required: true,     errorMessage: '列名称为必填项',  },  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},matchType: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},width: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},controlKind: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},controlDetail: {  rules: [  {      maxLength: 2000,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},filterControlKind: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},sortNo: {  rules: [  {      required: true,     errorMessage: '排序号为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},defaultValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},initValue: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},controlWidth: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},colSpan: {  rules: [  {      required: true,     errorMessage: 'col_span为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},align: {  rules: [  {      maxLength: 50,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isAutoPostBack: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},visibleCondition: {  rules: [  {      maxLength: 2000,     errorMessage: '长度需在{maxLength}个字符以内',  },  ]},isSort: {  rules: [  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isVirtualColumn: {  rules: [  {      required: true,     errorMessage: '虚拟字段为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},mobileListPageColSpan: {  rules: [  {      required: true,     errorMessage: '移动端列表页占列数为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},isDbNotNull: {  rules: [  {      required: true,     errorMessage: '数据库必填为必填项',  },  {      pattern: '^-?\\d+$',     errorMessage: '只能输入整数',  },  ]},
      }
    }
  },
  watch: {

  },
  computed: {
    readOnlyCondition() {    if (this.page_is_function_table) {        if (!this.page_is_update || this.parentReadOnlyCondition) {            return true;        }        return false;    }    var pageAllowsWrite = this.id ? this.page_is_update : this.page_is_new;    if (!pageAllowsWrite || this.parentReadOnlyCondition) {        return true;    }    return false;},nameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.name;},joinTableReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.joinTable;},isHideSelectGridReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isHideSelectGrid;},isJoinColumnReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isJoinColumn;},columnTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.columnType;},columnConstrainReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.columnConstrain;},columnLengthReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.columnLength;},columnFullNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.columnFullName;},isAutoPostBackQueryReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoPostBackQuery;},saveOptionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.saveOption;},displayOptionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.displayOption;},descriptionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.description;},developerRemarksReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.developerRemarks;},isAutoUpdateReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoUpdate;},isCopyReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isCopy;},queryDefaultValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.queryDefaultValue;},columnNameReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.columnName;},matchTypeReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.matchType;},widthReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.width;},controlKindReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.controlKind;},controlDetailReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.controlDetail;},filterControlKindReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.filterControlKind;},sortNoReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.sortNo;},defaultValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.defaultValue;},initValueReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.initValue;},controlWidthReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.controlWidth;},colSpanReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.colSpan;},alignReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.align;},isAutoPostBackReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isAutoPostBack;},visibleConditionReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.visibleCondition;},isSortReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isSort;},isVirtualColumnReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isVirtualColumn;},mobileListPageColSpanReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.mobileListPageColSpan;},isDbNotNullReadOnly(){    return this.readOnlyCondition || this.entity_column_readonly.isDbNotNull;},
    customerButtonShow1764267649526751234() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1764267649526751234",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow1764274114228662273() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"1764274114228662273",visibleScheme:"Normal",visibleRuleJson:""}, this.entity, this.readOnlyCondition);},customerButtonShow2040725488384864258() {this.permissionVersion;return yzp.isButtonVisible({buttonId:"2040725488384864258",visibleScheme:"Normal",visibleRuleJson:"null"}, this.entity, this.readOnlyCondition);},hasVisibleCustomerButtons() {this.permissionVersion;return this.customerButtonShow1764267649526751234 || this.customerButtonShow1764274114228662273 || this.customerButtonShow2040725488384864258;},
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
      formParameters.configColumn = {};
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
    receive1670726645322010626(newEntity) {    this.entity = newEntity;},customerButtonHandler1764267649526751234: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1764267649526751234", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler1764274114228662273: function() {var that = this;yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "1764274114228662273", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})},customerButtonHandler2040725488384864258: function() {var that = this;uni.showModal({content: "您确定清空视图控件明细配置吗？",success(modal) {if (modal.confirm) {that.saveEntity(function(saveResult) {yzp.request({url: "/admin/sys/config_button/execute",data: {buttonId: "2040725488384864258", entityId: that.id, entity: that.entity},sendSessionId:true,method: "POST",success: function(res) {yzp.customButtonHandler(res, that, false, 'detail');}})})}}})},showCustomerButtons: function() {var that = this;var actions = [];if (that.customerButtonShow1764267649526751234) {actions.push({name: "往数据库创建字段", handler: function() { that.customerButtonHandler1764267649526751234(); }});}if (that.customerButtonShow1764274114228662273) {actions.push({name: "从数据库删除字段", handler: function() { that.customerButtonHandler1764274114228662273(); }});}if (that.customerButtonShow2040725488384864258) {actions.push({name: "清空视图控件明细配置", handler: function() { that.customerButtonHandler2040725488384864258(); }});}if (actions.length == 0) {uni.showToast({title: '暂无可用操作', icon: 'none'});return;}uni.showActionSheet({itemList: actions.map(function(item) { return item.name; }),success: function(res) {actions[res.tapIndex].handler();}});},/* 刷新权限快照后递增版本，触发按钮显示重新计算。 */refreshCurrentPermissions: function(done) {var that = this;var finish = function() {that.permissionVersion++;if (typeof done === 'function') { done(); }};yzp.refreshCurrentPermissions(finish, finish);},



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
        url: '/admin/auto/config_column/6217715532627968/detail/' + that.id + that.buildParentIdQuery(),
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
        that.entity.configTableId = that.parent_id;
        yzp.request({
          url: '/admin/auto/config_column/6217715532627968/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_column/6217715532627968' + that.buildParentIdQuery(),
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