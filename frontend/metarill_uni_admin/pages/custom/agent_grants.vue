<template>
	<view class="agent-grants-page">
		<uni-nav-bar left-icon="left" @clickLeft="back" fixed statusBar :border="false" title="Agent 授权管理"></uni-nav-bar>
		<view class="grants-content">
			<view class="grants-heading"><text class="grants-title">已授权的 Agent</text><button size="mini" :disabled="loading" @click="loadGrants">刷新</button></view>
			<text class="grants-description">以下连接使用当前账号的权限。撤销后，该 Agent 的访问令牌和刷新授权立即失效。</text>
			<view v-if="error" class="grants-error" role="alert">{{ error }}</view>
			<view v-if="loading" class="grants-empty">正在加载授权…</view>
			<view v-else-if="!grants.length && !error" class="grants-empty">当前没有已授权的 Agent。</view>
			<view v-for="grant in grants" :key="grant.id" class="grant-card">
				<view class="grant-header"><text class="grant-name">{{ grant.clientName || 'Metarill Agent CLI' }}</text><button size="mini" :disabled="loading || revokingId !== ''" @click="confirmRevoke(grant)">{{ revokingId === grant.id ? '撤销中' : '撤销授权' }}</button></view>
				<text class="grant-time">授权时间：{{ formatTime(grant.createdAt) }}</text>
				<text class="grant-time">最近使用：{{ formatTime(grant.lastUsedAt, '尚未使用') }}</text>
				<text class="grant-time">授权到期：{{ formatTime(grant.expiresAt) }}</text>
			</view>
		</view>
	</view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime';

export default {
	data() { return { grants: [], error: '', loading: false, revokingId: '', requestVersion: 0, disposed: false, requestTask: null }; },
	onShow() { if (this.ensureLogin()) this.loadGrants(); },
	onUnload() {
		// 卸载后迟到响应不能重新显示已撤销的授权。
		this.disposed = true;
		this.requestVersion++;
		if (this.requestTask && this.requestTask.abort) this.requestTask.abort();
	},
	methods: {
		/** 继续使用浏览器既有登录态，授权列表不接收客户端账号参数。 */
		ensureLogin() {
			if (uni.getStorageSync('loginStateId')) return true;
			yzp.goLoginWithRedirect('/pages/custom/agent_grants');
			return false;
		},
		/** 兼容旧服务的毫秒时间戳和 C# 服务的 ISO 日期，空值或非法值保持明确提示。 */
		formatTime(value, empty = '以服务器校验为准') {
			if (value === null || value === undefined || String(value).trim() === '') return empty;
			const numeric = Number(value);
			const date = new Date(Number.isFinite(numeric) ? numeric : value);
			return Number.isNaN(date.getTime()) ? empty : date.toLocaleString();
		},
		/** 每次进入或刷新重新读取当前账号的有效连接。 */
		loadGrants() {
			if (this.loading || this.revokingId || !this.ensureLogin()) return;
			const version = ++this.requestVersion;
			this.loading = true;
			this.error = '';
			this.requestTask = yzp.request({ url: '/admin/sys/agent/grants', method: 'GET', sendSessionId: true, showError: false,
				isCurrent: () => !this.disposed && version === this.requestVersion,
				success: res => {
					if (res.code == 200 && Array.isArray(res.data)) this.grants = res.data;
					else { this.grants = []; this.error = res.msg || '无法读取授权列表'; }
				},
				fail: () => { this.error = '无法读取授权列表，请检查网络后刷新'; },
				complete: () => { if (!this.disposed && version === this.requestVersion) this.loading = false; }
			});
		},
		/** 撤销前让用户核对具体客户端，避免误撤销正在使用的连接。 */
		confirmRevoke(grant) {
			if (this.loading || this.revokingId) return;
			uni.showModal({ title: '撤销 Agent 授权', content: '撤销“' + (grant.clientName || 'Metarill Agent CLI') + '”后，该客户端需要重新登录并取得授权。',
				success: result => { if (result.confirm && !this.disposed) this.revoke(grant); } });
		},
		/** 仅提交授权 ID，服务端检查其归属；写请求不自动重试。 */
		revoke(grant) {
			if (this.loading || this.revokingId || !this.ensureLogin()) return;
			const version = ++this.requestVersion;
			this.revokingId = grant.id;
			this.error = '';
			this.requestTask = yzp.request({ url: '/admin/sys/agent/revoke', method: 'POST', data: { grantId: grant.id },
				sendSessionId: true, showError: false, isCurrent: () => !this.disposed && version === this.requestVersion,
				success: res => {
					if (res.code == 200) { this.grants = this.grants.filter(item => item.id !== grant.id); uni.showToast({ title: '已撤销授权', icon: 'success' }); }
					else this.error = res.msg || '撤销失败，请刷新列表后核对';
				},
				fail: () => { this.error = '撤销结果未确认，请刷新列表后核对'; },
				complete: () => { if (!this.disposed && version === this.requestVersion) this.revokingId = ''; }
			});
		},
		/** 直接打开管理链接时也可以返回项目首页。 */
		back() { yzp.back(); }
	}
};
</script>

<style lang="scss" scoped>
/* 导航组件自带占位；正文单独留白，避免根容器内边距将固定导航栏向下推。 */
.agent-grants-page { min-height: calc(100vh - var(--top-window-height, 0px)); background: #f3f6f4; box-sizing: border-box; }
.grants-content { max-width: 840px; margin: 0 auto; padding: 24px 20px 28px; box-sizing: border-box; }
.grants-heading, .grant-header { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.grants-heading { margin-bottom: 16px; }
.grants-heading button, .grant-header button { margin: 0; flex-shrink: 0; }
.grants-title { font-size: 23px; font-weight: 600; color: #263d30; }
.grants-description { display: block; color: #647269; font-size: 14px; line-height: 24px; margin-bottom: 24px; }
.grants-error { background: #fff2f0; color: #a53e33; padding: 14px 16px; border-radius: 8px; margin-bottom: 16px; font-size: 14px; line-height: 24px; }
.grants-empty { padding: 42px 20px; background: #fff; border: 1px solid #e1e8e3; border-radius: 12px; text-align: center; color: #647269; }
.grant-card { background: #fff; border: 1px solid #e1e8e3; border-radius: 12px; padding: 20px; margin-bottom: 14px; }
.grant-header { margin-bottom: 12px; }
.grant-name { font-weight: 600; font-size: 17px; color: #263d30; overflow-wrap: anywhere; }
.grant-time { display: block; font-size: 13px; color: #647269; line-height: 24px; }
@media (max-width: 480px) { .grants-content { padding: 20px 12px 24px; } .grants-title { font-size: 21px; } .grant-card { padding: 16px; } }
</style>
