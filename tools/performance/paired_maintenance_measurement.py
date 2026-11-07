"""固定原维护接口的同条件计时，写回与无变化回放分别判定。"""
from threading import Event
import time

import paired_cgroup_probe as cgroups
import paired_http as http
import paired_maintenance_fixture as fixture
from paired_measurement import database_context

PROFILES = (("repair", 1, 1), ("repeat-single", 1, 4), ("repeat-parallel", 4, 8))
ROUNDS = 3
WARMUP_ROUND = -1
REPLAY_REQUESTS = 2 * len(fixture.JOINS) * (ROUNDS + 1) * sum(count for _, _, count in PROFILES)


def summarize(comparisons):
    """按表和负载分别判断三轮中的稳定退化。"""
    groups = {(kind, profile): [] for kind in fixture.JOINS for profile, _, _ in PROFILES}
    for row in comparisons:
        groups[row["kind"], row["profile"]].append(row)
    valid = all(sorted(row["round"] for row in rows) == list(range(ROUNDS))
                and all(row["comparison"]["valid"] for row in rows) for rows in groups.values())
    return valid, valid and any(sum(row["comparison"]["regression"] for row in rows) >= 2
                                for rows in groups.values())


def run_measurement(root, run, targets, probe, query, execute_owned, request):
    """按两端交替次序维护；置旧、回读和环境核对均在计时窗口外。"""
    output = root / "measurement"
    output.mkdir(mode=0o700)
    config = {"runId": run, "transportProof": "server200-loopback-v1", "targets": targets,
              "requestTimeoutSeconds": 30, "maxP95Ratio": 1.10, "minThroughputRatio": 0.95}
    limits = http.equal_limits(probe, targets)
    transport = http.verify_transport(config, probe, limits)
    resource_probe = cgroups.CgroupProbe(probe, targets, limits)
    initial = database_context(query)
    comparisons = []
    issued = 0

    def trial(side, kind, profile, concurrency, count, round_index):
        """真正写回一次，后续重复场景在已经正确的物理数据上执行。"""
        join = fixture.JOINS[kind]
        repair = profile == "repair"
        if repair:
            fixture.require(execute_owned(fixture.damage_sql(run, side, kind)) == "1\n1", "replay-damage")
        fixture.verify_cache(run, side, kind, query, stale=repair)
        target = targets[side]
        scenario = {"name": "maintenance-" + kind + "-" + profile, "steps": [{}]}
        before = database_context(query)
        start = resource_probe.snapshot(target["container"])
        stop = Event()

        def send(_target, _step, _variables, timeout):
            """首个坏响应停止发新请求，已发出的请求由工具收齐。"""
            if stop.is_set():
                raise ValueError("measurement-maintenance-replay-stopped")
            try:
                response = request(side, fixture.PATH, {"configTableId": join.table_id}, timeout)
                fixture.verify_response(side, kind, response, repeated=not repair)
                return "maintenance-" + kind + "-" + profile
            except Exception:
                stop.set()
                raise

        phase = "warmup" if round_index == WARMUP_ROUND else "measured"
        timings = http.run_trial(config, target, scenario, concurrency, round_index, phase, count, send)
        end = resource_probe.snapshot(target["container"])
        after = database_context(query)
        fact = {"side": side, "kind": kind, "profile": profile, "round": round_index,
                "phase": phase, "concurrency": concurrency, "before": before, "after": after,
                "timings": timings, "resourceBefore": start, "resourceAfter": end}
        with (output / "events.jsonl").open("ab") as stream:
            stream.write(http.canonical(fact) + b"\n")
        fixture.require(timings["completedScenarios"] == count and not timings["failedScenarios"], "replay-request")
        fixture.require(before["variables"] == after["variables"] == initial["variables"], "replay-database-drift")
        fixture.require(end["cpuUsageUsec"] >= start["cpuUsageUsec"] and end["observedAtNs"] > start["observedAtNs"],
                        "replay-resource-drift")
        fixture.verify_cache(run, side, kind, query, stale=False)
        return timings

    for round_index in range(WARMUP_ROUND, ROUNDS):
        order = ("csharp", "java") if round_index % 2 else ("java", "csharp")
        for kind in fixture.JOINS:
            for profile, concurrency, count in PROFILES:
                pair = {side: trial(side, kind, profile, concurrency, count, round_index) for side in order}
                issued += count * len(pair)
                if round_index != WARMUP_ROUND:
                    comparisons.append({"kind": kind, "profile": profile, "round": round_index, "order": list(order),
                                        "comparison": http.compare_pair(pair["java"], pair["csharp"],
                                                                        {**config, "iterations": count})})
    fixture.require(issued == REPLAY_REQUESTS, "replay-budget")
    fixture.require(http.equal_limits(probe, targets) == limits, "replay-resource-limits")
    fixture.require(http.verify_transport(config, probe, limits) == transport, "replay-transport")
    resource_probe.verify()
    valid, regression = summarize(comparisons)
    report = {"measurementValid": valid, "stableRegression": regression, "replayRequests": issued,
              "comparison": comparisons, "transport": transport, "limits": limits,
              "resourceSource": "server200-cgroup-v2-before-after", "report": "measurement/measurement.json"}
    (output / "measurement.json").write_bytes(http.canonical(report))
    return report

# 与原回放预算相同，改变请求分配以分别观察充分预热后的真实修复。
REPAIR_COST_PROFILE = "fresh-repair-cost-v1"
REPAIR_WARMUP_SAMPLES = 16
REPAIR_MEASURED_SAMPLES = 12


