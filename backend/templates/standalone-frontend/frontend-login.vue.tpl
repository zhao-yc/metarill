<template>
	<view style="height: 100%;">
		<view style="display: flex;justify-content: center;align-items: center;height: 100%;">
			<view class="login-cover">
				<view class="login-cover-img"></view>
				<view class="login-cover-bg"></view>
			</view>
			<view class="login-container">


				<view class="top">
					<view class="top-left">{{ projectName }} 后台登录</view>
					<view class="top-right">{{ enevName }}</view>
				</view>
				<!-- 原生表单的稳定标识、POST 和 submit 事件帮助密码管理器配对账号与密码。 -->
				<!-- #ifdef H5 -->
				<component :is="'form'" id="yzp-login-form" name="login" method="post"
					:action="loginFormAction" autocomplete="on" @submit.prevent="login">
				<!-- #endif -->
				<!-- #ifndef H5 -->
				<!-- 小程序与App使用uni-app表单，同一登录方法保持界面和认证协议。 -->
				<form id="yzp-login-form" @submit="login">
				<!-- #endif -->
					<view class="login-register-input">
						<uni-icons type="chatboxes" size="18"></uni-icons>
						<view style="flex: 1;">
							<!-- #ifdef H5 -->
							<component :is="'label'" for="yzp-login-username" class="login-field-label">用户名</component>
							<component :is="'input'" id="yzp-login-username" class="login-native-input" type="text" name="username"
								autocapitalize="none" autocorrect="off" :spellcheck="false"
								autocomplete="username" placeholder="请输入用户名" :value="user_name"
								@input="onUsernameInput" @change="onUsernameInput" @keydown.enter.prevent="submitLoginForm" />
							<!-- #endif -->
							<!-- #ifndef H5 -->
							<label for="yzp-login-username" class="login-field-label">用户名</label>
							<input id="yzp-login-username" class="login-native-input" type="text" name="username"
								placeholder="请输入用户名" :value="user_name" @input="onUsernameInput" @confirm="login" />
							<!-- #endif -->
						</view>
					</view>
					<view class="login-register-input">
						<uni-icons type="locked" size="18"></uni-icons>
						<view style="flex: 1;">
							<!-- #ifdef H5 -->
							<component :is="'label'" for="yzp-login-password" class="login-field-label">密码</component>
							<component :is="'input'" id="yzp-login-password" class="login-native-input" type="password" name="password"
								autocomplete="current-password" placeholder="请输入密码" :value="password"
								@input="onPasswordInput" @change="onPasswordInput" @keydown.enter.prevent="submitLoginForm" />
							<!-- #endif -->
							<!-- #ifndef H5 -->
							<label for="yzp-login-password" class="login-field-label">密码</label>
							<input id="yzp-login-password" class="login-native-input" type="text" name="password" :password="true"
								placeholder="请输入密码" :value="password" @input="onPasswordInput" @confirm="login" />
							<!-- #endif -->
						</view>
						<!--		<uni-icons type="eye-slash" size="18"></uni-icons>  -->
					</view>
					<!-- #ifdef H5 -->
					<component :is="'button'" class="Button" :class="{'is-disabled': loginSubmitting}" type="submit"
						:disabled="loginSubmitting">
						{{ loginButtonText }}
					</component>
					<!-- #endif -->
					<!-- #ifndef H5 -->
					<button class="Button" :class="{'is-disabled': loginSubmitting}" form-type="submit"
						:disabled="loginSubmitting">{{ loginButtonText }}</button>
					<!-- #endif -->
				<!-- #ifdef H5 -->
				</component>
				<!-- #endif -->
				<!-- #ifndef H5 -->
				</form>
				<!-- #endif -->

				<!-- <view style="margin: 0px 20px;display: flex;">
					<view style="flex: 1;font-size: 14px;" @click="retrievePassword">找回密码</view>
					<view style="color: #2979ff;font-size: 14px;" @click="toRegister">快速注册</view>
				</view> -->
			</view>
		</view>
	</view>
</template>

