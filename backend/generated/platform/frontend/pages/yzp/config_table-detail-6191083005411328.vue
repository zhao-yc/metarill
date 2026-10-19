<template>
  <view class='detail-page' :class="{ ispc: $deviceInfo.is_pc, ismobile: !$deviceInfo.is_pc }" :style="$deviceInfo.is_pc ? {'--detail-grid-columns': detailGridColumns} : null">
    <view v-if="!is_props"><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-if='!readOnlyCondition' :border="false" title="表" @clickRight="showCustomerButtons" :rightIcon="hasVisibleCustomerButtons ? 'more-filled' : ''"></uni-nav-bar><uni-nav-bar left-icon="left" @clickLeft="$yzp.back" fixed statusBar v-else :border="false" title="表"></uni-nav-bar></view>
    <view class="base-info" type='line'>
      <view class='section-view'>
        <uni-forms ref='entityForm' :modelValue='entity' :rules="entity_rules" label-width="96px">
          <uni-forms-item label='名称' name='name' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!nameReadOnly' v-model='entity.name' maxlength='50'   /><text v-if='nameReadOnly'>{{entity.name}}</text>
</uni-forms-item>
<uni-forms-item label='表名' name='tableName' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!tableNameReadOnly' v-model='entity.tableName' maxlength='50'   /><text v-if='tableNameReadOnly'>{{entity.tableName}}</text>
</uni-forms-item>
<uni-forms-item label='存储模式' name='isRongyu' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<yzp-table-storage-mode :entity="entity" :record-id="id" :readonly="isRongyuReadOnly" @receive="receive1671029182696714241" />
</uni-forms-item>
<uni-forms-item label='功能表' name='isFunctionTable' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isFunctionTableReadOnly'>{{(($checkboxRange || [])[entity.isFunctionTable==null?0:entity.isFunctionTable] || {}).text || ''}}</text><template v-if='!isFunctionTableReadOnly'><uni-data-checkbox :disabled='isFunctionTableReadOnly' v-model='entity.isFunctionTable' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='sql' name='sql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sqlReadOnly' v-model='entity.sql' maxlength='-1'   /><text v-if='sqlReadOnly'>{{entity.sql}}</text>
</uni-forms-item>
<uni-forms-item label='数据库视图' name='isView' required v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='isViewReadOnly'>{{(($checkboxRange || [])[entity.isView==null?0:entity.isView] || {}).text || ''}}</text><template v-if='!isViewReadOnly'><uni-data-checkbox :disabled='isViewReadOnly' v-model='entity.isView' :localdata='$checkboxRange' /></template>
</uni-forms-item>
<uni-forms-item label='oracle_sql' name='oracleSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!oracleSqlReadOnly' v-model='entity.oracleSql' maxlength='-1'   /><text v-if='oracleSqlReadOnly'>{{entity.oracleSql}}</text>
</uni-forms-item>
<uni-forms-item label='where_sql' name='whereSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!whereSqlReadOnly' v-model='entity.whereSql' maxlength='-1'   /><text v-if='whereSqlReadOnly'>{{entity.whereSql}}</text>
</uni-forms-item>
<uni-forms-item label='order_by' name='orderBy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!orderByReadOnly' v-model='entity.orderBy' maxlength='-1'   /><text v-if='orderByReadOnly'>{{entity.orderBy}}</text>
</uni-forms-item>
<uni-forms-item label='group_by' name='groupBy'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!groupByReadOnly' v-model='entity.groupBy' maxlength='-1'   /><text v-if='groupByReadOnly'>{{entity.groupBy}}</text>
</uni-forms-item>
<uni-forms-item label='page_size' name='pageSize'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!pageSizeReadOnly' v-model='entity.pageSize'  type='number'  /><text v-if='pageSizeReadOnly'>{{entity.pageSize}}</text>
</uni-forms-item>
<uni-forms-item label='is_edit' name='isEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isEditReadOnly' v-model='entity.isEdit'  type='number'  /><text v-if='isEditReadOnly'>{{entity.isEdit}}</text>
</uni-forms-item>
<uni-forms-item label='is_new' name='isNew'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isNewReadOnly' v-model='entity.isNew'  type='number'  /><text v-if='isNewReadOnly'>{{entity.isNew}}</text>
</uni-forms-item>
<uni-forms-item label='is_save' name='isSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isSaveReadOnly' v-model='entity.isSave'  type='number'  /><text v-if='isSaveReadOnly'>{{entity.isSave}}</text>
</uni-forms-item>
<uni-forms-item label='is_delete' name='isDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isDeleteReadOnly' v-model='entity.isDelete'  type='number'  /><text v-if='isDeleteReadOnly'>{{entity.isDelete}}</text>
</uni-forms-item>
<uni-forms-item label='is_search' name='isSearch'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isSearchReadOnly' v-model='entity.isSearch'  type='number'  /><text v-if='isSearchReadOnly'>{{entity.isSearch}}</text>
</uni-forms-item>
<uni-forms-item label='before_delete_sql' name='beforeDeleteSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!beforeDeleteSqlReadOnly' v-model='entity.beforeDeleteSql' maxlength='-1'   /><text v-if='beforeDeleteSqlReadOnly'>{{entity.beforeDeleteSql}}</text>
</uni-forms-item>
<uni-forms-item label='after_save_sql' name='afterSaveSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!afterSaveSqlReadOnly' v-model='entity.afterSaveSql' maxlength='-1'   /><text v-if='afterSaveSqlReadOnly'>{{entity.afterSaveSql}}</text>
</uni-forms-item>
<uni-forms-item label='after_import_sql' name='afterImportSql'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!afterImportSqlReadOnly' v-model='entity.afterImportSql' maxlength='-1'   /><text v-if='afterImportSqlReadOnly'>{{entity.afterImportSql}}</text>
</uni-forms-item>
<uni-forms-item label='is_import' name='isImport'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isImportReadOnly' v-model='entity.isImport'  type='number'  /><text v-if='isImportReadOnly'>{{entity.isImport}}</text>
</uni-forms-item>
<uni-forms-item label='is_export' name='isExport'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isExportReadOnly' v-model='entity.isExport'  type='number'  /><text v-if='isExportReadOnly'>{{entity.isExport}}</text>
</uni-forms-item>
<uni-forms-item label='is_multi_select' name='isMultiSelect'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isMultiSelectReadOnly' v-model='entity.isMultiSelect'  type='number'  /><text v-if='isMultiSelectReadOnly'>{{entity.isMultiSelect}}</text>
</uni-forms-item>
<uni-forms-item label='is_lazy_query' name='isLazyQuery'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isLazyQueryReadOnly' v-model='entity.isLazyQuery'  type='number'  /><text v-if='isLazyQueryReadOnly'>{{entity.isLazyQuery}}</text>
</uni-forms-item>
<uni-forms-item label='grid_edit' name='gridEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!gridEditReadOnly' v-model='entity.gridEdit' maxlength='50'   /><text v-if='gridEditReadOnly'>{{entity.gridEdit}}</text>
</uni-forms-item>
<uni-forms-item label='title' name='title'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!titleReadOnly' v-model='entity.title' maxlength='50'   /><text v-if='titleReadOnly'>{{entity.title}}</text>
</uni-forms-item>
<uni-forms-item label='title_2' name='title2'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!title2ReadOnly' v-model='entity.title2' maxlength='50'   /><text v-if='title2ReadOnly'>{{entity.title2}}</text>
</uni-forms-item>
<uni-forms-item label='title_3' name='title3'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!title3ReadOnly' v-model='entity.title3' maxlength='50'   /><text v-if='title3ReadOnly'>{{entity.title3}}</text>
</uni-forms-item>
<uni-forms-item label='is_sum' name='isSum'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isSumReadOnly' v-model='entity.isSum'  type='number'  /><text v-if='isSumReadOnly'>{{entity.isSum}}</text>
</uni-forms-item>
<uni-forms-item label='grid_width' name='gridWidth'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!gridWidthReadOnly' v-model='entity.gridWidth' maxlength='10'   /><text v-if='gridWidthReadOnly'>{{entity.gridWidth}}</text>
</uni-forms-item>
<uni-forms-item label='is_print_preview' name='isPrintPreview'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isPrintPreviewReadOnly' v-model='entity.isPrintPreview'  type='number'  /><text v-if='isPrintPreviewReadOnly'>{{entity.isPrintPreview}}</text>
</uni-forms-item>
<uni-forms-item label='is_batch_delete' name='isBatchDelete'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isBatchDeleteReadOnly' v-model='entity.isBatchDelete'  type='number'  /><text v-if='isBatchDeleteReadOnly'>{{entity.isBatchDelete}}</text>
</uni-forms-item>
<uni-forms-item label='import_method' name='importMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importMethodReadOnly' v-model='entity.importMethod' maxlength='50'   /><text v-if='importMethodReadOnly'>{{entity.importMethod}}</text>
</uni-forms-item>
<uni-forms-item label='read_only_condition' name='readOnlyCondition'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!readOnlyConditionReadOnly' v-model='entity.readOnlyCondition' maxlength='50'   /><text v-if='readOnlyConditionReadOnly'>{{entity.readOnlyCondition}}</text>
</uni-forms-item>
<uni-forms-item label='pop_window_width' name='popWindowWidth'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!popWindowWidthReadOnly' v-model='entity.popWindowWidth'  type='number'  /><text v-if='popWindowWidthReadOnly'>{{entity.popWindowWidth}}</text>
</uni-forms-item>
<uni-forms-item label='pop_window_height' name='popWindowHeight'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!popWindowHeightReadOnly' v-model='entity.popWindowHeight'  type='number'  /><text v-if='popWindowHeightReadOnly'>{{entity.popWindowHeight}}</text>
</uni-forms-item>
<uni-forms-item label='is_export_edit' name='isExportEdit'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isExportEditReadOnly' v-model='entity.isExportEdit'  type='number'  /><text v-if='isExportEditReadOnly'>{{entity.isExportEdit}}</text>
</uni-forms-item>
<uni-forms-item label='is_no_save_and_close' name='isNoSaveAndClose'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isNoSaveAndCloseReadOnly' v-model='entity.isNoSaveAndClose'  type='number'  /><text v-if='isNoSaveAndCloseReadOnly'>{{entity.isNoSaveAndClose}}</text>
</uni-forms-item>
<uni-forms-item label='is_update' name='isUpdate'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isUpdateReadOnly' v-model='entity.isUpdate'  type='number'  /><text v-if='isUpdateReadOnly'>{{entity.isUpdate}}</text>
</uni-forms-item>
<uni-forms-item label='is_head_group' name='isHeadGroup'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isHeadGroupReadOnly' v-model='entity.isHeadGroup'  type='number'  /><text v-if='isHeadGroupReadOnly'>{{entity.isHeadGroup}}</text>
</uni-forms-item>
<uni-forms-item label='is_modify_log' name='isModifyLog'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isModifyLogReadOnly' v-model='entity.isModifyLog'  type='number'  /><text v-if='isModifyLogReadOnly'>{{entity.isModifyLog}}</text>
</uni-forms-item>
<uni-forms-item label='layout' name='layout'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!layoutReadOnly' v-model='entity.layout' maxlength='10'   /><text v-if='layoutReadOnly'>{{entity.layout}}</text>
</uni-forms-item>
<uni-forms-item label='conn_string' name='connString'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!connStringReadOnly' v-model='entity.connString' maxlength='100'   /><text v-if='connStringReadOnly'>{{entity.connString}}</text>
</uni-forms-item>
<uni-forms-item label='export_method' name='exportMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!exportMethodReadOnly' v-model='entity.exportMethod' maxlength='10'   /><text v-if='exportMethodReadOnly'>{{entity.exportMethod}}</text>
</uni-forms-item>
<uni-forms-item label='import_no' name='importNo'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!importNoReadOnly' v-model='entity.importNo' maxlength='50'   /><text v-if='importNoReadOnly'>{{entity.importNo}}</text>
</uni-forms-item>
<uni-forms-item label='copy_method' name='copyMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!copyMethodReadOnly' v-model='entity.copyMethod' maxlength='20'   /><text v-if='copyMethodReadOnly'>{{entity.copyMethod}}</text>
</uni-forms-item>
<uni-forms-item label='sql_more' name='sqlMore'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sqlMoreReadOnly' v-model='entity.sqlMore' maxlength='-1'   /><text v-if='sqlMoreReadOnly'>{{entity.sqlMore}}</text>
</uni-forms-item>
<uni-forms-item label='authorize_scheme' name='authorizeScheme'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!authorizeSchemeReadOnly' v-model='entity.authorizeScheme' maxlength='20'   /><text v-if='authorizeSchemeReadOnly'>{{entity.authorizeScheme}}</text>
</uni-forms-item>
<uni-forms-item label='clear_method' name='clearMethod'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!clearMethodReadOnly' v-model='entity.clearMethod' maxlength='20'   /><text v-if='clearMethodReadOnly'>{{entity.clearMethod}}</text>
</uni-forms-item>
<uni-forms-item label='is_raw' name='isRaw'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isRawReadOnly' v-model='entity.isRaw'  type='number'  /><text v-if='isRawReadOnly'>{{entity.isRaw}}</text>
</uni-forms-item>
<uni-forms-item label='same_edit_code' name='sameEditCode'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sameEditCodeReadOnly' v-model='entity.sameEditCode' maxlength='10'   /><text v-if='sameEditCodeReadOnly'>{{entity.sameEditCode}}</text>
</uni-forms-item>
<uni-forms-item label='sql_grid' name='sqlGrid'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!sqlGridReadOnly' v-model='entity.sqlGrid' maxlength='-1'   /><text v-if='sqlGridReadOnly'>{{entity.sqlGrid}}</text>
</uni-forms-item>
<uni-forms-item label='data_src' name='dataSrc'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!dataSrcReadOnly' v-model='entity.dataSrc' maxlength='50'   /><text v-if='dataSrcReadOnly'>{{entity.dataSrc}}</text>
</uni-forms-item>
<uni-forms-item label='main_column' name='mainColumn'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!mainColumnReadOnly' v-model='entity.mainColumn' maxlength='50'   /><text v-if='mainColumnReadOnly'>{{entity.mainColumn}}</text>
</uni-forms-item>
<uni-forms-item label='batch_new_page_name' name='batchNewPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!batchNewPageNameReadOnly' v-model='entity.batchNewPageName' maxlength='50'   /><text v-if='batchNewPageNameReadOnly'>{{entity.batchNewPageName}}</text>
</uni-forms-item>
<uni-forms-item label='edit_table_id' name='editTableId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editTableIdReadOnly' v-model='entity.editTableId'    /><text v-if='editTableIdReadOnly'>{{entity.editTableId}}</text>
</uni-forms-item>
<uni-forms-item label='edit_table_name' name='editTableName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editTableNameReadOnly' v-model='entity.editTableName' maxlength='50'   /><text v-if='editTableNameReadOnly'>{{entity.editTableName}}</text>
</uni-forms-item>
<uni-forms-item label='edit_page_id' name='editPageId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editPageIdReadOnly' v-model='entity.editPageId'    /><text v-if='editPageIdReadOnly'>{{entity.editPageId}}</text>
</uni-forms-item>
<uni-forms-item label='edit_page_name' name='editPageName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editPageNameReadOnly' v-model='entity.editPageName' maxlength='50'   /><text v-if='editPageNameReadOnly'>{{entity.editPageName}}</text>
</uni-forms-item>
<uni-forms-item label='edit_key_column' name='editKeyColumn'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editKeyColumnReadOnly' v-model='entity.editKeyColumn' maxlength='50'   /><text v-if='editKeyColumnReadOnly'>{{entity.editKeyColumn}}</text>
</uni-forms-item>
<uni-forms-item label='button_visible' name='buttonVisible'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!buttonVisibleReadOnly' v-model='entity.buttonVisible' maxlength='50'   /><text v-if='buttonVisibleReadOnly'>{{entity.buttonVisible}}</text>
</uni-forms-item>
<uni-forms-item label='auto_save' name='autoSave'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!autoSaveReadOnly' v-model='entity.autoSave' maxlength='50'   /><text v-if='autoSaveReadOnly'>{{entity.autoSave}}</text>
</uni-forms-item>
<uni-forms-item label='child_place' name='childPlace'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!childPlaceReadOnly' v-model='entity.childPlace' maxlength='20'   /><text v-if='childPlaceReadOnly'>{{entity.childPlace}}</text>
</uni-forms-item>
<uni-forms-item label='super_column_name' name='superColumnName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!superColumnNameReadOnly' v-model='entity.superColumnName' maxlength='50'   /><text v-if='superColumnNameReadOnly'>{{entity.superColumnName}}</text>
</uni-forms-item>
<uni-forms-item label='edit_page_url' name='editPageUrl'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editPageUrlReadOnly' v-model='entity.editPageUrl' maxlength='100'   /><text v-if='editPageUrlReadOnly'>{{entity.editPageUrl}}</text>
</uni-forms-item>
<uni-forms-item label='display_option' name='displayOption'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!displayOptionReadOnly' v-model='entity.displayOption' maxlength='50'   /><text v-if='displayOptionReadOnly'>{{entity.displayOption}}</text>
</uni-forms-item>
<uni-forms-item label='bpm_step_kind' name='bpmStepKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!bpmStepKindReadOnly' v-model='entity.bpmStepKind' maxlength='50'   /><text v-if='bpmStepKindReadOnly'>{{entity.bpmStepKind}}</text>
</uni-forms-item>
<uni-forms-item label='document_ready' name='documentReady'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!documentReadyReadOnly' v-model='entity.documentReady' maxlength='-1'   /><text v-if='documentReadyReadOnly'>{{entity.documentReady}}</text>
</uni-forms-item>
<uni-forms-item label='help' name='help'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!helpReadOnly' v-model='entity.help' maxlength='-1'   /><text v-if='helpReadOnly'>{{entity.help}}</text>
</uni-forms-item>
<uni-forms-item label='edit_width' name='editWidth'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!editWidthReadOnly' v-model='entity.editWidth'  type='number'  /><text v-if='editWidthReadOnly'>{{entity.editWidth}}</text>
</uni-forms-item>
<uni-forms-item label='yz_project_name' name='yzProjectName'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!yzProjectNameReadOnly' v-model='entity.yzProjectName' maxlength='50'   /><text v-if='yzProjectNameReadOnly'>{{entity.yzProjectName}}</text>
</uni-forms-item>
<uni-forms-item label='is_platform' name='isPlatform'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isPlatformReadOnly' v-model='entity.isPlatform'  type='number'  /><text v-if='isPlatformReadOnly'>{{entity.isPlatform}}</text>
</uni-forms-item>
<uni-forms-item label='is_sys' name='isSys'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isSysReadOnly' v-model='entity.isSys'  type='number'  /><text v-if='isSysReadOnly'>{{entity.isSys}}</text>
</uni-forms-item>
<uni-forms-item label='is_repeat_pwd' name='isRepeatPwd'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!isRepeatPwdReadOnly' v-model='entity.isRepeatPwd'  type='number'  /><text v-if='isRepeatPwdReadOnly'>{{entity.isRepeatPwd}}</text>
</uni-forms-item>
<uni-forms-item label='所属后台' name='yzAdminTypeId'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<text v-if='yzAdminTypeIdReadOnly'>{{readonlyDisplayText(entity.yzAdminTypeId)}}</text><template v-if='!yzAdminTypeIdReadOnly'><zxz-uni-data-select v-model='entity.yzAdminTypeId' :localdata='yz_admin_type_list' filterable :disabled='yzAdminTypeIdReadOnly' dataKey='name' dataValue='id' @change='yzAdminTypeIdDetailChange' /></template>
</uni-forms-item>
<uni-forms-item label='数据归属模式' name='scopeKind'  v-if='!loadEntitySuccess || $yzp.isColumnVisible({visibleCondition: &quot;&quot;}, entity)'>
<uni-easyinput primaryColor="#0ab3ad" v-if='!scopeKindReadOnly' v-model='entity.scopeKind' maxlength='30'   /><text v-if='scopeKindReadOnly'>{{entity.scopeKind}}</text>
</uni-forms-item>

        </uni-forms>
      </view>
      <view v-if="!is_props" class='bottom-buttons is-before-child' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><button size='mini' type='default' v-if='customerButtonShow1676828142707372033' @click='customerButtonHandler1676828142707372033'>列排序</button>
