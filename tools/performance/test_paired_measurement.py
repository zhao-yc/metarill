"""只读性能专用用途和回放证据离线回归。"""
import contextlib
import io
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import paired_measurement as measurement
import paired_measurement_fixture as fixture
import paired_native_bootstrap as native
import paired_write_measurement as write
import test_paired_native_bootstrap as previous


RUN="aa"*16
TARGETS={side:{"container":"metarill-perf-"+RUN+"-"+side,"expectedImageId":native.resources.EXISTING_LINE_IMAGES[side],
              "containerPort":8080,"baseUrl":f"http://127.0.0.1:{port}/api"} for side,port in (("java",28191),("csharp",28192))}
TOKENS={(side,"reader"):"PRIVATE-TEST-TOKEN" for side in TARGETS}


class MeasurementReplayTests(unittest.TestCase):
    def test_fixed_current_read_routes_and_warmup_bounds(self):
        """只用原读入口，不忽略响应字段或使用超级账号。"""
        config=measurement.config_for(RUN,TARGETS,TOKENS)
        self.assertEqual((3,400,[1,4],8),(config["rounds"],config["iterations"],config["concurrency"],config["warmupIterations"]))
        steps=config["scenarios"][0]["steps"]
        self.assertEqual(["page","count",str(fixture.ID_BASE+10001),"page"],[step["path"].rsplit("/",1)[1] for step in steps])
        self.assertTrue(all("ignorePointers" not in step and "capture" not in step for step in steps))
        self.assertEqual(str(fixture.ID_BASE+10001),steps[3]["json"]["parentId"])

    def test_preflight_response_difference_never_calls_measurement(self):
        """一处业务响应不一致时停止，私有证据保留摘要而非正文或令牌。"""
        with tempfile.TemporaryDirectory() as folder,mock.patch.object(measurement.http,"request",side_effect=["a"]*4+["a","b","a","a"]),mock.patch.object(measurement.http,"run") as run:
            with self.assertRaisesRegex(ValueError,"measurement-preflight-response-mismatch"):
                measurement.run_read_measurement(Path(folder),RUN,TARGETS,TOKENS,None,mock.Mock())
            run.assert_not_called()
            record=(Path(folder)/"measurement/preflight.json").read_text()
            self.assertNotIn("PRIVATE",record)
            self.assertEqual("b",json.loads(record)["responseFingerprints"]["csharp"][1])

    def test_temporary_database_setting_change_invalidates_full_measurement(self):
        """期中共享配置变化即使结束恢复也不出具有效成绩。"""
        normal={"variables":{"bufferPoolBytes":"128"},"globalCounters":{},"loadAverage":[]}
        changed={**normal,"variables":{"bufferPoolBytes":"256"}}
        def measured(config,probe,on_event):
            on_event({"kind":"trial-intent"})
            on_event({"kind":"trial"})
            return {"results":[{"measurementValid":True,"stableRegression":False}]*2}
        with tempfile.TemporaryDirectory() as folder,mock.patch.object(measurement.http,"request",return_value="a"),mock.patch.object(measurement.http,"run",side_effect=measured),mock.patch.object(measurement,"database_context",side_effect=[normal,changed,normal,normal]):
            result=measurement.run_read_measurement(Path(folder),RUN,TARGETS,TOKENS,None,mock.Mock())
            self.assertFalse(result["measurementValid"])
            self.assertFalse(result["stableRegression"])
            self.assertNotIn("PRIVATE",(Path(folder)/"measurement/measurement.json").read_text())


class MeasurementFlowTests(previous.FlowFixture):
    def setUp(self):
        super().setUp()
        self.value.update(purpose=native.resources.MEASUREMENT_NATIVE,images=native.resources.EXISTING_LINE_IMAGES)

    def test_profile_keeps_current_images_and_turns_off_capture(self):
        """旧用例仍依原固定镜像，本用途不能借旧授权。"""
        descriptor=native.validate(self.value)
        self.assertEqual(native.resources.MEASUREMENT_RESOURCES,descriptor["purpose"])
        self.assertFalse(native.resources.is_observed(descriptor))
        self.assertEqual(["--metarill.paired.sql-observation.enabled=false"],native.resources.observation_arguments(descriptor,"java"))
        self.assertEqual(["--Metarill:Paired:SqlObservation:Enabled=false"],native.resources.observation_arguments(descriptor,"csharp"))
        for change in ({"authorizationStatus":"pending"},{"images":native.resources.OBSERVATION_IMAGES}):
            with mock.patch.object(native.importlib,"import_module") as importing,self.assertRaises(ValueError):
                native.run({**self.value,**change})
            importing.assert_not_called()

    def test_measurement_reads_back_before_and_after_and_cleans(self):
        """只有专用模式运行种子和计时，报告仍不宣称完整性能验收。"""
        def seeded(mysql,value,owner,model,target):
            self.events.append("seed-"+target)
            return {"target":target}
        def measured(*args):
            self.events.append("measure")
            return {"measurementValid":True,"stableRegression":False}
        with mock.patch.object(native,"seed_measurement_database",side_effect=seeded),mock.patch.object(measurement,"run_read_measurement",side_effect=measured),mock.patch.object(fixture,"verify_readback",side_effect=lambda run,side,query:{"target":side}),contextlib.redirect_stdout(io.StringIO()):
            result=native.run(self.value)
        self.assertTrue(result["passed"])
        self.assertFalse(result["performanceAccepted"])
        self.assertTrue(result["measurement"]["physicalFixtureUnchanged"])
        self.assertLess(self.events.index("seed-csharp"),self.events.index("resources-start-hosts"))
        self.assertLess(self.events.index("measure"),self.events.index("database-cleanup"))


