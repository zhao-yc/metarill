import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, mkdtempSync, mkdirSync, cpSync, rmSync, realpathSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { basename, dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { deriveSecurity } from './derive-security.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const source = JSON.parse(readFileSync(join(root, 'backend/metadata/platform.json'), 'utf8'));
const fresh = () => structuredClone(source);
const managerId = '6031906809708544';
const managerPassword = '1679698326770069505';
const requiredTables = ['yz_project', 'yz_admin_type', 'yz_manager', 'yz_role', 'yz_role_permission', 'sys_session'];
const physicalColumnKeys = ['id', 'configTableId', 'columnName', 'logicalType', 'columnLength', 'isDbNotNull', 'isVirtualColumn', 'precision', 'scale', 'defaultValue'];

// 只比较公开物理快照，测试不读取账号、连接或任何数据库行。
test('派生六表完整物理闭包且不修改平台源，不传播控件和配置扩展', () => {
  const input = fresh(), before = JSON.stringify(input), actual = deriveSecurity(input);
  assert.equal(JSON.stringify(input), before);
  assert.deepEqual(actual.configTables.map(table => table.tableName).sort(), requiredTables.toSorted());
  const tableIds = new Set(actual.configTables.map(table => table.id));
  const expected = source.configColumns.filter(column => tableIds.has(column.configTableId) && (column.isVirtualColumn ?? 0) === 0);
  assert.equal(actual.configColumns.length, expected.length);
  for (const column of actual.configColumns) {
    assert.deepEqual(Object.keys(column), physicalColumnKeys);
    const original = expected.find(row => row.id === column.id);
    for (const key of physicalColumnKeys) assert.equal(column[key], original[key] ?? ({ isVirtualColumn: 0, precision: 18, scale: 2 })[key]);
  }
  for (const table of actual.configTables) assert.deepEqual(Object.keys(table), ['id', 'tableName', 'isFunctionTable', 'isView']);
  for (const key of ['configPages', 'configPageColumns', 'yzAdminTypes', 'configMenus', 'configCustomPages', 'configButtons', 'configChildren']) assert.deepEqual(actual[key], []);
  assert.equal(actual.configIndexes.length, 5); assert.equal(actual.configIndexColumns.length, 8);
  assert.ok(!JSON.stringify(actual).includes('sys_agent'));
});

test('六张固定表任意缺失均拒绝，不能用未受信表名或另一个ID替代', () => {
  for (const name of requiredTables) {
    const input = fresh(); input.configTables = input.configTables.filter(table => table.tableName !== name);
    assert.throws(() => deriveSecurity(input), /固定安全基础表/);
  }
  for (const change of [table => table.tableName = 'sys_agent_grant', table => table.id = '42', table => table.isView = 1]) {
    const input = fresh(); change(input.configTables.find(table => table.id === managerId));
    assert.throws(() => deriveSecurity(input), /固定安全基础表|表名重复/);
  }
});

test('平台无关表不能进入闭包，所有关系和配置内容都不传播', () => {
  const input = fresh(), marker = '不得下发的配置占位';
  input.configTables.find(table => table.id === managerId).connectionString = marker;
  const password = input.configColumns.find(column => column.id === managerPassword);
  for (const key of ['controlKind', 'controlDetail', 'initValue', 'queryDefault', 'whereSql', 'orderBy', 'remarks', 'linkConfigTableId']) password[key] = marker;
  input.configTables.push({ id: '42', tableName: 'untrusted_extra', isFunctionTable: 0, isView: 0 });
  input.configColumns.push({ id: '43', configTableId: '42', columnName: 'secret', logicalType: 'string', defaultValue: marker });
  const actual = deriveSecurity(input);
  assert.equal(actual.configTables.length, 6); assert.ok(!JSON.stringify(actual).includes(marker));
  assert.ok(!actual.configColumns.some(column => column.id === '43'));
});

test('各表必要主键或安全关联字段缺失、虚拟化或类型漂移拒绝', () => {
  const columns = ['2200000000000000172', '1696799602636988418', managerPassword, '1692533179573940226', '2063264600941453314', '2200000000000000318'];
  for (const id of columns) {
    const input = fresh(); input.configColumns = input.configColumns.filter(column => column.id !== id);
    assert.throws(() => deriveSecurity(input), /必要字段/);
  }
  for (const change of [column => column.id = '42', column => column.logicalType = 'json', column => column.isVirtualColumn = 1, column => column.columnLength = '50']) {
    const input = fresh(); change(input.configColumns.find(column => column.id === managerPassword));
    assert.throws(() => deriveSecurity(input), /必要字段/);
  }
  const input = fresh(); input.configColumns = input.configColumns.filter(column => !(column.configTableId === managerId && column.columnName === 'update_time'));
  assert.throws(() => deriveSecurity(input), /必要审计字段/);
});

test('表名、列名以及跨类别全局ID重复都不能被过滤或去重掩盖', () => {
  const mutations = [
    input => input.configTables.push(structuredClone(input.configTables.find(table => table.id === managerId))),
    input => input.configColumns.push({ ...input.configColumns.find(column => column.id === managerPassword), id: '42' }),
    input => input.configColumns.find(column => column.id === managerPassword).id = managerId,
    input => input.configIndexes.push(structuredClone(input.configIndexes[0])),
    input => input.configIndexColumns.push(structuredClone(input.configIndexColumns[0])),
    input => input.configTables.push({ id: '42', tableName: 'YZ_MANAGER', isFunctionTable: 0, isView: 0 }),
  ];
  for (const change of mutations) { const input = fresh(); change(input); assert.throws(() => deriveSecurity(input), /重复|冲突/); }
});

test('连接及凭据物理默认值拒绝且错误不回显原值', () => {
  for (const name of ['password', 'salt', 'old_password', 'mysql_conn_str', 'db_url', 'db_username', 'db_password', 'token_hash']) {
    const input = fresh(), marker = '此处仅测试禁止透出';
    const column = input.configColumns.find(column => column.columnName === name && requiredTables.some(tableName => input.configTables.some(table => table.tableName === tableName && table.id === column.configTableId)));
    column.defaultValue = marker;
    assert.throws(() => deriveSecurity(input), error => error.message === '连接或凭据字段不允许非空物理默认值。' && !error.message.includes(marker));
  }
});

test('必要索引缺失、禁用、换标识、唯一性及列顺序漂移拒绝', () => {
  for (const index of deriveSecurity(source).configIndexes) {
    const input = fresh(); input.configIndexes = input.configIndexes.filter(row => row.id !== index.id);
    assert.throws(() => deriveSecurity(input), /必要索引/);
  }
  const mutations = [
    input => input.configIndexes.find(index => index.id === '2200000000000000734').isEnabled = 0,
    input => input.configIndexes.find(index => index.id === '2200000000000000734').id = '42',
    input => Object.assign(input.configIndexes.find(index => index.id === '2200000000000000734'), { isUnique: 0, indexType: 'INDEX' }),
    input => input.configIndexColumns.find(field => field.id === '900235310800048128').sortNo = 20,
    input => input.configIndexColumns.find(field => field.id === '900235310800048128').id = '42',
  ];
  for (const change of mutations) { const input = fresh(); change(input); assert.throws(() => deriveSecurity(input), /安全基础.*索引/); }
});

test('索引跨表引用、未知字段、重复排序和降序不能静默扩展安全闭包', () => {
  const mutations = [
    field => field.configColumnId = managerPassword,
    field => field.configColumnId = '42',
    field => field.columnName = 'password',
    field => field.sortNo = 10,
    field => field.sortDirection = 'DESC',
  ];
  for (const change of mutations) {
    const input = fresh(); change(input.configIndexColumns.find(field => field.id === '900235310800048128'));
    assert.throws(() => deriveSecurity(input), /索引字段/);
  }
});

test('新增合法物理字段可派生，非法属性和必要逻辑删除约束拒绝', () => {
  const input = fresh(), column = { ...input.configColumns.find(column => column.id === managerPassword), id: '42', columnName: 'future_label', defaultValue: '固定普通名称' };
  input.configColumns.push(column);
  assert.equal(deriveSecurity(input).configColumns.find(row => row.id === '42').defaultValue, '固定普通名称');
  for (const change of [column => column.logicalType = 'raw_sql', column => column.columnLength = '255);DROP', column => column.precision = 0, column => column.precision = null, column => column.columnName = undefined, column => column.isVirtualColumn = 2, column => column.defaultValue = 'a\\b']) {
    const bad = structuredClone(input); change(bad.configColumns.find(column => column.id === '42'));
    assert.throws(() => deriveSecurity(bad), /安全基础/);
  }
  const bad = fresh(); bad.configColumns.find(column => column.id === '2200000000000000008').isDbNotNull = 1;
  assert.throws(() => deriveSecurity(bad), /逻辑删除/);
});

// 临时目录只含公开JSON及脚本；清理前核实当前随机目录仍在系统临时目录正下方。
function withSandbox(action) {
  const workspace = mkdtempSync(join(tmpdir(), 'metarill-security-derive-'));
  try {
    for (const file of ['tools/metadata/derive-security.mjs', 'backend/metadata/platform.json', 'backend/metadata/security.json']) {
      mkdirSync(dirname(join(workspace, file)), { recursive: true }); cpSync(join(root, file), join(workspace, file));
    }
    action(workspace);
  } finally {
    const actual = realpathSync(workspace);
    assert.equal(dirname(actual).toLowerCase(), realpathSync(tmpdir()).toLowerCase());
    assert.ok(basename(actual).startsWith('metarill-security-derive-')); rmSync(actual, { recursive: true, force: false });
  }
}
const run = (workspace, ...args) => execFileSync(process.execPath, [join(workspace, 'tools/metadata/derive-security.mjs'), ...args], { cwd: workspace, encoding: 'utf8', stdio: 'pipe' });

test('实际CLI核验提交文件，跨轮派生保持UTF8无BOM和LF稳定字节', () => withSandbox(workspace => {
  run(workspace, '--check');
  const path = join(workspace, 'backend/metadata/security.json'), before = readFileSync(path);
  run(workspace); assert.deepEqual(readFileSync(path), before);
  run(workspace); assert.deepEqual(readFileSync(path), before);
  assert.ok(!before.includes(13)); assert.notEqual(before[0], 0xef);
}));

test('check发现源头物理变更及产物漂移，不修改文件，CLI不接表名或任意路径', () => withSandbox(workspace => {
  const path = join(workspace, 'backend/metadata/security.json'), before = readFileSync(path);
  const platformPath = join(workspace, 'backend/metadata/platform.json'), input = fresh();
  input.configColumns.find(column => column.configTableId === managerId && column.columnName === 'notes').columnLength = '100';
  writeFileSync(platformPath, JSON.stringify(input));
  assert.throws(() => run(workspace, '--check')); assert.deepEqual(readFileSync(path), before);
  writeFileSync(platformPath, JSON.stringify(source)); writeFileSync(path, Buffer.concat([before, Buffer.from('\n')]));
  assert.throws(() => run(workspace, '--check')); assert.deepEqual(readFileSync(path), Buffer.concat([before, Buffer.from('\n')]));
  for (const args of [['--tables', 'sys_agent_grant'], ['--output', 'outside.json'], ['--check', '--check']]) assert.throws(() => run(workspace, ...args));
}));
