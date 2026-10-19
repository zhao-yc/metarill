import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

// 文件基础表只从平台元数据派生；排序固定，跨操作系统产生相同 UTF-8/LF 字节。
const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const source = JSON.parse(readFileSync(resolve(root, 'backend/metadata/platform.json'), 'utf8'));
const names = new Set(['yz_file_library', 'yz_file_library_folder', 'yz_file_ref']);
const tables = source.configTables.filter(table => names.has(table.tableName));
if (tables.length !== names.size || new Set(tables.map(table => table.tableName)).size !== names.size) throw new Error('平台文件基础表缺失或重复。');
const tableIds = new Set(tables.map(table => table.id));
const columns = source.configColumns.filter(column => tableIds.has(column.configTableId));
const columnIds = new Set(columns.map(column => column.id));
if (columns.some(column => column.linkConfigTableId && !tableIds.has(column.linkConfigTableId))) throw new Error('文件基础表出现未纳入派生包的关联表。');
const indexes = source.configIndexes.filter(index => tableIds.has(index.configTableId));
const indexIds = new Set(indexes.map(index => index.id));
const indexColumns = source.configIndexColumns.filter(column => indexIds.has(column.configIndexId));
if (indexColumns.some(column => !columnIds.has(column.configColumnId))) throw new Error('文件索引字段不在基础表闭包内。');
const byId = values => values.toSorted((left, right) => left.id < right.id ? -1 : left.id > right.id ? 1 : 0);
const catalog = {
  schemaVersion: source.schemaVersion, backendTarget: source.backendTarget,
  yzProjectId: source.yzProjectId, yzProjectName: source.yzProjectName, databaseProvider: source.databaseProvider,
  configTables: byId(tables), configColumns: byId(columns), configPages: [], configPageColumns: [],
  yzAdminTypes: [], configMenus: [], configCustomPages: [], configButtons: [], configChildren: [],
  configIndexes: byId(indexes), configIndexColumns: byId(indexColumns),
};
const destination = resolve(root, 'backend/metadata/files.json');
const expected = JSON.stringify(catalog, null, 2) + '\n';
if (process.argv.includes('--check')) {
  if (readFileSync(destination, 'utf8') !== expected) throw new Error('文件基础元数据漂移，请执行 node tools/metadata/derive-files.mjs 并提交派生产物。');
  console.log('文件基础元数据与平台源头一致。');
} else {
  writeFileSync(destination, expected, 'utf8');
  console.log('已从平台元数据派生文件基础表、字段与完整索引闭包。');
}
