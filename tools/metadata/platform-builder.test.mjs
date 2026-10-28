import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, cpSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, resolve, join, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const fixture = JSON.parse(readFileSync(join(repository, 'tools/metadata/fixtures/platform-published-declarations.json'), 'utf8'));
const canonical = JSON.parse(readFileSync(join(repository, 'backend/metadata/platform.json'), 'utf8'));
const normalize = value => Array.isArray(value) ? value.map(normalize) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, normalize(value[key])])) : value;
const hash = value => createHash('sha256').update(JSON.stringify(normalize(value))).digest('hex');

// 每次只复制统一builder输入到新临时目录，旧YZP参考路径设为不存在，CI不依赖D盘源码。
function withSandbox(action) {
  const workspace = mkdtempSync(join(tmpdir(), 'metarill-platform-builder-'));
  try {
    for (const file of ['scripts/build-platform-metadata.mjs', 'tools/metadata/derive-files.mjs',
      'backend/metadata/platform-source.json', 'backend/metadata/platform-schema-reference.json', 'backend/metadata/platform.json', 'backend/metadata/identifiers.json',
      'backend/metadata/demo.json', 'backend/metadata/files.json']) {
      mkdirSync(dirname(join(workspace, file)), { recursive: true }); cpSync(join(repository, file), join(workspace, file));
    }
    return action(workspace);
  } finally {
    // 递归清理前核实绝对真实路径仍是本次具名前缀的系统临时子目录，绝不从输入计算删除路径。
    const real = realpathSync(workspace), expectedParent = realpathSync(tmpdir());
    assert.equal(dirname(real).toLowerCase(), expectedParent.toLowerCase());
    assert.ok(basename(real).startsWith('metarill-platform-builder-')); rmSync(real, { recursive: true, force: false });
  }
}

// 实际执行复制出的builder；没有数据库或应用启动入口，输出仅用于失败诊断。
function generate(workspace) {
  execFileSync(process.execPath, [join(workspace, 'scripts/build-platform-metadata.mjs')], {
    cwd: workspace, env: { ...process.env, YZP_REFERENCE_ROOT: join(workspace, 'missing-legacy-reference') }, encoding: 'utf8',
  });
  return JSON.parse(readFileSync(join(workspace, 'backend/metadata/platform.json'), 'utf8'));
}

// 从实际重建目录中提取冻结发布依赖，hash覆盖精确关系/控件声明及4索引9字段的稳定ID与顺序。
function protectedPayload(actual) {
  const records = (collection, expected) => expected.map(reference => {
    const matched = actual[collection].filter(value => value.id === reference.id); assert.equal(matched.length, 1);
    return Object.fromEntries(Object.keys(reference).map(key => [key, matched[0][key]]));
  });
  return {
    columns: records('configColumns', fixture.payload.columns), pageColumns: records('configPageColumns', fixture.payload.pageColumns),
    indexes: records('configIndexes', fixture.payload.indexes), indexColumns: records('configIndexColumns', fixture.payload.indexColumns),
  };
}
function assertProtected(actual) {
  const payload = protectedPayload(actual); assert.deepEqual(payload, fixture.payload);
  assert.equal(hash(payload), fixture.protectedDeclarationsSha256);
}

// 原目录全量重建必须保留当前canonical所有语义；第二次还必须保持UTF8/LF字节和稳定ID清单。
test('完整builder保留已发布声明且两轮稳定，不依赖原YZP目录', () => withSandbox(workspace => {
  assertProtected(canonical); const first = generate(workspace); assert.equal(hash(first), hash(canonical), '完整metadata语义摘要必须保留'); assertProtected(first);
  const files = ['platform.json', 'identifiers.json', 'demo.json'];
  const before = files.map(file => readFileSync(join(workspace, 'backend/metadata', file)));
  assert.deepEqual(generate(workspace), first);
  files.forEach((file, index) => assert.deepEqual(readFileSync(join(workspace, 'backend/metadata', file)), before[index]));
  execFileSync(process.execPath, [join(workspace, 'tools/metadata/derive-files.mjs'), '--check'], { cwd: workspace, encoding: 'utf8' });
}));

