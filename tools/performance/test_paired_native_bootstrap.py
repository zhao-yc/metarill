"""完整双端协调器的离线编排与实际回环HTTP传输合同；不连接Server-200或MySQL。"""
import base64
import contextlib
import copy
import hashlib
import http.server
import io
import json
import os
import pathlib
import subprocess
import tempfile
import threading
import unittest
from unittest import mock

import paired_native_bootstrap as subject
from test_server200_paired_resources import Engine


def settings():
    """仅用于离线测试的虚构批准描述，不是任何外部对象授权。"""
    run="a"*32
    return {"version":1,"purpose":"paired-native-v1","authorizationStatus":"approved","leaseStatus":"not-started","runId":run,
            "images":dict(subject.resources.IMAGES),"ports":{"java":28071,"csharp":28072},"materialsSha256":"b"*64,"bootstrapSha256":"c"*64,"identitySha256":"d"*64,
            "databases":["metarill_fresh_"+run[:16]+"_paired_"+t for t in ("java","csharp")],"user":"mtf_"+run[:16],"instancePin":dict(subject.PIN)}


def credentials():
    """所有口令/盐均为测试假值，不用于外部账号。"""
    return {k:{"password":str(i+1)*64,"javaSalt":str(i+4)*32,"csharpSalt":base64.b64encode(bytes([i+1])*24).decode()} for i,k in enumerate(subject.seed.KINDS)}


class ContractTests(unittest.TestCase):
    """前置批准、原命令结果与来源约束。"""
    def test_pending_ended_wrong_scope_and_old_image_fail_before_any_io(self):
        """实际入口未批准或来源范围不符，不查源文件、不读秘密、不运行外部程序。"""
        for change in ({"authorizationStatus":"pending"},{"leaseStatus":"ended"},{"purpose":"agent"},{"databases":["metarill_dev"]},{"user":"root"},{"instancePin":{"hostname":"other"}},{"images":{**subject.resources.IMAGES,"csharp":"old:latest"}}):
            value={**settings(),**change}
            with self.subTest(change=change),mock.patch.object(subject,"verify_tool_sources",side_effect=AssertionError("不应读源")),mock.patch.object(subject.resources,"root_for",side_effect=AssertionError("不应读配置")),self.assertRaises(ValueError):subject.run(value)

    def test_retired_csharp_image_fails_before_files_or_external_commands(self):
        """旧版合法内容ID也必须拒绝，不能把原待批方案沿用为当前产物的实库验收。"""
        value=settings()
        value["images"]["csharp"]="sha256:49649d47669f6b71db535f0221bd81855ae6792fe0325b341218b214662fc02c"
        with mock.patch.object(subject,"verify_tool_sources",side_effect=AssertionError("不应读取旧工具")),mock.patch.object(subject.resources,"root_for",side_effect=AssertionError("不应读取旧材料")),mock.patch.object(subject.subprocess,"run",side_effect=AssertionError("不应启动外部程序")),self.assertRaises(ValueError):
            subject.run(value)

    def test_actual_cli_pending_calls_no_external_command(self):
        """CLI拒绝发生在程序前置，而不是测试跳过真实执行入口。"""
        value=settings();value["authorizationStatus"]="pending"
        with mock.patch.object(subject.sys,"argv",["native"]),mock.patch.object(subject.sys,"stdin",io.StringIO(json.dumps(value))),mock.patch.object(subject.resources,"root_for",side_effect=AssertionError("禁止文件")),mock.patch.object(subject.subprocess,"run",side_effect=AssertionError("禁止外部")),contextlib.redirect_stderr(io.StringIO()):self.assertEqual(subject.main(),1)

    def test_original_json_outputs_are_parsed_without_guessing_log_words(self):
        """Java前缀和C#多行根对象均读取结构化字段，中文不依赖系统默认编码。"""
        expected={"changes":[],"message":"核验"}
        self.assertEqual(subject.parse_job_output(("原日志\n结果："+json.dumps(expected)+"\n").encode(),"java"),expected)
        self.assertEqual(subject.parse_job_output(("info: 进入\n"+json.dumps(expected,ensure_ascii=False,indent=2)+"\n").encode(),"csharp"),expected)
        for raw,target in ((b'not-json','csharp'),(b'{"changes":[],"changes":[1]}','csharp'),(b'{"n":NaN}','csharp'),(b'{"a":1}\n{"b":2}','csharp'),('结果：{}\n结果：{}'.encode(),'java')):
            with self.subTest(raw=raw,target=target),self.assertRaises(ValueError):subject.parse_job_output(raw,target)

    def test_source_hash_gate_reads_the_actual_five_files(self):
        """验证当前实际执行文件，不把字典里的版本号当作来源正确。"""
        toolroot=pathlib.Path(subject.__file__).resolve().parents[1]
        names=("performance/paired_native_bootstrap.py","performance/paired_http.py","performance/paired_identity_seed.py","performance/server200_paired_resources.py","acceptance/server200-fresh-mysql.py")
        value={"toolSourceCommit":"a"*40,"toolSha256":{n:hashlib.sha256((toolroot/n).read_bytes()).hexdigest() for n in names}}
        subject.verify_tool_sources(value)
        value["toolSha256"][names[0]]="0"*64
        with self.assertRaises(ValueError):subject.verify_tool_sources(value)



