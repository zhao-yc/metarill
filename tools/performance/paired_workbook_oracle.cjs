// 同条件验收的真实工作簿读回：复用原前端SheetJS，文件字节和原sheet_to_json选项分别核验。
'use strict';
const fs = require('node:fs');
const crypto = require('node:crypto');
const MAX_BYTES = 4 * 1024 * 1024;
const ROW_LIMIT = 256;
const COLUMN_LIMIT = 32;

/** 拒绝超出固定验收范围的输入，不回显业务值或文件内容。 */
function requireFact(ok, code) {
  if (!ok) throw new Error(code);
}

/** 原字段只接受JSON标量；大整数必须保留为字符串，不能先经JS数字丢失精度。 */
function scalar(value) {
  return value === null || typeof value === 'boolean' ||
    typeof value === 'string' && value.length <= 16384 ||
    typeof value === 'number' && Number.isFinite(value) && (!Number.isInteger(value) || Number.isSafeInteger(value));
}

/** 工作簿类型按真实容器签名判断，普通JSON或CSV不能冒充原Excel。 */
function format(bytes) {
  if (bytes.subarray(0, 4).equals(Buffer.from([0x50, 0x4b, 0x03, 0x04]))) return 'xlsx';
  if (bytes.subarray(0, 8).equals(Buffer.from([0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1]))) return 'xls';
  throw new Error('workbook-container');
}

/** 对真实二进制使用原read(binary,cellDates:true)和默认sheet_to_json，另保留全格读回。 */
function inspect(XLSX, bytes) {
  requireFact(bytes.length > 0 && bytes.length <= MAX_BYTES, 'workbook-size');
  const kind = format(bytes);
  const workbook = XLSX.read(bytes.toString('binary'), {type: 'binary', cellDates: true});
  requireFact(workbook.SheetNames.length === 1, 'workbook-sheet-count');
  const sheet = workbook.Sheets[workbook.SheetNames[0]];
  requireFact(typeof sheet['!ref'] === 'string', 'workbook-range');
  const range = XLSX.utils.decode_range(sheet['!ref']);
  requireFact(range.s.r === 0 && range.s.c === 0 && range.e.r <= ROW_LIMIT && range.e.c < COLUMN_LIMIT, 'workbook-range');
  for (const [key, cell] of Object.entries(sheet)) {
    if (key.startsWith('!')) continue;
    requireFact(!cell.f && (cell.v instanceof Date || scalar(cell.v)), 'workbook-cell');
  }
  const cells = XLSX.utils.sheet_to_json(sheet, {header: 1, defval: null, blankrows: false});
  const headers = cells[0];
  requireFact(Array.isArray(headers) && headers.length > 0 && headers.every(x => typeof x === 'string' && x.length > 0) && new Set(headers).size === headers.length, 'workbook-headers');
  const rows = XLSX.utils.sheet_to_json(sheet, {});
  requireFact(rows.length <= ROW_LIMIT, 'workbook-row-count');
  return {format: kind, byteLength: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    sheetNames: workbook.SheetNames, headers, rows, cells, sheetJsVersion: XLSX.version, nodeVersion: process.version};
}

/** 创建后必须再解析实际XLSX；读取既有导出时必须匹配下载所得摘要。 */
function execute(XLSX, input) {
  requireFact(input && Object.getPrototypeOf(input) === Object.prototype && input.version === 1, 'workbook-input');
  if (input.operation === 'create') {
    requireFact(Object.keys(input).sort().join(',') === 'headers,operation,rows,version', 'workbook-input');
    const {headers, rows} = input;
    requireFact(Array.isArray(headers) && headers.length > 0 && headers.length <= COLUMN_LIMIT && headers.every(x => typeof x === 'string' && x.length > 0 && x.length <= 128) && new Set(headers).size === headers.length, 'workbook-headers');
    requireFact(Array.isArray(rows) && rows.length > 0 && rows.length <= ROW_LIMIT && rows.every(row => Array.isArray(row) && row.length === headers.length && row.every(scalar)), 'workbook-rows');
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet([headers, ...rows]), 'sheet1');
    // 原浏览器完整包在Node中返回Uint8Array，按真实字节转Buffer后统一校验。
    const bytes = Buffer.from(XLSX.write(workbook, {type: 'buffer', bookType: 'xlsx'}));
    return {...inspect(XLSX, bytes), workbookBase64: bytes.toString('base64')};
  }
  requireFact(input.operation === 'read' && Object.keys(input).sort().join(',') === 'base64,expectedSha256,operation,version', 'workbook-input');
  requireFact(typeof input.base64 === 'string' && input.base64.length <= MAX_BYTES * 4 / 3 + 4 && input.base64.length % 4 === 0 && Buffer.from(input.base64, 'base64').toString('base64') === input.base64, 'workbook-base64');
  requireFact(typeof input.expectedSha256 === 'string' && /^[a-f0-9]{64}$/.test(input.expectedSha256), 'workbook-digest');
  const bytes = Buffer.from(input.base64, 'base64');
  requireFact(crypto.createHash('sha256').update(bytes).digest('hex') === input.expectedSha256, 'workbook-digest');
  return inspect(XLSX, bytes);
}

/** 只消费stdin和明确模块路径；模块版本固定，输出不包含本机环境或凭据。 */
async function main() {
  requireFact(process.argv.length === 3, 'workbook-arguments');
  requireFact(crypto.createHash('sha256').update(fs.readFileSync(process.argv[2])).digest('hex') === 'cc015130aa8521e7f088f88898eba949ccdcbfb38df0bd129b44b7273c3a6f41', 'workbook-library-digest');
  const XLSX = require(process.argv[2]);
  requireFact(XLSX.version === '0.20.3', 'workbook-library-version');
  const chunks = []; let size = 0;
  for await (const chunk of process.stdin) {
    size += chunk.length; requireFact(size <= 6 * 1024 * 1024, 'workbook-input-size'); chunks.push(chunk);
  }
  const result = execute(XLSX, JSON.parse(Buffer.concat(chunks).toString('utf8')));
  process.stdout.write(JSON.stringify(result));
}
main().catch(error => {
  const reason = /^workbook-[a-z-]+$/.test(error.message || '') ? error.message : error.name;
  process.stderr.write('工作簿核验失败：' + reason + '\n'); process.exitCode = 1;
});