import { readFile, readdir, lstat } from 'node:fs/promises';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { CliError } from './errors.mjs';
import { validateRuntimeOrigin,validateRuntimeSnapshot,validateRuntimePlan } from './generation-runtime-scope.mjs';
import { verifyGenerationRuntimeDevice } from './generation-device.mjs';

const UUID = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;
const HASH = /^[a-f0-9]{64}$/;
const ACTIONS = ['generate-project-config', 'sync-runtime', 'generate-all-page'];
const equal = (left, right) => JSON.stringify(left) === JSON.stringify(right);
const proof = work => ({ requestId: work.requestId, action: work.snapshot.action, requestSha256: work.snapshot.requestSha256, metadataSha256: work.snapshot.metadataSha256 });
const terminal = receipt => ({ status: receipt.status.replaceAll('-', '_'), applied: receipt.status === 'applied', outcome: receipt.status === 'applied' ? 'applied' : receipt.status === 'rolled-back' ? 'rolled_back' : 'unknown' });

/** 应用回执只有本机结果摘要；错误和未知状态绝不伪装为应用成功。 */
function receipt(state, status, errorCode = null, counts = {}) {
 return { bindingId: state.bindingId, groupId: state.groupId, ticketSha256: state.ticket.ticketSha256, planSha256: state.authorization.planSha256, status, writtenFiles: counts.writtenFiles || 0, deletedFiles: counts.deletedFiles || 0, preservedFiles: counts.preservedFiles || 0, errorCode };
}
/** 固定plan必须恰好对应真实确认动作并集；禁止在本机偷偷追加scope。 */
function planValid(plan, binding, state) {
 validateRuntimePlan(plan,state.runtimeOrigin);
 if (!plan || plan.targetDirectory !== binding.target || plan.candidateDirectory !== state.candidate || plan.projectId !== binding.projectId || plan.rootNamespace !== binding.rootNamespace || !equal(plan.actions, state.authorization.actions) || !HASH.test(plan.planSha256 || '') || plan.planSha256 !== state.authorization.planSha256 || !plan.canApply || !Array.isArray(plan.requiredActions) || plan.requiredActions.length || !Array.isArray(plan.files)) throw new CliError('应用计划与确认集合或本机归属不一致，保留现场。', 'helper_invalid_plan');
}
/** ticket只认可本机刚请求的UUID、原请求hash、同快照和短时限，不接任何服务器路径。 */
function ticketValid(ticket, state, requireFresh = true) {
 const expected = state.approvals;
 if (!equal(ticket?.runtimeOrigin??null,state.runtimeOrigin??null))throw new CliError('ticket分端来源与原任务不一致。','helper_invalid_ticket');
 if (!ticket || ticket.groupId !== state.groupId || ticket.bindingId !== state.bindingId || ticket.metadataSha256 !== state.authorization.metadataSha256 || ticket.planSha256 !== state.authorization.planSha256 || !HASH.test(ticket.ticketSha256 || '') || !equal(ticket.actions, state.authorization.actions) || !equal(ticket.approvals, expected) || !Number.isFinite(Date.parse(ticket.expiresAt)) || requireFresh && Date.parse(ticket.expiresAt) <= Date.now()) throw new CliError('应用ticket与本机已确认计划不一致或已过期。', 'helper_invalid_ticket');
}
/** 原事务恢复只使用固定C#CLI对本机已确认计划反查的ID；不从网络领取恢复路径或transaction。 */
async function inspectOrRecover(context, binding, io, state) {
 await io.safePath(state.planPath);
 const args = ['--target', binding.target, '--plan', state.planPath, '--confirm', state.authorization.planSha256];
 try {
  let status = (await io.runCli(binding, ['update','apply-status',...args], 120000)).value;
  if (status.planSha256 !== state.authorization.planSha256 || !['unknown','committed','rolled-back','recovery-required'].includes(status.status)) throw new CliError('原事务状态未知。', 'helper_recovery_unknown');
  if (status.status === 'recovery-required') {
   if (!/^[a-f0-9]{32}$/.test(status.transactionId || '')) throw new CliError('原事务身份无效。', 'helper_recovery_unknown');
   const recovered = (await io.runCli(binding, ['update','recover','--target',binding.target,'--transaction',status.transactionId,'--confirm',state.authorization.planSha256], 120000)).value;
   if (recovered.planSha256 !== state.authorization.planSha256 || !['committed','rolled-back'].includes(recovered.status)) throw new CliError('原事务恢复结果未知。', 'helper_recovery_unknown');
   status = (await io.runCli(binding, ['update','apply-status',...args], 120000)).value;
  }
  if (status.status === 'committed') { const plan = JSON.parse(await readFile(state.planPath, 'utf8')); planValid(plan, binding, state); return receipt(state, 'applied', null, { writtenFiles: plan.files.filter(file => ['create','replace'].includes(file.action)).length, deletedFiles: plan.files.filter(file => file.action === 'delete').length, preservedFiles: plan.files.filter(file => file.action === 'preserve').length }); }
  if (status.status === 'rolled-back') return receipt(state, 'rolled-back', 'application_rolled_back');
 } catch (error) { if (error.localDiagnostic) await io.atomicJson(join(state.task, 'application-diagnostic.json'), error.localDiagnostic); }
 return receipt(state, 'manual-recovery', 'application_unknown');
}
/** 应用意图先刷盘；异常只检查/恢复同一个原事务，绝不重跑apply。 */
async function execute(context, binding, io, state, statePath) {
 if(state.runtimeOrigin&&['authorizing','authorized'].includes(state.phase))await verifyGenerationRuntimeDevice(binding,io,state.runtimeOrigin);
 if (state.phase === 'authorizing') {
  const ticket = await context.rpc('/admin/sys/generation-assistant/helper/authorize', state.authorization); ticketValid(ticket, state);
  state = { ...state, ticket, phase: 'authorized' }; await io.atomicJson(statePath, state);
 }
 if (state.phase === 'authorized') {
  ticketValid(state.ticket, state);
  const ticket = await context.rpc('/admin/sys/generation-assistant/helper/verify-ticket', { bindingId: binding.bindingId, groupId: state.groupId, ticketSha256: state.ticket.ticketSha256, planSha256: state.authorization.planSha256 }); ticketValid(ticket, state);
  if (!equal(ticket, state.ticket)) throw new CliError('应用前ticket已变化。', 'helper_invalid_ticket');
  await io.safePath(state.planPath); const plan = JSON.parse(await readFile(state.planPath, 'utf8')); planValid(plan, binding, state);
  if(state.runtimeOrigin)await verifyGenerationRuntimeDevice(binding,io,state.runtimeOrigin);
  // ticket有效且固定action策略已复核后，只有这一个位置进入原C#Apply。
  state = { ...state, phase: 'applying' }; await io.atomicJson(statePath, state);
  try {
   const result = (await io.runCli(binding, ['update','apply','--target',binding.target,'--candidate',state.candidate,'--plan',state.planPath,'--confirm',state.authorization.planSha256], 120000)).value;
   if (result.planSha256 !== state.authorization.planSha256 || !['committed','unchanged'].includes(result.status) || ![result.writtenFiles,result.deletedFiles,result.preservedFiles].every(count => Number.isInteger(count) && count >= 0 && count <= 20000)) throw new CliError('应用返回未知结果。', 'helper_recovery_unknown');
   state.receipt = receipt(state, 'applied', null, result);
  } catch (error) { if (error.localDiagnostic) await io.atomicJson(join(state.task, 'application-diagnostic.json'), error.localDiagnostic); state.receipt = await inspectOrRecover(context, binding, io, state); }
 } else if (state.phase === 'applying') state.receipt = await inspectOrRecover(context, binding, io, state);
 if (state.receipt) { state = { ...state, phase: 'reporting' }; await io.atomicJson(statePath, state); }
 if (state.phase === 'reporting') {
  const reported = await context.rpc('/admin/sys/generation-assistant/helper/application-report', state.receipt);
  if (!equal(reported, state.receipt)) throw new CliError('服务器尚未确认本机应用回执，出箱保留。', 'helper_invalid_receipt');
  await io.atomicJson(statePath, { ...state, phase: 'reported' }); return { requestId: state.requestId, ...terminal(state.receipt) };
 }
 return null;
}
/** 启动先恢复本绑定已持久应用；只有原事务恢复可先于在线认证，新应用仍必须fresh identity+verify-ticket。 */
export async function resumeApplications(context, binding, io, recoveryOnly = false) {
 if (binding.mode !== 'apply') return null;
 const entries = await readdir(binding.workRoot, { withFileTypes: true }); if (entries.length > 2000) throw new CliError('助手恢复扫描超过上限。', 'helper_invalid_state');
 for (const entry of entries.filter(entry => UUID.test(entry.name)).sort((a,b) => a.name.localeCompare(b.name))) {
  const task = join(binding.workRoot, entry.name); await io.safePath(task); if (!entry.isDirectory()) throw new CliError('助手任务目录类型无效。', 'helper_invalid_state');
  const statePath = join(task, 'application.json'); let state;
  try { await io.safePath(statePath); const info = await lstat(statePath); if (!info.isFile() || info.size > 131072) throw new CliError('应用状态无效。', 'helper_invalid_state'); state = JSON.parse(await readFile(statePath, 'utf8')); } catch(error) { if (error.code === 'ENOENT') continue; throw error; }
  if (state.schemaVersion !== (state.runtimeOrigin?2:1) || state.bindingId !== binding.bindingId || state.localBindingSha256 !== binding.localBindingSha256 || state.requestId !== entry.name || state.task !== task || state.candidate !== join(task,'candidate') || state.planPath !== join(task,state.runtimeOrigin?'plan.json':'application-plan.json') || !UUID.test(state.groupId || '') || !state.authorization || state.authorization.groupId !== state.groupId || state.authorization.bindingId !== binding.bindingId || !equal(state.authorization.actions,[...state.authorization.actions].sort()) || state.authorization.actions.some(action => !binding.allowedActions.includes(action)) || !['authorizing','authorized','applying','reporting','reported'].includes(state.phase)) throw new CliError('持久应用与本机绑定不一致，保留现场。', 'helper_invalid_state');
  if(state.runtimeOrigin){validateRuntimeOrigin(state.runtimeOrigin,binding);if(state.authorization.actions.length!==1||state.authorization.actions[0]!=='sync-runtime'||state.approvals?.length!==1||state.approvals[0].requestId!==state.requestId)throw new CliError('持久分端应用确认范围无效。','helper_invalid_state')}
  if (state.phase === 'reported') continue;
  if (recoveryOnly && !['applying','reporting'].includes(state.phase)) continue;
  if (state.ticket) ticketValid(state.ticket, state, false);
  return execute(context, binding, io, state, statePath);
 }
 return null;
}
/** 只合并同hash下已经由用户分别确认的动作；缺依赖留在原按钮，不发隐式all。 */
export async function attemptApplication(context, binding, io, work, task, preview) {
 if (binding.mode !== 'apply') return null;
 validateRuntimeSnapshot(work.snapshot,work.runtimeOrigin,binding);
 if(work.runtimeOrigin)return attemptRuntimeApplication(context,binding,io,work,task,preview);
 const received = await context.rpc('/admin/sys/generation-assistant/helper/approvals', { bindingId: binding.bindingId, metadataSha256: work.snapshot.metadataSha256 });
 if (!Array.isArray(received) || received.length > 3 || received.some(item => !UUID.test(item.requestId || '') || !ACTIONS.includes(item.action) || !HASH.test(item.requestSha256 || '') || item.metadataSha256 !== work.snapshot.metadataSha256) || new Set(received.map(item => item.action)).size !== received.length) throw new CliError('确认集合身份无效。', 'helper_invalid_work');
 const approvals = [...received.filter(item => item.action !== work.snapshot.action), proof(work)].filter(item => binding.allowedActions.includes(item.action)).sort((left,right) => left.action.localeCompare(right.action));
 const actions = approvals.map(item => item.action);
 if (!actions.includes(work.snapshot.action) || preview.requiredActions.some(action => !actions.includes(action))) return { requestId: work.requestId, status: 'dependencies_required', requiredActions: preview.requiredActions, applied: false, outcome: 'not_applied' };
 const planPath = join(task,'application-plan.json'); const candidate = join(task,'candidate');
 let plan; await io.safePath(planPath);
 try { const info=await lstat(planPath); if(!info.isFile()||info.size>4*1024*1024) throw new CliError('已有组计划无效。','helper_invalid_plan'); plan=JSON.parse(await readFile(planPath,'utf8')); if(!equal(plan.actions,actions)) throw new CliError('已有组计划动作不同，保留现场。','helper_invalid_plan'); } catch(error) { if(error.code!=='ENOENT') throw error; plan=(await io.runCli(binding,['update','preview','--target',binding.target,'--candidate',candidate,'--actions',actions.join(','),'--plan',planPath],120000)).value; }
 if (!plan.canApply || plan.requiredActions?.length) return { requestId: work.requestId, status: 'dependencies_required', requiredActions: plan.requiredActions, applied: false, outcome: 'not_applied' };
 const groupId = randomUUID(); const authorization = { bindingId: binding.bindingId, groupId, metadataSha256: work.snapshot.metadataSha256, requestIds: approvals.map(item => item.requestId).sort(), actions, planSha256: plan.planSha256 };
 const state = { schemaVersion: 1, bindingId: binding.bindingId, localBindingSha256: binding.localBindingSha256, requestId: work.requestId, groupId, task, candidate, planPath, approvals, authorization, phase: 'authorizing' };
 planValid(plan, binding, state); const statePath = join(task,'application.json'); await io.atomicJson(statePath,state);
 return execute(context,binding,io,state,statePath);
}

