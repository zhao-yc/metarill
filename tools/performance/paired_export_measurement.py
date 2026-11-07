"""固定原三页导出同条件回放，下载原件留证、逐格核对置于计时之外。"""
import hashlib
from threading import Event

import paired_cgroup_probe as cgroups
import paired_export_contract as export
import paired_http as http
from paired_measurement import database_context
from server200_paired_resources import EXCEL_EXPORT_REPLAY_REQUESTS

ROUNDS = 3
ITERATIONS = 36
WARMUP = 8
CONCURRENCY = (1, 4)
DOWNLOAD_LIMIT = 24 * 1024
REPLAY_REQUESTS = 2 * len(CONCURRENCY) * (ROUNDS * ITERATIONS + WARMUP) * len(export.HEADERS) * 2


def run_measurement(root, run, targets, tokens, probe, query, parse_workbook):
    """先过实文件预检，再逐轮交替执行；只有全部物理文件验完才计算双端比值。"""
    if REPLAY_REQUESTS != EXCEL_EXPORT_REPLAY_REQUESTS:
        raise ValueError("measurement-export-replay-budget")
    scenario = {"name": "original-three-page-export", "steps": []}
    for kind in export.HEADERS:
        scenario["steps"].extend((export.ticket_step(kind), {"downloadKind": kind}))
    config = {"runId": run, "transportProof": "server200-loopback-v1", "targets": targets,
              "requestTimeoutSeconds": 15, "iterations": ITERATIONS, "maxP95Ratio": 1.10, "minThroughputRatio": 0.95}
    limits = http.equal_limits(probe, targets)
    transport = http.verify_transport(config, probe, limits)
    resource_probe = cgroups.CgroupProbe(probe, targets, limits)
    preflight = export.run_preflight(root, targets, tokens, parse_workbook, limit=DOWNLOAD_LIMIT, max_batches=1)
    output = root / "measurement"
    output.mkdir(mode=0o700)
    settings = database_context(query)
    samples, trials = [], []
    stop = Event()
    tickets = {side: set() for side in targets}

    def trial(side, concurrency, round_index):
        """计时只包含原票据请求及完整下载；留证、SHA 和工作簿解析均在窗口外。"""
        phase = "warmup" if round_index == -1 else "measured"
        count = WARMUP if phase == "warmup" else ITERATIONS
        target = {**targets[side], "headers": {"loginStateId": tokens[side, "writer"]}}
        captures = []

        def send(item, step, variables, timeout):
            """每次下载使用本迭代当前页票据，失败后停止签发新请求。"""
            if stop.is_set():
                raise ValueError("measurement-export-replay-stopped")
            try:
                if "downloadKind" not in step:
                    http.request(item, step, variables, timeout)
                    return "export-ticket-http200-r200"
                kind = step["downloadKind"]
                ticket = variables.pop("exportId")
                data = export.fetch_download(item["baseUrl"], ticket, limit=DOWNLOAD_LIMIT)
                captures.append((variables["iteration"], kind, ticket, data))
                return "export-workbook-downloaded-" + kind
            except Exception:
                stop.set()
                raise

        before = database_context(query)
        result = http.run_trial(config, target, scenario, concurrency, round_index, phase, count, send,
                                resource_probe if phase == "measured" else None)
        after = database_context(query)
        evidence = {"side": side, "concurrency": concurrency, "round": round_index, "phase": phase,
                    "before": before, "after": after, "timings": result, "downloads": []}
        for iteration, kind, ticket, data in sorted(captures, key=lambda capture: (capture[0], capture[1])):
            digest = hashlib.sha256(data).hexdigest()
            filename = digest + "." + export.FORMAT[side]
            file = output / filename
            if not file.exists():
                file.write_bytes(data)
            evidence["downloads"].append({"iteration": iteration, "kind": kind, "sha256": digest, "byteLength": len(data)})
            samples.append((side, kind, data))
        with (output / "events.jsonl").open("ab") as log:
            log.write(http.canonical(evidence) + b"\n")
        actual = {(iteration, kind) for iteration, kind, _, _ in captures}
        expected = {(iteration, kind) for iteration in range(count) for kind in export.HEADERS}
        current_tickets = {ticket for _, _, ticket, _ in captures}
        if result["failedScenarios"] or actual != expected or len(captures) != len(expected):
            raise ValueError("measurement-export-replay-incomplete")
        if len(current_tickets) != len(captures) or current_tickets & tickets[side]:
            raise ValueError("measurement-export-replay-ticket-reused")
        tickets[side].update(current_tickets)
        if before["variables"] != settings["variables"] or after["variables"] != settings["variables"]:
            raise ValueError("measurement-export-replay-database-drift")
        return result

    for concurrency in CONCURRENCY:
        for side in targets:
            trial(side, concurrency, -1)
        for round_index in range(ROUNDS):
            order = ("java", "csharp") if round_index % 2 == 0 else ("csharp", "java")
            trials.append((concurrency, round_index, order, {side: trial(side, concurrency, round_index) for side in order}))
    if len(samples) * 2 != REPLAY_REQUESTS:
        raise ValueError("measurement-export-replay-budget")
    proofs = export.verify_workbooks(samples, parse_workbook, max_batches=export.oracle.MAX_JOBS - 1)
    for (side, kind, _), proof in zip(samples, proofs, strict=True):
        if proof["semanticSha256"] != preflight["workbooks"][side][kind]["semanticSha256"]:
            raise ValueError("measurement-export-replay-semantics")
    (output / "workbook-proofs.json").write_bytes(http.canonical(proofs))
    after_limits = http.equal_limits(probe, targets)
    if limits != after_limits:
        raise ValueError("measurement-export-replay-resource-drift")
    http.verify_transport(config, probe, after_limits)
    resource_probe.verify()
    results = []
    for concurrency, round_index, order, trial_pair in trials:
        comparison = http.compare_pair(trial_pair["java"], trial_pair["csharp"], config)
        comparison["resourcesComplete"] = all(t["resourceSampleCount"] >= 2 and not t["resourceErrors"] for t in trial_pair.values())
        # 每页两步独立汇总，避免整场景数值掩盖票据生成与传输的差异。
        step_p95 = {}
        for side, measured in trial_pair.items():
            requests = [row["requestMs"] for row in measured["rows"]]
            step_p95[side] = {
                kind: {action: http.percentile([row[2 * i + step] for row in requests], 95)
                       for step, action in enumerate(("ticket", "download"))}
                for i, kind in enumerate(export.HEADERS)
            }
        results.append({"concurrency": concurrency, "round": round_index, "order": list(order),
                        "comparison": comparison, "stepP95Ms": step_p95})
    valid = all(r["comparison"]["valid"] and r["comparison"]["resourcesComplete"] for r in results)
    regression = valid and any(sum(r["comparison"].get("regression", False) for r in results if r["concurrency"] == level) >= 2
                               for level in CONCURRENCY)
    report = {"measurementValid": valid, "stableRegression": regression, "transport": transport, "limits": limits, "resourceSource": cgroups.METHOD,
              "preflightRequests": preflight["requests"], "replayRequests": REPLAY_REQUESTS, "workbooksVerified": len(proofs),
              "comparison": results, "report": "measurement/measurement.json"}
    (output / "measurement.json").write_bytes(http.canonical(report))
    return report
