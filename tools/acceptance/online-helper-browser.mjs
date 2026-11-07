import {createHash} from 'node:crypto';
import {readFile,open,mkdir,lstat} from 'node:fs/promises';
import {resolve,dirname,join,sep,isAbsolute} from 'node:path';
import {fileURLToPath} from 'node:url';

export const ACTIONS=Object.freeze(['generate-all-page','generate-project-config','sync-runtime']);
export const BASELINE='896d10a2430c52ff395ca10bc9bfe06a28c1f904';
const PROJECT='2200000000000100000', HASH=/^[a-f0-9]{64}$/, COMMIT=/^[a-f0-9]{40}$/, UUID=/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
const digest=value=>createHash('sha256').update(typeof value==='string'||Buffer.isBuffer(value)?value:JSON.stringify(value)).digest('hex');
const same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const canonical=path=>process.platform==='win32'?resolve(path).toLowerCase():resolve(path);
const within=(root,path)=>canonical(path)===canonical(root)||canonical(path).startsWith(canonical(root)+sep);
const positive=value=>typeof value==='string'&&/^[1-9][0-9]*$/.test(value)&&BigInt(value)<=9223372036854775807n;
function requireValue(value,message){if(!value)throw new Error(message);}
function keys(value,expected){requireValue(value&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).every(key=>expected.includes(key)),'证据结构含未知字段。');}
/** 所有计划路径必须显式绝对；相邻同名前缀不等同于父目录。 */
function absolute(value){requireValue(typeof value==='string'&&isAbsolute(value),'计划必须使用明确绝对路径。');return resolve(value);}
/** 计划不持有口令或令牌；仅允许本机回环平台API，用户凭据由真实登录交接。 */
function api(value){let url;try{url=new URL(value);}catch{throw new Error('平台API地址无效。');}requireValue(url.protocol==='http:'&&['127.0.0.1','[::1]'].includes(url.hostname)&&url.port&&url.pathname==='/api'&&!url.username&&!url.password&&!url.search&&!url.hash,'验收仅允许明确本机回环API，不接受凭据或附加参数。');return url.href;}
/** 构造只读执行计划；不联网、不登录、不生成账号/元数据，不执行计划内命令。 */
export function buildPlan(spec){
 keys(spec,['runId','apiBaseUrl','expectedAccountId','sourceRoot','sourceCommit','baselineSourceRoot','baselineCommit','dotnet','currentCli','baselineCli','fixtureRoot','rootNamespace']);
 requireValue(typeof spec.runId==='string'&&/^[a-f0-9]{16}$/.test(spec.runId)&&positive(spec.expectedAccountId),'验收编号或已确认真实账号ID无效。');
 requireValue(COMMIT.test(spec.sourceCommit||'')&&spec.baselineCommit===BASELINE,'必须固定当前提交和已验证旧源码提交。');
 requireValue(typeof spec.rootNamespace==='string'&&/^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$/.test(spec.rootNamespace),'命名空间无效。');
 const sourceRoot=absolute(spec.sourceRoot),baselineSourceRoot=absolute(spec.baselineSourceRoot),fixtureRoot=absolute(spec.fixtureRoot),dotnet=absolute(spec.dotnet),currentCli=absolute(spec.currentCli),baselineCli=absolute(spec.baselineCli);
 for(const source of [sourceRoot,baselineSourceRoot])requireValue(!within(source,fixtureRoot)&&!within(fixtureRoot,source),'目标和工作区必须位于可信源码根目录之外。');
 requireValue(!within(sourceRoot,baselineSourceRoot)&&!within(baselineSourceRoot,sourceRoot),'新旧源码必须使用独立普通克隆。');
 const target=join(fixtureRoot,'target'),workRoot=join(fixtureRoot,'work'),profile='online-browser-'+spec.runId,config=join(fixtureRoot,'credentials','config.json');
 const agent=join(sourceRoot,'tools','metarill-agent-cli','bin','metarill-agent.mjs');
 const value={schemaVersion:1,kind:'online-helper-browser-plan',runId:spec.runId,apiBaseUrl:api(spec.apiBaseUrl),expectedAccountId:spec.expectedAccountId,projectId:PROJECT,sourceRoot,sourceCommit:spec.sourceCommit,baselineSourceRoot,baselineCommit:BASELINE,dotnet,currentCli,baselineCli,fixtureRoot,target,workRoot,rootNamespace:spec.rootNamespace,profile,privateAgentConfig:config,
  permissions:{metadataWrites:[],accountWrites:[],roleWrites:[],businessWrites:[],ddl:[],newDatabases:0,webSessions:'仅本轮新建会话',oauthGrants:1,bindings:1,requests:3,groups:{min:1,max:3}},
  commands:{baselinePackage:[dotnet,baselineCli,'package','--metadata',join(baselineSourceRoot,'backend','metadata','demo.json'),'--output',target,'--namespace',spec.rootNamespace,'--source-root',baselineSourceRoot],inspect:[dotnet,currentCli,'update','target-identity','--target',target],authLogin:[process.execPath,agent,'auth','login','--url',api(spec.apiBaseUrl),'--profile',profile,'--name','浏览器源码验收-'+spec.runId],bind:[process.execPath,agent,'helper','bind','--profile',profile,'--target',target,'--source-root',sourceRoot,'--cli',currentCli,'--dotnet',dotnet,'--work-root',workRoot,'--mode','apply','--allow-actions',ACTIONS.join(','),'--confirm'],poll:[process.execPath,agent,'helper','poll','--profile',profile],revoke:[process.execPath,agent,'auth','logout','--profile',profile]},
  protectedPaths:['backend/Application.Host/HostCustomization.cs','backend/Application.Host/appsettings.local.json','backend/generated/Services/DemoCustomerService.cs','frontend/manifest.json'].sort(),
  browser:{entry:'平台原管理前端首页，不是业务Full首页',confirmSeparately:ACTIONS,manualOAuthApproval:true,manualCredentialHandoff:true,noJsonDownload:true,doNotForceSingleGroup:true},
  preconditions:['根冻结与CLI/依赖发布目录摘要一致','实际Demo导出为Full且当前严格target检查通过','当前源码在现场重新执行三scope预览，750历史证明不能代替新提交','无四个封存进程复用或停止','真实浏览器登录/设备批准，不自动填入用户凭据','私有METARILL_AGENT_CONFIG指向本计划文件，命令只作为argv数组执行'],
  cleanup:{server:'仅本grant原auth logout撤权、仅本Web session原exit_login；不硬删账号/授权行',retain:['绑定与任务/组审计','候选/plan/journal/出箱','失败证据及本地受保护文件摘要'],remove:'不自动删目录；根核确切fixtureRoot及所有权后另行决定'},planSha256:''};
 return {...value,planSha256:digest(value)};
}
/** 验证计划完整性，不能通过修改路径或动作扩大原审查范围。 */
export function validatePlan(plan){requireValue(plan?.schemaVersion===1&&plan.kind==='online-helper-browser-plan'&&HASH.test(plan.planSha256||'')&&digest({...plan,planSha256:''})===plan.planSha256,'计划摘要或版本不一致。');return plan;}
/** 输入必须为脱敏事实摘要；动态错误与私有认证配置不能混入可分享证据。 */
function rejectSecrets(value){if(Array.isArray(value)){for(const item of value)rejectSecrets(item);return;}if(value&&typeof value==='object')for(const [key,item]of Object.entries(value)){requireValue(!/^(?:password|salt|token|accessToken|refreshToken|loginStateId|connectionString|deviceCode)$/i.test(key),'证据不能包含原始认证或连接字段。');rejectSecrets(item);}}
/** 只检查真实捕获文件的关联结构；不能把手工JSON或离线反例宣称为现场验收。 */
export function verifyReceiptStructure(plan,evidence){
 validatePlan(plan);rejectSecrets(evidence);keys(evidence,['schemaVersion','planSha256','owner','requests','groups','protectedBefore','protectedAfter','browser']);
 requireValue(evidence.schemaVersion===1&&evidence.planSha256===plan.planSha256,'回执不属于本次计划。');
 keys(evidence.owner,['accountId','projectId','grantId','bindingId']);const owner=evidence.owner;
 requireValue(owner.accountId===plan.expectedAccountId&&owner.projectId===plan.projectId&&positive(owner.grantId)&&UUID.test(owner.bindingId||''),'回执身份/绑定不是本次真实账号项目。');
 requireValue(Array.isArray(evidence.requests)&&evidence.requests.length===3&&Array.isArray(evidence.groups)&&evidence.groups.length>=1&&evidence.groups.length<=3,'必须有3个原按钮UUID和1至3个真实应用组。');
 const ids=new Set(),actions=[],byId=new Map();let metadata;
 for(const item of evidence.requests){keys(item,['requestId','action','metadataSha256','requestSha256','groupId','server']);requireValue(UUID.test(item.requestId||'')&&!ids.has(item.requestId)&&ACTIONS.includes(item.action)&&HASH.test(item.metadataSha256||'')&&HASH.test(item.requestSha256||''),'原按钮身份或摘要无效。');ids.add(item.requestId);actions.push(item.action);byId.set(item.requestId,item);metadata??=item.metadataSha256;requireValue(metadata===item.metadataSha256,'三个按钮必须属于同一实际发布快照。');const server=item.server;keys(server,['requestId','bindingId','projectId','action','requestSha256','status','outcome','applied','application']);requireValue(server.requestId===item.requestId&&server.bindingId===owner.bindingId&&server.projectId===plan.projectId&&server.action===item.action&&server.requestSha256===item.requestSha256&&server.status==='applied'&&server.outcome==='applied'&&server.applied===true,'服务端仍未证明本次UUID的应用终态。');}
 requireValue(same(actions.sort(),ACTIONS),'三个原动作必须各确认一次。');
 const assigned=new Set(),groupIds=new Set();const outcomes=[];
 for(const group of evidence.groups){keys(group,['groupId','requestIds','actions','metadataSha256','planSha256','result','journal','localReceipt','cliProof']);requireValue(UUID.test(group.groupId||'')&&!groupIds.has(group.groupId)&&HASH.test(group.planSha256||'')&&group.metadataSha256===metadata&&Array.isArray(group.requestIds)&&group.requestIds.length>=1&&group.requestIds.length<=3,'组身份或固定快照无效。');groupIds.add(group.groupId);const expected=[];for(const id of group.requestIds){requireValue(byId.has(id)&&!assigned.has(id)&&byId.get(id).groupId===group.groupId,'原请求缺失、重复归组或组关联错误。');assigned.add(id);expected.push(byId.get(id).action);}requireValue(Array.isArray(group.actions)&&same([...group.actions].sort(),expected.sort()),'组动作不能超出原按钮分别确认的集合。');
  const receipt=group.localReceipt;keys(receipt,['bindingId','groupId','ticketSha256','planSha256','status','writtenFiles','deletedFiles','preservedFiles','errorCode']);requireValue(receipt.bindingId===owner.bindingId&&receipt.groupId===group.groupId&&receipt.planSha256===group.planSha256&&receipt.status==='applied'&&HASH.test(receipt.ticketSha256||'')&&receipt.errorCode===null&&[receipt.writtenFiles,receipt.deletedFiles,receipt.preservedFiles].every(n=>Number.isSafeInteger(n)&&n>=0&&n<=20000),'本机回执身份或计数不一致。');
  for(const id of group.requestIds){const app=byId.get(id).server.application;keys(app,['bindingId','groupId','ticketSha256','planSha256','status','writtenFiles','deletedFiles','preservedFiles','errorCode']);requireValue(app&&app.groupId===group.groupId&&app.bindingId===owner.bindingId&&app.planSha256===group.planSha256&&app.status==='applied'&&app.ticketSha256===receipt.ticketSha256&&app.errorCode===null&&app.preservedFiles===receipt.preservedFiles&&app.writtenFiles===receipt.writtenFiles&&app.deletedFiles===receipt.deletedFiles,'服务端终态和本机应用回执不一致。');}
  const proof=group.cliProof;requireValue(proof&&proof.planSha256===group.planSha256,'缺少本次固定CLI独立核验。');
  if(group.result==='committed'){requireValue(proof.status==='committed'&&/^[a-f0-9]{32}$/.test(proof.transactionId||'')&&group.journal&&group.journal.transactionId===proof.transactionId&&group.journal.planSha256===group.planSha256&&group.journal.status==='committed','不能用状态自称或未知journal冒充真实提交。');}
  else{requireValue(group.result==='unchanged'&&group.journal===null&&receipt.writtenFiles===0&&receipt.deletedFiles===0&&proof.status==='unchanged-revalidated'&&proof.originalPlanSha256===group.planSha256&&proof.previewPlanSha256===group.planSha256&&proof.mutatingFiles===0,'NoOp必须有原CLI真实零变更与同计划再预览，不可用unknown猜成功。');}outcomes.push({groupId:group.groupId,result:group.result,actions:group.actions});
 }
 requireValue(assigned.size===3,'有原按钮未进入实际应用组。');
 requireValue(evidence.protectedBefore&&same(Object.keys(evidence.protectedBefore).sort(),plan.protectedPaths)&&same(Object.keys(evidence.protectedBefore).sort(),Object.keys(evidence.protectedAfter||{}).sort()),'缺少Host/partial/AppID/私有配置的完整保护摘要。');
 for(const [path,sha]of Object.entries(evidence.protectedBefore)){requireValue(HASH.test(sha)&&evidence.protectedAfter[path]===sha,'受保护手写或配置字节变化。');}
 requireValue(Array.isArray(evidence.browser)&&evidence.browser.length===3,'缺少三个原toast的浏览器证据。');const seen=new Set();for(const item of evidence.browser){keys(item,['requestId','action','toastText','screenshotSha256','observedBy']);requireValue(byId.has(item.requestId)&&!seen.has(item.requestId)&&item.action===byId.get(item.requestId).action&&item.toastText===({'generate-project-config':'生成项目配置','sync-runtime':'同步运行时','generate-all-page':'生成全部页面'}[item.action]+'完成')&&HASH.test(item.screenshotSha256||'')&&item.observedBy==='真实浏览器人工复核','toast必须来自对应原UUID与真实浏览器外部复核。');seen.add(item.requestId);}
 return {schemaVersion:1,planSha256:plan.planSha256,structureValid:true,requestCount:3,groupCount:groupIds.size,groups:outcomes,acceptanceCompleted:false,requiresLiveCaptureAndRootReview:true,note:'本工具校验结构及关联，不从离线JSON推断真实HTTP、文件apply或浏览器已经发生。'};
}
/** 拒绝既有祖先链接，不把证据路径变成任意文件读取/覆盖入口。 */
async function safe(path){for(let item=resolve(path);;item=dirname(item)){try{requireValue(!(await lstat(item)).isSymbolicLink(),'验收路径不能经过链接。');}catch(error){if(error.code!=='ENOENT')throw error;}if(dirname(item)===item)break;}return path;}
/** 仅新建并刷盘自己的私有计划/报告，存在文件或目录立即拒绝，不覆盖旧证据。 */
async function writeNew(path,value){await safe(path);await mkdir(dirname(path),{recursive:true,mode:0o700});const file=await open(path,'wx',0o600);try{await file.writeFile(JSON.stringify(value,null,2)+'\n');await file.sync();}finally{await file.close();}}
/** 读取有界本地摘要文件；没有网络、登录或产品命令的隐式执行路径。 */
async function readJson(path){await safe(path);const stat=await lstat(path);requireValue(stat.isFile()&&stat.size>0&&stat.size<=4*1024*1024,'输入必须为4MiB以内普通JSON文件。');return JSON.parse(await readFile(path,'utf8'));}
/** 入口只支持计划和离线回执结构校验；现场命令由根审阅后逐步执行，不能在本阶段隐式上线。 */
export async function main(args){requireValue(args.length===5&&['plan','verify'].includes(args[0])&&args[1]==='--input'&&args[3]==='--output','用法：node online-helper-browser.mjs plan|verify --input 私有输入.json --output 新报告.json');const input=await readJson(absolute(args[2]));const output=absolute(args[4]);if(args[0]==='plan'){const plan=buildPlan(input);requireValue(within(plan.fixtureRoot,output)&&!within(plan.target,output)&&!within(plan.workRoot,output),'计划报告必须在本轮fixture控制目录，不能进入target/work。');await writeNew(output,plan);return{status:'planned-not-executed',planSha256:plan.planSha256,metadataWrites:0,newDatabases:0};}keys(input,['plan','evidence']);validatePlan(input.plan);requireValue(within(input.plan.fixtureRoot,output)&&!within(input.plan.target,output)&&!within(input.plan.workRoot,output),'报告必须位于本轮fixture控制目录。');const result=verifyReceiptStructure(input.plan,input.evidence);await writeNew(output,result);return result;}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url))main(process.argv.slice(2)).then(result=>console.log(JSON.stringify(result))).catch(()=>{console.error('验收计划或回执校验失败，原证据保留；本工具未联网或执行现场命令。');process.exitCode=1;});
