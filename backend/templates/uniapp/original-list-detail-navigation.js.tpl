    toDetail(id) {
      var that = this;
      this.clicked_id = id;
      // 回执只影响打开详情时的登录和父级上下文。
      const detailIdentity = this.originalListIdentity();
      const detailCurrent = () => this._originalListAlive && detailIdentity === this.originalListIdentity();
      if (this.is_son_page && utils.isBlank(this.parent_id)) {
        uni.showModal({
          showCancel: false,
          content: "为避免数据丢失，请先保存当前页面的数据"
        });
        return;
      }
##toDetailNewGuard##    return;}##toDetailQueryGuard##    return;}      // 子表弹窗配置只对 PC 端生效，移动端保持新页面打开。
      if (this.is_son_page && this.$deviceInfo.is_pc && this.child_page_open_type === 2 && this.openChildDetailPopup) {
        this.openChildDetailPopup(id);
        return;
      }
      uni.navigateTo({
        url: '/pages/yzp/##table_name##-detail-##page_id##?id=' + encodeURIComponent(id) + '&parent_id=' + encodeURIComponent(this.parent_id || '') + '&parent_page_sn=' + encodeURIComponent(this.parent_page_sn || '') + '&yzpMenuId=' + encodeURIComponent(this.yzpMenuId || ''),
        events: {
          onChange: function (change) {
            if (detailCurrent()) that.handleDetailChange(change);
          }
        }
      })
    },
