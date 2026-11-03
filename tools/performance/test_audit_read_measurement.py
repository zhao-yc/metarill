"""离线复核固定只读正式报告，不能从汇总标志或迟到采样推断通过。"""
import copy
import hashlib
from pathlib import Path
import tempfile
import unittest

import audit_read_measurement as audit

RUN = "ab" * 16
FINGERPRINTS = [str(index) * 64 for index in range(1, 5)]


def trial(ms, elapsed, count=400):
    """生成可手算且摘要固定的逐场景和资源样本。"""
    rows = [{"iteration": index, "error": None, "failedStep": None, "attemptedRequests": 4,
             "fingerprints": FINGERPRINTS.copy(), "elapsedMs": ms, "requestMs": [ms / 5] * 4}
            for index in range(count)]
    resources = [{"elapsedSeconds": when, "values": {"cpuPercent": cpu, "memoryBytes": memory}}
                 for when, cpu, memory in ((1, 5, 100), (2, 7, 200), (50, 99, 9999))]
    return {"elapsedSeconds": elapsed, "rows": rows, "completedScenarios": count, "failedScenarios": 0,
            "p95Ms": ms, "scenarioThroughput": count / elapsed, "resources": resources, "resourceErrors": []}


def evidence():
    """固定两档各三轮，逐事件引用与报告相同的原始试验。"""
    context = {"variables": dict.fromkeys(("version", "bufferPoolBytes", "maxConnections", "transactionIsolation",
                                           "globalTimeZone", "globalSqlMode"), "fixture")}
    transport = {"verified": True, "mode": "server200-loopback-v1", "proxyBypassed": True}
    limits = {"java": {"id": "test-java"}, "csharp": {"id": "test-csharp"}}
    first = {"runId": RUN, "configSha256": "a" * 64,
             "responseFingerprints": {side: FINGERPRINTS.copy() for side in audit.SIDES}}
    report = {"runId": RUN, "configSha256": first["configSha256"], "version": 1,
              "transportEvidence": transport, "actualLimits": limits, "releaseAccepted": False,
              "databaseVariablesUnchanged": True, "databaseBefore": context, "databaseAfter": context, "results": []}
    events = [{"kind": "container-preflight", "transportEvidence": transport, "actualLimits": limits}]
    for concurrency in (1, 4):
        for side in audit.SIDES:
            events.append({"kind": "warmup", "concurrency": concurrency, "target": side, "trial": trial(3, 5, 8)})
        rounds = []
        for index in range(3):
            order = audit.SIDES if index % 2 == 0 else audit.SIDES[::-1]
            pair = {"round": index, "order": list(order), "trials": {
                "java": trial(10, 5),
                "csharp": trial(12 if concurrency == 4 and index < 2 else 10, 5),
            }}
            rounds.append(pair)
            for side in order:
                keys = {"concurrency": concurrency, "round": index, "target": side, "databaseContext": context}
                events.append({**keys, "kind": "trial-intent"})
                events.append({**keys, "kind": "trial", "trial": pair["trials"][side]})
        report["results"].append({"scenario": audit.SCENARIO, "concurrency": concurrency, "pairs": rounds})
    return report, first, events


