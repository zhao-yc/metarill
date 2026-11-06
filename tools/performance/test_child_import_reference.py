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
    def test_layout_profile_is_bound_to_its_own_image_and_data(self):
        resources = native.resources
        value = {**previous.scope(), "purpose": resources.EXCEL_CHILD_LAYOUT_SQL_NATIVE,
                 "images": resources.EXCEL_CHILD_LAYOUT_IMAGES, "workbookReplayRequests": 10}
        descriptor = native.validate(value)
        self.assertEqual(resources.EXCEL_CHILD_LAYOUT_SQL_RESOURCES, descriptor["purpose"])
        self.assertTrue(resources.uses_relation_indexes(descriptor))
        self.assertIn("--Metarill:Paired:SqlObservation:ExpectedDataSha256="+resources.EXCEL_CHILD_LAYOUT_DATA_SHA256,
                      resources.observation_arguments(descriptor,"csharp"))
        self.assertEqual([5,5],[len(plan) for plan in capture.Exchange(value,mock.Mock()).plans.values()])
        for override in ({"images":resources.EXCEL_CHILD_REFERENCE_IMAGES},
                         {"purpose":resources.EXCEL_CHILD_SQL_NATIVE},{"workbookReplayRequests":11}):
            with mock.patch.object(native,"verify_tool_sources",side_effect=AssertionError("禁止 IO")),self.assertRaises(ValueError):
                native.run({**value,**override})

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

    def test_combined_insert_proves_one_command_and_preserves_unread_late_result(self):
        run = read_tests.RUN
        database = subject.fixture.database_name(run, "csharp")
        parent = str(subject.PARENT_ID)
        proof = b"x"*32
        source = f"`{database}`.`sample_contract_line`"
        target = f"`{database}`.`sample_contract`"
        guarded = "b.`id`=CASE WHEN CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY) THEN @exact_id END"
        identity = "SELECT DATABASE(),@@SESSION.max_allowed_packet"
        lock_read = f"SELECT b.`id` FROM {source} b LEFT JOIN {target} r0 ON 1=1 WHERE {guarded} FOR UPDATE OF b FOR SHARE OF r0"
        update = (f"UPDATE {source} b SET `sample_contract_name`=(SELECT r0.`name` FROM {target} r0 "
                  f"WHERE 1=1 FOR SHARE OF r0) WHERE {guarded} AND NOT (NULL <=> NULL)")
        readback = f"SELECT b.`id`,b.`sample_contract_name` FROM {source} b WHERE {guarded} FOR UPDATE OF b"
        insert = (f"INSERT INTO {source}(`id`, `name`, `parent_id`) SELECT @Id_0, @Name_0, @ParentId_0 "
                  "WHERE CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY)")
        batch = "; ".join((identity, insert, "SELECT ROW_COUNT()", lock_read, update, "SELECT ROW_COUNT()", readback))

        def parameter(name, kind, value):
            return {"name":name, "valueKind":kind,
                    "valueHash":hmac.digest(proof,(kind+"\0"+str(value)).encode(),"sha256").hex()}

        def command(sql, start, params=(), operation="reader", affected=None):
            return {"sql":sql, "connection":1, "transaction":7, "operation":operation, "succeeded":True,
                    "startedTimestamp":start, "finishedTimestamp":start+1, "affectedRows":affected, "parameters":params}

        def transaction(operation, start):
            return {"operation":operation, "succeeded":True, "connection":1, "transaction":7,
                    "isolation":"RepeatableRead", "startedTimestamp":start, "finishedTimestamp":start+1}

        parent_parameter = parameter("@ParentId_0","string",parent)
        database_parameter = parameter("@expected_database","string",database)
        lock = command(f"SELECT a.`id` FROM {target} a WHERE a.`id`=@ParentId_0 FOR UPDATE", 2,[parent_parameter])

        def append(rows, end):
            name, *following = rows
            first = command(f"INSERT INTO {source}(`name`) VALUES(@Name_0)",4,
                            [parameter("@Name_0","string",name), parent_parameter, parameter("@Id_0","integer",1)],
                            "nonQuery",1)
            cache = command("; ".join((identity,lock_read,update,readback)),6,
                            [database_parameter,parameter("@exact_id","integer",1)])
            merged = [command(batch,6+identifier*2,[database_parameter,parent_parameter,
                              parameter("@Id_0","integer",identifier),parameter("@exact_id","integer",identifier),
                              parameter("@Name_0","string",item)])
                      for identifier,item in enumerate(following,2)]
            return {"commands":[lock,first,cache,*merged], "transactions":[transaction("begin",0),transaction(end,40)]}

        names = [row[0] for row in subject.workbook.workbook_requests(run)["line"]["rows"]]
        positive = append(names,"commit")
        facts = capture.verify_child_transaction(positive,database,"csharp","positive",run,proof)
        self.assertEqual((8,8,7,0), tuple(facts[key] for key in
                         ("successfulInserts","cacheUpdates","combinedInsertCacheCommands","insertResultsUnobserved")))

        prefix = subject.fixture.import_sample_prefix(run)
        rollback = append([prefix+"rollback-first",prefix+"rollback-last-"+"长"*2048],"rollback")
        facts = capture.verify_child_transaction(rollback,database,"csharp","late-row-rollback",run,proof)
        self.assertEqual((2,1,0,1), tuple(facts[key] for key in
                         ("childInserts","successfulInserts","failedInserts","insertResultsUnobserved")))
        last = rollback["commands"][-1]
        for replacement, problem in (
            ({"sql":batch.replace("SELECT ROW_COUNT()", "SELECT 1",1)}, "insert-batch-shape"),
            ({"parameters":[*last["parameters"][:-2],parameter("@exact_id","integer",99),last["parameters"][-1]]},
             "insert-batch-binding"),
            ({"parameters":last["parameters"][:-1]}, "late-insert-batch"),
        ):
            wrong = {**rollback,"commands":[*rollback["commands"][:-1],{**last,**replacement}]}
            with self.assertRaisesRegex(ValueError,"capture-child-"+problem):
                capture.verify_child_transaction(wrong,database,"csharp","late-row-rollback",run,proof)

    def test_sql_csharp_inline_id_requires_the_exact_single_literal(self):
        run = read_tests.RUN
        database = subject.fixture.database_name(run, "csharp")
        parent = str(subject.DELETED_PARENT_ID)
        statement = "SELECT a.`id` FROM `sample_contract` a WHERE (((a.`id`) in ({}))) FOR UPDATE"
        rollback = {"operation":"rollback", "succeeded":True, "connection":1, "transaction":7, "startedTimestamp":3}
        lock = {"sql":statement.format(parent), "connection":1, "transaction":7, "succeeded":True,
                "startedTimestamp":1, "finishedTimestamp":2, "parameters":[]}
        trace = {"commands":[lock], "transactions":[rollback]}
        facts = capture.verify_child_transaction(trace,database,"csharp","deleted-parent",run,b"x"*32)
        self.assertEqual(["literal-integer"],facts["parentLockBindings"])
        for value in (parent+"0", parent+",1", "'"+parent+"'"):
            with self.assertRaisesRegex(ValueError,"capture-child-parent-lock"):
                capture.verify_child_transaction({**trace,"commands":[{**lock,"sql":statement.format(value)}]},
                                                 database,"csharp","deleted-parent",run,b"x"*32)

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
