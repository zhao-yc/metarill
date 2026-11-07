import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
const source=await readFile(new URL('../../../frontend/metarill_uni_admin/common/yzp_runtime/buttonVisibleRule.js',import.meta.url),'utf8')
const module=source.replace(/^import .*$/gm,'').replace(/export\s*\{[\s\S]*?\}\s*$/,'return {evaluateRule};')
const {evaluateRule}=new Function('utils',module)({isBlank:value=>value==null||typeof value==='string'&&!value.trim()})
const rule=JSON.parse(process.argv[2]),fields=JSON.parse(process.argv[3])
const full={id:'9007199254740993',name:'原列表',flag:'queryKey',status:1,statusShow:'secret',remark:'仅详情大文本',secret:'不应携带',queryKey:'仅查询条件'}
const narrow=Object.fromEntries(fields.map(key=>[key,full[key]]))
assert(evaluateRule(rule,full));assert(evaluateRule(rule,narrow));assert(!('secret' in narrow));assert(!('queryKey' in narrow))
const missing={...narrow};delete missing.flag;assert.equal(evaluateRule(rule,missing),false)
console.log('通过：原按钮条件runtime在窄投影中保持行为，value/valueList不转成读取列')
