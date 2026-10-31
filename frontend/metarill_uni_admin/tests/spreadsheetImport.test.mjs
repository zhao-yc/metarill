import test from 'node:test'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import vm from 'node:vm'
import * as XLSX from 'xlsx'
import * as relationRuntime from '../common/yzp_runtime/relationReadValues.js'

const source = await readFile(new URL('../common/yzp_runtime/yzp-runtime.js', import.meta.url), 'utf8')

/** 使用升级后的真实 SheetJS，模拟浏览器选文件/读取事件并执行原共享导入方法。 */
function chooseImportFile(binary, extension) {
 const selections = [], errors = [], choices = []
 const context = { ...relationRuntime, XLSX, Blob, console: { error: (...args) => errors.push(args) },
  FileReader: class {
   readAsBinaryString(blob) { assert.ok(blob instanceof Blob); this.onload({ target: { result: binary } }) }
  },
  uni: { chooseFile: options => { choices.push(options); options.success({ tempFiles: [{ path: '模拟浏览器文件', name: '导入.' + extension }] }) } } }
 vm.runInNewContext(source.slice(source.indexOf('const TAB_BAR_PAGES')).replace('export default yzp;', 'globalThis.runtime = yzp;'), context)
 context.runtime.chooseImportFile(rows => selections.push(rows))
 return { selections, errors, choices }
}

/** 同时写入第二张工作表，验证旧协议始终只读取首张而不合并其他表。 */
function workbook() {
 const book = XLSX.utils.book_new()
 XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
  ['姓名', '金额', '启用', '日期', '备注'], ['客户甲', 12.5, true, new Date('2026-10-19T00:00:00.000Z'), null], ['客户乙', 0, false, new Date('2026-10-20T00:00:00.000Z'), '保留备注']
 ], { cellDates: true }), '首张数据表')
 XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([['姓名'], ['不应导入第二张']]), '其他数据')
 return book
}

for (const [bookType, extension] of [['xlsx', 'xlsx'], ['biff8', 'xls']]) {
 test(`实际${extension}导入保留中文、数值、布尔、日期、省略空单元格和首表选择`, () => {
  const f = chooseImportFile(XLSX.write(workbook(), { bookType, type: 'binary' }), extension)
  assert.deepEqual(f.errors, []); assert.equal(f.selections.length, 1); const rows = f.selections[0]
  assert.equal(rows.length, 2); assert.equal(rows[0].姓名, '客户甲'); assert.equal(rows[0].金额, 12.5); assert.equal(rows[0].启用, true); assert.equal(rows[0].日期.toISOString(), '2026-10-19T00:00:00.000Z'); assert.equal(Object.hasOwn(rows[0], '备注'), false)
  assert.equal(rows[1].金额, 0); assert.equal(rows[1].启用, false); assert.equal(rows[1].备注, '保留备注')
  assert.deepEqual(Array.from(f.choices[0].extension), ['.xls', '.xlsx', '.csv']); assert.equal(f.choices[0].count, 1)
 })
}

test('实际UTF8 CSV导入保留中文、零值、引号内逗号与原选择回调', () => {
 const book = XLSX.utils.book_new(); XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([['姓名', '金额', '备注'], ['客户甲', 0, '包含,逗号'], ['客户乙', 12.5, '中文备注']]), '数据')
 const f = chooseImportFile(XLSX.write(book, { bookType: 'csv', type: 'binary' }), 'csv')
 assert.deepEqual(f.errors, []); assert.equal(f.selections.length, 1); assert.deepEqual(f.selections[0], [{ 姓名: '客户甲', 金额: 0, 备注: '包含,逗号' }, { 姓名: '客户乙', 金额: 12.5, 备注: '中文备注' }])
})

test('损坏的实际Excel文件不调用导入回调，错误沿用原处理流程', () => {
 const binary = XLSX.write(workbook(), { bookType: 'xlsx', type: 'binary' }).slice(0, 50)
 const f = chooseImportFile(binary, 'xlsx')
 assert.equal(f.selections.length, 0); assert.equal(f.errors.length, 1); assert.equal(f.errors[0][0], '上传失败')
})

test('官方源码归档、安装版本和锁文件完整性一致，不回退npm旧发布', async () => {
 const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))
 const lock = JSON.parse(await readFile(new URL('../package-lock.json', import.meta.url), 'utf8'))
 const provenance = JSON.parse(await readFile(new URL('../vendor/sheetjs-source.json', import.meta.url), 'utf8'))
 const bytes = await readFile(new URL('../vendor/xlsx-0.20.3.tgz', import.meta.url))
 const entry = lock.packages['node_modules/xlsx']
 assert.equal(XLSX.version, '0.20.3'); assert.equal(packageJson.dependencies.xlsx, 'file:vendor/xlsx-0.20.3.tgz'); assert.equal(entry.version, '0.20.3'); assert.equal(entry.resolved, 'file:vendor/xlsx-0.20.3.tgz')
 assert.equal(provenance.url, 'https://cdn.sheetjs.com/xlsx-0.20.3/xlsx-0.20.3.tgz'); assert.equal(provenance.sha256, createHash('sha256').update(bytes).digest('hex')); assert.equal(entry.integrity, 'sha512-' + createHash('sha512').update(bytes).digest('base64')); assert.equal(entry.integrity, provenance.integrity)
})
