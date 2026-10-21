import yzp from './yzp_runtime/yzp-runtime'
const request = yzp.request.bind(yzp)
/** 独立宿主以HTTP 401/403和R状态表达开发认证错误，沿用公共请求加载与回调收尾。 */
yzp.request = function (options) {
 const success = options.success
 return request({ ...options, success(response) {
  if (response?.code === 401 && options.url !== '/admin/sys/yz_manager/login') { yzp.goLoginWithRedirect(); return }
  if (response?.code !== 200 && options.showError !== false) uni.showModal({ title: '操作失败', content: response?.msg || '请求未完成', showCancel: false })
  if (success) success.call(options, response)
 } })
}
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

export default yzp
