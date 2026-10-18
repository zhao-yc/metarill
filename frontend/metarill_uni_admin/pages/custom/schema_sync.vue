<template>
 <view class="schema-sync">
  <text class="heading">数据库结构预览</text>
  <text class="note">项目：{{projectId}}　范围：{{tableId || '全部受管表'}}。先预览，再确认执行，完成后重新读取数据库核验。</text>
  <view class="actions"><button size="mini" :disabled="busy" @click="preview">重新预览</button><button size="mini" type="primary" :disabled="!canApply" @click="apply">确认执行此计划</button></view>
  <text v-if="busy" class="note">{{phaseText}}</text>
  <text v-if="state.message" class="note">{{state.message}}</text><text v-if="state.error" class="error">{{state.error}}</text>
  <view v-if="state.plan" class="card">
   <text class="title">预览目标：{{state.plan.provider}} / {{state.plan.database}}</text>
   <text class="note">计划：{{state.plan.id}}　差异：{{state.plan.changes.length}} 项</text>
   <view v-for="(change,index) in state.plan.changes" :key="index" class="change"><text>{{index+1}}. {{change.kind}} · {{change.table}} {{change.column || ''}}</text><text selectable class="sql">{{change.sql}}</text></view>
  </view>
  <view v-if="state.journal" class="card"><text class="title">执行记录</text><text selectable class="sql">{{journalText}}</text></view>
  <view v-if="state.verification" class="card"><text class="title">执行后核验</text><text class="note">剩余差异：{{state.verification.changes.length}} 项；核验计划：{{state.verification.id}}。此计划不会自动执行。</text><view v-for="(change,index) in state.verification.changes" :key="index" class="change"><text>{{change.kind}} · {{change.table}}</text><text selectable class="sql">{{change.sql}}</text></view></view>
 </view>
</template>
<script>
import yzp from '@/common/yzp_runtime/yzp-runtime'
import { createSchemaSyncWorkbench } from '@/common/schemaSyncWorkbench'
/** 仅提供预览、明确确认及执行后核验；不修改原计划，也不触发循环结构同步。 */
export default {
 data() { return { projectId:'', tableId:'', state:{phase:'idle',plan:null,journal:null,verification:null,message:'',error:''} } },
 computed:{
  busy() { return ['previewing','confirming','applying','verifying'].includes(this.state.phase) },
  canApply() { return this.state.phase==='ready' && this.state.plan && this.state.plan.changes.length>0 },
  phaseText() { return {previewing:'正在读取结构差异…',confirming:'请确认执行…',applying:'正在执行计划，请勿重复操作…',verifying:'正在重新读取数据库核验…'}[this.state.phase] || '' },
  journalText() { return JSON.stringify(this.state.journal,null,2) }
 },
 onLoad(options) {
  const context = () => JSON.stringify([uni.getStorageSync('loginStateId'),uni.getStorageSync('sysParams')])
  const original = context(), params=uni.getStorageSync('sysParams') || {}
  this.projectId=String(options.projectId || params.yzProjectId || ''); this.tableId=String(options.tableId || '')
  if(this.projectId!==String(params.yzProjectId || '')) { this.state.error='当前项目已变化，请从当前项目重新打开结构预览。';return }
  try {
   this.workbench=createSchemaSyncWorkbench({projectId:this.projectId,tableId:this.tableId,isCurrent:()=>context()===original,
    request:(url,data)=>new Promise((resolve,reject)=>yzp.request({url,data,method:'POST',sendSessionId:true,showError:false,isCurrent:()=>context()===original,success:resolve,fail:()=>reject(new Error('网络请求失败，执行结果可能未知'))})),
    confirm:plan=>new Promise(resolve=>uni.showModal({title:'确认执行数据库结构计划',content:'目标：'+plan.provider+' / '+plan.database+'，共 '+plan.changes.length+' 项变更。MySQL DDL 可能部分生效，执行失败后须根据记录重新预览。',success:result=>resolve(result.confirm===true),fail:()=>resolve(false)})),
    onChange:state=>{this.state=state}})
   this.preview()
  } catch(error) { this.state.error=error.message }
 },
 onUnload() { if(this.workbench)this.workbench.dispose() },
 methods:{
  /** 显式重新预览后才允许再次确认执行。 */
  preview() { if(this.workbench)return this.workbench.preview() },
  /** 完整计划由共享状态机保存，页面展示不会改写执行内容。 */
  apply() { if(this.workbench)return this.workbench.apply() }
 }
}
</script>
<style scoped>
.schema-sync{padding:24px;max-width:1100px;margin:auto}.heading{font-size:24px;font-weight:600;display:block}.note,.error,.title{display:block;margin:12px 0}.note{color:#576575;line-height:1.6}.error{color:#c63d3d;white-space:pre-wrap}.actions{display:flex;gap:12px;justify-content:flex-start;margin:18px 0}.actions button{margin:0}.card{border:1px solid #dce3ea;border-radius:6px;padding:16px;margin:16px 0}.title{font-weight:600}.change{padding:12px 0;border-top:1px solid #eee}.sql{display:block;white-space:pre-wrap;word-break:break-all;font-family:monospace;margin-top:8px;background:#f6f8fa;padding:12px}
</style>
