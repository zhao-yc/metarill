"""测量合同、原HTTP回放及资源边界回归；只启动回环HTTP，不调用SSH/Docker/MySQL。"""
import contextlib
import copy
import io
import http.server
import importlib.util
import json
import pathlib
import subprocess
import threading
import tempfile
import unittest
import urllib.error
from unittest import mock

spec = importlib.util.spec_from_file_location("paired_http", pathlib.Path(__file__).with_name("paired_http.py"))
subject = importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


def config():
    """只提供不可达演示地址，离线验证不得建立连接。"""
    return {"version": 1, "runId": "a" * 32, "rounds": 3, "iterations": 100, "warmupIterations": 2, "concurrency": [1, 4],
            "requestTimeoutSeconds": 2, "maxP95Ratio": 1.1, "minThroughputRatio": 0.95,
            "targets": {"java": {"baseUrl": "http://127.0.0.1:1/api", "container": "java-owned"}, "csharp": {"baseUrl": "http://127.0.0.1:2/api", "container": "csharp-owned"}},
            "scenarios": [{"name": "list", "steps": [{"method": "GET", "path": "/list", "status": 200, "checks": [{"pointer": "/code", "equals": 200}]}]}]}


class Probe:
    """内存资源回执，不执行任何系统命令。"""
    def limits(self, container):
        return {"running": True, "id": container, "image": "sha256:" + container, "nanoCpus": 1_000_000_000, "memoryBytes": 512 * 1024 * 1024, "memorySwap": 0, "cpuQuota": 0, "cpuPeriod": 0, "cpusetCpus": ""}

    def sample(self, container):
        return {"Name": container, "CPUPerc": "5.0%", "MemUsage": "20MiB / 512MiB"}


