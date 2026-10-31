// 工作区仅保存账号对应的设备、项目标识，不保存登录态、权限或凭据。
const CACHE_KEY = 'yzpWorkspaceSelections';

/** 从当前登录身份取缓存归属；未登录时不读取其他账号的偏好。 */
export function workspaceAccountId() {
	const user = uni.getStorageSync('userInfo') || {};
	return user.id == null ? '' : String(user.id);
}

/** 只保留标识字符串，避免将其他会话字段带入长期缓存。 */
function selection(value) {
	const source = value && typeof value === 'object' ? value : {};
	const id = value => typeof value === 'string' || typeof value === 'number' ? String(value) : '';
	return { deviceId: id(source.deviceId), projectId: id(source.projectId) };
}

/** 读取并清理缓存结构，登出后只恢复这份非认证偏好。 */
function preferences() {
	let source;
	try { source = uni.getStorageSync(CACHE_KEY); } catch (_) { return {}; }
	const result = {};
	if (!source || typeof source !== 'object' || Array.isArray(source)) return result;
	Object.keys(source).filter(key => key.startsWith('account:')).forEach(key => {
		result[key] = selection(source[key]);
	});
	return result;
}

/** 按账号恢复上次选项；是否可用仍由页面查询服务端列表确认。 */
export function readWorkspaceSelection(accountId = workspaceAccountId()) {
	const cache = preferences();
	const key = 'account:' + accountId;
	return accountId && Object.prototype.hasOwnProperty.call(cache, key) ? selection(cache[key]) : null;
}

/** 点击或校验选项后保存偏好，浏览器禁用存储时不影响本次选择。 */
export function saveWorkspaceSelection(deviceId, projectId, accountId = workspaceAccountId()) {
	if (!accountId) return;
	const cache = preferences();
	cache['account:' + accountId] = selection({ deviceId, projectId });
	try { uni.setStorageSync(CACHE_KEY, cache); } catch (_) { /* 存储不可用时仍允许进入系统。 */ }
}

/** 清除登录态及权限，迁移旧 sysParams 的选项后仅恢复账号隔离的工作区偏好。 */
export function clearLoginStorageWithWorkspaceSelection() {
	const params = uni.getStorageSync('sysParams') || {};
	const cache = preferences();
	const accountId = workspaceAccountId();
	const key = 'account:' + accountId;
	// 新偏好优先于旧运行上下文，未确认的最新设备选择也不会被登出覆盖。
	if (accountId && params.yzManagerDevicesId && !Object.prototype.hasOwnProperty.call(cache, key)) {
		cache[key] = selection({ deviceId: params.yzManagerDevicesId, projectId: params.yzProjectId || '' });
	}
	uni.clearStorageSync();
	if (Object.keys(cache).length) {
		try { uni.setStorageSync(CACHE_KEY, cache); } catch (_) { /* 恢复偏好失败也不恢复认证信息。 */ }
	}
}
