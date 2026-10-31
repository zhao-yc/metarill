<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="列" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="列"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'   /><text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>
<uni-forms-item label='关联表' name='joinTable'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!joinTableReadOnly' v-model='entity.joinTable' maxlength='50'   /><text v-if='joinTableReadOnly'>{{entity.joinTable}}</text>
</uni-forms-item>
<uni-forms-item label='is_hide_select_grid' name='isHideSelectGrid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isHideSelectGridReadOnly' v-model='entity.isHideSelectGrid'  type='number'  /><text v-if='isHideSelectGridReadOnly'>{{entity.isHideSelectGrid}}</text>
</uni-forms-item>
<uni-forms-item label='联接字段' name='isJoinColumn'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isJoinColumnReadOnly'>{{(($checkboxRange || [])[entity.isJoinColumn==null?0:entity.isJoinColumn] || {}).text || ''}}</text><template v-if='!isJoinColumnReadOnly'><uni-data-checkbox :disabled='isJoinColumnReadOnly' v-model='entity.isJoinColumn' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='数据类型' name='columnType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='columnTypeReadOnly'>{{entity.columnTypeShow}}</text><template v-if='!columnTypeReadOnly'><uni-data-checkbox :disabled='columnTypeReadOnly' v-model='entity.columnType' :localdata='columnTypeRange' /></template>
</uni-forms-item>
<uni-forms-item label='column_constrain' name='columnConstrain'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!columnConstrainReadOnly' v-model='entity.columnConstrain' maxlength='50'   /><text v-if='columnConstrainReadOnly'>{{entity.columnConstrain}}</text>
</uni-forms-item>
<uni-forms-item label='长度' name='columnLength'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!columnLengthReadOnly' v-model='entity.columnLength' maxlength='50'   /><text v-if='columnLengthReadOnly'>{{entity.columnLength}}</text>
</uni-forms-item>
<uni-forms-item label='列全名' name='columnFullName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!columnFullNameReadOnly' v-model='entity.columnFullName' maxlength='50'   /><text v-if='columnFullNameReadOnly'>{{entity.columnFullName}}</text>
</uni-forms-item>
<uni-forms-item label='is_auto_post_back_query' name='isAutoPostBackQuery' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAutoPostBackQueryReadOnly'>{{(($checkboxRange || [])[entity.isAutoPostBackQuery==null?0:entity.isAutoPostBackQuery] || {}).text || ''}}</text><template v-if='!isAutoPostBackQueryReadOnly'><uni-data-checkbox :disabled='isAutoPostBackQueryReadOnly' v-model='entity.isAutoPostBackQuery' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='保存选项' name='saveOption'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!saveOptionReadOnly' v-model='entity.saveOption' maxlength='50'   /><text v-if='saveOptionReadOnly'>{{entity.saveOption}}</text>
</uni-forms-item>
<uni-forms-item label='display_option' name='displayOption'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!displayOptionReadOnly' v-model='entity.displayOption' maxlength='50'   /><text v-if='displayOptionReadOnly'>{{entity.displayOption}}</text>
</uni-forms-item>
<uni-forms-item label='字段描述' name='description'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!descriptionReadOnly' v-model='entity.description' maxlength='500'   /><text v-if='descriptionReadOnly'>{{entity.description}}</text>
</uni-forms-item>
<uni-forms-item label='开发者备注' name='developerRemarks'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!developerRemarksReadOnly' v-model='entity.developerRemarks' maxlength='500'   /><text v-if='developerRemarksReadOnly'>{{entity.developerRemarks}}</text>
</uni-forms-item>
<uni-forms-item label='is_auto_update' name='isAutoUpdate'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isAutoUpdateReadOnly' v-model='entity.isAutoUpdate'  type='number'  /><text v-if='isAutoUpdateReadOnly'>{{entity.isAutoUpdate}}</text>
</uni-forms-item>
<uni-forms-item label='复制' name='isCopy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isCopyReadOnly' v-model='entity.isCopy' maxlength='50'   /><text v-if='isCopyReadOnly'>{{entity.isCopy}}</text>
</uni-forms-item>
<uni-forms-item label='query_default_value' name='queryDefaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!queryDefaultValueReadOnly' v-model='entity.queryDefaultValue' maxlength='50'   /><text v-if='queryDefaultValueReadOnly'>{{entity.queryDefaultValue}}</text>
</uni-forms-item>
<uni-forms-item label='列名称' name='columnName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!columnNameReadOnly' v-model='entity.columnName' maxlength='50'   /><text v-if='columnNameReadOnly'>{{entity.columnName}}</text>
</uni-forms-item>
<uni-forms-item label='匹配类型' name='matchType'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='matchTypeReadOnly'>{{entity.matchTypeShow}}</text><template v-if='!matchTypeReadOnly'><uni-data-checkbox :disabled='matchTypeReadOnly' v-model='entity.matchType' :localdata='matchTypeRange' /></template>
</uni-forms-item>
<uni-forms-item label='width' name='width'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!widthReadOnly' v-model='entity.width' maxlength='50'   /><text v-if='widthReadOnly'>{{entity.width}}</text>
</uni-forms-item>
<uni-forms-item label='config_table_name' name='configTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!configTableNameReadOnly' v-model='entity.configTableName' maxlength='50'   /><text v-if='configTableNameReadOnly'>{{entity.configTableName}}</text>
</uni-forms-item>
<uni-forms-item label='is_in_grid' name='isInGrid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isInGridReadOnly' v-model='entity.isInGrid'  type='number'  /><text v-if='isInGridReadOnly'>{{entity.isInGrid}}</text>
</uni-forms-item>
<uni-forms-item label='is_in_filter' name='isInFilter'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isInFilterReadOnly' v-model='entity.isInFilter'  type='number'  /><text v-if='isInFilterReadOnly'>{{entity.isInFilter}}</text>
</uni-forms-item>
<uni-forms-item label='is_in_edit' name='isInEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isInEditReadOnly' v-model='entity.isInEdit'  type='number'  /><text v-if='isInEditReadOnly'>{{entity.isInEdit}}</text>
</uni-forms-item>
<uni-forms-item label='控件类型' name='controlKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='controlKindReadOnly'>{{entity.controlKindShow}}</text><template v-if='!controlKindReadOnly'><uni-data-checkbox :disabled='controlKindReadOnly' v-model='entity.controlKind' :localdata='controlKindRange' /></template>
</uni-forms-item>
<uni-forms-item label='is_read_only' name='isReadOnly'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isReadOnlyReadOnly' v-model='entity.isReadOnly'  type='number'  /><text v-if='isReadOnlyReadOnly'>{{entity.isReadOnly}}</text>
</uni-forms-item>
<uni-forms-item label='控件明细' name='controlDetail'  style='grid-column: span 2;' v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<yzp-control-detail-editor v-if='!controlDetailReadOnly' :entity="entity" :record-id="id" :readonly="controlDetailReadOnly" @receive="receive1670726645322010626" />
</uni-forms-item>
<uni-forms-item label='is_must' name='isMust'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isMustReadOnly' v-model='entity.isMust'  type='number'  /><text v-if='isMustReadOnly'>{{entity.isMust}}</text>
</uni-forms-item>
<uni-forms-item label='控件类型-筛选' name='filterControlKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='filterControlKindReadOnly'>{{entity.filterControlKindShow}}</text><template v-if='!filterControlKindReadOnly'><uni-data-checkbox :disabled='filterControlKindReadOnly' v-model='entity.filterControlKind' :localdata='filterControlKindRange' /></template>
</uni-forms-item>
<uni-forms-item label='link_config_table_name' name='linkConfigTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!linkConfigTableNameReadOnly' v-model='entity.linkConfigTableName' maxlength='50'   /><text v-if='linkConfigTableNameReadOnly'>{{entity.linkConfigTableName}}</text>
</uni-forms-item>
<uni-forms-item label='link_config_page_name' name='linkConfigPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!linkConfigPageNameReadOnly' v-model='entity.linkConfigPageName' maxlength='50'   /><text v-if='linkConfigPageNameReadOnly'>{{entity.linkConfigPageName}}</text>
</uni-forms-item>
<uni-forms-item label='排序号' name='sortNo' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sortNoReadOnly' v-model='entity.sortNo'  type='number'  /><text v-if='sortNoReadOnly'>{{entity.sortNo}}</text>
</uni-forms-item>
<uni-forms-item label='默认值' name='defaultValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!defaultValueReadOnly' v-model='entity.defaultValue' maxlength='50'   /><text v-if='defaultValueReadOnly'>{{entity.defaultValue}}</text>
</uni-forms-item>
<uni-forms-item label='yz_project_id' name='yzProjectId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectIdReadOnly' v-model='entity.yzProjectId'    /><text v-if='yzProjectIdReadOnly'>{{entity.yzProjectId}}</text>
</uni-forms-item>
<uni-forms-item label='新建初始值' name='initValue'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!initValueReadOnly' v-model='entity.initValue' maxlength='50'   /><text v-if='initValueReadOnly'>{{entity.initValue}}</text>
</uni-forms-item>
<uni-forms-item label='yz_project_name' name='yzProjectName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectNameReadOnly' v-model='entity.yzProjectName' maxlength='50'   /><text v-if='yzProjectNameReadOnly'>{{entity.yzProjectName}}</text>
</uni-forms-item>
<uni-forms-item label='is_print' name='isPrint'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isPrintReadOnly' v-model='entity.isPrint'  type='number'  /><text v-if='isPrintReadOnly'>{{entity.isPrint}}</text>
</uni-forms-item>
<uni-forms-item label='import_no' name='importNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importNoReadOnly' v-model='entity.importNo' maxlength='45'   /><text v-if='importNoReadOnly'>{{entity.importNo}}</text>
</uni-forms-item>
<uni-forms-item label='control_width' name='controlWidth'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!controlWidthReadOnly' v-model='entity.controlWidth' maxlength='50'   /><text v-if='controlWidthReadOnly'>{{entity.controlWidth}}</text>
</uni-forms-item>
<uni-forms-item label='col_span' name='colSpan' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!colSpanReadOnly' v-model='entity.colSpan'  type='number'  /><text v-if='colSpanReadOnly'>{{entity.colSpan}}</text>
</uni-forms-item>
<uni-forms-item label='align' name='align'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!alignReadOnly' v-model='entity.align' maxlength='50'   /><text v-if='alignReadOnly'>{{entity.align}}</text>
</uni-forms-item>
<uni-forms-item label='is_auto_post_back' name='isAutoPostBack'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isAutoPostBackReadOnly'>{{(($checkboxRange || [])[entity.isAutoPostBack==null?0:entity.isAutoPostBack] || {}).text || ''}}</text><template v-if='!isAutoPostBackReadOnly'><uni-data-checkbox :disabled='isAutoPostBackReadOnly' v-model='entity.isAutoPostBack' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='动态显示规则' name='visibleCondition'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!visibleConditionReadOnly' v-model='entity.visibleCondition' maxlength='2000'   /><text v-if='visibleConditionReadOnly'>{{entity.visibleCondition}}</text>
</uni-forms-item>
<uni-forms-item label='开启排序' name='isSort' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isSortReadOnly'>{{(($checkboxRange || [])[entity.isSort==null?0:entity.isSort] || {}).text || ''}}</text><template v-if='!isSortReadOnly'><uni-data-checkbox :disabled='isSortReadOnly' v-model='entity.isSort' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='sum_kind' name='sumKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sumKindReadOnly' v-model='entity.sumKind' maxlength='50'   /><text v-if='sumKindReadOnly'>{{entity.sumKind}}</text>
</uni-forms-item>
<uni-forms-item label='虚拟字段' name='isVirtualColumn' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isVirtualColumnReadOnly'>{{(($checkboxRange || [])[entity.isVirtualColumn==null?0:entity.isVirtualColumn] || {}).text || ''}}</text><template v-if='!isVirtualColumnReadOnly'><uni-data-checkbox :disabled='isVirtualColumnReadOnly' v-model='entity.isVirtualColumn' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='逻辑类型' name='logicalType' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!logicalTypeReadOnly' v-model='entity.logicalType' maxlength='30'   /><text v-if='logicalTypeReadOnly'>{{entity.logicalType}}</text>
</uni-forms-item>
<uni-forms-item label='移动端列表页占列数' name='mobileListPageColSpan' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='mobileListPageColSpanReadOnly'>{{entity.mobileListPageColSpanShow}}</text><template v-if='!mobileListPageColSpanReadOnly'><uni-data-checkbox :disabled='mobileListPageColSpanReadOnly' v-model='entity.mobileListPageColSpan' :localdata='mobileListPageColSpanRange' /></template>
</uni-forms-item>
<uni-forms-item label='数值精度' name='precision'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!precisionReadOnly' v-model='entity.precision'  type='number'  /><text v-if='precisionReadOnly'>{{entity.precision}}</text>
</uni-forms-item>
<uni-forms-item label='数据库必填' name='isDbNotNull' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isDbNotNullReadOnly'>{{(($checkboxRange || [])[entity.isDbNotNull==null?0:entity.isDbNotNull] || {}).text || ''}}</text><template v-if='!isDbNotNullReadOnly'><uni-data-checkbox :disabled='isDbNotNullReadOnly' v-model='entity.isDbNotNull' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='小数位数' name='scale'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!scaleReadOnly' v-model='entity.scale'  type='number'  /><text v-if='scaleReadOnly'>{{entity.scale}}</text>
</uni-forms-item>

        </uni-forms>
      </view>
      
    </view>
    
    
    <view style="height:150px;" v-if="!is_props"></view><view><view class='bottom-buttons' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><button size='mini' type='default' v-if='customerButtonShow1764267649526751234' @click='customerButtonHandler1764267649526751234'>往数据库创建字段</button>
