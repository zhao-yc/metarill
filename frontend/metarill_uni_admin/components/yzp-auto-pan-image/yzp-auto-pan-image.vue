<template>
  <view class="yzp-auto-pan-image">
    <image
      class="yzp-auto-pan-image__image"
      :class="imageClass"
      :mode="imageMode"
      :src="src"
      :style="imageStyle"
      @load="handleImageLoad"
      @error="handleImageError"
    ></image>
  </view>
</template>

<script>
import { calculateThumbnailPan } from '@/common/yzp_runtime/fileLibraryThumbnailPan'

function createEmptyPan() {
  return {
    direction: 'none',
    renderedWidth: 0,
    renderedHeight: 0,
    offsetPixels: 0,
    durationSeconds: 0
  }
}

export default {
  name: 'YzpAutoPanImage',
  props: {
    src: {
      type: String,
      default: ''
    }
  },
  data() {
    return {
      naturalWidth: 0,
      naturalHeight: 0,
      pan: createEmptyPan(),
      measureTimer: null,
      windowResizeHandler: null
    }
  },
  computed: {
    hasMeasuredSize() {
      return this.pan.renderedWidth > 0 && this.pan.renderedHeight > 0
    },
    imageMode() {
      return this.hasMeasuredSize ? 'scaleToFill' : 'aspectFill'
    },
    imageClass() {
      return {
        'yzp-auto-pan-image__image--horizontal': this.pan.direction === 'horizontal',
        'yzp-auto-pan-image__image--vertical': this.pan.direction === 'vertical'
      }
    },
    imageStyle() {
      if (!this.hasMeasuredSize) {
        return null
      }
      var style = {
        width: this.pan.renderedWidth + 'px',
        height: this.pan.renderedHeight + 'px'
      }
      if (this.pan.direction !== 'none') {
        style['--yzp-auto-pan-offset'] = this.pan.offsetPixels + 'px'
        style['--yzp-auto-pan-negative-offset'] = '-' + this.pan.offsetPixels + 'px'
        style['--yzp-auto-pan-duration'] = this.pan.durationSeconds + 's'
      }
      return style
    }
  },
  watch: {
    src() {
      this.naturalWidth = 0
      this.naturalHeight = 0
      this.pan = createEmptyPan()
    }
  },
  mounted() {
    var that = this
    this.windowResizeHandler = function() {
      that.scheduleMeasure()
    }
    if (typeof uni.onWindowResize === 'function') {
      uni.onWindowResize(this.windowResizeHandler)
    }
    // #ifdef H5
    // 标签的 v-show 不会触发窗口 resize，观察容器恢复尺寸后再计算缩略图。
    if (typeof ResizeObserver !== 'undefined' && this.$el) {
      this._containerResizeObserver = new ResizeObserver(function(entries) {
        if (entries.some(function(entry) { return entry.contentRect.width > 0 && entry.contentRect.height > 0 })) {
          that.scheduleMeasure()
        }
      })
      this._containerResizeObserver.observe(this.$el)
    }
    // #endif
  },
  // #ifndef VUE3
  destroyed() {
    this.dispose()
  },
  // #endif
  // #ifdef VUE3
  unmounted() {
    this.dispose()
  },
  // #endif
  methods: {
    /**
     * 图片加载完成后记录原始尺寸，再根据当前缩略图容器独立计算裁切方向。
     */
    handleImageLoad(event) {
      var detail = (event && event.detail) || {}
      this.naturalWidth = Number(detail.width || 0)
      this.naturalHeight = Number(detail.height || 0)
      this.scheduleMeasure()
    },
    handleImageError() {
      this.naturalWidth = 0
      this.naturalHeight = 0
      this.pan = createEmptyPan()
    },
    /**
     * 布局和窗口尺寸可能连续变化，延迟到下一帧统一测量，避免重复查询节点。
     */
    scheduleMeasure() {
      if (!this.naturalWidth || !this.naturalHeight) {
        return
      }
      this.clearMeasureTimer()
      var that = this
      this.measureTimer = setTimeout(function() {
        that.measureTimer = null
        that.measureContainer()
      }, 16)
    },
    measureContainer() {
      var that = this
      this.$nextTick(function() {
        uni.createSelectorQuery()
          .in(that)
          .select('.yzp-auto-pan-image')
          .boundingClientRect(function(rect) {
            if (!rect) {
              return
            }
            that.pan = calculateThumbnailPan(
              that.naturalWidth,
              that.naturalHeight,
              rect.width,
              rect.height
            )
          })
          .exec()
      })
    },
    clearMeasureTimer() {
      if (this.measureTimer) {
        clearTimeout(this.measureTimer)
        this.measureTimer = null
      }
    },
    /**
     * 组件销毁时释放窗口监听和待执行测量，避免文件列表切换后残留回调。
     */
    dispose() {
      this.clearMeasureTimer()
      // H5 容器观察与窗口监听一起释放，避免隐藏详情关闭后继续回调。
      if (this._containerResizeObserver) {
        this._containerResizeObserver.disconnect()
        this._containerResizeObserver = null
      }
      if (this.windowResizeHandler && typeof uni.offWindowResize === 'function') {
        uni.offWindowResize(this.windowResizeHandler)
      }
      this.windowResizeHandler = null
    }
  }
}
</script>

<style scoped>
.yzp-auto-pan-image {
  position: relative;
  width: 100%;
  height: 100%;
  display: block;
  overflow: hidden;
  background: #eef3f8;
}

.yzp-auto-pan-image__image {
  position: absolute;
  left: 50%;
  top: 50%;
  width: 100%;
  height: 100%;
  transform: translate(-50%, -50%);
}

.yzp-auto-pan-image__image--horizontal,
.yzp-auto-pan-image__image--vertical {
  animation-delay: 1.5s;
  animation-duration: var(--yzp-auto-pan-duration);
  animation-fill-mode: backwards;
  animation-iteration-count: infinite;
  animation-timing-function: ease-in-out;
  will-change: transform;
}

.yzp-auto-pan-image__image--horizontal {
  animation-name: yzp-auto-pan-horizontal;
  transform: translate(-50%, -50%) translateX(var(--yzp-auto-pan-offset));
}

.yzp-auto-pan-image__image--vertical {
  animation-name: yzp-auto-pan-vertical;
  transform: translate(-50%, -50%) translateY(var(--yzp-auto-pan-offset));
}

.yzp-auto-pan-image:hover .yzp-auto-pan-image__image--horizontal,
.yzp-auto-pan-image:hover .yzp-auto-pan-image__image--vertical {
  animation-play-state: paused;
}

@keyframes yzp-auto-pan-horizontal {
  0%,
  5%,
  95%,
  100% {
    transform: translate(-50%, -50%) translateX(var(--yzp-auto-pan-offset));
  }
  45%,
  55% {
    transform: translate(-50%, -50%) translateX(var(--yzp-auto-pan-negative-offset));
  }
}

@keyframes yzp-auto-pan-vertical {
  0%,
  5%,
  95%,
  100% {
    transform: translate(-50%, -50%) translateY(var(--yzp-auto-pan-offset));
  }
  45%,
  55% {
    transform: translate(-50%, -50%) translateY(var(--yzp-auto-pan-negative-offset));
  }
}

@media (prefers-reduced-motion: reduce) {
  .yzp-auto-pan-image__image--horizontal,
  .yzp-auto-pan-image__image--vertical {
    animation: none;
  }
}
</style>
