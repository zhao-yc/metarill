"""原六初始化与两端显式同索引升级的离线回归；不执行真实SQL、容器或性能负载。"""
import contextlib
import copy
import io
import json
import subprocess
import unittest
from unittest import mock

import paired_native_bootstrap as native
import paired_measurement_fixture as fixture
import paired_write_measurement as write
import test_paired_native_bootstrap as previous
import test_paired_measurement_fixture as fixtures


def indexed():
    """虚构范围仅用于离线验证，不代表任何远端授权。"""
    value=previous.settings();value.update(purpose=native.resources.INDEXED_WRITE_MEASUREMENT_NATIVE,images=dict(native.resources.INDEXED_WRITE_IMAGES),relationIndexProfile=native.resources.RELATION_INDEX_PROFILE,writeReplayRequests=8624);return value


def receipt(value,side,mode):
    """模拟固定工具的结构化回执，故意漂移时由真实校验器拒绝。"""
    return {"format":native.resources.RELATION_INDEX_PROFILE,"runId":value["runId"],"target":side,"operation":mode,"database":"metarill_fresh_"+value["runId"][:16]+"_paired_"+side,"planSha256":("e" if mode!="verify" else "f")*64,"changes":copy.deepcopy([] if mode=="verify" else native.INDEX_CHANGES),"succeeded":True,"appliedStatements":2 if mode=="apply" else 0}


class IndexedContractTests(unittest.TestCase):
    def test_precise_purpose_images_profile_and_budget_cannot_upgrade_legacy(self):
        """拒绝发生在读取来源、配置和外部调用之前，历史用途仍要求原内容ID。"""
        value=indexed();descriptor=native.validate(value)
        self.assertEqual(native.resources.INDEXED_WRITE_MEASUREMENT_RESOURCES,descriptor["purpose"]);self.assertEqual(native.resources.INDEXED_WRITE_IMAGES,native.resources.images_for(descriptor));self.assertFalse(native.resources.is_observed(descriptor));self.assertEqual(["--Metarill:Paired:SqlObservation:Enabled=false"],native.resources.observation_arguments(descriptor,"csharp"))
        for change in ({"authorizationStatus":"pending"},{"relationIndexProfile":None},{"relationIndexProfile":"other"},{"writeReplayRequests":True},{"writeReplayRequests":8625},{"images":native.resources.CURRENT_MEASUREMENT_IMAGES},{"images":native.resources.EXISTING_LINE_IMAGES}):
            with self.subTest(change=change),mock.patch.object(native,"verify_tool_sources",side_effect=AssertionError("禁止IO")),self.assertRaises(ValueError):native.run({**value,**change})
        old={**value,"purpose":native.resources.CURRENT_WRITE_MEASUREMENT_NATIVE,"images":native.resources.CURRENT_MEASUREMENT_IMAGES}
        with self.assertRaises(ValueError):native.validate(old)
        old.pop("relationIndexProfile");self.assertEqual(native.resources.CURRENT_WRITE_MEASUREMENT_RESOURCES,native.validate(old)["purpose"])

    def test_receipt_rejects_wrong_sql_partial_apply_foreign_scope_and_confirmation(self):
        """完整计划配对、真实执行条数及零差异验证缺一不可。"""
        value=indexed();before=receipt(value,"java","preview");applied=receipt(value,"java","apply");native.require_index_receipt(value,"java","preview",before);native.require_index_receipt(value,"java","apply",applied,before);native.require_index_receipt(value,"java","verify",receipt(value,"java","verify"))
        for changed in ({"database":"production"},{"target":"csharp"},{"runId":"b"*32},{"appliedStatements":1},{"appliedStatements":True},{"succeeded":False},{"changes":[]},{"planSha256":"f"*64}):
            with self.subTest(changed=changed),self.assertRaises(ValueError):native.require_index_receipt(value,"java","apply",{**applied,**changed},before)
        bad=copy.deepcopy(applied);bad["changes"][0]["sql"]+="; DROP TABLE foreign"
        with self.assertRaises(ValueError):native.require_index_receipt(value,"java","apply",bad,before)
        with self.assertRaises(ValueError):native.require_index_receipt(value,"java","apply",applied)
        with self.assertRaises(ValueError):native.require_index_receipt(value,"java","verify",{**receipt(value,"java","verify"),"changes":native.INDEX_CHANGES})
        with self.assertRaises(ValueError):native.require_index_receipt(value,"java","drop",{**before,"operation":"drop"})

    def test_indexed_layout_is_strict_and_old_layout_contract_is_unchanged(self):
        """两端新索引按列序逐属性核对，旧用途不能因这个扩展接受额外索引。"""
        old=fixtures.response_gateway("java")(fixture.index_sql(fixtures.RUN,"java"));extra=[]
        for table,columns in fixture.RELATION_INDEX_COLUMNS.items():
            for n,column in enumerate(columns,1):extra.append("\t".join([table,"relation_lookup","1",str(n),column,"A","","BTREE","YES"]))
        new=old+"\n"+"\n".join(extra);fixture.verify_indexes(old);fixture.verify_indexes(new,relation_indexes=True)
        with self.assertRaises(ValueError):fixture.verify_indexes(new)
        with self.assertRaises(ValueError):fixture.verify_indexes(old,relation_indexes=True)
        for text in [new.rsplit("\n",1)[0],new.replace("relation_lookup\t1","relation_lookup\t0"),new.replace("sample_customer_id\tA","sample_contract_id\tA")]:
            with self.assertRaises(ValueError):fixture.verify_indexes(text,relation_indexes=True)


