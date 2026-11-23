import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';

// 写出真实 Excel 字节，包括跨日舍入以及不靠数字格式识别的显式日期单元格。
const [root, target, dateSystem] = process.argv.slice(2);
const xlsx = await import(pathToFileURL(path.join(root, 'frontend/metarill_uni_admin/node_modules/xlsx/xlsx.mjs')).href);
const date1904 = dateSystem === '1904';
const legacy = path.extname(target) === '.xls';
const epoch = date1904 ? Date.UTC(1904, 0, 1) : Date.UTC(1899, 11, 30);
const serial = timestamp => (timestamp - epoch) / 86_400_000;
const sheet = xlsx.utils.aoa_to_sheet([
  ['Stamp', 'Rollover', 'Number'],
  [serial(Date.UTC(2026, 2, 8, 2, 30, 0, 125)), serial(Date.UTC(2026, 10, 25) - 0.25), 42]
]);
sheet.A2.z = sheet.B2.z = 'yyyy-mm-dd hh:mm:ss.000';
if (!legacy) {
  xlsx.utils.sheet_add_aoa(sheet, [['Explicit']], { origin: 'D1' });
  sheet.D2 = { t: 'd', v: new Date('2026-11-24T12:34:00.125Z'), z: 'General' };
  sheet['!ref'] = 'A1:D2';
}

const book = xlsx.utils.book_new();
book.Workbook = { WBProps: { date1904 } };
xlsx.utils.book_append_sheet(book, sheet, 'Calendar');
xlsx.utils.book_append_sheet(book, xlsx.utils.aoa_to_sheet([['Ignored'], ['只读取首个工作表']]), 'Ignored');
const bytes = xlsx.write(book, { type: 'buffer', bookType: legacy ? 'biff8' : 'xlsx', cellDates: true });
const raw = xlsx.read(bytes, { type: 'buffer', cellDates: false, cellNF: true });
assert.equal(Boolean(raw.Workbook.WBProps.date1904), date1904);
assert.equal(raw.Sheets.Calendar.A2.t, 'n');
if (!legacy) {
  assert.equal(raw.Sheets.Calendar.D2.z, 'General');
  const archive = xlsx.CFB.read(bytes, { type: 'buffer' });
  const xml = xlsx.CFB.find(archive, '/xl/worksheets/sheet1.xml').content;
  assert.match(Buffer.from(xml).toString(), /<c r="D2"[^>]* t="d">/);
}
fs.writeFileSync(target, bytes);
