"""200本机接线合同回归；所有Docker/SSH均为内存桩，不建立服务器连接。"""
import contextlib
import copy
import io
import json
import pathlib
import subprocess
import tempfile
import unittest
from unittest import mock

from test_paired_http import subject, config, Probe


def strict_config():
    """固定本轮、两镜像及不同发布端口，虚拟资源不作为正式成绩。"""
    value = config()
    value["transportProof"] = "server200-loopback-v1"
    value["concurrency"] = [1]
    for index, (name, target) in enumerate(value["targets"].items(), 1):
        target.update(container=f"metarill-perf-{value['runId']}-{name}",
                      expectedImageId="sha256:" + str(index) * 64, containerPort=8080)
    return value


class LocalProbe(subject.Server200LocalProbe):
    """仅模拟实际inspect形状；不运行构造器或系统命令。"""
    def __init__(self, value):
        """根据测试配置制造可核对的独立容器、镜像、标签和发布端口。"""
        self.value = value
        self.observations = {}
        for index, (name, target) in enumerate(value["targets"].items(), 1):
            self.observations[target["container"]] = {
                **Probe().limits(target["container"]), "id": str(index) * 64,
                "name": "/" + target["container"], "image": target["expectedImageId"],
                "leaseRun": value["runId"], "leaseTarget": name, "networkMode": "bridge",
                "ports": {"8080/tcp": [{"HostIp": "127.0.0.1", "HostPort": str(index)}], "9090/tcp": None}}

    def limits(self, container):
        """返回副本，防止前后比较被同一可变对象掩盖。"""
        return copy.deepcopy(self.observations[container])


def trial(*args):
    """只隔离测量回放；由run真实执行前后探针、比较和事件记录。"""
    return {"failedScenarios": 0, "rows": [{"iteration": 0, "fingerprints": ["same"]}],
            "p95Ms": 1, "scenarioThroughput": 10, "resourceSampleCount": 2, "resourceErrors": []}


class TransportConfigTests(unittest.TestCase):
    """配置离线拒绝无法对应实际容器的地址和未启用的证明声明。"""
    def test_strict_validate_performs_no_external_call(self):
        """新模式只验证配置，不因出现镜像和端口声明而连接外部。"""
        with mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("禁止命令")), mock.patch.object(subject.urllib.request, "build_opener", side_effect=AssertionError("禁止HTTP")):
            self.assertEqual(subject.validate(strict_config())["transportProof"], "server200-loopback-v1")

    def test_strict_addresses_cannot_use_remote_dns_proxy_path_or_default_port(self):
        """只有明确宿主回环端口可与Docker绑定逐项核对。"""
        for url in ("http://localhost:1/api", "http://192.168.31.200:1/api", "https://127.0.0.1:1/api", "http://127.0.0.1/api", "http://127.0.0.1:1/other", "http://127.0.0.1:01/api", "http://127.0.0.1:1/api?x=1"):
            value = strict_config(); value["targets"]["java"]["baseUrl"] = url
            with self.subTest(url=url), self.assertRaises(subject.ContractError): subject.validate(value)

    def test_run_label_image_and_container_port_must_be_explicit(self):
        """实验身份和镜像不能依赖模糊名称、隐式端口或被忽略的证明字段。"""
        for change in ({"container": "foreign-java"}, {"expectedImageId": "java:latest"}, {"expectedImageId": "sha256:" + "a" * 63}, {"containerPort": True}, {"containerPort": 0}, {"containerPort": 65536}):
            value = strict_config(); value["targets"]["java"].update(change)
            with self.subTest(change=change), self.assertRaises(subject.ContractError): subject.validate(value)
        for change in ({"transportProof": "unknown"}, {"transportProof": None}):
            value = strict_config(); value.update(change)
            with self.subTest(change=change), self.assertRaises(subject.ContractError): subject.validate(value)

    def test_legacy_measurement_explicitly_does_not_prove_transport(self):
        """旧配置仍能测量，但报告必须保留URL接线缺口。"""
        with mock.patch.object(subject, "run_trial", side_effect=trial):
            report = subject.run(config(), Probe())
        self.assertFalse(report["transportEvidence"]["verified"])
        self.assertIn("URL与容器实例的真实接线核验", report["remainingEvidence"])
        self.assertFalse(report["releaseAccepted"])


