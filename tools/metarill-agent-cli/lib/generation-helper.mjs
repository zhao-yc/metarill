import { readFile, writeFile, open, rename, unlink, mkdir, rmdir, lstat, readdir } from 'node:fs/promises';
import { resolve, dirname, join, parse, isAbsolute, sep } from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { restrictPermissions } from './config.mjs';
import { CliError } from './errors.mjs';
import { attemptApplication, resumeApplications } from './generation-application.mjs';
import { bindGenerationDevice,resumeGenerationDevice,generationDeviceStatus,validateDeviceOptions } from './generation-device.mjs';

const HASH = /^[a-f0-9]{64}$/;
const UUID = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
const ACTIONS = ['generate-project-config', 'sync-runtime', 'generate-all-page'];
const BIND_KEYS = ['version','bindingId','apiBaseUrl','accountId','projectId','grantId','rootNamespace','databaseProvider','target','sourceRoot','cli','dotnet','workRoot','cliSha256','dotnetSha256','mode','allowedActions'];
const digest = value => createHash('sha256').update(typeof value === 'string' ? value : JSON.stringify(value)).digest('hex');
const bindingHash = value => digest(Object.fromEntries(BIND_KEYS.map(key => [key, value[key]])));
const within = (root, path) => path.toLowerCase() === root.toLowerCase() || path.toLowerCase().startsWith(root.toLowerCase() + sep);

