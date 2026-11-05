"""双格式导出按同一组原筛选、父范围和逐格值验收。"""
import base64
import copy
import hashlib
import unittest
from decimal import Decimal

import paired_export_contract as subject


def workbook(side, kind):
    """构造解析器合同，实际工作簿字节验证另行调用原 SheetJS。"""
    headers = subject.HEADERS[kind]
    rows = [[float(cell) if isinstance(cell, Decimal) else cell for cell in row]
            for row in subject.expected_export(kind)]
    return {"headers": headers, "cells": [headers, *rows], "format": subject.FORMAT[side]}


class ExportContractTests(unittest.TestCase):
    def test_original_ticket_filters_and_parent_range(self):
        """保留原下载票据和请求字段，父 ID 超过 JS 安全整数且保持字符串。"""
        for kind, prefix in subject.PREFIX.items():
            step = subject.ticket_step(kind)
            self.assertEqual({"name": prefix}, step["json"]["filters"])
            self.assertNotIn("parentId", step["json"])
        step = subject.ticket_step("line")
        self.assertEqual("/admin/auto/sample_contract_line/81003/initExport", step["path"])
        self.assertEqual({"exportId": "/data"}, step["capture"])
        self.assertEqual(["/data"], step["ignorePointers"])
        self.assertEqual({"column": "name", "order": "ascending"}, step["json"]["orderBy"])
        self.assertGreater(int(step["json"]["parentId"]), 2**53)
        self.assertEqual({}, step["json"]["filters"])

    def test_both_formats_match_exact_common_cells(self):
        """两端文件格式独立校验，允许空备注在两种容器的实际表示不同。"""
        for kind, count in (("customer", 94), ("contract", 90), ("line", 3)):
            java = workbook("java", kind)
            csharp = workbook("csharp", kind)
            csharp["cells"][1][csharp["headers"].index("备注")] = ""
            expected = subject.verify_cells("java", kind, java)
            self.assertEqual(count, expected["rows"])
            self.assertEqual(expected, subject.verify_cells("csharp", kind, csharp))
        self.assertEqual(["性能明细-0000", "性能明细-0001", "性能明细-0002"],
                         [row[0] for row in subject.expected_export("line")])

    def test_wrong_format_order_amount_relation_and_attachment_are_rejected(self):
        """防止只核 HTTP 成功、只核行数或吞掉真正的格值类型变化。"""
        original = workbook("csharp", "contract")
        with self.assertRaisesRegex(ValueError, "format-or-header"):
            subject.verify_cells("java", "contract", original)
        for index, bad in ((1, "错误客户"), (2, "0.00"), (2, False), (3, "错误备注"), (4, "[123]")):
            with self.subTest(index=index, bad=bad):
                copy_of_export = copy.deepcopy(original)
                copy_of_export["cells"][1][index] = bad
                with self.assertRaisesRegex(ValueError, "cell-value"):
                    subject.verify_cells("csharp", "contract", copy_of_export)
        original["cells"][1:3] = reversed(original["cells"][1:3])
        with self.assertRaisesRegex(ValueError, "cell-value"):
            subject.verify_cells("csharp", "contract", original)

    def test_download_proof_is_bound_to_exact_response_bytes(self):
        """逐格一致仍需核对解析器回执与本次响应的真实摘要。"""
        raw = b"PK\x03\x04contract-stub"
        parsed = workbook("csharp", "line")
        parsed.update(byteLength=len(raw), sha256=hashlib.sha256(raw).hexdigest(),
                      sheetNames=["sheet1"], rows=[dict(zip(parsed["headers"], row)) for row in parsed["cells"][1:]],
                      sheetJsVersion="0.20.3", nodeVersion="v24.14.0")

        def parse(request):
            """核对解析器入口只接收本次下载响应。"""
            self.assertEqual(raw, base64.b64decode(request["base64"], validate=True))
            return parsed

        result = subject.verify_download("csharp", "line", raw, parse)
        self.assertEqual((3, len(raw), parsed["sha256"]), (result["rows"], result["byteLength"], result["sha256"]))
        parsed["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "output-digest"):
            subject.verify_download("csharp", "line", raw, parse)
