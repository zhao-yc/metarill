<template>
	<view class="agent-authorize-page">
		<uni-nav-bar fixed statusBar :border="false" title="Agent 授权"></uni-nav-bar>
		<view class="authorize-content">
			<view class="agent-card">
				<text class="agent-title">连接项目与 Agent</text>
				<text class="agent-description">请核对终端中显示的用户码和客户端名称，再决定是否允许 Agent 使用当前账号操作本项目。</text>
				<view v-if="!device && !loading" class="code-input">
					<uni-easyinput v-model="userCode" placeholder="输入终端显示的用户码" :maxlength="24" @confirm="loadDevice" />
					<button type="primary" :disabled="!userCode.trim()" @click="loadDevice">查询授权请求</button>
				</view>
				<view v-if="loading" class="agent-message">正在核对授权请求…</view>
				<view v-if="error" class="agent-message agent-error" role="alert">{{ error }}</view>
				<view v-if="device" class="device-details">
					<view class="detail-row"><text class="detail-label">项目</text><text>{{ device.projectName || '当前项目' }}</text></view>
					<view class="detail-row"><text class="detail-label">客户端</text><text>{{ device.clientName || 'YZP Agent CLI' }}</text></view>
					<view class="detail-row"><text class="detail-label">当前账号</text><text>{{ operatorName }}</text></view>
					<view class="detail-row"><text class="detail-label">用户码</text><text class="user-code">{{ device.userCode || userCode }}</text></view>
					<view class="detail-row"><text class="detail-label">有效期</text><text>{{ expiresText }}</text></view>
					<view class="agent-message" :class="{ 'agent-success': device.status === 'APPROVED' }" role="status">{{ statusText }}</view>
					<view v-if="canApprove" class="permission-note">Agent 只能使用当前账号已有的数据和功能权限。你可以随时在授权管理中撤销连接。</view>
					<view v-if="canApprove" class="agent-actions">
						<button :disabled="submitting" @click="decide(false)">拒绝</button>
						<button type="primary" :disabled="submitting" @click="decide(true)">{{ submitting ? '正在提交' : '确认授权' }}</button>
					</view>
				</view>
				<button class="manage-link" size="mini" @click="openGrants">管理已授权的 Agent</button>
			</view>
		</view>
	</view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime';