/** 每个现存祖先都必须是普通文件/目录，拒绝Windows junction和其他符号链接。 */
async function safePath(path) {
 for (let value = path; ; value = dirname(value)) {
  try { if ((await lstat(value)).isSymbolicLink()) throw new CliError('助手路径不能经过链接。', 'helper_unsafe_path'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (dirname(value) === value) break;
 }
 return path;
}
/** 绑定只接受明确绝对普通目录，禁止盘根。 */
async function directory(path, existing = true) {
 if (!isAbsolute(path || '')) throw new CliError('本机绑定必须使用显式绝对路径。', 'helper_invalid_binding');
 path = resolve(path); if (path === parse(path).root) throw new CliError('本机绑定不能指向磁盘根目录。', 'helper_invalid_binding'); await safePath(path);
 if (existing && !(await lstat(path)).isDirectory()) throw new CliError('本机绑定目录不存在。', 'helper_invalid_binding'); return path;
}
/** 固定执行文件摘要在每次运行前复核。 */
async function fileHash(path) { await safePath(path); const stat = await lstat(path); if (!stat.isFile() || stat.size > 64 * 1024 * 1024) throw new CliError('固定CLI或dotnet文件无效。', 'helper_invalid_executable'); return createHash('sha256').update(await readFile(path)).digest('hex'); }
/** 本机任务与回执先刷盘后原子替换，不在网络确认前丢弃现场。 */
async function atomicJson(path, value) {
 await safePath(path); const temp = path + '.' + randomUUID() + '.tmp';
 try { const handle = await open(temp, 'wx', 0o600); try { await handle.writeFile(JSON.stringify(value, null, 2) + '\n'); await handle.sync(); } finally { await handle.close(); } await restrictPermissions(temp); await safePath(path); await rename(temp, path); }
 finally { await unlink(temp).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
}
/** 仅为本助手新建目录限制当前用户权限。 */
async function privateDirectory(path) { await safePath(path); const created = await mkdir(path, { recursive: true, mode: 0o700 }); await safePath(path); if (created) await restrictPermissions(path, true); }

/** 只运行本机明确选择的dotnet和固定CLI，所有参数用数组，永不调用shell。 */
async function runCli(binding, args, timeoutMs) {
 if (await fileHash(binding.cli) !== binding.cliSha256 || await fileHash(binding.dotnet) !== binding.dotnetSha256) throw new CliError('固定CLI或dotnet已变化，请重新绑定。', 'helper_binding_changed');
 return new Promise((accept, reject) => {
  const child = spawn(binding.dotnet, [binding.cli, ...args], { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] }); let stdout = '', stderr = '', stderrSize = 0, failed = false;
  const fail = (exitCode = null) => { if (!failed) { failed = true; child.kill(); const error = new CliError('本机预览未完成；目标未应用，现场已保留。', 'helper_prepare_failed'); error.localDiagnostic = { exitCode: Number.isInteger(exitCode) ? exitCode : null, stdoutTail: stdout.slice(-8192), stderrTail: stderr.slice(-8192) }; reject(error); } };
  const timer = setTimeout(fail, timeoutMs);
  child.stdout.on('data', bytes => { stdout += bytes.toString('utf8'); if (Buffer.byteLength(stdout) > 8 * 1024 * 1024) fail(); });
  child.stderr.on('data', bytes => { stderr += bytes.toString('utf8'); stderrSize += bytes.length; if (stderrSize > 65536) fail(); });
  child.on('error', fail); child.on('close', code => { clearTimeout(timer); if (failed) return; if (![0, 3].includes(code)) { fail(code); return; } try { accept({ code, value: JSON.parse(stdout) }); } catch { fail(); } });
 });
}
/** 线上绑定只包含身份与摘要，不含本机目录。 */
function publicBinding(binding) { return { bindingId: binding.bindingId, rootNamespace: binding.rootNamespace, localBindingSha256: binding.localBindingSha256, mode: binding.mode, allowedActions: binding.allowedActions }; }
/** 绑定与当前网络授权三元组必须完全相同。 */
function sameIdentity(binding, current) {
 const me = current.me; if (binding.apiBaseUrl !== current.selected.profile.url || binding.accountId !== String(me.accountId) || binding.projectId !== String(me.projectId) || binding.grantId !== String(me.grantId)) throw new CliError('助手绑定与当前账号、项目或grant不一致。', 'helper_identity_changed');
}
/** 失败只回传固定错误码，不上传stderr或本机路径。 */
function failure(work, code) { return { bindingId: work.bindingId, requestId: work.requestId, leaseId: work.leaseId, requestSha256: work.snapshot.requestSha256, status: 'failed', planSha256: null, requiredActions: [], createdFiles: 0, replacedFiles: 0, deletedFiles: 0, preservedFiles: 0, errorCode: code }; }

/** PID锁只回收已证明退出进程的原锁；未知内容或仍活跃PID均保留，不能强抢。 */
async function lockBinding(binding) {
 await privateDirectory(binding.workRoot); const path = join(binding.workRoot, 'helper-' + binding.bindingId + '.lock'); await safePath(path);
 const gate = path + '.gate'; await safePath(gate);
 try { await mkdir(gate); } catch (error) { if (error.code === 'EEXIST') throw new CliError('助手锁正被核验或遗留未完成gate，请保留现场检查。', 'helper_locked'); throw error; }
 try {
 for (let attempt = 0; attempt < 3; attempt++) {
  try { const handle = await open(path, 'wx', 0o600); const value = { schemaVersion: 1, pid: process.pid, bindingId: binding.bindingId, lockId: randomUUID() }; await handle.writeFile(JSON.stringify(value)); await handle.sync(); await restrictPermissions(path); return async () => { await handle.close(); const current = JSON.parse(await readFile(path, 'utf8')); if (current.lockId !== value.lockId) throw new CliError('助手锁在运行中变化，保留现场。', 'helper_locked'); await unlink(path); }; }
  catch (error) {
   if (error.code !== 'EEXIST') throw error;
   const text = await readFile(path, 'utf8'); let old; try { old = JSON.parse(text); } catch { throw new CliError('助手锁内容未知，保留现场。', 'helper_locked'); }
   if (old.schemaVersion !== 1 || old.bindingId !== binding.bindingId || !Number.isSafeInteger(old.pid) || old.pid <= 0) throw new CliError('助手锁身份未知，保留现场。', 'helper_locked');
   try { process.kill(old.pid, 0); throw new CliError('另一个助手进程仍在处理本机绑定。', 'helper_locked'); }
   catch (probe) { if (probe.code !== 'ESRCH') throw probe; }
   if (await readFile(path, 'utf8') !== text) throw new CliError('助手锁在检查后发生变化。', 'helper_locked');
   await rename(path, path + '.stale-' + randomUUID());
  }
 }
 throw new CliError('无法取得助手锁。', 'helper_locked');
 } finally { await rmdir(gate); }
}

/** 注册意图先在本机持久化；响应未知时只重试原bindingId，不产生新的服务器孤儿绑定。 */
async function finishRegistration(context, binding) {
 const registered = await context.rpc('/admin/sys/generation-assistant/helper/bind', publicBinding(binding));
 if (!registered || registered.bindingId !== binding.bindingId || registered.projectId !== binding.projectId || registered.rootNamespace !== binding.rootNamespace || registered.localBindingSha256 !== binding.localBindingSha256 || registered.mode !== binding.mode || JSON.stringify(registered.allowedActions) !== JSON.stringify(binding.allowedActions)) throw new CliError('服务器未确认当前本机绑定。', 'helper_invalid_receipt');
 const current = await context.identity(); sameIdentity(binding, current);
 await context.store.locked(async config => {
  const profile = config.profiles[current.selected.name];
  if (!profile || profile.url !== binding.apiBaseUrl || String(profile.projectId) !== binding.projectId || profile.generationAssistant?.bindingId !== binding.bindingId || profile.generationAssistant.localBindingSha256 !== binding.localBindingSha256) throw new CliError('注册期间本机profile已变化，保留原绑定。', 'helper_binding_conflict');
  profile.generationAssistant.registrationStatus = 'bound'; await context.store.write(config);
 });
 binding.registrationStatus = 'bound';
}

/** 显式绑定本机目录与执行文件；服务器只收到不可逆摘要和namespace。 */
export async function bindGenerationHelper(context, options) {
 const current = await context.identity();
 const mode = options.mode ?? 'preview'; const allowedActions = options['allow-actions']?.split(',').sort() ?? [];
 if (!['preview','apply'].includes(mode) || mode === 'preview' && allowedActions.length || mode === 'apply' && (!options.confirm || !allowedActions.length) || allowedActions.length > 3 || new Set(allowedActions).size !== allowedActions.length || allowedActions.some(action => !ACTIONS.includes(action))) throw new CliError('apply绑定必须显式--confirm并指定唯一allow-actions；默认preview不能写入。', 'helper_invalid_policy');
 if(options['device-id']!==undefined){validateDeviceOptions(options);if(mode!=='apply'||!allowedActions.includes('sync-runtime'))throw new CliError('设备关联要求apply并允许sync-runtime。','helper_invalid_policy')}else if(options['replace-device-binding']!==undefined)throw new CliError('替换设备关联必须指定device-id。','helper_invalid_device');
 if (current.selected.profile.generationAssistant) throw new CliError('本机profile已有绑定；待注册绑定请运行helper poll恢复原编号。', 'helper_binding_conflict');
 const target = await directory(options.target); const sourceRoot = await directory(options['source-root']); const workRoot = await directory(options['work-root'], false);
 if ([target, sourceRoot].some(path => within(path, workRoot) || within(workRoot, path)) || within(target, sourceRoot) || within(sourceRoot, target)) throw new CliError('目标、源码与助手工作目录必须互不包含。', 'helper_invalid_binding');
 for (const name of ['cli', 'dotnet']) if (!isAbsolute(options[name] || '')) throw new CliError('CLI和dotnet必须显式使用绝对路径。', 'helper_invalid_executable');
 try { if ((await readdir(workRoot)).length) throw new CliError('新绑定必须使用新的空工作目录；保留旧profile和任务证据，不得混用。','helper_invalid_binding'); } catch(error) { if(error.code!=='ENOENT') throw error; }
 const cli = resolve(options.cli), dotnet = resolve(options.dotnet); if (!cli.endsWith('.dll')) throw new CliError('固定生成CLI必须是明确的DLL文件。', 'helper_invalid_executable');
 const basis = { cli, dotnet, cliSha256: await fileHash(cli), dotnetSha256: await fileHash(dotnet) };
 const targetIdentity = (await runCli(basis, ['update', 'target-identity', '--target', target], context.timeoutMs)).value;
 if (String(targetIdentity.projectId) !== String(current.me.projectId) || !targetIdentity.rootNamespace || targetIdentity.databaseProvider !== 'mysql') throw new CliError('本机已验证工程与授权项目或数据库不一致。', 'helper_identity_changed');
 const binding = { version: 1, bindingId: randomUUID(), apiBaseUrl: current.selected.profile.url, accountId: String(current.me.accountId), projectId: String(current.me.projectId), grantId: String(current.me.grantId), rootNamespace: targetIdentity.rootNamespace, databaseProvider: targetIdentity.databaseProvider, target, sourceRoot, cli, dotnet, workRoot, cliSha256: basis.cliSha256, dotnetSha256: basis.dotnetSha256, mode, allowedActions };
 binding.localBindingSha256 = bindingHash(binding); binding.registrationStatus = 'pending'; await privateDirectory(workRoot);
 await context.store.locked(async config => { const profile = config.profiles[current.selected.name]; if (!profile || profile.url !== binding.apiBaseUrl || String(profile.projectId) !== binding.projectId || profile.generationAssistant) throw new CliError('本机profile已变化或存在绑定，请先审阅原配置。', 'helper_binding_conflict'); profile.generationAssistant = binding; await context.store.write(config); });
 await finishRegistration(context, binding);
 const device=options['device-id']!==undefined?await associateGenerationDevice(context,options):null;
 return { ...(device?{deviceAssociation:device}:{}),bindingId: binding.bindingId, projectId: binding.projectId, rootNamespace: binding.rootNamespace, preparationOnly: mode === 'preview', mode, allowedActions, recoveryAuthorized: mode === 'apply', applied: false };
}

/** 先恢复已刷盘但网络未确认的出箱；即使服务器Claim已跳过终态，也只重传原回执。 */
async function flushPreparedOutbox(context, binding) {
 const entries = await readdir(binding.workRoot, { withFileTypes: true });
 if (entries.length > 2000) throw new CliError('助手工作区超过恢复扫描上限，保留现场。', 'helper_invalid_state');
 for (const entry of entries.filter(item => UUID.test(item.name)).sort((left, right) => left.name.localeCompare(right.name))) {
  const folder = join(binding.workRoot, entry.name); await safePath(folder); if (!entry.isDirectory()) throw new CliError('任务目录类型无效。', 'helper_invalid_state');
  const path = join(folder, 'helper-task.json'); let state;
  try { await safePath(path); const stat = await lstat(path); if (!stat.isFile() || stat.size > 65536) throw new CliError('本机任务状态无效。', 'helper_invalid_state'); state = JSON.parse(await readFile(path, 'utf8')); } catch (error) { if (error.code === 'ENOENT') continue; throw error; }
  if (!['prepared','preview-acked'].includes(state.status)) continue;
  const receipt = state.receipt;
  if (state.schemaVersion !== 1 || state.bindingId !== binding.bindingId || state.requestId !== entry.name || !UUID.test(state.leaseId || '') || !HASH.test(state.requestSha256 || '') || !receipt || receipt.bindingId !== binding.bindingId || receipt.requestId !== state.requestId || receipt.leaseId !== state.leaseId || receipt.requestSha256 !== state.requestSha256) throw new CliError('待确认出箱与本机绑定不一致，保留现场。', 'helper_invalid_state');
  return finishPreview(context,binding,{ runCli,atomicJson,safePath },folder,path,state);

 }
 return null;
}

/** 先持久确认预览，再登记受控应用；中间崩溃也不会遗失待应用意图或重复生成。 */
async function finishPreview(context,binding,io,task,statePath,state) {
 const receipt = state.receipt;
 if (state.status === 'prepared') {
  const reported = await context.rpc('/admin/sys/generation-assistant/helper/report',receipt);
  if (reported?.requestId !== state.requestId || reported.applied !== false || reported.outcome !== 'not_applied') throw new CliError('服务器尚未确认原预览回执，出箱保留。','helper_invalid_receipt');
  state={ ...state,status:'preview-acked' }; await atomicJson(statePath,state);
 }
 if (binding.mode === 'apply' && receipt.status !== 'failed') {
  try {
   const applicationPath=join(task,'application.json'); await safePath(applicationPath);
   const application=JSON.parse(await readFile(applicationPath,'utf8'));
   if(application.phase === 'reported') { await atomicJson(statePath,{...state,status:'reported'}); return {requestId:state.requestId,status:application.receipt.status.replaceAll('-','_'),applied:application.receipt.status==='applied',outcome:application.receipt.status==='applied'?'applied':application.receipt.status==='rolled-back'?'rolled_back':'unknown'}; }
  } catch(error) { if(error.code!=='ENOENT') throw error; }
  const requestPath=join(task,'request.json'); await safePath(requestPath); const info=await lstat(requestPath); if(!info.isFile()||info.size>4*1024*1024) throw new CliError('原请求快照无效。','helper_invalid_state');
  const snapshot=JSON.parse(await readFile(requestPath,'utf8')); if(snapshot.requestSha256!==state.requestSha256 || snapshot.projectId!==binding.projectId) throw new CliError('原请求与待应用意图不一致。','helper_invalid_state');
  const applied=await attemptApplication(context,binding,io,{requestId:state.requestId,snapshot},task,{requiredActions:receipt.requiredActions});
  await atomicJson(statePath,{...state,status:'reported'}); if(applied) return applied;
 }
 await atomicJson(statePath,{...state,status:'reported'});
 return {requestId:state.requestId,status:receipt.status.replaceAll('-','_'),requiredActions:receipt.requiredActions,applied:false,outcome:'not_applied'};
}

/** 所有助手命令共用v1核心绑定校验，新增设备意图不修改原BIND_KEYS或静默升级旧profile。 */
function boundProfile(selected){
 const binding=selected.profile.generationAssistant;
 if (binding && binding.mode === undefined && binding.allowedActions === undefined) throw new CliError('检测到阶段一旧preview绑定；保留原profile，使用新profile重新auth login和helper bind，默认仍只预览；不得编辑摘要或静默升权。','helper_legacy_binding');
 if (!binding || binding.version !== 1 || !UUID.test(binding.bindingId || '') || Object.keys(binding).some(key => ![...BIND_KEYS, 'localBindingSha256', 'registrationStatus'].includes(key)) || bindingHash(binding) !== binding.localBindingSha256 || !['pending','bound'].includes(binding.registrationStatus)) throw new CliError('助手尚未绑定或绑定摘要变化。', 'helper_invalid_binding');
 if (selected.profile.url !== binding.apiBaseUrl || String(selected.profile.projectId) !== binding.projectId || !['preview','apply'].includes(binding.mode) || !Array.isArray(binding.allowedActions) || binding.allowedActions.some(action => !ACTIONS.includes(action)) || new Set(binding.allowedActions).size !== binding.allowedActions.length || binding.mode === 'preview' && binding.allowedActions.length || binding.mode === 'apply' && !binding.allowedActions.length) throw new CliError('本机写策略或profile已变化。', 'helper_invalid_binding');
 return binding;
}

/** 领取前重验本机身份和执行文件，重启时只重传已持久化回执，不重新运行中断的prepare。 */
export async function pollGenerationHelperOnce(context) {
 const selected=await context.local();const binding=boundProfile(selected);
 for (const path of [binding.target, binding.sourceRoot, binding.workRoot]) await directory(path);
 const release = await lockBinding(binding);
 try {
  const io = { runCli, atomicJson, safePath };
  const recovery = await resumeApplications(context,binding,io,true); if (recovery) return recovery;
  const current = await context.identity(); sameIdentity(binding,current);
  if (binding.registrationStatus === 'pending') await finishRegistration(context,binding);
  const device=await resumeGenerationDevice(context,binding,{runCli,atomicJson,safePath,sameIdentity});if(device)return {status:'device_bound',...device};
  const pending = await resumeApplications(context,binding,io); if (pending) return pending;
  const replay = await flushPreparedOutbox(context, binding); if (replay) return replay;
  const identity = (await runCli(binding, ['update', 'target-identity', '--target', binding.target], context.timeoutMs)).value;
  if (identity.projectId !== binding.projectId || identity.rootNamespace !== binding.rootNamespace || identity.databaseProvider !== binding.databaseProvider) throw new CliError('本机工程归属已变化。', 'helper_binding_changed');
  const work = await context.rpc('/admin/sys/generation-assistant/helper/claim', { bindingId: binding.bindingId });
  if (work === null) return { status: 'idle', applied: false };
  if (!work || !UUID.test(work.requestId || '') || !UUID.test(work.leaseId || '') || work.bindingId !== binding.bindingId || work.projectId !== binding.projectId || work.rootNamespace !== binding.rootNamespace || work.localBindingSha256 !== binding.localBindingSha256 || work.applied !== false || !work.snapshot || work.snapshot.type !== 'metarillUpdateRequest' || work.snapshot.schemaVersion !== 1 || work.snapshot.projectId !== binding.projectId || !ACTIONS.includes(work.snapshot.action) || !HASH.test(work.snapshot.requestSha256 || '') || !Number.isFinite(Date.parse(work.leaseExpiresAt)) || Date.parse(work.leaseExpiresAt) <= Date.now()) throw new CliError('助手任务身份、lease或快照无效。', 'helper_invalid_work');
  const task = join(binding.workRoot, work.requestId); await safePath(task); const statePath = join(task, 'helper-task.json'); let state;
  try { const info = await lstat(statePath); if (!info.isFile() || info.size > 65536) throw new CliError('本机任务状态无效。', 'helper_invalid_state'); await safePath(statePath); state = JSON.parse(await readFile(statePath, 'utf8')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (state && (state.schemaVersion !== 1 || state.bindingId !== binding.bindingId || state.requestId !== work.requestId || state.requestSha256 !== work.snapshot.requestSha256 || state.leaseId !== work.leaseId)) throw new CliError('本机任务与服务端快照或lease不一致，保留现场。', 'helper_invalid_state');
  let receipt = state?.receipt;
  if (!receipt && state) receipt = failure(work, 'local_prepare_interrupted');
  if (!state) {
   try { await mkdir(task, { mode: 0o700 }); await restrictPermissions(task, true); } catch (error) { if (error.code === 'EEXIST') throw new CliError('任务目录已有未登记内容，保留现场。', 'helper_invalid_state'); throw error; }
   state = { schemaVersion: 1, bindingId: binding.bindingId, requestId: work.requestId, requestSha256: work.snapshot.requestSha256, leaseId: work.leaseId, status: 'preparing' }; await atomicJson(statePath, state);
   const request = join(task, 'request.json'), candidate = join(task, 'candidate'), plan = join(task, 'plan.json');
   try {
    const text = JSON.stringify(work.snapshot, null, 2) + '\n'; if (Buffer.byteLength(text) > 4 * 1024 * 1024) throw new CliError('任务快照超限。', 'helper_invalid_work'); await atomicJson(request, work.snapshot);
    const result = await runCli(binding, ['update', 'prepare', '--request', request, '--target', binding.target, '--candidate', candidate, '--source-root', binding.sourceRoot, '--plan', plan], Math.max(120000, context.timeoutMs)); const output = result.value, preview = output.plan;
    if (!['preview-ready','dependencies-required'].includes(output.status) || output.requestSha256 !== work.snapshot.requestSha256 || output.candidateDirectory !== candidate || output.planPath !== plan || preview?.targetDirectory !== binding.target || preview?.candidateDirectory !== candidate || preview?.rootNamespace !== binding.rootNamespace || preview?.projectId !== binding.projectId || !Array.isArray(preview.actions) || preview.actions.length !== 1 || preview.actions[0] !== work.snapshot.action || !HASH.test(preview.planSha256 || '') || !Array.isArray(preview.requiredActions) || preview.requiredActions.some(action => !ACTIONS.includes(action)) || preview.canApply !== (output.status === 'preview-ready') || !Array.isArray(preview.files)) throw new CliError('CLI预览回执不匹配，现场保留。', 'helper_invalid_result');
    const count = action => preview.files.filter(file => file.action === action).length;
    receipt = { bindingId: binding.bindingId, requestId: work.requestId, leaseId: work.leaseId, requestSha256: work.snapshot.requestSha256, status: output.status, planSha256: preview.planSha256, requiredActions: preview.requiredActions, createdFiles: count('create'), replacedFiles: count('replace'), deletedFiles: count('delete'), preservedFiles: count('preserve'), errorCode: null };
   } catch (error) { if (error?.localDiagnostic) await atomicJson(join(task, 'diagnostic.json'), { status: 'prepare_failed', ...error.localDiagnostic }); receipt = failure(work, 'prepare_failed'); }
  }
  await atomicJson(statePath, { ...state, status: 'prepared', receipt });
  return await finishPreview(context,binding,io,task,statePath,{...state,status:'prepared',receipt});
 } finally { await release(); }
}

/** 复用本机原锁和固定执行器完成一次明确设备关联；不会入队生成或写业务工程。 */
export async function associateGenerationDevice(context,options){
 validateDeviceOptions(options);const binding=boundProfile(await context.local());
 for(const path of [binding.target,binding.sourceRoot,binding.workRoot])await directory(path);
 const release=await lockBinding(binding);
 try{sameIdentity(binding,await context.identity());if(binding.registrationStatus==='pending')await finishRegistration(context,binding);return await bindGenerationDevice(context,binding,options,{runCli,atomicJson,safePath,sameIdentity})}finally{await release()}
}
/** 只读回查设备旧摘要，目录已移动也可用于取得明确换绑所需的ETag。 */
export async function statusGenerationDevice(context,options){
 validateDeviceOptions(options,false);const binding=boundProfile(await context.local());sameIdentity(binding,await context.identity());return generationDeviceStatus(context,binding,options);
}
