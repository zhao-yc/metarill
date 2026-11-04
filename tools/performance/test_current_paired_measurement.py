"""当前首版固定镜像的测量入口、历史用途和实际编排回归。"""
import unittest
from unittest import mock

import paired_native_bootstrap as native
import test_paired_native_bootstrap as previous
import test_paired_measurement as measurement_tests


class CurrentMeasurementTests(previous.FlowFixture):
    def setUp(self):
        """复用真实协调器的内存资源替身，禁止触及外部实例。"""
        super().setUp()
        self.value.update(purpose=native.resources.CURRENT_MEASUREMENT_NATIVE,
                          images=dict(native.resources.CURRENT_MEASUREMENT_IMAGES))

    def test_current_read_and_write_keep_precise_purpose_and_budget(self):
        """新镜像只进入明确的新用途，写入仍须完整的独立预算。"""
        resources = native.resources
        descriptor = native.validate(self.value)
        self.assertEqual(resources.CURRENT_MEASUREMENT_RESOURCES, descriptor["purpose"])
        self.assertEqual(resources.CURRENT_MEASUREMENT_IMAGES, resources.images_for(descriptor))
        self.assertFalse(resources.is_observed(descriptor))
        self.assertEqual(["--Metarill:Paired:SqlObservation:Enabled=false"],
                         resources.observation_arguments(descriptor, "csharp"))
        value = {**self.value, "purpose": resources.CURRENT_WRITE_MEASUREMENT_NATIVE,
                 "writeReplayRequests": resources.WRITE_REQUEST_BUDGET}
        self.assertEqual(resources.CURRENT_WRITE_MEASUREMENT_RESOURCES, native.validate(value)["purpose"])
        for budget in (None, True, resources.WRITE_REQUEST_BUDGET + 1):
            with self.subTest(budget=budget), self.assertRaises(ValueError):
                native.validate({**value, "writeReplayRequests": budget})

    def test_historical_and_current_images_cannot_be_interchanged(self):
        """历史用途保留原摘要，新用途拒绝旧包及任意自选镜像。"""
        resources = native.resources
        legacy = {**self.value, "purpose": resources.MEASUREMENT_NATIVE,
                  "images": dict(resources.EXISTING_LINE_IMAGES)}
        self.assertEqual(resources.MEASUREMENT_RESOURCES, native.validate(legacy)["purpose"])
        self.assertEqual(resources.EXISTING_LINE_IMAGES, resources.images_for(legacy))
        changes = ((self.value, resources.EXISTING_LINE_IMAGES),
                   (legacy, resources.CURRENT_MEASUREMENT_IMAGES),
                   (self.value, {**self.value["images"], "csharp": "sha256:" + "a" * 64}))
        for value, images in changes:
            with self.subTest(purpose=value["purpose"]), \
                 mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止读取来源")), \
                 self.assertRaises(ValueError):
                native.run({**value, "images": images})

    def test_current_native_seed_checks_ownership_and_rejects_resource_descriptors(self):
        """装载真实种子的入口仅接受原生用途，资源用途不能借用装载能力。"""
        mysql, fixture = mock.Mock(), mock.Mock()
        mysql.mysql.return_value = "1\n1"
        fixture.verify_readback.return_value = {"verified": True}
        with mock.patch.object(native, "require_database_ownership") as ownership:
            self.assertEqual({"verified": True}, native.seed_measurement_database(
                mysql, self.value, "owner-token", fixture, "csharp"))
            ownership.assert_called_once_with(mysql, self.value, "owner-token")
            mysql.mysql.assert_called_once_with(fixture.seed_transaction_sql.return_value,
                                                guarded_transaction=True)
            fixture.verify_readback.assert_called_once_with(self.value["runId"], "csharp", mysql.mysql)
            with self.assertRaises(ValueError):
                native.seed_measurement_database(mysql,
                    {**self.value, "purpose": native.resources.CURRENT_MEASUREMENT_RESOURCES},
                    "owner-token", fixture, "csharp")
            ownership.assert_called_once()

    def test_current_seed_replay_readback_and_cleanup_use_the_actual_coordinator(self):
        """当前新用途也必须装载种子、回放、前后核验并执行清理。"""
        measurement_tests.MeasurementFlowTests.test_measurement_reads_back_before_and_after_and_cleans(self)


if __name__ == "__main__":
    unittest.main()
