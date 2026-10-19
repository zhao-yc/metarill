    <yzp-popup ref="child_detail_popup" type="center" @change="onChildDetailPopupChange">
      <view class="select-page-dialog-view"><uni-section title="{{TITLE}}" type="line"><view class="section-view child-detail-dialog-view">
        <PageDetail v-if="childDetailPopupVisible" :key="childDetailPopupKey" :is_props="true" :props_id="childDetailPopupId" :props_parent_id="parentId" :props_parent_page_sn="parent_page_sn" :parentReadOnlyCondition="readOnlyCondition" @onChange="handleChildDetailChange" @onPopClose="closeChildDetailPopup" />
        <button size="mini" @click="closeChildDetailPopup">关闭</button>
      </view></uni-section></view>
    </yzp-popup>