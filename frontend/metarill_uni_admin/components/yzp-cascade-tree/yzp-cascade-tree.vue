<template>
  <view class="yzp-cascade-tree" :style="componentStyle">
    <view class="yzp-cascade-tree__input" @click="handleInput">
      <view class="yzp-cascade-tree__value" :class="{ 'yzp-cascade-tree__value--border': border, 'is-readonly': readonly }">
        <view v-if="loading && !isOpened" class="yzp-cascade-tree__selected-area yzp-cascade-tree__loading-inline">
          <uni-load-more class="yzp-cascade-tree__load-more" :contentText="loadMore" status="loading"></uni-load-more>
        </view>
        <text v-else-if="displayText" class="yzp-cascade-tree__selected-area">{{ displayText }}</text>
        <text v-else class="yzp-cascade-tree__selected-area yzp-cascade-tree__placeholder">{{ computedPlaceholder }}</text>
        <view v-if="clearIcon && !readonly && hasSelection" class="yzp-cascade-tree__icon-clear" @click.stop="clear">
          <uni-icons type="clear" color="#c0c4cc" size="20"></uni-icons>
        </view>
        <view v-else-if="!readonly" class="yzp-cascade-tree__arrow-area">
          <view class="yzp-cascade-tree__arrow"></view>
        </view>
      </view>
    </view>
    <view v-if="isOpened" class="yzp-cascade-tree__cover" @click="handleClose"></view>
    <view v-if="isOpened" class="yzp-cascade-tree__dialog">
      <view class="yzp-cascade-tree__dialog-arrow"></view>
      <view class="yzp-cascade-tree__dialog-caption">
        <view class="yzp-cascade-tree__title-area">
          <text class="yzp-cascade-tree__dialog-title">{{ popupTitle }}</text>
        </view>
        <view class="yzp-cascade-tree__dialog-close" @click="handleClose">
          <view class="yzp-cascade-tree__dialog-close-line"></view>
          <view class="yzp-cascade-tree__dialog-close-line yzp-cascade-tree__dialog-close-line--rotate"></view>
        </view>
      </view>
      <view class="yzp-cascade-tree__pickerview">
        <scroll-view class="yzp-cascade-tree__tabs" scroll-x="true">
          <view class="yzp-cascade-tree__tabs-inner">
            <view
              v-for="(level, index) in levels"
              :key="getLevelKey(level, index) || index"
              class="yzp-cascade-tree__tab"
              :class="{ 'yzp-cascade-tree__tab--active': index === activeLevelIndex }"
              @click="setActiveLevel(index)"
            >
              <text class="yzp-cascade-tree__tab-text">{{ getTabText(index) }}</text>
            </view>
          </view>
        </scroll-view>
        <view class="yzp-cascade-tree__panel">
          <scroll-view class="yzp-cascade-tree__list" :scroll-y="true">
            <view
              v-for="(item, optionIndex) in currentOptions"
              :key="item.id || optionIndex"
              class="yzp-cascade-tree__item"
              :class="{ 'yzp-cascade-tree__item--active': isCurrentSelected(item), 'yzp-cascade-tree__item--disabled': item.disable }"
              @click="handleOptionSelect(item, optionIndex)"
            >
              <text class="yzp-cascade-tree__item-text">{{ item.name || '' }}</text>
              <view v-if="isCurrentSelected(item)" class="yzp-cascade-tree__check"></view>
            </view>
            <view v-if="!loading && !errorMessage && !currentOptions.length" class="yzp-cascade-tree__empty">
              <text>暂无可选项</text>
            </view>
          </scroll-view>
          <view v-if="loading" class="yzp-cascade-tree__loading-cover">
            <uni-load-more class="yzp-cascade-tree__load-more" :contentText="loadMore" status="loading"></uni-load-more>
          </view>
          <view v-if="errorMessage" class="yzp-cascade-tree__error-message">
            <text class="yzp-cascade-tree__error-text">{{ errorMessage }}</text>
          </view>
        </view>
      </view>
    </view>
  </view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime'

function buildEmptyDefinition() {
  return {
    sourceMode: 'crossTable',
    levels: [],
    labels: {},
    displayJoiner: '/',
    selfTree: {
      label: '',
      table: '',
      idField: 'id',
      nameField: 'name',
      parentField: '',
      orderBy: '',
      rootMode: 'blank',
      filterMatchMode: 'selfAndDescendants'
    }
  }
}