// 负证明都真实执行错误source生成；冻结合同应捕获回退，不用字符串断言冒充生成行为。
test('丢失固定父owner声明会被发布合同发现', () => withSandbox(workspace => {
  const path = join(workspace, 'backend/metadata/platform-source.json'), source = JSON.parse(readFileSync(path, 'utf8'));
  const column = source.configColumns.find(value => value.id === '1676901024774926338');
  const detail = JSON.parse(column.controlDetail); delete detail.sourceParentOwnerColumnId; column.controlDetail = JSON.stringify(detail);
  writeFileSync(path, JSON.stringify(source, null, 2) + '\n'); assert.throws(() => assertProtected(generate(workspace)), assert.AssertionError);
}));

test('退回旧Custom标签会被发布合同发现', () => withSandbox(workspace => {
  const path = join(workspace, 'backend/metadata/platform-source.json'), source = JSON.parse(readFileSync(path, 'utf8'));
  source.configPageColumns.find(value => value.id === '1774021578289803000').controlDetail = 'ButtonVisibleRuleEditor';
  writeFileSync(path, JSON.stringify(source, null, 2) + '\n'); assert.throws(() => assertProtected(generate(workspace)), assert.AssertionError);
}));

test('全量builder丢弃既有索引会被发布合同发现', () => withSandbox(workspace => {
  const path = join(workspace, 'scripts/build-platform-metadata.mjs'), source = readFileSync(path, 'utf8');
  const declaration = "index('config_index','ix_index_table',['config_table_id','delete_no']);";
  assert.equal(source.split(declaration).length, 2); writeFileSync(path, source.replace(declaration, ''));
  assert.throws(() => assertProtected(generate(workspace)), assert.AssertionError);
}));
// 原物理补充源漏一列也必须被全目录摘要发现，不能仅保护少数关系后宣称完整自举。
test('缺失受控结构字段会被完整目录合同发现', () => withSandbox(workspace => {
  const path = join(workspace, 'backend/metadata/platform-schema-reference.json'), source = JSON.parse(readFileSync(path, 'utf8'));
  const table = source.tables.find(value => value.tableName === 'config_table');
  assert.equal(table.columns.filter(value => value.name === 'sql').length, 1);
  table.columns = table.columns.filter(value => value.name !== 'sql'); writeFileSync(path, JSON.stringify(source, null, 2) + '\n');
  assert.throws(() => assert.equal(hash(generate(workspace)), hash(canonical)), assert.AssertionError);
}));

// 缺失name/type不能被JavaScript正则隐式转成字符串后接受，结构源损坏必须在builder中明确拒绝。
test('结构声明缺字段名或类型时由builder明确拒绝', () => {
  for (const key of ['name', 'type']) withSandbox(workspace => {
    const path = join(workspace, 'backend/metadata/platform-schema-reference.json'), source = JSON.parse(readFileSync(path, 'utf8'));
    const table = source.tables.find(value => value.columns.length > 0); delete table.columns[0][key];
    writeFileSync(path, JSON.stringify(source, null, 2) + '\n');
    assert.throws(() => generate(workspace), error => error.status === 1 && String(error.stderr).includes('平台结构参考字段不合法。'));
  });
});