def schema_apply_output(expected_database, **result_overrides):
    """复现原宿主先输出计划、再输出执行结果的两根JSON合同；只有合成结构。"""
    plan = {"id": "a"*32, "provider": "mysql", "database": expected_database,
            "beforeFingerprint": "b"*64, "desiredFingerprint": "c"*64, "changes": [{"sql": "CREATE TABLE fixture(id bigint)"}]}
    result = {"planId": plan["id"], "database": expected_database, "startedAt": "2026-11-02T00:00:00Z",
              "finishedAt": "2026-11-02T00:00:01Z", "succeeded": True, "appliedStatements": 1, "error": None}
    result.update(result_overrides)
    return (json.dumps(plan, indent=2)+"\n"+json.dumps(result)+"\n").encode(), result


class SchemaApplyOutputTests(unittest.TestCase):
    """只允许已知apply命令的计划与结果配对，不把最后一段任意JSON当成功。"""
    def setUp(self):
        self.database = settings()["databases"][1]

    def parse(self, raw, job="csharp-apply"):
        """绑定本轮业务库和精确命令，不能从回执自行选择目标。"""
        return subject.parse_job_output(raw, "csharp", job, self.database)

    def test_original_plan_then_success_result_are_paired(self):
        raw, result = schema_apply_output(self.database)
        self.assertEqual(self.parse(b"info: schema apply\n"+raw), result)

    def test_two_roots_are_not_allowed_for_preview_or_unspecified_job(self):
        raw, _ = schema_apply_output(self.database)
        for job in (None, "csharp-preview", "csharp-verify", "csharp-security"):
            with self.subTest(job=job), self.assertRaises(ValueError): self.parse(raw, job)
        with self.assertRaises(ValueError): subject.parse_job_output(raw, "csharp", "csharp-apply")

    def test_unrelated_plan_database_and_partial_or_failed_results_are_rejected(self):
        for change in ({"database": "unapproved"}, {"planId": "d"*32}, {"succeeded": False},
                       {"succeeded": 1}, {"appliedStatements": 0}, {"appliedStatements": True}, {"error": "failed"}):
            raw, _ = schema_apply_output(self.database, **change)
            with self.subTest(change=change), self.assertRaises(ValueError): self.parse(raw)
        raw, _ = schema_apply_output("unapproved")
        with self.assertRaises(ValueError): self.parse(raw)

    def test_missing_extra_duplicate_and_nonfinite_outputs_are_rejected(self):
        raw, _ = schema_apply_output(self.database)
        malformed = (raw+b'{}', raw+b'finished', raw.replace(b'"succeeded": true', b'"succeeded": false, "succeeded": true'),
                     raw.replace(b'"sql": ', b'"n": NaN, "sql": '), raw.replace(b'"provider": "mysql"', b'"provider": "sqlite"'),
                     raw.replace(b'"changes": [', b'"unexpected": 1, "changes": ['), json.dumps({"succeeded": True}).encode())
        for value in malformed:
            with self.subTest(value=value), self.assertRaises(ValueError): self.parse(value)


