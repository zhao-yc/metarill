"""同条件 Java/C# HTTP 场景回放及 Server-200 容器资源采样；不会重试业务请求。"""
from __future__ import annotations
import argparse
import concurrent.futures
import copy
import hashlib
import json
import math
import os
import pathlib
import re
import shlex
import statistics
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request


class ContractError(ValueError):
    """配置、响应或同条件证明不满足实验合同。"""


def canonical(value):
    """统一JSON字节；拒绝NaN，避免散列掩盖非法JSON。"""
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")


def digest(value):
    """只公开内容摘要，不把响应、口令或认证头写入报告。"""
    return hashlib.sha256(canonical(value)).hexdigest()


def unique_object(pairs):
    """重复键会导致验证与消费解释不同，直接拒绝。"""
    result = {}
    for key, value in pairs:
        if key in result:
            raise ContractError("JSON存在重复键")
        result[key] = value
    return result


def read_json(data):
    """请求和响应采用相同的严格JSON语法。"""
    return json.loads(data, object_pairs_hook=unique_object, parse_constant=lambda _: (_ for _ in ()).throw(ContractError("JSON包含非有限数")))


def locate(value, pointer):
    """严格JSON Pointer读取；缺失字段不是null，不能静默略过。"""
    if not isinstance(pointer, str) or not pointer.startswith("/"):
        raise ContractError("必须提供非根JSON Pointer")
    tokens = pointer[1:].split("/")
    current = value
    for token in tokens:
        if re.search(r"~(?![01])", token):
            raise ContractError("JSON Pointer转义无效")
        key = token.replace("~1", "/").replace("~0", "~")
        if isinstance(current, list):
            if not re.fullmatch(r"0|[1-9][0-9]*", key):
                raise ContractError("数组索引无效")
            key = int(key)
        elif not isinstance(current, dict):
            raise ContractError("指针经过非容器值")
        try:
            parent = current
            current = parent[key]
        except (KeyError, IndexError) as error:
            raise ContractError("JSON Pointer字段不存在") from error
    return parent, key, current


def expand(value, variables, path=False):
    """仅替换显式变量；路径变量百分号编码，不允许变量改变目标主机。"""
    if isinstance(value, str):
        def replace(match):
            """只替换当前场景已定义标识，不解释任意表达式。"""
            if match[1] not in variables:
                raise ContractError("场景变量未定义")
            text = str(variables[match[1]])
            return urllib.parse.quote(text, safe="") if path else text
        return re.sub(r"\$\{([A-Za-z][A-Za-z0-9_]*)\}", replace, value)
    if isinstance(value, list):
        return [expand(item, variables) for item in value]
    if isinstance(value, dict):
        return {key: expand(item, variables) for key, item in value.items()}
    return value


def percentile(values, percent):
    """采用最近秩百分位，单样本及尾延迟有明确边界。"""
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * percent / 100) - 1)]


class BinaryResponse:
    """只携带已验证下载的字节数和摘要，原始文件不会进入报告或内存缓存。"""
    def __init__(self, byte_length, sha256):
        """保存实体字节证据，指纹显式区分JSON与二进制响应。"""
        self.byte_length = byte_length
        self.sha256 = sha256
        self.fingerprint = digest({"kind": "binary", "byteLength": byte_length, "sha256": sha256})


def response_kind(step):
    """下载必须固定整个文件的长度和摘要，不能通过忽略字段或成功HTTP掩盖错文件。"""
    kind = step.get("responseKind", "json")
    if kind not in ("json", "binary"):
        raise ContractError("响应类型只能为json或binary")
    if kind == "binary":
        size, checksum = step.get("expectedBytes"), step.get("expectedSha256")
        if type(size) is not int or not 0 <= size <= 64 * 1024 * 1024:
            raise ContractError("二进制下载必须明确0至64MiB的完整字节数")
        if not isinstance(checksum, str) or not re.fullmatch(r"[a-f0-9]{64}", checksum):
            raise ContractError("二进制下载必须明确小写SHA-256")
        if type(step.get("status")) is not int or step["status"] != 200:
            raise ContractError("完整二进制下载只接受HTTP200")
        if step.get("checks") or step.get("capture") or step.get("ignorePointers"):
            raise ContractError("二进制响应不能套用JSON断言、捕获或忽略字段")
    elif "expectedBytes" in step or "expectedSha256" in step:
        raise ContractError("二进制完整性声明不能被JSON模式忽略")
    return kind


