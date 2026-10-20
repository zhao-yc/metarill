import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// 仅这六张平台物理表进入安全基础模型。稳定 ID 来自已提交平台源，绝不生成新 ID。
const tables = [
  ['yz_project', '1669251289347932162', {
    id: ['2200000000000000172', 'int64'], delete_no: ['2200000000000000181', 'datetime'],
    yz_project_id: ['2200000000000000182', 'int64'], name: ['1669255869418848258', 'string', '50'],
  }],
  ['yz_admin_type', '1696799308939239425', {
    id: ['1696799602892840962', 'int64'], delete_no: ['2200000000000000222', 'datetime'],
    yz_project_id: ['1696799602636988418', 'int64'], name: ['1696799602439856130', 'string', '50'],
  }],
  ['yz_manager', '6031906809708544', {
    id: ['2200000000000000000', 'int64'], delete_no: ['2200000000000000008', 'datetime'],
    yz_project_id: ['2200000000000000009', 'int64'], yz_admin_type_id: ['1679698329655750657', 'int64'],
    name: ['1679698326577131521', 'string', '50'], password: ['1679698326770069505', 'string', '255'],
    yz_role_ids: ['1692528723801219073', 'string', '500'], is_disabled: ['1679698327931891714', 'int32'],
    login_rise_time: ['1679698329148239874', 'datetime'], login_stop_time: ['1679698329278263297', 'datetime'],
    validity_time: ['1679698329404092418', 'datetime'],
  }],
  ['yz_role', '1692532858499969026', {
    id: ['2200000000000000209', 'int64'], delete_no: ['2200000000000000218', 'datetime'],
    yz_project_id: ['2200000000000000219', 'int64'], yz_admin_type_id: ['1692533179766878209', 'int64'],
    name: ['1692533307588292610', 'string', '50'], is_super: ['1692533179573940226', 'int32'],
    is_built_in: ['1692533179959816194', 'int32'],
  }],
  ['yz_role_permission', '2063264600555577345', {
    id: ['2200000000000000240', 'int64'], delete_no: ['2200000000000000249', 'datetime'],
    yz_project_id: ['2200000000000000250', 'int64'], yz_role_id: ['2063264600941453314', 'int64'],
    config_menu_id: ['2063264600681406465', 'int64'], config_page_id: ['2200000000000000251', 'int64'],
    config_button_id: ['2200000000000101006', 'int64'], permission_values: ['2063264600849178625', 'string', 'max'],
  }],
  ['sys_session', '2200000000000000306', {
    id: ['2200000000000000307', 'int64'], delete_no: ['2200000000000000316', 'datetime'],
    account_id: ['2200000000000000317', 'int64'], token_hash: ['2200000000000000318', 'string', '64'],
    expires_at: ['2200000000000000319', 'datetime'], kind: ['2200000000000000320', 'string', '20'],
    grant_id: ['2200000000000000321', 'int64'], project_id: ['2200000000000000322', 'int64'],
  }],
];
// 必需索引的名称、标识、唯一性及有序字段共同固定，不能用同名不同结构替代。
const indexes = [
  ['2200000000000000734', '6031906809708544', 'ux_manager_name', 1, [
    ['2200000000000000735', '1679698326577131521', 'name', 0],
  ]],
  ['900235310779076608', '2063264600555577345', 'yzp_idx_role_permission_role_active', 0, [
    ['900235310800048128', '2063264600941453314', 'yz_role_id', 0], ['900235310821019648', '2200000000000000249', 'delete_no', 10],
  ]],
  ['900235316722405376', '1692532858499969026', 'yzp_idx_role_admin_active', 0, [
    ['900235316734988288', '1692533179766878209', 'yz_admin_type_id', 0], ['900235316751765504', '2200000000000000218', 'delete_no', 10],
  ]],
  ['900235322581848064', '6031906809708544', 'yzp_idx_manager_admin_active', 0, [
    ['900235322598625280', '1679698329655750657', 'yz_admin_type_id', 0], ['900235322615402496', '2200000000000000008', 'delete_no', 10],
  ]],
  ['2200000000000000736', '2200000000000000306', 'ux_session_token', 1, [
    ['2200000000000000737', '2200000000000000318', 'token_hash', 0],
  ]],
];
const identifier = /^[A-Za-z_][A-Za-z0-9_]{0,63}$/;
const logicalTypes = new Set(['string', 'int32', 'int64', 'decimal', 'bool', 'date', 'datetime', 'time', 'json', 'binary']);
const byId = rows => rows.toSorted((left, right) => left.id < right.id ? -1 : left.id > right.id ? 1 : 0);
const fail = message => { throw new Error(message); };
const isId = value => typeof value === 'string' && /^[1-9][0-9]{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n;
const unique = (rows, key, message) => {
  if (new Set(rows.map(key)).size !== rows.length) fail(message);
};

// 明确白名单逐项复制，不传播页面、控件、连接配置、SQL、关联声明或任意扩展字段。
function columnDefinition(column) {
  if (typeof column.columnName !== 'string' || !identifier.test(column.columnName) || !logicalTypes.has(column.logicalType) || typeof column.columnLength !== 'string'
    || ![0, 1].includes(column.isDbNotNull) || ![undefined, 0, 1].includes(column.isVirtualColumn)
    || typeof column.defaultValue !== 'string') fail('安全基础字段物理属性无效。');
  const precision = column.precision === undefined ? 18 : column.precision, scale = column.scale === undefined ? 2 : column.scale;
  if (!Number.isInteger(precision) || precision < 1 || precision > 65 || !Number.isInteger(scale) || scale < 0 || scale > 30 || scale > precision)
    fail('安全基础字段精度无效。');
  if (['string', 'binary'].includes(column.logicalType) && column.columnLength !== ''
    && !(['max', 'text', 'longtext'].includes(column.columnLength) && column.logicalType === 'string')
    && !(/^[1-9][0-9]*$/.test(column.columnLength) && Number(column.columnLength) <= 16000)) fail('安全基础字段长度无效。');
  if (column.columnName === 'id' && (column.logicalType !== 'int64' || column.isDbNotNull !== 1)) fail('安全基础主键必须为非空 int64。');
  if (column.columnName === 'delete_no' && (column.logicalType !== 'datetime' || column.isDbNotNull !== 0)) fail('安全基础逻辑删除字段必须为可空 datetime。');
  const sensitive = /password|passwd|pwd|secret|token|salt|conn|credential|certificate|privatekey|dburl|dbusername|dbname/.test(column.columnName.replaceAll('_', '').toLowerCase());
  // 敏感列仅保留结构；即使源头误配，也不能把默认凭据或连接值写入公开派生文件或错误信息。
  if (sensitive && column.defaultValue !== '') fail('连接或凭据字段不允许非空物理默认值。');
  validateDefault(column);
  return {
    id: column.id, configTableId: column.configTableId, columnName: column.columnName,
    logicalType: column.logicalType, columnLength: column.columnLength, isDbNotNull: column.isDbNotNull,
    isVirtualColumn: column.isVirtualColumn ?? 0, precision, scale, defaultValue: column.defaultValue,
  };
}

// 与结构编译器一致，只接收受支持的字面默认值，不执行表达式或输出原值。
function validateDefault(column) {
  const value = column.defaultValue;
  if (value === '') return;
  if (column.logicalType === 'string' && !value.includes('\\')) return;
  if (['int32', 'int64'].includes(column.logicalType) && /^-?(0|[1-9][0-9]*)$/.test(value)) {
    const limit = column.logicalType === 'int32' ? 2147483648n : 9223372036854775808n;
    if (BigInt(value) >= -limit && BigInt(value) < limit) return;
  }
  if (column.logicalType === 'bool' && ['true', 'false', '0', '1'].includes(value)) return;
  if (column.logicalType === 'decimal' && /^-?[0-9]+(?:\.[0-9]+)?$/.test(value)) return;
  fail('安全基础字段默认值不是受支持的安全常量。');
}

// 纯派生只读取传入快照：完整平台可含其它表，但从不接受客户端表单、表名清单或任意闭包扩展。
export function deriveSecurity(source) {
  if (source.schemaVersion !== 1 || source.backendTarget !== 'csharp' || source.databaseProvider !== 'mysql'
    || source.yzProjectId !== '1608097412978577409' || source.yzProjectName !== 'Metarill') fail('安全基础模型必须来自受信平台版本与项目。');
  for (const key of ['configTables', 'configColumns', 'configIndexes', 'configIndexColumns'])
    if (!Array.isArray(source[key]) || source[key].some(row => !row || typeof row !== 'object' || !isId(row.id))) fail('平台物理元数据集合或标识无效。');
  const all = ['configTables', 'configColumns', 'configIndexes', 'configIndexColumns'].flatMap(key => source[key]);
  unique(all, row => row.id, '平台物理元数据标识重复或跨类别冲突。');
  unique(source.configTables, row => row.tableName?.toLowerCase(), '平台表名重复。');
  const selected = tables.map(([name, id, required]) => {
    const table = source.configTables.find(row => row.id === id);
    if (!table || table.tableName !== name || table.isFunctionTable !== 0 || table.isView !== 0)
      fail('固定安全基础表缺失、标识不符或不是物理实体表。');
    const columns = source.configColumns.filter(column => column.configTableId === id);
    unique(columns, column => column.columnName?.toLowerCase(), '安全基础表字段名重复。');
    if (columns.some(column => ![undefined, 0, 1].includes(column.isVirtualColumn))) fail('安全基础字段物理标志无效。');
    for (const [columnName, [columnId, logicalType, length]] of Object.entries(required)) {
      const column = columns.find(column => column.id === columnId);
      if (!column || column.columnName !== columnName || column.logicalType !== logicalType || (column.isVirtualColumn ?? 0) !== 0
        || (length !== undefined && column.columnLength !== length)) fail('安全基础表必要字段缺失或固定物理定义漂移。');
    }
    for (const columnName of ['insert_time', 'update_time', 'insert_manager_id', 'update_manager_id']) {
      const column = columns.find(column => column.columnName === columnName);
      if (!column || (column.isVirtualColumn ?? 0) !== 0 || column.logicalType !== (columnName.endsWith('_id') ? 'int64' : 'datetime'))
        fail('安全基础表必要审计字段缺失或类型漂移。');
    }
    return { id: table.id, tableName: table.tableName, isFunctionTable: table.isFunctionTable, isView: table.isView };
  });
  const tableIds = new Set(selected.map(table => table.id));
  const columns = source.configColumns.filter(column => tableIds.has(column.configTableId) && (column.isVirtualColumn ?? 0) === 0).map(columnDefinition);
  const columnMap = new Map(columns.map(column => [column.id, column]));
  // 只保留六表既有索引；所有索引字段必须在同表物理闭包内，不能借索引带入 Agent 或配置表。
  const selectedIndexes = source.configIndexes.filter(index => tableIds.has(index.configTableId)).map(index => {
    if (typeof index.indexName !== 'string' || !identifier.test(index.indexName) || index.indexName.toUpperCase() === 'PRIMARY' || ![0, 1].includes(index.isUnique)
      || ![0, 1].includes(index.isEnabled) || index.indexType !== (index.isUnique === 1 ? 'UNIQUE' : 'INDEX')) fail('安全基础索引属性无效。');
    return { id: index.id, configTableId: index.configTableId, indexName: index.indexName, isUnique: index.isUnique, indexType: index.indexType, isEnabled: index.isEnabled };
  });
  unique(selectedIndexes, index => `${index.configTableId}:${index.indexName.toLowerCase()}`, '安全基础表索引名重复。');
  const selectedIndexIds = new Set(selectedIndexes.map(index => index.id));
  const indexColumns = source.configIndexColumns.filter(column => selectedIndexIds.has(column.configIndexId)).map(column => {
    const index = selectedIndexes.find(index => index.id === column.configIndexId), target = columnMap.get(column.configColumnId);
    if (!target || target.configTableId !== index.configTableId || column.columnName !== target.columnName
      || !Number.isInteger(column.sortNo) || column.sortNo < 0 || column.sortDirection !== 'ASC') fail('安全基础索引字段越界或顺序方向无效。');
    return { id: column.id, configIndexId: column.configIndexId, configColumnId: column.configColumnId, columnName: column.columnName, sortNo: column.sortNo, sortDirection: column.sortDirection };
  });
  for (const index of selectedIndexes) {
    const fields = indexColumns.filter(field => field.configIndexId === index.id);
    if (fields.length < 1 || fields.length > 16) fail('安全基础索引必须包含一至十六个物理字段。');
    unique(fields, field => field.configColumnId, '安全基础索引字段重复。');
    unique(fields, field => field.sortNo, '安全基础索引字段排序重复。');
  }
  for (const [id, tableId, name, isUnique, fields] of indexes) {
    const index = selectedIndexes.find(index => index.id === id);
    if (!index || index.configTableId !== tableId || index.indexName !== name || index.isUnique !== isUnique || index.isEnabled !== 1)
      fail('安全基础必要索引缺失或定义漂移。');
    const actual = indexColumns.filter(field => field.configIndexId === id).toSorted((a, b) => a.sortNo - b.sortNo);
    const expected = fields.map(([fieldId, columnId, columnName, sortNo]) => ({ id: fieldId, configIndexId: id, configColumnId: columnId, columnName, sortNo, sortDirection: 'ASC' }));
    if (JSON.stringify(actual) !== JSON.stringify(expected)) fail('安全基础必要索引字段定义漂移。');
  }
  return {
    schemaVersion: source.schemaVersion, backendTarget: source.backendTarget,
    yzProjectId: source.yzProjectId, yzProjectName: source.yzProjectName, databaseProvider: source.databaseProvider,
    configTables: byId(selected), configColumns: byId(columns), configPages: [], configPageColumns: [],
    yzAdminTypes: [], configMenus: [], configCustomPages: [], configButtons: [], configChildren: [],
    configIndexes: byId(selectedIndexes), configIndexColumns: byId(indexColumns),
  };
}

// CLI 的输入和输出路径固定在当前检出目录，不支持选择其它表、外部配置或任意写入路径。
function main(args) {
  if (args.length > 1 || (args.length === 1 && args[0] !== '--check')) fail('仅支持无参数派生或 --check 核验。');
  const root = fileURLToPath(new URL('../..', import.meta.url));
  const catalog = deriveSecurity(JSON.parse(readFileSync(resolve(root, 'backend/metadata/platform.json'), 'utf8')));
  const destination = resolve(root, 'backend/metadata/security.json'), expected = JSON.stringify(catalog, null, 2) + '\n';
  if (args[0] === '--check') {
    if (readFileSync(destination, 'utf8') !== expected) fail('安全基础元数据漂移，请执行 node tools/metadata/derive-security.mjs 并提交派生产物。');
    console.log('安全基础物理元数据与平台源头一致。');
  } else {
    writeFileSync(destination, expected, 'utf8');
    console.log('已从平台元数据派生六张安全基础表及完整物理字段与索引闭包。');
  }
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main(process.argv.slice(2));