<button size='mini' type='default' v-if='customerButtonShow2076978804953321472' @click='customerButtonHandler2076978804953321472'>索引管理</button>
<button size='mini' type='default' v-if='customerButtonShow1861242035055382529' @click='customerButtonHandler1861242035055382529'>一键生成</button>
<button size='mini' type='default' v-if='customerButtonShow2062356006758912000' @click='customerButtonHandler2062356006758912000'>添加联接字段</button>
<button size='mini' type='default' v-if='customerButtonShow1676850201126719490' @click='customerButtonHandler1676850201126719490'>批量创建字段</button>
<button size='mini' type='default' v-if='customerButtonShow1696359699042168834' @click='customerButtonHandler1696359699042168834'>在数据库创建表</button>
<button size='mini' type='default' v-if='customerButtonShow1696795154426523650' @click='customerButtonHandler1696795154426523650'>在数据库删除表</button>
<button size='mini' type='default' v-if='customerButtonShow1697063177846710274' @click='customerButtonHandler1697063177846710274'>创建视图</button>
</view>
    </view>
    <view v-if="is_props"><view class='bottom-buttons is-before-child' :class="{ 'is-embedded': is_props }"><button type='warn' size="mini" v-if="!readOnlyCondition && page_is_delete && $yzp.hasPagePermission(page_sn,'is_delete')" @click='deleteEntity()'>删除</button><button type='primary' size="mini" v-if="!readOnlyCondition && !page_is_function_table && (id ? page_is_save : page_is_new) && $yzp.hasPagePermission(page_sn,id ? 'is_update' : 'is_new')" @click='saveEntity()'>保存</button><button type='default' size="mini" @click="closeDetail()">关闭</button><button size='mini' type='default' v-if='customerButtonShow1676828142707372033' @click='customerButtonHandler1676828142707372033'>列排序</button>
