"""独立用途、真实协议编排及错误时的写入边界。"""
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import paired_child_import_reference as subject
import paired_native_bootstrap as native
import test_import_measurement as previous
import test_paired_measurement as read_tests


class ChildReferenceContract(unittest.TestCase):
    def test_reference_image_and_budget_are_isolated(self):
        resources = native.resources
        original = previous.scope()
        value = {**original, "purpose": resources.EXCEL_CHILD_REFERENCE_NATIVE,
                 "images": resources.EXCEL_CHILD_REFERENCE_IMAGES, "workbookReplayRequests": 10}
        descriptor = native.validate(value)
        self.assertEqual(resources.EXCEL_CHILD_REFERENCE_RESOURCES, descriptor["purpose"])
        self.assertTrue(resources.uses_relation_indexes(descriptor))
        self.assertTrue(resources.is_measurement(descriptor))
        self.assertFalse(resources.is_observed(descriptor))
        for override in ({"images": original["images"]}, {"purpose": original["purpose"]},
                         {"workbookReplayRequests": original["workbookReplayRequests"]},
                         {"writeReplayRequests": 1}, {"authorizationStatus": "pending"}):
            with mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止 IO")), self.assertRaises(ValueError):
                native.run({**value, **override})

    def test_physical_readback_rejects_parent_or_rollback_leak(self):
        """同样响应无法掩盖错误父键、遗留首行或误入成功集的第二行。"""
        run = read_tests.RUN
        positive = previous.workbook_stub(subject.workbook.workbook_requests(run)["line"])
        expected = {row["明细名称"]: row for row in positive["rows"]}
        raw = self.physical(expected)
        self.assertEqual(8, len(subject.verify_rows(raw, expected)))
        with self.assertRaisesRegex(ValueError, "reference-parent"):
            subject.verify_rows(raw.replace(f"\t{subject.PARENT_ID}\t", "\tNULL\t"), expected)
        with self.assertRaisesRegex(ValueError, "reference-row"):
            subject.verify_rows(raw + "\n" + raw.splitlines()[0], expected)

    def test_exact_requests_accept_visibility_and_reject_side_effects(self):
        run = read_tests.RUN
        prefix = subject.fixture.import_sample_prefix(run)
        positive = previous.workbook_stub(subject.workbook.workbook_requests(run)["line"])
        expected = {row["明细名称"]: row for row in positive["rows"]}
        physical = self.physical(expected)
        page = list(subject.verify_rows(physical, expected).values())
        for leak in (False, True):
            with self.subTest(leak=leak), tempfile.TemporaryDirectory() as folder:
                calls = []
                side_count = {"java": 0, "csharp": 0}

                def query(sql):
                    side = "java" if "paired_java" in sql else "csharp"
                    if "CHARACTER_MAXIMUM_LENGTH" in sql:
                        return "255"
                    if "IF(delete_no IS NULL,1,0) FROM" in sql:
                        return f"{subject.PARENT_ID}\t1\n{subject.DELETED_PARENT_ID}\t0"
                    if sql == subject.workbook.sample_sql(run, side, "line") and side_count[side]:
                        return physical + "\n" if leak and side_count[side] == 5 else physical
                    return ""

                def send(side, path, body):
                    calls.append((side, path, body))
                    side_count[side] += 1
                    step = side_count[side]
                    if step == 1:
                        self.assertEqual((subject.IMPORT, str(subject.PARENT_ID), positive["rows"]),
                                         (path, body["parentId"], body["requestJson"]))
                        return 200, {"code": 200, "data": "导入成功8条数据"}
                    if step == 2:
                        self.assertEqual((subject.PAGE, {"name": prefix}), (path, body["filters"]))
                        return 200, {"code": 200, "data": {"total": 8, "records": page}}
                    self.assertEqual(subject.IMPORT, path)
                    self.assertEqual(str((subject.MISSING_PARENT_ID, subject.DELETED_PARENT_ID, subject.PARENT_ID)[step - 3]),
                                     body["parentId"])
                    if step == 5:
                        self.assertEqual(2, len(body["requestJson"]))
                        self.assertTrue(body["requestJson"][0]["明细名称"].endswith("rollback-first"))
                        self.assertGreater(len(body["requestJson"][1]["明细名称"]), 255)
                    return 200, {"code": 500}

                if leak:
                    with self.assertRaisesRegex(ValueError, "late-row-rollback-physical"):
                        subject.run_preflight(Path(folder), run, query, send, previous.workbook_stub)
                    self.assertEqual(5, len(calls))
                else:
                    result = subject.run_preflight(Path(folder), run, query, send, previous.workbook_stub)
                    self.assertEqual((10, 0), (result["requests"], result["timedRequests"]))
                    self.assertEqual({"java": 5, "csharp": 5}, side_count)

    @staticmethod
    def physical(rows):
        """模拟独立查询 TSV，保留原关系缓存、ID 与精确金额。"""
        text = lambda value: value.encode("utf-8").hex().upper()
        return "\n".join("\t".join((str(9107199254800000 + index), text(name), text(row["备注"]), "1", "1",
                                    str(subject.PARENT_ID), text("性能合同-0000"), str(row["明细金额"])))
                         for index, (name, row) in enumerate(rows.items()))
