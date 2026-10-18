<template>
	<view class="select-page" :class="{ ispc: $deviceInfo.is_pc }">
		<view class="select-shell">
			<view class="hero-card">
				<text class="hero-badge">工作区切换</text>
				<text class="hero-title">选择当前设备和项目</text>
				<text class="hero-desc">登录后的代码生成、菜单和运行上下文都会跟随这里的选择生效。</text>
			</view>

			<view class="content-grid">
				<view class="main-column">
					<view class="selection-card device-card">
						<view class="section-head">
							<view class="section-index">1</view>
							<view class="section-info">
								<text class="section-title">设备</text>
								<text class="section-desc">当前账号下共有 {{ yzManagerDevicesList.length }} 台可用设备</text>
							</view>
						</view>
						<view v-if="yzManagerDevicesList.length" class="project-tags">
							<view
								v-for="item in yzManagerDevicesList"
								:key="item.value"
								class="project-tag"
								:class="{ active: yzManagerDevicesId == item.value }"
								@click="clickDevice(item)"
							>
								<text class="project-tag-text">{{ getOptionLabel(item) }}</text>
								<text class="project-tag-mark" v-if="yzManagerDevicesId == item.value">当前</text>
							</view>
						</view>
						<view v-else class="empty-projects">{{ devicesLoading ? '正在加载设备…' : '当前账号暂无可用设备' }}</view>
						<text class="auto-tip" v-if="yzManagerDevicesList.length === 1">当前账号只有一台设备，已自动选中。</text>
					</view>

					<view class="selection-card">
						<view class="section-head">
							<view class="section-index">2</view>
							<view class="section-info">
								<text class="section-title">项目</text>
								<text class="section-desc">
									{{ yzManagerDevicesId ? "当前设备下共有 " + yzProjectList.length + " 个可选项目" : "请先选择设备，再选择项目" }}
								</text>
							</view>
						</view>
						<view v-if="yzManagerDevicesId && yzProjectList.length" class="project-tags">
							<view
								v-for="item in yzProjectList"
								:key="item.value"
								class="project-tag"
								:class="{ active: yzProjectId == item.value }"
								@click="clickProject(item)"
							>
								<text class="project-tag-text">{{ getOptionLabel(item) }}</text>
								<text class="project-tag-mark" v-if="yzProjectId == item.value">当前</text>
							</view>
						</view>
						<view v-else-if="yzManagerDevicesId" class="empty-projects">
							{{ projectsLoading ? '正在加载项目…' : '当前设备暂无可用项目' }}
						</view>
						<text class="auto-tip" v-if="yzManagerDevicesId && yzProjectList.length">点击项目标签后会直接进入系统。</text>
					</view>
				</view>
			</view>
		</view>
	</view>
</template>

