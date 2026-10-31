"""实际回环下载验证完整性、失败边界及有效字节吞吐，不执行SSH、Docker或MySQL。"""
import copy
import hashlib
import http.server
import io
import json
import threading
import time
import unittest
import urllib.error
import urllib.parse
from unittest import mock
from test_paired_http import subject, config

PAYLOAD = bytes(range(256)) * 4096
CHECKSUM = hashlib.sha256(PAYLOAD).hexdigest()


def binary_step(path="/download", payload=PAYLOAD):
    """完整文件长度和摘要来自独立样本，不从服务器响应自证。"""
    return {"method": "GET", "path": path, "status": 200, "responseKind": "binary", "expectedBytes": len(payload), "expectedSha256": hashlib.sha256(payload).hexdigest()}


class BinaryContractTests(unittest.TestCase):
    """连接前拒绝缺失完整性、分段和混用JSON的实验声明。"""
    def test_fixed_binary_configuration_validates_without_external_operations(self):
        """固定完整下载配置离线验证，不得隐式联网或采样。"""
        cfg = config(); cfg["scenarios"][0]["steps"] = [binary_step()]
        with mock.patch.object(subject.urllib.request, "build_opener", side_effect=AssertionError("不应联网")), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("不应执行进程")):
            self.assertIs(subject.validate(cfg), cfg)

    def test_binary_example_is_offline_and_clearly_uses_placeholder_endpoints(self):
        """示例只声明原接口占位路径，不冒充已对接的双端样本。"""
        from pathlib import Path
        path = Path(__file__).with_name("paired.binary.example.json")
        cfg = subject.read_json(path.read_bytes())
        with mock.patch.object(subject.urllib.request, "build_opener", side_effect=AssertionError("不能联网")), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("不能SSH")):
            subject.validate(cfg)
        self.assertTrue(all("__replace_with_original_" in step["path"] for step in cfg["scenarios"][0]["steps"]))

    def test_incomplete_or_mixed_contract_is_rejected(self):
        """缺少完整性或混用分段与JSON合同在连接前拒绝。"""
        changes = [{"expectedBytes": None}, {"expectedBytes": True}, {"expectedBytes": -1}, {"expectedBytes": 64 * 1024 * 1024 + 1}, {"expectedSha256": None}, {"expectedSha256": "f" * 63}, {"expectedSha256": "F" * 64}, {"status": 206}, {"checks": [{"pointer": "/code", "equals": 200}]}, {"capture": {"id": "/id"}}, {"ignorePointers": ["/data"]}, {"responseKind": "bniary"}]
        for change in changes:
            cfg = config(); cfg["scenarios"][0]["steps"] = [{**binary_step(), **change}]
            with self.subTest(change=change), self.assertRaises(subject.ContractError): subject.validate(cfg)
        for header in ("Range", "rAnGe", "If-Range"):
            cfg = config(); cfg["scenarios"][0]["steps"] = [binary_step()]; cfg["targets"]["java"]["headers"] = {header: "bytes=0-99"}
            with self.subTest(header=header), self.assertRaises(subject.ContractError): subject.validate(cfg)
        cfg = config(); cfg["scenarios"][0]["steps"][0]["expectedSha256"] = CHECKSUM
        with self.assertRaises(subject.ContractError): subject.validate(cfg)

    def test_stream_reads_are_bounded_and_do_not_return_raw_body(self):
        """实际8MiB输入按块读取，结果不得保存原文件正文。"""
        class Stream(io.BytesIO):
            """8MiB样本不允许整文件read，记录最大单次传输块。"""
            def read(self, size=-1):
                self.assertion.assertGreater(size, 0); self.assertion.assertLessEqual(size, 64 * 1024)
                return super().read(size)
        payload = PAYLOAD * 8; stream = Stream(payload); stream.assertion = self
        from email.message import Message
        stream.headers = Message(); stream.headers["Content-Length"] = str(len(payload))
        result = subject.read_binary(stream, binary_step(payload=payload))
        self.assertEqual(result.byte_length, len(payload)); self.assertEqual(result.sha256, hashlib.sha256(payload).hexdigest())
        self.assertEqual(set(vars(result)), {"byte_length", "sha256", "fingerprint"})

    def test_forged_transport_or_zero_timer_cannot_produce_successful_download(self):
        """纯摘要、错误证据和零时长不能伪造有效下载。"""
        scenario = {"name": "download", "steps": [binary_step()]}
        for transport in (lambda *args: "hash-only", lambda *args: subject.BinaryResponse(len(PAYLOAD) - 1, CHECKSUM), lambda *args: subject.BinaryResponse(len(PAYLOAD), "0" * 64)):
            trial = subject.run_trial(config(), config()["targets"]["java"], scenario, 1, 0, "measured", 1, transport)
            self.assertEqual(trial["failedScenarios"], 1); self.assertEqual(trial["binaryDownload"]["verifiedBytes"], 0)
        with mock.patch.object(subject.time, "perf_counter", return_value=7):
            trial = subject.run_trial(config(), config()["targets"]["java"], scenario, 1, 0, "measured", 1, lambda *args: subject.BinaryResponse(len(PAYLOAD), CHECKSUM))
        self.assertEqual(trial["failedScenarios"], 1); self.assertEqual(trial["binaryDownload"]["goodputBytesPerSecond"], 0)