class BootstrapInputsTests(unittest.TestCase):
    """只验证初始化材料合同，class字节为受控假输入；原Java class身份另有V21真实证据。"""
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=pathlib.Path(self.temp.name);self.folder=self.root/"bootstrap";self.folder.mkdir();self.value=settings()
        names={"java/project-config.json","java/business-schema.sql","java/sources.json","descriptor.json","bootstrap.env"}
        names.update("java/configuration-schema/"+t+".sql" for t in ("config_button","config_child","config_column","config_first_menu","config_index_column","config_index","config_page_column","config_page","config_second_menu","config_table","yz_admin_type","yz_file_library_folder","yz_file_library","yz_file_ref"))
        names.update("java/account-schema/"+t+".sql" for t in ("yz_manager","yz_role","yz_role_permission","yz_manager_log"))
        names.update("classes/com/yinzon/samplehost/"+n for n in subject.CLASS_HASHES)
        for name in names:
            path=self.folder/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b"controlled-fixture")
        descriptor={"Version":1,"Purpose":"paired-runtime-bootstrap-v1","RunId":self.value["runId"],"ExpectedDatabase":self.value["databases"][0],"BundleRoot":"/run/metarill/bootstrap/java","SourcesManifestSha256":hashlib.sha256(b"controlled-fixture").hexdigest(),"AuthorizationStatus":"approved","LeaseStatus":"running","InstancePin":subject.PIN,"ExpectedPlanSha256":"f"*64}
        (self.folder/"descriptor.json").write_text(json.dumps(descriptor),encoding="utf-8")
        (self.folder/"bootstrap.env").write_text("Authentication__Bootstrap__Username=paired-owner-"+"a"*16+"\nAuthentication__Bootstrap__Password="+"1"*64+"\n",encoding="utf-8")
        self.enterContext(mock.patch.object(subject.resources,"regular",side_effect=lambda p,directory=False:type("Info",(),{"st_mode":0o40755 if directory else 0o100600 if p.name in ("inputs.json","bootstrap.env") else 0o100444})()))
        self.enterContext(mock.patch.object(subject,"CLASS_HASHES",{n:hashlib.sha256(b"controlled-fixture").hexdigest() for n in subject.CLASS_HASHES}))
        self.refresh()

    def refresh(self):
        """只更新测试自己的材料摘要，证明自报哈希不能扩展来源或环境键。"""
        entries={p.relative_to(self.folder).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in self.folder.rglob('*') if p.is_file() and p.name!="inputs.json"}
        raw=json.dumps(entries).encode();(self.folder/"inputs.json").write_bytes(raw);self.value["bootstrapSha256"]=hashlib.sha256(raw).hexdigest()

    def test_complete_fixed_27_inputs_are_accepted_offline(self):
        self.assertEqual(subject.bootstrap(self.root,self.value)["ExpectedDatabase"],self.value["databases"][0])

    def test_unlisted_or_self_reported_extra_class_is_rejected(self):
        path=self.folder/"classes/Shadow.class";path.write_bytes(b"not-fixed")
        with self.assertRaises(ValueError):subject.bootstrap(self.root,self.value)
        self.refresh()
        with self.assertRaises(ValueError):subject.bootstrap(self.root,self.value)

    def test_updated_class_digest_does_not_replace_trusted_entry(self):
        path=next((self.folder/"classes").rglob('*.class'));path.write_bytes(b"changed");self.refresh()
        with self.assertRaises(ValueError):subject.bootstrap(self.root,self.value)

    def test_updated_descriptor_or_environment_cannot_change_realm_or_mode(self):
        path=self.folder/"descriptor.json";original=path.read_bytes();body=json.loads(original);body["ExpectedDatabase"]="metarill_dev";path.write_text(json.dumps(body),encoding="utf-8");self.refresh()
        with self.assertRaises(ValueError):subject.bootstrap(self.root,self.value)
        path.write_bytes(original);env=self.folder/"bootstrap.env";env.write_text(env.read_text(encoding="utf-8")+"Authentication__Mode=Development\n",encoding="utf-8");self.refresh()
        with self.assertRaises(ValueError):subject.bootstrap(self.root,self.value)


