"""独立复核已回收的原四请求只读测量，不调用 HTTP、SSH、Docker 或数据库。"""
from __future__ import annotations

import argparse
import hashlib
import math
from pathlib import Path
import re
import statistics

from paired_http import canonical, read_json
from paired_measurement_fixture import COUNTS


SCENARIO = "customer-count-contract-child"
SIDES = ("java", "csharp")
ITERATIONS = 400
STEPS = 4
DIGEST = re.compile(r"[a-f0-9]{64}")


def require(condition, message):
    """缺少原始证据即停止，错误不输出响应或路径内容。"""
    if not condition:
        raise ValueError(message)


def positive(number):
    """禁止布尔值、非有限值及零时长参与统计。"""
    return type(number) in (float, int) and math.isfinite(number) and number > 0


def digest_list(value):
    """逐步检查四个真实响应摘要，不允许缩短响应证明。"""
    return isinstance(value, list) and len(value) == STEPS and all(
        isinstance(item, str) and DIGEST.fullmatch(item) for item in value)


def checked_rows(trial, count):
    """逐项核对预热和正式轮的请求、索引及耗时。"""
    rows = trial["rows"]
    require(positive(trial["elapsedSeconds"]) and len(rows) == count
            and trial["completedScenarios"] == count and trial["failedScenarios"] == 0,
            "轮次不完整或含失败场景")
    for index, row in enumerate(rows):
        require(type(row["iteration"]) is int and row["iteration"] == index
                and row["error"] is None and row["failedStep"] is None
                and row["attemptedRequests"] == STEPS and digest_list(row["fingerprints"])
                and positive(row["elapsedMs"]) and len(row["requestMs"]) == STEPS
                and all(map(positive, row["requestMs"])), "场景请求或耗时不符")
    return rows


def trial_values(trial):
    """从正式轮的每一场景重算 p95、吞吐和计时窗口内资源。"""
    elapsed = trial["elapsedSeconds"]
    rows = checked_rows(trial, ITERATIONS)
    p95 = sorted(row["elapsedMs"] for row in rows)[379]
    throughput = len(rows) / elapsed
    require(trial["p95Ms"] == p95
            and math.isclose(trial["scenarioThroughput"], throughput, rel_tol=1e-12),
            "正式轮汇总与逐场景记录不符")
    require(not trial["resourceErrors"], "正式轮的采样器报错")
    samples = [sample for sample in trial["resources"] if 0 <= sample["elapsedSeconds"] <= elapsed]
    require(len(samples) >= 2, "正式窗口内资源样本不足两份")
    values = [sample["values"] for sample in samples]
    require(all(type(item["memoryBytes"]) is int and item["memoryBytes"] >= 0
                and type(item["cpuPercent"]) in (int, float) and math.isfinite(item["cpuPercent"])
                and item["cpuPercent"] >= 0 for item in values), "资源采样数值无效")
    return {"p95Ms": p95, "scenarioThroughput": throughput, "windowSamples": len(samples),
            "outsideSamples": len(trial["resources"]) - len(samples) + len(trial.get("lateResources", [])),
            "meanCpuPercent": statistics.mean(item["cpuPercent"] for item in values),
            "observedMaxMemoryBytes": max(item["memoryBytes"] for item in values)}