class ContractTests(unittest.TestCase):
    """配置和比较结论必须拒绝不公平或不完整输入。"""
    def test_validate_is_offline_and_does_not_fill_missing_measurements(self):
        with mock.patch.object(subject.urllib.request, "build_opener", side_effect=AssertionError("不应联网")), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("不应运行进程")):
            self.assertEqual(subject.validate(config())["rounds"], 3)

    def test_invalid_thresholds_targets_and_small_sample_rejected(self):
        for key, value in (("rounds", 2), ("iterations", 10), ("concurrency", [1, 1]), ("maxP95Ratio", float("nan")), ("minThroughputRatio", 1.2)):
            value_config = config(); value_config[key] = value
            with self.subTest(key=key), self.assertRaises(subject.ContractError):
                subject.validate(value_config)
        for field, value in (("baseUrl", "http://user:password@localhost/api"), ("container", "--all")):
            value_config = config(); value_config["targets"]["java"][field] = value
            with self.subTest(field=field), self.assertRaises(subject.ContractError):
                subject.validate(value_config)
        value_config = config(); value_config["targets"]["csharp"] = value_config["targets"]["java"]
        with self.assertRaises(subject.ContractError): subject.validate(value_config)

    def test_fixed_business_assertions_and_capture_identity_required(self):
        for change in ({"path": "//another-host/path"}, {"checks": []}, {"checks": [{"pointer": "/data/name", "equals": "x"}]}, {"status": 500}, {"capture": {"iteration": "/map/id"}}):
            value_config = config(); value_config["scenarios"][0]["steps"][0].update(change)
            with self.subTest(change=change), self.assertRaises(subject.ContractError): subject.validate(value_config)

    def test_json_and_pointer_keep_type_missing_and_escape_semantics(self):
        self.assertNotEqual(subject.digest(True), subject.digest(1))
        self.assertNotEqual(subject.digest(9007199254740993), subject.digest("9007199254740993"))
        self.assertEqual(subject.locate({"a/b": {"x~y": [None]}}, "/a~1b/x~0y/0")[2], None)
        for text in ('{"code":200,"code":500}', '{"n":NaN}'):
            with self.assertRaises(subject.ContractError): subject.read_json(text)
        for pointer in ("", "/absent", "/bad~2"):
            with self.assertRaises(subject.ContractError): subject.locate({}, pointer)

    def test_percentiles_and_response_mismatch_precede_latency(self):
        self.assertEqual(subject.percentile(list(range(1, 101)), 95), 95)
        self.assertEqual(subject.percentile([7], 99), 7)
        self.assertIsNone(subject.percentile([], 95))
        unit_config = {**config(), "iterations": 1}
        left = {"failedScenarios": 0, "rows": [{"iteration": 0, "fingerprints": ["expected"]}], "p95Ms": 10, "scenarioThroughput": 100}
        right = copy.deepcopy(left); right["rows"][0]["fingerprints"] = ["different"]; right["p95Ms"] = 1
        self.assertFalse(subject.compare_pair(left, right, unit_config)["valid"])
        self.assertFalse(subject.compare_pair(left, left, {**unit_config, "iterations": 2})["valid"])
        right = copy.deepcopy(left); right["failedScenarios"] = 1
        self.assertFalse(subject.compare_pair(left, right, unit_config)["valid"])
        right = copy.deepcopy(left); right["p95Ms"] = 12
        self.assertTrue(subject.compare_pair(left, right, unit_config)["regression"])

    def test_resources_require_equal_explicit_limits_and_distinct_instances(self):
        for changed in ({"nanoCpus": 0}, {"memoryBytes": 0}, {"running": False}, {"memorySwap": 42}, {"cpusetCpus": "2"}, {"id": "java-owned"}):
            probe = Probe(); original = probe.limits
            probe.limits = lambda name: {**original(name), **(changed if name == "csharp-owned" else {})}
            with self.subTest(changed=changed), self.assertRaises(subject.ContractError): subject.equal_limits(probe, config()["targets"])
        self.assertEqual(len(subject.equal_limits(Probe(), config()["targets"])), 2)

    def test_docker_command_checks_200_before_unix_socket_and_never_trusts_new_key(self):
        commands = []
        def invoke(args, **kwargs):
            commands.append(args)
            return subprocess.CompletedProcess(args, 0, '{"running":true}', "")
        probe = subject.DockerProbe("server-200", invoke)
        probe.limits("owned-java"); probe.sample("owned-java")
        for args in commands:
            self.assertIn("StrictHostKeyChecking=yes", args)
            self.assertEqual(args[0], "ssh")
            self.assertLess(args[-1].index("hostname -I"), args[-1].index("docker --host"))
            self.assertIn("192.168.31.200", args[-1])
            self.assertIn("unix:///var/run/docker.sock", args[-1])
            self.assertIn("env -u DOCKER_HOST -u DOCKER_CONTEXT", args[-1])
            self.assertNotIn(".Config.Env", args[-1])
        for name in ("--all", "*", "owned; other"):
            with self.assertRaises(subject.ContractError): probe.sample(name)
        with self.assertRaises(subject.ContractError): subject.DockerProbe("host;evil", invoke)
        self.assertEqual(len(commands), 2)

    def test_resource_units_and_wrong_container_or_invalid_values(self):
        value = subject.resource_values({"Name":"owned","CPUPerc":"125.2%","MemUsage":"1.5GiB / 2GiB"}, "owned")
        self.assertEqual(value["memoryBytes"], 1610612736)
        self.assertEqual(value["cpuPercent"], 125.2)
        for raw in ({"Name":"other","CPUPerc":"1%","MemUsage":"1B / 2B"}, {"Name":"owned","CPUPerc":"NaN%","MemUsage":"1B / 2B"}, {"Name":"owned","CPUPerc":"1%","MemUsage":"bad"}):
            with self.assertRaises(subject.ContractError): subject.resource_values(raw, "owned")

    def test_rounds_alternate_keep_warmup_separate_and_persist_events(self):
        calls, events = [], []
        def trial(cfg, target, scenario, concurrency, index, phase, iterations, *args):
            calls.append((target["container"], index, phase, iterations))
            latency = 12 if target["container"] == "csharp-owned" else 10
            return {"failedScenarios": 0, "rows": [{"iteration": index, "fingerprints": ["same"]} for index in range(cfg["iterations"])], "p95Ms": latency, "scenarioThroughput": 100, "resourceSampleCount": 2, "resourceErrors": []}
        value_config = config(); value_config["concurrency"] = [1]
        with mock.patch.object(subject, "run_trial", side_effect=trial):
            report = subject.run(value_config, Probe(), on_event=events.append)
        self.assertEqual([x[0] for x in calls[2:]], ["java-owned", "csharp-owned", "csharp-owned", "java-owned", "java-owned", "csharp-owned"])
        self.assertEqual([x[3] for x in calls[:2]], [2, 2])
        self.assertTrue(report["results"][0]["stableRegression"])
        self.assertFalse(report["releaseAccepted"])
        self.assertEqual(len([x for x in events if x["kind"] == "trial"]), 6)

    def test_zero_clock_span_is_invalid_not_infinite_throughput(self):
        with mock.patch.object(subject.time, "perf_counter", return_value=7):
            trial = subject.run_trial(config(), config()["targets"]["java"], config()["scenarios"][0], 1, 0, "measured", 1, lambda *args: "ok")
        self.assertEqual(trial["scenarioThroughput"], 0)
        self.assertFalse(subject.compare_pair(trial, trial, {**config(), "iterations": 1})["valid"])

    def test_resource_failures_or_limits_drift_prevent_valid_comparison(self):
        class Broken(Probe):
            def sample(self, name): raise OSError("原始连接错误可能含秘密")
        trial = subject.run_trial(config(), config()["targets"]["java"], config()["scenarios"][0], 1, 0, "measured", 1, lambda *args: "ok", Broken())
        self.assertEqual(trial["resourceErrors"], ["OSError"])
        self.assertNotIn("秘密", json.dumps(trial, ensure_ascii=False))
        with mock.patch.object(subject, "equal_limits", side_effect=[{"cpu": 1}, {"cpu": 2}]), mock.patch.object(subject, "run_trial", return_value={"failedScenarios":0,"rows":[],"p95Ms":1,"scenarioThroughput":1,"resourceSampleCount":2,"resourceErrors":[]}):
            with self.assertRaises(subject.ContractError): subject.run(config(), Probe())