export default {
  name: 'YzpCascadeTree',
  emits: ['popupopened', 'popupclosed', 'input', 'change', 'update:modelValue', 'inputclick'],
  props: {
    value: {
      type: Object,
      default: function() {
        return {}
      }
    },
    definition: {
      type: [Object, String],
      required: true
    },
    requestUrl: {
      type: String,
      required: true
    },
    pathRequestUrl: {
      type: String,
      default: ''
    },
    leafIdProperty: {
      type: String,
      default: ''
    },
    leafNameProperty: {
      type: String,
      default: ''
    },
    readonly: {
      type: Boolean,
      default: false
    },
    primaryColor: {
      type: String,
      default: '#007aff'
    },
    placeholder: {
      type: String,
      default: ''
    },
    placeholderPrefix: {
      type: String,
      default: '请选择'
    },
    popupTitle: {
      type: String,
      default: '请选择'
    },
    clearIcon: {
      type: Boolean,
      default: true
    },
    border: {
      type: Boolean,
      default: true
    },
    split: {
      type: String,
      default: '/'
    }
  },
  data() {
    return {
      normalizedDefinition: buildEmptyDefinition(),
      definitionSnapshot: '',
      internalValue: {},
      levelStates: [],
      selfTreePath: [],
      selfTreePathLeafId: '',
      valueSnapshot: '',
      isOpened: false,
      activeLevelIndex: 0,
      loading: false,
      errorMessage: '',
      loadMore: {
        contentdown: '',
        contentrefresh: '',
        contentnomore: ''
      }
    }
  },
  computed: {
    levels() {
      if (this.isSelfTreeMode) {
        var total = Math.max(this.levelStates.length, this.selfTreePath.length || 1)
        return Array.from({ length: total }, function(_, index) {
          return {
            __selfTreeIndex: index
          }
        })
      }
      return this.normalizedDefinition.levels || []
    },
    isSelfTreeMode() {
      return this.normalizedDefinition.sourceMode === 'selfTree'
    },
    displayJoiner() {
      return this.normalizedDefinition.displayJoiner || this.split || '/'
    },
    displayText() {
      return this.getDisplayText()
    },
    hasSelection() {
      if (this.isSelfTreeMode) {
        return !!this.internalValue[this.leafIdPropertyResolved]
      }
      for (let index = 0; index < this.levels.length; index++) {
        if (this.internalValue[this.getIdPropertyName(this.levels[index])]) {
          return true
        }
      }
      return false
    },
    leafIdPropertyResolved() {
      if (this.leafIdProperty) {
        return this.leafIdProperty
      }
      if (!this.levels.length) {
        return ''
      }
      var leafLevel = this.levels[this.levels.length - 1]
      return this.toCamelName(((leafLevel.columnPrefix || leafLevel.table) || '') + '_id')
    },
    leafNamePropertyResolved() {
      if (this.leafNameProperty) {
        return this.leafNameProperty
      }
      if (!this.levels.length) {
        return ''
      }
      var leafLevel = this.levels[this.levels.length - 1]
      return this.toCamelName(((leafLevel.columnPrefix || leafLevel.table) || '') + '_name')
    },
    computedPlaceholder() {
      if (this.placeholder) {
        return this.placeholder
      }
      if (!this.levels.length) {
        return this.placeholderPrefix
      }
      return this.placeholderPrefix + this.getLevelLabel(this.levels[this.levels.length - 1])
    },
    currentOptions() {
      var state = this.levelStates[this.activeLevelIndex]
      if (!state || !state.options) {
        return []
      }
      return state.options
    },
    componentStyle() {
      return {
        '--yzp-cascade-tree-primary-color': this.primaryColor || '#007aff'
      }
    }
  },
  watch: {
    definition: {
      immediate: true,
      handler: function() {
        this.setupDefinition()
      }
    },
    value: {
      immediate: true,
      deep: true,
      handler: function() {
        this.syncFromValue()
      }
    }
  },
  methods: {
    setupDefinition() {
      var nextDefinition = this.normalizeDefinition(this.definition)
      var nextDefinitionSnapshot = JSON.stringify(nextDefinition)
      var definitionChanged = nextDefinitionSnapshot !== this.definitionSnapshot
      this.normalizedDefinition = nextDefinition
      if (!definitionChanged) {
        return
      }
      this.definitionSnapshot = nextDefinitionSnapshot
      this.internalValue = this.extractValue(this.value, this.normalizedDefinition.levels)
      this.valueSnapshot = JSON.stringify(this.internalValue)
      this.selfTreePath = []
      this.selfTreePathLeafId = ''
      this.resetLevelStates()
      this.initLevels()
    },
    normalizeDefinition(definition) {
      if (!definition) {
        return buildEmptyDefinition()
      }
      if (typeof definition === 'string') {
        try {
          definition = JSON.parse(definition)
        } catch (error) {
          return buildEmptyDefinition()
        }
      }
      var normalized = Object.assign({}, buildEmptyDefinition(), definition || {})
      normalized.levels = Array.isArray(normalized.levels) ? normalized.levels : []
      normalized.labels = normalized.labels || {}
      normalized.selfTree = Object.assign({}, buildEmptyDefinition().selfTree, normalized.selfTree || {})
      if (!normalized.sourceMode) {
        normalized.sourceMode = normalized.selfTree && normalized.selfTree.table && !normalized.levels.length
          ? 'selfTree'
          : 'crossTable'
      }
      if (!normalized.displayJoiner) {
        normalized.displayJoiner = this.split || '/'
      }
      return normalized
    },
    extractValue(source, levels) {
      var nextValue = {}
      var sourceValue = source || {}
      if (this.isSelfTreeMode) {
        nextValue[this.leafIdPropertyResolved] = sourceValue[this.leafIdPropertyResolved] || ''
        nextValue[this.leafNamePropertyResolved] = sourceValue[this.leafNamePropertyResolved] || ''
        return nextValue
      }
      var definitionLevels = levels || this.levels
      definitionLevels.forEach((level) => {
        nextValue[this.getIdPropertyName(level)] = sourceValue[this.getIdPropertyName(level)] || ''
        nextValue[this.getNamePropertyName(level)] = sourceValue[this.getNamePropertyName(level)] || ''
      })
      return nextValue
    },
    resetLevelStates() {
      if (this.isSelfTreeMode) {
        this.levelStates = []
        return
      }
      this.levelStates = this.levels.map(function() {
        return {
          options: [],
          index: -1,
          requestKey: ''
        }
      })
    },
    initLevels() {
      if (!this.levels.length && !this.isSelfTreeMode) {
        return
      }
      this.activeLevelIndex = this.findActiveLevelIndex()
    },
    loadChildLevels(index, forceReload) {
      var that = this
      if (index >= this.levels.length) {
        return Promise.resolve()
      }
      var parentLevel = this.levels[index - 1]
      if (!parentLevel || !this.internalValue[this.getIdPropertyName(parentLevel)]) {
        return Promise.resolve()
      }
      return this.loadLevel(index, forceReload).then(function() {
        return that.loadChildLevels(index + 1, forceReload)
      })
    },
    buildLevelRequestKey(index, parentIdOverride) {
      if (this.isSelfTreeMode) {
        return 'selfTree::' + index + '::' + this.getSelfTreeRequestParentId(index, parentIdOverride)
      }
      var level = this.levels[index]
      if (!level) {
        return ''
      }
      if (index <= 0) {
        return this.getLevelKey(level)
      }
      var parentLevel = this.levels[index - 1]
      return this.getLevelKey(level) + '::' + (this.internalValue[this.getIdPropertyName(parentLevel)] || '')
    },
    // 层级数组使用splice，所选值更新既有属性；兼容Vue3并保留原控件协议和交互。
    loadLevel(index, forceReload, parentIdOverride) {
      if (this.isSelfTreeMode) {
        return this.loadSelfTreeLevel(index, forceReload, parentIdOverride)
      }
      var that = this
      var level = this.levels[index]
      if (!level || !this.requestUrl || !this.canLoadLevel(index)) {
        return Promise.resolve()
      }
      var state = this.levelStates[index] || { options: [], index: -1, requestKey: '' }
      var requestKey = this.buildLevelRequestKey(index)
      if (!forceReload && state.requestKey === requestKey && state.options.length) {
        this.levelStates.splice(index, 1, {
          options: state.options,
          index: this.findOptionIndex(state.options, this.internalValue[this.getIdPropertyName(level)]),
          requestKey: requestKey
        })
        return Promise.resolve()
      }
      this.loading = true
      this.errorMessage = ''
      return new Promise(function(resolve) {
        yzp.request({
          url: that.requestUrl,
          data: {
            levelKey: that.getLevelKey(level),
            selectedPath: that.buildSelectedPath()
          },
          sendSessionId: true,
          method: 'POST',
          success: function(res) {
            if (res.code === 200) {
              var options = res.data || []
              that.levelStates.splice(index, 1, {
                options: options,
                index: that.findOptionIndex(options, that.internalValue[that.getIdPropertyName(level)]),
                requestKey: requestKey
              })
            } else {
              that.errorMessage = res.msg || '加载失败'
              that.levelStates.splice(index, 1, {
                options: [],
                index: -1,
                requestKey: ''
              })
            }
            resolve()
          },
          fail: function() {
            that.errorMessage = '加载失败'
            resolve()
          },
          complete: function() {
            that.loading = false
          }
        })
      })
    },
    loadSelfTreeLevel(index, forceReload, parentIdOverride) {
      var that = this
      if (!this.requestUrl || !this.canLoadLevel(index, parentIdOverride)) {
        return Promise.resolve()
      }
      var state = this.levelStates[index] || { options: [], index: -1, requestKey: '' }
      var requestKey = this.buildLevelRequestKey(index, parentIdOverride)
      var requestParentId = this.getSelfTreeRequestParentId(index, parentIdOverride)
      if (!forceReload && state.requestKey === requestKey && state.options.length) {
        this.levelStates.splice(index, 1, {
          options: state.options,
          index: this.findSelfTreeOptionIndex(index, state.options),
          requestKey: requestKey
        })
        return Promise.resolve()
      }
      this.loading = true
      this.errorMessage = ''
      return new Promise(function(resolve) {
        yzp.request({
          url: that.requestUrl,
          data: {
            levelKey: that.getLevelKey(null, index),
            selectedPath: {
              parentId: requestParentId
            }
          },
          sendSessionId: true,
          method: 'POST',
          success: function(res) {
            if (res.code === 200) {
              var options = res.data || []
              that.levelStates.splice(index, 1, {
                options: options,
                index: that.findSelfTreeOptionIndex(index, options),
                requestKey: requestKey
              })
            } else {
              that.errorMessage = res.msg || '加载失败'
              that.levelStates.splice(index, 1, {
                options: [],
                index: -1,
                requestKey: ''
              })
            }
            resolve()
          },
          fail: function() {
            that.errorMessage = '加载失败'
            resolve()
          },
          complete: function() {
            that.loading = false
          }
        })
      })
    },
    buildSelectedPath() {
      if (this.isSelfTreeMode) {
        return {
          parentId: this.getSelfTreeParentIdForLevel(this.activeLevelIndex)
        }
      }
      var selectedPath = {}
      this.levels.forEach((level) => {
        selectedPath[this.getIdPropertyName(level)] = this.internalValue[this.getIdPropertyName(level)] || ''
      })
      return selectedPath
    },
    findOptionIndex(options, selectedId) {
      if (!selectedId) {
        return -1
      }
      for (var i = 0; i < options.length; i++) {
        if (options[i] && options[i].id === selectedId) {
          return i
        }
      }
      return -1
    },
    findActiveLevelIndex() {
      if (this.isSelfTreeMode) {
        for (var treeIndex = 0; treeIndex < this.levels.length; treeIndex++) {
          if (!this.selfTreePath[treeIndex] || !this.selfTreePath[treeIndex].id) {
            return treeIndex
          }
        }
        return this.levels.length ? this.levels.length - 1 : 0
      }
      for (var index = 0; index < this.levels.length; index++) {
        if (!this.internalValue[this.getIdPropertyName(this.levels[index])]) {
          return index
        }
      }
      return this.levels.length ? this.levels.length - 1 : 0
    },
    canLoadLevel(index, parentIdOverride) {
      if (this.isSelfTreeMode) {
        if (index <= 0) {
          return true
        }
        return !!this.getSelfTreeRequestParentId(index, parentIdOverride)
      }
      if (index <= 0) {
        return true
      }
      var parentLevel = this.levels[index - 1]
      return !!(parentLevel && this.internalValue[this.getIdPropertyName(parentLevel)])
    },
    show() {
      if (this.isSelfTreeMode) {
        return this.showSelfTree()
      }
      var that = this
      this.isOpened = true
      this.activeLevelIndex = this.findActiveLevelIndex()
      if (!this.levels.length) {
        this.$emit('popupopened')
        return
      }
      this.errorMessage = ''
      this.loadLevel(0, false).then(function() {
        return that.loadChildLevels(1, false)
      }).then(function() {
        that.activeLevelIndex = that.findActiveLevelIndex()
        if (that.canLoadLevel(that.activeLevelIndex) && !that.currentOptions.length) {
          return that.loadLevel(that.activeLevelIndex, false)
        }
      }).then(function() {
        if (that.isOpened) {
          that.$emit('popupopened')
        }
      })
    },
    showSelfTree() {
      var that = this
      this.isOpened = true
      this.errorMessage = ''
      var preparePath = Promise.resolve()
      if (this.internalValue[this.leafIdPropertyResolved]) {
        preparePath = this.ensureSelfTreePathLoaded(true)
      } else {
        this.clearSelfTreeSelectionState(false)
      }
      preparePath.then(function() {
        return that.ensureSelfTreeOpenStates()
      }).then(function() {
        if (that.isOpened) {
          that.activeLevelIndex = that.findActiveLevelIndex()
          that.$emit('popupopened')
        }
      })
    },
    hide() {
      this.isOpened = false
      this.$emit('popupclosed')
    },
    handleInput() {
      if (this.readonly) {
        this.$emit('inputclick')
        return
      }
      this.show()
    },
    handleClose() {
      this.hide()
    },
    setActiveLevel(index) {
      if (!this.canLoadLevel(index)) {
        return
      }
      this.activeLevelIndex = index
      if (!this.currentOptions.length) {
        this.loadLevel(index)
      }
    },
    handleOptionSelect(item, optionIndex) {
      if (this.isSelfTreeMode) {
        return this.handleSelfTreeOptionSelect(item, optionIndex)
      }
      var that = this
      if (!item || item.disable || this.readonly) {
        return
      }
      var level = this.levels[this.activeLevelIndex]
      if (!level) {
        return
      }

      this.levelStates.splice(this.activeLevelIndex, 1, {
        options: this.currentOptions.slice(0),
        index: optionIndex,
        requestKey: this.buildLevelRequestKey(this.activeLevelIndex)
      })

      this.internalValue[this.getIdPropertyName(level)] = item.id || ''
      this.internalValue[this.getNamePropertyName(level)] = item.name || ''

      for (var childIndex = this.activeLevelIndex + 1; childIndex < this.levels.length; childIndex++) {
        var childLevel = this.levels[childIndex]
        this.internalValue[this.getIdPropertyName(childLevel)] = ''
        this.internalValue[this.getNamePropertyName(childLevel)] = ''
        this.levelStates.splice(childIndex, 1, {
          options: [],
          index: -1,
          requestKey: ''
        })
      }

      this.emitValue()

      if (this.activeLevelIndex >= this.levels.length - 1) {
        this.hide()
        return
      }

      this.activeLevelIndex = this.activeLevelIndex + 1
      this.loadLevel(this.activeLevelIndex, true).then(function() {
        if (!that.currentOptions.length) {
          that.hide()
        }
      })
    },
    handleSelfTreeOptionSelect(item, optionIndex) {
      var that = this
      if (!item || item.disable || this.readonly) {
        return
      }

      this.levelStates.splice(this.activeLevelIndex, 1, {
        options: this.currentOptions.slice(0),
        index: optionIndex,
        requestKey: this.buildLevelRequestKey(this.activeLevelIndex)
      })

      var nextPath = this.selfTreePath.slice(0, this.activeLevelIndex)
      nextPath[this.activeLevelIndex] = {
        id: item.id || '',
        name: item.name || ''
      }
      this.selfTreePath = nextPath
      this.selfTreePathLeafId = item.id || ''

      this.internalValue[this.leafIdPropertyResolved] = item.id || ''
      this.internalValue[this.leafNamePropertyResolved] = item.name || ''

      for (var childIndex = this.activeLevelIndex + 1; childIndex < this.levelStates.length; childIndex++) {
        this.levelStates.splice(childIndex, 1, {
          options: [],
          index: -1,
          requestKey: ''
        })
      }
      this.levelStates = this.levelStates.slice(0, this.activeLevelIndex + 1)

      this.emitValue()
      this.levelStates.push({
        options: [],
        index: -1,
        requestKey: ''
      })
      this.activeLevelIndex = this.activeLevelIndex + 1
      this.loadLevel(this.activeLevelIndex, true, item.id || '').then(function() {
        if (!that.currentOptions.length) {
          // 自树模式加载不到子级时，当前节点就是最后层级，选择完成后自动收起。
          that.levelStates = that.levelStates.slice(0, that.activeLevelIndex)
          that.activeLevelIndex = Math.max(that.activeLevelIndex - 1, 0)
          that.hide()
        }
      })
    },
    clear() {
      if (this.isSelfTreeMode) {
        this.internalValue = this.extractValue({}, this.levels)
        this.valueSnapshot = JSON.stringify(this.internalValue)
        this.clearSelfTreeSelectionState(true)
        this.emitValue()
        this.activeLevelIndex = 0
        return
      }
      this.internalValue = this.extractValue({}, this.levels)
      this.valueSnapshot = JSON.stringify(this.internalValue)
      this.emitValue()
      this.activeLevelIndex = 0
      this.initLevels()
    },
    syncFromValue() {
      var nextValue = this.extractValue(this.value, this.levels)
      var nextSnapshot = JSON.stringify(nextValue)
      if (nextSnapshot === this.valueSnapshot) {
        return
      }
      this.valueSnapshot = nextSnapshot
      this.internalValue = nextValue
      if (this.isSelfTreeMode) {
        var that = this
        if (this.internalValue[this.leafIdPropertyResolved]) {
          this.ensureSelfTreePathLoaded(false).then(function() {
            if (that.isOpened) {
              return that.ensureSelfTreeOpenStates()
            }
          }).then(function() {
            that.activeLevelIndex = that.findActiveLevelIndex()
          })
        } else {
          this.clearSelfTreeSelectionState(!this.isOpened)
          this.activeLevelIndex = 0
        }
        return
      }
      this.activeLevelIndex = this.findActiveLevelIndex()
      if (this.isOpened) {
        this.errorMessage = ''
        this.resetLevelStates()
        this.loadLevel(0, true).then(() => {
          return this.loadChildLevels(1, true)
        }).then(() => {
          this.activeLevelIndex = this.findActiveLevelIndex()
        })
      }
    },
    emitValue() {
      var payload = this.extractValue(this.internalValue, this.levels)
      this.valueSnapshot = JSON.stringify(payload)
      this.$emit('input', payload)
      this.$emit('update:modelValue', payload)
      this.$emit('change', {
        detail: {
          value: payload,
          displayText: this.getDisplayText()
        }
      })
    },
    getTabText(index) {
      if (this.isSelfTreeMode) {
        var node = this.selfTreePath[index]
        return (node && node.name) || this.getPlaceholder(this.levels[index], index)
      }
      var level = this.levels[index]
      if (!level) {
        return ''
      }
      var name = this.internalValue[this.getNamePropertyName(level)]
      return name || this.getPlaceholder(level)
    },
    isCurrentSelected(item) {
      if (this.isSelfTreeMode) {
        return !!(item && this.selfTreePath[this.activeLevelIndex] && item.id === this.selfTreePath[this.activeLevelIndex].id)
      }
      var level = this.levels[this.activeLevelIndex]
      if (!level || !item) {
        return false
      }
      return item.id === this.internalValue[this.getIdPropertyName(level)]
    },
    getPlaceholder(level, index) {
      return this.placeholderPrefix + this.getLevelLabel(level, index)
    },
    getLevelLabel(level, index) {
      if (this.isSelfTreeMode) {
        return (this.normalizedDefinition.selfTree && this.normalizedDefinition.selfTree.label) || ('第' + ((index || 0) + 1) + '级')
      }
      var labels = this.normalizedDefinition.labels || {}
      var levelKey = this.getLevelKey(level)
      return labels[levelKey] || levelKey || ''
    },
    getDisplayText() {
      if (this.isSelfTreeMode) {
        var names = this.selfTreePath.map(function(item) {
          return item && item.name ? item.name : ''
        }).filter(function(item) {
          return !!item
        })
        if (names.length) {
          return names.join(this.displayJoiner)
        }
        return this.internalValue[this.leafNamePropertyResolved] || ''
      }
      var names = []
      this.levels.forEach((level) => {
        var name = this.internalValue[this.getNamePropertyName(level)]
        if (name) {
          names.push(name)
        }
      })
      return names.join(this.displayJoiner)
    },
    getLevelKey(level, index) {
      if (this.isSelfTreeMode) {
        return 'selfTree-' + (typeof index === 'number' ? index : 0)
      }
      return (level && (level.key || level.table)) || ''
    },
    getColumnPrefix(level) {
      if (this.isSelfTreeMode) {
        return ''
      }
      return (level && (level.columnPrefix || level.table)) || ''
    },
    getIdPropertyName(level) {
      if (this.isSelfTreeMode) {
        return this.leafIdPropertyResolved
      }
      return this.toCamelName(this.getColumnPrefix(level) + '_id')
    },
    getNamePropertyName(level) {
      if (this.isSelfTreeMode) {
        return this.leafNamePropertyResolved
      }
      return this.toCamelName(this.getColumnPrefix(level) + '_name')
    },
    getSelfTreeParentIdForLevel(index) {
      if (index <= 0) {
        return ''
      }
      var parentNode = this.selfTreePath[index - 1]
      return parentNode && parentNode.id ? parentNode.id : ''
    },
    getSelfTreeRequestParentId(index, parentIdOverride) {
      if (typeof parentIdOverride !== 'undefined') {
        return parentIdOverride || ''
      }
      return this.getSelfTreeParentIdForLevel(index)
    },
    findSelfTreeOptionIndex(index, options) {
      var selectedNode = this.selfTreePath[index]
      if (!selectedNode || !selectedNode.id) {
        return -1
      }
      return this.findOptionIndex(options, selectedNode.id)
    },
    clearSelfTreeSelectionState(resetStates) {
      this.selfTreePath = []
      this.selfTreePathLeafId = ''
      if (resetStates) {
        this.levelStates = []
      }
    },
    ensureSelfTreePathLoaded(forceReload) {
      var that = this
      var leafId = this.internalValue[this.leafIdPropertyResolved] || ''
      if (!leafId || !this.pathRequestUrl) {
        this.clearSelfTreeSelectionState(false)
        return Promise.resolve()
      }
      if (!forceReload && this.selfTreePathLeafId === leafId && this.selfTreePath.length) {
        return Promise.resolve()
      }
      this.loading = true
      return new Promise(function(resolve) {
        yzp.request({
          url: that.pathRequestUrl,
          data: {
            leafId: leafId
          },
          sendSessionId: true,
          method: 'POST',
          success: function(res) {
            if (res.code === 200 && res.data) {
              var path = Array.isArray(res.data.path) ? res.data.path : []
              that.selfTreePath = path.map(function(item) {
                return {
                  id: item && item.id ? item.id : '',
                  name: item && item.name ? item.name : ''
                }
              })
              that.selfTreePathLeafId = leafId
            } else {
              that.clearSelfTreeSelectionState(false)
              if (that.isOpened) {
                that.errorMessage = res.msg || '路径加载失败'
              }
            }
            resolve()
          },
          fail: function() {
            if (that.isOpened) {
              that.errorMessage = '路径加载失败'
            }
            resolve()
          },
          complete: function() {
            that.loading = false
          }
        })
      })
    },
    ensureSelfTreeOpenStates() {
      var that = this
      if (!this.selfTreePath.length) {
        if (!this.levelStates.length) {
          this.levelStates = [{
            options: [],
            index: -1,
            requestKey: ''
          }]
        }
        return this.loadLevel(0, false).then(function() {
          that.activeLevelIndex = 0
        })
      }

      this.levelStates = this.selfTreePath.map(function() {
        return {
          options: [],
          index: -1,
          requestKey: ''
        }
      })

      var sequence = Promise.resolve()
      for (var index = 0; index < this.selfTreePath.length; index++) {
        ;(function(levelIndex) {
          sequence = sequence.then(function() {
            return that.loadLevel(levelIndex, false)
          })
        })(index)
      }

      return sequence.then(function() {
        return that.loadLevel(that.selfTreePath.length, false).then(function() {
          var nextLevelState = that.levelStates[that.selfTreePath.length]
          if (nextLevelState && nextLevelState.options && nextLevelState.options.length) {
            that.activeLevelIndex = that.selfTreePath.length
            return
          }
          that.levelStates = that.levelStates.slice(0, that.selfTreePath.length)
          that.activeLevelIndex = Math.max(that.selfTreePath.length - 1, 0)
        })
      })
    },
    toCamelName(columnName) {
      return (columnName || '').replace(/_([a-z])/g, function(all, letter) {
        return letter.toUpperCase()
      })
    }
  }
}
</script>