class IndependentReadReviewTests(unittest.TestCase):
    def test_recounts_trials_and_ignores_post_window_stats(self):
        """足量有效样本可比较，窗口外高值不进入资源统计。"""
        result = audit.review(*evidence(), RUN)
        self.assertEqual((19200, 128, 8), (result["measuredRequests"], result["warmupRequests"], result["preflightRequests"]))
        self.assertEqual([False, True], [group["stableRegression"] for group in result["results"]])
        values = result["results"][0]["rounds"][0]["java"]
        self.assertEqual((2, 1, 6, 200), (values["windowSamples"], values["outsideSamples"],
                                        values["meanCpuPercent"], values["observedMaxMemoryBytes"]))
        self.assertFalse(result["releaseAccepted"])

    def test_report_cannot_rely_on_late_sample_or_wrong_arithmetic(self):
        """两个总样本不足以证明窗口，时延汇总必须来自原始行。"""
        report, first, events = evidence()
        target = report["results"][0]["pairs"][0]["trials"]["java"]
        target["resources"].pop(0)
        with self.assertRaisesRegex(ValueError, "窗口内资源样本不足"):
            audit.review(report, first, events, RUN)
        report, first, events = evidence()
        report["results"][0]["pairs"][0]["trials"]["java"]["p95Ms"] = 1
        with self.assertRaisesRegex(ValueError, "汇总与逐场景记录不符"):
            audit.review(report, first, events, RUN)

    def test_fingerprint_and_database_drift_and_event_loss_are_rejected(self):
        """即使返回数量正确，响应、共享设置及事件完整性仍须成立。"""
        report, first, events = evidence()
        report["results"][0]["pairs"][0]["trials"]["java"]["rows"][0]["fingerprints"][0] = "f" * 64
        with self.assertRaisesRegex(ValueError, "逐场景响应不一致"):
            audit.review(report, first, events, RUN)
        report, first, events = evidence()
        events[3]["databaseContext"] = {"variables": {"version": "changed"}}
        with self.assertRaisesRegex(ValueError, "期中数据库配置"):
            audit.review(report, first, events, RUN)
        report, first, events = evidence()
        with self.assertRaisesRegex(ValueError, "事件序列不完整"):
            audit.review(report, first, events[:-1], RUN)

    def test_journal_and_report_must_bind_same_trial(self):
        """保留两份独立写出的记录，单份合法值更换仍会被发现。"""
        report, first, events = evidence()
        item = next(event for event in events if event["kind"] == "trial")
        item["trial"] = copy.deepcopy(item["trial"])
        item["trial"]["p95Ms"] = 999
        with self.assertRaisesRegex(ValueError, "事件与正式报告不一致"):
            audit.review(report, first, events, RUN)


    def test_attempt_reads_only_manifest_bound_complete_results(self):
        """整套私有归档要经过物理证明、独立清理及原始测量摘要。"""
        report, first, events = evidence()
        state = {"runId": RUN, "leaseStatus": "ended", "acceptedScope": "paired-read-measurement-only",
                 "cleanupErrors": [], "releaseAccepted": False, "measurement": {"physicalFixtureUnchanged": True},
                 "measurementSeeds": {side: {"rows": audit.COUNTS, "physicalReadbackVerified": True,
                                             "fixedIndexesVerified": True} for side in audit.SIDES},
                 "resourceEvidence": report["actualLimits"], "transport": report["transportEvidence"]}
        absence = dict.fromkeys(("newSsh", "executionProcessStopped", "userAbsent", "networkAbsent",
                                 "ownershipAbsent", "sourceCredentialsAbsent", "dataDirectoriesAbsent"), True)
        absence.update(databasesAbsent=2, containersAbsent=9)
        materials = {"native-state.json": audit.canonical(state),
                     "measurement/measurement.json": audit.canonical(report),
                     "measurement/preflight.json": audit.canonical(first),
                     "measurement/events.jsonl": b"\n".join(map(audit.canonical, events))}
        with tempfile.TemporaryDirectory() as root:
            folder = Path(root)
            (folder / "evidence/measurement").mkdir(parents=True)
            for name, raw in materials.items():
                (folder / "evidence" / name).write_bytes(raw)
            (folder / "evidence-files.json").write_bytes(
                audit.canonical({name: hashlib.sha256(raw).hexdigest() for name, raw in materials.items()}))
            (folder / "independent-absence.json").write_bytes(audit.canonical(absence))
            self.assertEqual(19200, audit.review_attempt(folder, RUN)["measuredRequests"])
            (folder / "evidence/measurement/preflight.json").write_bytes(b"{}")
            with self.assertRaisesRegex(ValueError, "证据字节摘要不符"):
                audit.review_attempt(folder, RUN)
