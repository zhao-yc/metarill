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
            if type(step.get("status")) is not int or not 200 <= step["status"] < 300 or not step.get("checks"):
                raise ContractError("必须断言HTTP状态及业务响应，不能把HTTP200当业务成功")
            for check in step["checks"]:
                if set(check) != {"pointer", "equals"} or not isinstance(check["pointer"], str) or not check["pointer"].startswith("/"):
                    raise ContractError("响应断言格式错误")
            if not any(check["pointer"] == "/code" and type(check["equals"]) is int and check["equals"] == 200 for check in step["checks"]):
                raise ContractError("原R响应必须显式断言code为数值200")
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
    """响应限8MiB、显式业务校验；保留原数字/字符串/null类型差别。"""
    path = expand(step["path"], variables, path=True)
    headers = dict(target.get("headers", {}))
    headers["Accept"] = "application/json"
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
    """每条Docker命令先经SSH核验200的本机IP，强制使用该机Unix socket。"""
    def __init__(self, host, invoke=subprocess.run):
        if not re.fullmatch(r"(?:[A-Za-z0-9_.-]+@)?[A-Za-z0-9][A-Za-z0-9_.-]*", host):
            raise ContractError("SSH别名不合法")
        self.host = host
        self.invoke = invoke

    def docker(self, arguments):
        """禁止本机Docker、未知主机自动信任和任意远端Docker context。"""
        command = ('set -eu; case " $(hostname -I) " in *" 192.168.31.200 "*) ;; *) exit 71;; esac; '
                   'env -u DOCKER_HOST -u DOCKER_CONTEXT docker --host unix:///var/run/docker.sock ' + shlex.join(arguments))
        result = self.invoke(["ssh", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", self.host, command], capture_output=True, text=True, timeout=20, check=True)
        return read_json(result.stdout)

    def limits(self, container):
        """只读取同条件限制及镜像/容器身份，不读取环境变量或凭据。"""
        self.check_name(container)
        return self.docker(["inspect", "--format", '{"running":{{json .State.Running}},"id":{{json .Id}},"image":{{json .Image}},"nanoCpus":{{json .HostConfig.NanoCpus}},"memoryBytes":{{json .HostConfig.Memory}},"memorySwap":{{json .HostConfig.MemorySwap}},"cpuQuota":{{json .HostConfig.CpuQuota}},"cpuPeriod":{{json .HostConfig.CpuPeriod}},"cpusetCpus":{{json .HostConfig.CpusetCpus}}}', container])

    @staticmethod
    def check_name(container):
        """即使直接调用采样器也不允许通配、选项或其它容器集合。"""
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*", container):
            raise ContractError("容器名必须精确且合法")

    def sample(self, container):
        """采集原始Docker百分比及内存文本，不能把采样峰值称为进程真实峰值。"""
        self.check_name(container)
        return self.docker(["stats", "--no-stream", "--format", '{{json .}}', container])


def equal_limits(probe, targets):
    """无实际硬限制或配置不同都拒绝比较，不能把两个无限制容器当等资源。"""
    values = {name: probe.limits(target["container"]) for name, target in targets.items()}
    for item in values.values():
        if item.get("running") is not True or type(item.get("nanoCpus")) is not int or item["nanoCpus"] <= 0 or type(item.get("memoryBytes")) is not int or item["memoryBytes"] <= 0:
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
            fingerprints, latencies = [], []
            start = time.perf_counter()
            error_name = None
            for step in scenario["steps"]:
                step_start = time.perf_counter()
                try:
                    fingerprints.append(transport(target, step, variables, config["requestTimeoutSeconds"]))
                    latencies.append((time.perf_counter() - step_start) * 1000)
                except Exception as error:
                    error_name = type(error).__name__
                    break
            result.append({"iteration": index, "elapsedMs": (time.perf_counter() - start) * 1000, "requestMs": latencies, "fingerprints": fingerprints, "error": error_name, "failedStep": len(fingerprints) if error_name else None, "attemptedRequests": len(fingerprints) + (1 if error_name else 0)})
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
    return {"elapsedSeconds": elapsed, "completedScenarios": len(successful), "failedScenarios": len(rows) - len(successful), "scenarioThroughput": len(successful) / elapsed if elapsed > 0 else 0,
            "p50Ms": percentile([row["elapsedMs"] for row in successful], 50), "p95Ms": percentile([row["elapsedMs"] for row in successful], 95), "p99Ms": percentile([row["elapsedMs"] for row in successful], 99),
            "rows": rows, "resources": samples, "resourceErrors": sample_errors, "resourceSampleCount": len(samples),
            "sampledResources": {"meanCpuPercent": statistics.mean(item["values"]["cpuPercent"] for item in samples) if samples else None, "observedMaxMemoryBytes": max((item["values"]["memoryBytes"] for item in samples), default=None)}}


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
    return {"valid": True, "p95Ratio": latency, "throughputRatio": throughput, "regression": latency > config["maxP95Ratio"] or throughput < config["minThroughputRatio"]}


def run(config, probe, transport=request, on_event=lambda _: None):
    """预热不计正式结果，逐轮交替顺序；写入场景必须自带正常清理步骤。"""
    validate(config)
    before = equal_limits(probe, config["targets"])
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
    if before != after:
        raise ContractError("实验前后容器资源限制变化，结果不可作为同条件证据")
    return {"version": 1, "runId": config["runId"], "configSha256": digest(config), "actualLimits": before, "results": results, "releaseAccepted": False,
            "remainingEvidence": ["独立测试数据/索引及SQL实际等价核验", "URL与容器实例的真实接线及共享MySQL/主机负载核验", "两端应用/运行时/镜像及MySQL资源固定", "写入失败后的独占样本核验及清理", "浏览器/设备及完整首版验收"],
            "clientBoundary": "Python urllib独立HTTP连接；未测连接池或浏览器；Docker采样峰值不等于真实峰值"}


def main():
    """只在显式run命令执行HTTP/SSH；validate完全离线且不输出私有配置。"""
    parser = argparse.ArgumentParser(description="Java/C#同条件HTTP回放与200容器采样")
    parser.add_argument("operation", choices=("validate", "run"))
    parser.add_argument("--config", required=True)
    parser.add_argument("--ssh-host")
    parser.add_argument("--output")
    args = parser.parse_args()
    try:
        config = validate(read_json(pathlib.Path(args.config).read_bytes()))
        if args.operation == "validate":
            print("离线配置验证通过；未执行HTTP、SSH或Docker。")
            return 0
        if not args.ssh_host or not args.output:
            raise ContractError("run必须提供SSH别名和私有输出目录")
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
            report = run(config, DockerProbe(args.ssh_host), on_event=journal)
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
