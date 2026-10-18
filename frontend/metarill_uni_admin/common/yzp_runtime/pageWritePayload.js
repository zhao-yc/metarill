/** 在模块初始化时建立精确路由索引，普通请求不扫描全部页面定义。 */
export function createPageWriteFilter(schema) {
  if (schema?.schemaVersion !== 1 || !schema.pages || typeof schema.pages !== 'object' || Array.isArray(schema.pages)) throw new Error('页面写入清单格式无效，请重新生成');
  const routes = new Map();
  Object.values(schema.pages).forEach(page => {
    routes.set(page.route + '/add', page.createFields);
    routes.set(page.route + '/update', page.updateFields);
  });
  /** 只裁剪已登记标准写请求；不修改调用者实体，不为缺失字段补值。 */
  return function filterPageWriteData(url, method, data) {
    if (String(method || 'GET').toUpperCase() !== 'POST') return data;
    const fields = routes.get(String(url || '').split(/[?#]/)[0]);
    if (!fields || data === null || typeof data !== 'object' || Array.isArray(data)) return data;
    return Object.fromEntries(fields.filter(field => Object.prototype.hasOwnProperty.call(data, field)).map(field => [field, data[field]]));
  };
}