def run_repair_cost_measurement(root, run, targets, probe, query, execute_owned, request):
    """每次重新置旧并独立读回，将故障准备和核验排除在 HTTP／CPU 窗口之外。"""
    expected = 2 * len(fixture.JOINS) * (REPAIR_WARMUP_SAMPLES + ROUNDS * REPAIR_MEASURED_SAMPLES)
    fixture.require(expected == REPLAY_REQUESTS, "repair-cost-budget")
    output = root / "measurement"
    output.mkdir(mode=0o700)
    config = {"runId": run, "transportProof": "server200-loopback-v1", "targets": targets,
              "iterations": REPAIR_MEASURED_SAMPLES, "maxP95Ratio": 1.10, "minThroughputRatio": 0.95}
    limits = http.equal_limits(probe, targets)
    transport = http.verify_transport(config, probe, limits)
    counters = cgroups.CgroupProbe(probe, targets, limits)
    initial = database_context(query)
    comparisons = []
    issued = 0

    def trial(side, kind, round_index):
        """顺序执行同量修复，保留每个真实请求和精确 CPU 切片，失败直接停止本轮。"""
        target = targets[side]
        count = REPAIR_WARMUP_SAMPLES if round_index == WARMUP_ROUND else REPAIR_MEASURED_SAMPLES
        phase = "warmup" if round_index == WARMUP_ROUND else "measured"
        before = database_context(query)
        slices = []
        rows = []
        for index in range(count):
            fixture.require(execute_owned(fixture.damage_sql(run, side, kind)) == "1\n1", "repair-cost-damage")
            fixture.verify_cache(run, side, kind, query, stale=True)
            resource_before = counters.snapshot(target["container"])
            http.validate_resource_snapshot(resource_before)
            started = time.perf_counter()
            response = request(side, fixture.PATH, {"configTableId": fixture.JOINS[kind].table_id}, 30)
            elapsed = time.perf_counter() - started
            resource_after = counters.snapshot(target["container"])
            window = http.resource_window(resource_before, resource_after, elapsed, 1)
            fixture.verify_response(side, kind, response, repeated=False)
            fixture.verify_cache(run, side, kind, query, stale=False)
            slices.append(window)
            rows.append({"iteration": index, "elapsedMs": elapsed * 1000,
                         "fingerprints": ["maintenance-repair-" + kind], "error": None})
            fact = {"side": side, "kind": kind, "profile": REPAIR_COST_PROFILE,
                    "phase": phase, "round": round_index, "sample": index,
                    "httpElapsedSeconds": elapsed, "resourceWindow": window,
                    "verifiedChangedValues": fixture.SOURCE_COUNT}
            with (output / "events.jsonl").open("ab") as stream:
                stream.write(http.canonical(fact) + b"\n")
        after = database_context(query)
        fixture.require(before["variables"] == after["variables"] == initial["variables"],
                        "repair-cost-database-drift")
        elapsed = sum(row["elapsedMs"] for row in rows) / 1000
        cpu = sum(window["cpuUsageUsec"] for window in slices)
        return {"rows": rows, "failedScenarios": 0, "completedScenarios": count,
                "elapsedSeconds": elapsed, "p95Ms": http.percentile([row["elapsedMs"] for row in rows], 95),
                "scenarioThroughput": count / elapsed, "cpuUsageUsec": cpu,
                "cpuUsecPerRepair": cpu / count,
                "observedBoundaryMaxMemoryBytes": max(window["observedBoundaryMaxMemoryBytes"] for window in slices),
                "databaseBefore": before, "databaseAfter": after}

    for round_index in range(WARMUP_ROUND, ROUNDS):
        order = ("csharp", "java") if round_index % 2 else ("java", "csharp")
        for kind in fixture.JOINS:
            pair = {side: trial(side, kind, round_index) for side in order}
            issued += sum(item["completedScenarios"] for item in pair.values())
            if round_index != WARMUP_ROUND:
                comparison = http.compare_pair(pair["java"], pair["csharp"], config)
                cpu = pair["java"]["cpuUsecPerRepair"]
                comparison["cpuCostRatio"] = pair["csharp"]["cpuUsecPerRepair"] / cpu if cpu else None
                comparisons.append({"kind": kind, "round": round_index, "order": list(order),
                                    "comparison": comparison, "trials": pair})
    fixture.require(issued == REPLAY_REQUESTS, "repair-cost-request-count")
    fixture.require(http.equal_limits(probe, targets) == limits, "repair-cost-resource-limits")
    fixture.require(http.verify_transport(config, probe, limits) == transport, "repair-cost-transport")
    counters.verify()
    valid = len(comparisons) == len(fixture.JOINS) * ROUNDS and all(row["comparison"]["valid"] for row in comparisons)
    regression = valid and any(sum(row["comparison"].get("regression", False) for row in comparisons
                                  if row["kind"] == kind) >= 2 for kind in fixture.JOINS)
    report = {"measurementValid": valid, "stableRegression": regression, "replayRequests": issued,
              "profile": REPAIR_COST_PROFILE, "warmupSamplesPerSideAndKind": REPAIR_WARMUP_SAMPLES,
              "measuredSamplesPerRoundSideAndKind": REPAIR_MEASURED_SAMPLES,
              "comparison": comparisons, "transport": transport, "limits": limits,
              "resourceSource": "server200-cgroup-v2-per-repaired-request",
              "timingBoundary": "sum-of-http-windows-excluding-fixture-damage-and-verification",
              "report": "measurement/measurement.json"}
    (output / "measurement.json").write_bytes(http.canonical(report))
    return report
