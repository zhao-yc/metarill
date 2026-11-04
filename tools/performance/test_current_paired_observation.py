"""当前镜像 SQL 诊断的精确来源和原生采集编排回归。"""
import unittest
from unittest import mock

import paired_native_bootstrap as native
import paired_observed_capture as capture
import test_paired_observed_native as previous


class CurrentObservedContractTests(unittest.TestCase):
    def settings(self):
        """只构造离线合成范围，不授权任何真实外部操作。"""
        return {**previous.value(), "purpose": native.resources.CURRENT_OBSERVED_NATIVE,
                "images": dict(native.resources.CURRENT_MEASUREMENT_IMAGES)}

    def test_current_data_hash_and_images_are_bound_to_the_new_observation_purpose(self):
        """当前镜像与程序集摘要进入观察参数，历史摘要保持。"""
        resources = native.resources
        value = self.settings()
        descriptor = native.validate(value)
        self.assertEqual(resources.CURRENT_OBSERVED_RESOURCES, descriptor["purpose"])
        self.assertEqual(value["runId"], capture.authorize(value))
        self.assertFalse(resources.is_measurement(value))
        args = resources.observation_arguments(descriptor, "csharp")
        self.assertIn("--Metarill:Paired:SqlObservation:ExpectedDataSha256=" + resources.CURRENT_OBSERVATION_DATA_SHA256, args)
        self.assertIn("--Metarill:Paired:SqlObservation:Enabled=true", args)
        self.assertEqual(resources.OBSERVATION_IMAGES, resources.images_for(previous.value()))
        self.assertEqual(resources.OBSERVATION_DATA_SHA256, resources.observation_data_sha(previous.value()))
        measured = {**value, "purpose": resources.CURRENT_MEASUREMENT_NATIVE}
        self.assertFalse(resources.is_observed(measured))
        self.assertEqual(["--Metarill:Paired:SqlObservation:Enabled=false"], resources.observation_arguments(measured, "csharp"))

    def test_current_business_keeps_three_roles_and_rejects_existing_line_scope(self):
        """真实业务入口验证当前用途，不借已有明细的额外写入能力。"""
        value = {**self.settings(), "purpose": native.resources.CURRENT_OBSERVED_BUSINESS,
                 "leaseStatus": "running"}
        tokens = {side: {kind: (f"{index}" * 8 + "-" + f"{index}" * 4 + "-" + f"{index}" * 4 + "-" + f"{index}" * 4 + "-" + f"{index}" * 12
                               if side == "java" else f"{index}" * 64)
                         for index, kind in enumerate(native.seed.KINDS, 1)} for side in ("java", "csharp")}
        self.assertEqual(value["runId"], previous.business.authorize(value, tokens)[0])
        with self.assertRaises(ValueError):
            previous.business.authorize(value, tokens, existing_lines=True)

    def test_old_images_and_pending_scope_fail_before_source_or_external_io(self):
        """旧镜像和待执行范围不能借新入口接入观察器。"""
        for change in ({"images": native.resources.OBSERVATION_IMAGES},
                       {"images": {**native.resources.CURRENT_MEASUREMENT_IMAGES, "csharp": "sha256:bc768ec8fb3e85c82799937c4a92f8c17fa7865b06307864dd8778463a68f146"}},
                       {"authorizationStatus": "pending"}):
            with self.subTest(change=change), \
                 mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止读源")), \
                 self.assertRaises(ValueError):
                native.run({**self.settings(), **change})


class CurrentObservedFlowTests(unittest.TestCase):
    database = previous.ObservedFlowTests.database
    job = previous.ObservedFlowTests.job
    resources = previous.ObservedFlowTests.resources
    capture = previous.ObservedFlowTests.capture
    run_scope = previous.ObservedFlowTests.run_scope

    def setUp(self):
        """复用外部替身，运行当前用途的实际协调器。"""
        previous.ObservedFlowTests.setUp(self)
        self.value.update(purpose=native.resources.CURRENT_OBSERVED_NATIVE,
                          images=dict(native.resources.CURRENT_MEASUREMENT_IMAGES))

    def run_business(self, lease, tokens, request, query):
        """当前普通业务必须继续绑定原三角色及已完成的六初始化。"""
        self.events.append("business")
        self.assertEqual(native.resources.CURRENT_OBSERVED_BUSINESS, lease["purpose"])
        self.assertEqual(self.value["images"], lease["images"])
        self.assertEqual(set(native.seed.KINDS), set(tokens["java"]))
        self.assertEqual(6, self.jobs.call_count)
        return {"mockedOnly": True, "sqlSemanticAccepted": False}

    def test_stops_captures_and_cleans_without_issuing_performance_acceptance(self):
        """正常停止、采集和清理顺序复用原回归，诊断不能签发性能通过。"""
        result = self.run_scope()
        self.assertTrue(result["passed"])
        self.assertFalse(result["performanceAccepted"])
        self.assertFalse(result["releaseAccepted"])
        self.assertEqual("ended", result["leaseStatus"])
        for first, second in (("resources-wait-hosts", "business"), ("business", "resources-snapshot-hosts"),
                              ("resources-snapshot-hosts", "resources-stop-hosts"), ("resources-stop-hosts", "capture"),
                              ("capture", "resources-cleanup"), ("resources-cleanup", "database-cleanup")):
            self.assertLess(self.events.index(first), self.events.index(second))
        targets = self.limits.call_args.args[1]
        for side in ("java", "csharp"):
            self.assertEqual(self.value["images"][side], targets[side]["expectedImageId"])
        self.assertEqual(native.resources.CURRENT_OBSERVED_RESOURCES,
                         native.resources.manage.call_args_list[0].args[0]["purpose"])


if __name__ == "__main__":
    unittest.main()
