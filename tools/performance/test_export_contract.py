"""双格式导出按同一组原筛选、父范围和逐格值验收。"""
import base64
import copy
import hashlib
import contextlib
from email.message import Message
import io
from pathlib import Path
import tempfile
import unittest
from decimal import Decimal
from unittest import mock

import paired_native_bootstrap as native
import test_indexed_write_measurement as indexed
import test_paired_native_bootstrap as previous

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
            self.assertEqual({"column": "name", "order": "ascending"}, step["json"]["orderBy"])
        step = subject.ticket_step("line")
        self.assertEqual("/admin/auto/sample_contract_line/81003/initExport", step["path"])
        self.assertEqual({"exportId": "/data"}, step["capture"])
        self.assertEqual(["/data"], step["ignorePointers"])
        self.assertNotIn("orderBy", step["json"])
        self.assertGreater(int(step["json"]["parentId"]), 2**53)
        self.assertEqual({}, step["json"]["filters"])

    def test_both_formats_match_exact_common_cells(self):
        """两端文件格式独立校验，允许空备注在两种容器的实际表示不同。"""
        for kind, count in (("customer", 94), ("contract", 90), ("line", 3)):
            java = workbook("java", kind)
            csharp = workbook("csharp", kind)
            # 按共同种子定位空备注，不假定该行在原排序中的位置。
            remarks = csharp["headers"].index("备注")
            blank = next(row for row in csharp["cells"][1:] if row[remarks] is None)
            blank[remarks] = ""
            expected = subject.verify_cells("java", kind, java)
            self.assertEqual(count, expected["rows"])
            self.assertEqual(expected, subject.verify_cells("csharp", kind, csharp))
        self.assertEqual(["性能明细-0002", "性能明细-0001", "性能明细-0000"],
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
            self.assertEqual(raw, base64.b64decode(request["workbooks"][0]["base64"], validate=True))
            return {"workbooks": [parsed]}

        result, = subject.verify_workbooks([("csharp", "line", raw)], parse)
        self.assertEqual((3, len(raw), parsed["sha256"]), (result["rows"], result["byteLength"], result["sha256"]))
        parsed["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "output-digest"):
            subject.verify_workbooks([("csharp", "line", raw)], parse)


    def test_replay_batches_preserve_sample_order_and_only_reuse_identical_bytes(self):
        """重复下载保留全部样本；只有相同目标、页面和响应字节才复用解析。"""
        first = b"PK\x03\x04first"
        second = b"PK\x03\x04second"
        samples = [("csharp", "line", raw) for raw in (first, second, first)]
        batches = []

        def parse(request):
            """模拟容器边界并按每件输入生成正确摘要和工作簿回执。"""
            batches.append(request)
            outputs = []
            for item in request["workbooks"]:
                parsed = workbook("csharp", "line")
                parsed.update(byteLength=len(base64.b64decode(item["base64"])), sha256=item["expectedSha256"],
                              sheetNames=["sheet1"], rows=[dict(zip(parsed["headers"], row)) for row in parsed["cells"][1:]],
                              sheetJsVersion="0.20.3", nodeVersion="v24.14.0")
                outputs.append(parsed)
            return {"workbooks": outputs}

        with mock.patch.object(subject.oracle, "MAX_BATCH", 1):
            results = subject.verify_workbooks(samples, parse)
        self.assertEqual(2, len(batches))
        self.assertEqual([hashlib.sha256(raw).hexdigest() for _, _, raw in samples], [p["sha256"] for p in results])
        self.assertEqual(1, len({p["semanticSha256"] for p in results}))
        with mock.patch.object(subject.oracle, "MAX_BATCH", 1), mock.patch.object(subject.oracle, "MAX_JOBS", 7):
            with self.assertRaisesRegex(ValueError, "replay-batches"):
                subject.verify_workbooks(samples, parse)
        self.assertEqual(2, len(batches))


class ExportPreflightContracts(unittest.TestCase):
    def test_ticket_and_download_use_original_http_and_keep_token_out_of_download(self):
        """在传输边界核对真实请求形状、票据贯通和响应文件留证。"""
        targets = {side: {"baseUrl": f"http://127.0.0.1:{28100 + i}/api"}
                   for i, side in enumerate(("java", "csharp"))}
        tokens = {(side, "writer"): f"offline-{side}" for side in targets}
        requests = []

        @contextlib.contextmanager
        def exchange(request, timeout):
            """替身只截断实际 HTTP I/O，保留真实 URL、头和票据解析。"""
            self.assertEqual(15, timeout)
            requests.append(request)
            index = (len(requests) + 1) // 2
            ticket = f"offline-ticket-{index:04}"
            headers = dict(request.header_items())
            if request.get_method() == "POST":
                self.assertIn(headers["Loginstateid"], tokens.values())
                raw = subject.http.canonical({"code": 200, "data": ticket})
            else:
                self.assertNotIn("Loginstateid", headers)
                self.assertEqual("identity", headers["Accept-encoding"])
                self.assertTrue(request.full_url.endswith("/admin/sys/export/export?exportId=" + ticket))
                raw = f"workbook-{index}".encode()
            response = io.BytesIO(raw)
            response.status = 200
            response.headers = Message()
            response.headers["Content-Length"] = str(len(raw))
            yield response

        with tempfile.TemporaryDirectory() as folder, mock.patch.object(subject.urllib.request, "build_opener") as opener, \
             mock.patch.object(subject, "verify_workbooks", return_value=[{"semanticSha256": "same"}] * 6):
            opener.return_value.open.side_effect = exchange
            result = subject.run_preflight(Path(folder), targets, tokens, None)
            self.assertTrue(result["passed"])
            self.assertFalse(result["measurementValid"])
            self.assertEqual(12, len(requests))
            self.assertEqual(7, len(list((Path(folder) / "export-preflight").iterdir())))

    def test_invalid_download_range_and_non_loopback_ticket_are_rejected(self):
        """拒绝向外部地址发送登录态，并拒绝把分段响应当完整文件。"""
        targets = {"java": {"baseUrl": "http://127.0.0.1:28100/api"},
                   "csharp": {"baseUrl": "https://example.invalid/api"}}
        with mock.patch.object(subject.http, "request") as send:
            with self.assertRaisesRegex(ValueError, "loopback"):
                subject.run_preflight(Path("unused"), targets, {}, None)
            send.assert_not_called()
        response = io.BytesIO(b"fragment")
        response.status = 200
        response.headers = Message()
        response.headers["Content-Range"] = "bytes 0-7/16"
        with mock.patch.object(subject.urllib.request, "build_opener") as opener:
            opener.return_value.open.return_value = response
            with self.assertRaisesRegex(ValueError, "download-response"):
                subject.fetch_download(targets["java"]["baseUrl"], "offline-ticket-1234")

    def test_preflight_scope_rejects_unapproved_or_mixed_load_before_io(self):
        """新增用途只授权原导出预检请求，不得借此进行计时或其它写入。"""
        resources = native.resources
        value = export_scope()
        self.assertEqual(resources.EXCEL_EXPORT_PREFLIGHT_RESOURCES, native.validate(value)["purpose"])
        for patch in ({"authorizationStatus": "pending"}, {"exportPreflightRequests": 13},
                      {"readReplayRequests": 19336}, {"images": resources.INDEXED_WRITE_IMAGES}):
            with self.subTest(patch=patch), mock.patch.object(native,"verify_tool_sources", side_effect=AssertionError("禁止IO")):
                with self.assertRaises(ValueError):
                    native.run({**value, **patch})


def export_scope():
    """生成只在离线验收使用的固定导出预检范围。"""
    resources = native.resources
    return {**previous.settings(), "purpose": resources.EXCEL_EXPORT_PREFLIGHT_NATIVE,
            "images": dict(resources.UNICODE_OBSERVED_IMAGES), "relationIndexProfile": resources.RELATION_INDEX_PROFILE,
            "exportPreflightRequests": resources.EXCEL_EXPORT_PREFLIGHT_REQUESTS}


class ExportPreflightFlow(previous.FlowFixture):
    def test_native_preflight_keeps_original_seeds_and_reaches_cleanup(self):
        """走实际双端协调器及独立核零分支，替换外部环境和工作簿执行。"""
        self.value = export_scope()

        def job(root, value, name, state, path, **options):
            """提供原初始化以及共同索引的固定命令回执。"""
            state["jobs"].append({"kind": "container", "target": "init-" + name,
                                  "name": "metarill-perf-" + value["runId"] + "-init-" + name,
                                  "id": "e" * 64, "containerAbsent": True})
            if name in native.JOBS:
                return self.job(root, value, name, state, path)
            side, mode = name.split("-")[2:]
            self.events.append(name)
            return indexed.receipt(value, side, mode)

        def seeded(mysql, value, owner, model, side):
            """记录物理种子校验进入预检前执行。"""
            self.events.append("seed-" + side)
            return {"target": side}

        def checked(*args):
            """预检收据与计时结果分开。"""
            self.events.append("export")
            return {"passed": True, "measurementValid": False}

        def preserved(run, side, query, *, relation_indexes):
            """只接受未排除任何行的原种子复查。"""
            self.assertTrue(relation_indexes)
            return {"target": side}

        self.jobs.side_effect = job
        with mock.patch.object(native, "seed_measurement_database", side_effect=seeded), \
             mock.patch.object(native, "require_database_ownership"), \
             mock.patch.object(subject.oracle, "read_inputs"), \
             mock.patch.object(subject, "run_preflight", side_effect=checked), \
             mock.patch.object(subject.fixture, "verify_readback", side_effect=preserved), contextlib.redirect_stdout(io.StringIO()):
            result = native.run(self.value)
        self.assertTrue(result["passed"], (result.get("failure"), result.get("cleanupErrors"), self.events))
        self.assertEqual("paired-excel-export-preflight-only", result["acceptedScope"])
        self.assertTrue(result["measurement"]["physicalFixtureUnchanged"])
        self.assertFalse(result["performanceAccepted"])
        self.assertLess(self.events.index(native.INDEX_JOBS[-1]), self.events.index("seed-java"))
        self.assertLess(self.events.index("export"), self.events.index("database-cleanup"))