class HttpReplayTests(unittest.TestCase):
    """实际回环HTTP证明认证、JSON、捕获/回放、无重试及无重定向。"""
    def setUp(self):
        self.calls = []; self.lock = threading.Lock(); outer = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                if self.path == "/redirect":
                    with outer.lock: outer.calls.append(self.path)
                    self.send_response(302); self.send_header("Location", "/ok"); self.end_headers(); return
                code = 500 if self.path == "/business-failure" else 200
                self.reply({"code": code, "map": {"id": "9007199254740993", "value": None}})
            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
                self.reply({"code": 200, "map": {"id": "9007199254740993", "name": body["name"]}})
            def do_DELETE(self): self.reply({"code": 200, "map": {"deleted": "9007199254740993"}})
            def reply(self, value):
                with outer.lock: outer.calls.append((self.command, self.path, self.headers.get("X-Test-Token")))
                encoded = subject.canonical(value); self.send_response(200); self.send_header("Content-Length", str(len(encoded))); self.end_headers(); self.wfile.write(encoded)
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True); self.thread.start()
        self.target = {"baseUrl": f"http://127.0.0.1:{self.server.server_port}", "headers": {"X-Test-Token": "private-token"}}
        self.step = {"method":"GET","path":"/ok","status":200,"checks":[{"pointer":"/code","equals":200}]}

    def tearDown(self):
        self.server.shutdown(); self.server.server_close(); self.thread.join(2)

    def test_actual_http_preserves_large_id_and_null_and_captures(self):
        step = {**self.step,"capture":{"record":"/map/id"},"checks":self.step["checks"]+[{"pointer":"/map/value","equals":None},{"pointer":"/map/id","equals":"9007199254740993"}]}
        variables = {}; fingerprint = subject.request(self.target, step, variables, 2)
        self.assertEqual(variables["record"], "9007199254740993")
        self.assertEqual(len(fingerprint), 64)
        self.assertEqual(self.calls[0][2], "private-token")

    def test_business_failure_and_redirect_are_not_retried(self):
        for path, error in (("/business-failure",subject.ContractError),("/redirect",urllib.error.HTTPError)):
            with self.assertRaises(error): subject.request(self.target, {**self.step,"path":path}, {}, 2)
        self.assertEqual(len(self.calls), 2)
        self.assertNotIn(("GET", "/ok", "private-token"), self.calls)

    def test_actual_multistep_concurrent_replay_and_failure_stops_remaining_steps(self):
        scenario = {"name":"crud","steps":[{"method":"POST","path":"/create","json":{"name":"${run}-${iteration}"},"status":200,"checks":self.step["checks"],"capture":{"record":"/map/id"}}, {**self.step,"path":"/detail/${record}"},{"method":"DELETE","path":"/delete/${record}","status":200,"checks":self.step["checks"]}]}
        trial = subject.run_trial(config(), self.target, scenario, 2, 0, "measured", 4)
        self.assertEqual(trial["failedScenarios"], 0)
        self.assertEqual(trial["completedScenarios"], 4)
        self.assertEqual(len(self.calls), 12)
        self.assertTrue(all(len(row["requestMs"]) == 3 for row in trial["rows"]))
        failed = {"name":"fail","steps":[{**self.step,"path":"/business-failure"},self.step]}
        trial = subject.run_trial(config(), self.target, failed, 1, 0, "measured", 1)
        self.assertEqual(trial["failedScenarios"], 1)
        self.assertEqual(len(self.calls), 13)
        self.assertNotIn("private-token", json.dumps(trial))

    def test_only_explicit_existing_dynamic_fields_may_be_ignored(self):
        normalized = {"code":200,"map":{"id":"<已声明的动态字段>","value":None}}
        step = {**self.step,"ignorePointers":["/map/id"]}
        self.assertEqual(subject.request(self.target, step, {}, 2), subject.digest(normalized))
        with self.assertRaises(subject.ContractError): subject.request(self.target, {**step,"ignorePointers":["/absent"]}, {}, 2)
        self.assertEqual(subject.expand("/name/${id}", {"id":"x/y?z"}, path=True), "/name/x%2Fy%3Fz")