<button size='mini' type='default' v-if='customerButtonShow1764274114228662273' @click='customerButtonHandler1764274114228662273'>从数据库删除字段</button>
<button size='mini' type='default' v-if='customerButtonShow2040725488384864258' @click='customerButtonHandler2040725488384864258'>清空视图控件明细配置</button>
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
      page_sn: '6217715532627968',
      parent_page_sn: '',
      formParametersName: 'formParameters6217715532627968',
      loadEntitySuccess: false,
      columnTypeRange:[{"text":"\u5B57\u7B26","value":"nvarchar"},{"text":"\u6574\u6570","value":"int"},{"text":"\u5C0F\u6570","value":"decimal"},{"text":"\u65E5\u671F\u65F6\u95F4","value":"datetime"},{"text":"\u4E8C\u8FDB\u5236\u6570\u636E","value":"varbinary"},{"text":"Point","value":"point"}],matchTypeRange:[{"text":"\u7CBE\u786E","value":"P"},{"text":"\u524D\u5339\u914D","value":"L"},{"text":"\u540E\u5339\u914D","value":"R"},{"text":"\u8303\u56F4","value":"B"},{"text":"\u591A\u884C","value":"M"},{"text":"\u5217\u8868\u8F93\u5165\u67E5\u8BE2","value":"ListToText"}],controlKindRange:[{"text":"--","value":""},{"text":"\u4E0B\u62C9\u5217\u8868","value":"List"},{"text":"\u5E38\u91CF\u5217\u8868","value":"ConstList"},{"text":"\u5E38\u91CF\u4E0B\u62C9\u5217\u8868","value":"ConstDownList"},{"text":"\u590D\u9009\u6846","value":"CheckBox"},{"text":"\u590D\u9009\u6846-\u6570\u636E\u5E93","value":"MultiCheckBoxDatabase"},{"text":"\u591A\u884C\u8F93\u5165\u6846","value":"TextArea"},{"text":"\u5BCC\u6587\u672C","value":"RTF"},{"text":"\u6587\u4EF6\u4E0A\u4F20","value":"Upload"},{"text":"\u89C6\u9891","value":"Video"},{"text":"\u56FE\u7247","value":"Image"},{"text":"Base64\u56FE\u7247","value":"Base64Image"},{"text":"\u591A\u56FE\u7247","value":"MultiImage"},{"text":"\u7F51\u7EDC\u56FE\u7247","value":"IntenetImage"},{"text":"\u9009\u62E9\u9875","value":"SelectPage"},{"text":"\u591A\u9009\u62E9\u9875","value":"MultiSelectPage"},{"text":"\u65E5\u671F","value":"date"},{"text":"\u5E74\u6708","value":"month"},{"text":"\u65F6\u95F4","value":"time"},{"text":"\u65E5\u671F\u65F6\u95F4","value":"date_time"},{"text":"\u5B9A\u5236\u63A7\u4EF6","value":"CustomControl"},{"text":"\u7EA7\u8FDE\u9009\u62E9","value":"CascadeTree"},{"text":"\u6587\u4EF6\u5E93","value":"FileLibrary"}],filterControlKindRange:[{"text":"--","value":""},{"text":"\u4E0B\u62C9\u5217\u8868","value":"List"},{"text":"\u590D\u9009\u6846","value":"CheckBox"}],mobileListPageColSpanRange:[{"text":"1","value":1},{"text":"2","value":2}],originalButtons:[{"id":"1764267649526751234","name":"\u5F80\u6570\u636E\u5E93\u521B\u5EFA\u5B57\u6BB5","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_column/add_column_to_db","close":false},{"id":"1764274114228662273","name":"\u4ECE\u6570\u636E\u5E93\u5220\u9664\u5B57\u6BB5","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_column/drop_column_to_db","close":false},{"id":"2040725488384864258","name":"\u6E05\u7A7A\u89C6\u56FE\u63A7\u4EF6\u660E\u7EC6\u914D\u7F6E","action":1,"save":"Save","confirm":true,"confirmMsg":"","target":"/admin/custom/config_column/clear_view_control_detail","close":false}],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:false,page_is_copy:false,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        name:"",joinTable:"",isHideSelectGrid:"",isJoinColumn:"",columnType:"",columnConstrain:"",columnLength:"",columnFullName:"",isAutoPostBackQuery:"",saveOption:"",displayOption:"",description:"",developerRemarks:"",isAutoUpdate:"",isCopy:"",queryDefaultValue:"",columnName:"",matchType:"",width:"",configTableName:"",isInGrid:"",isInFilter:"",isInEdit:"",controlKind:"",isReadOnly:"",controlDetail:"",isMust:"",filterControlKind:"",linkConfigTableName:"",linkConfigPageName:"",sortNo:"",defaultValue:"",yzProjectId:"",initValue:"",yzProjectName:"",isPrint:"",importNo:"",controlWidth:"",colSpan:1,align:"",isAutoPostBack:"",visibleCondition:"",isSort:"",sumKind:"",isVirtualColumn:0,logicalType:"string",mobileListPageColSpan:"",precision:"",isDbNotNull:0,scale:"",
      },
      entity_column_readonly: {
        name:false,joinTable:false,isHideSelectGrid:false,isJoinColumn:false,columnType:false,columnConstrain:false,columnLength:false,columnFullName:false,isAutoPostBackQuery:false,saveOption:false,displayOption:false,description:false,developerRemarks:false,isAutoUpdate:false,isCopy:false,queryDefaultValue:false,columnName:false,matchType:false,width:false,configTableName:false,isInGrid:false,isInFilter:false,isInEdit:false,controlKind:false,isReadOnly:false,controlDetail:false,isMust:false,filterControlKind:false,linkConfigTableName:false,linkConfigPageName:false,sortNo:false,defaultValue:false,yzProjectId:true,initValue:false,yzProjectName:true,isPrint:false,importNo:false,controlWidth:false,colSpan:false,align:false,isAutoPostBack:false,visibleCondition:false,isSort:false,sumKind:false,isVirtualColumn:false,logicalType:false,mobileListPageColSpan:false,precision:false,isDbNotNull:false,scale:false,
      },
      entity_rules: {
        name:{rules:[{"required":true,"errorMessage":"\u540D\u79F0\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},joinTable:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isHideSelectGrid:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isJoinColumn:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},columnType:{rules:[{"required":true,"errorMessage":"\u6570\u636E\u7C7B\u578B\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},columnConstrain:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},columnLength:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},columnFullName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAutoPostBackQuery:{rules:[{"required":true,"errorMessage":"is_auto_post_back_query\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},saveOption:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},displayOption:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},description:{rules:[{"maxLength":500,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},developerRemarks:{rules:[{"maxLength":500,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAutoUpdate:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isCopy:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},queryDefaultValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},columnName:{rules:[{"required":true,"errorMessage":"\u5217\u540D\u79F0\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},matchType:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},width:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},configTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isInGrid:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isInFilter:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isInEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},controlKind:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isReadOnly:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},controlDetail:{rules:[{"maxLength":2000,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isMust:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},filterControlKind:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},linkConfigTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},linkConfigPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},sortNo:{rules:[{"required":true,"errorMessage":"\u6392\u5E8F\u53F7\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},defaultValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzProjectId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},initValue:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},yzProjectName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isPrint:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},importNo:{rules:[{"maxLength":45,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},controlWidth:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},colSpan:{rules:[{"required":true,"errorMessage":"col_span\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},align:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isAutoPostBack:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},visibleCondition:{rules:[{"maxLength":2000,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isSort:{rules:[{"required":true,"errorMessage":"\u5F00\u542F\u6392\u5E8F\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},sumKind:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isVirtualColumn:{rules:[{"required":true,"errorMessage":"\u865A\u62DF\u5B57\u6BB5\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},logicalType:{rules:[{"required":true,"errorMessage":"\u903B\u8F91\u7C7B\u578B\u4E3A\u5FC5\u586B\u9879"},{"maxLength":30,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},mobileListPageColSpan:{rules:[{"required":true,"errorMessage":"\u79FB\u52A8\u7AEF\u5217\u8868\u9875\u5360\u5217\u6570\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},precision:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isDbNotNull:{rules:[{"required":true,"errorMessage":"\u6570\u636E\u5E93\u5FC5\u586B\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},scale:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    nameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.name; },
joinTableReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.joinTable; },
isHideSelectGridReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isHideSelectGrid; },
isJoinColumnReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isJoinColumn; },
columnTypeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.columnType; },
columnConstrainReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.columnConstrain; },
columnLengthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.columnLength; },
columnFullNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.columnFullName; },
isAutoPostBackQueryReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoPostBackQuery; },
saveOptionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.saveOption; },
displayOptionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.displayOption; },
descriptionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.description; },
developerRemarksReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.developerRemarks; },
isAutoUpdateReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoUpdate; },
isCopyReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isCopy; },
queryDefaultValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.queryDefaultValue; },
columnNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.columnName; },
matchTypeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.matchType; },
widthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.width; },
configTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.configTableName; },
isInGridReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isInGrid; },
isInFilterReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isInFilter; },
isInEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isInEdit; },
controlKindReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.controlKind; },
isReadOnlyReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isReadOnly; },
controlDetailReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.controlDetail; },
isMustReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isMust; },
filterControlKindReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.filterControlKind; },
linkConfigTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.linkConfigTableName; },
linkConfigPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.linkConfigPageName; },
sortNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sortNo; },
defaultValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.defaultValue; },
yzProjectIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectId; },
initValueReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.initValue; },
yzProjectNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectName; },
isPrintReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isPrint; },
importNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importNo; },
controlWidthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.controlWidth; },
colSpanReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.colSpan; },
alignReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.align; },
isAutoPostBackReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isAutoPostBack; },
visibleConditionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.visibleCondition; },
isSortReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSort; },
sumKindReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sumKind; },
isVirtualColumnReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isVirtualColumn; },
logicalTypeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.logicalType; },
mobileListPageColSpanReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.mobileListPageColSpan; },
precisionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.precision; },
isDbNotNullReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isDbNotNull; },
scaleReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.scale; },
customerButtonShow1764267649526751234() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1764267649526751234","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow1764274114228662273() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1764274114228662273","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow2040725488384864258() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"2040725488384864258","visibleScheme":"Normal","visibleRuleJson":"null"},this.entity,this.readOnlyCondition); },
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
      uni.$emit('yzpMenuChanged', { pageId: '6217715532627968', tableName: 'config_column' });
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
      formParameters.configColumn = {};
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
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={name:"",joinTable:"",isHideSelectGrid:"",isJoinColumn:"",columnType:"",columnConstrain:"",columnLength:"",columnFullName:"",isAutoPostBackQuery:"",saveOption:"",displayOption:"",description:"",developerRemarks:"",isAutoUpdate:"",isCopy:"",queryDefaultValue:"",columnName:"",matchType:"",width:"",configTableName:"",isInGrid:"",isInFilter:"",isInEdit:"",controlKind:"",isReadOnly:"",controlDetail:"",isMust:"",filterControlKind:"",linkConfigTableName:"",linkConfigPageName:"",sortNo:"",defaultValue:"",yzProjectId:"",initValue:"",yzProjectName:"",isPrint:"",importNo:"",controlWidth:"",colSpan:1,align:"",isAutoPostBack:"",visibleCondition:"",isSort:"",sumKind:"",isVirtualColumn:0,logicalType:"string",mobileListPageColSpan:"",precision:"",isDbNotNull:0,scale:"",};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
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
 const mutation=route==="/admin/auto/config_column/6217715532627968"&&method==='DELETE'||method==='POST'&&["/admin/auto/config_column/6217715532627968"+'/add',"/admin/auto/config_column/6217715532627968"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/config_column/6217715532627968"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/config_column/6217715532627968"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["name","joinTable","isHideSelectGrid","isJoinColumn","columnType","columnConstrain","columnLength","columnFullName","isAutoPostBackQuery","saveOption","displayOption","description","developerRemarks","isAutoUpdate","isCopy","queryDefaultValue","columnName","matchType","width","configTableName","isInGrid","isInFilter","isInEdit","controlKind","isReadOnly","controlDetail","isMust","filterControlKind","linkConfigTableName","linkConfigPageName","sortNo","defaultValue","initValue","isPrint","importNo","controlWidth","colSpan","align","isAutoPostBack","visibleCondition","isSort","sumKind","isVirtualColumn","logicalType","mobileListPageColSpan","precision","isDbNotNull","scale"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/config_column/6217715532627968"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
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

    receive1670726645322010626(newEntity) { if(this.controlDetailReadOnly) return; this.entity=newEntity; },
customerButtonHandler1764267649526751234() { this.runOriginalButton("1764267649526751234"); },
customerButtonHandler1764274114228662273() { this.runOriginalButton("1764274114228662273"); },
customerButtonHandler2040725488384864258() { this.runOriginalButton("2040725488384864258"); },
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
        url: '/admin/auto/config_column/6217715532627968/detail/' + that.id + that.buildParentIdQuery(),
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
        that.entity.configTableId=that.parent_id;
        that.originalRequest({
          url: '/admin/auto/config_column/6217715532627968/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_column/6217715532627968' + that.buildParentIdQuery(),
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