<style scoped>
.yzp-cascade-tree {
  width: 100%;
  position: relative;
}

.yzp-cascade-tree__input {
  width: 100%;
}

.yzp-cascade-tree__value {
  /* 触发框参照 uni-easyinput 固定尺寸，避免表单控件高度和文字随 rpx 缩放漂移。 */
  height: 35px;
  min-height: 35px;
  padding: 0 10px;
  border-radius: 4px;
  background: #fff;
  display: flex;
  align-items: center;
  color: #303133;
  box-sizing: border-box;
}

.yzp-cascade-tree__value--border {
  border: 1px solid #dcdfe6;
}

/* 只读态对齐下拉列表禁用态样式，避免看起来像可编辑输入框。 */
.yzp-cascade-tree__value.is-readonly {
  background-color: #f5f7fa;
  cursor: not-allowed;
}

.yzp-cascade-tree__value--border.is-readonly {
  border-color: #e5e5e5;
}

.yzp-cascade-tree__selected-area {
  flex: 1;
  min-width: 0;
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 14px;
  line-height: 1;
  color: #303133;
}

.yzp-cascade-tree__placeholder {
  color: #999;
  font-size: 12px;
}

.yzp-cascade-tree__loading-inline {
  display: flex;
  align-items: center;
}

.yzp-cascade-tree__icon-clear,
.yzp-cascade-tree__arrow-area {
  width: 20px;
  height: 35px;
  margin-left: 5px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.yzp-cascade-tree__arrow {
  width: 8px;
  height: 8px;
  border-right: 1px solid #c0c4cc;
  border-bottom: 1px solid #c0c4cc;
  transform: rotate(45deg);
  margin-top: -4px;
}

.yzp-cascade-tree__cover {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.18);
  z-index: 998;
}

