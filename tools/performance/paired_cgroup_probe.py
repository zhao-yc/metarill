"""通过已证明的 Docker 容器身份读取宿主 cgroup v2 计数，适配短时回放。"""
from pathlib import Path, PurePosixPath
import re
import time

CGROUP_ROOT = Path("/sys/fs/cgroup")
PROC_ROOT = Path("/proc")
METHOD = "server200-cgroup-v2-100ms"
INTERVAL = 0.10


def numeric(text):
    """拒绝不可读、不受限或不可解析的内核计数。"""
    if not re.fullmatch(r"[0-9]{1,24}", text):
        raise ValueError("measurement-cgroup-number")
    return int(text)


class CgroupProbe:
    """只绑定本轮两端宿主；实际取样仅读取内核文件，不启动 Docker 子进程。"""

    def __init__(self, probe, targets, limits):
        """将已证明的容器名、内容 ID、进程与宿主 cgroup 逐一绑定。"""
        self.probe = probe
        self.bindings = {}
        for side, target in targets.items():
            name = target["container"]
            limit = limits[side]
            if limit.get("name") != "/" + name or not re.fullmatch(r"[a-f0-9]{64}", limit.get("id", "")):
                raise ValueError("measurement-cgroup-container")
            self.bindings[name] = self.binding(name, limit["id"], limit["memoryBytes"])

    def binding(self, name, identifier, memory_limit):
        """Docker 查询沿用主机／Unix 端点验证；PID 对应的组目录必须带完整容器 ID。"""
        self.probe.check_name(name)
        actual = self.probe.docker(["inspect", "--format",
                                    '{"id":{{json .Id}},"pid":{{json .State.Pid}}}', name])
        pid = actual.get("pid")
        if actual.get("id") != identifier or type(pid) is not int or pid <= 0:
            raise ValueError("measurement-cgroup-process")
        raw = (PROC_ROOT / str(pid) / "cgroup").read_text(encoding="ascii").splitlines()
        if len(raw) != 1 or not raw[0].startswith("0::/"):
            raise ValueError("measurement-cgroup-version")
        relative = PurePosixPath(raw[0][4:])
        if relative.is_absolute() or ".." in relative.parts or relative.name not in (identifier, "docker-" + identifier + ".scope"):
            raise ValueError("measurement-cgroup-path")
        root = CGROUP_ROOT.resolve(strict=True)
        path = (root / relative).resolve(strict=True)
        if not path.is_relative_to(root) or path.relative_to(root).as_posix() != relative.as_posix():
            raise ValueError("measurement-cgroup-path")
        if numeric((path / "memory.max").read_text(encoding="ascii").strip()) != memory_limit:
            raise ValueError("measurement-cgroup-memory-limit")
        stat = path.stat()
        return (identifier, pid, path, stat.st_dev, stat.st_ino, memory_limit)

    def verify(self):
        """回放后重新核对进程及组目录，防止中途重启后沿用旧资源证据。"""
        for name, bound in self.bindings.items():
            if self.binding(name, bound[0], bound[-1]) != bound:
                raise ValueError("measurement-cgroup-binding-drift")

    @staticmethod
    def usage(path, device, inode):
        """从同一个内核组读取累计 CPU 微秒；目录漂移或计数缺失直接拒绝。"""
        stat = path.stat()
        if (stat.st_dev, stat.st_ino) != (device, inode):
            raise ValueError("measurement-cgroup-binding-drift")
        entries = re.findall(r"^usage_usec ([0-9]{1,24})$", (path / "cpu.stat").read_text(encoding="ascii"), re.MULTILINE)
        if len(entries) != 1:
            raise ValueError("measurement-cgroup-cpu-counter")
        return int(entries[0]), time.monotonic_ns()

    def sample(self, container):
        """读取约 100 毫秒 CPU 增量和结束时内存，保留独立复算所需的原始计数。"""
        identifier, _, path, device, inode, limit = self.bindings[container]
        start_cpu, start_ns = self.usage(path, device, inode)
        time.sleep(INTERVAL)
        end_cpu, end_ns = self.usage(path, device, inode)
        elapsed_ns = end_ns - start_ns
        if elapsed_ns <= 0 or end_cpu < start_cpu:
            raise ValueError("measurement-cgroup-counter-drift")
        memory = numeric((path / "memory.current").read_text(encoding="ascii").strip())
        cpu = (end_cpu - start_cpu) * 100000 / elapsed_ns
        return {"Name": container, "CPUPerc": f"{cpu:.6f}%", "MemUsage": f"{memory}B / {limit}B",
                "source": METHOD, "containerId": identifier, "cpuUsageUsec": [start_cpu, end_cpu],
                "sampleElapsedNs": elapsed_ns, "memoryCurrentBytes": memory, "memoryLimitBytes": limit}