def review(report, preflight, journal, run):
    """从原始记录复核固定只读档位、逐轮对应关系和退化规则。"""
    require(report["runId"] == preflight["runId"] == run and report["version"] == 1
            and report["configSha256"] == preflight["configSha256"]
            and DIGEST.fullmatch(report["configSha256"]), "测量与首检归属不符")
    fingerprints = preflight["responseFingerprints"]
    require(set(fingerprints) == set(SIDES) and digest_list(fingerprints["java"])
            and fingerprints["java"] == fingerprints["csharp"], "四请求首检不一致")
    transport = report["transportEvidence"]
    require(transport["mode"] == "server200-loopback-v1" and transport["verified"] is True
            and transport["proxyBypassed"] is True and report["releaseAccepted"] is False,
            "双端接线或首版边界不符")
    settings = report["databaseBefore"]["variables"]
    require(set(settings) == {"version", "bufferPoolBytes", "maxConnections", "transactionIsolation",
                              "globalTimeZone", "globalSqlMode"}
            and settings == report["databaseAfter"]["variables"] and report["databaseVariablesUnchanged"] is True,
            "实验期间数据库设置缺失或变化")
    expected = [("container-preflight", None, None, None)]
    trials = {}
    outcomes = []
    require([item["concurrency"] for item in report["results"]] == [1, 4], "并发档位不符")
    for result in report["results"]:
        concurrency = result["concurrency"]
        require(result["scenario"] == SCENARIO and [item["round"] for item in result["pairs"]] == [0, 1, 2],
                "正式场景或轮次不符")
        expected.extend(("warmup", concurrency, None, side) for side in SIDES)
        rounds = []
        for pair in result["pairs"]:
            index = pair["round"]
            order = SIDES if index % 2 == 0 else SIDES[::-1]
            require(pair["order"] == list(order) and set(pair["trials"]) == set(SIDES), "双端顺序或样本缺失")
            for side in order:
                expected.extend((kind, concurrency, index, side) for kind in ("trial-intent", "trial"))
                trials[concurrency, index, side] = pair["trials"][side]
            java, csharp = (pair["trials"][side] for side in SIDES)
            require([row["fingerprints"] for row in java["rows"]] ==
                    [row["fingerprints"] for row in csharp["rows"]], "双端逐场景响应不一致")
            baseline, candidate = trial_values(java), trial_values(csharp)
            p95_ratio = candidate["p95Ms"] / baseline["p95Ms"]
            throughput_ratio = candidate["scenarioThroughput"] / baseline["scenarioThroughput"]
            regression = p95_ratio > 1.10 or throughput_ratio < 0.95
            comparison = pair["comparison"]
            require(comparison["valid"] is True and comparison["resourcesComplete"] is True
                    and comparison["p95Ratio"] == p95_ratio and comparison["throughputRatio"] == throughput_ratio
                    and comparison["regression"] is regression, "单轮退化结论与原始记录不符")
            rounds.append({"round": index, "java": baseline, "csharp": candidate, "p95Ratio": p95_ratio,
                           "throughputRatio": throughput_ratio, "regression": regression})
        regressions = sum(item["regression"] for item in rounds)
        stable = regressions >= 2
        require(result["measurementValid"] is True and result["regressionRounds"] == regressions
                and result["stableRegression"] is stable, "并发档位退化结论与原始记录不符")
        outcomes.append({"concurrency": concurrency, "rounds": rounds, "stableRegression": stable})
    actual = [(event["kind"], event.get("concurrency"), event.get("round"), event.get("target")) for event in journal]
    require(actual == expected, "原始事件序列不完整或顺序变化")
    require(journal[0]["actualLimits"] == report["actualLimits"]
            and journal[0]["transportEvidence"] == transport, "容器首检与正式报告不符")
    warmup = {}
    for event in journal[1:]:
        require(event.get("scenario") == SCENARIO, "原始事件的场景与固定报告不符")
        if event["kind"] == "warmup":
            warmup[event["concurrency"], event["target"]] = [
                row["fingerprints"] for row in checked_rows(event["trial"], 8)]
        if event["kind"] in ("trial-intent", "trial"):
            require(event["databaseContext"]["variables"] == settings, "期中数据库配置与前后不符")
        if event["kind"] == "trial":
            key = event["concurrency"], event["round"], event["target"]
            require(event["trial"] == trials[key], "事件与正式报告不一致")
    for concurrency in (1, 4):
        require(warmup[concurrency, "java"] == warmup[concurrency, "csharp"], "双端预热响应不一致")
    return {"runId": run, "measuredRequests": 19200, "warmupRequests": 128,
            "preflightRequests": 8, "results": outcomes, "releaseAccepted": False}


def review_attempt(folder, run):
    """先验证独立收回的证据摘要和物理回读，再审核测量文件。"""
    manifest = read_json((folder / "evidence-files.json").read_bytes())

    def evidence(relative):
        """只读取指定证据文件，并逐份核对收回时的 SHA-256。"""
        require(relative in manifest, "缺少正式测量或原始证据")
        raw = (folder / "evidence" / relative).read_bytes()
        require(hashlib.sha256(raw).hexdigest() == manifest[relative], "证据字节摘要不符")
        return raw

    state = read_json(evidence("native-state.json"))
    require(state["runId"] == run and state["leaseStatus"] == "ended"
            and state["acceptedScope"] == "paired-read-measurement-only"
            and not state["cleanupErrors"] and state["releaseAccepted"] is False, "正式范围或资源清理不符")
    require(state.get("measurement", {}).get("physicalFixtureUnchanged") is True,
            "缺少正式负载或负载后物理回读")
    require(all(state["measurementSeeds"][side]["rows"] == COUNTS
                and state["measurementSeeds"][side]["physicalReadbackVerified"] is True
                and state["measurementSeeds"][side]["fixedIndexesVerified"] is True for side in SIDES),
            "缺少物理种子与索引证明")
    absence = read_json((folder / "independent-absence.json").read_bytes())
    require(absence["newSsh"] is True and absence["executionProcessStopped"] is True
            and absence["databasesAbsent"] == 2 and absence["containersAbsent"] == 9
            and all(absence[key] is True for key in ("userAbsent", "networkAbsent", "ownershipAbsent",
                                                   "sourceCredentialsAbsent", "dataDirectoriesAbsent")), "缺少独立核零")
    report = read_json(evidence("measurement/measurement.json"))
    require(report["actualLimits"] == state["resourceEvidence"]
            and report["transportEvidence"] == state["transport"], "正式报告与宿主资源回执不符")
    return review(report, read_json(evidence("measurement/preflight.json")),
                  [read_json(line) for line in evidence("measurement/events.jsonl").splitlines()], run)


def main():
    """只复核现有证据；可比不等于首版验收。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--attempt", type=Path, required=True)
    parser.add_argument("--run", required=True)
    args = parser.parse_args()
    try:
        require(re.fullmatch(r"[a-f0-9]{32}", args.run), "运行号无效")
        print(canonical(review_attempt(args.attempt, args.run)).decode("utf-8"))
    except (ValueError, KeyError, TypeError, OSError) as error:
        print("正式证据复核未通过：" + (str(error) if type(error) is ValueError else type(error).__name__))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
