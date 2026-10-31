<template>
  <view v-if="showPopup" class="yzp-popup" :class="[popupstyle, isDesktop ? 'fixforpc-z-index' : '']">
    <view @touchstart="touchstart">
      <uni-transition
        key="1"
        v-if="maskShow"
        name="mask"
        mode-class="fade"
        :styles="maskClass"
        :duration="duration"
        :show="showTrans"
        @click="onTap"
      />
      <uni-transition
        key="2"
        :mode-class="ani"
        name="content"
        :styles="transClass"
        :duration="duration"
        :show="showTrans"
        @click="onTap"
      >
        <view class="yzp-popup__wrapper" :style="getStyles" :class="[popupstyle]" @click="clear">
          <slot />
        </view>
      </uni-transition>
    </view>
    <!-- #ifdef H5 -->
    <keypress v-if="maskShow" @esc="onTap" />
    <!-- #endif -->
  </view>
</template>

<script>
// #ifdef H5
const keypress = {
  name: 'Keypress',
  props: {
    disable: {
      type: Boolean,
      default: false
    }
  },
  mounted() {
    const keyNames = {
      esc: ['Esc', 'Escape'],
      tab: 'Tab',
      enter: 'Enter',
      space: [' ', 'Spacebar'],
      up: ['Up', 'ArrowUp'],
      left: ['Left', 'ArrowLeft'],
      right: ['Right', 'ArrowRight'],
      down: ['Down', 'ArrowDown'],
      delete: ['Backspace', 'Delete', 'Del']
    }
    const listener = ($event) => {
      if (this.disable) {
        return
      }
      const keyName = Object.keys(keyNames).find((key) => {
        const eventKeyName = $event.key
        const value = keyNames[key]
        return value === eventKeyName || (Array.isArray(value) && value.includes(eventKeyName))
      })
      if (keyName) {
        setTimeout(() => {
          this.$emit(keyName, {})
        }, 0)
      }
    }
    document.addEventListener('keyup', listener)
  },
  render: () => {}
}
// #endif

const H5_POPUP_SCROLL_LOCK_KEY = '__yzpPopupH5ScrollLock__'

function getH5PopupScrollLockState() {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    return null
  }
  if (!window[H5_POPUP_SCROLL_LOCK_KEY]) {
    window[H5_POPUP_SCROLL_LOCK_KEY] = {
      count: 0,
      scrollTop: 0,
      bodyStyle: {}
    }
  }
  return window[H5_POPUP_SCROLL_LOCK_KEY]
}