class CliEvidenceTests(unittest.TestCase):
    """命令行失败留证与配置验证，不触发远端进程或业务请求。"""
    def test_example_validates_without_any_external_operation(self):
        """仓库示例必须可离线读取，不能在validate分支隐式执行采样。"""
        path = pathlib.Path(__file__).with_name("paired.example.json")
        with mock.patch("sys.argv", ["paired_http", "validate", "--config", str(path)]), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("不能SSH")), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(subject.main(), 0)

    def test_failed_run_preserves_completed_event_and_redacts_error_and_credentials(self):
        """失败仍落盘已完成阶段，报告不保存原始认证头及异常正文。"""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory); cfg = config(); cfg["targets"]["java"]["headers"] = {"loginStateId":"private-session"}
            source = root / "settings.json"; source.write_text(json.dumps(cfg), encoding="utf-8"); output = root / "new-run"
            def fail(config, probe, on_event):
                on_event({"kind":"trial","target":"java","fingerprint":"safe"})
                raise RuntimeError("private-session original server message")
            stdout = io.StringIO()
            with mock.patch("sys.argv", ["paired_http", "run", "--config", str(source), "--ssh-host", "known-200", "--output", str(output)]), mock.patch.object(subject, "run", side_effect=fail), contextlib.redirect_stdout(stdout):
                self.assertEqual(subject.main(), 1)
            text = "".join(path.read_text(encoding="utf-8") for path in output.iterdir()) + stdout.getvalue()
            self.assertNotIn("private-session", text); self.assertNotIn("original server", text)
            self.assertTrue((output / "intent.json").exists()); self.assertTrue((output / "events.jsonl").exists())
            self.assertEqual(json.loads((output / "failure.json").read_text())["retryAttempted"], False)
            self.assertFalse((output / "measurement.json").exists())

    def test_old_output_is_not_reused_or_overwritten(self):
        """旧目录拒绝后不调用执行器，也不覆盖原证据。"""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory); source = root / "settings.json"; source.write_text(json.dumps(config()), encoding="utf-8")
            output = root / "old-run"; output.mkdir(); (output / "intent.json").write_text("preserve")
            with mock.patch("sys.argv", ["paired_http", "run", "--config", str(source), "--ssh-host", "known-200", "--output", str(output)]), mock.patch.object(subject, "run") as execute, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(subject.main(), 1); execute.assert_not_called()
            self.assertEqual((output / "intent.json").read_text(), "preserve")


if __name__ == "__main__":
    unittest.main()
