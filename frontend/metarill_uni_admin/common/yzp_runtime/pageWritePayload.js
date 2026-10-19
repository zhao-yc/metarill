/** 在模块初始化时建立精确路由索引，普通请求不扫描全部页面定义。 */
export function createPageWriteFilter(schema) {
  if (schema?.schemaVersion !== 1 || !schema.pages || typeof schema.pages !== 'object' || Array.isArray(schema.pages)) throw new Error('页面写入清单格式无效，请重新生成');
  const routes = new Map();
  Object.values(schema.pages).forEach(page => {
    routes.set('POST ' + page.route + '/add', page.createFields);
    routes.set('POST ' + page.route + '/update', page.updateFields);
    // 原详情页删除会提交完整实体；严格删除 DTO 只接收标识。
    routes.set('DELETE ' + page.route, ['id']);
  });
  /** 只裁剪已登记标准写请求；不修改调用者实体，不为缺失字段补值。 */
  return function filterPageWriteData(url, method, data) {
    const key = String(method || 'GET').toUpperCase() + ' ' + String(url || '').split(/[?#]/)[0];
    const fields = routes.get(key);
    if (!fields || data === null || typeof data !== 'object' || Array.isArray(data)) return data;
    return Object.fromEntries(fields.filter(field => Object.prototype.hasOwnProperty.call(data, field)).map(field => [field, data[field]]));
  };
}