<button size='mini' type='default' v-if='customerButtonShow2076978804953321472' @click='customerButtonHandler2076978804953321472'>索引管理</button>
<button size='mini' type='default' v-if='customerButtonShow1861242035055382529' @click='customerButtonHandler1861242035055382529'>一键生成</button>
<button size='mini' type='default' v-if='customerButtonShow2062356006758912000' @click='customerButtonHandler2062356006758912000'>添加联接字段</button>
<button size='mini' type='default' v-if='customerButtonShow1676850201126719490' @click='customerButtonHandler1676850201126719490'>批量创建字段</button>
<button size='mini' type='default' v-if='customerButtonShow1696359699042168834' @click='customerButtonHandler1696359699042168834'>在数据库创建表</button>
<button size='mini' type='default' v-if='customerButtonShow1696795154426523650' @click='customerButtonHandler1696795154426523650'>在数据库删除表</button>
<button size='mini' type='default' v-if='customerButtonShow1697063177846710274' @click='customerButtonHandler1697063177846710274'>创建视图</button>
</view></view>
    <view v-if="originalChildrenReady"><ChildList1868973890525487105 :key="id + ':1868973890525487105'" :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='ChildList1868973890525487105' /><ChildList1610249810450849794 :key="id + ':1610249810450849794'" :parent_id='id' :parentReadOnlyCondition='readOnlyCondition' :parent_page_sn='page_sn' :is_son_page='true' :child_page_open_type='1' ref='ChildList1610249810450849794' /></view><view v-else class='section-view'>为避免数据丢失，请先保存当前页面的数据，再维护子表。</view>
    
  </view>