/** 分端不请求/合并旧approvals，不重新preview；只读取本次prepare落盘并已回传摘要的原plan.json。 */
async function attemptRuntimeApplication(context,binding,io,work,task,preview){
 await verifyGenerationRuntimeDevice(binding,io,work.runtimeOrigin);
 if(preview.requiredActions.length)return {requestId:work.requestId,status:'dependencies_required',requiredActions:preview.requiredActions,applied:false,outcome:'not_applied'};
 const planPath=join(task,'plan.json'),candidate=join(task,'candidate');await io.safePath(planPath);const info=await lstat(planPath);
 if(!info.isFile()||info.size>4*1024*1024)throw new CliError('原分端准备计划无效，保留现场。','helper_invalid_plan');
 const plan=JSON.parse(await readFile(planPath,'utf8'));const approvals=[proof(work)],actions=['sync-runtime'];
 if(plan.planSha256!==preview.planSha256)throw new CliError('原分端准备计划摘要已变化，不能重新预览替代。','helper_invalid_plan');
 const groupId=randomUUID();const authorization={bindingId:binding.bindingId,groupId,metadataSha256:work.snapshot.metadataSha256,requestIds:[work.requestId],actions,planSha256:plan.planSha256};
 const state={schemaVersion:2,runtimeOrigin:work.runtimeOrigin,bindingId:binding.bindingId,localBindingSha256:binding.localBindingSha256,requestId:work.requestId,groupId,task,candidate,planPath,approvals,authorization,phase:'authorizing'};
 planValid(plan,binding,state);const statePath=join(task,'application.json');await io.atomicJson(statePath,state);return execute(context,binding,io,state,statePath);
}