export default {
	data() {
		return { userCode: '', userInfo: {}, device: null, error: '', loading: false, submitting: false,
			now: Date.now(), requestVersion: 0, disposed: false, clock: null, requestTask: null };
	},
	computed: {
		/** 账号信息只用于展示，服务端通过既有登录态识别真实操作者。 */
		operatorName() { return this.userInfo.name || this.userInfo.userName || this.userInfo.user_name || '当前登录账号'; },
		canApprove() { return !!this.device && this.device.status === 'PENDING' && Number(this.device.expiresAt) > this.now; },
		expiresText() { return this.device && this.device.expiresAt ? new Date(Number(this.device.expiresAt)).toLocaleString() : '以服务器校验为准'; },
		statusText() {
			if (!this.device) return '';
			if (this.device.status === 'APPROVED') return '已授权，请返回终端继续操作。';
			if (this.device.status === 'DENIED') return '已拒绝此请求，客户端无法取得授权。';
			if (this.device.status === 'EXPIRED' || Number(this.device.expiresAt) <= this.now) return '授权请求已过期，请在终端重新发起登录。';
			return '等待你确认授权。';
		}
	},
	onLoad(options) {
		// 用户码不是访问凭据；大小写和空白在提交前统一规范化。
		this.userCode = this.normalizeCode(options && (options.user_code || options.userCode));
		this.clock = setInterval(() => { this.now = Date.now(); }, 1000);
	},
	onShow() {
		if (!this.ensureLogin()) return;
		this.userInfo = uni.getStorageSync('userInfo') || {};
		if (this.userCode && !this.device && !this.loading) this.loadDevice();
	},
	onUnload() {
		// 离开页面后忽略迟到的授权响应，并释放计时器与网络任务。
		this.disposed = true;
		this.requestVersion++;
		clearInterval(this.clock);
		if (this.requestTask && this.requestTask.abort) this.requestTask.abort();
	},
	methods: {
		/** 登录返回始终保留本页用户码，不把浏览器选择当作授权身份。 */
		ensureLogin() {
			if (uni.getStorageSync('loginStateId')) return true;
			yzp.goLoginWithRedirect('/pages/custom/agent_authorize' + (this.userCode ? '?user_code=' + encodeURIComponent(this.userCode) : ''));
			return false;
		},
		/** 兼容终端复制时夹带的空格，格式有效性由服务端决定。 */
		normalizeCode(value) { return String(value || '').replace(/\s/g, '').toUpperCase(); },
		/** 读取一次待确认请求，界面只展示项目、客户端和有效期。 */
		loadDevice() {
			if (this.loading || this.submitting || !this.ensureLogin()) return;
			this.userCode = this.normalizeCode(this.userCode);
			if (!this.userCode) { this.error = '请输入终端显示的用户码'; return; }
			const version = ++this.requestVersion;
			this.loading = true;
			this.error = '';
			this.requestTask = yzp.request({ url: '/admin/sys/agent/device', method: 'GET', data: { userCode: this.userCode },
				sendSessionId: true, showError: false, isCurrent: () => !this.disposed && version === this.requestVersion,
				success: res => {
					if (res.code == 200 && res.data) { this.device = res.data; this.now = Date.now(); }
					else this.error = res.msg || '授权请求不可用，请在终端重新发起登录';
				},
				fail: () => { this.error = '无法读取授权请求，请检查网络后重新查询'; },
				complete: () => { if (!this.disposed && version === this.requestVersion) this.loading = false; }
			});
		},
		/** 只有明确点击确认或拒绝才提交决定，提交期间禁止重复操作。 */
		decide(approve) {
			if (!this.canApprove || this.submitting || !this.ensureLogin()) return;
			const version = ++this.requestVersion;
			this.submitting = true;
			this.error = '';
			this.requestTask = yzp.request({ url: '/admin/sys/agent/approve', method: 'POST',
				data: { userCode: this.userCode, approve }, sendSessionId: true, showError: false,
				isCurrent: () => !this.disposed && version === this.requestVersion,
				success: res => {
					if (res.code == 200) this.device = { ...this.device, status: approve ? 'APPROVED' : 'DENIED' };
					else this.error = res.msg || '授权决定提交失败，请重新核对请求';
				},
				// 写请求失败不自动重试，避免在结果未知时重复批准。
				fail: () => { this.error = '提交结果未确认，请重新查询授权状态后再操作'; this.device = null; },
				complete: () => { if (!this.disposed && version === this.requestVersion) this.submitting = false; }
			});
		},
		/** 授权管理只列出当前登录账号自己的连接。 */
		openGrants() { uni.navigateTo({ url: '/pages/custom/agent_grants' }); }
	}
};
</script>

<style lang="scss" scoped>
/* 固定导航使用自身占位；卡片间距放在正文容器，独立授权页无需预留后台顶栏。 */
.agent-authorize-page { min-height: 100vh; box-sizing: border-box; background: #f3f6f4; }
.authorize-content { padding: 28px 20px 32px; box-sizing: border-box; }
.agent-card { max-width: 560px; margin: 0 auto; padding: 28px; border: 1px solid #e1e8e3; background: #fff; border-radius: 16px; box-sizing: border-box; }
.agent-title { display: block; font-size: 24px; font-weight: 600; color: #243c2e; margin-bottom: 14px; }
.agent-description, .permission-note { display: block; color: #647269; font-size: 14px; line-height: 24px; }
.code-input { display: flex; gap: 10px; margin-top: 20px; flex-wrap: wrap; }
.code-input > uni-easyinput { flex: 1; min-width: 180px; }
.code-input button { margin: 0; font-size: 14px; }
.device-details { margin-top: 24px; }
.detail-row { display: flex; gap: 16px; padding: 10px 0; color: #263d30; font-size: 15px; line-height: 24px; overflow-wrap: anywhere; }
.detail-label { flex-shrink: 0; width: 72px; color: #768178; }
.user-code { font-weight: 700; letter-spacing: 2px; }
.agent-message { margin-top: 16px; padding: 14px 16px; background: #f1f4f2; border-radius: 8px; color: #58675d; line-height: 24px; font-size: 14px; }
.agent-error { background: #fff2f0; color: #a53e33; }
.agent-success { background: #eaf5ed; color: #286643; }
.permission-note { margin-top: 18px; }
.agent-actions { display: flex; gap: 12px; margin-top: 24px; }
.agent-actions button { flex: 1; margin: 0; }
.manage-link { display: block; margin: 26px auto 0; background: transparent; color: $uni-primary; }
.manage-link::after { border: none; }
@media (max-width: 480px) { .authorize-content { padding: 20px 12px 24px; } .agent-card { padding: 20px; } .agent-title { font-size: 22px; } }
</style>
