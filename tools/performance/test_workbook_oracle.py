"""使用原SheetJS实际创建、读取XLSX及BIFF工作簿，不把JSON桩计为文件通过。"""
import base64
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = Path(__file__).with_name('paired_workbook_oracle.cjs')
XLSX = ROOT / 'frontend/metarill_uni_admin/node_modules/xlsx/dist/xlsx.full.min.js'
NODE = shutil.which('node')


@unittest.skipUnless(NODE and XLSX.is_file(), '需要已安装的原前端SheetJS及Node，CI在性能工具前执行npm ci')
class WorkbookOracleTests(unittest.TestCase):
    def invoke(self, value, success=True, reason=None):
        """只用真实本机Node和现有依赖，保持stdout单个有界JSON。"""
        result = subprocess.run([NODE, str(SCRIPT), str(XLSX)], input=json.dumps(value, ensure_ascii=False).encode(), capture_output=True, timeout=15)
        self.assertEqual(success, result.returncode == 0, result.stderr.decode('utf-8', errors='replace'))
        if success:
            return json.loads(result.stdout)
        self.assertEqual(b'', result.stdout)
        self.assertIn('工作簿核验失败', result.stderr.decode('utf-8'))
        if reason is not None:
            self.assertIn(reason, result.stderr.decode('utf-8'))

    def test_actual_xlsx_preserves_chinese_long_id_and_file_reference_cells(self):
        """长ID和附件JSON是字符串，金额数字及空格按原客户端语义保持。"""
        file_ids = '["9007199254740993"]'
        value = {'version': 1, 'operation': 'create', 'headers': ['合同名称', '客户', '合同金额', '合同附件'], 'rows': [[' 合同甲 ', '9007199254740993', 12.5, file_ids], ['合同乙', '客户乙', 3.25, file_ids]]}
        result = self.invoke(value)
        data = base64.b64decode(result['workbookBase64'], validate=True)
        self.assertEqual(b'PK\x03\x04', data[:4])
        self.assertEqual(hashlib.sha256(data).hexdigest(), result['sha256'])
        self.assertEqual('0.20.3', result['sheetJsVersion'])
        self.assertEqual(value['headers'], result['headers'])
        self.assertEqual(value['rows'][0], result['cells'][1])
        self.assertEqual({'合同名称': ' 合同甲 ', '客户': '9007199254740993', '合同金额': 12.5, '合同附件': file_ids}, result['rows'][0])
        read = self.invoke({'version': 1, 'operation': 'read', 'base64': result['workbookBase64'], 'expectedSha256': result['sha256']})
        self.assertEqual(result['rows'], read['rows'])
        self.assertNotIn('workbookBase64', read)

    def test_original_biff_xls_is_read_as_real_workbook(self):
        """Java原HSSF导出是BIFF文件，不能只验证C#的ZIP/XLSX。"""
        source = "const X=require(process.argv[1]);const w=X.utils.book_new();X.utils.book_append_sheet(w,X.utils.aoa_to_sheet([['名称','标识','金额'],['原导出','9007199254740993',12.5]]),'sheet1');process.stdout.write(X.write(w,{type:'buffer',bookType:'xls'}));"
        result = subprocess.run([NODE, '-e', source, str(XLSX)], capture_output=True, timeout=15, check=True)
        self.assertEqual(bytes.fromhex('d0cf11e0a1b11ae1'), result.stdout[:8])
        read = self.invoke({'version': 1, 'operation': 'read', 'base64': base64.b64encode(result.stdout).decode(), 'expectedSha256': hashlib.sha256(result.stdout).hexdigest()})
        self.assertEqual('xls', read['format'])
        self.assertEqual([{'名称': '原导出', '标识': '9007199254740993', '金额': 12.5}], read['rows'])

    def test_invalid_digest_non_excel_duplicate_headers_and_unsafe_ids_are_rejected(self):
        """错文件、伪Excel、重名表头及已经丢精度的数字ID不能给出成功回执。"""
        data = b'{"not":"excel"}'
        for checksum, reason in [('f' * 64, 'workbook-digest'), (hashlib.sha256(data).hexdigest(), 'workbook-container')]:
            with self.subTest(checksum=checksum):
                self.invoke({'version': 1, 'operation': 'read', 'base64': base64.b64encode(data).decode(), 'expectedSha256': checksum}, success=False, reason=reason)
        for headers, rows, reason in [(['名称','名称'], [['甲','乙']], 'workbook-headers'), (['标识'], [[9007199254740993]], 'workbook-rows'), (['名称'], [[{}]], 'workbook-rows'), (['名称'], [['甲']] * 257, 'workbook-rows')]:
            with self.subTest(headers=headers, row_count=len(rows)):
                self.invoke({'version': 1, 'operation': 'create', 'headers': headers, 'rows': rows}, success=False, reason=reason)


if __name__ == '__main__':
    unittest.main()