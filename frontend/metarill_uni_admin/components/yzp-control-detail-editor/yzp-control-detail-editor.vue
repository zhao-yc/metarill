<template>
	<view class="control-detail-editor">
		<view v-if="isCascadeTree" class="cascade-editor">
			<view class="editor-header">
				<view>
					<text class="editor-title">级连选择配置</text>
					<text class="editor-desc">通过表单维护数据源、层级和筛选规则，保存时会自动写回控件明细 JSON。</text>
				</view>
				<button v-if="isCrossTableMode" size="mini" type="primary" @click="addLevel">新增层级</button>
			</view>

			<view class="editor-row">
				<text class="editor-label">数据源模式</text>
				<radio-group class="source-mode-radio-group" @change="onSourceModeChange">
					<label
						v-for="item in sourceModeOptions"
						:key="item.value"
						class="source-mode-radio-item"
					>
						<radio
							:value="item.value"
							:checked="cascadeForm.sourceMode === item.value"
							color="#007aff"
						/>
						<text class="source-mode-radio-text">{{ item.text }}</text>
					</label>
				</radio-group>
			</view>

			<view class="editor-row">
				<text class="editor-label">显示分隔符</text>
				<uni-easyinput
					primaryColor="#007aff"
					v-model="cascadeForm.displayJoiner"
					maxlength="10"
					placeholder="默认 /"
				/>
			</view>

			<template v-if="isCrossTableMode">
				<view v-if="!cascadeForm.levels.length" class="editor-empty">
					<text>当前还没有层级，请先新增至少 2 级。</text>
				</view>

				<view v-for="(level, index) in cascadeForm.levels" :key="level.uid" class="level-card">
					<view class="level-card-header">
						<text class="level-card-title">第 {{ index + 1 }} 级</text>
						<view class="level-card-actions">
							<button size="mini" @click="moveLevel(index, -1)" :disabled="index === 0">上移</button>
							<button size="mini" @click="moveLevel(index, 1)" :disabled="index === cascadeForm.levels.length - 1">下移</button>
							<button size="mini" type="warn" @click="removeLevel(index)">删除</button>
						</view>
					</view>

					<view class="level-grid">
						<view class="editor-row">
							<text class="editor-label">显示名称</text>
							<uni-easyinput primaryColor="#007aff" v-model="level.label" placeholder="例如 省" />
						</view>

						<view class="editor-row">
							<text class="editor-label">数据表</text>
							<uni-easyinput primaryColor="#007aff" v-model="level.table" placeholder="例如 province" />
						</view>

						<view class="editor-row">
							<text class="editor-label">ID 字段</text>
							<uni-easyinput primaryColor="#007aff" v-model="level.idField" placeholder="默认 id" />
						</view>

						<view class="editor-row">
							<text class="editor-label">名称字段</text>
							<uni-easyinput primaryColor="#007aff" v-model="level.nameField" placeholder="默认 name" />
						</view>

						<view class="editor-row">
							<text class="editor-label">父级字段</text>
							<uni-easyinput
								primaryColor="#007aff"
								v-model="level.parentField"
								:disabled="index === 0"
								:placeholder="index === 0 ? '首级不用填写' : '例如 province_id'"
							/>
						</view>

						<view class="editor-row">
							<text class="editor-label">排序</text>
							<uni-easyinput primaryColor="#007aff" v-model="level.orderBy" placeholder="例如 sort_no asc,id asc" />
						</view>
					</view>
				</view>
			</template>

			<template v-else>
				<view class="level-card">
					<view class="level-card-header">
						<text class="level-card-title">同表递归配置</text>
					</view>

					<view class="level-grid">
						<view class="editor-row">
							<text class="editor-label">显示名称</text>
							<text class="editor-static-text">默认使用当前字段标题{{ resolvedFieldTitle ? `：${resolvedFieldTitle}` : '' }}</text>
						</view>

						<view class="editor-row">
							<text class="editor-label">数据表</text>
							<uni-easyinput primaryColor="#007aff" v-model="cascadeForm.selfTree.table" placeholder="例如 yz_role" />
						</view>

						<view class="editor-row">
							<text class="editor-label">ID 字段</text>
							<uni-easyinput primaryColor="#007aff" v-model="cascadeForm.selfTree.idField" placeholder="默认 id" />
						</view>

						<view class="editor-row">
							<text class="editor-label">名称字段</text>
							<uni-easyinput primaryColor="#007aff" v-model="cascadeForm.selfTree.nameField" placeholder="默认 name" />
						</view>

						<view class="editor-row">
							<text class="editor-label">父级字段</text>
							<uni-easyinput primaryColor="#007aff" v-model="cascadeForm.selfTree.parentField" placeholder="例如 parent_yz_role_id" />
						</view>

						<view class="editor-row">
							<text class="editor-label">排序</text>
							<uni-easyinput primaryColor="#007aff" v-model="cascadeForm.selfTree.orderBy" placeholder="例如 sort_no asc,id asc" />
						</view>

						<view class="editor-row">
							<text class="editor-label">根节点规则</text>
							<uni-data-select v-model="cascadeForm.selfTree.rootMode" :localdata="rootModeOptions" />
						</view>

						<view class="editor-row">
							<text class="editor-label">筛选命中方式</text>
							<uni-data-select v-model="cascadeForm.selfTree.filterMatchMode" :localdata="filterMatchModeOptions" />
						</view>
					</view>
				</view>
			</template>

			<view class="editor-preview">
				<text class="editor-label">生成的 JSON</text>
				<text selectable class="editor-preview-text">{{ serializedControlDetail }}</text>
			</view>
		</view>

		<view v-else-if="isListControl" class="plain-editor">
			<view class="editor-header">
				<view>
					<text class="editor-title">下拉列表配置</text>
					<text class="editor-desc">使用 JSON 维护目标数据表、显示字段、筛选条件和排序，目标数据表仅显示当前项目。</text>
				</view>
			</view>

			<view class="level-card">
				<view class="level-grid">
					<view class="editor-row">
						<text class="editor-label">目标数据表</text>
						<zxz-uni-data-select
							v-model="listForm.tableId"
							:localdata="configTableOptions"
							filterable
							dataKey="name"
							dataValue="id"
							@change="onListTableChange"
						/>
						<text v-if="listForm.table && !listForm.tableId" class="editor-tip-inline">当前表名：{{ listForm.table }}</text>
					</view>

					<view class="editor-row">
						<text class="editor-label">显示字段</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="listForm.nameField"
							maxlength="50"
							placeholder="默认 name"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">筛选条件</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="listForm.whereSql"
							maxlength="200"
							placeholder="例如 yz_project_id=@current_yz_project_id"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">排序</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="listForm.orderBy"
							maxlength="100"
							placeholder="例如 sort_no asc,id desc"
						/>
					</view>
				</view>

				<text class="editor-tip">保存后会写入标准 JSON，例如 `{"tableId":"xxx","table":"yz_user","nameField":"name","whereSql":"","orderBy":""}`。</text>
			</view>

			<view class="editor-preview">
				<text class="editor-label">生成的 JSON</text>
				<text selectable class="editor-preview-text">{{ serializedControlDetail }}</text>
			</view>
		</view>

		<view v-else-if="isSelectPage" class="plain-editor">
			<view class="editor-header">
				<view>
					<text class="editor-title">{{ isMultiSelectPage ? '多选择页配置' : '选择页配置' }}</text>
					<text class="editor-desc">使用 JSON 维护目标页面、按钮文案、显示字段、筛选条件和排序。</text>
				</view>
			</view>

			<view class="level-card">
				<view class="level-grid">
					<view class="editor-row">
						<text class="editor-label">目标页面</text>
						<zxz-uni-data-select
							v-model="selectPageForm.pageId"
							:localdata="configPageOptions"
							filterable
							dataKey="name"
							dataValue="id"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">按钮文案</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="selectPageForm.buttonText"
							maxlength="50"
							placeholder="例如 选择"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">显示字段</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="selectPageForm.nameField"
							maxlength="100"
							placeholder="例如 title、nick_name，默认 name"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">筛选条件</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="selectPageForm.whereSql"
							maxlength="200"
							placeholder="例如 yz_project_id=@current_yz_project_id"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">排序</text>
						<uni-easyinput
							primaryColor="#007aff"
							v-model="selectPageForm.orderBy"
							maxlength="100"
							placeholder="例如 sort_no asc,id desc"
						/>
					</view>
				</view>

				<text class="editor-tip">保存后会写入标准 JSON，例如 `{"pageId":"xxx","buttonText":"选择","nameField":"name","whereSql":"","orderBy":""}`。</text>
			</view>

			<view class="editor-preview">
				<text class="editor-label">生成的 JSON</text>
				<text selectable class="editor-preview-text">{{ serializedControlDetail }}</text>
			</view>
		</view>

		<view v-else-if="isImageUploadControl" class="plain-editor">
			<view class="editor-header">
				<view>
					<text class="editor-title">{{ isMultiImage ? '多图上传配置' : '单图上传配置' }}</text>
					<text class="editor-desc">配置图片上传数量和是否启用 H5 图片裁切，保存后会写回控件明细 JSON。</text>
				</view>
			</view>

			<view class="level-card">
				<view class="level-grid">
					<view v-if="isMultiImage" class="editor-row">
						<text class="editor-label">最多数量</text>
						<uni-easyinput
							primaryColor="#007aff"
							type="number"
							v-model="multiImageForm.limit"
							placeholder="默认 9"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">图片裁切</text>
						<view class="switch-row">
							<switch :checked="imageUploadCropImagesValue" color="#007aff" @change="onImageUploadCropImagesChange" />
							<text class="editor-static-inline">{{ imageUploadCropImagesValue ? '开启' : '关闭' }}</text>
						</view>
					</view>
				</view>

				<text class="editor-tip">默认不裁切；只有显式开启后，H5 图片上传才会进入裁切流程。</text>
			</view>

			<view class="editor-preview">
				<text class="editor-label">生成的 JSON</text>
				<text selectable class="editor-preview-text">{{ serializedControlDetail }}</text>
			</view>
		</view>

		<view v-else-if="isFileLibrary" class="plain-editor">
			<view class="editor-header">
				<view>
					<text class="editor-title">文件库配置</text>
					<text class="editor-desc">配置字段固定访问级别、上传数量、大小和可选后缀，保存后会写回控件明细 JSON。</text>
				</view>
			</view>

			<view class="level-card">
				<view class="level-grid">
					<view class="editor-row">
						<text class="editor-label">访问级别</text>
						<uni-data-select
							v-model="fileLibraryForm.accessMode"
							:localdata="accessModeOptions"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">允许类型</text>
						<uni-data-checkbox multiple v-model="fileLibraryForm.acceptTypes" :localdata="fileLibraryAcceptOptions" />
					</view>

					<view class="editor-row">
						<text class="editor-label">最多数量</text>
						<uni-easyinput
							primaryColor="#007aff"
							type="number"
							v-model="fileLibraryForm.maxCount"
							placeholder="默认 9"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">单文件大小 MB</text>
						<uni-easyinput
							primaryColor="#007aff"
							type="number"
							v-model="fileLibraryForm.maxSizeMb"
							placeholder="默认 50"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">允许后缀</text>
						<uni-easyinput
							primaryColor="#007aff"
							type="textarea"
							:maxlength="-1"
							v-model="fileLibraryForm.extensionsText"
							placeholder="例如 .pdf,.docx；留空表示不限制"
						/>
					</view>

					<view class="editor-row">
						<text class="editor-label">允许复用文件库</text>
						<view class="switch-row">
							<switch :checked="fileLibraryForm.allowLibrary" color="#007aff" @change="onFileLibraryAllowLibraryChange" />
							<text class="editor-static-inline">{{ fileLibraryForm.allowLibrary ? '允许' : '不允许' }}</text>
						</view>
					</view>

					<view class="editor-row">
						<text class="editor-label">图片裁切</text>
						<view class="switch-row">
							<switch :checked="fileLibraryForm.cropImages" color="#007aff" @change="onFileLibraryCropImagesChange" />
							<text class="editor-static-inline">{{ fileLibraryForm.cropImages ? '开启' : '关闭' }}</text>
						</view>
					</view>
				</view>

				<text class="editor-tip">合同等需要鉴权的文件选择“受保护”，普通图片和公开资料选择“公开”。同一个字段内的文件访问级别固定，不混用。</text>
			</view>

			<view class="editor-preview">
				<text class="editor-label">生成的 JSON</text>
				<text selectable class="editor-preview-text">{{ serializedControlDetail }}</text>
			</view>
		</view>

		<view v-else class="plain-editor">
			<text class="editor-desc plain-desc">当前控件类型暂未接入表单化编辑，先保留原始文本模式。</text>
			<uni-easyinput
				primaryColor="#007aff"
				type="textarea"
				:maxlength="-1"
				v-model="plainControlDetail"
				placeholder="请输入控件明细"
			/>
		</view>
	</view>