class WriteMeasurementFlowTests(previous.FlowFixture):
    def setUp(self):
        self.original_verify=native.verify_tool_sources
        super().setUp()
        self.value.update(purpose=native.resources.WRITE_MEASUREMENT_NATIVE,
                          images=native.resources.EXISTING_LINE_IMAGES,writeReplayRequests=8624)

    def test_pending_or_wrong_budget_never_reaches_external_boundaries(self):
        """用途和精确预算缺一不可，原只读用途不能从布尔或数量隐式升级。"""
        for change in ({"authorizationStatus":"pending"},{"writeReplayRequests":8625},
                       {"writeReplayRequests":True},{"writeReplayRequests":None}):
            with mock.patch.object(native.importlib,"import_module") as importing,self.assertRaises(ValueError):
                native.run({**self.value,**change})
            importing.assert_not_called()
        self.assertEqual([],self.events)
        descriptor=native.validate(self.value)
        self.assertEqual(native.resources.WRITE_MEASUREMENT_RESOURCES,descriptor["purpose"])
        self.assertFalse(native.resources.is_observed(descriptor))

    def test_only_write_purpose_requires_the_ninth_fixed_file(self):
        """只读仍核对八文件，写入必须核对实际第九文件，不能漏报。"""
        base=Path(native.__file__).resolve().parents[1]
        names=("performance/paired_native_bootstrap.py","performance/paired_http.py","performance/paired_identity_seed.py",
               "performance/server200_paired_resources.py","acceptance/server200-fresh-mysql.py",
               "performance/paired_business_http.py","performance/paired_measurement_fixture.py","performance/paired_measurement.py")
        checksums={name:hashlib.sha256((base/name).read_bytes()).hexdigest() for name in names}
        value={**self.value,"toolSourceCommit":"a"*40,"toolSha256":checksums}
        # FlowFixture 替换了执行前来源验证，单独调用原函数验证实际清单。
        verify=self.original_verify
        with self.assertRaises(ValueError):
            verify(value)
        verify({**value,"purpose":native.resources.MEASUREMENT_NATIVE})
        name="performance/paired_write_measurement.py"
        checksums[name]=hashlib.sha256((base/name).read_bytes()).hexdigest()
        verify(value)

    def test_success_requires_pre_post_tombstones_and_seed_readbacks(self):
        """写入分支在归属和空样本之后，清理之前独立核验两个侧。"""
        def seeded(mysql,value,owner,model,side):
            return {"target":side}

        def samples(run,data,*,before=False):
            self.events.append("before-sample" if before else "after-sample")
            return {"verifiedRows":0 if before else 616}

        def measured(*args):
            self.events.append("write")
            return {"measurementValid":True,"stableRegression":False}

        def preserved(run,side,query,*,exclude_write_samples=False):
            self.assertTrue(exclude_write_samples)
            self.events.append("preserved-"+side)
            return {"target":side}

        with mock.patch.object(native,"seed_measurement_database",side_effect=seeded), \
             mock.patch.object(native,"require_database_ownership") as ownership, \
             mock.patch.object(write,"run_write_measurement",side_effect=measured), \
             mock.patch.object(write,"verify_readback",side_effect=samples), \
             mock.patch.object(fixture,"verify_readback",side_effect=preserved), \
             contextlib.redirect_stdout(io.StringIO()):
            result=native.run(self.value)
        ownership.assert_called_once()
        self.assertTrue(result["passed"])
        self.assertFalse(result["performanceAccepted"])
        self.assertEqual({"java":{"verifiedRows":616},"csharp":{"verifiedRows":616}},result["measurement"]["writeReadback"])
        ordered=[name for name in self.events if name in {"before-sample","write","after-sample","preserved-java","preserved-csharp","resources-cleanup","database-cleanup"}]
        self.assertEqual(["before-sample","before-sample","write","after-sample","after-sample",
                          "preserved-java","preserved-csharp","resources-cleanup","database-cleanup"],ordered)

    def test_ownership_failure_still_cleans_without_replay(self):
        """未证明真实双库归属时不准发任何写入回放请求。"""
        with mock.patch.object(native,"seed_measurement_database",return_value={}), \
             mock.patch.object(native,"require_database_ownership",side_effect=ValueError("business-fixture-ownership")), \
             mock.patch.object(write,"run_write_measurement") as measured,contextlib.redirect_stdout(io.StringIO()):
            result=native.run(self.value)
        measured.assert_not_called()
        self.assertFalse(result["passed"])
        self.assertEqual([],result["cleanupErrors"])
        self.assertLess(self.events.index("resources-cleanup"),self.events.index("database-cleanup"))


if __name__=="__main__":
    unittest.main()
