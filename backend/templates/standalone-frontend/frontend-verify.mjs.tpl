import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const files = [];
/** 验证工程只读取自身文件，拒绝任何级别的链接或目录逃逸。 */
function visit(directory) {
 for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
  if (['node_modules', 'unpackage', '.git'].includes(entry.name)) continue;
  const target = path.join(directory, entry.name);
  if (fs.lstatSync(target).isSymbolicLink()) throw new Error('前端验证目录不能经过链接');
  if (entry.isDirectory()) visit(target); else files.push(target);
 }
}
visit(root);
const packageJson = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const externals = new Set([...Object.keys(packageJson.dependencies), 'vue', '@dcloudio/uni-i18n']);
/** 别名和相对导入必须解析到工程内部真实文件，不能遗漏组件内部依赖。 */
function resolve(specifier, parent) {
 if (!specifier.startsWith('.') && !specifier.startsWith('@/')) {
  if (externals.has(specifier) || parent.startsWith(path.join(root, 'scripts')) && specifier.startsWith('node:')) return null;
  throw new Error('未声明外部依赖：' + specifier);
 }
 const base = specifier.startsWith('@/') ? path.join(root, specifier.slice(2)) : path.resolve(path.dirname(parent), specifier);
 if (!base.startsWith(root + path.sep)) throw new Error('模块导入逃逸工程目录');
 for (const candidate of [base, ...['.js', '.mjs', '.json', '.vue', '.scss', '.css'].map(extension => base + extension), path.join(base, 'index.js')]) {
  if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) return candidate;
 }
 throw new Error('缺少本地依赖：' + path.relative(root, parent) + ' -> ' + specifier);
}
/** 检查器只验证H5/Vue3目标，先按uni-app条件指令排除其他平台互斥声明。 */
function preprocess(text) {
 const stack = [true], flags = new Set(['H5', 'VUE3']);
 return text.split('\n').map(line => {
  const directive = line.match(/^\s*(?:\/\/|<!--|\/\*)\s*#(ifdef|ifndef|endif)\s*(.*?)(?:\s*--> |\s*-->$|\s*\*\/$|$)/);
  if (!directive) return stack.at(-1) ? line : '';
  if (directive[1] === 'endif') { if (stack.length < 2) throw new Error('条件编译指令不配对'); stack.pop(); }
  else { const enabled = directive[2].trim().split(/\s*\|\|\s*/).some(flag => flags.has(flag)); stack.push(stack.at(-1) && (directive[1] === 'ifdef' ? enabled : !enabled)); }
  return '';
 }).join('\n');
}
/** Vue模板与脚本分开检查；不执行第三方DOM组件，也不冒充HBuilderX编译器。 */
function blocks(file) {
 const text = preprocess(fs.readFileSync(file, 'utf8'));
 if (!file.endsWith('.vue')) return [{ attributes: '', source: text }];
 return Array.from(text.matchAll(/<script([^>]*)>([\s\S]*?)<\/script>/g), match => ({ attributes: match[1], source: match[2] }));
}
function script(file) {
 return blocks(file).find(block => !/\bmodule\s*=|\blang\s*=\s*['"](?:wxs|renderjs)['"]/.test(block.attributes))?.source || '';
}
const componentNames = new Set(files.filter(file => file.endsWith('.vue')).map(file => path.basename(file, '.vue')));
for (const file of files.filter(file => /\.(?:vue|js|mjs)$/.test(file))) {
 for (const block of blocks(file)) {
  const source = block.source;
  const src = block.attributes.match(/\bsrc\s*=\s*['"]([^'"]+)['"]/); if (src) resolve(src[1], file);
  if (/\blang\s*=\s*['"]wxs['"]/.test(block.attributes)) continue;
  const module = new vm.SourceTextModule(source, { identifier: file });
  for (const dependency of module.dependencySpecifiers) resolve(dependency, file);
  for (const match of source.matchAll(/\b(?:import|require)\s*\(\s*['"]([^'"]+)['"]/g)) resolve(match[1], file);
 }
 if (file.endsWith('.vue')) {
  for (const match of preprocess(fs.readFileSync(file, 'utf8')).matchAll(/<(uni-[a-z0-9-]+|yzp-[a-z0-9-]+|zxz-[a-z0-9-]+)\b/g)) assert.ok(componentNames.has(match[1]), '缺少组件：' + match[1]);
 }
}
const pages = JSON.parse(fs.readFileSync(path.join(root, 'pages.json'), 'utf8'));
const routes = new Set();
for (const item of pages.pages) { assert.ok(!routes.has(item.path), '重复页面路由'); routes.add(item.path); assert.ok(fs.existsSync(path.join(root, item.path + '.vue')), '页面不存在：' + item.path); }
for (const item of pages.tabBar.list) assert.ok(routes.has(item.pagePath), 'tabBar路由不存在');
for (const window of ['leftWindow', 'topWindow']) assert.ok(fs.existsSync(path.join(root, pages[window].path)), '窗口不存在');
// 空AppID由打包初始产物测试保证；工程定制后只检查类型，不阻止开发者配置自己的标识。
assert.equal(typeof JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8')).appid, 'string', 'manifest.appid必须为字符串');
console.log('H5/Vue3前端脚本、路由与组件闭包检查通过；未执行HBuilderX发行。');

if (process.argv.includes('--http-probe')) await httpProbe();
/** 仅在显式配置时，用Node虚拟页面执行真实开发登录、菜单与生成CRUD；凭据不打印。 */
async function httpProbe() {
 const baseUrl = process.env.METARILL_FRONTEND_PROBE_URL;
 const url = new URL(baseUrl);
 assert.ok(url.protocol === 'http:' && url.hostname === '127.0.0.1' && url.pathname === '/api', 'HTTP验收只允许本次随机回环宿主');
 const storage = new Map(), events = new Map(), navigation = [], errors = [];
 let route = 'pages/login', outstanding = 0;
 const uni = {
  getStorageSync: key => storage.get(key) ?? '', setStorageSync: (key, value) => storage.set(key, value), clearStorageSync: () => storage.clear(), removeStorageSync: key => storage.delete(key),
  showLoading() {}, hideLoading() {}, showToast() {}, hideToast() {},
  showModal(options) { if (options.title === '操作失败') errors.push(options.content); options.success?.({ confirm: true, cancel: false }); },
  $on(name, callback) { if (!events.has(name)) events.set(name, []); events.get(name).push(callback); }, $off() {}, $emit(name, value) { for (const callback of events.get(name) || []) callback(value); },
  switchTab(options) { route = options.url.slice(1); navigation.push(options.url); options.success?.({}); }, navigateTo(options) { route = options.url.slice(1); navigation.push(options.url); options.success?.({}); }, reLaunch(options) { route = options.url.slice(1); navigation.push(options.url); options.success?.({}); }, navigateBack() { navigation.push('back'); },
  request(options) {
   const abort = new AbortController(); outstanding++;
   const method = options.method || 'GET'; const headers = { ...options.header };
   let body;
   if (method !== 'GET' && method !== 'HEAD') { body = JSON.stringify(options.data || {}); headers['Content-Type'] = 'application/json'; }
   fetch(options.url, { method, headers, body, signal: abort.signal }).then(async response => ({ data: await response.json(), statusCode: response.status })).then(result => options.success?.(result), error => options.fail?.({ errMsg: error.message })).finally(() => { outstanding--; options.complete?.({}); });
   return { abort: () => abort.abort() };
  }
 };
 const context = vm.createContext({ console, uni, getApp: () => ({ globalData: { environment: { base_url: baseUrl } } }), getCurrentPages: () => [{ route, options: {} }], setTimeout, clearTimeout, URL, Map, Set, Proxy, Promise, Date });
 const modules = new Map();
 function load(file) {
  if (modules.has(file)) return modules.get(file);
  if (file.endsWith('.json')) { const value = JSON.parse(fs.readFileSync(file, 'utf8')); const module = new vm.SyntheticModule(['default'], function () { this.setExport('default', value); }, { context, identifier: file }); modules.set(file, module); return module; }
  const module = new vm.SourceTextModule(script(file), { context, identifier: file }); modules.set(file, module); return module;
 }
 const linker = (specifier, referring) => {
  const file = resolve(specifier, referring.identifier);
  if (file) return load(file);
  // 本验收只运行标准CRUD；Excel/DOM组件依赖已做真实闭包验证，不在Node模拟第三方组件。
  if (!modules.has(specifier)) modules.set(specifier, new vm.SyntheticModule([], function () {}, { context, identifier: specifier }));
  return modules.get(specifier);
 };
 async function component(relative) {
  const module = load(path.join(root, relative)); if (module.status === 'unlinked') await module.link(linker); if (module.status === 'linked') await module.evaluate();
  const definition = module.namespace.default; const instance = definition.data ? definition.data() : {};
  Object.assign(instance, Object.fromEntries(Object.entries(definition.methods || {}).map(([name, callback]) => [name, callback.bind(instance)])));
  return { definition, instance };
 }
 async function until(check) { for (let attempt = 0; attempt < 200; attempt++) { if (check()) return; await new Promise(resolve => setTimeout(resolve, 25)); } throw new Error('独立前端HTTP流程未完成'); }
 const login = await component('pages/login.vue');
 login.instance.user_name = process.env.METARILL_FRONTEND_PROBE_USERNAME; login.instance.password = process.env.METARILL_FRONTEND_PROBE_PASSWORD; login.instance.login();
 await until(() => storage.has('loginStateId') && outstanding === 0); assert.ok(navigation.includes('/pages/main/index')); assert.equal(login.instance.password, '');
 const yzp = modules.get(path.join(root, 'common/yzp.js')).namespace.default;
 const menuModule = load(path.join(root, 'common/yzp_runtime/menuRuntimeBridge.js')); if (menuModule.status === 'unlinked') await menuModule.link(linker); if (menuModule.status === 'linked') await menuModule.evaluate();
 const runtime = menuModule.namespace.getMenuRuntime(); runtime.load(); await until(() => ['ready', 'empty', 'error'].includes(runtime.getState().status) && outstanding === 0); assert.equal(runtime.getState().status, 'ready', runtime.getState().error);
 function leaf(nodes) { for (const node of nodes) { if (node.target) return node; const child = leaf(node.children || []); if (child) return child; } }
 const selected = leaf(runtime.getState().menus); assert.ok(selected?.target?.route); await runtime.navigateTo(selected.menu_id); assert.ok(navigation.at(-1).startsWith(selected.target.route));
 const listPath = selected.target.route.split('?')[0].slice(1); const detailPath = listPath.replace('-list-', '-detail-');
 const detail = await component(detailPath + '.vue'); detail.definition.onLoad.call(detail.instance, {});
 const marker = '前端宿主HTTP验收-' + Date.now(); detail.instance.change('name', marker); detail.instance.change('notes', '新增'); detail.instance.save();
 await until(() => detail.instance.id && !detail.instance.saving && outstanding === 0); const id = detail.instance.id; assert.equal(typeof id, 'string');
 detail.instance.change('notes', null); detail.instance.save(); await until(() => !detail.instance.saving && outstanding === 0); assert.equal(detail.instance.entity.notes, null);
 const list = await component(listPath + '.vue'); list.definition.onLoad.call(list.instance, {}); list.instance.loadPage(); await until(() => !list.instance.loading && outstanding === 0); assert.ok(list.instance.records.some(row => row.id === id && row.name === marker));
 detail.instance.remove(); await until(() => navigation.at(-1) === 'back' && outstanding === 0); list.instance.loadPage(); await until(() => !list.instance.loading && outstanding === 0); assert.ok(!list.instance.records.some(row => row.id === id));
 await new Promise(resolve => yzp.request({ url: '/admin/sys/yz_manager/exit_login', method: 'POST', sendSessionId: true, complete: resolve }));
 list.instance.loadPage(); await until(() => !storage.has('loginStateId') && outstanding === 0); assert.ok(navigation.at(-1).startsWith('/pages/login'));
 runtime.dispose?.(); assert.equal(errors.length, 0, '真实前端流程存在业务错误');
 console.log('真实独立宿主：开发登录、菜单、生成list/detail新增修改删除及注销失效通过；未替代浏览器或真机验收。');
}
