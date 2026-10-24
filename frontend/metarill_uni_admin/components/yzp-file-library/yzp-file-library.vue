<template>
  <view class="yzp-file-library" :style="componentStyle">
    <view class="yzp-file-library__summary">
      <view class="yzp-file-library__summary-main">
        <view class="yzp-file-library__summary-icon">
          <uni-icons type="folder-add" size="20" color="#4f6f96"></uni-icons>
        </view>
        <view class="yzp-file-library__summary-text">
          <text class="yzp-file-library__summary-title">{{ selectedSummaryTitle }}</text>
          <text v-if="selectedSummaryDesc" class="yzp-file-library__summary-desc" :title="selectedSummaryDesc">{{ selectedSummaryDesc }}</text>
        </view>
      </view>
      <button v-if="!readonly" class="yzp-file-library__manage-button" size="mini" type="primary" @click="openManagerDialog">
        管理文件
      </button>
    </view>

    <view class="yzp-file-library__selected-preview" v-if="files.length">
      <view class="yzp-file-library__selected-chip" v-for="(file, index) in files" :key="file.fileId || index" @click="openFile(file)">
        <yzp-auto-pan-image v-if="isImage(file) && thumbnailUrl(file)" class="yzp-file-library__selected-chip-thumb" :src="thumbnailUrl(file)"></yzp-auto-pan-image>
        <uni-icons v-else :type="fileIcon(file)" size="18" color="#52616f"></uni-icons>
        <text :title="file.name || file.fileId">{{ file.name || file.fileId }}</text>
      </view>
    </view>

    <view class="yzp-file-library__readonly-empty" v-else-if="readonly">
      <text>暂无文件</text>
    </view>

    <view v-if="dialogVisible" class="yzp-file-library__dialog-mask" @click="cancelManagerDialog">
      <view class="yzp-file-library__dialog" :class="{ 'yzp-file-library__dialog--maximized': dialogMaximized }" @click.stop>
        <view class="yzp-file-library__dialog-header">
          <view>
            <text class="yzp-file-library__dialog-title">管理文件</text>
            <text class="yzp-file-library__dialog-subtitle">已选 {{ dialogFiles.length }}/{{ normalizedMaxCount }}</text>
          </view>
          <view class="yzp-file-library__dialog-actions">
            <view
              class="yzp-file-library__dialog-maximize"
              :class="{ 'yzp-file-library__dialog-maximize--restore': dialogMaximized }"
              :title="dialogMaximized ? '还原' : '最大化'"
              @click="toggleDialogMaximized"
            >
              <view class="yzp-file-library__maximize-icon">
                <view v-if="dialogMaximized" class="yzp-file-library__maximize-icon-back"></view>
              </view>
            </view>
            <view class="yzp-file-library__dialog-close" title="关闭" @click="cancelManagerDialog">
              <uni-icons type="closeempty" size="24" color="#5f6b7a"></uni-icons>
            </view>
          </view>
        </view>

        <view class="yzp-file-library__dialog-body">
          <view class="yzp-file-library__dialog-toolbar">
            <view class="yzp-file-library__dialog-search">
              <uni-easyinput v-model="libraryKeyword" placeholder="搜索文件或文件夹" @confirm="loadLibraryFiles"></uni-easyinput>
            </view>
            <button class="yzp-file-library__plain-button" size="mini" @click="loadLibraryFiles">搜索</button>
          </view>

          <view class="yzp-file-library__browser">
            <view class="yzp-file-library__browser-head">
              <view class="yzp-file-library__browser-nav">
                <button v-if="!isLibrarySearching()" class="yzp-file-library__folder-back-button" size="mini" :disabled="currentFolderPath.length <= 1" aria-label="上一级" title="上一级" @click="goParentFolder">
                  <uni-icons type="left" size="18" :color="currentFolderPath.length <= 1 ? '#a8b1bd' : '#326da8'"></uni-icons>
                </button>
                <view v-if="!isLibrarySearching()" class="yzp-file-library__breadcrumb">
                  <view
                    class="yzp-file-library__breadcrumb-item"
                    v-for="(folder, index) in currentFolderPath"
                    :key="folder.id || 'root'"
                    @click="selectBreadcrumbFolder(folder)"
                  >
                    <text>{{ folder.name }}</text>
                    <text v-if="index < currentFolderPath.length - 1" class="yzp-file-library__breadcrumb-separator">/</text>
                  </view>
                </view>
                <text v-else class="yzp-file-library__search-label">搜索结果</text>
                <text class="yzp-file-library__browser-count">（{{ browserItems.length }} 项）</text>
              </view>
              <view class="yzp-file-library__browser-head-right">
                <view class="yzp-file-library__browser-actions">
                  <button class="yzp-file-library__plain-button" size="mini" @click="openCreateFolderDialog">新建文件夹</button>
                  <button class="yzp-file-library__upload-button" size="mini" type="primary" :disabled="dialogRemainingCount <= 0 || uploading" @click="chooseUploadType">
                    <uni-icons type="cloud-upload" color="#ffffff" size="16"></uni-icons>
                    <text>上传</text>
                  </button>
                </view>
                <view class="yzp-file-library__view-switch">
                  <view
                    class="yzp-file-library__view-switch-item"
                    :class="{'yzp-file-library__view-switch-item--active': libraryViewMode === 'grid'}"
                    title="图标视图"
                    @click="setLibraryViewMode('grid')"
                  >
                    <view class="yzp-file-library__grid-view-icon">
                      <view></view>
                      <view></view>
                      <view></view>
                      <view></view>
                    </view>
                  </view>
                  <view
                    class="yzp-file-library__view-switch-item"
                    :class="{'yzp-file-library__view-switch-item--active': libraryViewMode === 'list'}"
                    title="列表视图"
                    @click="setLibraryViewMode('list')"
                  >
                    <uni-icons type="list" size="15" :color="libraryViewMode === 'list' ? '#ffffff' : '#64748b'"></uni-icons>
                  </view>
                </view>
              </view>
            </view>
            <view
              class="yzp-file-library__library-grid"
              :class="{'yzp-file-library__library-grid--searching': isLibrarySearching()}"
              v-if="allowLibraryValue && browserItems.length && libraryViewMode === 'grid'"
            >
              <template v-for="item in browserItems" :key="item.type + '-' + item.id">
                <view
                  v-if="item.type === 'folder'"
                  class="yzp-file-library__library-card yzp-file-library__library-card--folder yzp-file-library__folder-card"
                  @click="openFolderItem(item)"
                >
                  <view class="yzp-file-library__card-thumb yzp-file-library__folder-card-thumb">
                    <view class="yzp-file-library__folder-icon">
                      <view class="yzp-file-library__folder-icon-tab"></view>
                      <view class="yzp-file-library__folder-icon-body"></view>
                    </view>
                  </view>
                  <view class="yzp-file-library__card-title">
                    <text class="yzp-file-library__name" :title="item.name">{{ item.name }}</text>
                    <text v-if="isLibrarySearching()" class="yzp-file-library__card-path">{{ item.pathText }}</text>
                  </view>
                  <view class="yzp-file-library__card-actions">
                    <view class="yzp-file-library__move-link" @click.stop="chooseMoveFolderTarget(item.folder)">移动到</view>
                    <view class="yzp-file-library__move-link" @click.stop="openRenameFolderDialog(item.folder)">重命名</view>
                    <view class="yzp-file-library__danger-text" @click.stop="confirmDeleteCurrentFolder(item.folder)">删除</view>
                  </view>
                </view>
                <view
                  v-else
                  class="yzp-file-library__library-card yzp-file-library__library-card--file"
                  :class="{'yzp-file-library__library-card--selected': isDialogSelected(item.file)}"
                  @click="toggleDialogFile(item.file)"
                >
                  <view v-if="item.file.accessMode === 'PROTECTED'" class="yzp-file-library__protected-lock">
                    <uni-icons type="locked" size="12" color="#ffffff"></uni-icons>
                  </view>
                  <view class="yzp-file-library__card-thumb">
                    <yzp-auto-pan-image v-if="isImage(normalizeFileItem(item.file)) && thumbnailUrl(item.file)" class="yzp-file-library__image" :src="thumbnailUrl(item.file)"></yzp-auto-pan-image>
                    <uni-icons v-else :type="fileIcon(normalizeFileItem(item.file))" size="26" color="#607287"></uni-icons>
                  </view>
                  <view class="yzp-file-library__card-title">
                    <text class="yzp-file-library__name" :title="item.file.originalName || item.file.id">{{ item.file.originalName || item.file.id }}</text>
                    <text v-if="isLibrarySearching()" class="yzp-file-library__card-path">{{ item.pathText }}</text>
                  </view>
                  <view class="yzp-file-library__card-actions">
                    <view class="yzp-file-library__icon-button" @click.stop="previewLibraryFile(item.file)">
                      <uni-icons type="eye" size="18" color="#52616f"></uni-icons>
                    </view>
                    <view class="yzp-file-library__move-link" @click.stop="chooseMoveTarget(item.file)">移动到</view>
                    <view class="yzp-file-library__move-link" @click.stop="openRenameFileDialog(item.file)">重命名</view>
                    <view class="yzp-file-library__danger-text" @click.stop="confirmDeleteLibraryFile(item.file)">删除</view>
                  </view>
                  <view class="yzp-file-library__selected-mark" v-if="isDialogSelected(item.file)">
                    <uni-icons type="checkmarkempty" size="18" color="#ffffff"></uni-icons>
                  </view>
                </view>
              </template>
            </view>
            <view class="yzp-file-library__library-list" v-else-if="allowLibraryValue && browserItems.length">
              <view class="yzp-file-library__library-list-table" :class="{'yzp-file-library__library-list-table--searching': isLibrarySearching()}">
                <view class="yzp-file-library__library-list-row yzp-file-library__library-list-head">
                  <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-name">名称</text>
                  <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-actions">操作</text>
                  <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-type">类型</text>
                  <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-size">大小</text>
                  <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-status">状态</text>
                  <text v-if="isLibrarySearching()" class="yzp-file-library__library-list-cell yzp-file-library__library-list-path">位置</text>
                </view>
                <template v-for="item in browserItems" :key="'list-' + item.type + '-' + item.id">
                  <view
                    v-if="item.type === 'folder'"
                    class="yzp-file-library__library-list-row yzp-file-library__library-list-row--folder"
                    @click="openFolderItem(item)"
                  >
                    <view class="yzp-file-library__library-list-cell yzp-file-library__library-list-name">
                      <view class="yzp-file-library__library-list-icon yzp-file-library__library-list-icon--folder">
                        <view class="yzp-file-library__folder-icon yzp-file-library__folder-icon--list">
                          <view class="yzp-file-library__folder-icon-tab"></view>
                          <view class="yzp-file-library__folder-icon-body"></view>
                        </view>
                      </view>
                      <text class="yzp-file-library__name" :title="item.name">{{ item.name }}</text>
                    </view>
                    <view class="yzp-file-library__library-list-cell yzp-file-library__library-list-actions" @click.stop>
                      <view class="yzp-file-library__move-link" @click.stop="chooseMoveFolderTarget(item.folder)">移动到</view>
                      <view class="yzp-file-library__move-link" @click.stop="openRenameFolderDialog(item.folder)">重命名</view>
                      <view class="yzp-file-library__danger-text" @click.stop="confirmDeleteCurrentFolder(item.folder)">删除</view>
                    </view>
                    <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-type">{{ formatBrowserItemType(item) }}</text>
                    <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-size">{{ formatBrowserItemSize(item) }}</text>
                    <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-status">{{ formatBrowserItemStatus(item) }}</text>
                    <text v-if="isLibrarySearching()" class="yzp-file-library__library-list-cell yzp-file-library__library-list-path">{{ item.pathText }}</text>
                  </view>
                  <view
                    v-else
                    class="yzp-file-library__library-list-row yzp-file-library__library-list-row--file"
                    :class="{'yzp-file-library__library-list-row--selected': isDialogSelected(item.file)}"
                    @click="toggleDialogFile(item.file)"
                  >
                    <view class="yzp-file-library__library-list-cell yzp-file-library__library-list-name">
                      <view class="yzp-file-library__library-list-icon">
                        <yzp-auto-pan-image v-if="isImage(normalizeFileItem(item.file)) && thumbnailUrl(item.file)" class="yzp-file-library__library-list-image" :src="thumbnailUrl(item.file)"></yzp-auto-pan-image>
                        <uni-icons v-else :type="fileIcon(normalizeFileItem(item.file))" size="20" color="#607287"></uni-icons>
                      </view>
                      <text class="yzp-file-library__name" :title="item.file.originalName || item.file.id">{{ item.file.originalName || item.file.id }}</text>
                    </view>
                    <view class="yzp-file-library__library-list-cell yzp-file-library__library-list-actions" @click.stop>
                      <view class="yzp-file-library__icon-button" @click.stop="previewLibraryFile(item.file)">
                        <uni-icons type="eye" size="17" color="#52616f"></uni-icons>
                      </view>
                      <view class="yzp-file-library__move-link" @click.stop="chooseMoveTarget(item.file)">移动到</view>
                      <view class="yzp-file-library__move-link" @click.stop="openRenameFileDialog(item.file)">重命名</view>
                      <view class="yzp-file-library__danger-text" @click.stop="confirmDeleteLibraryFile(item.file)">删除</view>
                    </view>
                    <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-type">{{ formatBrowserItemType(item) }}</text>
                    <text class="yzp-file-library__library-list-cell yzp-file-library__library-list-size">{{ formatBrowserItemSize(item) }}</text>
                    <view class="yzp-file-library__library-list-cell yzp-file-library__library-list-status">
                      <view v-if="item.file.accessMode === 'PROTECTED'" class="yzp-file-library__library-list-lock">
                        <uni-icons type="locked" size="11" color="#ffffff"></uni-icons>
                      </view>
                      <text>{{ formatBrowserItemStatus(item) }}</text>
                    </view>
                    <text v-if="isLibrarySearching()" class="yzp-file-library__library-list-cell yzp-file-library__library-list-path">{{ item.pathText }}</text>
                  </view>
                </template>
              </view>
            </view>
            <view class="yzp-file-library__library-empty" v-else>
              <text>{{ libraryEmptyText }}</text>
            </view>
          </view>

          <view class="yzp-file-library__selected-panel">
            <view class="yzp-file-library__selected-panel-head">
              <text>已选文件</text>
              <text>{{ dialogFiles.length }}/{{ normalizedMaxCount }}</text>
            </view>
            <view class="yzp-file-library__selected-list" v-if="dialogFiles.length">
              <view class="yzp-file-library__selected-row" v-for="(file, index) in dialogFiles" :key="file.fileId || index">
                <view class="yzp-file-library__selected-row-main" @click="openFile(file)">
                  <yzp-auto-pan-image v-if="isImage(file) && thumbnailUrl(file)" class="yzp-file-library__selected-row-thumb" :src="thumbnailUrl(file)"></yzp-auto-pan-image>
                  <uni-icons v-else :type="fileIcon(file)" size="20" color="#52616f"></uni-icons>
                  <view>
                    <text class="yzp-file-library__name" :title="file.name || file.fileId">{{ file.name || file.fileId }}</text>
                    <text class="yzp-file-library__desc">{{ formatFileDesc(file) }}</text>
                  </view>
                </view>
                <view class="yzp-file-library__selected-row-actions">
                  <view class="yzp-file-library__move-link" @click="locateDialogFileFolder(file)">定位到文件夹</view>
                  <view class="yzp-file-library__remove-link" @click="removeDialogFile(index)">移除引用</view>
                </view>
              </view>
            </view>
            <view class="yzp-file-library__selected-empty" v-else>
              <text>尚未选择文件</text>
            </view>
          </view>
        </view>

        <view v-if="moveDialogVisible" class="yzp-file-library__move-dialog-mask" @click.stop="cancelMoveTarget">
          <view class="yzp-file-library__move-dialog" @click.stop>
            <view class="yzp-file-library__move-dialog-head">
              <view>
                <text class="yzp-file-library__dialog-title">{{ moveDialogTitle }}</text>
                <text class="yzp-file-library__dialog-subtitle">{{ movingTargetName }}</text>
              </view>
              <view class="yzp-file-library__dialog-close" @click="cancelMoveTarget">
                <uni-icons type="closeempty" size="22" color="#5f6b7a"></uni-icons>
              </view>
            </view>
            <view class="yzp-file-library__move-target-list">
              <view
                class="yzp-file-library__move-target-item"
                :class="{'yzp-file-library__move-target-item--active': !moveTargetFolderId}"
                @click="selectMoveTarget('')"
              >
                <view class="yzp-file-library__folder-toggle" @click.stop="toggleMoveRootFolderExpanded">
                  <uni-icons :type="moveRootFolderExpanded ? 'bottom' : 'right'" size="12" color="#64748b"></uni-icons>
                </view>
                <uni-icons type="folder" size="17" color="#52616f"></uni-icons>
                <text class="yzp-file-library__folder-name">根目录</text>
                <text class="yzp-file-library__folder-count">{{ formatRootFolderFileCount() }}</text>
              </view>
              <view
                class="yzp-file-library__move-target-item"
                :class="{
                  'yzp-file-library__move-target-item--active': moveTargetFolderId === folder.id,
                  'yzp-file-library__move-target-item--disabled': isMoveTargetDisabled(folder.id)
                }"
                :style="{paddingLeft: (folder.level * 14 + 4) + 'px'}"
                v-for="folder in folderTreeNodes"
                :key="folder.id"
                @click="selectMoveTarget(folder.id)"
              >
                <view class="yzp-file-library__folder-toggle yzp-file-library__folder-toggle--spacer"></view>
                <uni-icons type="folder" size="17" color="#52616f"></uni-icons>
                <text class="yzp-file-library__folder-name">{{ folder.name }}</text>
                <text class="yzp-file-library__folder-count">{{ formatFolderFileCount(folder) }}</text>
              </view>
            </view>
            <view class="yzp-file-library__move-dialog-footer">
              <text class="yzp-file-library__move-target-summary">移动到{{ moveTargetFolderName }}</text>
              <view>
                <button class="yzp-file-library__plain-button" size="mini" @click="cancelMoveTarget">取消</button>
                <button class="yzp-file-library__confirm-button" size="mini" type="primary" @click="confirmMoveTarget">确定</button>
              </view>
            </view>
          </view>
        </view>

        <view v-if="folderNameDialogVisible" class="yzp-file-library__name-dialog-mask" @click.stop="cancelFolderNameDialog">
          <view class="yzp-file-library__name-dialog" @click.stop>
            <view class="yzp-file-library__name-dialog-head">
              <text class="yzp-file-library__dialog-title">{{ folderNameDialogTitle }}</text>
              <view class="yzp-file-library__dialog-close" @click="cancelFolderNameDialog">
                <uni-icons type="closeempty" size="22" color="#5f6b7a"></uni-icons>
              </view>
            </view>
            <view class="yzp-file-library__name-dialog-body">
              <text v-if="folderNameDialogMode === 'create'" class="yzp-file-library__name-dialog-location">新建位置：{{ folderNameDialogParentPathText }}</text>
              <uni-easyinput
                v-model="folderNameDialogValue"
                :placeholder="folderNameDialogPlaceholder"
                :focus="folderNameDialogVisible"
                @confirm="confirmFolderNameDialog"
              ></uni-easyinput>
            </view>
            <view class="yzp-file-library__name-dialog-footer">
              <button class="yzp-file-library__plain-button" size="mini" @click="cancelFolderNameDialog">取消</button>
              <button class="yzp-file-library__confirm-button" size="mini" type="primary" :disabled="folderNameDialogSubmitting" @click="confirmFolderNameDialog">确定</button>
            </view>
          </view>
        </view>

        <view class="yzp-file-library__dialog-footer">
          <button class="yzp-file-library__plain-button" size="mini" @click="cancelManagerDialog">取消</button>
          <button class="yzp-file-library__confirm-button" size="mini" type="primary" @click="confirmManagerDialog">确定</button>
        </view>
      </view>
    </view>
  </view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime'
