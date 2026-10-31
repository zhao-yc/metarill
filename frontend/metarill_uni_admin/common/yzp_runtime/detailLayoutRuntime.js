/** 详情分组只管理主表标签；字段仍在同一 uni-forms 中完成整表校验。 */
export function createDetailLayoutMixin(definition, fields) {
  const groups = definition && definition.mode === 'tabs' ? definition.groups || [] : []
  const fieldList = Array.isArray(fields) ? fields : []
  return {
    data() {
      return { activeDetailGroupKey: '' }
    },
    computed: {
      /** 复用字段自身的可见性规则，隐藏没有可见字段的空组。 */
      visibleDetailGroups() {
        return groups.filter(group => fieldList.some(field => field.groupKey === group.key &&
          (!this.loadEntitySuccess || this.$yzp.isColumnVisible(field, this.entity))))
      }
    },
    watch: {
      visibleDetailGroups: {
        immediate: true,
        handler(visibleGroups) {
          // 数据加载和条件联动可能使当前组变空，此时回到首个有效组。
          if (!visibleGroups.some(group => group.key === this.activeDetailGroupKey)) {
            this.activeDetailGroupKey = visibleGroups.length ? visibleGroups[0].key : ''
          }
        }
      }
    },
    methods: {
      /** 点击标签仅改变显示状态，保留输入值、控件和校验注册。 */
      selectDetailGroup(key) {
        if (this.visibleDetailGroups.some(group => group.key === key)) this.activeDetailGroupKey = key
      },
      /** 定位整表校验返回的第一个字段；常显字段不改变当前标签。 */
      revealDetailValidationError(errors) {
        const first = Array.isArray(errors) ? errors[0] : errors
        if (!first) return
        const name = first.key || first.name || first.field
        const field = fieldList.find(item => item.name === name)
        if (!field) return
        if (field.groupKey) this.selectDetailGroup(field.groupKey)
        this.$nextTick(() => this.scrollToDetailField(field.name))
      },
      /** H5 优先滚动实际容器，兼容弹窗；其他端使用 uni 页面定位。 */
      scrollToDetailField(name) {
        const selector = '#yzp-detail-field-' + name
        // #ifdef H5
        const root = this.$el
        const element = root && typeof root.querySelector === 'function' ? root.querySelector(selector) : null
        if (element && typeof element.scrollIntoView === 'function') {
          element.scrollIntoView({ behavior: 'smooth', block: 'center' })
          const input = element.querySelector('input, textarea')
          if (input && typeof input.focus === 'function') input.focus({ preventScroll: true })
          return
        }
        // #endif
        if (typeof uni !== 'undefined' && typeof uni.createSelectorQuery === 'function') {
          uni.createSelectorQuery().in(this).select(selector).boundingClientRect().selectViewport().scrollOffset()
            .exec(result => {
              if (result && result[0] && result[1]) {
                uni.pageScrollTo({ scrollTop: Math.max(0, result[0].top + result[1].scrollTop - 80), duration: 200 })
              }
            })
        }
      }
    }
  }
}
