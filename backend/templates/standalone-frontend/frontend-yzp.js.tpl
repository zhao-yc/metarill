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
export default yzp