.yzp-cascade-tree__dialog {
  position: fixed;
  left: 50%;
  top: 50%;
  width: min(720rpx, calc(100vw - 40rpx));
  max-height: 70vh;
  transform: translate(-50%, -50%);
  background: #fff;
  border-radius: 20rpx;
  box-shadow: 0 20rpx 60rpx rgba(15, 23, 42, 0.18);
  z-index: 999;
  overflow: hidden;
}

.yzp-cascade-tree__dialog-arrow {
  display: none;
}

.yzp-cascade-tree__dialog-caption {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 24rpx 28rpx;
  border-bottom: 1px solid #f1f5f9;
}

.yzp-cascade-tree__dialog-title {
  font-size: 30rpx;
  font-weight: 600;
  color: #111827;
}

.yzp-cascade-tree__dialog-close {
  width: 44rpx;
  height: 44rpx;
  position: relative;
}

.yzp-cascade-tree__dialog-close-line {
  position: absolute;
  left: 50%;
  top: 50%;
  width: 2px;
  height: 28rpx;
  background: #94a3b8;
  border-radius: 999rpx;
  transform: translate(-50%, -50%) rotate(45deg);
}

.yzp-cascade-tree__dialog-close-line--rotate {
  transform: translate(-50%, -50%) rotate(-45deg);
}

