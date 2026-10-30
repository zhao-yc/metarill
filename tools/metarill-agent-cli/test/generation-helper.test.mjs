import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, mkdir, readFile, writeFile, readdir, rm, cp, stat, access, realpath } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, dirname, delimiter, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { main } from '../lib/cli.mjs';
import { ConfigStore } from '../lib/config.mjs';

const source = fileURLToPath(new URL('../../../', import.meta.url));
let shared, dotnet, cli, probe;
/** 测试只构建固定C#CLI和离线夹具，不安装依赖或启动产品服务。 */
async function processRun(executable, args) {
 return new Promise((accept, reject) => { const child = spawn(executable, args, { cwd: source, shell: false, windowsHide: true }); let out = ''; child.stdout.on('data', x => out += x); child.stderr.on('data', x => out += x); child.on('error', reject); child.on('close', code => code === 0 ? accept(out) : reject(new Error('固定测试进程失败：' + out.slice(-2000)))); });
}
async function dotnetPath() {
 const values = [process.env.METARILL_DOTNET_PATH, process.env.DOTNET_HOST_PATH, ...(process.env.PATH || '').split(delimiter).map(path => join(path, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'))].filter(Boolean);
 for (const value of values) { try { await access(value); return await realpath(resolve(value)); } catch {} } throw new Error('真实本机助手测试需要已安装dotnet；未将缺失视为通过。');
}
before(async () => {
 dotnet = await dotnetPath(); shared = await mkdtemp(join(tmpdir(), 'metarill-helper-build-')); const binaries = join(shared, 'cli');
 await processRun(dotnet, ['build', join(source, 'backend/src/Metarill.Cli/Metarill.Cli.csproj'), '--nologo', '--disable-build-servers', '--verbosity', 'quiet', '--output', binaries]); cli = binaries;
 const folder = join(shared, 'probe'); await mkdir(folder); const escape = text => text.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
 await writeFile(join(folder, 'Probe.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><ProjectReference Include="${escape(join(source, 'backend/src/Metarill.Generation/Metarill.Generation.csproj'))}"/></ItemGroup></Project>`);
 await writeFile(join(folder, 'Program.cs'), `using System.Text.Json; using Metarill.Core.Metadata; using Metarill.Generation;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var managed=args[0]=="managed";if(managed)args=args.Skip(1).ToArray();
// 独占测试包用原Updater在实际替换后注入异常，生产CLI没有故障参数。
if(args.Length>2) {
 var plan=LocalGenerationUpdater.ReadPlan(args[2]); var updater=new LocalGenerationUpdater(); string changed=null;
 if(args[3]=="shell-unconfirmed") {
  typeof(LocalGenerationUpdater).GetProperty("AfterCreatePublication",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(updater,(Action<string>)(path=>{changed=path;throw new InvalidOperationException("原壳已发布但归属确认未刷盘");}));
  try {updater.Apply(plan,plan.PlanSha256);} catch(InvalidOperationException) {}
  if(changed!="frontend/common/project.js")throw new InvalidOperationException("必须在真实原壳发布后确认前切断");return;
 }

 typeof(LocalGenerationUpdater).GetProperty("AfterWrite",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(updater,(Action<string>)(path=>{changed=path;File.WriteAllText(Path.Combine(plan.TargetDirectory,path),"本次故障注入留下的第三种字节");throw new InvalidOperationException("实际替换后测试中断");}));
 try {updater.Apply(plan,plan.PlanSha256);} catch(InvalidOperationException) {}
 if(changed is null)throw new InvalidOperationException("没有产生实际写入，不能当恢复通过");
 if(args[3]=="recoverable") {
  var active=JsonDocument.Parse(File.ReadAllText(Path.Combine(plan.TargetDirectory,".local/metarill-updates/active.json"))).RootElement;
  var tx=active.GetProperty("transactionId").GetString()!; var folder=Path.Combine(plan.TargetDirectory,".local/metarill-updates/transactions",tx);
  var files=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"journal.json"))).RootElement.GetProperty("files").EnumerateArray().ToArray(); var index=Array.FindIndex(files,file=>file.GetProperty("path").GetString()==changed);
  File.WriteAllBytes(Path.Combine(plan.TargetDirectory,changed),File.ReadAllBytes(Path.Combine(folder,"staged",index.ToString("D5")+".bin")));
 }
 return;
}
var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(args[1], "backend/metadata/demo.json")), json)!;
if(managed)new ApplicationPackager().CreateManagement(catalog,Path.Combine(args[0],"target"),"Example.Assistant",args[1],catalog.YzAdminTypes[0].Id);
else new ApplicationPackager().Create(catalog, Path.Combine(args[0], "target"), "Example.Assistant", args[1]);
catalog.YzProjectName = "在线助手预览"; catalog.ConfigColumns.First(c => c.ColumnName == "name").Name = "助手验收名称";
File.WriteAllText(Path.Combine(args[0],"snapshots.json"),JsonSerializer.Serialize(new[]{"generate-project-config","sync-runtime","generate-all-page"}.Select(action => LocalGenerationUpdater.CreateRequest(catalog,action)),json));
File.WriteAllText(Path.Combine(args[0], "snapshot.json"), JsonSerializer.Serialize(LocalGenerationUpdater.CreateRequest(catalog, "generate-project-config"), json));`);
 await processRun(dotnet, ['build', join(folder, 'Probe.csproj'), '--nologo', '--disable-build-servers', '--verbosity', 'quiet', '--output', join(shared, 'fixture')]); probe = join(shared, 'fixture/Probe.dll');
});
after(async () => { if (shared) { assert.ok(shared.startsWith(tmpdir())); await rm(shared, { recursive: true }); } });

/** 本地真实HTTP通道配合真实C#离线程序；网络认证是合约替身，实际grant另由后端MySQL用例验证。 */
async function fixture(t, managed=false) {
 const root = await mkdtemp(join(tmpdir(), 'metarill-helper-node-')); await processRun(dotnet, [probe,...(managed?['managed']:[]),root,source]); await cp(cli, join(root, 'cli'), { recursive: true });
 const store = new ConfigStore(join(root, 'config/config.json')); const snapshot = JSON.parse(await readFile(join(root, 'snapshot.json'), 'utf8'));
 const snapshots=JSON.parse(await readFile(join(root,'snapshots.json'),'utf8'));
 const state = { snapshots, approvals: [], requests: [], binding: null, reports: [], requestId: randomUUID(), leaseId: randomUUID(), snapshot, failReport: false, failBind: false, failApplicationReport: false, applications: [], accountId: '1', projectId: snapshot.projectId, grantId: '11', foreign: false };
 const server = createServer(async (request, response) => {
  const chunks = []; for await (const chunk of request) chunks.push(chunk); const body = chunks.length ? JSON.parse(Buffer.concat(chunks).toString()) : null; state.requests.push({ path: request.url, body });
  const send = (data, code = 200, status = 200) => { response.writeHead(status, { 'content-type': 'application/json' }); response.end(JSON.stringify({ code, data, msg: code === 200 ? null : '测试模拟回执响应丢失' })); };
  if (request.headers.authorization !== 'Bearer helper-test-access') return send(null, 500, 401);
  if (request.url === '/api/admin/sys/agent/me') return send({ projectId: state.projectId, accountId: state.accountId, grantId: state.grantId });
  if (request.url === '/api/admin/sys/generation-assistant/helper/bind') { assert.deepEqual(Object.keys(body).sort(), ['allowedActions','bindingId','localBindingSha256','mode','rootNamespace']); state.binding = body; if (state.failBind) { state.failBind = false; return send(null, 500, 503); } return send({ ...body, projectId: state.projectId }); }
  if (request.url === '/api/admin/sys/generation-assistant/helper/claim') return send(state.done ? null : { requestId: state.requestId, bindingId: state.binding.bindingId, projectId: state.foreign ? '999' : state.projectId, rootNamespace: state.binding.rootNamespace, localBindingSha256: state.binding.localBindingSha256, leaseId: state.leaseId, leaseExpiresAt: new Date(Date.now() + 300000).toISOString(), snapshot: state.snapshot, applied: false });
  if (request.url === '/api/admin/sys/generation-assistant/helper/report') { state.reports.push(body); state.done = true; if (state.failReport) { state.failReport = false; return send(null, 500, 503); } return send({ requestId: state.requestId, status: body.status.replaceAll('-', '_'), applied: false, outcome: 'not_applied' }); }
  if (request.url === '/api/admin/sys/generation-assistant/helper/approvals') return send(state.approvals);
  if (request.url === '/api/admin/sys/generation-assistant/helper/authorize') { state.ticket={groupId:body.groupId,bindingId:body.bindingId,metadataSha256:body.metadataSha256,approvals:state.approvals.filter(item=>body.requestIds.includes(item.requestId)).sort((a,b)=>a.action.localeCompare(b.action)),actions:body.actions,planSha256:body.planSha256,issuedAt:new Date().toISOString(),expiresAt:new Date(Date.now()+90000).toISOString(),ticketSha256:'c'.repeat(64)}; return send(state.ticket); }
  if (request.url === '/api/admin/sys/generation-assistant/helper/verify-ticket') { if(state.revokeBeforeVerify) return send(null,500,401); return send(state.ticket); }
  if (request.url === '/api/admin/sys/generation-assistant/helper/application-report') { state.applications.push(body); state.applicationDone=true; if(state.failApplicationReport) {state.failApplicationReport=false;return send(null,500,503)} return send(body); }
  send(null, 500, 404);
 });
 await new Promise(done => server.listen(0, '127.0.0.1', done)); const url = `http://127.0.0.1:${server.address().port}/api`;
 await store.write({ version: 1, activeProfile: 'helper', profiles: { helper: { url, projectId: state.projectId, credentials: { boundUrl: url, projectId: state.projectId, accessToken: 'helper-test-access', refreshToken: 'helper-test-refresh', expiresAt: Date.now() + 3600000 } } } });
 const run = async args => { let stdout = '', stderr = ''; const code = await main(args, { store, stdout: { write: text => stdout += text }, stderr: { write: text => stderr += text } }); return { code, stdout, stderr, data: stdout ? JSON.parse(stdout) : null }; };
 const bind = (policy=[]) => run(['helper', 'bind', '--target', join(root, 'target'), '--source-root', source, '--work-root', join(root, 'work'), '--cli', join(root, 'cli/Metarill.Cli.dll'), '--dotnet', dotnet,...policy]);
 t.after(async () => { await new Promise(done => server.close(done)); assert.ok(root.startsWith(tmpdir())); await rm(root, { recursive: true }); });
 return { root, state, store, run, bind };
}
async function fingerprint(root) {
 const files = []; async function walk(path) { for (const item of await readdir(path, { withFileTypes: true })) { const full = join(path, item.name); if (item.isDirectory()) await walk(full); else files.push(full + ':' + createHash('sha256').update(await readFile(full)).digest('hex')); } } await walk(root); return files.sort().join('\n');
}

test('真实本机绑定和固定prepare只形成候选/预览，回执丢失重传不重新生成', { timeout: 180000 }, async t => {
 const f = await fixture(t); const before = await fingerprint(join(f.root, 'target')); f.state.failBind = true; assert.equal((await f.bind()).code, 1); const pending = (await f.store.read()).profiles.helper.generationAssistant; assert.equal(pending.registrationStatus, 'pending'); f.state.failReport = true;
 const first = await f.run(['helper','poll','--once']); assert.equal(first.code, 1); assert.equal(f.state.done, true, '服务端已持久终态，后续Claim必须为空'); assert.equal((await f.store.read()).profiles.helper.generationAssistant.bindingId, pending.bindingId); assert.equal((await f.store.read()).profiles.helper.generationAssistant.registrationStatus, 'bound'); assert.equal(await fingerprint(join(f.root, 'target')), before); const task = join(f.root, 'work', f.state.requestId); const planBefore = await stat(join(task, 'plan.json'));
 const second = await f.run(['helper','poll','--once']); assert.equal(second.code, 0, second.stderr); assert.equal(second.data.status, 'dependencies_required'); assert.equal(second.data.applied, false); assert.equal((await stat(join(task, 'plan.json'))).mtimeMs, planBefore.mtimeMs); assert.equal(f.state.reports.length, 2); assert.deepEqual(f.state.reports[0], f.state.reports[1]); assert.equal(await fingerprint(join(f.root, 'target')), before);
 const state = JSON.parse(await readFile(join(task, 'helper-task.json'), 'utf8')); assert.equal(state.status, 'reported'); assert.ok(!JSON.stringify(f.state.requests).includes('helper-test-access')); assert.ok(!JSON.stringify(f.state.requests.map(x => x.body)).includes('targetDirectory'));
 const bindCalls = f.state.requests.filter(item => item.path.endsWith('/helper/bind')).length; assert.equal((await f.bind()).code, 1); assert.equal(f.state.requests.filter(item => item.path.endsWith('/helper/bind')).length, bindCalls); assert.equal((await f.run(['helper','poll','--once','--apply'])).code, 1);
});

test('重启发现preparing只报告中断，不覆盖候选或重新执行本机命令', { timeout: 180000 }, async t => {
 const f = await fixture(t); assert.equal((await f.bind()).code, 0); const before = await fingerprint(join(f.root, 'target')); const folder = join(f.root, 'work', f.state.requestId); await mkdir(folder);
 await writeFile(join(f.root, 'work', 'helper-' + f.state.binding.bindingId + '.lock'), JSON.stringify({ schemaVersion: 1, pid: 2147483647, bindingId: f.state.binding.bindingId }));
 await writeFile(join(folder, 'helper-task.json'), JSON.stringify({ schemaVersion: 1, bindingId: f.state.binding.bindingId, requestId: f.state.requestId, requestSha256: f.state.snapshot.requestSha256, leaseId: f.state.leaseId, status: 'preparing' }));
 const result = await f.run(['helper','poll','--once']); assert.equal(result.code, 0, result.stderr); assert.equal(result.data.status, 'failed'); assert.equal(result.data.applied, false); assert.equal(f.state.reports[0].errorCode, 'local_prepare_interrupted'); assert.equal(await fingerprint(join(f.root, 'target')), before); await assert.rejects(stat(join(folder, 'candidate')), { code: 'ENOENT' });
});

test('跨项目工作与CLI字节漂移都拒绝，本机归属和目录不上传', { timeout: 180000 }, async t => {
 const f = await fixture(t); assert.equal((await f.bind()).code, 0); const before = await fingerprint(join(f.root, 'target')); f.state.foreign = true;
 const invalid = await f.run(['helper','poll','--once']); assert.equal(invalid.code, 1); assert.match(invalid.stderr, /helper_invalid_work/); assert.equal(f.state.reports.length, 0); assert.equal(await fingerprint(join(f.root, 'target')), before);
 await writeFile(join(f.root, 'cli/Metarill.Cli.dll'), '固定CLI测试漂移'); const count = f.state.requests.length; const changed = await f.run(['helper','poll','--once']); assert.equal(changed.code, 1); assert.match(changed.stderr, /helper_binding_changed/); assert.equal(f.state.requests.slice(count).filter(x => x.path.includes('helper/claim')).length, 0);
 const bodies = f.state.requests.filter(x => x.body).map(x => x.body); for (const body of bodies) assert.ok(!Object.keys(body).some(key => ['target','sourceRoot','cli','dotnet','workRoot'].includes(key)));
});


test('真实CLI拒绝篡改快照时保留本机失败诊断，只上报稳定失败代码', { timeout: 180000 }, async t => {
 const f = await fixture(t); assert.equal((await f.bind()).code, 0); const before = await fingerprint(join(f.root, 'target')); f.state.snapshot.metadata.yzProjectName = '哈希后被篡改';
 const result = await f.run(['helper','poll','--once']); assert.equal(result.code, 0, result.stderr); assert.equal(result.data.status, 'failed'); assert.equal(result.data.applied, false); assert.equal(f.state.reports[0].errorCode, 'prepare_failed'); assert.equal(await fingerprint(join(f.root, 'target')), before);
 const task = join(f.root, 'work', f.state.requestId); const diagnostic = JSON.parse(await readFile(join(task, 'diagnostic.json'), 'utf8')); assert.equal(diagnostic.exitCode, 1); assert.ok(diagnostic.stderrTail.length > 0); assert.ok(!JSON.stringify(f.state.reports).includes('stderrTail')); await assert.rejects(stat(join(task, 'candidate')), { code: 'ENOENT' });
});

/** 此处网络用实际HTTP合约替身；源码应用与后续回执恢复均调用真实C#CLI。 */
test('本机显式允许后仅合并同快照分别确认的动作，真实应用保留AppID和partial，终态ack丢失重传', {timeout:180000}, async t => {
 const f=await fixture(t); const configAction='generate-project-config',pagesAction='generate-all-page';
 assert.equal((await f.bind(['--mode','apply','--allow-actions',configAction+','+pagesAction,'--confirm'])).code,0);
 const appid=join(f.root,'target/frontend/manifest.json'); const manifest=JSON.parse(await readFile(appid,'utf8')); manifest.appid='__UNI__TEST_ASSISTANT'; await writeFile(appid,JSON.stringify(manifest));
 const service=join(f.root,'target/backend/generated/Services/DemoCustomerService.cs'); await writeFile(service,(await readFile(service,'utf8'))+'\n// 本机手写扩展必须保持\n'); const manual=await readFile(service,'utf8');
 const firstProof={requestId:f.state.requestId,action:configAction,requestSha256:f.state.snapshot.requestSha256,metadataSha256:f.state.snapshot.metadataSha256}; f.state.approvals=[firstProof];
 const before=await fingerprint(join(f.root,'target')); const missing=await f.run(['helper','poll','--once']); assert.equal(missing.code,0,missing.stderr); assert.equal(missing.data.applied,false); assert.ok(missing.data.requiredActions.includes(pagesAction)); assert.equal(await fingerprint(join(f.root,'target')),before); assert.equal(f.state.requests.filter(item=>item.path.endsWith('/helper/authorize')).length,0);
 f.state.snapshot=f.state.snapshots.find(item=>item.action===pagesAction); f.state.requestId=randomUUID(); f.state.leaseId=randomUUID(); f.state.done=false; f.state.approvals.push({requestId:f.state.requestId,action:pagesAction,requestSha256:f.state.snapshot.requestSha256,metadataSha256:f.state.snapshot.metadataSha256}); f.state.failApplicationReport=true;
 const apply=await f.run(['helper','poll','--once']); assert.equal(apply.code,1,apply.stderr); assert.equal(f.state.applicationDone,true); assert.notEqual(await fingerprint(join(f.root,'target')),before); assert.deepEqual(f.state.ticket.actions,[pagesAction,configAction].sort()); assert.ok(!f.state.ticket.actions.includes('sync-runtime'));
 const task=join(f.root,'work',f.state.requestId); const stamp=(await stat(join(task,'application-plan.json'))).mtimeMs; f.state.failApplicationReport=true; const replay=await f.run(['helper','poll','--once']); assert.equal(replay.code,1); assert.equal(f.state.applications.length,2); assert.deepEqual(f.state.applications[0],f.state.applications[1]);
 // 已实际提交的同一日志，回放持久applying切点，必须只读核验committed而不重跑apply。
 const applicationPath=join(task,'application.json'); const interrupted=JSON.parse(await readFile(applicationPath,'utf8')); interrupted.phase='applying'; delete interrupted.receipt; await writeFile(applicationPath,JSON.stringify(interrupted));
 const result=await f.run(['helper','poll','--once']); assert.equal(result.code,0,result.stderr); assert.equal(result.data.status,'applied'); assert.equal(result.data.applied,true); assert.equal(f.state.applications.length,3); assert.deepEqual(f.state.applications[0],f.state.applications[2]); assert.equal((await stat(join(task,'application-plan.json'))).mtimeMs,stamp); assert.equal(JSON.parse(await readFile(appid,'utf8')).appid,'__UNI__TEST_ASSISTANT'); assert.equal(await readFile(service,'utf8'),manual);
});

test('管理完整包沿真实CLI助手合并原确认动作，应用及回执重放保留管理来源和手写扩展', {timeout:360000}, async t => {
 const f=await fixture(t,true); const configAction='generate-project-config',pagesAction='generate-all-page';
 assert.equal((await f.bind(['--mode','apply','--allow-actions',configAction+','+pagesAction,'--confirm'])).code,0);
 const appid=join(f.root,'target/frontend/manifest.json'); const manifest=JSON.parse(await readFile(appid,'utf8')); manifest.appid='__UNI__TEST_ASSISTANT'; await writeFile(appid,JSON.stringify(manifest));
 const service=join(f.root,'target/backend/generated/Services/YzRoleService.cs'); await writeFile(service,(await readFile(service,'utf8'))+'\n// 本机手写扩展必须保持\n'); const manual=await readFile(service,'utf8');
 const firstProof={requestId:f.state.requestId,action:configAction,requestSha256:f.state.snapshot.requestSha256,metadataSha256:f.state.snapshot.metadataSha256}; f.state.approvals=[firstProof];
 const before=await fingerprint(join(f.root,'target')); const missing=await f.run(['helper','poll','--once']); assert.equal(missing.code,0,missing.stderr); assert.equal(missing.data.applied,false); assert.ok(missing.data.requiredActions.includes(pagesAction)); assert.equal(await fingerprint(join(f.root,'target')),before); assert.equal(f.state.requests.filter(item=>item.path.endsWith('/helper/authorize')).length,0);
 f.state.snapshot=f.state.snapshots.find(item=>item.action===pagesAction); f.state.requestId=randomUUID(); f.state.leaseId=randomUUID(); f.state.done=false; f.state.approvals.push({requestId:f.state.requestId,action:pagesAction,requestSha256:f.state.snapshot.requestSha256,metadataSha256:f.state.snapshot.metadataSha256}); f.state.failApplicationReport=true;
 const apply=await f.run(['helper','poll','--once']); assert.equal(apply.code,1,apply.stderr); assert.equal(f.state.applicationDone,true); assert.notEqual(await fingerprint(join(f.root,'target')),before); assert.deepEqual(f.state.ticket.actions,[pagesAction,configAction].sort()); assert.ok(!f.state.ticket.actions.includes('sync-runtime'));
 const task=join(f.root,'work',f.state.requestId); const stamp=(await stat(join(task,'application-plan.json'))).mtimeMs; f.state.failApplicationReport=true; const replay=await f.run(['helper','poll','--once']); assert.equal(replay.code,1); assert.equal(f.state.applications.length,2); assert.deepEqual(f.state.applications[0],f.state.applications[1]);
 // 已实际提交的同一日志，回放持久applying切点，必须只读核验committed而不重跑apply。
 const applicationPath=join(task,'application.json'); const interrupted=JSON.parse(await readFile(applicationPath,'utf8')); interrupted.phase='applying'; delete interrupted.receipt; await writeFile(applicationPath,JSON.stringify(interrupted));
 const result=await f.run(['helper','poll','--once']); assert.equal(result.code,0,result.stderr); assert.equal(result.data.status,'applied'); assert.equal(result.data.applied,true); assert.equal(f.state.applications.length,3); assert.deepEqual(f.state.applications[0],f.state.applications[2]); assert.equal((await stat(join(task,'application-plan.json'))).mtimeMs,stamp); assert.equal(JSON.parse(await readFile(appid,'utf8')).appid,'__UNI__TEST_ASSISTANT'); assert.equal(await readFile(service,'utf8'),manual);
 const packageJson=JSON.parse(await readFile(join(f.root,'target/.metarill-package.json'),'utf8')); assert.equal(packageJson.packageMode,'FullManagementApplication'); assert.equal(packageJson.managementModule.managementAdminTypeId,'2200000000000100001'); assert.ok(packageJson.files.some(file=>file.path==='backend/metadata/application-source.json')); const identity=JSON.parse(await processRun(dotnet,[join(f.root,'cli/Metarill.Cli.dll'),'update','target-identity','--target',join(f.root,'target')])); assert.equal(identity.rootNamespace,'Example.Assistant'); assert.equal(identity.projectId,f.state.projectId);
});

test('已领取且准备完成后撤权复核失败，实际目标仍保持原字节，不运行apply', {timeout:180000}, async t => {
 const f=await fixture(t); assert.equal((await f.bind(['--mode','apply','--allow-actions','generate-project-config,generate-all-page','--confirm'])).code,0);
 f.state.approvals=f.state.snapshots.filter(item=>['generate-project-config','generate-all-page'].includes(item.action)).map(item=>({requestId:item.action===f.state.snapshot.action?f.state.requestId:randomUUID(),action:item.action,requestSha256:item.requestSha256,metadataSha256:item.metadataSha256})); f.state.revokeBeforeVerify=true;
 const before=await fingerprint(join(f.root,'target')); const result=await f.run(['helper','poll','--once']); assert.equal(result.code,1); assert.equal(await fingerprint(join(f.root,'target')),before); assert.equal(f.state.applications.length,0); const state=JSON.parse(await readFile(join(f.root,'work',f.state.requestId,'application.json'),'utf8')); assert.equal(state.phase,'authorized');
});

for(const scenario of ['recoverable','third-state']) test('真实原事务重启恢复：'+scenario+'不重跑apply且不覆盖后来字节',{timeout:180000},async t=>{
 const f=await fixture(t); assert.equal((await f.bind(['--mode','apply','--allow-actions','generate-project-config,generate-all-page','--confirm'])).code,0);
 f.state.approvals=f.state.snapshots.filter(item=>['generate-project-config','generate-all-page'].includes(item.action)).map(item=>({requestId:item.action===f.state.snapshot.action?f.state.requestId:randomUUID(),action:item.action,requestSha256:item.requestSha256,metadataSha256:item.metadataSha256})); f.state.revokeBeforeVerify=true;
 const prepared=await f.run(['helper','poll','--once']); assert.equal(prepared.code,1); const task=join(f.root,'work',f.state.requestId); const appPath=join(task,'application.json'); const app=JSON.parse(await readFile(appPath,'utf8')); assert.equal(app.phase,'authorized');
 await processRun(dotnet,[probe,f.root,source,join(task,'application-plan.json'),scenario]); app.phase='applying'; await writeFile(appPath,JSON.stringify(app));
 const activePath=join(f.root,'target/.local/metarill-updates/active.json'); const active=JSON.parse(await readFile(activePath,'utf8')); const journalPath=join(f.root,'target/.local/metarill-updates/transactions',active.transactionId,'journal.json'); const journal=JSON.parse(await readFile(journalPath,'utf8')); assert.equal(journal.status,'manual-recovery'); const changed=journal.files[0].path; const full=join(f.root,'target',changed); const later=await readFile(full,'utf8');
 const result=await f.run(['helper','poll','--once']); assert.equal(result.code,0,result.stderr); assert.equal(result.data.applied,false); assert.equal(result.data.status,scenario==='recoverable'?'rolled_back':'manual_recovery');
 assert.equal(f.state.requests.filter(item=>item.path.endsWith('/helper/verify-ticket')).length,1,'恢复不应请求新的apply授权');
 if(scenario==='recoverable') {await assert.rejects(stat(activePath),{code:'ENOENT'}); assert.equal(JSON.parse(await readFile(journalPath,'utf8')).status,'rolled-back');}
 else {assert.equal(await readFile(full,'utf8'),later); assert.match(later,/第三种字节/); assert.equal(JSON.parse(await readFile(journalPath,'utf8')).status,'manual-recovery');}
});

test('本机apply绑定必须再次明确授权动作，非空工作区和旧策略不静默升级',{timeout:180000},async t=>{
 const f=await fixture(t); const invalid=[['--mode','apply','--allow-actions','sync-runtime'],['--allow-actions','sync-runtime'],['--mode','apply','--allow-actions','sync-runtime,sync-runtime','--confirm']];
 for(const options of invalid) {assert.equal((await f.bind(options)).code,1); assert.equal(f.state.requests.filter(item=>item.path.endsWith('/helper/bind')).length,0);}
 await mkdir(join(f.root,'work')); await writeFile(join(f.root,'work/evidence.txt'),'旧证据保持'); const failed=await f.bind(['--mode','apply','--allow-actions','sync-runtime','--confirm']); assert.equal(failed.code,1); assert.match(failed.stderr,/新的空工作目录/); assert.equal(await readFile(join(f.root,'work/evidence.txt'),'utf8'),'旧证据保持'); assert.equal(f.state.requests.filter(item=>item.path.endsWith('/helper/bind')).length,0);
});

/** v3原壳发布中断使用真实Updater生成严格日志，助手只能保留人工恢复状态，不能凭同字节重apply。 */
test('原壳已发布但未确认的v3事务回放不误报applied且保留同字节文件',{timeout:180000},async t=>{
 const f=await fixture(t); const actions=['generate-project-config','sync-runtime','generate-all-page']; assert.equal((await f.bind(['--mode','apply','--allow-actions',actions.join(','),'--confirm'])).code,0);
 const shell=join(f.root,'target/frontend/common/project.js'); await rm(shell);
 f.state.approvals=f.state.snapshots.map(item=>({requestId:item.action===f.state.snapshot.action?f.state.requestId:randomUUID(),action:item.action,requestSha256:item.requestSha256,metadataSha256:item.metadataSha256})); f.state.revokeBeforeVerify=true;
 const prepared=await f.run(['helper','poll','--once']); assert.equal(prepared.code,1); const task=join(f.root,'work',f.state.requestId); const appPath=join(task,'application.json'); const app=JSON.parse(await readFile(appPath,'utf8')); assert.equal(app.phase,'authorized');
 const planPath=join(task,'application-plan.json'); const plan=JSON.parse(await readFile(planPath,'utf8')); assert.ok(plan.files.some(file=>file.path==='frontend/common/project.js'&&file.action==='create'&&file.beforeSha256===null));
 await processRun(dotnet,[probe,f.root,source,planPath,'shell-unconfirmed']); app.phase='applying'; await writeFile(appPath,JSON.stringify(app)); const bytes=await readFile(shell); const stamp=(await stat(planPath)).mtimeMs;
 const activePath=join(f.root,'target/.local/metarill-updates/active.json'); const active=JSON.parse(await readFile(activePath,'utf8')); const journalPath=join(f.root,'target/.local/metarill-updates/transactions',active.transactionId,'journal.json'); const journal=JSON.parse(await readFile(journalPath,'utf8')); assert.equal(journal.schemaVersion,3); assert.deepEqual(journal.confirmedFrontendCreates,[]); assert.equal(journal.status,'manual-recovery');
 const result=await f.run(['helper','poll','--once']); assert.equal(result.code,0,result.stderr); assert.equal(result.data.status,'manual_recovery'); assert.equal(result.data.applied,false); assert.deepEqual(await readFile(shell),bytes); assert.equal((await stat(planPath)).mtimeMs,stamp); assert.equal(f.state.applications[0].status,'manual-recovery'); assert.equal(f.state.applications[0].writtenFiles,0); assert.equal(f.state.requests.filter(item=>item.path.endsWith('/helper/verify-ticket')).length,1); assert.equal(JSON.parse(await readFile(journalPath,'utf8')).status,'manual-recovery'); assert.ok(await stat(activePath));
});