</template>

<script>
import yzp from "@/common/yzp_runtime/yzp-runtime";

const CASCADE_TREE = 'CascadeTree'
const LIST = 'List'
const SELECT_PAGE = 'SelectPage'
const MULTI_SELECT_PAGE = 'MultiSelectPage'
const FILE_LIBRARY = 'FileLibrary'
const IMAGE = 'Image'
const MULTI_IMAGE = 'MultiImage'
const SOURCE_MODE_CROSS_TABLE = 'crossTable'
const SOURCE_MODE_SELF_TREE = 'selfTree'

const CONFIG_COLUMN_DETAIL_URL = '/admin/custom/config_column/control_metadata'
const CONFIG_TABLE_DETAIL_URL = '/admin/auto/config_table/6191083005411328/detail/'
const CONFIG_TABLE_OPTIONS_URL = '/admin/custom/config_column/list_control_tables'
const SELECT_PAGE_OPTIONS_URL = '/admin/custom/config_column/select_page_options'

let uidSeed = 0

function nextUid() {
	uidSeed += 1
	return `cascade-level-${Date.now()}-${uidSeed}`
}

function createEmptyLevel() {
	return {
		uid: nextUid(),
		label: '',
		table: '',
		idField: 'id',
		nameField: 'name',
		parentField: '',
		orderBy: 'sort_no asc,id asc'
	}
}