<script>
// 保留原H5 Cap执行器；App／小程序不引入浏览器Worker，启用时明确拒绝。
// #ifdef H5
import Cap from "cap-widget";
// #endif
import yzp from '@/common/yzp'
import application from '@/common/application.generated.json'
export default {
 data(){return{user_name:'',password:'',loginSubmitting:false,redirectUrl:'',enevName:'独立应用',projectName:application.name,capConfigLoaded:false,capEnabled:false,capApiEndpoint:'',capClient:null,capProgress:0,capStatusText:''}},
 computed:{loginFormAction(){return getApp().globalData.environment.base_url+'/admin/sys/yz_manager/login'},loginButtonText(){return this.loginSubmitting&&this.capStatusText?this.capStatusText:this.loginSubmitting?'登录中':'登录'}},
 /** 保留原匿名配置和持久会话复核，旧缓存不能作为真实身份或验证码证明。 */
 onLoad(options){this.loadCapConfig().catch(()=>{});this.redirectUrl=options?.redirect?decodeURIComponent(options.redirect):'';if(uni.getStorageSync('loginStateId'))yzp.request({url:'/admin/sys/yz_manager/getCurrentPermissions',sendSessionId:true,showError:false,success:response=>{if(response.code===200){yzp.saveCurrentPermissions(response.data);yzp.navigateAfterLogin(this.redirectUrl)}}})},
 methods:{
  /** H5输入从target读取，uni-app其他端从detail读取，主动清空保持空字符串。 */
  onUsernameInput(event){this.user_name=event?.detail?.value??event?.target?.value??''}, onPasswordInput(event){this.password=event?.detail?.value??event?.target?.value??''},
  /** 保留原生表单与回车交互，认证密码只提交给明确配置的API。 */
  submitLoginForm(event){if(this.loginSubmitting||event?.isComposing)return;const form=this.$el?.querySelector('#yzp-login-form');if(form?.requestSubmit)form.requestSubmit();else this.login()},
  /** 与原页面相同，补读浏览器未触发input的自动填充值。 */
  syncLoginInputFromDom(){
   // #ifdef H5
   if(!this.$el)return;const name=this.$el.querySelector('input[name="username"]');const password=this.$el.querySelector('input[name="password"]');if(name)this.user_name=name.value;if(password)this.password=password.value;
   // #endif
  },
  /** 下列三个Cap方法逐字复用原页面，保持配置、进度、重置和非Web拒绝规则。 */
		loadCapConfig() {
			return new Promise((resolve, reject) => {
				yzp.request({
					url: '/admin/sys/yz_manager/cap-config',
					notSendSessionId: true,
					method: 'GET',
					success: (res) => {
						if (res.code == 200) {
							const data = res.data || {};
							this.capEnabled = !!data.enabled;
							this.capApiEndpoint = data.apiEndpoint || '';
							this.capConfigLoaded = true;
							resolve(data);
							return;
						}
						this.capConfigLoaded = false;
						reject(new Error(res.msg || '安全验证暂不可用，请稍后再试'));
					},
					fail: (res) => {
						this.capConfigLoaded = false;
						reject(new Error((res && res.errMsg) || '安全验证暂不可用，请稍后再试'));
					}
				});
			});
		},
		/**
		 * 复用已加载配置；失败后允许下一次登录重新拉取。
		 */
		ensureCapConfig() {
			if (this.capConfigLoaded) {
				return Promise.resolve({
					enabled: this.capEnabled,
					apiEndpoint: this.capApiEndpoint
				});
			}
			return this.loadCapConfig();
		},
		/**
		 * 使用 Cap programmatic mode 静默完成安全验证，返回一次性 token。
		 */
		async solveCapToken() {
			const config = await this.ensureCapConfig();
			if (!config.enabled) {
				return '';
			}
			// 非Web尚无Cap执行器；启用验证时明确失败，不能退回无验证登录。
			// #ifndef H5
			throw new Error('安全验证暂不可用，请稍后再试');
			// #endif
			// #ifdef H5
			if (!config.apiEndpoint || typeof window === 'undefined') {
				throw new Error('安全验证暂不可用，请稍后再试');
			}
			if (!this.capClient || this.capApiEndpoint !== config.apiEndpoint) {
				this.capApiEndpoint = config.apiEndpoint;
				this.capClient = new Cap({
					apiEndpoint: config.apiEndpoint
				});
				// 进度事件用于反馈静默验证状态，避免用户误以为登录卡住。
				this.capClient.addEventListener('progress', (event) => {
					const progress = Number(event.detail && event.detail.progress ? event.detail.progress : 0);
					this.capProgress = Math.max(0, Math.min(100, Math.round(progress)));
					this.capStatusText = this.capProgress > 0 ? `安全验证中 ${this.capProgress}%` : '安全验证中';
				});
			}
			if (typeof this.capClient.reset === 'function') {
				this.capClient.reset();
			}
			this.capStatusText = '安全验证中';
			const solution = await this.capClient.solve();
			const token = solution && solution.token ? solution.token : '';
			if (!token) {
				throw new Error('安全验证失败，请重试');
			}
			return token;
			// #endif
		},

  /** 沿用原表单第三字段；身份与项目仍由服务端及包内目录决定。 */
  submitLogin(captchaVerification){return new Promise((resolve,reject)=>yzp.request({url:'/admin/sys/yz_manager/login',method:'POST',header:{'content-type':'application/x-www-form-urlencoded'},notSendSessionId:true,showError:false,data:{username:this.user_name,password:this.password,captchaVerification},success:response=>{if(response.code!==200){reject(new Error(response.msg||'登录失败'));return}this.handleLoginSuccess(response);resolve()},fail:response=>reject(new Error(response?.errMsg||'登录失败'))}))},
  /** 普通应用固定项目，后台只接受服务端字符串值；保留原事件、跳转与密码清空。 */
  handleLoginSuccess(response){uni.setStorageSync('loginStateId',response.map.loginStateId);uni.setStorageSync('userInfo',response.data);uni.setStorageSync('sysParams',{yzProjectId:application.projectId,yzAdminTypeId:typeof response.data?.yzAdminTypeId==='string'&&response.data.yzAdminTypeId.length?response.data.yzAdminTypeId:application.adminTypeId});this.password='';uni.$emit('onLoginLoadMenu');uni.$emit('onLoginLoadTop');yzp.navigateAfterLogin(this.redirectUrl)},
  /** 原失败模态只显示一次，任意验证码错误都不得继续提交密码。 */
  showLoginError(content){uni.showModal({content,showCancel:false})},
  /** 与原页相同，先解Cap再提交登录，所有分支统一恢复按钮与进度。 */
  async login(){if(this.loginSubmitting)return;this.syncLoginInputFromDom();if(!this.user_name||!this.password){this.showLoginError('请输入用户名和密码');return}this.loginSubmitting=true;this.capStatusText='';try{const proof=await this.solveCapToken();this.capStatusText='登录中';await this.submitLogin(proof)}catch(error){this.showLoginError(error?.message||'安全验证失败，请重试')}finally{this.loginSubmitting=false;this.capStatusText='';this.capProgress=0}}
 }
}
</script>
<style>
page {
	height: 100%;
}