class JobTests(unittest.TestCase):
    """实际命令构造与归属生命周期，Docker引擎为内存桩。"""
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=pathlib.Path(self.temp.name);self.value=settings();self.owner="e"*64;self.engine=Engine()
        self.resource={"runId":self.value["runId"],"ownerToken":self.owner,"status":"created","hostsStarted":False,"items":[]}
        (self.root/"resource-state.json").write_text(json.dumps(self.resource),encoding="utf-8")
        self.state={"runId":self.value["runId"],"ownerToken":self.owner,"jobs":[]};self.path=self.root/"native-state.json"
        self.enterContext(mock.patch.object(subject.resources,"docker",side_effect=self.docker))
        self.capture=self.enterContext(mock.patch.object(subject,"capture",return_value=subprocess.CompletedProcess([],0,b'{"changes":[]}',b'')))

    def docker(self,*args):
        """只模拟新的两个只读inspect形状，其余复用资源工具既有内存引擎。"""
        if args[:2]==("container","inspect") and args[args.index("--format")+1]=="{{.Image}}":return subject.resources.IMAGES["java" if "init-java" in args[-1] else "csharp"]
        if args[:2]==("container","inspect") and '"exitCode"' in args[args.index("--format")+1]:return '{"running":false,"exitCode":0}'
        return self.engine(*args)

    def test_csharp_job_uses_original_flag_and_no_secret_argv_then_cleans(self):
        """默认ENTRYPOINT执行原schema命令；私有配置只按env-file传递，finally核零。"""
        result=subject.initialization_job(self.root,self.value,"csharp-preview",self.state,self.path)
        self.assertEqual(result,{"changes":[]});self.assertFalse(self.engine.objects);self.assertTrue(self.state["jobs"][0]["containerAbsent"])
        command=next(c for c in self.engine.calls if c[:2]==("container","create"))
        self.assertEqual(command[-2:],(subject.resources.IMAGES["csharp"],"--schema-preview"));self.assertNotIn("--publish",command)
        self.assertIn("--read-only",command);self.assertIn("--env-file",command)
        with self.assertRaises(ValueError):subject.initialization_job(self.root,self.value,"csharp-preview",self.state,self.path)

    def test_apply_job_binds_plan_and_result_to_approved_database_then_cleans(self):
        """真正经过命令构造与解析边界，不用mock跳过导致首轮失败的输出合同。"""
        raw, result = schema_apply_output(self.value["databases"][1])
        self.capture.return_value = subprocess.CompletedProcess([], 0, raw, b'')
        self.assertEqual(subject.initialization_job(self.root, self.value, "csharp-apply", self.state, self.path), result)
        self.assertEqual(self.capture.call_count, 1)
        self.assertFalse(self.engine.objects)
        self.assertTrue(self.state["jobs"][0]["containerAbsent"])

    def test_java_job_reuses_fixed_external_loader_and_readonly_bundle(self):
        """不启动普通Web或替换Java实现，只调用已固定原配置安装入口。"""
        self.capture.return_value=subprocess.CompletedProcess([],0,'结果：{"applied":true,"verifiedTables":22}\n'.encode(),b'')
        subject.initialization_job(self.root,self.value,"java-apply",self.state,self.path)
        command=next(c for c in self.engine.calls if c[:2]==("container","create"))
        self.assertIn("org.springframework.boot.loader.launch.PropertiesLauncher",command);self.assertIn("apply-initial",command)
        self.assertIn("type=bind,src="+str(self.root)+"/bootstrap,dst=/run/metarill/bootstrap,readonly",command)
        self.assertFalse(self.engine.objects)

    def test_failed_start_and_lost_create_receipt_are_not_retried_and_clean_owned_only(self):
        """退出失败或回执丢失均保留错误，但同token对象会被清理。"""
        self.capture.return_value=subprocess.CompletedProcess([],1,b'',b'private-error')
        with self.assertRaises(RuntimeError):subject.initialization_job(self.root,self.value,"csharp-apply",self.state,self.path)
        self.assertEqual(self.capture.call_count,1);self.assertFalse(self.engine.objects)
        self.engine.fail_target="init-java-preview"
        with self.assertRaises(RuntimeError):subject.initialization_job(self.root,self.value,"java-preview",self.state,self.path)
        self.assertFalse(self.engine.objects);self.assertNotIn("id",self.state["jobs"][-1])

    def test_foreign_resource_token_refuses_before_docker(self):
        """不能从同名资源记录接管另一次调用的归属。"""
        self.resource["ownerToken"]="f"*64;(self.root/"resource-state.json").write_text(json.dumps(self.resource),encoding="utf-8")
        with self.assertRaises(ValueError):subject.initialization_job(self.root,self.value,"java-preview",self.state,self.path)
        self.assertFalse(self.engine.calls)