function createEmptySelfTree() {
	return {
		label: '',
		table: '',
		idField: 'id',
		nameField: 'name',
		parentField: '',
		orderBy: 'sort_no asc,id asc',
		rootMode: 'blank',
		filterMatchMode: 'selfAndDescendants'
	}
}

function createDefaultCascadeForm() {
	return {
		sourceMode: SOURCE_MODE_CROSS_TABLE,
		displayJoiner: '/',
		levels: [createEmptyLevel(), createEmptyLevel()],
		selfTree: createEmptySelfTree()
	}
}

function createDefaultSelectPageForm() {
	return {
		pageId: '',
		buttonText: '',
		nameField: 'name',
		whereSql: '',
		orderBy: ''
	}
}

function createDefaultListForm() {
	return {
		tableId: '',
		table: '',
		nameField: 'name',
		whereSql: '',
		orderBy: ''
	}
}

function createDefaultFileLibraryForm() {
	return {
		accessMode: 'PUBLIC',
		acceptTypes: ['image', 'file', 'video'],
		maxCount: 9,
		maxSizeMb: 50,
		extensionsText: '',
		allowLibrary: true,
		cropImages: false
	}
}

function createDefaultImageUploadForm() {
	return {
		cropImages: false
	}
}

function createDefaultMultiImageForm() {
	return {
		limit: 9,
		cropImages: false
	}
}