import {
  cropImageFile
} from '@/common/yzp_runtime/imageUploadCropper'
import {
  aggregateFolderFileCounts,
  buildFileLibraryBrowserItems,
  buildFolderPath,
  buildVisibleFolderNodes,
  normalizeFolders
} from '@/common/yzp_runtime/fileLibraryFolderTree'

const FILE_LIBRARY_VIEW_MODE_STORAGE_KEY = 'yzp:file-library:view-mode'

function safeJsonParse(value, fallback) {
  if (typeof value !== 'string') {
    return fallback
  }
  try {
    return JSON.parse(value)
  } catch (e) {
    return fallback
  }
}

export default {
  name: 'YzpFileLibrary',
  emits: ['input', 'change', 'update:modelValue'],
  props: {
    value: {
      type: [Array, String],
      default: function() {
        return []
      }
    },
    modelValue: {
      type: [Array, String],
      default: undefined
    },
    configColumnId: {
      type: String,
      required: true
    },
    pageId: {
      type: String,
      default: ''
    },
    tableId: {
      type: String,
      default: ''
    },
    recordId: {
      type: String,
      default: ''
    },
    parentId: {
      type: String,
      default: ''
    },
    parentPageSn: {
      type: String,
      default: ''
    },
    accessMode: {
      type: String,
      default: 'PUBLIC'
    },
    accept: {
      type: String,
      default: 'image,file,video'
    },
    maxCount: {
      type: [Number, String],
      default: 9
    },
    maxSizeMb: {
      type: [Number, String],
      default: 50
    },
    extensions: {
      type: [Array, String],
      default: function() {
        return []
      }
    },
    allowLibrary: {
      type: [Boolean, String],
      default: true
    },
    cropImages: {
      type: [Boolean, String],
      default: false
    },
    readonly: {
      type: Boolean,
      default: false
    },
    primaryColor: {
      type: String,
      default: '#007aff'
    }
  },
  data() {
    return {
      files: [],
      filesSnapshot: '',
      uploading: false,
      resolving: false,
      dialogVisible: false,
      dialogMaximized: false,
      dialogFiles: [],
      dialogFilesSnapshot: '',
      libraryKeyword: '',
      libraryLoading: false,
      libraryFiles: [],
      foldersLoading: false,
      folders: [],
      libraryFilesLoadError: '',
      foldersLoadError: '',
      rootDirectFolderFileCount: 0,
      rootFolderFileCount: 0,
      currentFolderId: '',
      libraryViewMode: 'grid',
      folderNameDialogVisible: false,
      folderNameDialogMode: 'create',
      folderNameDialogValue: '',
      folderNameDialogSubmitting: false,
      folderNameDialogParentId: '',
      folderNameDialogFolderId: '',
      folderNameDialogFileId: '',
      moveDialogVisible: false,
      movingFile: null,
      movingFolder: null,
      moveTargetFolderId: '',
      moveRootFolderExpanded: true
    }
  },
  created() {
    this.initLibraryViewMode()
  },
  computed: {
    componentStyle() {
      return {
        '--file-library-primary': this.primaryColor || '#007aff'
      }
    },
    normalizedMaxCount() {
      var count = Number(this.maxCount || 9)
      return count > 0 ? count : 9
    },
    normalizedMaxSizeBytes() {
      var sizeMb = Number(this.maxSizeMb || 50)
      return (sizeMb > 0 ? sizeMb : 50) * 1024 * 1024
    },
    normalizedExtensions() {
      var values = Array.isArray(this.extensions) ? this.extensions : safeJsonParse(this.extensions, [])
      return (values || []).map(function(item) {
        var suffix = String(item || '').trim().toLowerCase()
        return suffix && suffix.indexOf('.') !== 0 ? '.' + suffix : suffix
      }).filter(function(item) {
        return item
      })
    },
    acceptModes() {
      var modes = String(this.accept || 'file').split(',').map(function(item) {
        return item.trim()
      }).filter(function(item) {
        return item
      })
      return modes.length ? modes : ['file']
    },
    remainingCount() {
      return Math.max(this.normalizedMaxCount - this.files.length, 0)
    },
    dialogRemainingCount() {
      return Math.max(this.normalizedMaxCount - this.dialogFiles.length, 0)
    },
    selectedSummaryTitle() {
      if (!this.files.length) {
        return '暂无文件'
      }
      return '已选 ' + this.files.length + '/' + this.normalizedMaxCount
    },
    selectedSummaryDesc() {
      if (!this.files.length) {
        return this.readonly ? '当前字段没有绑定文件' : '点击管理文件进行上传或选择'
      }
      // 已选文件的具体名称由下方 chip 展示，summary 只保留数量。
      return ''
    },
    allowLibraryValue() {
      return this.allowLibrary === true || this.allowLibrary === 'true'
    },
    cropImagesValue() {
      return this.cropImages === true || this.cropImages === 'true'
    },
    libraryEmptyText() {
      if (!this.allowLibraryValue) {
        return '当前字段未开启文件库复用，可直接上传新文件'
      }
      if (this.libraryLoadErrorText) {
        return this.libraryLoadErrorText
      }
      return this.libraryLoading ? '加载中...' : '暂无可复用文件'
    },
    libraryLoadErrorText() {
      return this.libraryFilesLoadError || this.foldersLoadError || ''
    },
    folderNameDialogTitle() {
      if (this.folderNameDialogMode === 'renameFile') {
        return '重命名文件'
      }
      return this.folderNameDialogMode === 'rename' ? '重命名文件夹' : '新建文件夹'
    },
    folderNameDialogPlaceholder() {
      return this.folderNameDialogMode === 'renameFile' ? '文件名称' : '文件夹名称'
    },
    folderNameDialogParentPathText() {
      return buildFolderPath(this.folders, this.folderNameDialogParentId).map(function(folder) {
        return folder.name
      }).join(' / ')
    },
    folderTreeNodes() {
      return this.moveRootFolderExpanded ? buildVisibleFolderNodes(this.folders, this.folders.map(function(folder) {
        return folder.id
      })) : []
    },
    currentFolderPath() {
      return buildFolderPath(this.folders, this.currentFolderId)
    },
    currentFolderPathText() {
      return this.currentFolderPath.map(function(folder) {
        return folder.name
      }).join(' / ')
    },
    browserItems() {
      return buildFileLibraryBrowserItems(this.folders, this.libraryFiles, this.currentFolderId, this.libraryKeyword)
    },
    moveDialogTitle() {
      return this.movingFolder ? '移动文件夹' : '移动到文件夹'
    },
    movingFileName() {
      if (!this.movingFile) {
        return ''
      }
      return this.movingFile.originalName || this.movingFile.name || this.movingFile.id || this.movingFile.fileId || ''
    },
    movingTargetName() {
      if (this.movingFolder) {
        return this.movingFolder.name || this.movingFolder.id || ''
      }
      return this.movingFileName
    },
    moveTargetFolderName() {
      if (!this.moveTargetFolderId) {
        return '根目录'
      }
      var folder = this.folders.find(function(item) {
        return item.id === this.moveTargetFolderId
      }, this)
      return folder ? folder.name : '根目录'
    }
  },
  watch: {
    value: {
      handler(value) {
        this.syncFromExternal(value)
      },
      deep: true,
      immediate: true
    },
    modelValue: {
      handler(value) {
        if (value !== undefined) {
          this.syncFromExternal(value)
        }
      },
      deep: true
    }
  },
  methods: {
    /**
     * 文件库视图模式是纯前端偏好，默认保持现有图标视图。
     */
    initLibraryViewMode() {
      try {
        var savedMode = uni.getStorageSync(FILE_LIBRARY_VIEW_MODE_STORAGE_KEY)
        this.libraryViewMode = savedMode === 'list' ? 'list' : 'grid'
      } catch (e) {
        this.libraryViewMode = 'grid'
      }
    },
    /**
     * 切换主浏览区展示方式，并记住本机选择。
     */
    setLibraryViewMode(mode) {
      var nextMode = mode === 'list' ? 'list' : 'grid'
      this.libraryViewMode = nextMode
      try {
        uni.setStorageSync(FILE_LIBRARY_VIEW_MODE_STORAGE_KEY, nextMode)
      } catch (e) {
        // 本地存储不可用时仍完成本次切换，不影响文件选择主流程。
      }
    },
    /**
     * 外部 v-model 可能是数组、JSON 字符串或空值，这里统一为组件内部文件对象。
     */
    syncFromExternal(value) {
      var normalized = this.normalizeExternalValue(value)
      var snapshot = JSON.stringify(normalized.map(this.toEmitFile))
      if (snapshot === this.filesSnapshot) {
        return
      }
      this.filesSnapshot = snapshot
      this.files = normalized
      this.resolveFiles()
    },
    normalizeExternalValue(value) {
      var rawValue = value
      if (rawValue === undefined && this.modelValue !== undefined) {
        rawValue = this.modelValue
      }
      if (typeof rawValue === 'string') {
        rawValue = rawValue.trim() ? safeJsonParse(rawValue, []) : []
      }
      if (!Array.isArray(rawValue)) {
        rawValue = []
      }
      return rawValue.map(this.normalizeFileItem).filter(function(item) {
        return item.fileId
      })
    },
    normalizeFileItem(item) {
      if (typeof item === 'string') {
        return {
          fileId: item,
          name: item,
          url: '',
          mode: '',
          mimeType: '',
          sizeBytes: 0,
          folderId: ''
        }
      }
      var fileId = item.fileId || item.id || ''
      return {
        fileId: fileId,
        name: item.name || item.originalName || item.fileName || fileId,
        url: item.url || item.publicUrl || '',
        previewUrl: item.previewUrl || '',
        mode: item.mode || '',
        mimeType: item.mimeType || '',
        sizeBytes: item.sizeBytes || item.size || 0,
        folderId: item.folderId || ''
      }
    },
    /**
     * 保存到业务字段时只保留稳定元数据，不把受保护文件的短期签名 URL 写入数据库。
     */
    toEmitFile(file) {
      return {
        fileId: file.fileId,
        name: file.name,
        mimeType: file.mimeType || '',
        sizeBytes: file.sizeBytes || 0
      }
    },
    emitChange() {
      var payload = this.files.map(this.toEmitFile)
      this.filesSnapshot = JSON.stringify(payload)
      this.$emit('input', payload)
      this.$emit('update:modelValue', payload)
      this.$emit('change', payload)
    },
    /**
     * 解析访问地址时由后端统一判断 PUBLIC / PROTECTED 权限，前端只负责渲染结果。
     */
    resolveFiles() {
      var ids = this.files.map(function(file) {
        return file.fileId
      }).filter(function(fileId) {
        return fileId
      })
      if (!ids.length || this.resolving) {
        return
      }
      var that = this
      this.resolving = true
      this.request({
        url: '/admin/sys/file-library/resolve',
        method: 'POST',
        data: {
          fileIds: ids
        }
      }).then(function(rows) {
        var map = {}
        ;(rows || []).forEach(function(row) {
          map[row.fileId] = row
        })
        that.files = that.files.map(function(file) {
          var resolved = map[file.fileId]
          if (!resolved) {
            return file
          }
          return that.mergeResolvedAccess(file, resolved, function(updatedFile) {
            that.replaceFilePreview('files', updatedFile)
          })
        })
      }).finally(function() {
        that.resolving = false
      })
    },
    /**
     * 弹窗右侧已选列表也需要可预览地址，但确认前不写入外部字段。
     */
    resolveDialogFiles() {
      var ids = this.dialogFiles.map(function(file) {
        return file.fileId
      }).filter(function(fileId) {
        return fileId
      })
      if (!ids.length) {
        return
      }
      var that = this
      this.request({
        url: '/admin/sys/file-library/resolve',
        method: 'POST',
        data: {
          fileIds: ids
        }
      }).then(function(rows) {
        var map = {}
        ;(rows || []).forEach(function(row) {
          map[row.fileId] = row
        })
        that.dialogFiles = that.dialogFiles.map(function(file) {
          var resolved = map[file.fileId]
          if (!resolved) {
            return file
          }
          return that.mergeResolvedAccess(file, resolved, function(updatedFile) {
            that.replaceFilePreview('dialogFiles', updatedFile)
          })
        })
      })
    },
    /**
     * 文件列表也统一解析访问地址，使公开和受保护图片都能直接显示缩略图。
     */
    resolveLibraryFiles() {
      var ids = this.libraryFiles.map(function(file) {
        return file.fileId || file.id
      }).filter(function(fileId) {
        return fileId
      })
      if (!ids.length) {
        return
      }
      var that = this
      this.request({
        url: '/admin/sys/file-library/resolve',
        method: 'POST',
        data: {
          fileIds: ids
        }
      }).then(function(rows) {
        var map = {}
        ;(rows || []).forEach(function(row) {
          map[row.fileId] = row
        })
        that.libraryFiles = that.libraryFiles.map(function(file) {
          var resolved = map[file.fileId || file.id]
          return resolved ? that.mergeResolvedAccess(file, resolved, function(updatedFile) {
            that.replaceFilePreview('libraryFiles', updatedFile)
          }) : file
        })
      })
    },
    /**
     * 合并后端权限解析结果，并为图片准备可直接渲染的预览地址。
     */
    mergeResolvedAccess(file, resolved, previewReady) {
      var merged = Object.assign({}, file, {
        name: resolved.originalName || file.name,
        originalName: resolved.originalName || file.originalName,
        url: this.buildAbsoluteUrl(resolved.url),
        previewUrl: file.previewUrl || '',
        mode: resolved.mode || '',
        mimeType: resolved.mimeType || file.mimeType,
        folderId: resolved.folderId || file.folderId || ''
      })
      this.prepareImageThumbnail(merged, previewReady)
      return merged
    },
    /**
     * 本地及 S3 代理图片携带登录上下文下载为临时地址，避免绕过权限校验。
     */
    prepareImageThumbnail(file, previewReady) {
      if (!file || !this.isImage(file) || !file.url) {
        return
      }
      // S3 代理流和本地流均需鉴权下载，只有公开或签名地址可直接用作缩略图。
      if (file.mode !== 'LOCAL_STREAM' && file.mode !== 'PROXY_STREAM') {
        file.previewUrl = this.buildAbsoluteUrl(file.url)
        return
      }
      if (file.previewUrl || file.previewLoading) {
        return
      }
      file.previewLoading = true
      uni.downloadFile({
        url: file.url,
        header: this.buildAuthHeader(),
        success: function(res) {
          if (res.statusCode === 200) {
            file.previewUrl = res.tempFilePath
          }
          file.previewLoading = false
          if (file.previewUrl && previewReady) {
            previewReady(file)
          }
        },
        fail: function() {
          file.previewLoading = false
        }
      })
    },
    /**
     * 缩略图异步下载完成后替换数组项，确保卡片无需点击也能立即重渲染。
     */
    replaceFilePreview(listName, updatedFile) {
      var fileId = updatedFile && (updatedFile.fileId || updatedFile.id)
      if (!fileId || !Array.isArray(this[listName])) {
        return
      }
      this[listName] = this[listName].map(function(file) {
        var currentFileId = file.fileId || file.id
        if (currentFileId !== fileId) {
          return file
        }
        return Object.assign({}, file, {
          previewUrl: updatedFile.previewUrl || file.previewUrl || '',
          previewLoading: false
        })
      })
    },
    chooseUploadType() {
      var count = this.dialogVisible ? this.dialogRemainingCount : this.remainingCount
      if (this.readonly || count <= 0 || this.uploading) {
        return
      }
      var modes = this.acceptModes
      if (modes.length === 1) {
        this.chooseFilesByType(modes[0])
        return
      }
      var labels = modes.map(this.uploadTypeLabel)
      var that = this
      uni.showActionSheet({
        itemList: labels,
        success: function(res) {
          that.chooseFilesByType(modes[res.tapIndex])
        }
      })
    },
    chooseFilesByType(type) {
      var that = this
      var count = this.dialogVisible ? this.dialogRemainingCount : this.remainingCount
      if (type === 'image') {
        uni.chooseImage({
          count: count,
          success: function(res) {
            that.uploadChosenFiles(that.normalizeChosenFiles(res))
          }
        })
        return
      }
      if (type === 'video') {
        uni.chooseVideo({
          success: function(res) {
            that.uploadChosenFiles(that.normalizeChosenFiles({
              tempFiles: [res]
            }))
          }
        })
        return
      }
      if (this.chooseFilesWithNativeInput(type)) {
        return
      }
      if (typeof uni.chooseFile !== 'function') {
        this.showToast('当前环境不支持文件选择')
        return
      }
      try {
        uni.chooseFile({
          count: count,
          extension: this.normalizedExtensions,
          success: function(res) {
            that.uploadChosenFiles(that.normalizeChosenFiles(res))
          },
          fail: function(res) {
            if (that.isChooseCancel(res)) {
              return
            }
            that.showToast((res && res.errMsg) || '文件选择失败')
          }
        })
      } catch (e) {
        this.showToast(e.message || '文件选择失败')
      }
    },
    /**
     * H5 下普通文件选择优先使用浏览器原生 file input，避开部分 uni.chooseFile 无响应的问题。
     */
    chooseFilesWithNativeInput(type) {
      if (typeof document === 'undefined' || !document.body) {
        return false
      }
      var count = this.dialogVisible ? this.dialogRemainingCount : this.remainingCount
      var input = document.createElement('input')
      var that = this
      var cleaned = false
      input.type = 'file'
      input.style.display = 'none'
      input.multiple = count > 1
      var accept = this.buildNativeInputAccept(type)
      if (accept) {
        input.accept = accept
      }
      var cleanup = function() {
        if (cleaned) {
          return
        }
        cleaned = true
        input.onchange = null
        if (input.parentNode) {
          input.parentNode.removeChild(input)
        }
      }
      input.onchange = function(event) {
        var rawFiles = Array.prototype.slice.call((event.target && event.target.files) || [])
        cleanup()
        if (!rawFiles.length) {
          return
        }
        that.uploadChosenFiles(rawFiles.slice(0, count).map(function(rawFile) {
          var objectUrl = ''
          if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
            objectUrl = URL.createObjectURL(rawFile)
          }
          return {
            path: objectUrl,
            name: rawFile.name || '未命名文件',
            size: rawFile.size || 0,
            mimeType: rawFile.type || '',
            rawFile: rawFile,
            nativeObjectUrl: objectUrl
          }
        }))
      }
      try {
        document.body.appendChild(input)
        input.click()
        setTimeout(cleanup, 60000)
        return true
      } catch (e) {
        cleanup()
        return false
      }
    },
    buildNativeInputAccept(type) {
      if (type === 'image') {
        return 'image/*'
      }
      if (type === 'video') {
        return 'video/*'
      }
      return this.normalizedExtensions.join(',')
    },
    isChooseCancel(res) {
      return res && String(res.errMsg || '').toLowerCase().indexOf('cancel') >= 0
    },
    normalizeChosenFiles(res) {
      if (res && Array.isArray(res.tempFiles) && res.tempFiles.length) {
        return res.tempFiles.map(function(file) {
          var rawFile = file.file || file.rawFile || null
          var path = file.path || file.tempFilePath || file.tempFile || ''
          var objectUrl = ''
          if (!path && rawFile && typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
            objectUrl = URL.createObjectURL(rawFile)
            path = objectUrl
          }
          return {
            path: path,
            name: file.name || (rawFile && rawFile.name) || path.substring(path.lastIndexOf('/') + 1),
            size: file.size || 0,
            mimeType: file.type || file.mimeType || (rawFile && rawFile.type) || '',
            rawFile: rawFile,
            nativeObjectUrl: objectUrl
          }
        })
      }
      return ((res && res.tempFilePaths) || []).map(function(path) {
        return {
          path: path,
          name: path.substring(path.lastIndexOf('/') + 1),
          size: 0,
          mimeType: ''
        }
      })
    },
    /**
     * 上传流程：创建后端可信上传计划 -> 按供应商上传 -> complete 固化文件 -> 回写字段。
     */
    async uploadChosenFiles(chosenFiles) {
      if (!chosenFiles || !chosenFiles.length) {
        return
      }
      var acceptedFiles = []
      var uploadFiles = []
      try {
        var count = this.dialogVisible ? this.dialogRemainingCount : this.remainingCount
        acceptedFiles = chosenFiles.slice(0, count)
        if (!acceptedFiles.length) {
          this.showToast('已达到文件数量上限')
          return
        }
        this.uploading = true
        for (let i = 0; i < acceptedFiles.length; i++) {
          // 图片裁切默认关闭，只有控件明细显式开启时才透传裁切开关。
          var preparedFile = await cropImageFile(acceptedFiles[i], { cropImages: this.cropImagesValue })
          if (preparedFile) {
            uploadFiles.push(preparedFile)
          }
        }
        if (!uploadFiles.length) {
          return
        }
        for (let i = 0; i < uploadFiles.length; i++) {
          if (!this.validateChosenFile(uploadFiles[i])) {
            return
          }
        }
        yzp.showUploadToast()
        for (let i = 0; i < uploadFiles.length; i++) {
          await this.uploadOneFile(uploadFiles[i])
        }
        yzp.showUploadSuccess()
        if (!this.dialogVisible) {
          this.emitChange()
        }
      } catch (e) {
        uni.hideLoading()
        uni.showModal({
          title: '上传失败',
          content: e.message || '上传失败',
          showCancel: false
        })
      } finally {
        this.uploading = false
        this.revokeNativeObjectUrls(acceptedFiles)
        this.revokeNativeObjectUrls(uploadFiles)
      }
    },
    validateChosenFile(file) {
      if (!file || (!file.path && !file.rawFile)) {
        this.showToast('文件路径不存在')
        return false
      }
      if (file.size && file.size > this.normalizedMaxSizeBytes) {
        this.showToast('文件大小超过限制')
        return false
      }
      if (this.normalizedExtensions.length) {
        var suffix = this.getSuffix(file.name || file.path)
        if (this.normalizedExtensions.indexOf(suffix) < 0) {
          this.showToast('文件后缀不在允许范围内')
          return false
        }
      }
      return true
    },
    revokeNativeObjectUrls(files) {
      if (typeof URL === 'undefined' || typeof URL.revokeObjectURL !== 'function') {
        return
      }
      ;(files || []).forEach(function(file) {
        if (file && file.nativeObjectUrl) {
          URL.revokeObjectURL(file.nativeObjectUrl)
        }
      })
    },
    async uploadOneFile(file) {
      var planResult = await this.request({
        url: '/admin/sys/file-library/upload-plan',
        method: 'POST',
        data: {
          configColumnId: this.configColumnId,
          originalName: file.name,
          mimeType: file.mimeType || this.guessMimeType(file.name),
          sizeBytes: file.size || 0,
          folderId: this.currentFolderId || ''
        }
      })
      await this.uploadWithPlan(file, planResult.plan || {})
      var completed = await this.request({
        url: '/admin/sys/file-library/complete',
        method: 'POST',
        data: {
          fileId: planResult.fileId
        }
      })
      var item = this.normalizeFileItem(completed || {})
      item.fileId = item.fileId || planResult.fileId
      if (this.dialogVisible) {
        this.dialogFiles.push(item)
        this.resolveDialogFiles()
        this.loadLibraryFiles()
        this.loadFolders()
        return
      }
      this.files.push(item)
      this.resolveFiles()
    },
    /**
     * DIRECT_FORM 代表对象存储签名表单直传；只有后端 multipart 才附带应用登录头。
     */
    uploadWithPlan(file, plan) {
      var uploadMode = plan.uploadMode || 'SERVER_MULTIPART'
      var uploadUrl = uploadMode === 'DIRECT_FORM' ? plan.uploadUrl : this.buildAbsoluteUrl(plan.uploadUrl)
      var header = Object.assign({}, plan.headers || {})
      if (uploadMode === 'SERVER_MULTIPART') {
        header = Object.assign({}, header, this.buildAuthHeader())
      }
      return new Promise(function(resolve, reject) {
        var uploadOptions = {
          url: uploadUrl,
          name: 'file',
          header: header,
          formData: plan.formData || {},
          success: function(res) {
            if (uploadMode === 'SERVER_MULTIPART') {
              var body = typeof res.data === 'string' ? safeJsonParse(res.data, {}) : (res.data || {})
              if (body.code !== 200) {
                reject(new Error(body.msg || '上传失败'))
                return
              }
            } else if (res.statusCode < 200 || res.statusCode >= 300) {
              reject(new Error('对象存储上传失败'))
              return
            }
            resolve(res)
          },
          fail: function(res) {
            reject(new Error((res && res.errMsg) || '上传失败'))
          }
        }
        // H5 原生 file input 只能提供 File 对象；小程序/App 继续使用 filePath。
        if (file.rawFile && (file.nativeObjectUrl || (typeof File !== 'undefined' && file.rawFile instanceof File))) {
          uploadOptions.file = file.rawFile
        }
        if (file.path) {
          uploadOptions.filePath = file.path
        }
        uni.uploadFile(uploadOptions)
      })
    },
    loadLibraryFiles() {
      if (!this.allowLibraryValue) {
        this.libraryFiles = []
        this.libraryLoading = false
        return
      }
      var that = this
      this.libraryLoading = true
      this.libraryFilesLoadError = ''
      this.request({
        url: '/admin/sys/file-library/list',
        method: 'POST',
        data: {
          configColumnId: this.configColumnId,
          keyword: this.libraryKeyword,
          folderId: this.isLibrarySearching() ? null : (this.currentFolderId || '')
        }
      }).then(function(rows) {
        that.libraryFiles = rows || []
        that.syncCurrentFolderFileCount(that.libraryFiles.length)
        that.resolveLibraryFiles()
      }).catch(function(error) {
        // 文件库加载失败不能伪装成空目录，否则用户无法判断是无数据还是接口异常。
        that.libraryFilesLoadError = that.formatLoadError(error, '文件列表加载失败')
        that.showToast(that.libraryFilesLoadError)
      }).finally(function() {
        that.libraryLoading = false
      })
    },
    /**
     * 有关键词时进入全局搜索态，不再把当前目录作为查询条件。
     */
    isLibrarySearching() {
      return String(this.libraryKeyword || '').trim().length > 0
    },
    /**
     * 当前目录列表是后端返回的真实结果，用它校准左侧目录树的文件数量。
     */
    syncCurrentFolderFileCount(fileCount) {
      if (String(this.libraryKeyword || '').trim()) {
        return
      }
      var normalizedCount = this.normalizeFolderFileCount(fileCount)
      if (!this.currentFolderId) {
        this.rootDirectFolderFileCount = normalizedCount
        this.applyAggregatedFolderFileCounts()
        return
      }
      this.folders = this.folders.map(function(folder) {
        if (folder.id !== this.currentFolderId) {
          return folder
        }
        return Object.assign({}, folder, {
          directFileCount: normalizedCount
        })
      }, this)
      this.applyAggregatedFolderFileCounts()
    },
    normalizeFolder(item) {
      return {
        id: item.id || '',
        parentId: item.parentId || '',
        name: item.name || item.id || '',
        sortNo: item.sortNo || 0
      }
    },
    /**
     * 目录树是展示元数据，加载失败不会影响已选文件回显。
     */
    loadFolders() {
      var that = this
      this.foldersLoading = true
      this.foldersLoadError = ''
      this.request({
        url: '/admin/sys/file-library/folders/list',
        method: 'POST',
        data: {
          configColumnId: this.configColumnId
        }
      }).then(function(rows) {
        that.folders = normalizeFolders(rows)
        if (that.currentFolderId && !that.folders.some(function(folder) {
          return folder.id === that.currentFolderId
        })) {
          that.currentFolderId = ''
        }
        return that.refreshFolderFileCounts()
      }).catch(function(error) {
        // 文件夹加载失败时保留已选文件回显，同时把真实错误展示出来。
        that.foldersLoadError = that.formatLoadError(error, '文件夹加载失败')
        that.showToast(that.foldersLoadError)
      }).finally(function() {
        that.foldersLoading = false
      })
    },
    /**
     * 首屏目录数量复用文件列表接口计算，和用户点击目录后的回填结果保持一致。
     */
    refreshFolderFileCounts() {
      if (!this.allowLibraryValue) {
        this.rootDirectFolderFileCount = 0
        this.rootFolderFileCount = 0
        this.folders = this.folders.map(function(folder) {
          return Object.assign({}, folder, {
            directFileCount: 0,
            fileCount: 0
          })
        })
        return Promise.resolve()
      }
      var that = this
      var countTargets = [{
        id: ''
      }].concat(this.folders.map(function(folder) {
        return {
          id: folder.id || ''
        }
      }))
      var countTasks = countTargets.map(function(folder) {
        return that.request({
          url: '/admin/sys/file-library/list',
          method: 'POST',
          data: {
            configColumnId: that.configColumnId,
            keyword: '',
            folderId: folder.id || ''
          }
        }).then(function(rows) {
          return {
            folderId: folder.id || '',
            fileCount: that.normalizeFolderFileCount((rows || []).length)
          }
        }).catch(function() {
          // 单个目录数量刷新失败时保留原值，不阻断文件管理弹窗。
          return null
        })
      })
      return Promise.all(countTasks).then(function(results) {
        var fileCountMap = {}
        var rootDirectFileCount = 0
        ;(results || []).forEach(function(result) {
          if (!result) {
            return
          }
          if (!result.folderId) {
            rootDirectFileCount = result.fileCount
            return
          }
          fileCountMap[result.folderId] = result.fileCount
        })
        that.rootDirectFolderFileCount = rootDirectFileCount
        that.folders = that.folders.map(function(folder) {
          if (!Object.prototype.hasOwnProperty.call(fileCountMap, folder.id)) {
            return folder
          }
          return Object.assign({}, folder, {
            directFileCount: fileCountMap[folder.id]
          })
        })
        that.applyAggregatedFolderFileCounts()
      })
    },
    /**
     * 左侧目录数量展示累计值，父目录包含所有子目录文件数。
     */
    applyAggregatedFolderFileCounts() {
      var aggregateResult = aggregateFolderFileCounts(this.folders, this.rootDirectFolderFileCount)
      this.rootFolderFileCount = aggregateResult.rootFileCount
      this.folders = aggregateResult.folders
    },
    selectFolder(folderId) {
      this.currentFolderId = folderId || ''
      this.loadLibraryFiles()
    },
    /**
     * 文件夹卡片只是导航对象，点击后进入目录并退出搜索态。
     */
    openFolderItem(item) {
      if (!item || item.type !== 'folder') {
        return
      }
      this.libraryKeyword = ''
      this.selectFolder(item.id)
    },
    /**
     * 面包屑点击会切换目录，并清空搜索结果回到目录浏览。
     */
    selectBreadcrumbFolder(folder) {
      this.libraryKeyword = ''
      this.selectFolder(folder && folder.id)
    },
    /**
     * 返回上一级只改变当前浏览目录，不影响已选文件。
     */
    goParentFolder() {
      if (this.currentFolderPath.length <= 1) {
        return
      }
      var parentFolder = this.currentFolderPath[this.currentFolderPath.length - 2]
      this.libraryKeyword = ''
      this.selectFolder(parentFolder && parentFolder.id)
    },
    /**
     * 移动资源弹窗使用独立的根目录展开状态，避免影响浏览区目录。
     */
    toggleMoveRootFolderExpanded() {
      this.moveRootFolderExpanded = !this.moveRootFolderExpanded
    },
    openCreateFolderDialog() {
      this.folderNameDialogMode = 'create'
      this.folderNameDialogParentId = this.currentFolderId || ''
      this.folderNameDialogFolderId = ''
      this.folderNameDialogFileId = ''
      this.folderNameDialogValue = ''
      this.folderNameDialogVisible = true
    },
    /**
     * 文件重命名复用名称弹窗，但提交到文件接口而不是文件夹接口。
     */
    openRenameFileDialog(file) {
      var fileId = file && (file.fileId || file.id)
      if (!fileId) {
        this.showToast('请选择文件')
        return
      }
      this.folderNameDialogMode = 'renameFile'
      this.folderNameDialogParentId = ''
      this.folderNameDialogFolderId = ''
      this.folderNameDialogFileId = fileId
      this.folderNameDialogValue = file.originalName || file.name || fileId
      this.folderNameDialogVisible = true
    },
    openRenameFolderDialog(folder) {
      var targetFolder = folder || this.folders.find(function(item) {
        return item.id === this.currentFolderId
      }, this)
      if (!targetFolder || !targetFolder.id) {
        this.showToast('请选择文件夹')
        return
      }
      this.folderNameDialogMode = 'rename'
      this.folderNameDialogParentId = ''
      this.folderNameDialogFolderId = targetFolder.id
      this.folderNameDialogFileId = ''
      this.folderNameDialogValue = targetFolder.name || ''
      this.folderNameDialogVisible = true
    },
    cancelFolderNameDialog() {
      if (this.folderNameDialogSubmitting) {
        return
      }
      this.resetFolderNameDialog()
    },
    /**
     * 新建和重命名共用一个轻量弹窗，确认时再提交到原有文件夹接口。
     */
    confirmFolderNameDialog() {
      var name = String(this.folderNameDialogValue || '').trim()
      if (!name) {
        this.showToast(this.folderNameDialogMode === 'renameFile' ? '请输入文件名称' : '请输入文件夹名称')
        return
      }
      var that = this
      this.folderNameDialogSubmitting = true
      var submitTask
      if (this.folderNameDialogMode === 'renameFile') {
        submitTask = this.renameFileWithName(name)
      } else if (this.folderNameDialogMode === 'rename') {
        submitTask = this.renameFolderWithName(name)
      } else {
        submitTask = this.createFolderWithName(name, this.folderNameDialogParentId)
      }
      submitTask.then(function() {
        that.resetFolderNameDialog()
      }).catch(function(error) {
        that.showToast(error.message || '名称保存失败')
      }).finally(function() {
        that.folderNameDialogSubmitting = false
      })
    },
    resetFolderNameDialog() {
      this.folderNameDialogVisible = false
      this.folderNameDialogMode = 'create'
      this.folderNameDialogValue = ''
      this.folderNameDialogParentId = ''
      this.folderNameDialogFolderId = ''
      this.folderNameDialogFileId = ''
      this.folderNameDialogSubmitting = false
    },
    createFolderWithName(name, parentId) {
      var that = this
      return this.request({
        url: '/admin/sys/file-library/folders',
        method: 'POST',
        data: {
          configColumnId: this.configColumnId,
          parentId: parentId || '',
          name: name
        }
      }).then(function(folder) {
        that.currentFolderId = (folder && folder.id) || ''
        that.libraryKeyword = ''
        that.loadFolders()
        that.loadLibraryFiles()
      })
    },
    renameFolderWithName(name) {
      var folderId = this.folderNameDialogFolderId || this.currentFolderId
      if (!folderId) {
        this.showToast('请选择文件夹')
        return Promise.reject(new Error('请选择文件夹'))
      }
      var that = this
      return this.request({
        url: '/admin/sys/file-library/folders/' + folderId,
        method: 'PUT',
        data: {
          name: name
        }
      }).then(function() {
        that.loadFolders()
      })
    },
    renameFileWithName(name) {
      var fileId = this.folderNameDialogFileId
      if (!fileId) {
        this.showToast('请选择文件')
        return Promise.reject(new Error('请选择文件'))
      }
      var that = this
      return this.request({
        url: '/admin/sys/file-library/' + fileId + '/name',
        method: 'PUT',
        data: {
          name: name
        }
      }).then(function(file) {
        var fileName = (file && file.originalName) || name
        that.applyRenamedFile(fileId, fileName)
        that.loadLibraryFiles()
      })
    },
    /**
     * 文件改名后同步当前列表、右侧已选和外部已选展示，避免需要重新打开弹窗才看到新名称。
     */
    applyRenamedFile(fileId, name) {
      function renameInList(files) {
        return (files || []).map(function(file) {
          var currentFileId = file.fileId || file.id
          if (currentFileId !== fileId) {
            return file
          }
          return Object.assign({}, file, {
            name: name,
            originalName: name
          })
        })
      }
      this.libraryFiles = renameInList(this.libraryFiles)
      this.dialogFiles = renameInList(this.dialogFiles)
      this.files = renameInList(this.files)
    },
    confirmDeleteCurrentFolder(folder) {
      var folderId = (folder && folder.id) || this.currentFolderId
      if (!folderId) {
        return
      }
      var that = this
      var folderPath = buildFolderPath(this.folders, folderId).map(function(pathFolder) {
        return pathFolder.name
      }).join(' / ')
      uni.showModal({
        title: '删除文件夹',
        content: '确定删除文件夹“' + folderPath + '”吗？只有空文件夹可以删除。',
        success: function(res) {
          if (res.confirm) {
            that.deleteCurrentFolder(folderId)
          }
        }
      })
    },
    deleteCurrentFolder(folderId) {
      var that = this
      this.request({
        url: '/admin/sys/file-library/folders/' + folderId,
        method: 'DELETE'
      }).then(function() {
        if (that.currentFolderId === folderId) {
          that.currentFolderId = ''
        }
        that.loadFolders()
        that.loadLibraryFiles()
      }).catch(function(error) {
        // 后端拒绝删除非空目录时，把业务错误明确反馈给用户。
        that.showToast(error.message || '删除文件夹失败')
      })
    },
    chooseMoveTarget(item) {
      var fileId = item.fileId || item.id
      if (!fileId) {
        return
      }
      this.movingFile = item
      this.movingFolder = null
      this.moveTargetFolderId = item.folderId || ''
      this.moveRootFolderExpanded = true
      this.moveDialogVisible = true
    },
    /**
     * 文件夹移动复用树状目标选择，但会过滤自身及后代目录。
     */
    chooseMoveFolderTarget(folder) {
      if (!folder || !folder.id) {
        this.showToast('请选择文件夹')
        return
      }
      this.movingFile = null
      this.movingFolder = folder
      this.moveTargetFolderId = folder.parentId || ''
      this.moveRootFolderExpanded = true
      this.moveDialogVisible = true
    },
    selectMoveTarget(folderId) {
      var targetFolderId = folderId || ''
      if (this.isMoveTargetDisabled(targetFolderId)) {
        this.showToast('不能移动到自身或子文件夹下')
        return
      }
      this.moveTargetFolderId = targetFolderId
    },
    cancelMoveTarget() {
      this.moveDialogVisible = false
      this.movingFile = null
      this.movingFolder = null
      this.moveTargetFolderId = ''
      this.moveRootFolderExpanded = true
    },
    /**
     * 确认后才提交移动请求，避免用户浏览树结构时误触发移动。
     */
    confirmMoveTarget() {
      if (!this.movingFile && !this.movingFolder) {
        return
      }
      var targetFolderId = this.moveTargetFolderId || ''
      if (this.movingFolder) {
        var folderId = this.movingFolder.id
        if (this.isMoveTargetDisabled(targetFolderId)) {
          this.showToast('不能移动到自身或子文件夹下')
          return
        }
        this.cancelMoveTarget()
        this.moveLibraryFolder(folderId, targetFolderId)
        return
      }
      var fileId = this.movingFile.fileId || this.movingFile.id
      this.cancelMoveTarget()
      this.moveLibraryFile(fileId, targetFolderId)
    },
    /**
     * 移动文件只改变展示目录，后端不会迁移云存储对象。
     */
    moveLibraryFile(fileId, targetFolderId) {
      var that = this
      this.request({
        url: '/admin/sys/file-library/move',
        method: 'POST',
        data: {
          fileId: fileId,
          targetFolderId: targetFolderId || ''
        }
      }).then(function() {
        that.loadFolders()
        that.loadLibraryFiles()
      })
    },
    /**
     * 移动文件夹只改变展示层级，当前浏览目录保持不变并刷新面包屑和列表。
     */
    moveLibraryFolder(folderId, targetParentId) {
      var that = this
      return this.request({
        url: '/admin/sys/file-library/folders/' + folderId + '/move',
        method: 'PUT',
        data: {
          targetParentId: targetParentId || ''
        }
      }).then(function() {
        that.libraryKeyword = ''
        that.loadFolders()
        that.loadLibraryFiles()
      }).catch(function(error) {
        that.showToast(error.message || '移动文件夹失败')
      })
    },
    /**
     * 判断文件夹移动目标是否会形成“自己包含自己”的循环关系。
     */
    isMoveTargetDisabled(folderId) {
      if (!this.movingFolder) {
        return false
      }
      var targetFolderId = String(folderId || '').trim()
      if (!targetFolderId) {
        return false
      }
      var sourceFolderId = this.movingFolder.id
      if (targetFolderId === sourceFolderId) {
        return true
      }
      return this.isFolderDescendant(targetFolderId, sourceFolderId)
    },
    /**
     * 从目标文件夹向上查父级，命中 ancestorFolderId 表示它是后代目录。
     */
    isFolderDescendant(folderId, ancestorFolderId) {
      var visitedIds = {}
      var nextFolderId = String(folderId || '').trim()
      while (nextFolderId) {
        if (visitedIds[nextFolderId]) {
          return false
        }
        visitedIds[nextFolderId] = true
        if (nextFolderId === ancestorFolderId) {
          return true
        }
        var folder = this.folders.find(function(item) {
          return item.id === nextFolderId
        })
        if (!folder) {
          return false
        }
        nextFolderId = String(folder.parentId || '').trim()
      }
      return false
    },
    /**
     * 打开文件管理弹窗时先复制当前值，取消时不会污染外部 v-model。
     */
    openManagerDialog() {
      if (this.readonly) {
        return
      }
      this.dialogFiles = this.cloneFiles(this.files)
      this.dialogFilesSnapshot = JSON.stringify(this.dialogFiles.map(this.toEmitFile))
      this.dialogMaximized = false
      this.dialogVisible = true
      this.loadFolders()
      this.loadLibraryFiles()
      this.resolveDialogFiles()
    },
    cancelManagerDialog() {
      this.dialogFiles = []
      this.dialogFilesSnapshot = ''
      this.dialogMaximized = false
      this.dialogVisible = false
    },
    /**
     * 切换文件管理弹窗最大化状态，方便一次浏览更多文件。
     */
    toggleDialogMaximized() {
      this.dialogMaximized = !this.dialogMaximized
    },
    /**
     * 只有点击确定后，弹窗临时选择才写回业务字段。
     */
    confirmManagerDialog() {
      this.files = this.cloneFiles(this.dialogFiles)
      this.emitChange()
      this.resolveFiles()
      this.cancelManagerDialog()
    },
    cloneFiles(files) {
      var that = this
      return (files || []).map(function(file) {
        return that.normalizeFileItem(Object.assign({}, file))
      })
    },
    isDialogSelected(item) {
      var fileId = item.fileId || item.id
      return this.dialogFiles.some(function(file) {
        return file.fileId === fileId
      })
    },
    toggleDialogFile(item) {
      var fileId = item.fileId || item.id
      var selectedIndex = this.dialogFiles.findIndex(function(file) {
        return file.fileId === fileId
      })
      if (selectedIndex >= 0) {
        this.dialogFiles.splice(selectedIndex, 1)
        return
      }
      if (this.dialogRemainingCount <= 0) {
        this.showToast('已达到文件数量上限')
        return
      }
      this.dialogFiles.push(this.normalizeFileItem(item))
      this.resolveDialogFiles()
    },
    removeDialogFile(index) {
      this.dialogFiles.splice(index, 1)
    },
    /**
     * 右侧已选文件保留临时 folderId，用于快速跳转到文件所在目录，不写入外部字段值。
     */
    locateDialogFileFolder(file) {
      if (!file) {
        return
      }
      var folderId = file.folderId || ''
      if (folderId && !this.folders.some(function(folder) {
        return folder.id === folderId
      })) {
        this.showToast('文件所在文件夹不存在')
        return
      }
      this.libraryKeyword = ''
      this.currentFolderId = file.folderId || ''
      this.loadLibraryFiles()
    },
    /**
     * 文件库卡片可能还没有可访问 URL，预览前先按单文件解析权限地址。
     */
    previewLibraryFile(item) {
      var that = this
      var file = this.normalizeFileItem(item)
      if (file.url) {
        this.openFile(file)
        return
      }
      this.request({
        url: '/admin/sys/file-library/resolve',
        method: 'POST',
        data: {
          fileIds: [file.fileId]
        }
      }).then(function(rows) {
        var resolved = (rows || [])[0]
        if (!resolved) {
          that.showToast('文件暂时无法预览')
          return
        }
        that.openFile(Object.assign({}, file, {
          name: resolved.originalName || file.name,
          url: that.buildAbsoluteUrl(resolved.url),
          mode: resolved.mode || '',
          mimeType: resolved.mimeType || file.mimeType
        }))
      })
    },
    confirmDeleteLibraryFile(item) {
      var that = this
      uni.showModal({
        title: '删除文件',
        content: '确定删除该文件吗？如果文件已被其他记录引用，后端会拒绝删除。',
        success: function(res) {
          if (res.confirm) {
            that.deleteLibraryFile(item)
          }
        }
      })
    },
    /**
     * 删除文件成功后，同步清理文件库列表、弹窗临时选择和已保存值。
     */
    deleteLibraryFile(item) {
      var that = this
      var fileId = item.fileId || item.id
      this.request({
        url: '/admin/sys/file-library/' + fileId,
        method: 'DELETE'
      }).then(function() {
        that.libraryFiles = that.libraryFiles.filter(function(file) {
          return (file.fileId || file.id) !== fileId
        })
        that.dialogFiles = that.dialogFiles.filter(function(file) {
          return file.fileId !== fileId
        })
        that.files = that.files.filter(function(file) {
          return file.fileId !== fileId
        })
        that.emitChange()
        that.loadLibraryFiles()
        that.loadFolders()
      }).catch(function(error) {
        // 后端拒绝删除被引用文件时，把业务错误明确反馈给用户。
        that.showToast(error.message || '删除文件失败')
      })
    },
    /**
     * 预览文件时统一补齐相对地址，文件库卡片和已选文件都能直接打开。
     */
    openFile(file) {
      if (!file || !file.url) {
        if (this.dialogVisible) {
          this.resolveDialogFiles()
        } else {
          this.resolveFiles()
        }
        return
      }
      var previewFile = Object.assign({}, file, {
        url: this.buildAbsoluteUrl(file.url)
      })
      // S3 受保护文件复用本地文件的登录下载和预览流程。
      if (previewFile.mode === 'LOCAL_STREAM' || previewFile.mode === 'PROXY_STREAM') {
        this.downloadAndOpen(previewFile)
        return
      }
      if (this.isImage(previewFile)) {
        uni.previewImage({
          urls: [previewFile.url]
        })
        return
      }
      this.openUrl(previewFile.url)
    },
    /**
     * 本地及 S3 受保护文件必须带登录和项目上下文下载，再打开临时文件。
     */
    downloadAndOpen(file) {
      var that = this
      uni.downloadFile({
        url: file.url,
        header: this.buildAuthHeader(),
        success: function(res) {
          if (res.statusCode !== 200) {
            that.showToast('文件下载失败')
            return
          }
          if (that.isImage(file)) {
            uni.previewImage({
              urls: [res.tempFilePath]
            })
            return
          }
          uni.openDocument({
            filePath: res.tempFilePath,
            fail: function() {
              // H5 仅打开已鉴权下载的临时地址，不能重新发起无登录头的源文件请求。
              // #ifdef H5
              that.openUrl(res.tempFilePath)
              // #endif
              // #ifndef H5
              that.showToast('当前环境暂不支持预览此文件')
              // #endif
            }
          })
        },
        fail: function() {
          that.showToast('文件下载失败')
        }
      })
    },
    request(options) {
      return new Promise(function(resolve, reject) {
        yzp.request({
          url: options.url,
          method: options.method || 'GET',
          data: options.data || {},
          sendSessionId: true,
          success: function(res) {
            if (res.code === 200) {
              resolve(res.data)
              return
            }
            reject(new Error(res.msg || '请求失败'))
          },
          fail: function(res) {
            reject(new Error((res && res.msg) || '请求失败'))
          }
        })
      })
    },
    buildAuthHeader() {
      var header = {}
      if (uni.getStorageSync('loginStateId')) {
        header.loginStateId = uni.getStorageSync('loginStateId')
      }
      if (uni.getStorageSync('sysParams')) {
        header.sysParams = encodeURIComponent(JSON.stringify(uni.getStorageSync('sysParams')))
      }
      return header
    },
    buildAbsoluteUrl(url) {
      if (!url) {
        return ''
      }
      if (url.indexOf('http://') === 0 || url.indexOf('https://') === 0) {
        return url
      }
      var baseUrl = getApp().globalData.environment.base_url || ''
      return baseUrl.replace(/\/$/, '') + '/' + String(url).replace(/^\//, '')
    },
    thumbnailUrl(file) {
      if (!file) {
        return ''
      }
      if (file.previewUrl) {
        return file.previewUrl
      }
      // 鉴权流完成下载前不把后端地址赋给 img，避免浏览器发起无登录头请求。
      if (file.mode === 'LOCAL_STREAM' || file.mode === 'PROXY_STREAM') {
        return ''
      }
      return this.buildAbsoluteUrl(file.url || file.publicUrl || '')
    },
    openUrl(url) {
      // #ifdef H5
      window.open(url)
      // #endif
      // #ifndef H5
      plus.runtime.openURL(url)
      // #endif
    },
    uploadTypeLabel(type) {
      if (type === 'image') {
        return '图片'
      }
      if (type === 'video') {
        return '视频'
      }
      return '文件'
    },
    fileIcon(file) {
      if (this.isVideo(file)) {
        return 'videocam'
      }
      return 'paperclip'
    },
    isImage(file) {
      var mimeType = String(file.mimeType || '').toLowerCase()
      return mimeType.indexOf('image/') === 0 || ['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp'].indexOf(this.getSuffix(file.name)) >= 0
    },
    isVideo(file) {
      var mimeType = String(file.mimeType || '').toLowerCase()
      return mimeType.indexOf('video/') === 0 || ['.mp4', '.mov', '.avi', '.mkv'].indexOf(this.getSuffix(file.name)) >= 0
    },
    guessMimeType(name) {
      var suffix = this.getSuffix(name)
      var map = {
        '.jpg': 'image/jpeg',
        '.jpeg': 'image/jpeg',
        '.png': 'image/png',
        '.gif': 'image/gif',
        '.webp': 'image/webp',
        '.mp4': 'video/mp4',
        '.pdf': 'application/pdf'
      }
      return map[suffix] || ''
    },
    getSuffix(name) {
      var text = String(name || '').toLowerCase()
      var index = text.lastIndexOf('.')
      return index >= 0 ? text.substring(index) : ''
    },
    formatFileDesc(file) {
      var parts = []
      if (file.mimeType) {
        parts.push(file.mimeType)
      }
      if (file.sizeBytes) {
        parts.push(this.formatSize(file.sizeBytes))
      }
      return parts.join(' · ')
    },
    /**
     * 列表视图统一输出文件夹和文件类型，缺失 MIME 时退回后缀。
     */
    formatBrowserItemType(item) {
      if (!item || item.type === 'folder') {
        return '文件夹'
      }
      var file = item.file || {}
      if (file.mimeType) {
        return file.mimeType
      }
      var suffix = this.getSuffix(file.originalName || file.name || '')
      return suffix ? suffix.substring(1).toUpperCase() + ' 文件' : '文件'
    },
    /**
     * 列表视图中文件夹没有物理大小，文件沿用现有大小格式。
     */
    formatBrowserItemSize(item) {
      if (!item || item.type === 'folder') {
        return '-'
      }
      var size = Number((item.file && (item.file.sizeBytes || item.file.size)) || 0)
      return size > 0 ? this.formatSize(size) : '0 B'
    },
    /**
     * 列表视图用状态列承接受保护锁标识，公开文件显示为公开。
     */
    formatBrowserItemStatus(item) {
      if (!item || item.type === 'folder') {
        return '-'
      }
      return item.file && item.file.accessMode === 'PROTECTED' ? '受保护' : '公开'
    },
    formatLibraryFolderPath(item) {
      return buildFolderPath(this.folders, item && item.folderId).map(function(folder) {
        return folder.name
      }).join(' / ')
    },
    /**
     * 根目录不是后端文件夹记录，单独用当前根目录列表结果维护文件数。
     */
    formatRootFolderFileCount() {
      return this.formatFolderCountValue(this.rootFolderFileCount)
    },
    formatFolderFileCount(folder) {
      return this.formatFolderCountValue(folder && folder.fileCount)
    },
    normalizeFolderFileCount(fileCount) {
      var count = Number(fileCount || 0)
      if (!Number.isFinite(count) || count < 0) {
        return 0
      }
      return count
    },
    formatFolderCountValue(fileCount) {
      var count = this.normalizeFolderFileCount(fileCount)
      return '(' + (count > 0 ? count : 0) + ')'
    },
    formatLoadError(error, fallback) {
      return (error && error.message) || fallback || '加载失败'
    },
    formatSize(size) {
      var value = Number(size || 0)
      if (value >= 1024 * 1024) {
        return (value / 1024 / 1024).toFixed(1) + ' MB'
      }
      if (value >= 1024) {
        return (value / 1024).toFixed(1) + ' KB'
      }
      return value + ' B'
    },
    showToast(title) {
      uni.showToast({
        title: title,
        icon: 'none'
      })
    }
  }
}
</script>

