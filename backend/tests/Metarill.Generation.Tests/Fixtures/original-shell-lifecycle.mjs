import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = path.resolve(process.argv[2]);
const storage = new Map(), events = new Map(), navigation = [], requests = [], modals = [], errors = [], mixins = [];
let resize, appDefinition, appInstance;
const uni = {
 getStorageSync: key => storage.get(key) || '', setStorageSync: (key,value) => storage.set(key,value), clearStorageSync: () => storage.clear(), removeStorageSync: key => storage.delete(key),
 $on(name,callback) { if(!events.has(name)) events.set(name,[]); events.get(name).push(callback); }, $off() {}, $emit(name,value) { for(const callback of events.get(name) || [])callback(value); },
 getSystemInfo(options) { options.success({windowWidth:1366}); }, getWindowInfo: () => ({windowWidth:1366,windowHeight:900}), onWindowResize: callback => { resize=callback; },
 navigateTo: options => navigation.push(options.url), reLaunch: options => navigation.push(options.url), navigateBack: () => navigation.push('back'),
 getTopWindowStyle: () => ({height:'60px'}), showModal: options => modals.push(options), showActionSheet(options) {options.success({tapIndex:0});},
 showLoading() {}, hideLoading() {}, showToast() {}, hideToast() {},
 request(options) {requests.push(options);return {abort(){options.fail?.({errMsg:'已取消'});}};}
};
const context = vm.createContext({uni,getApp:()=>appInstance,getCurrentPages:()=>[{route:'pages/main/index',options:{}}],console:{log(){},warn(){},error(...values){errors.push(values);}},setTimeout,clearTimeout,URL,Map,Set,Proxy,Promise,Date});
const modules = new Map();
/** 测试按真实H5/Vue3条件执行包内脚本，不加载第三方DOM组件或替代浏览器渲染。 */
function preprocess(text) {
 const stack=[true], flags=new Set(['H5','VUE3']);
 return text.replace(/\r\n?/g,'\n').split('\n').map(line=>{
  const match=line.match(/^\s*(?:\/\/|<!--|\/\*)\s*#(ifdef|ifndef|endif)\s*(.*?)(?:\s*-->|\s*\*\/)?\s*$/);
  if(!match)return stack.at(-1)?line:'';
  if(match[1]==='endif')stack.pop();else {const enabled=match[2].split('||').some(flag=>flags.has(flag.trim()));stack.push(stack.at(-1)&&(match[1]==='ifdef'?enabled:!enabled));} return '';
 }).join('\n');
}
/** 只解析这个临时工程的本地模块，外部Vue仅提供测试所需创建/markRaw接口。 */
function resolve(specifier,parent) {
 const base=specifier.startsWith('@/')?path.join(root,specifier.slice(2)):path.resolve(path.dirname(parent),specifier);
 for(const candidate of [base,base+'.js',base+'.vue',base+'.json'])if(fs.existsSync(candidate)&&fs.statSync(candidate).isFile())return candidate;
 throw new Error('临时原壳缺少模块：'+specifier);
}
function synthetic(id,exports) {return new vm.SyntheticModule(Object.keys(exports),function(){for(const [name,value]of Object.entries(exports))this.setExport(name,value);},{context,identifier:id});}
function load(file) {
 if(modules.has(file))return modules.get(file);
 let module;
 if(file.endsWith('.json'))module=synthetic(file,{default:JSON.parse(fs.readFileSync(file,'utf8'))});
 else {
  let source=preprocess(fs.readFileSync(file,'utf8')); if(file.endsWith('.vue'))source=source.match(/<script[^>]*>([\s\S]*?)<\/script>/)?.[1]||'';
  module=new vm.SourceTextModule(source,{context,identifier:file,importModuleDynamically:async(specifier,referring)=>{const dynamic=load(resolve(specifier,referring.identifier));await evaluate(dynamic);return dynamic;}});
 }
 modules.set(file,module);return module;
}
async function linker(specifier,referring) {
 if(specifier==='vue') {
  if(!modules.has(specifier))modules.set(specifier,synthetic('vue',{markRaw:value=>value,createSSRApp:definition=>{appDefinition=definition;return {config:{globalProperties:{}},mixin:value=>mixins.push(value)};}}));return modules.get(specifier);
 }
 if(specifier==='xlsx'){if(!modules.has(specifier))modules.set(specifier,synthetic('xlsx',{}));return modules.get(specifier);}
 return load(resolve(specifier,referring.identifier));
}
async function evaluate(module) {if(module.status==='unlinked')await module.link(linker);if(module.status==='linked')await module.evaluate();return module;}
async function component(relative) {
 const module=await evaluate(load(path.join(root,relative)));const definition=module.namespace.default;const instance=definition.data?definition.data():{};
 Object.assign(instance,Object.fromEntries(Object.entries(definition.methods||{}).map(([name,callback])=>[name,callback.bind(instance)])));
 return {definition,instance};
}
/** 模拟SDK成功回调，生产请求适配器与加载凭据仍执行包内原代码。 */
function response(request,code=200) {request.success({statusCode:code,data:{code,msg:code===200?'':'测试拒绝'}});request.complete?.({});}

const main=await evaluate(load(path.join(root,'main.js')));const application=main.namespace.createApp();
const globals=application.app.config.globalProperties;
for(const name of ['$yzp','$deviceInfo','$listEmptyObj','$imageStyles','$checkboxRange','$multiCheckboxRange'])assert.ok(name in globals,'原全局变量缺失：'+name);
assert.ok(mixins.length>=1,'原菜单/标题全局mixin未安装');
appInstance={...globals,globalData:appDefinition.globalData};appDefinition.onLaunch.call(appInstance);
assert.equal(appInstance.$deviceInfo.is_pc,true);assert.equal(appInstance.$imageStyles.width,100);
resize({size:{windowWidth:1200,windowHeight:800}});assert.equal(appInstance.globalData.windowInfo.windowWidth,1200);
uni.$emit('navigateToChangePassword');assert.equal(navigation.at(-1),'/pages/main/change_password');

storage.set('loginStateId','本次模拟不透明会话');storage.set('userInfo',{id:'1',name:'开发账户',merchantName:'本次项目'});storage.set('sysParams',{});
const top=await component('pages/main/top.vue');top.definition.mounted.call(top.instance);
assert.equal(top.instance.userInfo.name,'开发账户');assert.equal(top.definition.computed.isYzp.call(top.instance),false);assert.equal(top.instance.enevName,'独立开发环境');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'common/application.generated.json'),'utf8'));assert.equal(top.instance.projectName,manifest.name);
storage.set('userInfo',{id:'1',name:'更新账户'});uni.$emit('onLoginLoadTop');assert.equal(top.instance.userInfo.name,'更新账户');
top.instance.showActions();assert.equal(navigation.at(-1),'/pages/main/change_password');top.instance.toIndex();assert.equal(navigation.at(-1),'/pages/main/index');
const my=await component('pages/main/my.vue');my.definition.onShow.call(my.instance);assert.equal(my.instance.userInfo.name,'更新账户');
const home=await component('pages/main/index.vue');home.definition.onLoad.call(home.instance,{});
for(let i=0;i<20&&!home.instance.asyncComponent;i++)await new Promise(resolve=>setTimeout(resolve,0));assert.ok(home.instance.asyncComponent,'原动态首页槽未完成导入');
home.definition.onShow.call(home.instance);assert.equal(home.instance.homeVisible,true);home.definition.onHide.call(home.instance);assert.equal(home.instance.homeVisible,false);
const menu=await component('components/yzp-menu/yzp-menu.vue');assert.equal(menu.definition.computed.supportsAgentGrants.call(menu.instance),false);
menu.instance.select({directory:true,node:{menu_id:'parent'}});assert.equal(menu.instance.expanded.parent,true);menu.instance.select({directory:true,node:{menu_id:'parent'}});assert.equal(menu.instance.expanded.parent,false);