</template>
<script>
import utils from "@/common/utils";
import yzp from "@/common/yzp_runtime/yzp-runtime";
import project from "@/common/project";
import ChildList1868973890525487105 from "@/pages/yzp/config_page-list-1868973685063311361.vue";
import ChildList1610249810450849794 from "@/pages/yzp/config_column-list-6217715532627968.vue";

const originalRecordId=value=>typeof value==='string' && !['null','undefined'].includes(value.trim())?value.trim():'';

export default {
  
  components: {
    ChildList1868973890525487105,ChildList1610249810450849794,
  },
  props: ['is_props', 'props_id', 'props_parent_id', 'props_parent_page_sn', 'parentReadOnlyCondition'],
  data() {
    return {
      id: '',
      parent_id: '',
      yzpMenuId: '',
      page_sn: '6191083005411328',
      parent_page_sn: '',
      formParametersName: 'formParameters6191083005411328',
      loadEntitySuccess: false,
      yz_admin_type_list:[],originalButtons:[{"id":"1676828142707372033","name":"\u5217\u6392\u5E8F","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/config_columns_sort","close":false},{"id":"2076978804953321472","name":"\u7D22\u5F15\u7BA1\u7406","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/config_indexes","close":false},{"id":"1861242035055382529","name":"\u4E00\u952E\u751F\u6210","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/generate_all","close":false},{"id":"2062356006758912000","name":"\u6DFB\u52A0\u8054\u63A5\u5B57\u6BB5","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/add_join_columns","close":false},{"id":"1676850201126719490","name":"\u6279\u91CF\u521B\u5EFA\u5B57\u6BB5","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/batch_create_columns","close":false},{"id":"1696359699042168834","name":"\u5728\u6570\u636E\u5E93\u521B\u5EFA\u8868","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/create_table","close":false},{"id":"1696795154426523650","name":"\u5728\u6570\u636E\u5E93\u5220\u9664\u8868","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/delete_table","close":false},{"id":"1697063177846710274","name":"\u521B\u5EFA\u89C6\u56FE","action":1,"save":"NotSave","confirm":false,"confirmMsg":"","target":"/admin/custom/config_table/create_page","close":false}],page_is_update:true,page_is_save:true,page_is_delete:true,page_is_new:true,page_is_copy:false,page_is_function_table:false,detailGridColumns:2,permissionVersion:0,permissionsReady:false,recordReady:false,saving:false,routeReadOnly:false,changes:{},
      
      
      entity: {
        name:"",tableName:"",isRongyu:1,isFunctionTable:0,sql:"",isView:0,oracleSql:"",whereSql:"",orderBy:"",groupBy:"",pageSize:"",isEdit:"",isNew:"",isSave:"",isDelete:"",isSearch:"",beforeDeleteSql:"",afterSaveSql:"",afterImportSql:"",isImport:"",isExport:"",isMultiSelect:"",isLazyQuery:"",gridEdit:"",title:"",title2:"",title3:"",isSum:"",gridWidth:"",isPrintPreview:"",isBatchDelete:"",importMethod:"",readOnlyCondition:"",popWindowWidth:"",popWindowHeight:"",isExportEdit:"",isNoSaveAndClose:"",isUpdate:"",isHeadGroup:"",isModifyLog:"",layout:"",connString:"",exportMethod:"",importNo:"",copyMethod:"",sqlMore:"",authorizeScheme:"",clearMethod:"",isRaw:"",sameEditCode:"",sqlGrid:"",dataSrc:"",mainColumn:"",batchNewPageName:"",editTableId:"",editTableName:"",editPageId:"",editPageName:"",editKeyColumn:"",buttonVisible:"",autoSave:"",childPlace:"",superColumnName:"",editPageUrl:"",displayOption:"",bpmStepKind:"",documentReady:"",help:"",editWidth:"",yzProjectName:"",isPlatform:"",isSys:"",isRepeatPwd:"",yzAdminTypeId:"",scopeKind:"",
      },
      entity_column_readonly: {
        name:false,tableName:false,isRongyu:false,isFunctionTable:false,sql:false,isView:false,oracleSql:false,whereSql:false,orderBy:false,groupBy:false,pageSize:false,isEdit:false,isNew:false,isSave:false,isDelete:false,isSearch:false,beforeDeleteSql:false,afterSaveSql:false,afterImportSql:false,isImport:false,isExport:false,isMultiSelect:false,isLazyQuery:false,gridEdit:false,title:false,title2:false,title3:false,isSum:false,gridWidth:false,isPrintPreview:false,isBatchDelete:false,importMethod:false,readOnlyCondition:false,popWindowWidth:false,popWindowHeight:false,isExportEdit:false,isNoSaveAndClose:false,isUpdate:false,isHeadGroup:false,isModifyLog:false,layout:false,connString:false,exportMethod:false,importNo:false,copyMethod:false,sqlMore:false,authorizeScheme:false,clearMethod:false,isRaw:false,sameEditCode:false,sqlGrid:false,dataSrc:false,mainColumn:false,batchNewPageName:false,editTableId:false,editTableName:false,editPageId:false,editPageName:false,editKeyColumn:false,buttonVisible:false,autoSave:false,childPlace:false,superColumnName:false,editPageUrl:false,displayOption:false,bpmStepKind:false,documentReady:false,help:false,editWidth:false,yzProjectName:true,isPlatform:false,isSys:false,isRepeatPwd:false,yzAdminTypeId:false,scopeKind:false,
      },
      entity_rules: {
        name:{rules:[{"required":true,"errorMessage":"\u540D\u79F0\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},tableName:{rules:[{"required":true,"errorMessage":"\u8868\u540D\u4E3A\u5FC5\u586B\u9879"},{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isRongyu:{rules:[{"required":true,"errorMessage":"\u5B58\u50A8\u6A21\u5F0F\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isFunctionTable:{rules:[{"required":true,"errorMessage":"\u529F\u80FD\u8868\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},sql:{rules:[]},isView:{rules:[{"required":true,"errorMessage":"\u6570\u636E\u5E93\u89C6\u56FE\u4E3A\u5FC5\u586B\u9879"},{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},oracleSql:{rules:[]},whereSql:{rules:[]},orderBy:{rules:[]},groupBy:{rules:[]},pageSize:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isNew:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isSave:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isDelete:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isSearch:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},beforeDeleteSql:{rules:[]},afterSaveSql:{rules:[]},afterImportSql:{rules:[]},isImport:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isExport:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isMultiSelect:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isLazyQuery:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},gridEdit:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},title:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},title2:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},title3:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isSum:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},gridWidth:{rules:[{"maxLength":10,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isPrintPreview:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isBatchDelete:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},importMethod:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},readOnlyCondition:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},popWindowWidth:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},popWindowHeight:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isExportEdit:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isNoSaveAndClose:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isUpdate:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isHeadGroup:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isModifyLog:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},layout:{rules:[{"maxLength":10,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},connString:{rules:[{"maxLength":100,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},exportMethod:{rules:[{"maxLength":10,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},importNo:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},copyMethod:{rules:[{"maxLength":20,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},sqlMore:{rules:[]},authorizeScheme:{rules:[{"maxLength":20,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},clearMethod:{rules:[{"maxLength":20,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isRaw:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},sameEditCode:{rules:[{"maxLength":10,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},sqlGrid:{rules:[]},dataSrc:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},mainColumn:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},batchNewPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},editTableId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},editTableName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},editPageId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},editPageName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},editKeyColumn:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},buttonVisible:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},autoSave:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},childPlace:{rules:[{"maxLength":20,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},superColumnName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},editPageUrl:{rules:[{"maxLength":100,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},displayOption:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},bpmStepKind:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},documentReady:{rules:[]},help:{rules:[]},editWidth:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},yzProjectName:{rules:[{"maxLength":50,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},isPlatform:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isSys:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},isRepeatPwd:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},yzAdminTypeId:{rules:[{"pattern":"^-?\\d\u002B$","errorMessage":"\u53EA\u80FD\u8F93\u5165\u6574\u6570"}]},scopeKind:{rules:[{"maxLength":30,"errorMessage":"\u957F\u5EA6\u9700\u5728{maxLength}\u4E2A\u5B57\u7B26\u4EE5\u5185"}]},
      }
    }
  },
  watch: {
    props_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    props_parent_id() { if (this.is_props) this.initPage({ id: this.props_id, parent_id: this.props_parent_id, parent_page_sn: this.props_parent_page_sn }); },
    
  },
  computed: {
    nameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.name; },
tableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.tableName; },
isRongyuReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isRongyu; },
isFunctionTableReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isFunctionTable; },
sqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sql; },
isViewReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isView; },
oracleSqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.oracleSql; },
whereSqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.whereSql; },
orderByReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.orderBy; },
groupByReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.groupBy; },
pageSizeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.pageSize; },
isEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isEdit; },
isNewReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isNew; },
isSaveReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSave; },
isDeleteReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isDelete; },
isSearchReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSearch; },
beforeDeleteSqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.beforeDeleteSql; },
afterSaveSqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.afterSaveSql; },
afterImportSqlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.afterImportSql; },
isImportReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isImport; },
isExportReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isExport; },
isMultiSelectReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isMultiSelect; },
isLazyQueryReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isLazyQuery; },
gridEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.gridEdit; },
titleReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.title; },
title2ReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.title2; },
title3ReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.title3; },
isSumReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSum; },
gridWidthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.gridWidth; },
isPrintPreviewReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isPrintPreview; },
isBatchDeleteReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isBatchDelete; },
importMethodReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importMethod; },
readOnlyConditionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.readOnlyCondition; },
popWindowWidthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.popWindowWidth; },
popWindowHeightReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.popWindowHeight; },
isExportEditReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isExportEdit; },
isNoSaveAndCloseReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isNoSaveAndClose; },
isUpdateReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isUpdate; },
isHeadGroupReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isHeadGroup; },
isModifyLogReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isModifyLog; },
layoutReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.layout; },
connStringReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.connString; },
exportMethodReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.exportMethod; },
importNoReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.importNo; },
copyMethodReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.copyMethod; },
sqlMoreReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sqlMore; },
authorizeSchemeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.authorizeScheme; },
clearMethodReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.clearMethod; },
isRawReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isRaw; },
sameEditCodeReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sameEditCode; },
sqlGridReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.sqlGrid; },
dataSrcReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.dataSrc; },
mainColumnReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.mainColumn; },
batchNewPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.batchNewPageName; },
editTableIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editTableId; },
editTableNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editTableName; },
editPageIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editPageId; },
editPageNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editPageName; },
editKeyColumnReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editKeyColumn; },
buttonVisibleReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.buttonVisible; },
autoSaveReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.autoSave; },
childPlaceReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.childPlace; },
superColumnNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.superColumnName; },
editPageUrlReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editPageUrl; },
displayOptionReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.displayOption; },
bpmStepKindReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.bpmStepKind; },
documentReadyReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.documentReady; },
helpReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.help; },
editWidthReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.editWidth; },
yzProjectNameReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzProjectName; },
isPlatformReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isPlatform; },
isSysReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isSys; },
isRepeatPwdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.isRepeatPwd; },
yzAdminTypeIdReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.yzAdminTypeId; },
scopeKindReadOnly() { return this.readOnlyCondition || this.entity_column_readonly.scopeKind; },
customerButtonShow1676828142707372033() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1676828142707372033","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow2076978804953321472() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"2076978804953321472","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow1861242035055382529() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1861242035055382529","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow2062356006758912000() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"2062356006758912000","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow1676850201126719490() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1676850201126719490","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow1696359699042168834() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1696359699042168834","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow1696795154426523650() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1696795154426523650","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
customerButtonShow1697063177846710274() { this.permissionVersion;return yzp.isButtonVisible({"buttonId":"1697063177846710274","visibleScheme":"Normal","visibleRuleJson":""},this.entity,this.readOnlyCondition); },
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
      uni.$emit('yzpMenuChanged', { pageId: '6191083005411328', tableName: 'config_table' });
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
      formParameters.configTable = {};
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
 this.disposeOriginalDetail();this._alive=true;this._detailTasks=[];this._detailRead=0;this.entity={name:"",tableName:"",isRongyu:1,isFunctionTable:0,sql:"",isView:0,oracleSql:"",whereSql:"",orderBy:"",groupBy:"",pageSize:"",isEdit:"",isNew:"",isSave:"",isDelete:"",isSearch:"",beforeDeleteSql:"",afterSaveSql:"",afterImportSql:"",isImport:"",isExport:"",isMultiSelect:"",isLazyQuery:"",gridEdit:"",title:"",title2:"",title3:"",isSum:"",gridWidth:"",isPrintPreview:"",isBatchDelete:"",importMethod:"",readOnlyCondition:"",popWindowWidth:"",popWindowHeight:"",isExportEdit:"",isNoSaveAndClose:"",isUpdate:"",isHeadGroup:"",isModifyLog:"",layout:"",connString:"",exportMethod:"",importNo:"",copyMethod:"",sqlMore:"",authorizeScheme:"",clearMethod:"",isRaw:"",sameEditCode:"",sqlGrid:"",dataSrc:"",mainColumn:"",batchNewPageName:"",editTableId:"",editTableName:"",editPageId:"",editPageName:"",editKeyColumn:"",buttonVisible:"",autoSave:"",childPlace:"",superColumnName:"",editPageUrl:"",displayOption:"",bpmStepKind:"",documentReady:"",help:"",editWidth:"",yzProjectName:"",isPlatform:"",isSys:"",isRepeatPwd:"",yzAdminTypeId:"",scopeKind:"",};this.changes={};this.loadEntitySuccess=false;this.recordReady=false;this.permissionsReady=false;this.saving=false;this.routeReadOnly=options.parentReadOnly==='1';this.initializeMultiSelections?.();
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
 const mutation=route==="/admin/auto/config_table/6191083005411328"&&method==='DELETE'||method==='POST'&&["/admin/auto/config_table/6191083005411328"+'/add',"/admin/auto/config_table/6191083005411328"+'/update'].includes(route)||method==='POST'&&route.startsWith("/admin/auto/config_table/6191083005411328"+'/copy/');
 if(mutation)this.permissionVersion++;if(mutation&&(this.saving||this.readOnlyCondition))return null;if(mutation)this.saving=true;
 const read=route.startsWith("/admin/auto/config_table/6191083005411328"+'/detail/'),sequence=read?++this._detailRead:0,active=()=>current()&&(!read||sequence===this._detailRead)&&(typeof options.isCurrent!=='function'||options.isCurrent());
 const complete=options.complete,success=options.success,fail=options.fail;
 const task=yzp.request({...options,isCurrent:active,success:res=>{if(active())success?.call(options,res);},fail:error=>{if(active())fail?.call(options,error);},complete:value=>{if(this._alive&&epoch===this._detailEpoch&&parent===this.parent_id&&identity===this.currentIdentity()){if(mutation)this.saving=false;complete?.call(options,value);}}});
 if(task)this._detailTasks.push(task);else if(mutation)this.saving=false;return task;
},
/** 保留原300ms候选/控件准备时序，只有实际VO标识一致才开放子表。 */
markOriginalRecordReady() {this.recordReady=Boolean(this.id&&originalRecordId(this.entity.id)===this.id);const current=this.originalContext();setTimeout(()=>{if(current())this.loadEntitySuccess=true;},300);},
/** 已验证多选桥接使用原实体；changes仅用于内部选择状态，不替换整entity请求。 */
change(field,value) {if(this.readOnlyCondition||!["name","tableName","isRongyu","isFunctionTable","sql","isView","oracleSql","whereSql","orderBy","groupBy","pageSize","isEdit","isNew","isSave","isDelete","isSearch","beforeDeleteSql","afterSaveSql","afterImportSql","isImport","isExport","isMultiSelect","isLazyQuery","gridEdit","title","title2","title3","isSum","gridWidth","isPrintPreview","isBatchDelete","importMethod","readOnlyCondition","popWindowWidth","popWindowHeight","isExportEdit","isNoSaveAndClose","isUpdate","isHeadGroup","isModifyLog","layout","connString","exportMethod","importNo","copyMethod","sqlMore","authorizeScheme","clearMethod","isRaw","sameEditCode","sqlGrid","dataSrc","mainColumn","batchNewPageName","editTableId","editTableName","editPageId","editPageName","editKeyColumn","buttonVisible","autoSave","childPlace","superColumnName","editPageUrl","displayOption","bpmStepKind","documentReady","help","editWidth","isPlatform","isSys","isRepeatPwd","yzAdminTypeId","scopeKind"].includes(field))return;this.entity[field]=value;this.changes[field]=value;},
/** 原权限刷新始终完成回调；子页复用快照仍由readOnlyCondition核对自身操作。 */
refreshCurrentPermissions(done) {const epoch=this._detailEpoch,identity=this.currentIdentity();const finish=()=>{if(this._alive&&epoch===this._detailEpoch&&identity===this.currentIdentity()){this.permissionVersion++;done?.();}};yzp.refreshCurrentPermissions(finish,finish);},
/** 复制沿用POST和原onChange/关闭行为，确认窗口的迟到回调不会写其他记录。 */
copyEntity() {if(this.readOnlyCondition||!this.page_is_copy||!this.id||!yzp.hasPagePermission(this.page_sn,'is_all'))return;const that=this,current=this.originalContext();uni.showModal({title:'确定要复制吗？',success(modal){if(!modal.confirm||!current()||that.readOnlyCondition)return;that.originalRequest({url:"/admin/auto/config_table/6191083005411328"+'/copy/'+that.id+that.buildParentIdQuery(),method:'POST',sendSessionId:true,success(res){if(res.code===200){that.notifyMenuMutation();that.emitDetailChange('copy');that.closeDetail();}else uni.showModal({showCancel:false,content:res.msg});}});}});},
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

    receive1671029182696714241(newEntity) { if(this.isRongyuReadOnly) return; this.entity=newEntity; },