export default {
  name: 'YzpPopup',
  components: {
    // #ifdef H5
    keypress
    // #endif
  },
  emits: ['change', 'maskClick'],
  props: {
    animation: {
      type: Boolean,
      default: true
    },
    type: {
      type: String,
      default: 'center'
    },
    isMaskClick: {
      type: Boolean,
      default: null
    },
    maskClick: {
      type: Boolean,
      default: null
    },
    backgroundColor: {
      type: String,
      default: 'none'
    },
    safeArea: {
      type: Boolean,
      default: true
    },
    maskBackgroundColor: {
      type: String,
      default: 'rgba(0, 0, 0, 0.4)'
    },
    borderRadius: {
      type: String
    }
  },
  data() {
    return {
      duration: 300,
      ani: [],
      showPopup: false,
      showTrans: false,
      h5BodyLocked: false,
      popupWidth: 0,
      popupHeight: 0,
      config: {
        top: 'top',
        bottom: 'bottom',
        center: 'center',
        left: 'left',
        right: 'right',
        message: 'top',
        dialog: 'center',
        share: 'bottom'
      },
      maskClass: {
        position: 'fixed',
        bottom: 0,
        top: 0,
        left: 0,
        right: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.4)'
      },
      transClass: {
        backgroundColor: 'transparent',
        borderRadius: this.borderRadius || '0',
        position: 'fixed',
        left: 0,
        right: 0
      },
      maskShow: true,
      mkclick: true,
      popupstyle: 'top'
    }
  },
  computed: {
    getStyles() {
      let res = { backgroundColor: this.bg }
      if (this.borderRadius || '0') {
        res = Object.assign(res, { borderRadius: this.borderRadius })
      }
      return res
    },
    isDesktop() {
      return this.popupWidth >= 500 && this.popupHeight >= 500
    },
    bg() {
      if (this.backgroundColor === '' || this.backgroundColor === 'none') {
        return 'transparent'
      }
      return this.backgroundColor
    }
  },
  watch: {
    type: {
      handler(type) {
        if (!this.config[type]) return
        this[this.config[type]](true)
      },
      immediate: true
    },
    isDesktop: {
      handler(newVal) {
        if (!this.config[newVal]) return
        this[this.config[this.type]](true)
      },
      immediate: true
    },
    maskClick: {
      handler(val) {
        this.mkclick = val
      },
      immediate: true
    },
    isMaskClick: {
      handler(val) {
        this.mkclick = val
      },
      immediate: true
    },
    showPopup(show) {
      // #ifdef H5
      this.setH5Visible(!show)
      // #endif
    }
  },
  created() {
    if (this.isMaskClick === null && this.maskClick === null) {
      this.mkclick = true
    } else {
      this.mkclick = this.isMaskClick !== null ? this.isMaskClick : this.maskClick
    }
    this.duration = this.animation ? 300 : 0
    this.messageChild = null
    this.clearPropagation = false
    this.maskClass.backgroundColor = this.maskBackgroundColor
  },
  mounted() {
    const fixSize = () => {
      // #ifdef MP-WEIXIN
      const {
        windowWidth,
        windowHeight,
        windowTop,
        safeArea,
        screenHeight,
        safeAreaInsets
      } = uni.getWindowInfo()
      // #endif
      // #ifndef MP-WEIXIN
      const {
        windowWidth,
        windowHeight,
        windowTop,
        safeArea,
        screenHeight,
        safeAreaInsets
      } = uni.getSystemInfoSync()
      // #endif
      this.popupWidth = windowWidth
      this.popupHeight = windowHeight + (windowTop || 0)
      if (safeArea && this.safeArea) {
        // #ifdef MP-WEIXIN
        this.safeAreaInsets = screenHeight - safeArea.bottom
        // #endif
        // #ifndef MP-WEIXIN
        this.safeAreaInsets = safeAreaInsets.bottom
        // #endif
      } else {
        this.safeAreaInsets = 0
      }
    }
    fixSize()
  },
  // #ifndef VUE3
  destroyed() {
    this.setH5Visible()
  },
  // #endif
  // #ifdef VUE3
  unmounted() {
    this.setH5Visible()
  },
  // #endif
  activated() {
    this.setH5Visible(!this.showPopup)
  },
  deactivated() {
    this.setH5Visible(true)
  },
  methods: {
    shouldLockH5Scroll() {
      // PC 端 center/dialog 弹窗优先保证页面不跳动，依赖遮罩层拦截交互即可
      return !(this.isDesktop && ['center', 'dialog'].includes(this.type))
    },
    lockH5BodyScroll() {
      // #ifdef H5
      if (this.h5BodyLocked) {
        return
      }
      if (!this.shouldLockH5Scroll()) {
        return
      }
      const state = getH5PopupScrollLockState()
      if (!state) {
        return
      }
      state.count += 1
      if (state.count === 1) {
        const body = document.body
        const scrollTop = Math.max(
          window.pageYOffset || 0,
          document.documentElement.scrollTop || 0,
          body.scrollTop || 0
        )
        state.scrollTop = scrollTop
        state.bodyStyle = {
          overflow: body.style.overflow,
          position: body.style.position,
          top: body.style.top,
          left: body.style.left,
          right: body.style.right,
          width: body.style.width
        }
        body.style.overflow = 'hidden'
        body.style.position = 'fixed'
        body.style.top = `-${scrollTop}px`
        body.style.left = '0'
        body.style.right = '0'
        body.style.width = '100%'
      }
      this.h5BodyLocked = true
      // #endif
    },
    unlockH5BodyScroll() {
      // #ifdef H5
      if (!this.h5BodyLocked) {
        return
      }
      const state = getH5PopupScrollLockState()
      if (!state) {
        return
      }
      state.count = Math.max(state.count - 1, 0)
      if (state.count === 0) {
        const body = document.body
        const bodyStyle = state.bodyStyle || {}
        body.style.overflow = bodyStyle.overflow || ''
        body.style.position = bodyStyle.position || ''
        body.style.top = bodyStyle.top || ''
        body.style.left = bodyStyle.left || ''
        body.style.right = bodyStyle.right || ''
        body.style.width = bodyStyle.width || ''
        const scrollTop = state.scrollTop || 0
        window.scrollTo(0, scrollTop)
        document.documentElement.scrollTop = scrollTop
        body.scrollTop = scrollTop
        requestAnimationFrame(() => {
          window.scrollTo(0, scrollTop)
          document.documentElement.scrollTop = scrollTop
          body.scrollTop = scrollTop
        })
        state.scrollTop = 0
        state.bodyStyle = {}
      }
      this.h5BodyLocked = false
      // #endif
    },
    setH5Visible(visible = true) {
      // #ifdef H5
      if (!this.shouldLockH5Scroll()) {
        this.unlockH5BodyScroll()
        return
      }
      if (visible) {
        this.unlockH5BodyScroll()
      } else {
        this.lockH5BodyScroll()
      }
      // #endif
    },
    closeMask() {
      this.maskShow = false
    },
    disableMask() {
      this.mkclick = false
    },
    clear(e) {
      // #ifndef APP-NVUE
      e.stopPropagation()
      // #endif
      this.clearPropagation = true
    },
    open(direction) {
      if (this.showPopup) {
        return
      }
      const innerType = ['top', 'center', 'bottom', 'left', 'right', 'message', 'dialog', 'share']
      if (!(direction && innerType.indexOf(direction) !== -1)) {
        direction = this.type
      }
      if (!this.config[direction]) {
        console.error('缺少类型：', direction)
        return
      }
      this[this.config[direction]]()
      this.$emit('change', {
        show: true,
        type: direction
      })
    },
    close() {
      this.showTrans = false
      this.$emit('change', {
        show: false,
        type: this.type
      })
      clearTimeout(this.timer)
      this.timer = setTimeout(() => {
        this.showPopup = false
      }, 300)
    },
    touchstart() {
      this.clearPropagation = false
    },
    onTap() {
      if (this.clearPropagation) {
        this.clearPropagation = false
        return
      }
      this.$emit('maskClick')
      if (!this.mkclick) return
      this.close()
    },
    top(type) {
      this.popupstyle = this.isDesktop ? 'fixforpc-top' : 'top'
      this.ani = ['slide-top']
      this.transClass = {
        position: 'fixed',
        left: 0,
        right: 0,
        backgroundColor: this.bg,
        borderRadius: this.borderRadius || '0'
      }
      if (type) return
      this.showPopup = true
      this.showTrans = true
      this.$nextTick(() => {
        this.showPoptrans()
        if (this.messageChild && this.type === 'message') {
          this.messageChild.timerClose()
        }
      })
    },
    bottom(type) {
      this.popupstyle = 'bottom'
      this.ani = ['slide-bottom']
      this.transClass = {
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        paddingBottom: this.safeAreaInsets + 'px',
        backgroundColor: this.bg,
        borderRadius: this.borderRadius || '0'
      }
      if (type) return
      this.showPoptrans()
    },
    center(type) {
      this.popupstyle = 'center'
      // #ifdef MP-WEIXIN
      this.ani = ['fade']
      // #endif
      // #ifndef MP-WEIXIN
      this.ani = ['zoom-out', 'fade']
      // #endif
      this.transClass = {
        position: 'fixed',
        /* #ifndef APP-NVUE */
        display: 'flex',
        flexDirection: 'column',
        /* #endif */
        bottom: 0,
        left: 0,
        right: 0,
        top: 0,
        justifyContent: 'center',
        alignItems: 'center',
        borderRadius: this.borderRadius || '0'
      }
      if (type) return
      this.showPoptrans()
    },
    left(type) {
      this.popupstyle = 'left'
      this.ani = ['slide-left']
      this.transClass = {
        position: 'fixed',
        left: 0,
        bottom: 0,
        top: 0,
        backgroundColor: this.bg,
        borderRadius: this.borderRadius || '0',
        /* #ifndef APP-NVUE */
        display: 'flex',
        flexDirection: 'column'
        /* #endif */
      }
      if (type) return
      this.showPoptrans()
    },
    right(type) {
      this.popupstyle = 'right'
      this.ani = ['slide-right']
      this.transClass = {
        position: 'fixed',
        bottom: 0,
        right: 0,
        top: 0,
        backgroundColor: this.bg,
        borderRadius: this.borderRadius || '0',
        /* #ifndef APP-NVUE */
        display: 'flex',
        flexDirection: 'column'
        /* #endif */
      }
      if (type) return
      this.showPoptrans()
    },
    showPoptrans() {
      this.$nextTick(() => {
        this.showPopup = true
        this.showTrans = true
      })
    }
  }
}
</script>

<style lang="scss">
.yzp-popup {
  position: fixed;
  /* #ifndef APP-NVUE */
  z-index: 99;
  /* #endif */

  &.top,
  &.left,
  &.right {
    /* #ifdef H5 */
    top: var(--window-top);
    /* #endif */
    /* #ifndef H5 */
    top: 0;
    /* #endif */
  }

  .yzp-popup__wrapper {
    /* #ifndef APP-NVUE */
    display: block;
    /* #endif */
    position: relative;

    &.left,
    &.right {
      /* #ifdef H5 */
      padding-top: var(--window-top);
      /* #endif */
      /* #ifndef H5 */
      padding-top: 0;
      /* #endif */
      flex: 1;
    }
  }
}

.fixforpc-z-index {
  /* #ifndef APP-NVUE */
  z-index: 999;
  /* #endif */
}

.fixforpc-top {
  top: 0;
}
</style>