const change=await component('pages/main/change_password.vue');change.instance.password_new='新密码-仅模拟';change.instance.password_confirm='不相同';change.instance.changePassword();assert.equal(requests.length,0);assert.equal(modals.pop().content,'两次输入的密码不一致');
change.instance.password_old='旧密码-仅模拟';change.instance.password_confirm=change.instance.password_new;change.instance.changePassword();
const passwordRequest=requests.pop();assert.equal(passwordRequest.method,'POST');assert.ok(passwordRequest.url.endsWith('/api/admin/sys/yz_manager/edit_password'));assert.equal(passwordRequest.data.oldPwd,'旧密码-仅模拟');assert.equal(passwordRequest.header.loginStateId,storage.get('loginStateId'));
response(passwordRequest);const changed=modals.pop();assert.equal(changed.content,'修改成功');changed.success();assert.equal(navigation.at(-1),'back');

my.instance.exitLogin();modals.pop().success({confirm:false});assert.equal(requests.length,0);assert.ok(storage.has('loginStateId'));
my.instance.exitLogin();modals.pop().success({confirm:true});const denied=requests.pop();response(denied,500);assert.ok(storage.has('loginStateId'),'撤销失败不得清客户端状态');modals.length=0;
my.instance.exitLogin();modals.pop().success({confirm:true});const logout=requests.pop();assert.equal(logout.method,'POST');assert.ok(logout.url.endsWith('/api/admin/sys/yz_manager/exit_login'));assert.ok(storage.has('loginStateId'),'服务端确认前不得清缓存');response(logout);assert.ok(!storage.has('loginStateId'));assert.ok(!storage.has('userInfo'));assert.equal(navigation.at(-1),'/pages/login');
assert.equal(errors.length,0,'原壳回调存在异常');
// 相同原App生命周期在移动设备清空PC图像样式；没有重写为新的移动首页。
uni.getSystemInfo=options=>options.success({windowWidth:390});const mobile={globalData:appDefinition.globalData,$deviceInfo:{is_pc:true},$imageStyles:{width:100,height:100}};appDefinition.onLaunch.call(mobile);assert.equal(mobile.$deviceInfo.is_pc,false);assert.equal(Object.keys(mobile.$imageStyles).length,0);
console.log('原壳真实脚本：全局变量、PC/移动生命周期、首页动态槽、账户事件、菜单能力门禁、改密与服务器确认退出通过；未执行浏览器渲染。');
