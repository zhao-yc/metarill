"""短轮采样从固定容器绑定的 cgroup 读取，测试无需 Docker 或宿主变更。"""
from pathlib import Path
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
             mock.patch.object(subject.time, "monotonic_ns", side_effect=[1, 100000001]):
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
