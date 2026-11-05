"""导入负载用真实参数编排和物理回读断言，不连接外部资源。"""
import base64
import contextlib
import hashlib
import io
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import paired_import_measurement as subject
import paired_cgroup_probe as cgroups
import paired_measurement_fixture as fixture
import paired_native_bootstrap as native
import test_indexed_write_measurement as indexed
import test_paired_native_bootstrap as previous
import test_paired_measurement as read_tests


def scope():
    """以虚构双库和固定当前镜像构建离线合同。"""
    resources = native.resources
    value = previous.settings()
    value.update(purpose=resources.EXCEL_IMPORT_MEASUREMENT_NATIVE,
                 images=dict(resources.UNICODE_OBSERVED_IMAGES),
                 relationIndexProfile=resources.RELATION_INDEX_PROFILE,
                 workbookReplayRequests=resources.EXCEL_IMPORT_REQUEST_BUDGET)
    return value


def workbook_stub(request):
    """伪造文件仅用于离线协议验证；真实 SheetJS 字节另行运行。"""
    raw = b"PK\x03\x04offline-contract-only"
    return {"format": "xlsx", "byteLength": len(raw), "sha256": hashlib.sha256(raw).hexdigest(),
            "sheetNames": ["sheet1"], "headers": request["headers"],
            "rows": [dict(zip(request["headers"], row)) for row in request["rows"]],
            "cells": [request["headers"], *request["rows"]], "sheetJsVersion": "0.20.3",
            "nodeVersion": "v24.14.0", "workbookBase64": base64.b64encode(raw).decode()}


def wire(run, kind, replays=subject.REPLAYS):
    """按固定解析行构造物理输出，覆盖所有重复批次，目标 ID 不与原种子交叠。"""
    result = []
    encoded = lambda value: value.encode("utf-8").hex().upper()
    for i, row in enumerate(subject.workbook_requests(run)[kind]["rows"]):
        name = row[0]
        for repeat in range(replays):
            fields = [str(1 + i * replays + repeat), encoded(name),
                      encoded(row[-2 if kind == "contract" else -1]), "1", "1"]
            if kind == "contract":
                fields += [str(fixture.ID_BASE), encoded("性能客户-0000"), f"{row[2]:.2f}", "0"]
            elif kind == "line":
                fields += [str(fixture.ID_BASE + 10000), encoded("性能合同-0000"), f"{row[1]:.2f}"]
            result.append("\t".join(fields))
    return "\n".join(result)


