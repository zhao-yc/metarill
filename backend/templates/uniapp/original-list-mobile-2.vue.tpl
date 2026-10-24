      <view class="yz-row-container">
            <view class='yz-row' v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
              ##list_columns##
            </view>
      </view>