<style lang="scss" scoped>
.yzp-file-library {
  width: 100%;
}

.yzp-file-library__summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 54px;
  padding: 9px 10px;
  border: 1px solid #dfe6ef;
  border-radius: 8px;
  background: #ffffff;
  box-sizing: border-box;
}

.yzp-file-library__summary-main {
  display: flex;
  align-items: center;
  min-width: 0;
  flex: 1;
}

.yzp-file-library__summary-icon {
  width: 34px;
  height: 34px;
  border-radius: 7px;
  background: #eef4fb;
  display: flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
}

.yzp-file-library__summary-text {
  min-width: 0;
  padding-left: 10px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.yzp-file-library__summary-title,
.yzp-file-library__dialog-title {
  color: #18212f;
  font-size: 14px;
  line-height: 20px;
  font-weight: 600;
}

.yzp-file-library__summary-desc,
.yzp-file-library__dialog-subtitle,
.yzp-file-library__desc {
  color: #7b8794;
  font-size: 12px;
  line-height: 18px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__manage-button,
.yzp-file-library__plain-button,
.yzp-file-library__upload-button,
.yzp-file-library__confirm-button {
  height: 32px;
  min-height: 32px;
  line-height: 32px;
  padding: 0 12px;
  margin: 0;
  flex: 0 0 auto;
  border-radius: 5px;
  font-size: 13px;
  box-sizing: border-box;
  white-space: nowrap;
}

.yzp-file-library__upload-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  min-width: 78px;
  line-height: normal;
}

.yzp-file-library__upload-button text {
  color: #ffffff;
  font-size: 13px;
  font-weight: 600;
}

.yzp-file-library__plain-button {
  background: #ffffff;
  color: #18212f;
}

.yzp-file-library__confirm-button {
  min-width: 76px;
}

.yzp-file-library__selected-preview {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.yzp-file-library__selected-chip {
  max-width: 240px;
  height: 48px;
  padding: 4px 8px 4px 4px;
  border: 1px solid #dfe6ef;
  border-radius: 6px;
  background: #f8fafc;
  display: flex;
  align-items: center;
  gap: 5px;
  color: #394657;
  font-size: 12px;
  box-sizing: border-box;
  cursor: pointer;
}

.yzp-file-library__selected-chip-thumb {
  width: 40px;
  height: 40px;
  flex: 0 0 40px;
  border-radius: 4px;
  background: #eef3f8;
}

.yzp-file-library__selected-chip text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__readonly-empty {
  min-height: 42px;
  margin-top: 8px;
  border: 1px dashed #d7dde6;
  border-radius: 8px;
  color: #8d99a6;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
}

.yzp-file-library__dialog-mask {
  position: fixed;
  left: 0;
  right: 0;
  top: 0;
  bottom: 0;
  z-index: 999;
  background: rgba(17, 24, 39, 0.42);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  box-sizing: border-box;
}

.yzp-file-library__dialog {
  width: min(920px, 100%);
  max-height: min(680px, 92vh);
  border-radius: 8px;
  background: #f6f8fb;
  position: relative;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 18px 50px rgba(15, 23, 42, 0.22);
}

.yzp-file-library__dialog--maximized {
  width: 100%;
  height: 100%;
  max-height: none;
}

.yzp-file-library__dialog-header,
.yzp-file-library__dialog-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px;
  border-bottom: 1px solid #e7edf4;
  background: #ffffff;
}

.yzp-file-library__dialog-footer {
  justify-content: flex-end;
  gap: 10px;
  border-top: 1px solid #e7edf4;
  border-bottom: none;
}

.yzp-file-library__dialog-header > view:first-child {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.yzp-file-library__dialog-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
  margin-left: 12px;
}

.yzp-file-library__dialog-maximize,
.yzp-file-library__dialog-close,
.yzp-file-library__icon-button {
  width: 30px;
  height: 30px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.yzp-file-library__dialog-maximize,
.yzp-file-library__dialog-close {
  border-radius: 6px;
}

.yzp-file-library__dialog-maximize:hover,
.yzp-file-library__dialog-close:hover {
  background: #eef4fb;
}

.yzp-file-library__dialog-maximize {
  color: #5f6b7a;
}

.yzp-file-library__maximize-icon {
  width: 14px;
  height: 14px;
  border: 2px solid currentColor;
  border-radius: 2px;
  box-sizing: border-box;
  position: relative;
}

.yzp-file-library__dialog-maximize--restore .yzp-file-library__maximize-icon {
  transform: translate(3px, -3px);
}

.yzp-file-library__maximize-icon-back {
  position: absolute;
  left: -7px;
  top: 5px;
  width: 12px;
  height: 12px;
  border: 2px solid currentColor;
  border-radius: 2px;
  background: #ffffff;
  box-sizing: border-box;
}

.yzp-file-library__dialog-toolbar {
  grid-area: search;
  align-self: start;
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 56px;
  padding: 8px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #ffffff;
  box-sizing: border-box;
}

.yzp-file-library__dialog-search {
  min-width: 0;
  flex: 1;
}

.yzp-file-library__dialog-body {
  flex: 1;
  min-height: 360px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 260px;
  grid-template-rows: 56px minmax(0, 1fr);
  grid-template-areas:
    "search selected"
    "browser selected";
  gap: 12px;
  padding: 12px 16px 14px;
  background: #f6f8fb;
  overflow: hidden;
  box-sizing: border-box;
}

.yzp-file-library__browser-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  color: #18212f;
  font-size: 13px;
  line-height: 19px;
  font-weight: 600;
  margin-bottom: 10px;
}

.yzp-file-library__browser-count {
  color: #7b8794;
  font-size: 12px;
  font-weight: 400;
  min-width: 0;
  flex: 0 0 auto;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__browser-head-right {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  gap: 8px;
}

.yzp-file-library__browser-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.yzp-file-library__view-switch {
  height: 28px;
  padding: 2px;
  border: 1px solid #d8e2ee;
  border-radius: 6px;
  background: #ffffff;
  display: flex;
  align-items: center;
  gap: 2px;
}

.yzp-file-library__view-switch-item {
  width: 24px;
  height: 24px;
  border-radius: 5px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.yzp-file-library__view-switch-item--active {
  background: $uni-primary;
}

.yzp-file-library__grid-view-icon {
  width: 14px;
  height: 14px;
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 2px;
}

.yzp-file-library__grid-view-icon view {
  border-radius: 2px;
  background: #64748b;
}

.yzp-file-library__view-switch-item--active .yzp-file-library__grid-view-icon view {
  background: #ffffff;
}

.yzp-file-library__browser-nav {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
}

.yzp-file-library__folder-back-button {
  width: 30px;
  min-width: 30px;
  height: 30px;
  min-height: 30px;
  line-height: normal;
  margin: 0;
  padding: 0;
  border-radius: 5px;
  color: #326da8;
  background: #ffffff;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  white-space: nowrap;
  box-sizing: border-box;
  cursor: pointer;
}

.yzp-file-library__folder-back-button[disabled] {
  cursor: not-allowed;
  background: #f8fafc;
}

.yzp-file-library__breadcrumb {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 4px;
  flex: 0 1 auto;
  overflow: hidden;
}

.yzp-file-library__breadcrumb-item {
  min-width: 0;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: #1f3f66;
  cursor: pointer;
}

.yzp-file-library__breadcrumb-item text:first-child {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__breadcrumb-separator {
  color: #8a96a5;
}

.yzp-file-library__search-label {
  flex: 0 0 auto;
  padding: 2px 6px;
  border-radius: 5px;
  color: #326da8;
  background: #eef4fb;
  font-size: 12px;
  font-weight: 500;
}

.yzp-file-library__folder-toggle {
  width: 18px;
  height: 24px;
  flex: 0 0 18px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
}

.yzp-file-library__folder-toggle:not(.yzp-file-library__folder-toggle--spacer):hover {
  background: #e4edf7;
}

.yzp-file-library__folder-toggle--spacer {
  pointer-events: none;
}

.yzp-file-library__folder-name,
.yzp-file-library__browser-path {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__folder-name {
  flex: 1;
}

.yzp-file-library__folder-count {
  flex: 0 0 auto;
  color: #8a96a5;
  font-size: 12px;
}

.yzp-file-library__browser {
  grid-area: browser;
  min-width: 0;
  min-height: 0;
  padding: 14px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #ffffff;
  overflow: auto;
  box-sizing: border-box;
}

.yzp-file-library__library-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(148px, 156px));
  gap: 16px 14px;
  justify-content: start;
  align-items: start;
}

.yzp-file-library__library-grid--searching .yzp-file-library__library-card {
  height: 224px;
  min-height: 224px;
}

.yzp-file-library__library-grid--searching .yzp-file-library__card-title {
  min-height: 42px;
}

.yzp-file-library__library-list {
  width: 100%;
  overflow-x: auto;
  overflow-y: visible;
}

.yzp-file-library__library-list-table {
  min-width: 760px;
  border: 1px solid #dfe7f1;
  border-radius: 8px;
  background: #ffffff;
  overflow: hidden;
}

.yzp-file-library__library-list-table--searching {
  min-width: 920px;
}

.yzp-file-library__library-list-row {
  min-height: 46px;
  display: grid;
  grid-template-columns: minmax(210px, 1.6fr) minmax(190px, 1fr) minmax(110px, 0.8fr) 88px 86px;
  align-items: center;
  gap: 8px;
  padding: 0 12px;
  border-bottom: 1px solid #edf2f7;
  box-sizing: border-box;
  cursor: pointer;
}

.yzp-file-library__library-list-table--searching .yzp-file-library__library-list-row {
  grid-template-columns: minmax(200px, 1.4fr) minmax(190px, 1fr) minmax(110px, 0.7fr) 84px 84px minmax(160px, 1fr);
}

.yzp-file-library__library-list-row:last-child {
  border-bottom: 0;
}

.yzp-file-library__library-list-row:hover {
  background: #f5f8fc;
}

.yzp-file-library__library-list-row--selected {
  background: #eef6ff;
  box-shadow: inset 3px 0 0 var(--file-library-primary);
}

.yzp-file-library__library-list-head {
  min-height: 38px;
  background: #f8fafc;
  color: #64748b;
  font-size: 12px;
  font-weight: 600;
  cursor: default;
}

.yzp-file-library__library-list-head:hover {
  background: #f8fafc;
}

.yzp-file-library__library-list-cell {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  color: #18212f;
  font-size: 12px;
  line-height: 18px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__library-list-head .yzp-file-library__library-list-cell {
  color: #64748b;
}

.yzp-file-library__library-list-head .yzp-file-library__library-list-actions {
  justify-content: center;
}

.yzp-file-library__library-list-type {
  justify-content: center;
  text-align: center;
}

.yzp-file-library__library-list-name .yzp-file-library__name,
.yzp-file-library__library-list-path {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__library-list-icon {
  width: 30px;
  height: 30px;
  flex: 0 0 30px;
  border-radius: 6px;
  background: #f1f5f9;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.yzp-file-library__library-list-icon--folder {
  background: transparent;
}

.yzp-file-library__folder-icon--list {
  width: 30px;
  height: 25px;
}

.yzp-file-library__library-list-image {
  width: 100%;
  height: 100%;
}

.yzp-file-library__library-list-status {
  color: #64748b;
}

.yzp-file-library__library-list-lock {
  width: 18px;
  height: 18px;
  flex: 0 0 18px;
  border-radius: 50%;
  background: rgba(51, 65, 85, 0.78);
  display: flex;
  align-items: center;
  justify-content: center;
}

.yzp-file-library__library-list-actions {
  justify-content: flex-end;
  gap: 8px;
}

.yzp-file-library__library-list-actions .yzp-file-library__icon-button {
  width: 22px;
  height: 22px;
  flex: 0 0 22px;
}

.yzp-file-library__library-card {
  position: relative;
  width: 148px;
  height: 196px;
  min-height: 196px;
  padding: 6px;
  border: 1px solid transparent;
  border-radius: 10px;
  background: transparent;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  gap: 6px;
  box-sizing: border-box;
  cursor: pointer;
  overflow: visible;
  transition: background 0.16s ease, border-color 0.16s ease;
}

.yzp-file-library__library-card:hover {
  background: #eef4fb;
  border-color: #dbe6f2;
}

.yzp-file-library__library-card--selected {
  border-color: transparent;
  box-shadow: none;
  background: transparent;
}

.yzp-file-library__library-card--selected .yzp-file-library__card-thumb {
  background: #e5e9ee;
  box-shadow: inset 0 0 0 1px rgba(148, 163, 184, 0.22);
}

.yzp-file-library__library-card--selected .yzp-file-library__card-title .yzp-file-library__name {
  max-width: 122px;
  padding: 0 8px;
  border-radius: 14px;
  background: var(--file-library-primary);
  color: #ffffff;
  line-height: 22px;
}

.yzp-file-library__card-thumb {
  width: 100px;
  height: 100px;
  flex: 0 0 100px;
  border-radius: 12px;
  background: #f1f5f9;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.yzp-file-library__image {
  width: 100%;
  height: 100%;
}

.yzp-file-library__folder-card-thumb {
  background: transparent;
}

.yzp-file-library__folder-icon {
  width: 78px;
  height: 70px;
  filter: drop-shadow(0 5px 6px rgba(14, 116, 144, 0.2));
  background-image: url("data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%20100%2088%22%3E%3Cdefs%3E%3ClinearGradient%20id%3D%22a%22%20x1%3D%220%22%20y1%3D%2218%22%20x2%3D%220%22%20y2%3D%2286%22%20gradientUnits%3D%22userSpaceOnUse%22%3E%3Cstop%20offset%3D%220%22%20stop-color%3D%22%2382ddff%22%2F%3E%3Cstop%20offset%3D%220.55%22%20stop-color%3D%22%2343bfee%22%2F%3E%3Cstop%20offset%3D%221%22%20stop-color%3D%22%2325a6df%22%2F%3E%3C%2FlinearGradient%3E%3ClinearGradient%20id%3D%22b%22%20x1%3D%220%22%20y1%3D%2210%22%20x2%3D%220%22%20y2%3D%2240%22%20gradientUnits%3D%22userSpaceOnUse%22%3E%3Cstop%20offset%3D%220%22%20stop-color%3D%22%23a8edff%22%2F%3E%3Cstop%20offset%3D%221%22%20stop-color%3D%22%2356c9f2%22%2F%3E%3C%2FlinearGradient%3E%3C%2Fdefs%3E%3Cpath%20d%3D%22M14%2024c0-5.5%204.5-10%2010-10h22c4.1%200%207.7%202.5%209.2%206.2L56.8%2024H76c5.5%200%2010%204.5%2010%2010v7H14V24z%22%20fill%3D%22url(%23b)%22%2F%3E%3Cpath%20d%3D%22M14%2034h72v39c0%206.1-4.9%2011-11%2011H25c-6.1%200-11-4.9-11-11V34z%22%20fill%3D%22url(%23a)%22%20stroke%3D%22%231296cf%22%20stroke-opacity%3D%22.35%22%2F%3E%3Cpath%20d%3D%22M22%2040h56%22%20stroke%3D%22%23fff%22%20stroke-opacity%3D%22.72%22%20stroke-width%3D%223%22%20stroke-linecap%3D%22round%22%2F%3E%3Cpath%20d%3D%22M17%2035h66v7H17z%22%20fill%3D%22%23fff%22%20opacity%3D%22.14%22%2F%3E%3C%2Fsvg%3E");
  background-position: center;
  background-repeat: no-repeat;
  background-size: contain;
}

.yzp-file-library__folder-icon-tab,
.yzp-file-library__folder-icon-body {
  display: none;
}

.yzp-file-library__folder-icon--list {
  width: 28px;
  height: 25px;
}

.yzp-file-library__card-title {
  min-width: 0;
  width: 100%;
  min-height: 24px;
  padding-top: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  gap: 1px;
  text-align: center;
}

.yzp-file-library__name {
  color: #18212f;
  font-size: 13px;
  line-height: 19px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__card-title .yzp-file-library__name {
  max-width: 136px;
  display: block;
  white-space: nowrap;
  text-align: center;
  word-break: normal;
}

.yzp-file-library__card-path {
  max-width: 100%;
  color: #64748b;
  font-size: 12px;
  line-height: 17px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.yzp-file-library__card-actions {
  position: static;
  min-height: 48px;
  padding: 0;
  border-radius: 0;
  background: transparent;
  box-shadow: none;
  display: flex;
  align-items: center;
  justify-content: center;
  align-content: center;
  gap: 4px 10px;
  margin-top: 0;
  flex-wrap: wrap;
}

.yzp-file-library__danger-text,
.yzp-file-library__remove-link,
.yzp-file-library__move-link {
  color: #c24141;
  font-size: 12px;
  line-height: 18px;
  cursor: pointer;
}

.yzp-file-library__move-link {
  color: #326da8;
}

.yzp-file-library__card-actions .yzp-file-library__icon-button {
  width: 22px;
  height: 22px;
}

.yzp-file-library__protected-lock {
  position: absolute;
  right: 7px;
  top: 7px;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: rgba(51, 65, 85, 0.78);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1;
}

.yzp-file-library__selected-mark {
  position: absolute;
  left: 8px;
  top: 8px;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--file-library-primary);
  display: flex;
  align-items: center;
  justify-content: center;
}

.yzp-file-library__library-empty,
.yzp-file-library__selected-empty {
  min-height: 120px;
  border: 1px dashed #d7dde6;
  border-radius: 8px;
  color: #8d99a6;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  background: #ffffff;
}

.yzp-file-library__selected-panel {
  grid-area: selected;
  min-width: 0;
  padding: 14px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #ffffff;
  overflow: auto;
  box-sizing: border-box;
}

.yzp-file-library__selected-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: #18212f;
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 10px;
}

.yzp-file-library__selected-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.yzp-file-library__selected-row {
  padding: 8px;
  border: 1px solid #e2e8f0;
  border-radius: 7px;
  background: #fbfcfe;
}

.yzp-file-library__selected-row-main {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.yzp-file-library__selected-row-thumb {
  width: 44px;
  height: 44px;
  flex: 0 0 44px;
  border-radius: 5px;
  background: #eef3f8;
}

.yzp-file-library__selected-row-main > view {
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.yzp-file-library__selected-row-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 6px;
  flex-wrap: wrap;
}

.yzp-file-library__move-dialog-mask {
  position: absolute;
  left: 0;
  right: 0;
  top: 0;
  bottom: 0;
  z-index: 2;
  background: rgba(15, 23, 42, 0.28);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 18px;
  box-sizing: border-box;
}

.yzp-file-library__move-dialog {
  width: min(420px, 100%);
  max-height: min(560px, 88vh);
  border-radius: 8px;
  background: #ffffff;
  box-shadow: 0 14px 42px rgba(15, 23, 42, 0.2);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.yzp-file-library__move-dialog-head,
.yzp-file-library__move-dialog-footer {
  padding: 12px 14px;
  border-bottom: 1px solid #e7edf4;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.yzp-file-library__move-dialog-head > view:first-child {
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.yzp-file-library__move-target-list {
  padding: 10px;
  overflow: auto;
}

.yzp-file-library__move-target-item {
  height: 34px;
  padding: 0 8px;
  border-radius: 6px;
  color: #394657;
  font-size: 13px;
  display: flex;
  align-items: center;
  gap: 5px;
  box-sizing: border-box;
  cursor: pointer;
}

.yzp-file-library__move-target-item--active {
  background: #eef4fb;
  color: #1f3f66;
  font-weight: 600;
}

.yzp-file-library__move-target-item--disabled {
  color: #a8b1bd;
  cursor: not-allowed;
  background: #f8fafc;
}

.yzp-file-library__move-target-item--disabled .yzp-file-library__folder-count {
  color: #b7c0cc;
}

.yzp-file-library__move-dialog-footer {
  border-top: 1px solid #e7edf4;
  border-bottom: none;
}

.yzp-file-library__move-dialog-footer > view {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
}

.yzp-file-library__name-dialog-mask {
  position: absolute;
  left: 0;
  right: 0;
  top: 0;
  bottom: 0;
  z-index: 3;
  background: rgba(15, 23, 42, 0.28);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 18px;
  box-sizing: border-box;
}

.yzp-file-library__name-dialog {
  width: min(360px, 100%);
  border-radius: 8px;
  background: #ffffff;
  box-shadow: 0 14px 42px rgba(15, 23, 42, 0.2);
  overflow: hidden;
}

.yzp-file-library__name-dialog-head,
.yzp-file-library__name-dialog-footer {
  padding: 12px 14px;
  border-bottom: 1px solid #e7edf4;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.yzp-file-library__name-dialog-body {
  padding: 14px;
}

.yzp-file-library__name-dialog-location {
  display: block;
  margin-bottom: 10px;
  color: #64748b;
  font-size: 12px;
  line-height: 18px;
  word-break: break-all;
}

.yzp-file-library__name-dialog-footer {
  justify-content: flex-end;
  border-top: 1px solid #e7edf4;
  border-bottom: none;
}

.yzp-file-library__move-target-summary {
  min-width: 0;
  color: #64748b;
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media screen and (max-width: 760px) {
  .yzp-file-library__dialog-mask {
    padding: 10px;
    align-items: stretch;
  }

  .yzp-file-library__dialog {
    max-height: 96vh;
  }

  .yzp-file-library__dialog-toolbar {
    min-height: 52px;
    padding: 8px 10px;
    flex-wrap: nowrap;
    align-items: center;
  }

  .yzp-file-library__dialog-search {
    flex: 1 1 auto;
    flex-basis: auto;
  }

  .yzp-file-library__dialog-toolbar > .yzp-file-library__plain-button {
    flex: 0 0 76px;
    padding: 0 12px;
  }

  .yzp-file-library__dialog-body {
    grid-template-columns: 1fr;
    grid-template-rows: auto auto auto;
    grid-template-areas:
      "search"
      "browser"
      "selected";
    align-content: start;
    gap: 12px;
    min-height: 0;
    padding: 12px;
    overflow: auto;
  }

  .yzp-file-library__browser-head {
    align-items: flex-start;
    flex-wrap: wrap;
  }

  .yzp-file-library__browser-nav {
    flex: 1 1 100%;
  }

  .yzp-file-library__browser-nav .yzp-file-library__browser-count {
    margin-left: auto;
  }

  .yzp-file-library__browser-head-right {
    flex: 1 1 100%;
    flex-wrap: wrap;
    gap: 6px;
    justify-content: flex-end;
  }

  .yzp-file-library__browser-actions {
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  .yzp-file-library__selected-panel {
    max-height: none;
  }

  .yzp-file-library__move-dialog {
    max-height: 86vh;
  }
}
</style>