class FlowFixture(unittest.TestCase):
    """完整阶段顺序与失败清理；所有外部边界显式替换，不能当作实库通过。"""
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=pathlib.Path(self.temp.name);self.value=settings();self.events=[];self.ownerfile=self.root/"mysql-owner.json"
        (self.root/"java.env").write_text("YZP_SAMPLE_DB_PASSWORD="+"1"*64,encoding="utf-8")
        self.enterContext(mock.patch.object(subject.resources,"root_for",return_value=self.root))
        self.enterContext(mock.patch.object(subject,"verify_tool_sources"));self.enterContext(mock.patch.object(subject.resources,"read_materials"))
        self.enterContext(mock.patch.object(subject,"bootstrap",return_value={"ExpectedPlanSha256":"e"*64}))
        self.enterContext(mock.patch.object(subject,"read_private",side_effect=lambda *args:credentials()))
        self.mysql=mock.Mock();self.mysql.ownership_path.return_value=self.ownerfile
        self.mysql.mysql.side_effect=lambda sql:subject.PIN["hostname"]+"\t"+subject.PIN["serverUuid"] if sql=="SELECT @@hostname,@@server_uuid;" else "0"
        self.enterContext(mock.patch.object(subject,"load_mysql",return_value=self.mysql));self.enterContext(mock.patch.object(subject.resources,"verify_server"))
        self.enterContext(mock.patch.object(subject.resources,"docker",side_effect=lambda *args:json.dumps({"id":args[-1],"os":"linux","architecture":"amd64"})))
        self.enterContext(mock.patch.object(subject.resources,"inspect",return_value=None))
        self.enterContext(mock.patch.object(subject,"mysql_operation",side_effect=self.database))
        self.enterContext(mock.patch.object(subject.resources,"manage",side_effect=self.resources))
        self.jobs=self.enterContext(mock.patch.object(subject,"initialization_job",side_effect=self.job))
        self.enterContext(mock.patch.object(subject.seed,"apply",side_effect=lambda value,target,*args:{"target":target,"verifiedRows":12 if target=="java" else 15}))
        self.enterContext(mock.patch.object(subject.http,"Server200LocalProbe"));self.enterContext(mock.patch.object(subject.http,"equal_limits",return_value={"java":{"id":"1"},"csharp":{"id":"2"}}))
        self.enterContext(mock.patch.object(subject.http,"verify_transport",return_value={"verified":True}))
        self.identity=self.enterContext(mock.patch.object(subject,"identity_http",return_value=([{"mockedOnly":True}],{})))
        self.enterContext(mock.patch.object(subject,"cleanup_private",return_value={"mockedOnly":True}))

    def database(self,mysql,value,operation,owner_token,password=None):
        """按真实归属token保存桩记录，事件仅用于验证停止资源早于DROP。"""
        self.events.append("database-"+operation)
        if operation=="create":self.ownerfile.write_text(owner_token,encoding="utf-8")
        else:self.assertEqual(self.ownerfile.read_text(encoding="utf-8"),owner_token);self.ownerfile.unlink()
        return {"mockedOnly":True}

    def resources(self,value,operation):
        """模拟主资源账本，token来自本次native状态，不另造共享身份。"""
        self.events.append("resources-"+operation)
        path=self.root/"resource-state.json"
        if operation=="create":path.write_text(json.dumps({"ownerToken":value["ownerToken"],"status":"created","items":[]}),encoding="utf-8")
        elif operation=="cleanup":path.write_text(json.dumps({"ownerToken":value["ownerToken"],"status":"ended","items":[]}),encoding="utf-8")
        return {"mockedOnly":True}

    def job(self,root,value,name,state,path):
        """返回原命令的结构化响应合同；每个正式命令恰调用一次。"""
        self.events.append(name)
        return {"java-preview":{"observedTableCount":0,"planSha256":"e"*64},"java-apply":{"applied":True,"verifiedTables":22},"csharp-preview":{"changes":[{}]},"csharp-apply":{"succeeded":True},"csharp-verify":{"changes":[]},"csharp-security":{"status":"initialized"}}[name]


