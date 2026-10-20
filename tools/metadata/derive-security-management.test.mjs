import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, mkdtempSync, mkdirSync, cpSync, rmSync, realpathSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { basename, dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { deriveSecurityManagement } from './derive-security-management.mjs';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const names=['platform','security','files'];
const bytes=names.map(name=>readFileSync(join(root,'backend/metadata',name+'.json')));
const inputs=()=>bytes.map(value=>JSON.parse(value));
const hashes=Object.fromEntries(names.map((name,index)=>[name+'Sha256',createHash('sha256').update(bytes[index]).digest('hex')]));
const derive=(values=inputs())=>deriveSecurityManagement(...values,hashes);
const pageIds=['6031906822946816','1692532858529329154','1696799309006348290'];
const tableIds=['6031906809708544','1692532858499969026','1696799308939239425'];

test('固定三页三表与原控件布局数组逐对象相等，物理基础完整且输入不改',()=>{
  const values=inputs(),before=JSON.stringify(values),result=derive(values),source=values[0];
  assert.equal(JSON.stringify(values),before); assert.equal(result.moduleId,'security-management-v1');
  assert.deepEqual(result.sources,hashes); assert.deepEqual(result.security,values[1]); assert.deepEqual(result.files,values[2]);
  for(const [key,predicate] of [['configTables',row=>tableIds.includes(row.id)],['configColumns',row=>tableIds.includes(row.configTableId)],
    ['configPages',row=>pageIds.includes(row.id)],['configPageColumns',row=>pageIds.includes(row.configPageId)]])
    assert.deepEqual(result.management[key],source[key].filter(predicate));
  assert.equal(result.management.configColumns.length,69); assert.equal(result.management.configPageColumns.length,63);
  assert.equal(result.management.configMenus.length,6); assert.equal(result.management.configButtons.length,1);
  assert.equal(result.management.configButtons[0].sql,'/admin/custom/index/init_admin');
  assert.equal(result.management.configPageColumns.find(field=>field.configColumnId==='1679698328573620226').defaultValue,'1');
  assert.deepEqual(result.pendingAdapters.map(value=>value.code),['admin-project-options','role-permission-workbench','initialize-account','security-write-runtime']);
  assert.equal(result.management.configPages.some(page=>page.configTableId==='1669251289347932162'),false);
});

test('三页、字段、菜单、按钮或后台归属冲突和固定身份漂移均拒绝',()=>{
  const changes=[p=>p.configTables.find(row=>row.id===tableIds[0]).tableName='other_manager',
    p=>p.configPages.find(row=>row.id===pageIds[0]).yzAdminTypeId='42',
    p=>p.configPageColumns.find(row=>row.configPageId===pageIds[0]).yzProjectId='42',
    p=>p.configColumns.find(row=>row.id==='1679698326770069505').yzProjectId='42',
    p=>p.configPageColumns.find(row=>row.configPageId===pageIds[0]).configColumnId='42',
    p=>p.configPageColumns.filter(row=>row.configPageId===pageIds[0])[1].id=p.configPageColumns.find(row=>row.configPageId===pageIds[0]).id,
    p=>p.configMenus.find(row=>row.id==='2026052700010000001').parentMenuId='42',
    p=>p.configCustomPages.find(row=>row.id==='2062363000000000002').pagePath='pages/new_dashboard',
    p=>p.configButtons.find(row=>row.id==='1696805746482847746').sql='/arbitrary',
    p=>p.yzAdminTypes.find(row=>row.id==='1762777514515656705').yzProjectId='42'];
  for(const change of changes){const values=inputs();change(values[0]);assert.throws(()=>derive(values));}
});

test('秘密初值与扩展连接值拒绝且错误不回显，原密码控件及开关声明保留',()=>{
  const marker='仅反例不应输出';
  for(const change of [p=>p.configColumns.find(row=>row.id==='1679698326770069505').defaultValue=marker,
    p=>p.configPageColumns.find(row=>row.id==='1679698326770069506').defaultValue=marker,
    p=>p.configTables.find(row=>row.id===tableIds[0]).connectionString=marker,
    p=>p.configColumns.find(row=>row.id==='1679698326770069505').controlDetail='Server='+marker]){
    const values=inputs();change(values[0]);assert.throws(()=>derive(values),error=>!error.message.includes(marker));
  }
  assert.ok(derive().management.configColumns.some(row=>row.columnName==='password'));
});

test('安全与文件快照漂移包含索引闭包均拒绝，不通过摘要字符串放宽',()=>{
  for(const change of [values=>values[1].configColumns[0].columnLength='999',values=>values[2].configIndexes[0].indexName='wrong_index',
    values=>values[2].configIndexColumns[0].columnName='wrong_column',values=>values[2].configTables[0].tableName='wrong_table']){
    const values=inputs();change(values);assert.throws(()=>derive(values));
  }
  assert.throws(()=>deriveSecurityManagement(...inputs(),{...hashes,clientVersion:'1'}));
});

// 沙箱仅复制公开源；清理限定系统临时目录正下方本次随机前缀，不接外部路径。
function sandbox(action){
  const directory=mkdtempSync(join(tmpdir(),'metarill-management-derive-'));
  try{
    for(const file of ['tools/metadata/derive-security-management.mjs','tools/metadata/derive-security.mjs',
      ...names.map(name=>'backend/metadata/'+name+'.json'),'backend/metadata/modules/security-management-v1.json','backend/metadata/modules/security-management-v1.sha256']){
      mkdirSync(dirname(join(directory,file)),{recursive:true});cpSync(join(root,file),join(directory,file));
    }
    action(directory);
  }finally{const actual=realpathSync(directory);assert.equal(dirname(actual).toLowerCase(),realpathSync(tmpdir()).toLowerCase());assert.ok(basename(actual).startsWith('metarill-management-derive-'));rmSync(actual,{recursive:true,force:false});}
}
const run=(directory,...args)=>execFileSync(process.execPath,[join(directory,'tools/metadata/derive-security-management.mjs'),...args],{cwd:directory,encoding:'utf8',stdio:'pipe'});

test('实际CLI两轮派生和check逐字稳定，摘要精确绑定UTF8无BOM LF资产',()=>sandbox(directory=>{
  const asset=join(directory,'backend/metadata/modules/security-management-v1.json'),sha=asset.replace('.json','.sha256'),before=readFileSync(asset),digest=readFileSync(sha);
  run(directory,'--check');run(directory);run(directory);assert.deepEqual(readFileSync(asset),before);assert.deepEqual(readFileSync(sha),digest);
  assert.equal(digest.toString(),createHash('sha256').update(before).digest('hex')+'\n');assert.equal(before.includes(13),false);assert.notEqual(before[0],0xef);
}));

test('check源头或产物漂移拒绝且不写，CLI不接版本/后台/任意输出参数',()=>sandbox(directory=>{
  const asset=join(directory,'backend/metadata/modules/security-management-v1.json'),before=readFileSync(asset);
  for(const args of [['--version','v2'],['--output','outside.json'],['--admin','42'],['--check','--check']])assert.throws(()=>run(directory,...args));
  writeFileSync(asset,Buffer.concat([before,Buffer.from('\n')]));assert.throws(()=>run(directory,'--check'));assert.deepEqual(readFileSync(asset),Buffer.concat([before,Buffer.from('\n')]));
  writeFileSync(asset,before);const path=join(directory,'backend/metadata/platform.json'),source=JSON.parse(readFileSync(path));source.configPages.find(page=>page.id===pageIds[0]).name='已变来源';writeFileSync(path,JSON.stringify(source));
  assert.throws(()=>run(directory,'--check'));assert.deepEqual(readFileSync(asset),before);
}));