// 恢复控件以追加方式进入已发布目录，既有数组顺序、三个完整对象和所有固定标识都不能漂移。
test('原账号三凭据按已发布尾部顺序重建，保留完整对象和1439固定标识', () => withSandbox(workspace => {
  const restoredIds = ['1679698326770069506', '1679698328187744258', '1679698328317767682'];
  const identifiersPath = join(workspace, 'backend/metadata/identifiers.json');
  const beforeIdentifiers = JSON.parse(readFileSync(identifiersPath, 'utf8'));
  assert.equal(Object.keys(beforeIdentifiers).length, 1439);
  const rebuilt = generate(workspace);
  assert.deepEqual(rebuilt.configPageColumns.map(field => field.id), canonical.configPageColumns.map(field => field.id), '全目录页字段ID数组顺序必须保持');
  assert.deepEqual(rebuilt.configPageColumns.slice(-3).map(field => field.id), restoredIds);
  for (const id of restoredIds) {
    const actual = rebuilt.configPageColumns.filter(field => field.id === id);
    const expected = canonical.configPageColumns.filter(field => field.id === id);
    assert.equal(actual.length, 1); assert.equal(expected.length, 1);
    assert.deepEqual(actual[0], expected[0], '恢复的原控件必须保留完整声明：' + id);
  }
  for (const collection of ['configTables', 'configColumns', 'configIndexes', 'configIndexColumns']) {
    assert.deepEqual(rebuilt[collection], canonical[collection], '恢复原控件不能改变物理模型：' + collection);
  }
  const afterIdentifiers = JSON.parse(readFileSync(identifiersPath, 'utf8'));
  for (const [key, value] of Object.entries(beforeIdentifiers)) {
    assert.equal(afterIdentifiers[key], value, '既有固定标识逐值保持：' + key);
  }
  // 已登记全部四个恢复控件的原别名，重复重建不再补隐式别名或分配任何新ID。
  const added = Object.fromEntries(Object.entries(afterIdentifiers).filter(([key]) => !Object.hasOwn(beforeIdentifiers, key)));
  assert.deepEqual(added, {});
  for(const [key,id] of Object.entries({
    'pagefield:6031906822946816:1679698326770069505':'1679698326770069506',
    'pagefield:6031906822946816:1679698328187744257':'1679698328187744258',
    'pagefield:6031906822946816:1679698328317767681':'1679698328317767682',
    'pagefield:1696799309006348290:1696799603148693506':'1696799603148693507'
  }))assert.equal(afterIdentifiers[key],id);
}));

// 只修发布顺序，字段内容仍从source推导；禁止为凑摘要而从canonical复制三个对象或忽略差异。
test('原凭据声明变更仍由source生成且完整目录合同拒绝漂移', () => {
  for (const id of ['1679698326770069506', '1679698328187744258', '1679698328317767682']) withSandbox(workspace => {
    const filename = join(workspace, 'backend/metadata/platform-source.json');
    const source = JSON.parse(readFileSync(filename, 'utf8'));
    const field = source.configPageColumns.find(field => field.id === id);
    assert.equal(field.isInEdit, 1); field.isInEdit = 0;
    writeFileSync(filename, JSON.stringify(source, null, 2) + '\n');
    const rebuilt = generate(workspace);
    assert.equal(rebuilt.configPageColumns.find(field => field.id === id).isEdit, 0, '实际消费改变后的原声明');
    assert.deepEqual(rebuilt.configPageColumns.map(field => field.id), canonical.configPageColumns.map(field => field.id));
    assert.throws(() => assert.deepEqual(rebuilt.configPageColumns, canonical.configPageColumns), assert.AssertionError);
  });
});