class ImportMeasurementContracts(unittest.TestCase):
    def test_pending_wrong_budget_or_image_is_rejected_before_io(self):
        """新用途只在当前镜像、共同索引和 3696 固定请求的组合下启动。"""
        resources = native.resources
        value = scope()
        descriptor = native.validate(value)
        self.assertEqual(resources.EXCEL_IMPORT_MEASUREMENT_RESOURCES, descriptor["purpose"])
        self.assertTrue(resources.uses_relation_indexes(descriptor))
        self.assertFalse(resources.is_observed(descriptor))
        self.assertEqual(["--Metarill:Paired:SqlObservation:Enabled=false"], resources.observation_arguments(descriptor, "csharp"))
        for change in ({"authorizationStatus": "pending"}, {"workbookReplayRequests": 3697},
                       {"images": resources.INDEXED_WRITE_IMAGES}, {"writeReplayRequests": 8624},
                       {"fileReplayRequests": 4834}, {"relationIndexProfile": None}):
            with self.subTest(change=change), mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止IO")), self.assertRaises(ValueError):
                native.run({**value, **change})

    def test_parent_profile_keeps_fixed_lease_and_two_original_routes(self):
        """父表计时与历史三页授权隔离，保留真实工作簿、原关系列和对应的固定预算。"""
        resources = native.resources
        parent = subject.PARENT_PAGES
        value = {**scope(), "purpose": resources.EXCEL_IMPORT_PARENT_NATIVE, "workbookReplayRequests": parent.requests}
        self.assertEqual(resources.EXCEL_IMPORT_PARENT_RESOURCES, native.validate(value)["purpose"])
        for bad in ({**value, "workbookReplayRequests": 3696}, {**scope(), "workbookReplayRequests": 928},
                    {**value, "authorizationStatus": "pending"}):
            with mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止IO")), self.assertRaises(ValueError):
                native.run(bad)
        run = read_tests.RUN
        workbooks = subject.prepare_workbooks(run, workbook_stub, parent)
        targets = {side: {**item, "expectedImageId": resources.UNICODE_OBSERVED_IMAGES[side]}
                   for side, item in read_tests.TARGETS.items()}
        tokens = {(side, "writer"): "offline" for side in targets}
        config = subject.config_for(run, targets, tokens, workbooks, parent)
        self.assertEqual((36, 232, 928), (config["iterations"], parent.replays, parent.requests))
        steps = config["scenarios"][0]["steps"]
        self.assertEqual(["importDatas81001", "importDatas81002"], [s["path"].rsplit("/", 1)[1] for s in steps])
        self.assertTrue(all("parentId" not in step["json"] for step in steps))
        self.assertEqual("性能客户-0000", steps[1]["json"]["requestJson"][0]["客户"])
        with self.assertRaises(ValueError):
            subject.http.validate(config)  # 通用 CLI 仍要求至少 100 个场景。

    def test_parent_profile_still_reads_and_rejects_unexpected_child_rows(self):
        """父表仅计时已对照的两页，但不能忽略同一运行前缀的子表副作用。"""
        run = read_tests.RUN
        parent = subject.PARENT_PAGES
        sql = {kind: subject.sample_sql(run, "java", kind, parent) for kind, *_ in subject.TABLES}
        rows = {sql[kind]: wire(run, kind, parent.replays) for kind, *_ in parent.tables}
        rows[sql["line"]] = ""
        self.assertEqual({"customer": 1856, "contract": 1856},
                         subject.verify_readback(run, "java", rows.__getitem__, workload=parent))
        rows[sql["line"]] = wire(run, "line", subject.WARMUP)
        with self.assertRaisesRegex(ValueError, "sample-collision"):
            subject.verify_readback(run, "java", rows.__getitem__, workload=parent)

    def test_parent_runner_requires_two_physical_warmups_and_complete_append_counts(self):
        """按真实协调流程执行两档独立预热和最终读回，并绑定已证的内核探针。"""
        parent = subject.PARENT_PAGES
        run = read_tests.RUN
        targets = {side: {**item, "expectedImageId": native.resources.UNICODE_OBSERVED_IMAGES[side]}
                   for side, item in read_tests.TARGETS.items()}
        tokens = {(side, "writer"): "offline" for side in targets}
        layouts = {subject.sample_sql(run, side, kind, parent): kind for side in targets for kind, *_ in subject.TABLES}
        sampler = mock.Mock()
        count = 0

        def query(sql):
            """返回固定页的完整物理追加，子页始终为空。"""
            kind = layouts[sql]
            return wire(run, kind, count) if count and kind != "line" else ""

        def trials(output, config, probe, reader, profile, *, transport, check_event, minimum_iterations, resource_probe):
            """按原回放事件驱动真实预热检查，限制只为本负载显式传入。"""
            nonlocal count
            self.assertEqual(36, minimum_iterations)
            self.assertIs(sampler, resource_probe)
            self.assertEqual(parent.name, profile)
            for level, count in ((1, 8), (4, 124)):
                for side in targets:
                    rows = [{"fingerprints": ["客户", "合同"]} for _ in range(8)]
                    check_event({"kind": "warmup", "concurrency": level, "target": side,
                                 "trial": {"failedScenarios": 0, "rows": rows}})
            count = parent.replays
            return {"measurementValid": True, "stableRegression": False}

        with tempfile.TemporaryDirectory() as folder, mock.patch.object(subject, "run_trials", side_effect=trials), \
             mock.patch.object(subject.http, "equal_limits", return_value={}), \
             mock.patch.object(subject.http, "verify_transport"), mock.patch.object(cgroups, "CgroupProbe", return_value=sampler):
            result = subject.run_import_measurement(Path(folder), run, targets, tokens, None, query, workbook_stub, workload=parent)
            self.assertEqual({side: {"customer": 1856, "contract": 1856} for side in targets}, result["importReadback"])
            self.assertEqual(cgroups.METHOD, result["resourceSource"])
            self.assertEqual(24, len(list((Path(folder) / "measurement").glob("*.tsv"))))
            self.assertEqual(2, len(list((Path(folder) / "measurement").glob("*.xlsx"))))
        sampler.verify.assert_called_once()

    def test_real_routes_share_verified_parse_results_and_fixed_parent(self):
        """同一工作簿解析结果进入两端原路由；伪造格值、父范围或容器镜像均拒绝。"""
        run = read_tests.RUN
        workbooks = subject.prepare_workbooks(run, workbook_stub)
        targets = {side: {**item, "expectedImageId": native.resources.UNICODE_OBSERVED_IMAGES[side]}
                   for side, item in read_tests.TARGETS.items()}
        tokens = {(side, "writer"): "offline" for side in targets}
        cfg = subject.config_for(run, targets, tokens, workbooks)
        steps = cfg["scenarios"][0]["steps"]
        self.assertEqual(3696, subject.REQUESTS)
        self.assertEqual([1, 4], cfg["concurrency"])
        for step, kind in zip(steps, ("customer", "contract", "line")):
            self.assertEqual(workbooks[kind]["rows"], step["json"]["requestJson"])
        self.assertTrue(steps[2]["path"].endswith("/importDatas81003"))
        self.assertEqual(str(fixture.ID_BASE + 10000), steps[2]["json"]["parentId"])
        workbooks["customer"]["rows"][0]["备注"] = "篡改"
        with self.assertRaisesRegex(ValueError, "created-rows"):
            subject.config_for(run, targets, tokens, workbooks)

    def test_physical_readback_rejects_missing_duplicate_or_wrong_parent(self):
        """验证三表全量及数值、父键、原索引范围，不能只比较导入响应。"""
        run = read_tests.RUN
        data = {subject.sample_sql(run, "java", kind): wire(run, kind) for kind, *_ in subject.TABLES}
        self.assertEqual(dict.fromkeys(("customer", "contract", "line"), 4928),
                         subject.verify_readback(run, "java", data.__getitem__))
        with self.assertRaisesRegex(ValueError, "sample-collision"):
            subject.verify_readback(run, "java", data.__getitem__, before=True)
        customer = subject.sample_sql(run, "java", "customer")
        missing = {**data, customer: data[customer].rsplit("\n", 1)[0]}
        with self.assertRaisesRegex(ValueError, "sample-count"):
            subject.verify_readback(run, "java", missing.__getitem__)
        line = subject.sample_sql(run, "java", "line")
        bad = {**data, line: data[line].replace(str(fixture.ID_BASE + 10000), str(fixture.ID_BASE + 10001), 1)}
        with self.assertRaisesRegex(ValueError, "sample-parent"):
            subject.verify_readback(run, "java", bad.__getitem__)
        for _, table, _, _ in subject.TABLES:
            sql = fixture.readback_sql(run, "java", table, exclude_import_samples=True)
            self.assertIn(fixture.import_sample_condition(run), sql)
            with self.assertRaises(ValueError):
                fixture.readback_sql(run, "java", table, exclude_import_samples=True, exclude_write_samples=True)


    def test_warmup_requires_both_responses_and_physical_rows_before_trials(self):
        """按真实预热事件进入两档回读；任何错位都会在正式请求前拒绝。"""
        run = read_tests.RUN
        targets = {side: {**item, "expectedImageId": native.resources.UNICODE_OBSERVED_IMAGES[side]}
                   for side, item in read_tests.TARGETS.items()}
        tokens = {(side, "writer"): "offline" for side in targets}
        lookups = {subject.sample_sql(run, side, kind): kind
                   for side in targets for kind, *_ in subject.TABLES}
        for failure in (None, "warmup-response", "sample-parent"):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as folder:
                stage = [0]

                def query(sql):
                    raw = wire(run, lookups[sql], stage[0]) if stage[0] else ""
                    if failure == "sample-parent" and stage[0] and sql == subject.sample_sql(run, "java", "line"):
                        parent = str(fixture.ID_BASE + 10000)
                        return raw.replace(f"\t{parent}\t", "\tNULL\t")
                    return raw

                def trials(output, config, probe, query, profile, *, transport, check_event):
                    for concurrency, replays in ((1, 8), (4, 316)):
                        stage[0] = replays
                        for side in targets:
                            fingerprint = "错位" if failure == "warmup-response" and side == "csharp" else "一致"
                            check_event({"kind": "warmup", "concurrency": concurrency, "target": side,
                                         "trial": {"failedScenarios": 0, "rows": [
                                             {"fingerprints": [fingerprint] * 3} for _ in range(8)]}})
                    stage[0] = subject.REPLAYS
                    return {"measurementValid": True}

                with mock.patch.object(subject, "run_trials", side_effect=trials):
                    if failure:
                        with self.assertRaisesRegex(ValueError, failure):
                            subject.run_import_measurement(Path(folder), run, targets, tokens, None, query, workbook_stub)
                        self.assertFalse((Path(folder) / "measurement/warmup-readback.json").exists())
                        snapshots = (Path(folder) / "measurement").glob("import-warmup-1-*.tsv")
                        self.assertEqual(6 if failure == "sample-parent" else 0, len(list(snapshots)))
                    else:
                        result = subject.run_import_measurement(Path(folder), run, targets, tokens, None, query, workbook_stub)
                        self.assertEqual(4928, result["importReadback"]["csharp"]["line"])
                        self.assertEqual(24, len(list((Path(folder) / "measurement").glob("*.tsv"))))

    def test_failed_transport_never_sends_a_following_request(self):
        """首个业务失败后，即使测量器尝试别的步骤也不发请求。"""
        run = read_tests.RUN
        targets = {side: {**item, "expectedImageId": native.resources.UNICODE_OBSERVED_IMAGES[side]}
                   for side, item in read_tests.TARGETS.items()}
        tokens = {(side, "writer"): "offline" for side in targets}

        def trials(output, config, probe, query, profile, *, transport, check_event):
            target = config["targets"]["java"]
            step = config["scenarios"][0]["steps"][0]
            with self.assertRaisesRegex(ValueError, "模拟失败"):
                transport(target, step, {}, 1)
            with self.assertRaisesRegex(ValueError, "measurement-import-stopped"):
                transport(target, step, {}, 1)
            return {"measurementValid": False}

        with tempfile.TemporaryDirectory() as folder, mock.patch.object(subject, "run_trials", side_effect=trials), \
             mock.patch.object(subject.http, "request", side_effect=ValueError("模拟失败")) as request:
            result = subject.run_import_measurement(Path(folder), run, targets, tokens, None, lambda _: "", workbook_stub)
            request.assert_called_once()
            self.assertFalse(result["measurementValid"])
            self.assertNotIn("importReadback", result)


