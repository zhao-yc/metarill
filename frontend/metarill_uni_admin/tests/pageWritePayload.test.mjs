import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile, mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { mergePageWriteSchemas, writeMergedSchema } from '../scripts/merge-page-write-schemas.mjs';

const source = await readFile(new URL('../common/yzp_runtime/pageWritePayload.js', import.meta.url), 'utf8');
const { createPageWriteFilter } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const page = { route: '/admin/auto/sample/42', createFields: ['name','notes'], updateFields: ['id','name','notes'] };
const schema = { schemaVersion: 1, pages: { 42: page } };

/** 直接验证字段存在性及对象隔离；白名单不从实体当前值猜测。 */
test('创建去掉ID、只读、审计及辅助字段，不为缺失字段补null', () => {
 const filter = createPageWriteFilter(schema);
 const entity = Object.freeze({ id: '不能自定主键', name: '对象', amount: 88, insertTime: '系统值', deleteNo: null, displayName: '展示' });
 assert.deepEqual(filter(page.route + '/add', 'POST', entity), { name: '对象' });
 assert.equal(Object.hasOwn(filter(page.route + '/add', 'POST', entity), 'notes'), false);
 assert.equal(entity.amount, 88);
});

test('更新只复制自有字段，显式null、false、零与空字符串不被吞掉', () => {
 const filter = createPageWriteFilter(schema);
 for (const value of [null, false, 0, '']) assert.deepEqual(filter(page.route + '/update?parentId=8', 'post', { id: '9', name: value }), { id: '9', name: value });
 const inherited = Object.create({ notes: '原型上的值' }); inherited.id='9'; inherited.name='实际值';
 assert.deepEqual(filter(page.route + '/update', 'POST', inherited), { id: '9', name: '实际值' });
});

test('未知路由、相似路径和未登记方法不作裁剪，非对象请求体交服务端拒绝', () => {
 const filter = createPageWriteFilter(schema), entity={ id:'9', unknown:'保留' };
 for (const [url,method] of [['/admin/custom/sample/update','POST'], ['/admin/auto/other/42/add','POST'], [page.route+'/add/extra','POST'], [page.route+'/add','GET'], [page.route+'/add','DELETE'],[page.route,'POST'],['/admin/sys/file-library/42','DELETE']]) assert.equal(filter(url,method,entity), entity);
 for (const body of [null, [], '正文', 0]) assert.equal(filter(page.route+'/add','POST',body),body);
});

test('合并清单按页面和字段排序，来源顺序不影响结果，冲突或损坏定义直接失败', () => {
 const one={source:'平台',schema}, two={source:'示例',schema:{schemaVersion:1,pages:{7:{route:'/admin/auto/other/7',createFields:['z','a'],updateFields:['id','a','z']}}}};
 assert.deepEqual(mergePageWriteSchemas([one,two]),mergePageWriteSchemas([two,one]));
 assert.deepEqual(mergePageWriteSchemas([one,one]).pages['42'].createFields,['name','notes']);
 assert.throws(()=>mergePageWriteSchemas([one,{source:'冲突',schema:{schemaVersion:1,pages:{42:{...page,createFields:['name']}}}}]),/冲突/);
 assert.throws(()=>mergePageWriteSchemas([{source:'损坏',schema:{schemaVersion:2,pages:{}}}]),/版本/);
 assert.throws(()=>mergePageWriteSchemas([{source:'损坏',schema:{schemaVersion:1,pages:{42:{...page,route:'/admin/custom/sample/42'}}}}]),/路由/);
});

/** 使用临时生成目录验证命令真实读写与重复执行稳定性。 */
test('可复现脚本同时读取platform/demo，重复执行不重写相同字节', async () => {
 const root = await mkdtemp(join(tmpdir(),'metarill-write-schema-'));
 try {
  for (const project of ['platform','demo']) { const directory=join(root,'backend/generated',project,'frontend/common/yzp_runtime'); await mkdir(directory,{recursive:true}); await writeFile(join(directory,'page-write-schema.generated.json'),JSON.stringify(schema)); }
  const output=join(root,'merged.json');
  const first=await writeMergedSchema({sourceRoot:root,output}), body=await readFile(output,'utf8'), second=await writeMergedSchema({sourceRoot:root,output});
  assert.equal(first.pages,1); assert.equal(first.changed,true); assert.equal(second.changed,false); assert.equal(second.sha256,first.sha256); assert.equal(await readFile(output,'utf8'),body);
  await rm(join(root,'backend/generated/demo'),{recursive:true,force:true});
  await assert.rejects(writeMergedSchema({sourceRoot:root,output}),/先生成/);
 } finally { await rm(root,{recursive:true,force:true}); }
});

/** 删除延续原界面交互，载荷在标准请求边界转换为严格标识 DTO。 */
test('标准删除只保留自有ID，移除实体、常量展示和系统字段且不修改原对象', () => {
 const filter = createPageWriteFilter(schema);
 const entity = Object.freeze({ id:'9007199254740993', name:'记录', statusShow:'启用', insertManagerId:'系统账号' });
 assert.deepEqual(filter(page.route+'?parentId=8', 'delete', entity), { id:'9007199254740993' });
 assert.equal(entity.statusShow,'启用');
 assert.deepEqual(filter(page.route,'DELETE',{name:'缺失ID'}),{});
 assert.deepEqual(filter(page.route,'DELETE',Object.create({id:'原型ID'})),{});
 for (const id of [null,0,'']) assert.deepEqual(filter(page.route,'DELETE',{id,name:'记录'}),{id});
 for (const data of [null,[],'正文']) assert.equal(filter(page.route,'DELETE',data),data);
});
