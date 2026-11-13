import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { deriveSecurity } from './derive-security.mjs';

// 固定来源契约：稳定ID、逻辑类型、长度、可空性和唯一索引均来自平台源头。
const contracts = [["2200000000000000323","sys_agent_grant",[["2200000000000000324","id","int64","",1],["2200000000000000325","insert_time","datetime","255",0],["2200000000000000326","update_time","datetime","255",0],["2200000000000000327","insert_manager_id","int64","",0],["2200000000000000328","update_manager_id","int64","",0],["2200000000000000329","insert_user_name","string","255",0],["2200000000000000330","update_user_name","string","255",0],["2200000000000000331","insert_manager_real_name","string","255",0],["2200000000000000332","update_manager_real_name","string","255",0],["2200000000000000333","delete_no","datetime","255",0],["2200000000000000334","account_id","int64","",0],["2200000000000000335","project_id","int64","",1],["2200000000000000336","device_code_hash","string","64",1],["2200000000000000337","user_code","string","16",1],["2200000000000000338","client_name","string","100",1],["2200000000000000339","status","string","20",1],["2200000000000000340","expires_at","datetime","255",1],["2200000000000000341","refresh_hash","string","64",0],["2200000000000101001","client_id","string","100",0],["2200000000000101002","last_poll_at","datetime","255",0],["2200000000000101003","poll_interval_seconds","int32","",0],["2200000000000101004","device_redeemed_at","datetime","255",0],["2200000000000101005","refresh_expires_at","datetime","255",0]],[["2200000000000000738","ux_grant_device",[["2200000000000000739","2200000000000000336","device_code_hash",0,"ASC"]]],["2200000000000000740","ux_grant_user",[["2200000000000000741","2200000000000000337","user_code",0,"ASC"]]]]],["2200000000000000342","sys_agent_task",[["2200000000000000343","id","int64","",1],["2200000000000000344","insert_time","datetime","255",0],["2200000000000000345","update_time","datetime","255",0],["2200000000000000346","insert_manager_id","int64","",0],["2200000000000000347","update_manager_id","int64","",0],["2200000000000000348","insert_user_name","string","255",0],["2200000000000000349","update_user_name","string","255",0],["2200000000000000350","insert_manager_real_name","string","255",0],["2200000000000000351","update_manager_real_name","string","255",0],["2200000000000000352","delete_no","datetime","255",0],["2200000000000000353","account_id","int64","",1],["2200000000000000354","project_id","int64","",1],["2200000000000000355","request_id","string","36",1],["2200000000000000356","capability","string","150",1],["2200000000000000357","request_hash","string","64",1],["2200000000000000358","status","string","20",1],["2200000000000000359","outcome","string","20",1],["2200000000000000360","result_id","int64","",0],["2200000000000000361","http_status","int32","",0],["2200000000000000362","business_code","int32","",0],["2200000000000000363","expires_at","datetime","255",1]],[["2200000000000000742","ux_task_request",[["2200000000000000743","2200000000000000353","account_id",0,"ASC"],["2200000000000000744","2200000000000000354","project_id",10,"ASC"],["2200000000000000745","2200000000000000355","request_id",20,"ASC"]]]]]];
const fail = message => { throw new Error(message); };
const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const sorted = rows => [...rows].sort((a, b) => a.id.localeCompare(b.id, 'en'));

