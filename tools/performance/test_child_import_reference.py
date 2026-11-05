"""独立用途、真实协议编排及错误时的写入边界。"""
from pathlib import Path
import hmac
import tempfile
import unittest
from unittest import mock

import paired_child_import_reference as subject
import paired_native_bootstrap as native
import paired_observed_capture as capture
import test_import_measurement as previous
import test_paired_measurement as read_tests


class ChildReferenceContract(unittest.TestCase):
    def test_sql_profile_samples_only_child_requests(self):
        resources = native.resources
        value = {**previous.scope(), "purpose": resources.EXCEL_CHILD_SQL_NATIVE,
                 "images": resources.EXCEL_CHILD_REFERENCE_IMAGES, "workbookReplayRequests": 10}
        descriptor = native.validate(value)
        self.assertEqual(resources.EXCEL_CHILD_SQL_RESOURCES, descriptor["purpose"])
        self.assertTrue(resources.is_observed(descriptor))
        self.assertIn("--Metarill:Paired:SqlObservation:Enabled=true", resources.observation_arguments(descriptor, "csharp"))
        sender = mock.Mock(return_value=(200, {"code": 200}))
        exchange = capture.Exchange(value, sender)
        self.assertEqual([5, 5], [len(plan) for plan in exchange.plans.values()])
        with self.assertRaisesRegex(ValueError, "capture-request-order"):
            exchange.call("java", "identity:login-writer", "POST", "/login")
        sender.assert_not_called()
        self.assertEqual((200, {"code": 200}), exchange.call("java", "child:positive", "POST", subject.IMPORT))
        self.assertEqual(exchange.plans["java"][0]["sample"], sender.call_args.kwargs["sample"])
        for override in ({"authorizationStatus": "pending"}, {"images": previous.scope()["images"]}, {"workbookReplayRequests": 3696}):
            with mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止 IO")), self.assertRaises(ValueError):
                native.run({**value, **override})

    def test_sql_late_failure_requires_first_write_on_rolled_back_connection(self):
        run = read_tests.RUN
        database = subject.fixture.database_name(run, "java")
        name = subject.fixture.import_sample_prefix(run)+"rollback-first"
        proof = b"x"*32
        parent = str(subject.PARENT_ID)

        def command(sql, values, start, succeeded=True):
            return {"sql":sql, "connection":1, "transaction":7, "operation":"execute", "succeeded":succeeded,
                    "startedNanos":start, "finishedNanos":start+1, "affectedRows":None,
                    "parameters":[{"kind":"string", "valueHash":hmac.digest(proof,("string\0"+value).encode(),"sha256").hex()}
                                  for value in values]}

        lock = command("SELECT id FROM `sample_contract` WHERE id=? LIMIT 1 FOR UPDATE", [parent], 1)
        first = command("INSERT INTO `sample_contract_line` (name) VALUES (?)", [name, parent, "性能合同-0000"], 3)
        failure = command(first["sql"], ["bad"], 5, False)
        rollback = {"operation":"rollback", "succeeded":True, "connection":1, "transaction":7, "startedNanos":7}
        trace = {"commands":[lock,first,failure], "transactions":[rollback]}
        facts = capture.verify_child_transaction(trace,database,"java","late-row-rollback",run,proof)
        self.assertEqual((1,1,0,"rollback"), (facts["successfulInserts"],facts["failedInserts"],facts["affectedRowsReported"],facts["transaction"]))
        for invalid, error in (({**trace,"transactions":[{**rollback,"operation":"commit"}]}, "transaction-outcome"),
                               ({**trace,"commands":[lock,{**first,"transaction":8},failure]}, "transaction-owner"),
                               ({**trace,"commands":[lock,{**first,"parameters":[]} ,failure]}, "insert-bindings"),
                               ({**trace,"commands":[lock,failure]}, "rollback-writes")):
            with self.assertRaisesRegex(ValueError, error):
                capture.verify_child_transaction(invalid,database,"java","late-row-rollback",run,proof)

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
        positive = previous.workbook_stub(subject.workbook.workbook_requests(run)["line"])
        expected = {row["明细名称"]: row for row in positive["rows"]}
        physical = self.physical(expected)
        seed = subject.parent_seed_records()
        self.assertEqual(3, len(seed))
        page = [*seed.values(), *subject.verify_rows(physical, expected).values()]
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

                def send(side, name, path, body):
                    calls.append((side, path, body))
                    side_count[side] += 1
                    step = side_count[side]
                    if step == 1:
                        self.assertEqual((subject.IMPORT, str(subject.PARENT_ID), positive["rows"]),
                                         (path, body["parentId"], body["requestJson"]))
                        return 200, {"code": 200, "data": "导入成功8条数据"}
                    if step == 2:
                        self.assertEqual((subject.PAGE, {}), (path, body["filters"]))
                        return 200, {"code": 200, "data": {"total": 11, "records": page}}
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
