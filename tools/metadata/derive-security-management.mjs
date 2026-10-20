import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { deriveSecurity } from './derive-security.mjs';

const MODULE = 'security-management-v1';
const PROJECT = '1608097412978577409', ADMIN = '1762777514515656705';
const TABLES = new Map([['6031906809708544', 'yz_manager'], ['1692532858499969026', 'yz_role'], ['1696799308939239425', 'yz_admin_type']]);
const PAGES = new Map([['6031906822946816', '6031906809708544'], ['1692532858529329154', '1692532858499969026'], ['1696799309006348290', '1696799308939239425']]);
const CUSTOM_PAGE = '2062363000000000002', WORKBENCH_MENU = '2026052700010000001', INIT_BUTTON = '1696805746482847746';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const fail = () => { throw new Error('固定安全管理模块来源、归属或公开字段校验失败；未输出输入值。'); };
const copy = value => structuredClone(value);
const normalize = key => key.replaceAll(/[^A-Za-z0-9]/g, '').toLowerCase();
const sensitive = new Set(['connstring', 'connectionstring', 'mysqlconnstr', 'dbpassword', 'dbusername', 'dburl', 'password', 'pwd', 'secret', 'secretkey', 'privatekey', 'clientsecret', 'apitoken', 'accesstoken', 'refreshtoken', 'token', 'apikey', 'credentials']);

// 公开资产不接受实际连接/凭据；字段名称和原密码控件本身仍保留，不把秘密值写入错误。
function requirePublic(value) {
  if (Array.isArray(value)) { value.forEach(requirePublic); return; }
  if (!value || typeof value !== 'object') {
    if (typeof value === 'string' && /(?:^|[;\s])(?:server|host|user\s?id|password|pwd|database)\s*=/i.test(value)) fail();
    return;
  }
  for (const [key, child] of Object.entries(value)) {
    if (sensitive.has(normalize(key)) && child !== '' && child !== null && child !== undefined) fail();
    requirePublic(child);
  }
}