/** 只派生两张固定物理表，不接表名参数、页面、默认秘密或任意扩展。 */
export function deriveAgent(source) {
  deriveSecurity(source); // 同时要求同一可信平台根及已有安全会话模型完整。
  const tables = [], columns = [], indexes = [], fields = [];
  for (const [id, name, expectedColumns, expectedIndexes] of contracts) {
    const table = source.configTables.find(row => row.id === id);
    if (!table || table.tableName !== name || table.isView !== 0 || table.isFunctionTable !== 0) fail('Agent固定实体表身份漂移。');
    tables.push({ id, tableName: name, isFunctionTable: 0, isView: 0 });
    const actual = source.configColumns.filter(row => row.configTableId === id);
    if (actual.length !== expectedColumns.length) fail('Agent物理字段集合漂移。');
    for (const definition of expectedColumns) {
      const column = actual.find(row => row.id === definition[0]);
      if (!column || !equal([column.id, column.columnName, column.logicalType, column.columnLength, column.isDbNotNull], definition)
        || (column.isVirtualColumn !== undefined && column.isVirtualColumn !== 0) || column.defaultValue !== '') fail('Agent字段契约或空默认值漂移。');
      const precision = column.precision === undefined ? 18 : column.precision, scale = column.scale === undefined ? 2 : column.scale;
      if (precision !== 18 || scale !== 2) fail('Agent字段精度漂移。');
      columns.push({ id: column.id, configTableId: id, columnName: column.columnName, logicalType: column.logicalType,
        columnLength: column.columnLength, isDbNotNull: column.isDbNotNull, isVirtualColumn: 0, precision, scale, defaultValue: '' });
    }
    const actualIndexes = source.configIndexes.filter(row => row.configTableId === id);
    if (actualIndexes.length !== expectedIndexes.length) fail('Agent索引集合漂移。');
    for (const [indexId, indexName, expectedFields] of expectedIndexes) {
      const index = actualIndexes.find(row => row.id === indexId);
      if (!index || index.indexName !== indexName || index.isUnique !== 1 || index.isEnabled !== 1 || index.indexType !== 'UNIQUE') fail('Agent必要唯一索引漂移。');
      const actualFields = source.configIndexColumns.filter(row => row.configIndexId === indexId).sort((a,b) => a.sortNo-b.sortNo);
      if (!equal(actualFields.map(row => [row.id,row.configColumnId,row.columnName,row.sortNo,row.sortDirection]), expectedFields)) fail('Agent唯一索引字段顺序或归属漂移。');
      indexes.push({ id: indexId, configTableId: id, indexName, isUnique: 1, indexType: 'UNIQUE', isEnabled: 1 });
      fields.push(...actualFields.map(row => ({ id:row.id,configIndexId:indexId,configColumnId:row.configColumnId,columnName:row.columnName,sortNo:row.sortNo,sortDirection:row.sortDirection })));
    }
  }
  return { schemaVersion:1,backendTarget:'csharp',yzProjectId:source.yzProjectId,yzProjectName:source.yzProjectName,databaseProvider:'mysql',
    configTables:sorted(tables),configColumns:sorted(columns),configPages:[],configPageColumns:[],yzAdminTypes:[],configMenus:[],
    configCustomPages:[],configButtons:[],configChildren:[],configIndexes:sorted(indexes),configIndexColumns:sorted(fields) };
}

/** 生成程序集内固定字符串，独立包无需读取平台或可变外部Agent定义。 */
export function renderAgent(source) {
  const json = JSON.stringify(deriveAgent(source), null, 2);
  return '// 由 tools/metadata/derive-agent.mjs 从平台元数据派生；禁止手工修改。\nnamespace Metarill.Runtime.Agent;\n\npublic static partial class AgentFoundationCatalog\n{\n    private const string Definition = """\n' + json + '\n""";\n}\n';
}

/** CLI只读当前检出平台源，输出固定受管C#文件。 */
function main(args) {
  if (args.length > 1 || (args.length === 1 && args[0] !== '--check')) fail('仅支持无参数派生或 --check 核验。');
  const root = fileURLToPath(new URL('../..', import.meta.url));
  const expected = renderAgent(JSON.parse(readFileSync(resolve(root,'backend/metadata/platform.json'),'utf8')));
  const destination = resolve(root,'backend/src/Metarill.Agent/AgentFoundationCatalog.g.cs');
  if (args[0] === '--check') { if (readFileSync(destination,'utf8').replaceAll('\r\n','\n') !== expected) fail('Agent派生产物漂移，请重新运行派生命令并提交。'); console.log('Agent两表及唯一索引与平台源一致。'); }
  else { writeFileSync(destination,expected,'utf8'); console.log('已派生Agent两张基础表、完整物理字段及三个唯一索引。'); }
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main(process.argv.slice(2));
