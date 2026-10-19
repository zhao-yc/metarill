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
				<component :is="'form'" id="yzp-login-form" name="login" method="post"
					:action="loginFormAction" autocomplete="on" @submit.prevent="login">
					<view class="login-register-input">
						<uni-icons type="chatboxes" size="18"></uni-icons>
						<view style="flex: 1;">
							<component :is="'label'" for="yzp-login-username" class="login-field-label">用户名</component>
							<component :is="'input'" id="yzp-login-username" class="login-native-input" type="text" name="username"
								autocapitalize="none" autocorrect="off" :spellcheck="false"
								autocomplete="username" placeholder="请输入用户名" :value="user_name"
								@input="onUsernameInput" @change="onUsernameInput" @keydown.enter.prevent="submitLoginForm" />
						</view>
					</view>
					<view class="login-register-input">
						<uni-icons type="locked" size="18"></uni-icons>
						<view style="flex: 1;">
							<component :is="'label'" for="yzp-login-password" class="login-field-label">密码</component>
							<component :is="'input'" id="yzp-login-password" class="login-native-input" type="password" name="password"
								autocomplete="current-password" placeholder="请输入密码" :value="password"
								@input="onPasswordInput" @change="onPasswordInput" @keydown.enter.prevent="submitLoginForm" />
						</view>
						<!--		<uni-icons type="eye-slash" size="18"></uni-icons>  -->
					</view>
					<component :is="'button'" class="Button" :class="{'is-disabled': loginSubmitting}" type="submit"
						:disabled="loginSubmitting">
						{{ loginButtonText }}
					</component>
				</component>

				<!-- <view style="margin: 0px 20px;display: flex;">
					<view style="flex: 1;font-size: 14px;" @click="retrievePassword">找回密码</view>
					<view style="color: #2979ff;font-size: 14px;" @click="toRegister">快速注册</view>
				</view> -->
			</view>
		</view>
	</view>
</template>

<script>
import yzp from '@/common/yzp'
import application from '@/common/application.generated.json'
export default {
 data(){return{user_name:'',password:'',loginSubmitting:false,redirectUrl:'',enevName:'独立开发认证',projectName:application.name}},
 computed:{loginFormAction(){return getApp().globalData.environment.base_url+'/admin/sys/yz_manager/login'},loginButtonText(){return this.loginSubmitting?'登录中':'登录'}},
 /** 已保存的令牌仍需服务器重新核验，不能只看本地缓存就进入业务页。 */
 onLoad(options){this.redirectUrl=options?.redirect?decodeURIComponent(options.redirect):'';if(uni.getStorageSync('loginStateId'))yzp.request({url:'/admin/sys/yz_manager/getCurrentPermissions',sendSessionId:true,showError:false,success:response=>{if(response.code===200){yzp.saveCurrentPermissions(response.data);yzp.navigateAfterLogin(this.redirectUrl)}}})},
 methods:{
  onUsernameInput(event){this.user_name=event.target.value}, onPasswordInput(event){this.password=event.target.value},
  /** 保留原生表单与回车交互，开发密码只提交给明确配置的API。 */
  submitLoginForm(event){if(this.loginSubmitting||event?.isComposing)return;const form=this.$el?.querySelector('#yzp-login-form');if(form?.requestSubmit)form.requestSubmit();else this.login()},
  /** 服务器签发不透明会话，项目只取包内公开描述，不提供平台项目选择入口。 */
  login(){if(this.loginSubmitting)return;if(!this.user_name||!this.password){uni.showModal({content:'请输入用户名和密码',showCancel:false});return}this.loginSubmitting=true;yzp.request({url:'/admin/sys/yz_manager/login',method:'POST',data:{username:this.user_name,password:this.password},success:response=>{if(response.code!==200)return;uni.setStorageSync('loginStateId',response.map.loginStateId);uni.setStorageSync('userInfo',response.data);uni.setStorageSync('sysParams',{yzProjectId:application.projectId,yzAdminTypeId:application.adminTypeId});this.password='';uni.$emit('onLoginLoadMenu');uni.$emit('onLoginLoadTop');yzp.navigateAfterLogin(this.redirectUrl)},complete:()=>{this.loginSubmitting=false}})}
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