def read_binary(response, step):
    """以64KiB块校验完整实体；多读至多一字节检查越界，不按Content-Length代替真实内容。"""
    if response.headers.get("Content-Encoding", "identity").strip().lower() != "identity" or response.headers.get("Content-Range"):
        raise ContractError("完整下载不接受编码变换或分段响应")
    lengths = response.headers.get_all("Content-Length", [])
    if lengths and (len(lengths) != 1 or not re.fullmatch(r"[0-9]+", lengths[0]) or int(lengths[0]) != step["expectedBytes"] or response.headers.get("Transfer-Encoding")):
        raise ContractError("下载长度头与固定完整实体不符")
    received, checksum = 0, hashlib.sha256()
    while True:
        chunk = response.read(min(64 * 1024, step["expectedBytes"] - received + 1))
        if not chunk:
            break
        received += len(chunk)
        if received > step["expectedBytes"]:
            raise ContractError("下载实体超过固定字节上限")
        checksum.update(chunk)
    actual = checksum.hexdigest()
    if received != step["expectedBytes"] or actual != step["expectedSha256"]:
        raise ContractError("下载完整性不符合固定长度与SHA-256")
    return BinaryResponse(received, actual)


def validate(config):
    """连接前验证完整实验配置；绝不替缺失证明补造默认成功。"""
    if config.get("version") != 1 or not re.fullmatch(r"[a-f0-9]{32}", config.get("runId", "")):
        raise ContractError("缺少版本或固定32位runId")
    for key, minimum in (("rounds", 3), ("iterations", 100), ("warmupIterations", 1)):
        if type(config.get(key)) is not int or config[key] < minimum:
            raise ContractError("轮次、次数或预热次数不合法")
    concurrency = config.get("concurrency")
    if not isinstance(concurrency, list) or not concurrency or any(type(n) is not int or n < 1 or n > config["iterations"] for n in concurrency) or len(set(concurrency)) != len(concurrency):
        raise ContractError("并发数必须唯一且不超过每轮场景数")
    for key in ("requestTimeoutSeconds", "maxP95Ratio", "minThroughputRatio"):
        value = config.get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or value <= 0:
            raise ContractError("期限及比较阈值必须为有限正数")
    if config["maxP95Ratio"] < 1 or config["minThroughputRatio"] > 1:
        raise ContractError("性能阈值方向错误")
    targets = config.get("targets", {})
    if set(targets) != {"java", "csharp"}:
        raise ContractError("必须同时提供java与csharp")
    for target in targets.values():
        url = urllib.parse.urlsplit(target.get("baseUrl", ""))
        if url.port is not None and not 1 <= url.port <= 65535:
            raise ContractError("基础地址端口不合法")
        if url.scheme not in ("http", "https") or not url.hostname or url.username or url.password or url.query or url.fragment or target["baseUrl"].endswith("/"):
            raise ContractError("基础地址不可含凭据、查询或尾斜线")
        if not isinstance(target.get("headers", {}), dict) or any(not isinstance(k, str) or not isinstance(v, str) or "\n" in k + v or "\r" in k + v for k, v in target.get("headers", {}).items()):
            raise ContractError("认证头格式错误")
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*", target.get("container", "")):
            raise ContractError("必须提供精确容器名")
    if targets["java"]["baseUrl"] == targets["csharp"]["baseUrl"] or targets["java"]["container"] == targets["csharp"]["container"]:
        raise ContractError("两端必须为不同实例及容器")
    profile = config.get("transportProof")
    if profile not in (None, "server200-loopback-v1"):
        raise ContractError("未知HTTP接线证明模式")
    if profile:
        for name, target in targets.items():
            url = urllib.parse.urlsplit(target["baseUrl"])
            if target["baseUrl"] != f"http://127.0.0.1:{url.port}/api" or url.port is None:
                raise ContractError("本机证明只接受带明确端口的HTTP回环/api地址")
            if target["container"] != f"metarill-perf-{config['runId']}-{name}":
                raise ContractError("证明容器必须对应本轮及语言")
            if not re.fullmatch(r"sha256:[a-f0-9]{64}", target.get("expectedImageId", "")):
                raise ContractError("证明模式必须固定完整镜像ID")
            if type(target.get("containerPort")) is not int or not 1 <= target["containerPort"] <= 65535:
                raise ContractError("证明模式必须明确容器TCP端口")
    elif any("expectedImageId" in target or "containerPort" in target for target in targets.values()):
        raise ContractError("镜像或端口证明声明必须启用接线证明模式，不能静默忽略")
    names = set()
    if not isinstance(config.get("scenarios"), list) or not config["scenarios"]:
        raise ContractError("缺少原接口业务场景")
    for scenario in config["scenarios"]:
        name = scenario.get("name", "")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", name) or name in names or not scenario.get("steps"):
            raise ContractError("场景名重复或步骤缺失")
        names.add(name)
        for step in scenario["steps"]:
            path = step.get("path", "")
            if step.get("method") not in ("GET", "POST", "PUT", "PATCH", "DELETE") or not path.startswith("/") or path.startswith("//") or "#" in path:
                raise ContractError("请求必须使用固定相对路径及明确方法")
            if type(step.get("status")) is not int or not 200 <= step["status"] < 300:
                raise ContractError("必须明确成功HTTP状态")
            if response_kind(step) == "json":
                if not isinstance(step.get("checks"), list) or not step["checks"]:
                    raise ContractError("必须断言业务响应，不能把HTTP200当业务成功")
                for check in step["checks"]:
                    if set(check) != {"pointer", "equals"} or not isinstance(check["pointer"], str) or not check["pointer"].startswith("/"):
                        raise ContractError("响应断言格式错误")
                if not any(check["pointer"] == "/code" and type(check["equals"]) is int and check["equals"] == 200 for check in step["checks"]):
                    raise ContractError("原R响应必须显式断言code为数值200")
            elif any(key.lower() in ("range", "if-range") for target in targets.values() for key in target.get("headers", {})):
                raise ContractError("完整下载实验不能带Range或If-Range请求头")
            for name in step.get("capture", {}):
                if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", name) or name in ("run", "round", "iteration", "phase", "scenario", "concurrency"):
                    raise ContractError("捕获变量不可覆盖实验身份")
    canonical(config)
    return config


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """认证和写入都不跟随重定向，也不自动重放请求。"""
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def request(target, step, variables, timeout):
    """JSON保留8MiB及原类型语义；下载流式校验，不缓存或落盘原始文件。"""
    kind = response_kind(step)
    path = expand(step["path"], variables, path=True)
    headers = dict(target.get("headers", {}))
    headers["Accept"] = "*/*" if kind == "binary" else "application/json"
    if kind == "binary":
        if any(key.lower() in ("range", "if-range") for key in headers):
            raise ContractError("完整下载不接受Range或If-Range")
        headers = {key: value for key, value in headers.items() if key.lower() != "accept-encoding"}
        headers["Accept-Encoding"] = "identity"
    data = None
    if "json" in step:
        headers["Content-Type"] = "application/json"
        data = canonical(expand(step["json"], variables))
    req = urllib.request.Request(target["baseUrl"] + path, data=data, headers=headers, method=step["method"])
    # 每个请求使用独立连接，Java/C#保持相同客户端条件；正式报告明确该边界。
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    with opener.open(req, timeout=timeout) as response:
        if response.status != step["status"]:
            raise ContractError("HTTP状态不符合断言")
        if kind == "binary":
            return read_binary(response, step)
        payload = response.read(8 * 1024 * 1024 + 1)
    if len(payload) > 8 * 1024 * 1024:
        raise ContractError("响应超过8MiB")
    result = read_json(payload)
    for check in step["checks"]:
        actual = locate(result, check["pointer"])[2]
        # Python的True==1不代表协议类型相同。
        if canonical(actual) != canonical(expand(check["equals"], variables)):
            raise ContractError("业务响应不符合断言")
    for name, pointer in step.get("capture", {}).items():
        value = locate(result, pointer)[2]
        if not isinstance(value, (str, int)) or isinstance(value, bool):
            raise ContractError("后续请求只能捕获字符串或整数标识")
        variables[name] = value
    normalized = copy.deepcopy(result)
    for pointer in step.get("ignorePointers", []):
        parent, key, _ = locate(normalized, pointer)
        parent[key] = "<已声明的动态字段>"
    return digest(normalized)