class FlowTests(FlowFixture):
    def test_full_flow_cleans_containers_before_database_and_never_accepts_performance(self):
        """一次从显式空库命令到身份HTTP，再独立清理；只有桩，不能转记正式通过。"""
        with contextlib.redirect_stdout(io.StringIO()):result=subject.run(self.value)
        self.assertTrue(result["passed"]);self.assertFalse(result["performanceAccepted"]);self.assertTrue(result["deniedWritesPhysicalRowsUnchanged"])
        self.assertEqual(self.jobs.call_count,6);self.assertEqual(result["leaseStatus"],"ended")
        self.assertLess(self.events.index("resources-cleanup"),self.events.index("database-cleanup"));self.assertFalse(self.ownerfile.exists())
        with self.assertRaises(FileExistsError),contextlib.redirect_stdout(io.StringIO()):subject.run(self.value)

    def test_failed_initialization_preserves_failure_and_still_cleans(self):
        """首因失败不续做播种/HTTP，也不以清理成功改写业务结果。"""
        self.jobs.side_effect=RuntimeError("模拟正式初始化失败")
        with contextlib.redirect_stdout(io.StringIO()):result=subject.run(self.value)
        self.assertFalse(result["passed"]);self.assertEqual(result["errorType"],"RuntimeError");self.identity.assert_not_called()
        self.assertEqual(result["cleanupErrors"],[]);self.assertFalse(self.ownerfile.exists())

    def test_foreign_resource_record_prevents_drop_and_secret_cleanup(self):
        """未确认资源归属/停止时绝不DROP，即便库是本次创建也保留现场。"""
        def foreign(*args):
            (self.root/"resource-state.json").write_text(json.dumps({"ownerToken":"f"*64,"status":"created"}),encoding="utf-8")
            raise RuntimeError("模拟未知资源")
        self.jobs.side_effect=foreign
        with contextlib.redirect_stdout(io.StringIO()):result=subject.run(self.value)
        self.assertFalse(result["passed"]);self.assertTrue(result["cleanupErrors"]);self.assertTrue(self.ownerfile.exists());self.assertNotIn("database-cleanup",self.events)


class CleanupRecoveryTests(FlowFixture):
    """继承同一外部桩边界；只恢复清理，不恢复或重放已结束的业务运行。"""
    def test_cleanup_only_after_failure_never_replays_initialization_or_http(self):
        def foreign(*args):
            (self.root/"resource-state.json").write_text(json.dumps({"ownerToken":"f"*64,"status":"created"}),encoding="utf-8")
            raise RuntimeError("模拟需人工核对清理")
        self.jobs.side_effect=foreign
        with contextlib.redirect_stdout(io.StringIO()):failed=subject.run(self.value)
        self.assertTrue(failed["cleanupErrors"]);calls=self.jobs.call_count
        (self.root/"resource-state.json").write_text(json.dumps({"ownerToken":failed["ownerToken"],"status":"created","items":[]}),encoding="utf-8")
        with mock.patch.object(subject.resources,"regular"),contextlib.redirect_stdout(io.StringIO()):cleaned=subject.cleanup_only(self.value)
        self.assertFalse(cleaned["passed"]);self.assertEqual(cleaned["cleanupErrors"],[]);self.assertFalse(self.ownerfile.exists())
        self.assertEqual(self.jobs.call_count,calls);self.identity.assert_not_called();self.assertEqual(self.events.count("database-create"),1)

    def test_physical_rows_changed_after_denied_http_are_not_accepted(self):
        with mock.patch.object(subject,"business_counts",side_effect=[{"rows":0},{"rows":1}]),contextlib.redirect_stdout(io.StringIO()):result=subject.run(self.value)
        self.assertFalse(result["passed"]);self.assertEqual(result["errorType"],"RuntimeError");self.assertEqual(result["cleanupErrors"],[])


class FailureEvidenceTests(unittest.TestCase):
    """证据必须不含原异常正文，失败端和已完成播种分别保存。"""
    def test_exception_details_are_not_serialized(self):
        error = RuntimeError("private SQL password=secret-value")
        error.database_error_number = "secret-value"
        self.assertEqual(subject.failure_evidence(error), {"code": "unclassified"})
        error = RuntimeError("播种后逐字段独立回读不符")
        error.database_error_number = 1054
        self.assertEqual(subject.failure_evidence(error), {"code": "seed-readback-mismatch", "databaseErrorNumber": 1054})


