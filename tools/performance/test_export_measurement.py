"""固定导出回放的请求数、完整逐文件核验及性能证据门槛。"""
import itertools
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import paired_export_measurement as subject
import paired_native_bootstrap as native
import test_export_contract as previous


class ExportMeasurementTests(unittest.TestCase):
    def test_scope_requires_exact_replay_budget_before_io(self):
        """计时用途与预检、其它负载相互隔离，待批准状态不会开始文件操作。"""
        resources = native.resources
        value = previous.export_scope()
        self.assertEqual(2784, subject.REPLAY_REQUESTS)
        valid = {**value, "purpose": resources.EXCEL_EXPORT_MEASUREMENT_NATIVE,
                 "exportReplayRequests": subject.REPLAY_REQUESTS}
        self.assertEqual(resources.EXCEL_EXPORT_MEASUREMENT_RESOURCES, native.validate(valid)["purpose"])
        for invalid in ({**valid, "authorizationStatus": "pending"}, {**valid, "exportReplayRequests": 2785},
                        {**valid, "readReplayRequests": 19336}, {**value, "exportReplayRequests": 2784}):
            with mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止 I/O")):
                with self.assertRaises(ValueError):
                    native.run(invalid)

    def test_actual_threaded_runner_requires_all_downloads_before_comparison(self):
        """保留真实线程与计时，替换 HTTP 和文件语义边界，验证完整请求和失效分支。"""
        targets = {side: {"baseUrl": f"http://127.0.0.1:{port}/api", "container": side}
                   for side, port in (("java", 28001), ("csharp", 28002))}
        tokens = {(side, "writer"): side for side in targets}
        preflight = {"requests": 12, "workbooks": {side: {kind: {"semanticSha256": kind}
                    for kind in subject.export.HEADERS} for side in targets}}
        for corrupt in (False, True):
            with self.subTest(corrupt=corrupt), tempfile.TemporaryDirectory() as folder:
                serial = itertools.count()
                verified = []
                calls = []

                def ticket(target, step, variables, timeout):
                    """每次票据 POST 用普通操作员，并生成独立短期值。"""
                    kind = next(kind for kind in subject.export.HEADERS if step == subject.export.ticket_step(kind))
                    side = target["headers"]["loginStateId"]
                    variables["exportId"] = f"{side}-{kind}-{next(serial):08}"
                    calls.append("POST")

                def download(url, ticket, *, limit):
                    """每次下载使用当前页票据，不传会话头。"""
                    side, kind, _ = ticket.split("-")
                    self.assertEqual(targets[side]["baseUrl"], url)
                    self.assertEqual(24 * 1024, limit)
                    calls.append("GET")
                    return f"{side}-{kind}".encode()

                def verify(samples, parser, *, max_batches):
                    """全部下载完成后才核验响应及计数，篡改语义时不得发布比值。"""
                    self.assertEqual(subject.REPLAY_REQUESTS, len(calls))
                    self.assertEqual(1392, len(samples))
                    self.assertEqual(11, max_batches)
                    verified.extend(samples)
                    return [{"semanticSha256": "错位" if corrupt and i == 0 else kind} for i, (_, kind, _) in enumerate(samples)]

                output = Path(folder) / "measurement"
                with mock.patch.object(subject.http, "equal_limits", return_value={}), \
                     mock.patch.object(subject.http, "verify_transport", return_value={"verified": True}), \
                     mock.patch.object(subject.cgroups, "CgroupProbe", return_value=mock.Mock()), \
                     mock.patch.object(subject, "database_context", return_value={"variables": {}}), \
                     mock.patch.object(subject.export, "run_preflight", return_value=preflight), \
                     mock.patch.object(subject.http, "request", side_effect=ticket), \
                     mock.patch.object(subject.export, "fetch_download", side_effect=download), \
                     mock.patch.object(subject.export, "verify_workbooks", side_effect=verify):
                    if corrupt:
                        with self.assertRaisesRegex(ValueError, "replay-semantics"):
                            subject.run_measurement(Path(folder), "a" * 32, targets, tokens, None, None, None)
                        self.assertFalse((output / "measurement.json").exists())
                    else:
                        result = subject.run_measurement(Path(folder), "a" * 32, targets, tokens, None, None, None)
                        self.assertEqual(6, len(result["comparison"]))
                        self.assertEqual(1392, result["workbooksVerified"])
                        self.assertTrue(all(r["comparison"]["valid"] for r in result["comparison"]))
                        self.assertFalse(result["measurementValid"])
                        self.assertEqual(subject.cgroups.METHOD, result["resourceSource"])
                self.assertEqual((1392, 1392), (calls.count("POST"), calls.count("GET")))
                self.assertEqual(1392, len(verified))
                self.assertEqual(16, len((output / "events.jsonl").read_bytes().splitlines()))