.top {
	/* position: absolute;
		top: 15%;
		left: 30px; */
	margin-bottom: 80px;
	margin-left: 30px;
}

.top-left {
	color: #fff;
	font-size: 23px;
	/* font-weight: 600; */
}

.top-right {
	color: #999999;
	padding-top: 10px;
}

.login-register-input {
	display: flex;
	justify-content: center;
	align-items: center;
	margin: 15px 20px;
	background-color: #fff;
	padding: 5px 20px;
	border-radius: 20px;
}

.login-native-input {
	box-sizing: border-box;
	width: 100%;
	height: 35px;
	border: 0;
	outline: 0;
	background: transparent;
	color: #333;
	font-size: 14px;
	line-height: 35px;
	padding-left: 10px;
}

.login-native-input::placeholder {
	color: #999;
	font-size: 12px;
}

/* 标签供浏览器与辅助工具关联字段，保留现有登录页的视觉布局。 */
.login-field-label {
	position: absolute;
	width: 1px;
	height: 1px;
	padding: 0;
	margin: -1px;
	overflow: hidden;
	clip-path: inset(50%);
	white-space: nowrap;
}

.Button {
	display: flex;
	justify-content: center;
	align-items: center;
	box-sizing: border-box;
	width: calc(100% - 40px);
	margin: 10px 20px;
	background: linear-gradient(to right, #2979ff, #2979ff);
	border: 0;
	padding: 13px 20px;
	border-radius: 30px;
	color: #FFFFFF;
	font-size: 18px;
	line-height: normal;
}

.Button::after {
	border: 0;
}

.Button.is-disabled {
	opacity: .75;
}

.login-cover {
	position: fixed;
	top: 0;
	left: 0;
	right: 0;
	bottom: 0;
}

.login-cover .login-cover-img {
	transition: background .2s ease;
	background: linear-gradient(140deg, #0c2434, #087c83);
}

.login-cover .login-cover-bg,
.login-cover .login-cover-img {
	position: fixed;
	top: 0;
	left: 0;
	right: 0;
	bottom: 0;
	background-size: cover;
	background-repeat: no-repeat;
	background-position: center;
}

.login-cover .login-cover-bg {
	background: linear-gradient(to bottom, rgba(0, 0, 0, .45) 0, rgba(0, 0, 0, .9));
}

.login-container {
	max-width: 400px;
	padding: 0 20px;
	position: relative;
	flex: 1;
}
</style>