export default {
	name: 'YzpControlDetailEditor',
	props: {
		entity: {
			type: Object,
			default: () => ({})
		},
		controlKind: {
			type: String,
			default: ''
		},
		columnName: {
			type: String,
			default: ''
		},
		columnType: {
			type: String,
			default: ''
		}
	},
	data() {
		return {
			isSyncing: false,
			plainControlDetail: '',
			cascadeForm: createDefaultCascadeForm(),
			listForm: createDefaultListForm(),
			selectPageForm: createDefaultSelectPageForm(),
			fileLibraryForm: createDefaultFileLibraryForm(),
			imageUploadForm: createDefaultImageUploadForm(),
			multiImageForm: createDefaultMultiImageForm(),
			fetchedColumnMeta: {
				controlKind: '',
				columnName: '',
				columnType: '',
				name: '',
				controlDetail: ''
			},
			lastFetchedConfigColumnId: '',
			configTableOptions: [{
				id: '',
				name: '--',
				value: '',
				text: '--'
			}],
			isLoadingConfigTableOptions: false,
			configPageOptions: [{
				id: '',
				name: '--',
				value: '',
				text: '--'
			}],
			isLoadingConfigPageOptions: false,
			loadedConfigPageOptionsControlKind: '',
			sourceModeOptions: [
				{ value: SOURCE_MODE_CROSS_TABLE, text: '异表多级' },
				{ value: SOURCE_MODE_SELF_TREE, text: '同表递归' }
			],
			rootModeOptions: [
				{ value: 'blank', text: '空值为根节点' }
			],
			filterMatchModeOptions: [
				{ value: 'selfAndDescendants', text: '命中本级和下级' },
				{ value: 'exact', text: '仅精确命中' }
			],
			accessModeOptions: [
				{ value: 'PUBLIC', text: '公开' },
				{ value: 'PROTECTED', text: '受保护' }
			],
			fileLibraryAcceptOptions: [
				{ value: 'image', text: '图片' },
				{ value: 'file', text: '文件' },
				{ value: 'video', text: '视频' }
			]
		}
	},
	computed: {
		resolvedControlKind() {
			if (this.controlKind) {
				return this.controlKind
			}
			if (this.entity && this.entity.controlKind) {
				return this.entity.controlKind
			}
			if (this.fetchedColumnMeta.controlKind) {
				return this.fetchedColumnMeta.controlKind
			}
			return this.inferControlKind(this.entity && this.entity.controlDetail)
		},
		isCascadeTree() {
			return this.resolvedControlKind === CASCADE_TREE
		},
		isListControl() {
			return this.resolvedControlKind === LIST
		},
		isSelectPage() {
			return this.resolvedControlKind === SELECT_PAGE || this.resolvedControlKind === MULTI_SELECT_PAGE
		},
		isMultiSelectPage() {
			return this.resolvedControlKind === MULTI_SELECT_PAGE
		},
		isFileLibrary() {
			return this.resolvedControlKind === FILE_LIBRARY
		},
		isImageUploadControl() {
			return this.resolvedControlKind === IMAGE || this.resolvedControlKind === MULTI_IMAGE
		},
		isMultiImage() {
			return this.resolvedControlKind === MULTI_IMAGE
		},
		imageUploadCropImagesValue() {
			return this.isMultiImage ? this.multiImageForm.cropImages === true : this.imageUploadForm.cropImages === true
		},
		isSelfTreeMode() {
			return this.cascadeForm.sourceMode === SOURCE_MODE_SELF_TREE
		},
		isCrossTableMode() {
			return !this.isSelfTreeMode
		},
		serializedControlDetail() {
			if (!this.isCascadeTree) {
				if (this.isListControl) {
					return this.buildListPayload()
				}
				if (this.isSelectPage) {
					return this.buildSelectPagePayload()
				}
				if (this.isFileLibrary) {
					return this.buildFileLibraryPayload()
				}
				if (this.isImageUploadControl) {
					return this.isMultiImage ? this.buildMultiImagePayload() : this.buildImageUploadPayload()
				}
				return this.plainControlDetail || ''
			}
			return JSON.stringify(this.buildCascadePayload())
		},
		resolvedFieldTitle() {
			if (this.entity && this.entity.name) {
				return this.entity.name
			}
			if (this.fetchedColumnMeta.name) {
				return this.fetchedColumnMeta.name
			}
			return ''
		},
		resolvedConfigColumnId() {
			if (this.entity && this.entity.configColumnId) {
				return this.entity.configColumnId
			}
			return ''
		}
	},
	watch: {
		'entity.controlDetail': {
			immediate: true,
			handler() {
				this.syncFromEntity()
			}
		},
		resolvedControlKind: {
			immediate: true,
			handler() {
				this.ensureConfigTableOptions()
				this.ensureConfigPageOptions()
				this.syncFromEntity()
			}
		},
		resolvedConfigColumnId: {
			immediate: true,
			handler(configColumnId) {
				this.ensureConfigColumnMeta(configColumnId)
			}
		},
		plainControlDetail() {
			if (this.isSyncing || this.isCascadeTree || this.isListControl || this.isSelectPage || this.isFileLibrary || this.isImageUploadControl) {
				return
			}
			this.emitEntity(this.plainControlDetail)
		},
		listForm: {
			deep: true,
			handler() {
				if (this.isSyncing || !this.isListControl) {
					return
				}
				this.emitEntity(this.serializedControlDetail)
			}
		},
		cascadeForm: {
			deep: true,
			handler() {
				if (this.isSyncing || !this.isCascadeTree) {
					return
				}
				this.emitEntity(this.serializedControlDetail)
			}
		},
		selectPageForm: {
			deep: true,
			handler() {
				if (this.isSyncing || !this.isSelectPage) {
					return
				}
				this.emitEntity(this.serializedControlDetail)
			}
		},
		fileLibraryForm: {
			deep: true,
			handler() {
				if (this.isSyncing || !this.isFileLibrary) {
					return
				}
				this.emitEntity(this.serializedControlDetail)
			}
		},
		imageUploadForm: {
			deep: true,
			handler() {
				if (this.isSyncing || !this.isImageUploadControl || this.isMultiImage) {
					return
				}
				this.emitEntity(this.serializedControlDetail)
			}
		},
		multiImageForm: {
			deep: true,
			handler() {
				if (this.isSyncing || !this.isMultiImage) {
					return
				}
				this.emitEntity(this.serializedControlDetail)
			}
		}
	},
	methods: {
		inferControlKind(controlDetail) {
			if (!controlDetail) {
				return ''
			}
			try {
				const parsed = JSON.parse(controlDetail)
				if (parsed && (Array.isArray(parsed.levels) || parsed.sourceMode === SOURCE_MODE_SELF_TREE || parsed.selfTree)) {
					return CASCADE_TREE
				}
				if (parsed && (parsed.tableId || parsed.table)) {
					return LIST
				}
				if (parsed && parsed.pageId) {
					return SELECT_PAGE
				}
				if (parsed && (parsed.accessMode || parsed.maxCount || parsed.maxSizeMb || parsed.allowLibrary !== undefined)) {
					return FILE_LIBRARY
				}
				if (parsed && parsed.limit !== undefined) {
					return MULTI_IMAGE
				}
				if (parsed && parsed.cropImages !== undefined) {
					return IMAGE
				}
			} catch (error) {
				return ''
			}
			return ''
		},
		/** 从真实项目字段读取继承配置，专用接口验证父表，不依赖当前页面的 parentId。 */
		ensureConfigColumnMeta(configColumnId) {
			if (this.controlKind || (this.entity && this.entity.controlKind)) {
				return
			}
			if (!configColumnId) {
				this.lastFetchedConfigColumnId = ''
				this.fetchedColumnMeta = {
					controlKind: '',
					columnName: '',
					columnType: '',
					name: '',
					controlDetail: ''
				}
				return
			}
			if (configColumnId === this.lastFetchedConfigColumnId && this.fetchedColumnMeta.controlKind) {
				return
			}
			const that = this
			yzp.request({
				url: CONFIG_COLUMN_DETAIL_URL,
				method: 'POST',
				data: { id: configColumnId },
				sendSessionId: true,
				success(res) {
					if (res.code !== 200 || !res.data) {
						return
					}
					if (that.resolvedConfigColumnId !== configColumnId) {
						return
					}
					that.lastFetchedConfigColumnId = configColumnId
					that.fetchedColumnMeta = {
						controlKind: res.data.controlKind || '',
						columnName: res.data.columnName || '',
						columnType: res.data.columnType || '',
						name: res.data.name || '',
						controlDetail: res.data.controlDetail || ''
					}
				}
			})
		},
		ensureConfigPageOptions() {
			if (!this.isSelectPage || this.isLoadingConfigPageOptions
				|| this.loadedConfigPageOptionsControlKind === this.resolvedControlKind) {
				return
			}
			const requestedControlKind = this.resolvedControlKind
			this.isLoadingConfigPageOptions = true
			yzp.request({
				// 原选择器交互不变，专用入口按控件类型读取候选，不借用有父范围的标准页面。
				url: SELECT_PAGE_OPTIONS_URL,
				data: { controlKind: requestedControlKind },
				sendSessionId: true,
				method: 'POST',
				success: (res) => {
					if (res.code !== 200 || !Array.isArray(res.data) || this.resolvedControlKind !== requestedControlKind) {
						return
					}
					this.configPageOptions = [{
						id: '',
						name: '--',
						value: '',
						text: '--'
					}].concat(res.data)
					this.loadedConfigPageOptionsControlKind = requestedControlKind
				},
				complete: () => {
					this.isLoadingConfigPageOptions = false
					// 控件类型在请求期间发生变化时，按新类型重新加载，避免复用错误选项。
					if (this.isSelectPage && this.resolvedControlKind !== requestedControlKind) {
						this.ensureConfigPageOptions()
					}
				}
			})
		},
		ensureConfigTableOptions() {
			if (!this.isListControl || this.isLoadingConfigTableOptions || this.configTableOptions.length > 1) {
				return
			}
			this.isLoadingConfigTableOptions = true
			yzp.request({
				url: CONFIG_TABLE_OPTIONS_URL,
				data: {},
				sendSessionId: true,
				method: 'POST',
				success: (res) => {
					if (res.code !== 200 || !Array.isArray(res.data)) {
						return
					}
					this.configTableOptions = [{
						id: '',
						name: '--',
						value: '',
						text: '--'
					}].concat(res.data)
				},
				complete: () => {
					this.isLoadingConfigTableOptions = false
				}
			})
		},
		syncSelectedConfigTableMeta(tableId, shouldEmit = false) {
			if (!tableId) {
				return
			}
			yzp.request({
				url: CONFIG_TABLE_DETAIL_URL + tableId,
				sendSessionId: true,
				success: (res) => {
					if (res.code !== 200 || !res.data) {
						return
					}
					if (!this.listForm || this.listForm.tableId !== tableId) {
						return
					}
					const nextTable = res.data.tableName || ''
					if (this.listForm.table === nextTable) {
						return
					}
					this.isSyncing = true
					this.$set(this.listForm, 'table', nextTable)
					this.$nextTick(() => {
						this.isSyncing = false
						if (shouldEmit && this.isListControl) {
							this.emitEntity(this.buildListPayload())
						}
					})
				}
			})
		},
		onListTableChange(event) {
			const value = event && event.id ? event.id : ''
			if (value) {
				this.syncSelectedConfigTableMeta(value, true)
				return
			}
			if (!this.listForm.table) {
				return
			}
			this.isSyncing = true
			this.$set(this.listForm, 'table', '')
			this.$nextTick(() => {
				this.isSyncing = false
				if (this.isListControl) {
					this.emitEntity(this.buildListPayload())
				}
			})
		},
		syncFromEntity() {
			const controlDetail = (this.entity && this.entity.controlDetail) || ''
			const resolvedListControlDetail = controlDetail || this.fetchedColumnMeta.controlDetail || ''
			const resolvedSelectPageControlDetail = controlDetail || this.fetchedColumnMeta.controlDetail || ''
			const resolvedFileLibraryControlDetail = controlDetail || this.fetchedColumnMeta.controlDetail || ''
			const resolvedImageUploadControlDetail = controlDetail || this.fetchedColumnMeta.controlDetail || ''
			if (!this.isCascadeTree && !this.isListControl && !this.isSelectPage && !this.isFileLibrary && !this.isImageUploadControl) {
				if (controlDetail === this.plainControlDetail) {
					return
				}
				this.isSyncing = true
				this.plainControlDetail = controlDetail
				this.$nextTick(() => {
					this.isSyncing = false
				})
				return
			}
			if (this.isImageUploadControl) {
				if (this.isMultiImage) {
					const nextForm = this.parseMultiImageForm(resolvedImageUploadControlDetail)
					const nextSerialized = this.serializeMultiImageFormForCompare(nextForm)
					const currentSerialized = this.serializeMultiImageFormForCompare(this.multiImageForm)
					if (nextSerialized === currentSerialized) {
						return
					}
					this.isSyncing = true
					this.multiImageForm = nextForm
					this.$nextTick(() => {
						this.isSyncing = false
					})
					return
				}
				const nextForm = this.parseImageUploadForm(resolvedImageUploadControlDetail)
				const nextSerialized = this.serializeImageUploadFormForCompare(nextForm)
				const currentSerialized = this.serializeImageUploadFormForCompare(this.imageUploadForm)
				if (nextSerialized === currentSerialized) {
					return
				}
				this.isSyncing = true
				this.imageUploadForm = nextForm
				this.$nextTick(() => {
					this.isSyncing = false
				})
				return
			}
			if (this.isFileLibrary) {
				const nextForm = this.parseFileLibraryForm(resolvedFileLibraryControlDetail)
				const nextSerialized = this.serializeFileLibraryFormForCompare(nextForm)
				const currentSerialized = this.serializeFileLibraryFormForCompare(this.fileLibraryForm)
				if (nextSerialized === currentSerialized) {
					return
				}
				this.isSyncing = true
				this.fileLibraryForm = nextForm
				this.$nextTick(() => {
					this.isSyncing = false
				})
				return
			}
			if (this.isListControl) {
				const nextForm = this.parseListForm(resolvedListControlDetail)
				const nextSerialized = this.serializeListFormForCompare(nextForm)
				const currentSerialized = this.serializeListFormForCompare(this.listForm)
				if (nextSerialized === currentSerialized) {
					return
				}
				this.isSyncing = true
				this.listForm = nextForm
				this.$nextTick(() => {
					this.isSyncing = false
					this.syncSelectedConfigTableMeta(nextForm.tableId)
				})
				return
			}
			if (this.isSelectPage) {
				const nextForm = this.parseSelectPageForm(resolvedSelectPageControlDetail)
				const nextSerialized = this.serializeSelectPageFormForCompare(nextForm)
				const currentSerialized = this.serializeSelectPageFormForCompare(this.selectPageForm)
				if (nextSerialized === currentSerialized) {
					return
				}
				this.isSyncing = true
				this.selectPageForm = nextForm
				this.$nextTick(() => {
					this.isSyncing = false
				})
				return
			}

			const nextForm = this.parseCascadeForm(controlDetail)
			const nextSerialized = this.serializeCascadeFormForCompare(nextForm)
			const currentSerialized = this.serializeCascadeFormForCompare(this.cascadeForm)
			if (nextSerialized === currentSerialized) {
				return
			}
			this.isSyncing = true
			this.cascadeForm = nextForm
			this.$nextTick(() => {
				this.isSyncing = false
			})
		},
		parseListForm(controlDetail) {
			const nextForm = createDefaultListForm()
			if (!controlDetail) {
				return nextForm
			}
			try {
				const parsed = JSON.parse(controlDetail)
				nextForm.tableId = parsed && parsed.tableId ? parsed.tableId : ''
				nextForm.table = parsed && parsed.table ? parsed.table : ''
				nextForm.nameField = parsed && parsed.nameField ? parsed.nameField : 'name'
				nextForm.whereSql = parsed && parsed.whereSql ? parsed.whereSql : ''
				nextForm.orderBy = parsed && parsed.orderBy ? parsed.orderBy : ''
				return nextForm
			} catch (error) {
				return nextForm
			}
		},
		serializeListFormForCompare(form) {
			if (!form) {
				return ''
			}
			return JSON.stringify({
				tableId: form.tableId || '',
				table: form.table || '',
				nameField: form.nameField || 'name',
				whereSql: form.whereSql || '',
				orderBy: form.orderBy || ''
			})
		},
		parseSelectPageForm(controlDetail) {
			const nextForm = createDefaultSelectPageForm()
			if (!controlDetail) {
				return nextForm
			}
			try {
				const parsed = JSON.parse(controlDetail)
				nextForm.pageId = parsed && parsed.pageId ? parsed.pageId : ''
				nextForm.buttonText = parsed && parsed.buttonText ? parsed.buttonText : ''
				nextForm.nameField = parsed && parsed.nameField ? parsed.nameField : 'name'
				nextForm.whereSql = parsed && parsed.whereSql ? parsed.whereSql : ''
				nextForm.orderBy = parsed && parsed.orderBy ? parsed.orderBy : ''
				return nextForm
			} catch (error) {
				return nextForm
			}
		},
		serializeSelectPageFormForCompare(form) {
			if (!form) {
				return ''
			}
			return JSON.stringify({
				pageId: form.pageId || '',
				buttonText: form.buttonText || '',
				nameField: form.nameField || 'name',
				whereSql: form.whereSql || '',
				orderBy: form.orderBy || ''
			})
		},
		parseCascadeForm(controlDetail) {
			if (!controlDetail) {
				return createDefaultCascadeForm()
			}
			try {
				const parsed = JSON.parse(controlDetail)
				const sourceMode = parsed.sourceMode === SOURCE_MODE_SELF_TREE || (parsed.selfTree && !(Array.isArray(parsed.levels) && parsed.levels.length))
					? SOURCE_MODE_SELF_TREE
					: SOURCE_MODE_CROSS_TABLE
				const levels = Array.isArray(parsed.levels) ? parsed.levels : []
				const nextForm = createDefaultCascadeForm()
				nextForm.sourceMode = sourceMode
				nextForm.displayJoiner = parsed.displayJoiner || '/'
				nextForm.selfTree = {
					label: parsed.selfTree && parsed.selfTree.label ? parsed.selfTree.label : '',
					table: parsed.selfTree && parsed.selfTree.table ? parsed.selfTree.table : '',
					idField: parsed.selfTree && parsed.selfTree.idField ? parsed.selfTree.idField : 'id',
					nameField: parsed.selfTree && parsed.selfTree.nameField ? parsed.selfTree.nameField : 'name',
					parentField: parsed.selfTree && parsed.selfTree.parentField ? parsed.selfTree.parentField : '',
					orderBy: parsed.selfTree && parsed.selfTree.orderBy ? parsed.selfTree.orderBy : 'sort_no asc,id asc',
					rootMode: parsed.selfTree && parsed.selfTree.rootMode ? parsed.selfTree.rootMode : 'blank',
					filterMatchMode: parsed.selfTree && parsed.selfTree.filterMatchMode ? parsed.selfTree.filterMatchMode : 'selfAndDescendants'
				}
				if (levels.length) {
					nextForm.levels = levels.map((level, index) => ({
						uid: this.getLevelUidByIndex(index),
						label: this.getCascadeLevelLabel(parsed, level),
						table: level.table || '',
						idField: level.idField || 'id',
						nameField: level.nameField || 'name',
						parentField: index === 0 ? '' : (level.parentField || ''),
						orderBy: level.orderBy || 'sort_no asc,id asc'
					}))
				}
				return nextForm
			} catch (error) {
				return createDefaultCascadeForm()
			}
		},
		serializeCascadeFormForCompare(form) {
			if (!form) {
				return ''
			}
			return JSON.stringify({
				sourceMode: form.sourceMode || SOURCE_MODE_CROSS_TABLE,
				displayJoiner: form.displayJoiner || '/',
				selfTree: {
					label: form.selfTree && form.selfTree.label ? form.selfTree.label : '',
					table: form.selfTree && form.selfTree.table ? form.selfTree.table : '',
					idField: form.selfTree && form.selfTree.idField ? form.selfTree.idField : 'id',
					nameField: form.selfTree && form.selfTree.nameField ? form.selfTree.nameField : 'name',
					parentField: form.selfTree && form.selfTree.parentField ? form.selfTree.parentField : '',
					orderBy: form.selfTree && form.selfTree.orderBy ? form.selfTree.orderBy : 'sort_no asc,id asc',
					rootMode: form.selfTree && form.selfTree.rootMode ? form.selfTree.rootMode : 'blank',
					filterMatchMode: form.selfTree && form.selfTree.filterMatchMode ? form.selfTree.filterMatchMode : 'selfAndDescendants'
				},
				levels: Array.isArray(form.levels)
					? form.levels.map((level, index) => ({
						label: level && level.label ? level.label : '',
						table: level && level.table ? level.table : '',
						idField: level && level.idField ? level.idField : 'id',
						nameField: level && level.nameField ? level.nameField : 'name',
						parentField: index === 0 ? '' : (level && level.parentField ? level.parentField : ''),
						orderBy: level && level.orderBy ? level.orderBy : 'sort_no asc,id asc'
					}))
					: []
			})
		},
		getLevelUidByIndex(index) {
			const currentLevels = Array.isArray(this.cascadeForm.levels) ? this.cascadeForm.levels : []
			if (currentLevels[index] && currentLevels[index].uid) {
				return currentLevels[index].uid
			}
			return nextUid()
		},
		buildCascadePayload() {
			const payload = {
				sourceMode: this.cascadeForm.sourceMode || SOURCE_MODE_CROSS_TABLE,
				displayJoiner: this.cascadeForm.displayJoiner || '/'
			}
			if (this.isSelfTreeMode) {
				payload.selfTree = {
					label: this.getResolvedSelfTreeLabel(),
					table: this.cascadeForm.selfTree.table || '',
					idField: this.cascadeForm.selfTree.idField || 'id',
					nameField: this.cascadeForm.selfTree.nameField || 'name',
					parentField: this.cascadeForm.selfTree.parentField || '',
					orderBy: this.cascadeForm.selfTree.orderBy || '',
					rootMode: this.cascadeForm.selfTree.rootMode || 'blank',
					filterMatchMode: this.cascadeForm.selfTree.filterMatchMode || 'selfAndDescendants'
				}
				return payload
			}

			payload.labels = {}
			payload.levels = []
			this.cascadeForm.levels.forEach((level, index) => {
				const currentLevel = {
					table: level.table || '',
					idField: level.idField || 'id',
					nameField: level.nameField || 'name',
					parentField: index === 0 ? null : (level.parentField || ''),
					orderBy: level.orderBy || ''
				}
				payload.levels.push(currentLevel)
				const levelKey = this.getCascadeLevelKey(level)
				if (levelKey && level.label) {
					payload.labels[levelKey] = level.label
				}
			})
			return payload
		},
		buildListPayload() {
			return JSON.stringify({
				tableId: this.listForm.tableId || '',
				table: this.listForm.table || '',
				nameField: this.listForm.nameField || 'name',
				whereSql: this.listForm.whereSql || '',
				orderBy: this.listForm.orderBy || ''
			})
		},
		buildSelectPagePayload() {
			return JSON.stringify({
				pageId: this.selectPageForm.pageId || '',
				buttonText: this.selectPageForm.buttonText || '',
				nameField: this.selectPageForm.nameField || 'name',
				whereSql: this.selectPageForm.whereSql || '',
				orderBy: this.selectPageForm.orderBy || ''
			})
		},
		/**
		 * 解析单图上传控件配置，默认不启用裁切。
		 */
		parseImageUploadForm(controlDetail) {
			const nextForm = createDefaultImageUploadForm()
			if (!controlDetail) {
				return nextForm
			}
			try {
				const parsed = JSON.parse(controlDetail)
				nextForm.cropImages = parsed && parsed.cropImages === true
				return nextForm
			} catch (error) {
				return nextForm
			}
		},
		serializeImageUploadFormForCompare(form) {
			if (!form) {
				return ''
			}
			return JSON.stringify({
				cropImages: form.cropImages === true
			})
		},
		buildImageUploadPayload() {
			return JSON.stringify({
				cropImages: this.imageUploadForm.cropImages === true
			})
		},
		/**
		 * 解析多图上传控件配置；旧纯数字不再兼容，回落默认数量 9。
		 */
		parseMultiImageForm(controlDetail) {
			const nextForm = createDefaultMultiImageForm()
			if (!controlDetail) {
				return nextForm
			}
			try {
				const parsed = JSON.parse(controlDetail)
				nextForm.limit = parsed && Number(parsed.limit) > 0 ? Number(parsed.limit) : 9
				nextForm.cropImages = parsed && parsed.cropImages === true
				return nextForm
			} catch (error) {
				return nextForm
			}
		},
		serializeMultiImageFormForCompare(form) {
			if (!form) {
				return ''
			}
			return JSON.stringify({
				limit: Number(form.limit) > 0 ? Number(form.limit) : 9,
				cropImages: form.cropImages === true
			})
		},
		buildMultiImagePayload() {
			return JSON.stringify({
				limit: Number(this.multiImageForm.limit) > 0 ? Number(this.multiImageForm.limit) : 9,
				cropImages: this.multiImageForm.cropImages === true
			})
		},
		/**
		 * 解析 FileLibrary 控件配置，兼容旧的空配置并补齐后端默认值。
		 */
		parseFileLibraryForm(controlDetail) {
			const nextForm = createDefaultFileLibraryForm()
			if (!controlDetail) {
				return nextForm
			}
			try {
				const parsed = JSON.parse(controlDetail)
				const accept = parsed && parsed.accept ? String(parsed.accept) : 'image,file,video'
				nextForm.accessMode = parsed && parsed.accessMode === 'PROTECTED' ? 'PROTECTED' : 'PUBLIC'
				nextForm.acceptTypes = accept.split(',').map(item => item.trim()).filter(item => item)
				nextForm.maxCount = parsed && Number(parsed.maxCount) > 0 ? Number(parsed.maxCount) : 9
				nextForm.maxSizeMb = parsed && Number(parsed.maxSizeMb) > 0 ? Number(parsed.maxSizeMb) : 50
				nextForm.extensionsText = Array.isArray(parsed.extensions) ? parsed.extensions.join(',') : ''
				nextForm.allowLibrary = !(parsed && parsed.allowLibrary === false)
				nextForm.cropImages = parsed && parsed.cropImages === true
				return nextForm
			} catch (error) {
				return nextForm
			}
		},
		serializeFileLibraryFormForCompare(form) {
			if (!form) {
				return ''
			}
			return JSON.stringify({
				accessMode: form.accessMode === 'PROTECTED' ? 'PROTECTED' : 'PUBLIC',
				accept: this.normalizeFileLibraryAcceptTypes(form.acceptTypes).join(','),
				maxCount: Number(form.maxCount) > 0 ? Number(form.maxCount) : 9,
				maxSizeMb: Number(form.maxSizeMb) > 0 ? Number(form.maxSizeMb) : 50,
				extensions: this.parseFileLibraryExtensions(form.extensionsText),
				allowLibrary: form.allowLibrary !== false,
				cropImages: form.cropImages === true
			})
		},
		buildFileLibraryPayload() {
			return JSON.stringify({
				accessMode: this.fileLibraryForm.accessMode === 'PROTECTED' ? 'PROTECTED' : 'PUBLIC',
				accept: this.normalizeFileLibraryAcceptTypes(this.fileLibraryForm.acceptTypes).join(','),
				maxCount: Number(this.fileLibraryForm.maxCount) > 0 ? Number(this.fileLibraryForm.maxCount) : 9,
				maxSizeMb: Number(this.fileLibraryForm.maxSizeMb) > 0 ? Number(this.fileLibraryForm.maxSizeMb) : 50,
				extensions: this.parseFileLibraryExtensions(this.fileLibraryForm.extensionsText),
				allowLibrary: this.fileLibraryForm.allowLibrary !== false,
				cropImages: this.fileLibraryForm.cropImages === true
			})
		},
		normalizeFileLibraryAcceptTypes(acceptTypes) {
			const values = Array.isArray(acceptTypes) && acceptTypes.length ? acceptTypes : ['file']
			return values.filter(item => ['image', 'file', 'video'].indexOf(item) >= 0)
		},
		parseFileLibraryExtensions(text) {
			if (!text) {
				return []
			}
			return String(text).split(/[\s,，、\n]+/)
				.map(item => item.trim().toLowerCase())
				.filter(item => item)
				.map(item => item.indexOf('.') === 0 ? item : `.${item}`)
		},
		onFileLibraryAllowLibraryChange(event) {
			this.fileLibraryForm.allowLibrary = !!(event && event.detail && event.detail.value)
		},
		onFileLibraryCropImagesChange(event) {
			this.fileLibraryForm.cropImages = !!(event && event.detail && event.detail.value)
		},
		onImageUploadCropImagesChange(event) {
			const checked = !!(event && event.detail && event.detail.value)
			if (this.isMultiImage) {
				this.multiImageForm.cropImages = checked
				return
			}
			this.imageUploadForm.cropImages = checked
		},
		getCascadeLevelKey(level) {
			if (!level) {
				return ''
			}
			return level.table || ''
		},
		getResolvedSelfTreeLabel() {
			return this.cascadeForm.selfTree.label || this.resolvedFieldTitle || ''
		},
		getCascadeLevelLabel(parsedDefinition, level) {
			if (!parsedDefinition || !level) {
				return ''
			}
			const levelKey = this.getCascadeLevelKey(level)
			if (!levelKey) {
				return ''
			}
			const labels = parsedDefinition.labels || {}
			return labels[levelKey] || ''
		},
		emitEntity(controlDetail) {
			const entity = this.entity && typeof this.entity === 'object' ? this.entity : {}
			if (entity.controlDetail === controlDetail) {
				return
			}
			this.$set(entity, 'controlDetail', controlDetail)
			this.$emit('receive', entity)
		},
		addLevel() {
			this.cascadeForm.levels.push(createEmptyLevel())
		},
		removeLevel(index) {
			this.cascadeForm.levels.splice(index, 1)
			if (!this.cascadeForm.levels.length) {
				this.cascadeForm.levels.push(createEmptyLevel())
			}
		},
		moveLevel(index, offset) {
			const targetIndex = index + offset
			if (targetIndex < 0 || targetIndex >= this.cascadeForm.levels.length) {
				return
			}
			const levels = this.cascadeForm.levels.slice()
			const temp = levels[index]
			levels[index] = levels[targetIndex]
			levels[targetIndex] = temp
			this.cascadeForm.levels = levels
		},
		onSourceModeChange(event) {
			const value = event && event.detail ? event.detail.value : ''
			this.cascadeForm.sourceMode = value || SOURCE_MODE_CROSS_TABLE
		}
	}
}
</script>

