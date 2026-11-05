"""锁定维护计时的请求范围、状态复核和稳定退化判定。"""
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import Mock, patch

import paired_maintenance_measurement as replay
import paired_native_bootstrap as native
from test_paired_maintenance_fixture import RUN, response


class MaintenanceMeasurementTests(unittest.TestCase):
    def test_measurement_scope_requires_its_own_exact_budget(self):
        r = native.resources
        value = dict(version=1, purpose=r.MAINTENANCE_MEASUREMENT_NATIVE,
                     authorizationStatus="approved", leaseStatus="not-started", runId=RUN,
                     images=r.MAINTENANCE_PREFLIGHT_IMAGES, ports={"java": 28367, "csharp": 28368},
                     materialsSha256="a"*64, bootstrapSha256="b"*64, identitySha256="c"*64,
                     databases=[replay.fixture.fixture.database_name(RUN, side) for side in ("java", "csharp")],
                     user="mtf_"+RUN[:16], instancePin=native.PIN,
                     maintenancePreflightRequests=8, maintenanceCatalogSha256=r.MAINTENANCE_CATALOG_SHA256,
                     maintenanceReplayRequests=replay.REPLAY_REQUESTS, relationIndexProfile=r.RELATION_INDEX_PROFILE)
        self.assertEqual(r.MAINTENANCE_MEASUREMENT_RESOURCES, native.validate(value)["purpose"])
        for images in (r.MAINTENANCE_INDEXED_IMAGES, r.MAINTENANCE_BATCH_IMAGES):
            self.assertEqual(images, native.validate({**value, "images": images})["images"])
        for changed in (dict(maintenanceReplayRequests=None), dict(maintenanceReplayRequests=207),
                        dict(purpose=r.MAINTENANCE_PREFLIGHT_NATIVE), dict(writeReplayRequests=208),
                        dict(authorizationStatus="pending")):
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                native.validate({**value, **changed})

    def test_stability_requires_two_regressions_in_one_complete_group(self):
        rows = [{"kind": kind, "profile": profile, "round": round_index,
                 "comparison": {"valid": True, "regression": round_index == 0}}
                for kind in replay.fixture.JOINS for profile, _, _ in replay.PROFILES
                for round_index in range(replay.ROUNDS)]
        self.assertEqual((True, False), replay.summarize(rows))
        rows[1]["comparison"]["regression"] = True
        self.assertEqual((True, True), replay.summarize(rows))
        self.assertEqual((False, False), replay.summarize(rows[:-1]))

    def test_http_replay_fixes_each_pair_and_records_full_window_evidence(self):
        targets = {side: {"container": side} for side in ("java", "csharp")}
        dirty = {}
        requests = []
        kinds = {join.table_id: kind for kind, join in replay.fixture.JOINS.items()}
        def damage(_run, side, kind):
            self.assertFalse(dirty.get((side, kind), False))
            dirty[side, kind] = True
            return "guarded"
        def verify(_run, side, kind, _query, *, stale):
            self.assertEqual(stale, dirty.get((side, kind), False))
        def request(side, path, body, timeout):
            self.assertEqual((replay.fixture.PATH, 30), (path, timeout))
            kind = kinds[body["configTableId"]]
            changed = replay.fixture.SOURCE_COUNT if dirty.pop((side, kind), False) else 0
            requests.append((side, kind, changed))
            return response(side, kind, changed)
        with TemporaryDirectory() as tmp, \
             patch.object(replay.http, "equal_limits", return_value={}), \
             patch.object(replay.http, "verify_transport", return_value={"verified": True}), \
             patch.object(replay, "database_context", return_value={"variables": []}), \
             patch.object(replay.cgroups, "CgroupProbe") as probe, \
             patch.object(replay.fixture, "damage_sql", side_effect=damage), \
             patch.object(replay.fixture, "verify_cache", side_effect=verify):
            probe.return_value.snapshot.side_effect = (
                {"cpuUsageUsec": i, "observedAtNs": i} for i in range(96))
            root = Path(tmp)
            report = replay.run_measurement(root, RUN, targets, Mock(), Mock(),
                                            Mock(return_value="1\n1"), request)
            journal = [json.loads(line) for line in (root / "measurement/events.jsonl").read_bytes().splitlines()]
        self.assertTrue(report["measurementValid"])
        self.assertEqual(208, len(requests))
        self.assertEqual(16, sum(changed > 0 for _, _, changed in requests))
        self.assertEqual(48, len(journal))
        self.assertEqual(18, len(report["comparison"]))
        self.assertTrue(all(row["resourceAfter"]["observedAtNs"] > row["resourceBefore"]["observedAtNs"]
                            for row in journal))


if __name__ == "__main__":
    unittest.main()