class BinaryHttpTests(unittest.TestCase):
    """真实HTTP流式下载，认证和票据保留原传输，报告只留长度、摘要和耗时。"""
    def setUp(self):
        """为当前用例创建独占回环端口和内存样本。"""
        self.calls = []; self.lock = threading.Lock(); outer = self
        class Handler(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"
            def log_message(self, *args):
                """关闭夹具访问正文日志，认证头不进入测试输出。"""
                pass
            def do_GET(self):
                """仅提供本轮独占回环传输，明确模拟真实完整/分段/坏响应。"""
                parsed = urllib.parse.urlsplit(self.path); path = parsed.path
                if path.startswith(("/java/", "/csharp/")): path = "/" + path.split("/", 2)[2]
                with outer.lock: outer.calls.append((path, self.headers.get("X-Fixture"), self.headers.get("Accept-Encoding"), parsed.query))
                if path == "/ticket":
                    payload = subject.canonical({"code": 200, "data": {"ticket": "private/ticket?汉字 "}})
                elif path == "/after": payload = subject.canonical({"code": 200})
                elif path == "/business-error": payload = subject.canonical({"code": 500, "msg": "private-server-message"})
                elif path == "/empty": payload = b""
                elif path == "/wrong": payload = PAYLOAD[:-1] + bytes([PAYLOAD[-1] ^ 1])
                elif path == "/cut": payload = PAYLOAD[:len(PAYLOAD) // 2]
                else: payload = PAYLOAD
                if path == "/download" and parsed.query:
                    if urllib.parse.parse_qs(parsed.query).get("ticket") != ["private/ticket?汉字 "]: self.send_error(400); return
                self.send_response(302 if path == "/redirect" else 206 if path == "/partial" else 200)
                self.send_header("Connection", "close"); self.close_connection = True
                if path == "/redirect": self.send_header("Location", "/download")
                if path == "/encoded": self.send_header("Content-Encoding", "gzip")
                if path in ("/partial", "/range-header"): self.send_header("Content-Range", "bytes 0-99/1000")
                if path == "/duplicate-length": self.send_header("Content-Length", str(len(payload)))
                if path == "/chunked": self.send_header("Transfer-Encoding", "chunked")
                elif path not in ("/oversize", "/no-length"):
                    self.send_header("Content-Length", str(len(PAYLOAD) if path == "/cut" else len(payload)))
                self.end_headers()
                try:
                    if path == "/slow": time.sleep(0.2)
                    if path == "/chunked":
                        for offset in range(0, len(payload), 16384):
                            chunk = payload[offset:offset + 16384]; self.wfile.write(("%X\r\n" % len(chunk)).encode() + chunk + b"\r\n")
                        self.wfile.write(b"0\r\n\r\n")
                    else: self.wfile.write(payload + (b"x" if path == "/oversize" else b""))
                except (BrokenPipeError, ConnectionResetError):
                    # 长度头等前置拒绝会关闭本轮连接，不重试写入夹具响应。
                    pass
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True); self.thread.start()
        self.target = {"baseUrl": "http://127.0.0.1:" + str(self.server.server_port), "headers": {"X-Fixture": "private-auth"}}

    def tearDown(self):
        """只停止本测试HTTP服务，不影响用户进程或保存样本文件。"""
        self.server.shutdown(); self.server.server_close(); self.thread.join(2)

    def test_actual_fixed_chunked_and_no_length_responses_have_same_integrity(self):
        """真实三种传输封装还原同一完整实体和认证头。"""
        fingerprints = []
        for path in ("/download", "/chunked", "/no-length"):
            result = subject.request(self.target, binary_step(path), {}, 2)
            self.assertEqual(result.byte_length, len(PAYLOAD)); self.assertEqual(result.sha256, CHECKSUM); fingerprints.append(result.fingerprint)
        self.assertEqual(len(set(fingerprints)), 1)
        self.assertTrue(all(call[1] == "private-auth" and call[2] == "identity" for call in self.calls))

    def test_bad_hash_truncation_extra_bytes_encoding_partial_and_json_error_rejected(self):
        """坏文件、截断、越界和不完整响应均必须失败。"""
        for path in ("/wrong", "/cut", "/oversize", "/encoded", "/partial", "/range-header", "/duplicate-length", "/business-error"):
            with self.subTest(path=path), self.assertRaises((subject.ContractError, urllib.error.HTTPError)):
                subject.request(self.target, binary_step(path), {}, 2)
        self.assertEqual(len(self.calls), 8)

    def test_binary_redirect_is_not_followed_or_retried(self):
        """下载重定向不能暗中换目标或重放请求。"""
        with self.assertRaises(urllib.error.HTTPError): subject.request(self.target, binary_step("/redirect"), {}, 2)
        self.assertEqual([call[0] for call in self.calls], ["/redirect"])

    def test_ticket_capture_concurrent_download_and_report_redaction(self):
        """原JSON票据接真实并发下载，证据不记录认证或票据正文。"""
        scenario = {"name": "attachment", "steps": [{"method": "GET", "path": "/ticket", "status": 200, "checks": [{"pointer": "/code", "equals": 200}], "capture": {"ticket": "/data/ticket"}, "ignorePointers": ["/data/ticket"]}, binary_step("/download?ticket=${ticket}")]}
        trial = subject.run_trial(config(), self.target, scenario, 2, 0, "measured", 4)
        self.assertEqual(trial["failedScenarios"], 0); self.assertEqual(trial["binaryDownload"]["verifiedResponses"], 4); self.assertEqual(trial["binaryDownload"]["verifiedBytes"], len(PAYLOAD) * 4)
        self.assertGreater(trial["binaryDownload"]["goodputBytesPerSecond"], 0); self.assertGreater(trial["binaryDownload"]["requestP95Ms"], 0)
        for row in trial["rows"]:
            self.assertEqual(row["binaryResponses"][0]["stepIndex"], 1); self.assertEqual(row["binaryResponses"][0]["sha256"], CHECKSUM)
        text = json.dumps(trial); self.assertNotIn("private-auth", text); self.assertNotIn("private/ticket", text); self.assertNotIn("private-server-message", text)
        self.assertEqual(len(self.calls), 8)

    def test_binary_failure_stops_later_steps_and_never_counts_failed_bytes(self):
        """完整性失败停止后续步骤，失败数据不算有效业务吞吐。"""
        scenario = {"name": "bad", "steps": [binary_step("/wrong"), {"method": "GET", "path": "/after", "status": 200, "checks": [{"pointer": "/code", "equals": 200}]}]}
        trial = subject.run_trial(config(), self.target, scenario, 1, 0, "measured", 1)
        self.assertEqual(trial["failedScenarios"], 1); self.assertEqual(trial["rows"][0]["failedStep"], 0); self.assertEqual(trial["rows"][0]["attemptedRequests"], 1)
        self.assertEqual(trial["binaryDownload"]["verifiedBytes"], 0); self.assertEqual(trial["binaryDownload"]["goodputBytesPerSecond"], 0); self.assertIsNone(trial["binaryDownload"]["requestP95Ms"])
        self.assertEqual([call[0] for call in self.calls], ["/wrong"])

    def test_two_sides_compare_only_after_actual_file_integrity_and_evidence(self):
        """两端先验证文件与逐项统计，缺证据或非有限值不能比较性能。"""
        cfg = {**config(), "iterations": 2}; scenario = {"name": "pair", "steps": [binary_step()]}
        left = subject.run_trial(cfg, {**self.target, "baseUrl": self.target["baseUrl"] + "/java"}, scenario, 1, 0, "measured", 2)
        right = subject.run_trial(cfg, {**self.target, "baseUrl": self.target["baseUrl"] + "/csharp"}, scenario, 1, 0, "measured", 2)
        compared = subject.compare_pair(left, right, cfg); self.assertTrue(compared["valid"]); self.assertGreater(compared["binaryGoodputRatio"], 0)
        absent = copy.deepcopy(right); absent.pop("binaryDownload"); self.assertFalse(subject.compare_pair(left, absent, cfg)["valid"])
        bad = subject.run_trial(cfg, self.target, {"name": "pair", "steps": [binary_step("/wrong")]}, 1, 0, "measured", 2)
        self.assertFalse(subject.compare_pair(left, bad, cfg)["valid"])
        both_absent = copy.deepcopy(left); both_absent.pop("binaryDownload")
        self.assertFalse(subject.compare_pair(both_absent, both_absent, cfg)["valid"])
        for field, value in (("verifiedBytes", 0), ("verifiedResponses", 0), ("goodputBytesPerSecond", float("nan")), ("goodputBytesPerSecond", float("inf"))):
            changed = copy.deepcopy(right); changed["binaryDownload"][field] = value
            self.assertFalse(subject.compare_pair(left, changed, cfg)["valid"])

    def test_socket_timeout_stops_download_and_does_not_retry(self):
        """实际读取超时不重试原下载。"""
        with self.assertRaises(TimeoutError): subject.request(self.target, binary_step("/slow"), {}, 0.05)
        self.assertEqual([call[0] for call in self.calls], ["/slow"])

    def test_later_business_failure_retains_download_evidence_without_success_goodput(self):
        """后续业务失败仍保留先前下载证据，但不计成功吞吐。"""
        scenario = {"name": "late", "steps": [binary_step(), {"method": "GET", "path": "/business-error", "status": 200, "checks": [{"pointer": "/code", "equals": 200}]}, {"method": "GET", "path": "/after", "status": 200, "checks": [{"pointer": "/code", "equals": 200}]}]}
        trial = subject.run_trial(config(), self.target, scenario, 1, 0, "measured", 1)
        self.assertEqual(trial["failedScenarios"], 1); self.assertEqual(trial["rows"][0]["failedStep"], 1); self.assertEqual(trial["rows"][0]["binaryResponses"][0]["byteLength"], len(PAYLOAD))
        self.assertEqual(trial["binaryDownload"]["verifiedBytes"], 0); self.assertEqual(trial["binaryDownload"]["verifiedResponses"], 0)
        self.assertEqual([call[0] for call in self.calls], ["/download", "/business-error"])

    def test_zero_byte_files_are_valid_but_do_not_invent_payload_throughput(self):
        """空文件合法但不产生虚构的正字节吞吐。"""
        scenario = {"name": "empty", "steps": [binary_step("/empty", b"")]}; cfg = {**config(), "iterations": 1}
        trial = subject.run_trial(cfg, self.target, scenario, 1, 0, "measured", 1)
        self.assertEqual(trial["failedScenarios"], 0); self.assertEqual(trial["binaryDownload"]["verifiedResponses"], 1); self.assertEqual(trial["binaryDownload"]["goodputBytesPerSecond"], 0)
        self.assertIsNone(subject.compare_pair(trial, trial, cfg)["binaryGoodputRatio"])


if __name__ == "__main__": unittest.main()