class ActualBindingTests(unittest.TestCase):
    """验证run在真实回放入口前检查inspect，并在末尾重新核对，不伪造HTTP成功。"""
    def test_correct_binding_journal_precedes_http_and_cannot_accept_release(self):
        """预检事件先于回放，接线通过也不得签发完整性能验收。"""
        value = strict_config(); probe = LocalProbe(value); events = []
        def measured(*args):
            self.assertEqual(events[0]["kind"], "container-preflight")
            self.assertTrue(events[0]["transportEvidence"]["verified"])
            return trial()
        with mock.patch.object(subject, "run_trial", side_effect=measured):
            report = subject.run(value, probe, on_event=events.append)
        self.assertTrue(report["transportEvidence"]["proxyBypassed"])
        self.assertFalse(report["releaseAccepted"])
        self.assertNotIn("URL与容器实例的真实接线核验", report["remainingEvidence"])
        self.assertIn("共享MySQL/主机负载核验", report["remainingEvidence"])

    def test_ssh_probe_rejected_before_any_http(self):
        """远端采样不能证明客户端的127.0.0.1属于该宿主。"""
        with mock.patch.object(subject, "run_trial") as http:
            with self.assertRaises(subject.ContractError): subject.run(strict_config(), Probe())
            http.assert_not_called()

    def test_wrong_container_image_or_lease_rejected_before_http(self):
        """错误归属、镜像或共享网络都不能进入业务回放。"""
        for change in ({"name": "/foreign"}, {"id": "short"}, {"image": "sha256:" + "f" * 64}, {"leaseRun": "b" * 32}, {"leaseTarget": "csharp"}, {"networkMode": "host"}, {"networkMode": "none"}, {"networkMode": "container:other"}):
            value = strict_config(); probe = LocalProbe(value); probe.observations[value["targets"]["java"]["container"]].update(change)
            with self.subTest(change=change), mock.patch.object(subject, "run_trial") as http:
                with self.assertRaises(subject.ContractError): subject.run(value, probe)
                http.assert_not_called()

    def test_wildcard_ipv6_wrong_port_extra_mapping_or_udp_rejected_before_http(self):
        """仅唯一IPv4回环TCP发布可对应固定HTTP地址。"""
        for ports in (None, {}, {"8080/tcp": [{"HostIp": "0.0.0.0", "HostPort": "1"}]}, {"8080/tcp": [{"HostIp": "::1", "HostPort": "1"}]}, {"8080/tcp": [{"HostIp": "127.0.0.1", "HostPort": "9"}]}, {"8080/udp": [{"HostIp": "127.0.0.1", "HostPort": "1"}]}, {"8080/tcp": [{"HostIp": "127.0.0.1", "HostPort": "1"}, {"HostIp": "127.0.0.1", "HostPort": "2"}]}, {"8080/tcp": [{"HostIp": "127.0.0.1", "HostPort": "1"}], "9090/tcp": [{"HostIp": "127.0.0.1", "HostPort": "3"}]}):
            value = strict_config(); probe = LocalProbe(value); probe.observations[value["targets"]["java"]["container"]]["ports"] = ports
            with self.subTest(ports=ports), mock.patch.object(subject, "run_trial") as http:
                with self.assertRaises(subject.ContractError): subject.run(value, probe)
                http.assert_not_called()

    def test_post_measurement_replacement_or_binding_change_rejects_result(self):
        """结束时再次核对；测量期间换容器或改绑定不得留下有效成绩。"""
        for change in ({"id": "e" * 64}, {"leaseRun": "b" * 32}, {"ports": {"8080/tcp": [{"HostIp": "127.0.0.1", "HostPort": "5"}]}}):
            value = strict_config(); probe = LocalProbe(value)
            def measured(*args):
                probe.observations[value["targets"]["java"]["container"]].update(change)
                return trial()
            with self.subTest(change=change), mock.patch.object(subject, "run_trial", side_effect=measured):
                with self.assertRaises(subject.ContractError): subject.run(value, probe)


