"""短轮采样从固定容器绑定的 cgroup 读取，测试无需 Docker 或宿主变更。"""
from pathlib import Path
import copy
import tempfile
import unittest
from unittest import mock

import paired_cgroup_probe as subject
import paired_http as http


class CgroupProbeTests(unittest.TestCase):
    def setUp(self):
        """构造容器检查回执、进程 cgroup 和可控的内核计数。"""
        folder = self.enterContext(tempfile.TemporaryDirectory())
        self.proc = Path(folder) / "proc"
        self.root = Path(folder) / "cgroup"
        self.name = "metarill-test-java"
        self.identifier = "a" * 64
        self.leaf = f"docker-{self.identifier}.scope"
        self.path = self.root / "system.slice" / self.leaf
        self.path.mkdir(parents=True)
        (self.proc / "42").mkdir(parents=True)
        self.cgroup = self.proc / "42/cgroup"
        self.cgroup.write_text(f"0::/system.slice/{self.leaf}\n", encoding="ascii")
        (self.path / "memory.max").write_text("1073741824\n", encoding="ascii")
        (self.path / "memory.current").write_text("1024\n", encoding="ascii")
        (self.path / "cpu.stat").write_text("usage_usec 1000\nuser_usec 900\n", encoding="ascii")
        self.docker = mock.Mock()
        self.docker.docker.return_value = {"id": self.identifier, "pid": 42}
        self.targets = {"java": {"container": self.name}}
        self.limits = {"java": {"name": "/" + self.name, "id": self.identifier, "memoryBytes": 1073741824}}
        self.enterContext(mock.patch.object(subject, "CGROUP_ROOT", self.root))
        self.enterContext(mock.patch.object(subject, "PROC_ROOT", self.proc))

    def test_sample_reads_same_kernel_binding_and_can_be_recomputed(self):
        """约百毫秒的 CPU 增量和实际内存可由原始计数独立复算。"""
        probe = subject.CgroupProbe(self.docker, self.targets, self.limits)

        def tick(delay):
            """模拟内核计数在采样间隔内递增。"""
            self.assertEqual(0.10, delay)
            (self.path / "cpu.stat").write_text("usage_usec 31000\n", encoding="ascii")

        with mock.patch.object(subject.time, "sleep", side_effect=tick), \
             mock.patch.object(subject.time, "perf_counter_ns", side_effect=[1, 100000001]):
            raw = probe.sample(self.name)
        self.assertEqual([1000, 31000], raw["cpuUsageUsec"])
        self.assertEqual(subject.METHOD, raw["source"])
        self.assertEqual({"cpuPercent": 30.0, "memoryBytes": 1024, "memoryLimitBytes": 1073741824},
                         http.resource_values(raw, self.name))
        self.docker.docker.assert_called_once()
        probe.verify()
        self.assertEqual(2, self.docker.docker.call_count)

    def test_foreign_group_limits_and_process_restart_are_rejected(self):
        """仅容器名称相同不足以证明内核组、资源上限和 PID 未漂移。"""
        probe = subject.CgroupProbe(self.docker, self.targets, self.limits)
        self.cgroup.write_text("0::/system.slice/docker-other.scope\n", encoding="ascii")
        with self.assertRaisesRegex(ValueError, "cgroup-path"):
            probe.verify()
        self.cgroup.write_text(f"0::/system.slice/{self.leaf}\n", encoding="ascii")
        (self.path / "memory.max").write_text("max\n", encoding="ascii")
        with self.assertRaisesRegex(ValueError, "cgroup-number"):
            probe.verify()
        self.docker.docker.return_value["pid"] = 0
        with self.assertRaisesRegex(ValueError, "cgroup-process"):
            probe.verify()

    def test_counter_rollback_is_invalid(self):
        """累积计数回退不能当作零 CPU 样本进入报告。"""
        probe = subject.CgroupProbe(self.docker, self.targets, self.limits)
        with mock.patch.object(subject.time, "sleep"), mock.patch.object(probe, "usage", side_effect=[(10, 1), (9, 2)]):
            with self.assertRaisesRegex(ValueError, "counter-drift"):
                probe.sample(self.name)


    def test_trial_windows_compare_raw_counts_for_equal_successful_business(self):
        """计时两端实际读取不同容器的组计数，独立比对业务量和累计 CPU。"""
        config = {"runId": "a" * 32, "requestTimeoutSeconds": 1, "iterations": 2,
                  "maxP95Ratio": 1e9, "minThroughputRatio": 1e-9}
        scenario = {"name": "ordinary-business", "steps": [{}]}
        probe = subject.CgroupProbe(self.docker, self.targets, self.limits)

        def trial(identifier, delta):
            # 采样线程与测试中的固定累计计数独立，避免采样改变被测数据。
            path = self.path / "cpu.stat"
            path.write_text("usage_usec 1000\n", encoding="ascii")
            probe.bindings[self.name] = (identifier, *probe.bindings[self.name][1:])

            def send(target, step, variables, timeout):
                path.write_text(f"usage_usec {1000 + delta}\n", encoding="ascii")
                return "same-business-response"

            raw = {"Name": self.name, "CPUPerc": "1%", "MemUsage": "1024B / 1073741824B"}
            with mock.patch.object(probe, "sample", return_value=raw):
                return http.run_trial(config, self.targets["java"], scenario, 1, 0, "measured", 2, send, probe)

        java, csharp = trial("a" * 64, 400), trial("b" * 64, 200)
        self.assertEqual(200, java["resourceWindow"]["cpuUsecPerCompletedScenario"])
        comparison = http.compare_pair(java, csharp, config)
        self.assertTrue(comparison["valid"])
        self.assertEqual({"cpuUsecPerCompletedScenario": 0.5, "observedBoundaryMaxMemory": 1.0},
                         comparison["resourceRatios"])
        altered = copy.deepcopy(csharp)
        altered["resourceWindow"]["cpuUsageUsec"] -= 1
        self.assertFalse(http.compare_pair(java, altered, config)["valid"])
        del altered["resourceWindow"]
        self.assertFalse(http.compare_pair(java, altered, config)["valid"])

    def test_invalid_window_never_becomes_valid_resource_evidence(self):
        """后快照漂移保留业务回执及错误，前快照无效时不发业务请求。"""
        probe = subject.CgroupProbe(self.docker, self.targets, self.limits)
        cfg = {"runId": "a" * 32, "requestTimeoutSeconds": 1}
        target, scenario = self.targets["java"], {"name": "write", "steps": [{}]}
        before = probe.snapshot(self.name)
        raw = {"Name": self.name, "CPUPerc": "1%", "MemUsage": "1024B / 1073741824B"}
        send = mock.Mock(return_value="same")
        with mock.patch.object(probe, "snapshot", side_effect=[before, None]), mock.patch.object(probe, "sample", return_value=raw):
            result = http.run_trial(cfg, target, scenario, 1, 0, "measured", 1, send, probe)
        self.assertEqual(1, result["completedScenarios"])
        self.assertEqual(["ContractError"], result["resourceErrors"])
        self.assertNotIn("resourceWindow", result)
        send.reset_mock()
        with mock.patch.object(probe, "snapshot", return_value=None):
            with self.assertRaises(http.ContractError):
                http.run_trial(cfg, target, scenario, 1, 0, "measured", 1, send, probe)
        send.assert_not_called()

    def test_window_drift_and_failure_normalization_are_explicit(self):
        """计数回退、短窗口及同容器不能生成可用比值，失败场景不产生成本。"""
        before = {"containerId": "a" * 64, "observedAtNs": 1, "cpuUsageUsec": 40,
                  "memoryCurrentBytes": 1024, "memoryLimitBytes": 4096}
        after = {**before, "observedAtNs": 2_000_000_001, "cpuUsageUsec": 140}
        window = http.resource_window(before, after, 1, 0)
        self.assertIsNone(window["cpuUsecPerCompletedScenario"])
        for changes in ({"cpuUsageUsec": 39}, {"observedAtNs": 1},
                        {"observedAtNs": 2}, {"memoryLimitBytes": 8192},
                        {"containerId": "b" * 64}, {"cpuUsageUsec": True}):
            with self.subTest(changes=changes), self.assertRaises(http.ContractError):
                http.resource_window(before, {**after, **changes}, 1, 100)

    def test_sampler_join_does_not_enter_cpu_window(self):
        """结束快照必须早于等待采样器，避免把采样尾部算作完成业务的CPU成本。"""
        probe = subject.CgroupProbe(self.docker, self.targets, self.limits)
        events = []
        snapshot = probe.snapshot

        def observed(container):
            events.append("snapshot")
            return snapshot(container)

        class Sampler:
            def __init__(self, **kwargs):
                pass
            def start(self):
                events.append("sampler-start")
            def join(self, timeout):
                events.append("sampler-join")
            def is_alive(self):
                return False

        def send(*args):
            events.append("request")
            return "ok"

        class Pool:
            def __init__(self, **kwargs):
                pass
            def __enter__(self):
                return self
            def __exit__(self, *args):
                pass
            def map(self, worker, indices):
                return map(worker, indices)

        with mock.patch.object(probe, "snapshot", side_effect=observed), \
             mock.patch.object(http.threading, "Thread", Sampler), \
             mock.patch.object(http.concurrent.futures, "ThreadPoolExecutor", Pool):
            result = http.run_trial({"runId": "a" * 32, "requestTimeoutSeconds": 1}, self.targets["java"],
                                    {"name": "read", "steps": [{}]}, 1, 0, "measured", 1, send, probe)
        self.assertEqual(["snapshot", "sampler-start", "request", "snapshot", "sampler-join"], events)
        self.assertIn("resourceWindow", result)
