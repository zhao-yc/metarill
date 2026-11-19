      <uni-list>
        <uni-list-item##row_clickable## v-for='(item, index) in list' :key='item.id'##row_detail_click##>
          <template v-slot:body>
            <view class='row'>
              ##list_columns##
            </view>
          </template>
        </uni-list-item>
      </uni-list>