// 原后台初始化账号的密码是配置输入控件，不是已保存登录摘要；不能被通用password名称过滤误删。
test('完整builder保留原后台初始化密码页字段及旧标识和空初值', () => withSandbox(workspace => {
  const actual=generate(workspace), field=actual.configPageColumns.filter(value=>value.id==='1696799603148693507');
  assert.equal(field.length,1,'原密码控件不能只留在旧页面，重建后必须仍有元数据');
  assert.equal(field[0].configPageId,'1696799309006348290');assert.equal(field[0].configColumnId,'1696799603148693506');
  assert.equal(field[0].name,'密码');assert.equal(field[0].isShow,1);assert.equal(field[0].isEdit,1);assert.equal(field[0].defaultValue,'');
  assert.deepEqual(actual.configPageColumns.slice(-4).map(value=>value.id),['1696799603148693507','1679698326770069506','1679698328187744258','1679698328317767682']);
  assert.deepEqual(actual.configPageColumns.filter(value=>value.id!==field[0].id).map(value=>value.id),canonical.configPageColumns.filter(value=>value.id!==field[0].id).map(value=>value.id));
  for(const key of ['configTables','configColumns','configIndexes','configIndexColumns'])assert.deepEqual(actual[key],canonical[key],'恢复页面声明不能改变物理模型');
}));

test('原初始化密码页字段缺失或身份及秘密初值漂移不能补成任意新控件', () => {
  for(const kind of ['missing','id','default'])withSandbox(workspace=>{
    const path=join(workspace,'backend/metadata/platform-source.json'),source=JSON.parse(readFileSync(path,'utf8'));
    const field=source.configPageColumns.find(value=>value.id==='1696799603148693507');assert.ok(field);
    if(kind==='missing')source.configPageColumns=source.configPageColumns.filter(value=>value!==field);
    if(kind==='id')field.id='9007199254740993';if(kind==='default')field.defaultValue='不得公开的初值';
    writeFileSync(path,JSON.stringify(source,null,2)+'\n');assert.throws(()=>generate(workspace));
  });
});

// 实际重建后数据库关联与目标主键同型；原控件和稳定ID不随类型优化改变。
test('固定六个配置关联使用int64且不改写不透明字符串标识', () => withSandbox(workspace => {
 const rebuilt=generate(workspace);
 for(const [table,name,id,target] of [
  ['config_child','child_config_page_id','1679391146044956674','config_page'],
  ['config_column','link_config_table_id','1670726646479638529','config_table'],
  ['config_column','link_config_page_id','1670726646546747393','config_page'],
  ['config_page_column','link_config_table_id','2200000000000000135','config_table'],
  ['config_page_column','link_config_page_id','2200000000000000137','config_page'],
  ['config_page_column','combine_super_column_id','2200000000000000139','config_column'],
 ]) {
  const owner=rebuilt.configTables.find(row=>row.tableName===table);const field=rebuilt.configColumns.find(row=>row.id===id);
  const targetTable=rebuilt.configTables.find(row=>row.tableName===target);const key=rebuilt.configColumns.find(row=>row.configTableId===targetTable.id&&row.columnName==='id');
  assert.equal(field.configTableId,owner.id);assert.equal(field.columnName,name);assert.equal(field.logicalType,key.logicalType);assert.equal(field.logicalType,'int64');assert.equal(field.columnType,'int');assert.equal(field.columnLength,'');
 }
 for(const name of ['client_id','request_id'])assert.equal(rebuilt.configColumns.find(row=>row.columnName===name).logicalType,'string');
 assert.deepEqual(rebuilt.configPageColumns,canonical.configPageColumns);assert.deepEqual(rebuilt.configMenus,canonical.configMenus);
}));

// 不能把被挪用的字段身份或错误目标自动归一成可信关联，必须在写出之前拒绝。
test('固定配置关联身份或目标漂移在builder中明确拒绝', () => {
 for(const change of ['identity','target'])withSandbox(workspace=>{
  const filename=join(workspace,'backend/metadata/platform-source.json'),source=JSON.parse(readFileSync(filename,'utf8'));
  const field=source.configColumns.find(row=>row.id==='1679391146044956674');assert.ok(field);
  if(change==='identity')field.id='9007199254740993';else field.joinTable='yz_manager';
  writeFileSync(filename,JSON.stringify(source,null,2)+'\n');
  assert.throws(()=>generate(workspace),error=>error.status===1&&String(error.stderr).includes('固定配置关联ID身份或目标声明变化'));
 });
});