.yzp-cascade-tree__pickerview {
  height: 60vh;
  max-height: 860rpx;
  display: flex;
  flex-direction: column;
}

.yzp-cascade-tree__tabs {
  white-space: nowrap;
  border-bottom: 1px solid #f1f5f9;
}

.yzp-cascade-tree__tabs-inner {
  display: flex;
  align-items: center;
  padding: 0 12rpx;
  min-width: max-content;
}

.yzp-cascade-tree__tab {
  flex: 0 0 auto;
  max-width: 240rpx;
  padding: 22rpx 18rpx 18rpx;
  margin: 0 6rpx;
  font-size: 26rpx;
  color: #64748b;
  white-space: nowrap;
  box-sizing: border-box;
}

.yzp-cascade-tree__tab--active {
  color: var(--yzp-cascade-tree-primary-color, #007aff);
  border-bottom: 4rpx solid var(--yzp-cascade-tree-primary-color, #007aff);
}

.yzp-cascade-tree__tab-text {
  display: block;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  word-break: keep-all;
  line-height: 1.4;
}

.yzp-cascade-tree__panel {
  position: relative;
  flex: 1;
  min-height: 0;
}

.yzp-cascade-tree__list {
  height: 100%;
}

.yzp-cascade-tree__item {
  min-height: 84rpx;
  padding: 0 28rpx;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #f8fafc;
  box-sizing: border-box;
}

.yzp-cascade-tree__item--disabled {
  opacity: 0.5;
}

.yzp-cascade-tree__item-text {
  flex: 1;
  min-width: 0;
  font-size: 28rpx;
  color: #1f2937;
}

.yzp-cascade-tree__item--active .yzp-cascade-tree__item-text {
  color: var(--yzp-cascade-tree-primary-color, #007aff);
}

.yzp-cascade-tree__check {
  width: 18rpx;
  height: 10rpx;
  border-left: 2px solid var(--yzp-cascade-tree-primary-color, #007aff);
  border-bottom: 2px solid var(--yzp-cascade-tree-primary-color, #007aff);
  transform: rotate(-45deg);
  margin-left: 20rpx;
}

.yzp-cascade-tree__loading-cover {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(255, 255, 255, 0.72);
}

.yzp-cascade-tree__load-more {
  margin: auto;
}

.yzp-cascade-tree__error-message,
.yzp-cascade-tree__empty {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 32rpx;
  text-align: center;
  background: rgba(255, 255, 255, 0.92);
  box-sizing: border-box;
}

.yzp-cascade-tree__error-text,
.yzp-cascade-tree__empty text {
  font-size: 26rpx;
  color: #94a3b8;
}

.yzp-cascade-tree__error-text {
  color: #ef4444;
}
</style>