class PartialSeedFailureTests(FlowFixture):
    """另一端播种失败保留此前成功回执，但整链仍失败并正常清理。"""
    def test_second_target_failure_keeps_first_target_and_never_starts_http(self):
        def fail_second(lease, target, fixture, query):
            if target == "csharp": raise RuntimeError("播种后逐字段独立回读不符")
            return {"target": "java", "verifiedRows": 12}
        with mock.patch.object(subject.seed, "apply", side_effect=fail_second), contextlib.redirect_stdout(io.StringIO()): result = subject.run(self.value)
        self.assertFalse(result["passed"])
        self.assertEqual(result["stage"], "C#普通身份播种与逐字段回读")
        self.assertEqual(result["identitySeeds"], [{"target": "java", "verifiedRows": 12}])
        self.assertEqual(result["failure"], {"code": "seed-readback-mismatch"})
        self.assertEqual(result["cleanupErrors"], [])
        self.identity.assert_not_called()


class ActualPrivateCleanupTests(unittest.TestCase):
    """真实POSIX目录清理仅在Linux执行，Windows不冒充unlink/rmtree安全证明。"""
    @unittest.skipUnless(os.name=="posix","真实私有材料清理只在Linux验证")
    def test_only_owned_secret_files_and_data_directories_are_removed(self):
        with tempfile.TemporaryDirectory() as folder:
            root=pathlib.Path(folder);(root/"bootstrap").mkdir()
            for name in ("java.env","csharp.env","redis.conf","identity.json","bootstrap/bootstrap.env"):(root/name).write_text("fake",encoding="utf-8")
            for name in ("data-java","data-csharp"):(root/name).mkdir();(root/name/"object.txt").write_text("fake",encoding="utf-8")
            (root/"keep-evidence.json").write_text("{}",encoding="utf-8");(root/"bootstrap").chmod(0o555)
            self.assertTrue(subject.cleanup_private(root)["privateCredentialsRemoved"])
            self.assertTrue((root/"keep-evidence.json").exists());self.assertFalse((root/"data-java").exists());self.assertFalse((root/"identity.json").exists())

    @unittest.skipUnless(os.name=="posix","真实私有材料清理只在Linux验证")
    def test_dangling_secret_link_is_refused_without_following_it(self):
        with tempfile.TemporaryDirectory() as folder:
            root=pathlib.Path(folder);(root/"java.env").symlink_to(root/"missing")
            with self.assertRaises(ValueError):subject.cleanup_private(root)
            self.assertTrue((root/"java.env").is_symlink());self.assertFalse((root/"missing").exists())


