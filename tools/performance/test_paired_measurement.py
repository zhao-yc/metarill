"""只读性能专用用途和回放证据离线回归。"""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import paired_measurement as measurement
import paired_measurement_fixture as fixture
import paired_native_bootstrap as native
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


if __name__=="__main__":
    unittest.main()
