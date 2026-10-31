import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root=path.resolve(process.argv[2]);
const application=JSON.parse(fs.readFileSync(path.join(root,'common/application.generated.json'),'utf8'));
const source=fs.readFileSync(path.join(root,'pages/login.vue'),'utf8').match(/<script[^>]*>([\s\S]*?)<\/script>/)[1];
const storage=new Map(),requests=[],events=[],navigation=[],modals=[];
const uni={getStorageSync:key=>storage.get(key)||'',setStorageSync:(key,value)=>storage.set(key,value),$emit:name=>events.push(name),showModal:value=>modals.push(value)};
const yzp={request:options=>requests.push(options),navigateAfterLogin:redirect=>navigation.push(redirect),saveCurrentPermissions:permissions=>storage.set('permissions',permissions)};
const context=vm.createContext({uni,getApp:()=>({globalData:{environment:{base_url:'http://127.0.0.1:5080/api'}}}),decodeURIComponent});
const module=new vm.SourceTextModule(source,{context});
await module.link(specifier=>{
 const value=specifier==='@/common/yzp'?yzp:specifier==='@/common/application.generated.json'?application:undefined;
 assert.notEqual(value,undefined,'原登录脚本新增了未经验证的依赖');
 return new vm.SyntheticModule(['default'],function(){this.setExport('default',value);},{context});
});
await module.evaluate();const definition=module.namespace.default;
/** 执行真实打包脚本和原成功/complete回调，SDK回执由测试控制，不冒称HTTP或浏览器验收。 */
function instance(){const result=definition.data();for(const [name,method]of Object.entries(definition.methods))result[name]=method.bind(result);return result;}
function login(response){
 const page=instance();page.user_name='离线用户名';page.password='离线密码';page.redirectUrl='/pages/main/menu';page.login();
 assert.equal(page.loginSubmitting,true);const request=requests.pop();assert.equal(request.url,'/admin/sys/yz_manager/login');assert.equal(request.method,'POST');
 assert.deepEqual(Object.keys(request.data).sort(),['password','username']);assert.equal(request.data.username,'离线用户名');assert.equal(request.data.password,'离线密码');
 request.success(response);request.complete();assert.equal(page.loginSubmitting,false);return page;
}
const secondRealm='9007199254740993';assert.notEqual(secondRealm,application.adminTypeId);
const page=login({code:200,map:{loginStateId:'离线不透明会话'},data:{id:'9007199254740995',yzAdminTypeId:secondRealm,projectId:'包外项目不能改变固定项目'}});
assert.equal(storage.get('sysParams').yzAdminTypeId,secondRealm);assert.equal(typeof storage.get('sysParams').yzAdminTypeId,'string');assert.equal(storage.get('sysParams').yzProjectId,application.projectId);
assert.equal(storage.get('userInfo').id,'9007199254740995');assert.equal(storage.get('loginStateId'),'离线不透明会话');assert.equal(page.password,'');assert.deepEqual(events,['onLoginLoadMenu','onLoginLoadTop']);assert.equal(navigation.at(-1),'/pages/main/menu');assert.equal(page.enevName,'独立应用');
// 兼容显式开发登录的旧回执，不把缺失后台强转为Number或自行选包外项目。
login({code:200,map:{loginStateId:'开发回执'},data:{id:'1',name:'开发账号'}});assert.equal(storage.get('sysParams').yzAdminTypeId,application.adminTypeId);assert.equal(storage.get('sysParams').yzProjectId,application.projectId);
login({code:200,map:{loginStateId:'非字符串回执'},data:{id:'2',yzAdminTypeId:42}});assert.equal(storage.get('sysParams').yzAdminTypeId,application.adminTypeId);
const stored=JSON.stringify([...storage]);const count=events.length;login({code:500,map:{},data:{yzAdminTypeId:'不可信失败回执'}});assert.equal(JSON.stringify([...storage]),stored);assert.equal(events.length,count);
const empty=instance();empty.login();assert.equal(requests.length,0);assert.equal(modals.at(-1).content,'请输入用户名和密码');
assert.equal(definition.computed.loginFormAction(), 'http://127.0.0.1:5080/api/admin/sys/yz_manager/login');
console.log('原登录真实脚本通过：第二后台长字符串ID、固定项目、开发缺字段回落、原事件/请求/密码清空；未执行HTTP或浏览器。');
