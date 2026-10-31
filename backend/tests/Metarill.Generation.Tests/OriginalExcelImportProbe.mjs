import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';

// 执行原选择函数本体，不复制其SheetJS参数或自行重写Excel解析。
const [root, workbook, expectedPath, outputPath] = process.argv.slice(2);
const source = fs.readFileSync(path.join(root, 'frontend/metarill_uni_admin/common/yzp_runtime/yzp-runtime.js'), 'utf8');
const marker = 'chooseImportFile(onSelect) {';
assert.equal(source.split(marker).length, 2, '原Excel选择方法必须唯一');
const start = source.indexOf(marker);
const end = source.indexOf('\n\tback: function', start);
assert.ok(end > start, '原Excel方法边界丢失');
const method = source.slice(start, end).trim().replace(/,$/, '');
assert.ok(method.includes('XLSX.utils.sheet_to_json'), '必须执行原SheetJS分支');
const xlsx = await import(pathToFileURL(path.join(root, 'frontend/metarill_uni_admin/node_modules/xlsx/xlsx.mjs')).href);
const bytes = fs.readFileSync(workbook);
let result, reads = 0, conversions = 0, choices = 0;
const XLSX = {
  ...xlsx,
  read(...args) { reads++; return xlsx.read(...args); },
  utils: {...xlsx.utils, sheet_to_json(...args) { conversions++; return xlsx.utils.sheet_to_json(...args); }},
};
const handlers = vm.runInNewContext('({' + method + '})', {
  XLSX,
  // 只模拟本机文件选择和二进制传递；不创建浏览器、不发送HTTP、不替换SheetJS。
  uni: {chooseFile(options) { choices++; assert.equal(options.count, 1); assert.ok(options.extension.includes('.xlsx')); options.success({tempFiles: [{path: workbook, name: 'paths.xlsx'}]}); }},
  Blob: class {},
  FileReader: class { readAsBinaryString() { this.onload({target: {result: bytes.toString('binary')}}); } },
  console: {error(...args) { throw new Error(args.map(String).join(' ')); }},
});
handlers.chooseImportFile(rows => { result = rows; });
assert.equal(choices, 1); assert.equal(reads, 1); assert.equal(conversions, 1);
assert.deepEqual(JSON.parse(JSON.stringify(result)), JSON.parse(fs.readFileSync(expectedPath, 'utf8')));
// 可选输出仍来自原回调，仅交给外部实际HTTP用例；本脚本不自行发送请求。
if (outputPath) fs.writeFileSync(outputPath, JSON.stringify(result));
process.stdout.write('真实XLSX与原Excel方法：' + result.length + '行逐字保持。\n');