<script>
	import yzp from "@/common/yzp";
	import { workspaceAccountId, readWorkspaceSelection, saveWorkspaceSelection } from '@/common/workspaceSelection';

	export default {
		data() {
			return {
				yzManagerDevicesId: "",
				yzManagerDevicesName: "",
				yzProjectId: "",
				yzProjectName: "",
				yzManagerDevicesList: [],
				yzProjectList: [],
				currentSysParams: {},
				selectionAccountId: '',
				devicesLoading: true,
				projectsLoading: false,
				projectRequestVersion: 0
			}
		},
		onLoad() {
			this._selectionActive = true;
			this.selectionAccountId = workspaceAccountId();
			this.currentSysParams = uni.getStorageSync("sysParams") || {};
			// 兼容升级前已存在的会话选项；登录成功仍会清除会话上下文。
			let saved = readWorkspaceSelection(this.selectionAccountId);
			if (!saved && this.currentSysParams.yzManagerDevicesId) {
				saveWorkspaceSelection(this.currentSysParams.yzManagerDevicesId, this.currentSysParams.yzProjectId || '', this.selectionAccountId);
				saved = { deviceId: String(this.currentSysParams.yzManagerDevicesId), projectId: String(this.currentSysParams.yzProjectId || '') };
			}
			saved = saved || { deviceId: '', projectId: '' };
			this.currentSysParams = { yzManagerDevicesId: saved.deviceId, yzProjectId: saved.projectId };
			this.yzManagerDevicesId = saved.deviceId;
			this.yzProjectId = saved.projectId;
			this.initDeviceList();
		},
		onUnload() {
			// 页面离开后不提交迟到的列表响应，也不改写已保存的选项。
			this._selectionActive = false;
			this.projectRequestVersion++;
		},
		methods: {
			/** 只接受仍属于当前账号的响应与选择。 */
			isSelectionCurrent() {
				return this._selectionActive && this.selectionAccountId === workspaceAccountId();
			},
			/** 保留长标识，按服务端返回列表核对当前选项。 */
			findOption(list, value) {
				return list.find(function(item) {
					return item.value == value;
				});
			},
			/** 兼容名称和标签两种既有选项响应。 */
			getOptionLabel(item) {
				if (!item) {
					return "";
				}
				return item.name || item.text || "";
			},
			/** 设备与项目同样点击选择；切换设备后重新核对其项目列表。 */
			clickDevice(item) {
				if (!this.isSelectionCurrent() || !item) return;
				var selectedDevice = this.findOption(this.yzManagerDevicesList, item.value);
				if (!selectedDevice || this.yzManagerDevicesId == selectedDevice.value) return;
				this.yzManagerDevicesId = selectedDevice.value;
				this.yzManagerDevicesName = this.getOptionLabel(selectedDevice);
				this.yzProjectId = "";
				this.yzProjectName = "";
				this.yzProjectList = [];
				saveWorkspaceSelection(this.yzManagerDevicesId, '', this.selectionAccountId);

				if (!this.yzManagerDevicesId) {
					return;
				}

				this.loadProjectList(this.yzManagerDevicesId, "");
			},
			/** 只允许点击当前设备已返回的项目，确认后才建立运行上下文。 */
			clickProject(item) {
				if (!this.isSelectionCurrent() || !item || !this.yzManagerDevicesId || !this.findOption(this.yzProjectList, item.value)) {
					return;
				}
				this.yzProjectId = item.value || "";
				this.yzProjectName = this.getOptionLabel(item);
				this.confirmSelect();
			},
			/** 偏好与会话上下文分开保存，退出登录不会保留认证数据。 */
			confirmSelect() {
				if (!this.isSelectionCurrent()) return;
				if (!this.yzManagerDevicesId) {
					uni.showModal({
						showCancel: false,
						content: "请选择设备"
					});
					return;
				}
				if (!this.yzProjectId) {
					uni.showModal({
						showCancel: false,
						content: "请选择项目"
					});
					return;
				}

				var oldSysParams = uni.getStorageSync("sysParams") || {};
				var obj = Object.assign({}, oldSysParams, {
					yzManagerDevicesId: this.yzManagerDevicesId,
					yzManagerDevicesName: this.yzManagerDevicesName,
					yzProjectId: this.yzProjectId,
					yzProjectName: this.yzProjectName
				});

				uni.setStorageSync("sysParams", obj);
				saveWorkspaceSelection(this.yzManagerDevicesId, this.yzProjectId, this.selectionAccountId);
				uni.$emit("onChangeProject");
				uni.switchTab({
					url: "/pages/main/index"
				});
			},
			/** 恢复选项必须先在当前账号的设备列表中找到，否则清空或选择唯一设备。 */
			initDeviceList() {
				var that = this;
				yzp.request({
					url: "/admin/sys/index/findNameIdYzManagerDevices",
					sendSessionId: true,
					isCurrent: () => that.isSelectionCurrent(),
					success: function(res) {
						if (!that.isSelectionCurrent()) return;
						that.devicesLoading = false;
						if (res.code == 200) {
							that.yzManagerDevicesList = res.data || [];
							var selectedDevice = that.findOption(that.yzManagerDevicesList, that.yzManagerDevicesId);
							if (!selectedDevice && that.yzManagerDevicesList.length === 1) {
								selectedDevice = that.yzManagerDevicesList[0];
							}
							if (!selectedDevice) {
								that.yzManagerDevicesId = "";
								that.yzManagerDevicesName = "";
								that.yzProjectId = "";
								that.yzProjectName = "";
								that.yzProjectList = [];
								saveWorkspaceSelection('', '', that.selectionAccountId);
								return;
							}

							that.yzManagerDevicesId = selectedDevice.value;
							that.yzManagerDevicesName = that.getOptionLabel(selectedDevice);

							var selectedProjectId = "";
							if (that.currentSysParams.yzManagerDevicesId == that.yzManagerDevicesId) {
								selectedProjectId = that.currentSysParams.yzProjectId || "";
							}

							that.loadProjectList(that.yzManagerDevicesId, selectedProjectId);
						} else {
							uni.showModal({
								showCancel: false,
								content: res.msg
							})
						}
					},
					complete: () => { if (that.isSelectionCurrent()) that.devicesLoading = false; }
				});
			},
			/** 列表按设备与请求序号隔离，快速切换不会用旧项目覆盖新设备。 */
			loadProjectList(yzManagerDevicesId, selectedProjectId) {
				var that = this;
				const requestVersion = ++this.projectRequestVersion;
				this.projectsLoading = true;
				const isCurrent = () => that.isSelectionCurrent() && requestVersion === that.projectRequestVersion && that.yzManagerDevicesId == yzManagerDevicesId;
				yzp.request({
					url: "/admin/sys/index/findNameIdYzProject",
					data: {
						yzManagerDevicesId: yzManagerDevicesId
					},
					sendSessionId: true,
					isCurrent,
					success: function(res) {
						if (!isCurrent()) return;
						that.projectsLoading = false;
						if (res.code == 200) {
							that.yzProjectList = res.data || [];
							var selectedProject = that.findOption(that.yzProjectList, selectedProjectId);
							if (!selectedProject && that.yzProjectList.length === 1) {
								selectedProject = that.yzProjectList[0];
							}
							if (!selectedProject) {
								that.yzProjectId = "";
								that.yzProjectName = "";
								saveWorkspaceSelection(that.yzManagerDevicesId, '', that.selectionAccountId);
								return;
							}

							that.yzProjectId = selectedProject.value;
							that.yzProjectName = that.getOptionLabel(selectedProject);
							saveWorkspaceSelection(that.yzManagerDevicesId, that.yzProjectId, that.selectionAccountId);
						} else {
							uni.showModal({
								showCancel: false,
								content: res.msg
							})
						}
					},
					complete: () => { if (isCurrent()) that.projectsLoading = false; }
				});
			}
		}
	}