class DockerProbe:
    """每条Docker命令先核验200的主机名和本机IP，强制使用该机Unix socket。"""
    def __init__(self, host, invoke=subprocess.run):
        if not re.fullmatch(r"(?:[A-Za-z0-9_.-]+@)?[A-Za-z0-9][A-Za-z0-9_.-]*", host):
            raise ContractError("SSH别名不合法")
        self.host = host
        self.invoke = invoke

    def docker(self, arguments):
        """禁止本机Docker、未知主机自动信任和任意远端Docker context。"""
        command = self.checked_command(arguments)
        result = self.invoke(["ssh", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", self.host, command], capture_output=True, text=True, timeout=20, check=True)
        return read_json(result.stdout)

    @staticmethod
    def checked_command(arguments):
        """每次命令均在执行Docker之前检查身份，不能复用一次过期主机判断。"""
        return ('set -eu; test "$(hostname)" = "Server-200"; '
                'case " $(hostname -I) " in *" 192.168.31.200 "*) ;; *) exit 71;; esac; '
                'env -u DOCKER_HOST -u DOCKER_CONTEXT docker --host unix:///var/run/docker.sock ' + shlex.join(arguments))

    def limits(self, container):
        """只读取同条件限制及镜像/容器身份，不读取环境变量或凭据。"""
        self.check_name(container)
        return self.docker(["inspect", "--format", '{"running":{{json .State.Running}},"id":{{json .Id}},"image":{{json .Image}},"nanoCpus":{{json .HostConfig.NanoCpus}},"memoryBytes":{{json .HostConfig.Memory}},"memorySwap":{{json .HostConfig.MemorySwap}},"cpuQuota":{{json .HostConfig.CpuQuota}},"cpuPeriod":{{json .HostConfig.CpuPeriod}},"cpusetCpus":{{json .HostConfig.CpusetCpus}},"name":{{json .Name}},"networkMode":{{json .HostConfig.NetworkMode}},"ports":{{json .NetworkSettings.Ports}},"leaseRun":{{json (index .Config.Labels "metarill.performance.run")}},"leaseTarget":{{json (index .Config.Labels "metarill.performance.target")}}}', container])

    @staticmethod
    def check_name(container):
        """即使直接调用采样器也不允许通配、选项或其它容器集合。"""
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*", container):
            raise ContractError("容器名必须精确且合法")

    def sample(self, container):
        """采集原始Docker百分比及内存文本，不能把采样峰值称为进程真实峰值。"""
        self.check_name(container)
        return self.docker(["stats", "--no-stream", "--format", '{{json .}}', container])


class Server200LocalProbe(DockerProbe):
    """仅在Linux宿主执行；每条Docker命令仍先确认实际是Server-200。"""
    def __init__(self, invoke=subprocess.run):
        """Windows开发机不能以本机模式运行；不在构造器启动任何进程。"""
        if sys.platform != "linux":
            raise ContractError("本机探针只允许Linux Server-200宿主")
        self.invoke = invoke

    def docker(self, arguments):
        """不使用SSH隧道；先验证主机名/IP，再访问本机显式Unix socket。"""
        result = self.invoke(["sh", "-c", self.checked_command(arguments)], capture_output=True, text=True, timeout=20, check=True)
        return read_json(result.stdout)


def verify_transport(config, probe, values):
    """只证明当前支持的宿主回环到单一发布端口，不把标签当作源码或权限证明。"""
    if not config.get("transportProof"):
        return {"mode": "unproven", "verified": False}
    if not isinstance(probe, Server200LocalProbe):
        raise ContractError("接线证明必须由Server-200本机探针执行，不能使用SSH或本机隧道")
    for name, target in config["targets"].items():
        item = values[name]
        if item.get("name") != "/" + target["container"] or item.get("leaseRun") != config["runId"] or item.get("leaseTarget") != name:
            raise ContractError("运行容器名称或本轮标识不符")
        if not re.fullmatch(r"[a-f0-9]{64}", item.get("id", "")) or item.get("image") != target["expectedImageId"]:
            raise ContractError("运行容器或固定镜像身份不符")
        network = item.get("networkMode")
        if not isinstance(network, str) or not network or network in ("host", "none") or network.startswith("container:"):
            raise ContractError("证明模式不接受共享宿主或其它容器网络")
        ports = item.get("ports")
        if not isinstance(ports, dict):
            raise ContractError("缺少实际Docker端口绑定")
        published = {key: value for key, value in ports.items() if value is not None and value != []}
        expected = {f"{target['containerPort']}/tcp": [{"HostIp": "127.0.0.1", "HostPort": str(urllib.parse.urlsplit(target["baseUrl"]).port)}]}
        if published != expected:
            raise ContractError("HTTP回环地址与唯一实际发布端口不符")
    return {"mode": config["transportProof"], "verified": True, "proxyBypassed": True,
            "boundary": "Server-200宿主回环到Docker发布端口；不证明镜像内源码、应用身份或SQL等价"}


def explicit_cpu_limit(item):
    """Docker的NanoCpus或正数CFS quota/period均可证明硬限额；零/无限制不能算通过。"""
    nano, quota, period = (item.get(k) for k in ("nanoCpus", "cpuQuota", "cpuPeriod"))
    if any(type(v) is not int for v in (nano, quota, period)) or nano < 0:
        return False
    return nano > 0 or quota > 0 and period > 0


def equal_limits(probe, targets):
    """无实际硬限制或配置不同都拒绝比较，不能把两个无限制容器当等资源。"""
    values = {name: probe.limits(target["container"]) for name, target in targets.items()}
    for item in values.values():
        if item.get("running") is not True or not explicit_cpu_limit(item) or type(item.get("memoryBytes")) is not int or item["memoryBytes"] <= 0:
            raise ContractError("容器必须运行且有明确CPU/内存硬限制")
    conditions = lambda value: {key: value[key] for key in ("nanoCpus", "memoryBytes", "memorySwap", "cpuQuota", "cpuPeriod", "cpusetCpus")}
    if not all(item.get("id") and item.get("image") for item in values.values()) or values["java"]["id"] == values["csharp"]["id"]:
        raise ContractError("缺少独立容器/镜像身份")
    if conditions(values["java"]) != conditions(values["csharp"]):
        raise ContractError("两端实际资源限制不同")
    return values


def resource_values(raw, container):
    """Docker人类可读格式转数值；明确这是离散采样，不宣称真实峰值。"""
    if raw.get("Name") != container:
        raise ContractError("采样容器身份不符")
    percent = raw.get("CPUPerc", "")
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+)?%", percent):
        raise ContractError("CPU采样值无效")
    memory = raw.get("MemUsage", "").split(" / ")
    if len(memory) != 2:
        raise ContractError("内存采样格式无效")
    def byte_value(text):
        """Docker十进制/二进制单位转换为字节，保留原始值供审查。"""
        match = re.fullmatch(r"([0-9]+(?:\.[0-9]+)?)([kKMGTPE]?i?B)", text)
        if not match:
            raise ContractError("内存采样单位无效")
        unit = match[2]
        exponent = "KMGTPE".find(unit[0].upper()) + 1 if len(unit) > 1 else 0
        return round(float(match[1]) * (1024 if "i" in unit else 1000) ** exponent)
    cpu = float(percent[:-1])
    if not math.isfinite(cpu):
        raise ContractError("CPU采样值非有限数")
    return {"cpuPercent": cpu, "memoryBytes": byte_value(memory[0]), "memoryLimitBytes": byte_value(memory[1])}


def run_trial(config, target, scenario, concurrency, round_index, phase, iterations, transport=request, probe=None):
    """每个工作线程串行回放完整业务场景；出错立即停止该场景且绝不重试。"""
    samples, sample_errors, stop = [], [], threading.Event()
    def sample_resources():
        """独立采样线程只观测对应容器，失败保留类型而不泄露连接错误正文。"""
        while not stop.is_set():
            try:
                raw = probe.sample(target["container"])
                samples.append({"elapsedSeconds": time.perf_counter() - started, "values": resource_values(raw, target["container"]), "docker": raw})
            except Exception as error:
                sample_errors.append(type(error).__name__)
                return
            stop.wait(0.5)
    def worker(worker_index):
        """同一场景变量仅属于当前迭代；失败步骤之后不继续写业务。"""
        result = []
        for index in range(worker_index, iterations, concurrency):
            variables = {"run": config["runId"], "round": round_index, "iteration": index, "phase": phase, "scenario": scenario["name"], "concurrency": concurrency}
            fingerprints, latencies, binary_responses = [], [], []
            start = time.perf_counter()
            error_name = None
            for step_index, step in enumerate(scenario["steps"]):
                step_start = time.perf_counter()
                try:
                    response = transport(target, step, variables, config["requestTimeoutSeconds"])
                    latency = (time.perf_counter() - step_start) * 1000
                    if step.get("responseKind", "json") == "binary":
                        if not isinstance(response, BinaryResponse) or type(response.byte_length) is not int or response.byte_length != step["expectedBytes"] or response.sha256 != step["expectedSha256"] or latency <= 0:
                            raise ContractError("缺少有效完整下载证据或计时")
                        fingerprints.append(response.fingerprint)
                        binary_responses.append({"stepIndex": step_index, "byteLength": response.byte_length, "sha256": response.sha256, "elapsedMs": latency})
                    else:
                        if not isinstance(response, str):
                            raise ContractError("JSON传输必须返回原响应摘要")
                        fingerprints.append(response)
                    latencies.append(latency)
                except Exception as error:
                    error_name = type(error).__name__
                    break
            row = {"iteration": index, "elapsedMs": (time.perf_counter() - start) * 1000, "requestMs": latencies, "fingerprints": fingerprints, "error": error_name, "failedStep": len(fingerprints) if error_name else None, "attemptedRequests": len(fingerprints) + (1 if error_name else 0)}
            if any(step.get("responseKind") == "binary" for step in scenario["steps"]):
                row["binaryResponses"] = binary_responses
            result.append(row)
        return result
    started = time.perf_counter()
    sampler = threading.Thread(target=sample_resources, daemon=True) if probe else None
    if sampler:
        sampler.start()
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
            rows = [row for group in pool.map(worker, range(concurrency)) for row in group]
        elapsed = time.perf_counter() - started
    finally:
        stop.set()
        if sampler:
            sampler.join(timeout=21)
            if sampler.is_alive():
                sample_errors.append("SamplerDeadlineExceeded")
    rows.sort(key=lambda row: row["iteration"])
    successful = [row for row in rows if not row["error"]]
    report = {"elapsedSeconds": elapsed, "completedScenarios": len(successful), "failedScenarios": len(rows) - len(successful), "scenarioThroughput": len(successful) / elapsed if elapsed > 0 else 0,
            "p50Ms": percentile([row["elapsedMs"] for row in successful], 50), "p95Ms": percentile([row["elapsedMs"] for row in successful], 95), "p99Ms": percentile([row["elapsedMs"] for row in successful], 99),
            "rows": rows, "resources": samples, "resourceErrors": sample_errors, "resourceSampleCount": len(samples),
            "sampledResources": {"meanCpuPercent": statistics.mean(item["values"]["cpuPercent"] for item in samples) if samples else None, "observedMaxMemoryBytes": max((item["values"]["memoryBytes"] for item in samples), default=None)}}
    if any(step.get("responseKind") == "binary" for step in scenario["steps"]):
        # 只统计完整成功场景的已验证字节；部分失败的已读数据不能抬高有效业务吞吐。
        downloads = [item for row in successful for item in row["binaryResponses"]]
        byte_length = sum(item["byteLength"] for item in downloads)
        report["binaryDownload"] = {"verifiedResponses": len(downloads), "verifiedBytes": byte_length,
            "goodputBytesPerSecond": byte_length / elapsed if elapsed > 0 else 0,
            "requestP50Ms": percentile([item["elapsedMs"] for item in downloads], 50),
            "requestP95Ms": percentile([item["elapsedMs"] for item in downloads], 95),
            "requestP99Ms": percentile([item["elapsedMs"] for item in downloads], 99)}
    return report


def compare_pair(java, csharp, config):
    """业务一致性先于性能；失败/缺样本不会作为快请求参与结论。"""
    expected = lambda trial: [(row["iteration"], row["fingerprints"]) for row in trial["rows"]]
    complete = all([row["iteration"] for row in trial["rows"]] == list(range(config["iterations"])) and all(not row.get("error") for row in trial["rows"]) for trial in (java, csharp))
    valid = complete and java["failedScenarios"] == csharp["failedScenarios"] == 0 and expected(java) == expected(csharp)
    if not valid:
        return {"valid": False, "reason": "业务失败或两端响应不一致"}
    if min(java["p95Ms"] or 0, csharp["p95Ms"] or 0, java["scenarioThroughput"], csharp["scenarioThroughput"]) <= 0:
        return {"valid": False, "reason": "缺少有效延迟或吞吐样本"}
    latency = csharp["p95Ms"] / java["p95Ms"]
    throughput = csharp["scenarioThroughput"] / java["scenarioThroughput"]
    result = {"valid": True, "p95Ratio": latency, "throughputRatio": throughput, "regression": latency > config["maxP95Ratio"] or throughput < config["minThroughputRatio"]}
    if "binaryDownload" in java or "binaryDownload" in csharp or any("binaryResponses" in row for trial in (java, csharp) for row in trial["rows"]):
        # 下载汇总还须与原逐项证据吻合，不能由缺失或非有限统计值制造快成绩。
        for trial in (java, csharp):
            summary = trial.get("binaryDownload")
            downloads = [item for row in trial["rows"] for item in row.get("binaryResponses", [])]
            if not summary or not downloads or type(summary.get("verifiedBytes")) is not int or type(summary.get("verifiedResponses")) is not int:
                return {"valid": False, "reason": "缺少完整下载统计证据"}
            if any(type(item.get("byteLength")) is not int or item["byteLength"] < 0 for item in downloads):
                return {"valid": False, "reason": "下载字节证据无效"}
            byte_length = sum(item["byteLength"] for item in downloads)
            goodput, elapsed = summary.get("goodputBytesPerSecond"), trial.get("elapsedSeconds")
            if summary["verifiedBytes"] != byte_length or summary["verifiedResponses"] != len(downloads) or type(elapsed) not in (int, float) or not math.isfinite(elapsed) or elapsed <= 0 or type(goodput) not in (int, float) or not math.isfinite(goodput) or goodput < 0 or not math.isclose(goodput, byte_length / elapsed, rel_tol=1e-9):
                return {"valid": False, "reason": "下载汇总与逐项证据不一致"}
        left, right = java["binaryDownload"], csharp["binaryDownload"]
        if left["verifiedBytes"] != right["verifiedBytes"] or left["verifiedResponses"] != right["verifiedResponses"]:
            return {"valid": False, "reason": "两端完整下载证据不一致"}
        result["binaryGoodputRatio"] = right["goodputBytesPerSecond"] / left["goodputBytesPerSecond"] if left["verifiedBytes"] > 0 else None
    return result


def run(config, probe, transport=request, on_event=lambda _: None):
    """预热不计正式结果，逐轮交替顺序；写入场景必须自带正常清理步骤。"""
    validate(config)
    before = equal_limits(probe, config["targets"])
    transport_evidence = verify_transport(config, probe, before)
    on_event({"kind": "container-preflight", "actualLimits": before, "transportEvidence": transport_evidence})
    results = []
    for scenario in config["scenarios"]:
        for concurrency in config["concurrency"]:
            for name in ("java", "csharp"):
                warmup = run_trial(config, config["targets"][name], scenario, concurrency, -1, "warmup", max(config["warmupIterations"], concurrency), transport)
                on_event({"kind": "warmup", "scenario": scenario["name"], "concurrency": concurrency, "target": name, "trial": warmup})
                if warmup["failedScenarios"]:
                    raise ContractError("预热业务失败，正式测量未开始；不重试，检查自有样本")
            pairs = []
            for round_index in range(config["rounds"]):
                order = ("java", "csharp") if round_index % 2 == 0 else ("csharp", "java")
                trials = {}
                for name in order:
                    on_event({"kind": "trial-intent", "scenario": scenario["name"], "concurrency": concurrency, "round": round_index, "target": name})
                    trials[name] = run_trial(config, config["targets"][name], scenario, concurrency, round_index, "measured", config["iterations"], transport, probe)
                    on_event({"kind": "trial", "scenario": scenario["name"], "concurrency": concurrency, "round": round_index, "target": name, "trial": trials[name]})
                pair = compare_pair(trials["java"], trials["csharp"], config)
                pair["resourcesComplete"] = all(trial["resourceSampleCount"] >= 2 and not trial["resourceErrors"] for trial in trials.values())
                pairs.append({"round": round_index, "order": list(order), "comparison": pair, "trials": trials})
            valid = all(pair["comparison"]["valid"] and pair["comparison"]["resourcesComplete"] for pair in pairs)
            regressions = sum(pair["comparison"].get("regression", False) for pair in pairs)
            results.append({"scenario": scenario["name"], "concurrency": concurrency, "measurementValid": valid, "regressionRounds": regressions, "stableRegression": valid and regressions >= math.ceil(config["rounds"] * 2 / 3), "pairs": pairs})
    after = equal_limits(probe, config["targets"])
    verify_transport(config, probe, after)
    if before != after:
        raise ContractError("实验前后容器资源限制变化，结果不可作为同条件证据")
    return {"version": 1, "runId": config["runId"], "configSha256": digest(config), "actualLimits": before, "transportEvidence": transport_evidence, "results": results, "releaseAccepted": False,
            "remainingEvidence": ["独立测试数据/索引及SQL实际等价核验", *([] if transport_evidence["verified"] else ["URL与容器实例的真实接线核验"]), "共享MySQL/主机负载核验", "两端应用/运行时/镜像及MySQL资源固定", "写入失败后的独占样本核验及清理", "浏览器/设备及完整首版验收"],
            "clientBoundary": "Python urllib独立HTTP连接；二进制含流式SHA256客户端成本；未测连接池或浏览器；Docker采样峰值不等于真实峰值"}


def main():
    """只在显式run命令执行HTTP/SSH；validate完全离线且不输出私有配置。"""
    parser = argparse.ArgumentParser(description="Java/C#同条件HTTP回放与200容器采样")
    parser.add_argument("operation", choices=("validate", "run"))
    parser.add_argument("--config", required=True)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--ssh-host")
    mode.add_argument("--server-local", action="store_true", help="仅在Server-200 Linux宿主执行并核验回环接线")
    parser.add_argument("--output")
    args = parser.parse_args()
    try:
        config = validate(read_json(pathlib.Path(args.config).read_bytes()))
        if args.operation == "validate":
            print("离线配置验证通过；未执行HTTP、SSH或Docker。")
            return 0
        if not (args.ssh_host or args.server_local) or not args.output:
            raise ContractError("run必须提供SSH别名或200本机模式及私有输出目录")
        if bool(config.get("transportProof")) != args.server_local:
            raise ContractError("200本机模式必须配套完整接线证明配置，SSH模式不提供此证明")
        probe = Server200LocalProbe() if args.server_local else DockerProbe(args.ssh_host)
        output = pathlib.Path(args.output)
        output.mkdir(parents=True, exist_ok=False)
        # 先登记意图；中断也留有精确配置摘要，不复用旧报告目录重跑。
        (output / "intent.json").write_bytes(canonical({"runId": config["runId"], "configSha256": digest(config), "startedAt": time.time()}))
        try:
            def journal(event):
                """每个阶段追加原始摘要证据，中断也保留已完成轮次，不记录私有配置。"""
                with (output / "events.jsonl").open("ab") as stream:
                    stream.write(canonical({"observedAt": time.time(), **event}) + b"\n")
                    stream.flush()
                    os.fsync(stream.fileno())
            report = run(config, probe, on_event=journal)
            (output / "measurement.json").write_bytes(canonical(report))
        except Exception as error:
            (output / "failure.json").write_bytes(canonical({"errorType": type(error).__name__, "runId": config["runId"], "retryAttempted": False}))
            raise
        print("测量已保存；同SQL/数据/索引等证据须独立核验，不代表性能验收通过。")
        return 2 if any(not item["measurementValid"] or item["stableRegression"] for item in report["results"]) else 0
    except Exception as error:
        print("测量未通过：" + type(error).__name__ + "；保留本轮证据，不自动重试。")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
