"""完整候选镜像 SQL 诊断的精确来源和原生采集编排回归。"""
import unittest
from unittest import mock

import paired_native_bootstrap as native
import paired_observed_capture as capture
import test_paired_observed_native as previous
import test_observed_paired_resources as material


class FullCandidateObservedContractTests(unittest.TestCase):
    def test_historical_current_observation_and_file_measurement_remain_distinct(self):
        """旧观察用途、当前文件计时、完整候选采集三者不能隐式互换镜像或范围。"""
        r=native.resources;old={**previous.value(),"purpose":r.CURRENT_OBSERVED_NATIVE,"images":dict(r.CURRENT_MEASUREMENT_IMAGES)}
        native.validate(old);self.assertEqual(r.CURRENT_OBSERVATION_DATA_SHA256,r.observation_data_sha(old));self.assertNotEqual(r.CURRENT_OBSERVATION_DATA_SHA256,r.FULL_OBSERVATION_DATA_SHA256)
        self.assertFalse(r.is_current_observed(self.settings()));self.assertFalse(r.is_file_measurement(self.settings()));self.assertFalse(r.is_measurement(self.settings()))
        for extra in [{'readReplayRequests':19336},{'writeReplayRequests':8624},{'fileReplayRequests':4834,'fileFixtureRequests':18},{'relationIndexProfile':r.RELATION_INDEX_PROFILE},{'workbookOperations':12,'maintenanceProfile':'original-auto-caches-without-join-columns-v1'}]:
            with self.subTest(extra=extra),self.assertRaises(ValueError):native.validate({**self.settings(),**extra})

    def settings(self):
        """只构造离线合成范围，不授权任何真实外部操作。"""
        return {**previous.value(), "purpose": native.resources.FULL_OBSERVED_NATIVE,
                "images": dict(native.resources.FULL_OBSERVED_IMAGES)}

    def test_current_data_hash_and_images_are_bound_to_the_new_observation_purpose(self):
        """完整候选镜像与程序集摘要进入观察参数，历史摘要保持。"""
        resources = native.resources
        value = self.settings()
        descriptor = native.validate(value)
        self.assertEqual(resources.FULL_OBSERVED_RESOURCES, descriptor["purpose"])
        self.assertEqual(value["runId"], capture.authorize(value))
        self.assertFalse(resources.is_measurement(value))
        args = resources.observation_arguments(descriptor, "csharp")
        self.assertIn("--Metarill:Paired:SqlObservation:ExpectedDataSha256=" + resources.FULL_OBSERVATION_DATA_SHA256, args)
        self.assertIn("--Metarill:Paired:SqlObservation:Enabled=true", args)
        self.assertEqual(resources.OBSERVATION_IMAGES, resources.images_for(previous.value()))
        self.assertEqual(resources.OBSERVATION_DATA_SHA256, resources.observation_data_sha(previous.value()))
        measured = {**value, "purpose": resources.CURRENT_MEASUREMENT_NATIVE,"images":dict(resources.CURRENT_MEASUREMENT_IMAGES)}
        self.assertFalse(resources.is_observed(measured))
        self.assertEqual(["--Metarill:Paired:SqlObservation:Enabled=false"], resources.observation_arguments(measured, "csharp"))

    def test_current_business_keeps_three_roles_and_rejects_existing_line_scope(self):
        """真实业务入口验证当前用途，不借已有明细的额外写入能力。"""
        value = {**self.settings(), "purpose": native.resources.FULL_OBSERVED_BUSINESS,
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
                       {"images": {**native.resources.FULL_OBSERVED_IMAGES, "csharp": "sha256:bc768ec8fb3e85c82799937c4a92f8c17fa7865b06307864dd8778463a68f146"}},
                       {"authorizationStatus": "pending"}):
            with self.subTest(change=change), \
                 mock.patch.object(native, "verify_tool_sources", side_effect=AssertionError("禁止读源")), \
                 self.assertRaises(ValueError):
                native.run({**self.settings(), **change})


class FullCandidateObservedFlowTests(unittest.TestCase):
    database = previous.ObservedFlowTests.database
    job = previous.ObservedFlowTests.job
    resources = previous.ObservedFlowTests.resources
    capture = previous.ObservedFlowTests.capture
    run_scope = previous.ObservedFlowTests.run_scope

    def setUp(self):
        """复用外部替身，运行当前用途的实际协调器。"""
        previous.ObservedFlowTests.setUp(self)
        self.value.update(purpose=native.resources.FULL_OBSERVED_NATIVE,
                          images=dict(native.resources.FULL_OBSERVED_IMAGES))

    def run_business(self, lease, tokens, request, query):
        """当前普通业务必须继续绑定原三角色及已完成的六初始化。"""
        self.events.append("business")
        self.assertEqual(native.resources.FULL_OBSERVED_BUSINESS, lease["purpose"])
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
        self.assertEqual(native.resources.FULL_OBSERVED_RESOURCES,
                         native.resources.manage.call_args_list[0].args[0]["purpose"])


class FullCandidateObservedMaterialTests(unittest.TestCase):
    materialize=material.ObservedResourceTests.materialize
    tearDown=material.ObservedResourceTests.tearDown
    refresh=material.ObservedResourceTests.refresh
    key=material.ObservedResourceTests.key

    def setUp(self):
        """实际读取假材料和构造命令，仅替换文件权限／Docker边界；不继承重复旧测试。"""
        material.ObservedResourceTests.setUp(self);self.value.update(purpose=native.resources.FULL_OBSERVED_RESOURCES,images=dict(native.resources.FULL_OBSERVED_IMAGES))

    def test_actual_materials_and_command_register_additional_deps_only_for_csharp(self):
        """真实四材料入口和实际容器参数均闭合，避免仅mock协调器漏掉加载白名单。"""
        r=native.resources;r.read_materials(self.root,self.value);r.manage(self.value,'create')
        commands=[c for c in self.engine.calls if c[:2]==('container','create')];self.assertEqual(3,len(commands))
        for side,command in zip(('redis','java','csharp'),commands):
            self.assertEqual(side=='csharp','DOTNET_ADDITIONAL_DEPS=/app/Metarill.Paired.SqlObservation.deps.json' in command)
            self.assertIn(r.FULL_OBSERVED_IMAGES[side],command)
            if side=='csharp':self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDataSha256='+r.FULL_OBSERVATION_DATA_SHA256,command)

    def test_missing_or_other_redis_target_rejected_by_actual_material_reader(self):
        """新用途仍只准同轮Redis口令／DB0；更改自报摘要不能绕过真实配置检查。"""
        r=native.resources;original=self.inputs['csharp.env'];before=len(self.engine.calls)
        for changed in [original.replace('defaultDatabase=0','defaultDatabase=1'),original.replace('redis:6379','outside:6379'),'\n'.join(line for line in original.splitlines() if not line.startswith('Authentication__LoginFailures'))+'\n']:
            self.inputs['csharp.env']=changed;self.materialize();self.refresh()
            with self.assertRaises(ValueError):r.read_materials(self.root,self.value)
        self.assertEqual(before,len(self.engine.calls))


if __name__ == "__main__":
    unittest.main()
