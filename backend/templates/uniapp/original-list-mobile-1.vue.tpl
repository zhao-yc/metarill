      <uni-list>
        <uni-list-item clickable v-for='(item, index) in list' :key='item.id' @click='toDetail(item.id)'>
          <template v-slot:body>
            <view class='row'>
              ##list_columns##
            </view>
          </template>
        </uni-list-item>
      </uni-list>
