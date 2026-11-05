"""固定原维护接口的同条件计时，写回与无变化回放分别判定。"""
from threading import Event

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