class ImportMeasurementFlow(previous.FlowFixture):
    def setUp(self):
        """实际走协调器，只替换所有外部边界。"""
        super().setUp()
        self.value = scope()
        catalog = self.root / "bootstrap/java/project-config.json"
        catalog.parent.mkdir(parents=True)
        catalog.write_bytes(b"offline-fixture")
        self.enterContext(mock.patch.object(subject, "JAVA_CATALOG_SHA256", hashlib.sha256(catalog.read_bytes()).hexdigest()))
        self.jobs.side_effect = self.index_job

    def index_job(self, root, value, name, state, path, *, expected_index_plan=None):
        """沿固定关系索引协议返回预览、确认和核验回执。"""
        state["jobs"].append({"kind": "container", "target": "init-" + name,
                              "name": "metarill-perf-" + value["runId"] + "-init-" + name,
                              "id": "e" * 64, "containerAbsent": True})
        if name in native.JOBS:
            return super().job(root, value, name, state, path)
        self.events.append(name)
        side, mode = name.split("-")[2:]
        if mode == "apply":
            self.assertEqual("e" * 64, expected_index_plan)
        return indexed.receipt(value, side, mode)

    def test_actual_native_entry_reaches_import_only_after_indexes_and_rechecks_seed(self):
        """协调器只进入三页导入，在结束前独立核对原种子并清理。"""
        def seeded(mysql, value, owner, model, side):
            self.events.append("seed-" + side)
            return {"target": side}

        def measured(*args):
            self.events.append("import")
            return {"measurementValid": True, "stableRegression": False}

        def preserved(run, side, query, *, relation_indexes=False, exclude_import_samples=False):
            self.assertTrue(relation_indexes and exclude_import_samples)
            return {"target": side}

        with mock.patch.object(native, "seed_measurement_database", side_effect=seeded), \
             mock.patch.object(native, "require_database_ownership"), \
             mock.patch.object(subject.oracle, "read_inputs"), \
             mock.patch.object(subject, "run_import_measurement", side_effect=measured), \
             mock.patch.object(fixture, "verify_readback", side_effect=preserved), contextlib.redirect_stdout(io.StringIO()):
            result = native.run(self.value)
        self.assertTrue(result["passed"])
        self.assertEqual("paired-excel-import-measurement-only", result["acceptedScope"])
        self.assertTrue(result["measurement"]["physicalFixtureUnchanged"])
        self.assertFalse(result["performanceAccepted"])
        self.assertLess(self.events.index(native.INDEX_JOBS[-1]), self.events.index("seed-java"))
        self.assertLess(self.events.index("import"), self.events.index("database-cleanup"))