class IdentityHttpTests(unittest.TestCase):
    """真实宿主响应合同的内存样本；HTTP生产链路仍必须另实测。"""
    @classmethod
    def setUpClass(cls):
        cls.creds=credentials(); decoded=copy.deepcopy(cls.creds)
        for value in decoded.values():value["csharpSalt"]=base64.b64decode(value["csharpSalt"])
        cls.fixture=subject.seed.prepare("a"*32,decoded)

    def reply(self,port,method,path,body=None,token=None,form=False):
        """按同逻辑三身份区分两个原宿主状态码，错误不被当作成功。"""
        target="java" if port==28071 else "csharp";failure=(200,500) if target=="java" else (403,403)
        if method=="GET":return 200,{"code":200,"data":{"enabled":False,"apiEndpoint":""}}
        if path.endswith("/login"):
            self.assertTrue(form)
            self.assertEqual(set(body),{"username","password","captchaVerification"})
            self.assertEqual(body["captchaVerification"],"")
            if body["password"].startswith("wrong-"):return (200,{"code":500}) if target=="java" else (401,{"code":401})
            principal=next(p for p in self.fixture["common"] if p["name"]==body["username"]);index=subject.seed.KINDS.index(principal["kind"])+1
            issued=f"11111111-1111-1111-1111-{index:012x}" if target=="java" else f"{index:064x}"
            return 200,{"code":200,"data":{"id":principal["accountId"],"name":principal["name"],"yzAdminTypeId":str(subject.seed.ADMIN)},"map":{"loginStateId":issued}}
        index=int(token.split('-')[-1],16)
        if index==3 or path.endswith('/add'):return failure[0],{"code":failure[1]}
        return 200,{"code":200,"data":{"records":[]}}

    def test_password_page_query_and_readonly_write_rejections_use_original_routes(self):
        """所有登录均为原form，错误密码与无权/只读拒绝都进入真实执行分支。"""
        with mock.patch.object(subject,"send",side_effect=self.reply) as send:
            facts,tokens=subject.identity_http(settings(),self.fixture,self.creds)
        self.assertEqual(len(facts),6);self.assertEqual(len(tokens),6)
        self.assertEqual(sum(c.args[2].endswith('/login') for c in send.call_args_list),8)
        self.assertTrue(all(c.kwargs.get('form') is True for c in send.call_args_list if c.args[2].endswith('/login')))
        self.assertEqual(sum(c.args[2].endswith('/add') for c in send.call_args_list),2)

    def test_identity_receipt_locates_timeout_without_retry_or_secret(self):
        """已完成步骤和超时一步留证，口令、令牌和异常正文都不落回执。"""
        receipts=[]
        def reply(port,method,path,body=None,token=None,form=False):
            if path.endswith("/page") and token.endswith("000000000002"):
                raise TimeoutError("sensitive-response-details")
            return self.reply(port,method,path,body,token,form=form)
        with mock.patch.object(subject,"send",side_effect=reply) as send,self.assertRaises(TimeoutError):
            subject.identity_http(settings(),self.fixture,self.creds,record_request=receipts.append)
        self.assertEqual(["ready","wrong-password","login-writer","page-writer","login-reader","page-reader"],
                         [item["step"] for item in receipts])
        self.assertEqual(("java","TimeoutError"),(receipts[-1]["target"],receipts[-1]["errorType"]))
        self.assertEqual(6,send.call_count)
        self.assertNotIn("sensitive-response-details",json.dumps(receipts))
        self.assertNotIn("11111111",json.dumps(receipts))

    def test_numeric_account_id_leaked_password_or_unprotected_write_cannot_pass(self):
        """ID精度、凭据脱敏和只读限制不能被HTTP200掩盖。"""
        original=self.reply
        for kind in ('numeric-id','secret','readonly-write'):
            def response(*args,**kwargs):
                status,payload=original(*args,**kwargs)
                if args[2].endswith('/login') and payload.get('code')==200:
                    if kind=='numeric-id':payload['data']['id']=int(payload['data']['id'])
                    if kind=='secret':payload['data']['password']='leak'
                if kind=='readonly-write' and args[2].endswith('/add'):return 200,{'code':200}
                return status,payload
            with self.subTest(kind=kind),mock.patch.object(subject,'send',side_effect=response),self.assertRaises(RuntimeError):subject.identity_http(settings(),self.fixture,self.creds)


class ActualTransportTests(unittest.TestCase):
    """只启动本机测试HTTP服务，验证form、代理绕过和不跟随重定向。"""
    def test_actual_form_and_redirect_never_send_secret_query_or_follow_location(self):
        calls=[]
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def do_POST(self):
                calls.append((self.path,self.headers.get('Content-Type'),self.rfile.read(int(self.headers['Content-Length']))))
                self.send_response(200);self.end_headers();self.wfile.write(b'{"code":200}')
            def do_GET(self):
                calls.append((self.path,None,b''));self.send_response(302);self.send_header('Location','/leaked');self.end_headers();self.wfile.write(b'{"code":302}')
        server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        try:
            with mock.patch.dict(os.environ,{'HTTP_PROXY':'http://127.0.0.1:1','NO_PROXY':''}):
                self.assertEqual(subject.send(server.server_port,'POST','/login',{'username':'测试','password':'fake','captchaVerification':''},form=True),(200,{'code':200}))
                self.assertEqual(subject.send(server.server_port,'GET','/redirect',token='fake-token'),(302,{'code':302}))
            self.assertEqual(len(calls),2);self.assertEqual(calls[0][0],'/login');self.assertIn('application/x-www-form-urlencoded',calls[0][1]);self.assertIn(b'password=fake',calls[0][2]);self.assertIn(b'captchaVerification=',calls[0][2]);self.assertNotIn('?',calls[0][0]);self.assertEqual(calls[1][0],'/redirect')
        finally:server.shutdown();server.server_close();thread.join()


if __name__=='__main__':
    unittest.main()