loadOriginalOptions2200000000000000087() {const that=this;that.yz_admin_type_list=[];(()=>{that._originalOptions=that._originalOptions||{};const optionSequence=(that._originalOptions["2200000000000000087"]||0)+1;that._originalOptions["2200000000000000087"]=optionSequence;const optionParent=null;that.originalRequest({url:"/admin/auto/config_table/6191083005411328/findNameIdYzAdminType2200000000000000087",isCurrent:()=>optionSequence===that._originalOptions["2200000000000000087"]&&true,data:{parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName),...(that.readOnlyCondition?{id:that.entity.yzAdminTypeId}:{})},method:'POST',sendSessionId:true,success(res){if(res.code===200){that.yz_admin_type_list=res.data||[];}else uni.showModal({showCancel:false,content:res.msg});}});})();},
yzAdminTypeIdDetailChange(item) { if(this.yzAdminTypeIdReadOnly) return; this.entity.yzAdminTypeId=item ? item.id : '';  },
customerButtonHandler1676828142707372033() { this.runOriginalButton("1676828142707372033"); },
customerButtonHandler2076978804953321472() { this.runOriginalButton("2076978804953321472"); },
customerButtonHandler1861242035055382529() { this.runOriginalButton("1861242035055382529"); },
customerButtonHandler2062356006758912000() { this.runOriginalButton("2062356006758912000"); },
customerButtonHandler1676850201126719490() { this.runOriginalButton("1676850201126719490"); },
customerButtonHandler1696359699042168834() { this.runOriginalButton("1696359699042168834"); },
customerButtonHandler1696795154426523650() { this.runOriginalButton("1696795154426523650"); },
customerButtonHandler1697063177846710274() { this.runOriginalButton("1697063177846710274"); },
showCustomerButtons() { const current=this.originalContext(),actions=this.originalButtons.filter(button=>this['customerButtonShow'+button.id]);if(!actions.length){uni.showToast({title:'暂无可用操作',icon:'none'});return;}uni.showActionSheet({itemList:actions.map(button=>button.name),success:res=>{if(current()&&actions[res.tapIndex])this['customerButtonHandler'+actions[res.tapIndex].id]();}}); },

    
    
    
    initLists: function () {
      // 整页只读时只展示详情数据，不加载编辑候选项接口。
      if (this.readOnlyCondition && !this.page_is_function_table) {
        return;
      }
      var that = this;
      that.loadOriginalOptions2200000000000000087();

    },
    refreshSonsData: function () {
      // this.getEntity();
      if (!this.originalChildrenReady) return;
      this.$refs.ChildList1868973890525487105?.refreshData();
this.$refs.ChildList1610249810450849794?.refreshData();
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
        url: '/admin/auto/config_table/6191083005411328/detail/' + that.id + that.buildParentIdQuery(),
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
          url: '/admin/auto/config_table/6191083005411328/' + method + that.buildParentIdQuery(),
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
              url: '/admin/auto/config_table/6191083005411328' + that.buildParentIdQuery(),
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