class IndexedCommandTests(unittest.TestCase):
    def setUp(self):
        """复用内存Docker引擎，但使用真实初始化命令构造与回执解析。"""
        previous.JobTests.setUp(self);self.value=indexed()
    def docker(self,*args):
        """实际镜像校验使用新固定内容ID，其余归属行为保持原引擎。"""
        if args[:2]==("container","inspect") and args[args.index("--format")+1]=="{{.Image}}":return native.resources.INDEXED_WRITE_IMAGES["csharp"]
        return previous.JobTests.docker(self,*args)
    def test_index_job_uses_same_host_closure_and_cleans_without_sql_or_password_argv(self):
        """仅此新用途可运行固定索引入口，配置仍从原私有env-file读取。"""
        self.capture.return_value=subprocess.CompletedProcess([],0,json.dumps(receipt(self.value,"java","preview")).encode(),b'')
        out=native.initialization_job(self.root,self.value,"csharp-index-java-preview",self.state,self.path);self.assertEqual("java",out["target"]);self.assertFalse(self.engine.objects)
        command=next(c for c in self.engine.calls if c[:2]==("container","create"));self.assertEqual("preview-java",command[-1]);self.assertIn("/app/Application.Host.deps.json",command);self.assertIn("/app/Metarill.Paired.Indexes.dll",command);self.assertIn("METARILL_INDEX_AUTHORIZATION=approved",command);self.assertNotIn("ALTER TABLE"," ".join(command));self.assertNotIn("Password="," ".join(command));self.assertTrue(self.state["jobs"][0]["containerAbsent"])
        with self.assertRaises(ValueError):native.initialization_job(self.root,self.value,"csharp-index-java-apply",self.state,self.path)
        with self.assertRaises(ValueError):native.initialization_job(self.root,previous.settings(),"csharp-index-csharp-preview",self.state,self.path)


class IndexedFlowTests(previous.FlowFixture):
    def setUp(self):
        """所有外部边界替换，实际运行协调器验证完整顺序，不宣称实库通过。"""
        super().setUp();self.value=indexed();self.jobs.side_effect=self.indexed_job
    def indexed_job(self,root,value,name,state,path,*,expected_index_plan=None):
        """原六动作先登记完成，索引动作返回各自明确的预览/应用/核验回执。"""
        if name in native.JOBS:
            state["jobs"].append({"kind":"container","target":"init-"+name,"name":"metarill-perf-"+value["runId"]+"-init-"+name,"id":"e"*64,"containerAbsent":True});return super().job(root,value,name,state,path)
        self.events.append(name);state["jobs"].append({"kind":"container","target":"init-"+name,"name":"metarill-perf-"+value["runId"]+"-init-"+name,"id":"e"*64,"containerAbsent":True});side,mode=name.split("-")[2:]
        if mode=="apply":self.assertEqual("e"*64,expected_index_plan)
        return receipt(value,side,mode)
    def test_actual_coordinator_upgrades_both_before_seed_and_keeps_original_write_budget(self):
        """六原动作、六索引动作、种子、原写入回放和独立回读严格排序，最后按原归属清理。"""
        def seeded(mysql,value,owner,model,side):self.events.append("seed-"+side);return {"target":side}
        def measured(*args):self.events.append("write");return {"measurementValid":True,"stableRegression":False}
        def preserved(run,side,query,*,exclude_write_samples=False,relation_indexes=False):self.assertTrue(exclude_write_samples and relation_indexes);return {"target":side}
        with mock.patch.object(native,"seed_measurement_database",side_effect=seeded),mock.patch.object(native,"require_database_ownership"),mock.patch.object(write,"run_write_measurement",side_effect=measured),mock.patch.object(write,"verify_readback",return_value={"verifiedRows":616}),mock.patch.object(fixture,"verify_readback",side_effect=preserved),contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result["passed"]);self.assertFalse(result["performanceAccepted"]);self.assertEqual(12,self.jobs.call_count)
        ordered=[event for event in self.events if event in native.JOBS+native.INDEX_JOBS];self.assertEqual(list(native.JOBS+native.INDEX_JOBS),ordered)
        self.assertLess(self.events.index(native.INDEX_JOBS[-1]),self.events.index("seed-java"));self.assertLess(self.events.index("write"),self.events.index("database-cleanup"));self.assertEqual({"java","csharp"},set(result["relationIndexPlans"]))
    def test_missing_original_initialization_or_failed_index_confirmation_never_replays(self):
        """缺原六初始化、或索引应用确认不一致必须停止，保留首因并清理本轮资源。"""
        with self.assertRaises(ValueError):native.upgrade_relation_indexes(self.root,self.value,{"jobs":[]},self.root/"unused.json",self.mysql)
        previous_job=self.jobs.side_effect
        def wrong(*args,**kwargs):
            result=previous_job(*args,**kwargs)
            if args[2]=="csharp-index-java-apply":result["appliedStatements"]=1
            return result
        self.jobs.side_effect=wrong
        with mock.patch.object(native,"require_database_ownership"),mock.patch.object(native,"seed_measurement_database") as seeded,mock.patch.object(write,"run_write_measurement") as replay,contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        seeded.assert_not_called();replay.assert_not_called();self.assertFalse(result["passed"]);self.assertEqual([],result["cleanupErrors"])


if __name__=="__main__":unittest.main()
