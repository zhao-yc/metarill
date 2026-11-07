/** 本地开发直连 C# API；部署时由同源 /api 反向代理。 */
export function createAppEnvironment() {
 const host = typeof window !== 'undefined' ? window.location.hostname : '';
 const local = !host || ['localhost','127.0.0.1','::1'].includes(host);
 return { name: local ? '开发环境' : '部署环境', base_url: local ? 'http://127.0.0.1:8088/api' : '/api' };
}