// 仅从三个已提交快照派生，调用者不能传表名、版本、后台或输出路径来扩展模块。
export function deriveSecurityManagement(platform, security, files, sourceHashes) {
  if (platform.yzProjectId !== PROJECT || platform.yzProjectName !== 'Metarill' || platform.schemaVersion !== 1 || platform.backendTarget !== 'csharp' || platform.databaseProvider !== 'mysql') fail();
  if (JSON.stringify(deriveSecurity(platform)) !== JSON.stringify(security)) fail();
  if (Object.keys(sourceHashes).sort().join(',') !== 'filesSha256,platformSha256,securitySha256' || Object.values(sourceHashes).some(value => !/^[a-f0-9]{64}$/.test(value))) fail();
  const physicalNames = new Set(['yz_file_library', 'yz_file_library_folder', 'yz_file_ref']);
  const fileTables = platform.configTables.filter(table => physicalNames.has(table.tableName));
  if (fileTables.length !== 3 || ['configPages','configPageColumns','yzAdminTypes','configMenus','configCustomPages','configButtons','configChildren'].some(key => !Array.isArray(files[key]) || files[key].length)) fail();
  const fileIndexes = platform.configIndexes.filter(index => fileTables.some(table => table.id === index.configTableId));
  for (const [key, source] of [['configTables', fileTables], ['configColumns', platform.configColumns.filter(column => fileTables.some(table => table.id === column.configTableId))],
    ['configIndexes', fileIndexes], ['configIndexColumns', platform.configIndexColumns.filter(field => fileIndexes.some(index => index.id === field.configIndexId))]]) {
    const expected = source.toSorted((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
    if (JSON.stringify(expected) !== JSON.stringify(files[key])) fail();
  }
  const tables = platform.configTables.filter(table => TABLES.has(table.id));
  if (tables.length !== TABLES.size || tables.some(table => table.tableName !== TABLES.get(table.id) || table.yzProjectId !== PROJECT)) fail();
  const columns = platform.configColumns.filter(column => TABLES.has(column.configTableId));
  if (columns.length !== 69 || columns.some(column => column.yzProjectId !== PROJECT)) fail();
  const columnIds = new Set(columns.map(column => column.id));
  const pages = platform.configPages.filter(page => PAGES.has(page.id));
  if (pages.length !== PAGES.size || pages.some(page => PAGES.get(page.id) !== page.configTableId || page.yzAdminTypeId !== ADMIN || page.yzProjectId !== PROJECT)) fail();
  const fields = platform.configPageColumns.filter(field => PAGES.has(field.configPageId));
  if (fields.length !== 62 || fields.some(field => !columnIds.has(field.configColumnId) || columns.find(column => column.id === field.configColumnId).configTableId !== PAGES.get(field.configPageId) || field.yzProjectId !== PROJECT)) fail();
  // 原 is_edit_pwd 是界面开关，不是秘密；仅实际凭据/连接列禁止初值，保留原开关默认。
  const secretIds = new Set(columns.filter(column => /^(?:password|oldpassword|salt|pwd|secret|token|tokenhash|mysqlconnstr|connectionstring|dbpassword|dbusername|dburl)$/.test(normalize(column.columnName))).map(column => column.id));
  for (const row of [...columns.filter(column => secretIds.has(column.id)), ...fields.filter(field => secretIds.has(field.configColumnId))])
    for (const key of ['defaultValue', 'initValue']) if (row[key] !== undefined && row[key] !== null && row[key] !== '') fail();
  const menusById = new Map(platform.configMenus.map(menu => [menu.id, menu]));
  const selectedMenus = new Set(platform.configMenus.filter(menu => PAGES.has(menu.configPageId) || menu.id === WORKBENCH_MENU).map(menu => menu.id));
  if (selectedMenus.size !== 4) fail();
  for (const id of selectedMenus) {
    const visited = new Set(); let menu = menusById.get(id);
    while (menu?.parentMenuId) {
      if (visited.has(menu.id) || !menusById.has(menu.parentMenuId)) fail();
      visited.add(menu.id); selectedMenus.add(menu.parentMenuId); menu = menusById.get(menu.parentMenuId);
    }
  }
  const menus = platform.configMenus.filter(menu => selectedMenus.has(menu.id));
  if (menus.length !== 6 || menus.some(menu => menu.yzProjectId !== PROJECT || menu.yzAdminTypeId !== ADMIN)) fail();
  const customPages = platform.configCustomPages.filter(page => page.id === CUSTOM_PAGE);
  if (customPages.length !== 1 || customPages[0].pagePath !== 'pages/main/set_role_permission' || customPages[0].yzAdminTypeId !== ADMIN || customPages[0].yzProjectId !== PROJECT) fail();
  const buttons = platform.configButtons.filter(button => PAGES.has(button.configPageId));
  if (buttons.length !== 1 || buttons[0].id !== INIT_BUTTON || buttons[0].sql !== '/admin/custom/index/init_admin' || buttons[0].yzProjectId !== PROJECT) fail();
  const admins = platform.yzAdminTypes.filter(admin => admin.id === ADMIN);
  if (admins.length !== 1 || admins[0].yzProjectId !== PROJECT) fail();
  const management = {
    schemaVersion: 1, backendTarget: 'csharp', yzProjectId: PROJECT, yzProjectName: platform.yzProjectName, databaseProvider: 'mysql',
    configTables: copy(tables), configColumns: copy(columns), configPages: copy(pages), configPageColumns: copy(fields), yzAdminTypes: copy(admins),
    configMenus: copy(menus), configCustomPages: copy(customPages), configButtons: copy(buttons), configChildren: [], configIndexes: [], configIndexColumns: [],
  };
  // 模块页、字段和菜单ID也必须跨类别唯一，不能因过滤固定三页而掩盖原图冲突。
  const records = [...tables,...columns,...pages,...fields,...admins,...menus,...customPages,...buttons];
  if (records.some(row => typeof row.id !== 'string' || !/^[1-9][0-9]{0,18}$/.test(row.id) || BigInt(row.id)>9223372036854775807n)
    || new Set(records.map(row => row.id)).size !== records.length) fail();
  const result = { schemaVersion: 1, moduleId: MODULE, sources: { ...sourceHashes }, files: copy(files), security: copy(security), management,
    pendingAdapters: [
      { code: 'admin-project-options', pageId: '1696799309006348290', columnId: '1696799602636988418', reference: 'yz_project' },
      { code: 'role-permission-workbench', pageId: '', columnId: '', reference: 'pages/main/set_role_permission' },
      { code: 'initialize-account', pageId: '1696799309006348290', columnId: '', reference: '/admin/custom/index/init_admin' },
      { code: 'security-write-runtime', pageId: '6031906822946816', columnId: '1692528723801219073', reference: 'Runtime A/3partial' },
    ] };
  requirePublic(result);
  return result;
}

// 工厂固定嵌入本资产与摘要；CLI没有外部输入、任意输出或版本覆盖入口。
function main(args) {
  if (args.length > 1 || args.length === 1 && args[0] !== '--check') throw new Error('仅支持无参数派生或 --check 核验。');
  const root = fileURLToPath(new URL('../..', import.meta.url));
  const read = name => readFileSync(resolve(root, `backend/metadata/${name}.json`));
  const platform = read('platform'), security = read('security'), files = read('files');
  const result = deriveSecurityManagement(JSON.parse(platform), JSON.parse(security), JSON.parse(files), { platformSha256: hash(platform), securitySha256: hash(security), filesSha256: hash(files) });
  const expected = JSON.stringify(result, null, 2) + '\n', digest = hash(expected) + '\n';
  const directory = resolve(root, 'backend/metadata/modules'), destination = resolve(directory, MODULE + '.json'), checksum = resolve(directory, MODULE + '.sha256');
  if (args[0] === '--check') {
    if (readFileSync(destination, 'utf8') !== expected || readFileSync(checksum, 'utf8') !== digest) throw new Error('固定安全管理模块或摘要漂移，请重新派生并提交。');
    console.log('固定安全管理模块与源头和摘要一致；仍未接宿主适配或发布。');
  } else {
    mkdirSync(directory, { recursive: true }); writeFileSync(destination, expected, 'utf8'); writeFileSync(checksum, digest, 'utf8');
    console.log('已派生固定三安全页模块及SHA；未执行结构、账号初始化或发布。');
  }
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main(process.argv.slice(2));
