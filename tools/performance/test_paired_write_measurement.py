"""原 CRUD 性能合同的本机回环与只读结果回归。"""
import json
from pathlib import Path
import tempfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import threading
import unittest
from unittest import mock

import paired_http as http
import paired_write_measurement as write

RUN = "ab" * 16
IDENTIFIER = "9007199254740993"


class WriteMeasurementTests(unittest.TestCase):
    def test_exact_schedule_ownership_and_writer_identity(self):
        """原读轮次不借给写入；每端 616 次场景，名字在本轮唯一。"""
        targets = {
            side: {"baseUrl": f"http://127.0.0.1:{port}/api", "container": f"metarill-perf-{RUN}-{side}",
                   "containerPort": 8080, "expectedImageId": "sha256:" + "a" * 64}
            for side, port in (("java", 20001), ("csharp", 20002))
        }
        tokens = {(side, "writer"): f"{side}-test-token" for side in targets}
        config = write.config_for(RUN, targets, tokens)
        self.assertEqual("java-test-token", config["targets"]["java"]["headers"]["loginStateId"])
        names = write.expected_names(RUN)
        self.assertEqual(616, len(names))
        self.assertLessEqual(max(map(len, names)), 100)
        self.assertEqual(8624, 2 * len(write.expected_names(RUN)) * len(write.write_scenario()["steps"]))
        sql = write.readback_sql(RUN, "java")
        self.assertIn(f"metarill_fresh_{RUN[:16]}_paired_java", sql)
        self.assertIn("LIMIT 617", sql)
        with self.assertRaises(ValueError):
            write.readback_sql(RUN, "other")
        with self.assertRaises(ValueError):
            write.readback_sql(RUN + "'", "java")

    def test_real_loopback_requests_keep_omission_null_and_string_id(self):
        """通过真实 HTTP 回放验证请求体和各次读写响应形状。"""
        variables = {"run": RUN, "phase": "warmup", "concurrency": 1, "round": -1, "iteration": 0}
        original = http.expand(write.NAME_PATTERN, variables)
        renamed = original + "-u"
        saved = lambda name, remarks: {"id": IDENTIFIER, "name": name, "remarks": remarks}
        observed = []
        responses = [
            saved(original, write.REMARKS), saved(original, write.REMARKS),
            saved(renamed, write.REMARKS), saved(renamed, None), saved(renamed, None),
            "删除成功", 0,
        ]
        alternate_id = str(int(IDENTIFIER) + 1)
        responses.extend(dict(item, id=alternate_id) if isinstance(item, dict) else item for item in responses[:])

        class Handler(BaseHTTPRequestHandler):
            def reply(self):
                """以已知原协议应答，保留测试实际收到的请求。"""
                length = int(self.headers.get("Content-Length", "0"))
                body = json.loads(self.rfile.read(length)) if length else None
                response = http.canonical({"code": 200, "data": responses[len(observed)]})
                observed.append((self.command, self.path, body))
                self.send_response(200)
                self.send_header("Content-Length", str(len(response)))
                self.end_headers()
                self.wfile.write(response)

            do_GET = do_POST = do_DELETE = reply

            def log_message(self, *_):
                """回环测试不输出逐条请求日志。"""

        with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
            worker = threading.Thread(target=server.serve_forever)
            worker.start()
            try:
                target = {"baseUrl": f"http://127.0.0.1:{server.server_port}/api"}
                alternate = dict(variables)
                fingerprints = [
                    [write.write_request(target, step, context, 2) for step in write.write_scenario()["steps"]]
                    for context in (variables, alternate)
                ]
            finally:
                server.shutdown()
                worker.join()
        self.assertEqual(IDENTIFIER, variables["record"])
        self.assertEqual(alternate_id, alternate["record"])
        self.assertEqual(*fingerprints)
        self.assertTrue(observed[8][1].endswith("/detail/" + alternate_id))
        self.assertEqual(["POST", "GET", "POST", "POST", "GET", "DELETE", "POST"],
                         [method for method, _, _ in observed[:7]])
        self.assertTrue(observed[1][1].endswith("/detail/" + IDENTIFIER))
        self.assertEqual({"id": IDENTIFIER, "name": renamed}, observed[2][2])
        self.assertEqual({"id": IDENTIFIER, "remarks": None}, observed[3][2])
        self.assertEqual({"id": IDENTIFIER}, observed[5][2])
        self.assertEqual({"name": renamed}, observed[6][2]["filters"])

    def test_numeric_create_id_is_rejected_before_next_step(self):
        """通用传输允许整数捕获，写入场景必须拒绝它。"""
        step = write.write_scenario()["steps"][0]
        with mock.patch.object(http, "request", return_value="digest"), self.assertRaises(ValueError):
            write.write_request({}, step, {"record": int(IDENTIFIER)}, 2)

    def test_postflight_demands_every_exact_tombstone(self):
        """漏写、重复、活动行或未清空备注均拒绝通过。"""
        names = sorted(write.expected_names(RUN))
        rows = [f"{int(IDENTIFIER) + index}\t{name.encode().hex()}\t1\t1"
                for index, name in enumerate(names)]
        self.assertEqual({"verifiedRows": 0}, write.verify_readback(RUN, "", before=True))
        self.assertEqual({"verifiedRows": 616}, write.verify_readback(RUN, "\n".join(rows)))
        for invalid in (rows[:-1], rows + rows[:1], rows[:-1] + [rows[-1][:-1] + "0"],
                        rows[:-1] + [rows[-1][:-3] + "0\t1"]):
            with self.subTest(size=len(invalid)), self.assertRaises(ValueError):
                write.verify_readback(RUN, "\n".join(invalid))
        with self.assertRaises(ValueError):
            write.verify_readback(RUN, rows[0], before=True)


    def test_request_error_closes_guard_before_another_http_request(self):
        """即使测量器还有工作项，首个错误后也不能继续实际写入。"""
        def run(output,config,probe,query,profile,*,transport,check_event):
            step = config["scenarios"][0]["steps"][0]
            with self.assertRaises(TimeoutError):
                transport({},step,{},1)
            with self.assertRaisesRegex(ValueError,"measurement-write-stopped"):
                transport({},step,{},1)
            event={"kind":"trial","concurrency":4,"round":0,"trial":{"failedScenarios":1}}
            check_event(event)

        with tempfile.TemporaryDirectory() as folder, \
             mock.patch.object(write,"config_for",return_value={"scenarios":[write.write_scenario()]}), \
             mock.patch.object(write,"run_trials",side_effect=run), \
             mock.patch.object(write,"write_request",side_effect=TimeoutError) as actual, \
             self.assertRaisesRegex(ValueError,"measurement-write-http-failed"):
            write.run_write_measurement(Path(folder),RUN,{}, {},None,None)
        actual.assert_called_once()

    def test_warmup_or_measured_response_mismatch_stops_next_trial(self):
        """原业务响应不等价立即中断，不用后续轮次覆盖差异。"""
        for kind in ("warmup","trial"):
            with self.subTest(kind=kind):
                def run(output,config,probe,query,profile,*,transport,check_event):
                    for result in ("java","csharp"):
                        check_event({"kind":kind,"concurrency":1,"round":0,
                                     "trial":{"failedScenarios":0,"rows":[{"fingerprints":[result]}]}})

                with tempfile.TemporaryDirectory() as folder, \
                     mock.patch.object(write,"config_for",return_value={}), \
                     mock.patch.object(write,"run_trials",side_effect=run), \
                     self.assertRaisesRegex(ValueError,"measurement-write-response-mismatch"):
                    write.run_write_measurement(Path(folder),RUN,{}, {},None,None)
