import configuration from './application.config'
import application from './application.generated.json'
/** API地址仅来自开发者的宿主配置，不从页面参数或元数据扩展字段选择服务器。 */
export function createAppEnvironment() {
 const base = String(configuration.apiBaseUrl || '').replace(/\/$/, '')
 if (!/^https?:\/\/[^\s]+\/api$/.test(base)) throw new Error('请在common/application.config.js配置可达的HTTP(S) API基地址，以/api结尾。')
 return { name: '独立开发环境', base_url: base, capabilities: application.capabilities }
}