<style scoped>
.control-detail-editor {
	padding: 20rpx;
	border: 1px solid #e5eef8;
	border-radius: 20rpx;
	background: linear-gradient(180deg, #f9fbff 0%, #f3f8ff 100%);
}

.cascade-editor,
.plain-editor {
	display: flex;
	flex-direction: column;
	gap: 20rpx;
}

.editor-header,
.level-card-header {
	display: flex;
	align-items: flex-start;
	justify-content: space-between;
	gap: 20rpx;
}

.editor-title {
	display: block;
	font-size: 30rpx;
	font-weight: 600;
	color: #1f2a44;
}

.editor-desc {
	display: block;
	margin-top: 8rpx;
	font-size: 24rpx;
	line-height: 1.6;
	color: #6b7280;
}

.plain-desc {
	margin-top: 0;
}

.editor-row {
	display: flex;
	flex-direction: column;
	gap: 10rpx;
}

.editor-static-text {
	min-height: 72rpx;
	padding: 20rpx 24rpx;
	border: 1px dashed #c9d7ea;
	border-radius: 16rpx;
	background: rgba(255, 255, 255, 0.78);
	font-size: 26rpx;
	line-height: 1.6;
	color: #475569;
	box-sizing: border-box;
}

.editor-static-inline {
	font-size: 24rpx;
	color: #475569;
}

.switch-row {
	min-height: 72rpx;
	display: flex;
	align-items: center;
	gap: 16rpx;
	padding: 0 6rpx;
}

.editor-label {
	font-size: 24rpx;
	color: #4b5563;
}

.editor-empty {
	padding: 24rpx;
	border-radius: 16rpx;
	background: #ffffff;
	font-size: 24rpx;
	color: #6b7280;
}

.source-mode-radio-group {
	display: flex;
	flex-wrap: wrap;
	gap: 16rpx 24rpx;
	padding: 20rpx 24rpx;
	border: 1px solid #dbe5f1;
	border-radius: 16rpx;
	background: #ffffff;
}

.source-mode-radio-item {
	display: inline-flex;
	align-items: center;
	gap: 6rpx;
	font-size: 0;
}

.source-mode-radio-item radio {
	transform: scale(0.82);
	transform-origin: center;
}

.source-mode-radio-text {
	font-size: 24rpx;
	color: #1f2937;
}

.level-card {
	padding: 20rpx;
	border-radius: 18rpx;
	background: #ffffff;
	box-shadow: 0 8rpx 20rpx rgba(15, 23, 42, 0.05);
}

.level-card-title {
	font-size: 28rpx;
	font-weight: 600;
	color: #1f2937;
}

.level-card-actions {
	display: flex;
	gap: 12rpx;
	flex-wrap: wrap;
	justify-content: flex-end;
}

.level-grid {
	margin-top: 20rpx;
	display: grid;
	grid-template-columns: repeat(2, minmax(0, 1fr));
	gap: 18rpx;
}

.editor-preview {
	padding: 20rpx;
	border-radius: 18rpx;
	background: #111827;
}

.editor-preview .editor-label {
	color: #cbd5e1;
}

.editor-preview-text {
	display: block;
	margin-top: 12rpx;
	font-size: 24rpx;
	line-height: 1.7;
	word-break: break-all;
	color: #f9fafb;
}

.editor-tip {
	display: block;
	margin-top: 20rpx;
	font-size: 22rpx;
	line-height: 1.7;
	color: #64748b;
}

.editor-tip-inline {
	display: block;
	font-size: 22rpx;
	line-height: 1.6;
	color: #64748b;
}

@media screen and (max-width: 768px) {
	.level-grid {
		grid-template-columns: 1fr;
	}

	.editor-header,
	.level-card-header {
		flex-direction: column;
	}

	.level-card-actions {
		justify-content: flex-start;
	}
}
</style>
