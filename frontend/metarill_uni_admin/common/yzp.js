import CryptoJS from "crypto-js";
import { createRuntimeBatchBridge } from "./runtimeBatchBridge";
import yzp from "./yzp_runtime/yzp-runtime";
import { clearLoginStorageWithWorkspaceSelection } from './workspaceSelection';

// 平台只跨登录保留工作区偏好；公共运行时的默认清理仍删除全部缓存。
yzp.clearLoginStorage = clearLoginStorageWithWorkspaceSelection;

// 平台专用接线位于手写适配层，重复生成不改原列表页面和独立应用运行时。
const defaultRequest = yzp.request;
let runtimeBatchBridge;
/** 原按钮已完成一次确认后进入；其它请求原样转发，不改变公共请求协议。 */
yzp.request = function (options) {
 if (options && options.method === 'POST' && /^\/admin\/custom\/index\/sync_runtime_all_projects(?:\?parentId=[^&#]*)?$/.test(options.url)) {
  if (!runtimeBatchBridge) runtimeBatchBridge = createRuntimeBatchBridge({
   request: value => defaultRequest.call(yzp, value),
   // 显式API适配兼容HBuilderX按需编译，不依赖全局uni对象整体传递。
   uni: { getStorageSync: key => uni.getStorageSync(key), setStorageSync: (key, value) => uni.setStorageSync(key, value),
    showModal: value => uni.showModal(value), showLoading: value => uni.showLoading(value), hideLoading: () => uni.hideLoading() },
   fingerprint: value => CryptoJS.SHA256(value).toString(), environment: () => getApp().globalData.environment.base_url
  });
  return runtimeBatchBridge.start(options);
 }
 return defaultRequest.call(this, options);
};

const defaultCustomButtonHandler = yzp.customButtonHandler;

/** 平台设备 Token 仅在重置响应中返回一次，必须在通用按钮刷新后显示原始值。 */
yzp.customButtonHandler = function (res, that, isAutoCloseAfterAction, type) {
	const result = defaultCustomButtonHandler.call(this, res, that, isAutoCloseAfterAction, type);
	if (res.code === 200 && typeof res.data === "string" && /^yzpmcp_[A-Za-z0-9_-]{32}$/.test(res.data)) {
		// 原始凭据只保留在当前弹窗闭包中，不写入页面实体或浏览器持久存储。
		uni.showModal({
			title: "设备 Token（仅本次显示）",
			content: res.data + "\n\n请保存原始 Token；设备详情页只显示哈希，无法找回原始值。",
			confirmText: "复制 Token",
			cancelText: "关闭",
			success: function (modal) {
				// 用户主动复制后，凭据可用于其选择的 MCP 客户端配置。
				if (modal.confirm) {
					uni.setClipboardData({ data: res.data });
				}
			}
		});
	}
	return result;
};

/** 保留原退出确认；服务器撤销成功后清理当前身份，迟到响应不能清除后来登录的身份。 */
yzp.exitLogin = function () {
 const session = uni.getStorageSync('loginStateId');
 const isCurrent = () => uni.getStorageSync('loginStateId') === session;
 const finish = () => {
  if (!isCurrent()) return;
  yzp.clearLoginStorage();
  uni.$emit('onLogout');
  uni.reLaunch({ url: '/pages/login' });
 };
 uni.showModal({ content: '您确认要退出登录吗？', success: confirmation => {
  if (!confirmation.confirm || !isCurrent()) return;
  if (!session) { finish(); return; }
  yzp.request({ url: '/admin/sys/yz_manager/exit_login', method: 'POST', sendSessionId: true,
   data: {}, showLoading: false, isCurrent,
   success: response => { if (response.code === 200) finish(); }
  });
 } });
};

export default yzp;