</script>

<style>
	.select-page {
		min-height: 100vh;
		padding: 18px 16px 28px;
		background:
			radial-gradient(circle at top left, rgba(37, 99, 235, 0.16), transparent 34%),
			radial-gradient(circle at top right, rgba(14, 165, 233, 0.12), transparent 28%),
			linear-gradient(180deg, #f8fbff 0%, #eef4ff 100%);
		box-sizing: border-box;
	}

	.select-shell {
		width: 100%;
		max-width: 1120px;
		margin: 0 auto;
	}

	.content-grid {
		display: flex;
		flex-direction: column;
	}

	.main-column,
	.side-column {
		display: flex;
		flex-direction: column;
	}

	.hero-card,
	.selection-card {
		background: rgba(255, 255, 255, 0.92);
		border: 1px solid rgba(148, 163, 184, 0.18);
		border-radius: 18px;
		box-shadow: 0 16px 36px rgba(15, 23, 42, 0.06);
		backdrop-filter: blur(10px);
	}

	.hero-card {
		display: flex;
		flex-direction: column;
		padding: 20px 18px;
		margin-bottom: 14px;
	}

	.hero-badge {
		align-self: flex-start;
		padding: 5px 10px;
		border-radius: 999px;
		background: rgba(37, 99, 235, 0.1);
		color: #2563eb;
		font-size: 12px;
		font-weight: 600;
		margin-bottom: 12px;
	}

	.hero-title {
		font-size: 22px;
		font-weight: 700;
		color: #0f172a;
		margin-bottom: 8px;
	}

	.hero-desc {
		font-size: 14px;
		line-height: 1.6;
		color: #475569;
	}

	.selection-card {
		padding: 18px 16px;
		margin-bottom: 14px;
	}

	.section-head {
		display: flex;
		align-items: center;
		margin-bottom: 14px;
	}

	.section-index {
		width: 28px;
		height: 28px;
		border-radius: 50%;
		background: linear-gradient(135deg, #2563eb 0%, #0ea5e9 100%);
		color: #fff;
		font-size: 14px;
		font-weight: 700;
		display: flex;
		align-items: center;
		justify-content: center;
		margin-right: 10px;
	}

	.section-info {
		display: flex;
		flex-direction: column;
	}

	.section-title {
		font-size: 17px;
		font-weight: 600;
		color: #0f172a;
		margin-bottom: 4px;
	}

	.section-desc {
		font-size: 13px;
		color: #64748b;
		line-height: 1.5;
	}

	.auto-tip {
		display: block;
		margin-top: 12px;
		font-size: 13px;
		color: #2563eb;
	}

	.project-tags {
		display: flex;
		flex-wrap: wrap;
		gap: 12px;
	}

	.project-tag {
		display: inline-flex;
		align-items: center;
		gap: 8px;
		padding: 10px 14px;
		cursor: pointer;
		border-radius: 999px;
		border: 1px solid rgba(37, 99, 235, 0.18);
		background: linear-gradient(135deg, rgba(239, 246, 255, 0.96) 0%, rgba(224, 242, 254, 0.92) 100%);
		color: #1d4ed8;
		box-shadow: 0 10px 20px rgba(14, 165, 233, 0.08);
		transition: all 0.2s ease;
	}

	.project-tag.active {
		border-color: rgba(37, 99, 235, 0.4);
		background: linear-gradient(135deg, #2563eb 0%, #0ea5e9 100%);
		color: #ffffff;
		box-shadow: 0 14px 28px rgba(37, 99, 235, 0.22);
	}

	.project-tag-text {
		font-size: 14px;
		font-weight: 600;
		line-height: 1.4;
		word-break: break-all;
	}

	.project-tag-mark {
		padding: 2px 8px;
		border-radius: 999px;
		background: rgba(255, 255, 255, 0.18);
		font-size: 12px;
		line-height: 1.2;
	}

	.empty-projects {
		padding: 14px 16px;
		border-radius: 14px;
		background: rgba(148, 163, 184, 0.1);
		color: #64748b;
		font-size: 14px;
		line-height: 1.6;
	}

	.ispc {
		padding: 28px 28px 36px;
	}

	.ispc .hero-card {
		padding: 28px 28px 24px;
		margin-bottom: 18px;
	}

	.ispc .hero-title {
		font-size: 28px;
	}

	.ispc .content-grid {
		display: block;
	}

	.ispc .main-column {
		width: 100%;
	}

	.ispc .selection-card {
		padding: 20px 18px;
	}

	.ispc .project-tags {
		gap: 14px;
	}
</style>