class LocalInvocationTests(unittest.TestCase):
    """仅检查模拟命令，不能在开发机启动Docker；主机核验必须每次执行。"""
    def test_windows_constructor_rejects_before_process(self):
        """Windows本机模式先拒绝，不能在开发电脑执行Docker。"""
        with mock.patch.object(subject.sys, "platform", "win32"), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("禁止进程")):
            with self.assertRaises(subject.ContractError): subject.Server200LocalProbe()

    def test_local_commands_check_name_ip_socket_and_only_whitelisted_labels(self):
        """每次模拟命令均检查主机名、IP、socket及最小字段范围。"""
        commands = []
        def invoke(args, **kwargs):
            commands.append(args)
            return subprocess.CompletedProcess(args, 0, "{}", "")
        with mock.patch.object(subject.sys, "platform", "linux"):
            probe = subject.Server200LocalProbe(invoke)
        probe.limits("owned-java"); probe.sample("owned-java")
        self.assertEqual(len(commands), 2)
        for args in commands:
            self.assertEqual(args[:2], ["sh", "-c"])
            self.assertLess(args[-1].index('test "$(hostname)" = "Server-200"'), args[-1].index("docker --host"))
            self.assertLess(args[-1].index("hostname -I"), args[-1].index("docker --host"))
            self.assertIn("192.168.31.200", args[-1]); self.assertIn("unix:///var/run/docker.sock", args[-1])
            self.assertNotIn(".Config.Env", args[-1]); self.assertNotIn("json .Config.Labels", args[-1])
        self.assertIn("metarill.performance.run", commands[0][-1])
        self.assertIn("metarill.performance.target", commands[0][-1])
        for name in ("--all", "*", "owned; other"):
            with self.assertRaises(subject.ContractError): probe.sample(name)
        self.assertEqual(len(commands), 2)

    def test_host_guard_failure_cannot_reach_http(self):
        """真实命令返回身份检查失败时，不得继续HTTP或重试。"""
        commands = []
        def invoke(args, **kwargs):
            commands.append(args); raise subprocess.CalledProcessError(71, args)
        with mock.patch.object(subject.sys, "platform", "linux"):
            probe = subject.Server200LocalProbe(invoke)
        with mock.patch.object(subject, "run_trial") as http:
            with self.assertRaises(subprocess.CalledProcessError): subject.run(strict_config(), probe)
            http.assert_not_called()
        self.assertEqual(len(commands), 1)

    def test_cli_mode_mismatch_rejects_before_process_or_output_creation(self):
        """缺少模式对应关系时不启动探针也不创建测量目录。"""
        for strict, mode in ((True, ["--ssh-host", "server-200"]), (False, ["--server-local"])):
            with tempfile.TemporaryDirectory() as directory:
                root = pathlib.Path(directory); source = root / "settings.json"; output = root / "result"
                source.write_text(json.dumps(strict_config() if strict else config()), encoding="utf-8")
                with mock.patch("sys.argv", ["paired_http", "run", "--config", str(source), "--output", str(output), *mode]), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("禁止进程")), mock.patch.object(subject, "run") as execute, contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(subject.main(), 1); execute.assert_not_called()
                self.assertFalse(output.exists())

    def test_strict_cli_validate_does_not_require_linux_or_make_calls(self):
        """包括仓库实际示例在内，离线校验不要求Linux或可用服务器。"""
        with tempfile.TemporaryDirectory() as directory:
            source = pathlib.Path(directory) / "settings.json"; source.write_text(json.dumps(strict_config()), encoding="utf-8")
            for path in (source, pathlib.Path(__file__).with_name("paired.loopback.example.json")):
                with self.subTest(path=path.name), mock.patch("sys.argv", ["paired_http", "validate", "--config", str(path)]), mock.patch.object(subject.sys, "platform", "win32"), mock.patch.object(subject.subprocess, "run", side_effect=AssertionError("禁止进程")), contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(subject.main(), 0)


if __name__ == "__main__":
    unittest.main()
