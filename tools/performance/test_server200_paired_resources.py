"""双端资源协调的离线归属/秘密/失败路径；不连接服务器、不创建真实容器。"""
import contextlib
import copy
import hashlib
import io
import json
import os
import pathlib
import stat
import tempfile
import unittest
from unittest import mock
import server200_paired_resources as subject

REAL_REGULAR = subject.regular


def configuration():
    """返回虚构运行号及完整固定镜像，不代表已取得任何真实授权。"""
    return {"version": 1, "purpose": "paired-resources-v1", "runId": "a" * 32,
            "authorizationStatus": "approved", "leaseStatus": "running", "images": dict(subject.IMAGES),
            "ports": {"java": 28071, "csharp": 28072}, "materialsSha256": "f" * 64}


class Engine:
    """模拟Docker对象身份和命令，只捕获白名单参数，不启动任何进程。"""
    def __init__(self):
        self.objects = {}; self.calls = []; self.next_id = 1; self.fail_target = None

    def __call__(self, *args):
        """用实际工具生成的命令制造归属标签，支持创建回执丢失及独立观察。"""
        self.calls.append(args)
        kind, operation = args[:2]
        if operation == "ls":
            return "\n".join(n for (k, n) in self.objects if k == kind)
        if operation == "inspect":
            return "\t".join(json.dumps(v) for v in self.objects[(kind, args[-1])])
        if operation == "create":
            name = args[-1] if kind == "network" else args[args.index("--name") + 1]
            labels = dict(args[i+1].split("=", 1) for i, a in enumerate(args) if a == "--label")
            identifier = f"{self.next_id:064x}"; self.next_id += 1
            self.objects[kind, name] = [identifier, labels[subject.LABEL + "run"], labels[subject.LABEL + "owner"], labels[subject.LABEL + "target"]]
            if labels[subject.LABEL + "target"] == self.fail_target:
                raise RuntimeError("模拟Docker已创建对象但回执丢失")
            return identifier
        if operation == "start": return args[-1]
        if operation == "rm": self.objects.pop((kind, args[-1])); return args[-1]
        raise AssertionError(args)


class Fixture(unittest.TestCase):
    """每项隔离本机临时配置；所有口令均为固定假值且不会连接外部。"""
    def setUp(self):
        """按真实白名单格式准备三份假配置，记录各自摘要。"""
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name); self.value = configuration(); self.engine = Engine()
        run = self.value["runId"][:16]; password = "1"*64; redis = "2"*64
        self.inputs = {
            "java.env": f"YZP_SAMPLE_JDBC_URL=jdbc:mysql://192.168.31.200:3306/metarill_fresh_{run}_paired_java?useUnicode=true&characterEncoding=utf8&serverTimezone=Asia%2FShanghai\nYZP_SAMPLE_DB_USER=mtf_{run}\nYZP_SAMPLE_DB_PASSWORD={password}\nYZP_SAMPLE_REDIS_HOST=redis\nYZP_SAMPLE_REDIS_PORT=6379\nYZP_SAMPLE_REDIS_PASSWORD={redis}\n",
            "csharp.env": f"Database__Provider=mysql\nDatabase__ExpectedDatabase=metarill_fresh_{run}_paired_csharp\nDatabase__ConnectionString=Server=192.168.31.200;Port=3306;Database=metarill_fresh_{run}_paired_csharp;User ID=mtf_{run};Password={password};Allow User Variables=true;\n",
            "redis.conf": f'bind 0.0.0.0\nport 6379\nprotected-mode yes\nsave ""\nappendonly no\nrequirepass {redis}\nmaxmemory 128mb\nmaxmemory-policy noeviction\ndir /data\n'}
        self.materialize()
        self.enterContext(mock.patch.object(subject, "root_for", return_value=self.root))
        self.enterContext(mock.patch.object(subject, "docker", side_effect=self.engine))
        self.enterContext(mock.patch.object(subject.os, "chown", create=True))
        # Windows不模拟POSIX权限；真实Linux另需原函数拒绝测试和正式预检。
        self.enterContext(mock.patch.object(subject, "regular", side_effect=lambda p, directory=False: type("Info", (), {"st_mode": (stat.S_IFDIR|0o700) if directory else (stat.S_IFREG|(0o444 if p.name in {"redis.conf", "observation.key"} else 0o600))})()))

    def materialize(self):
        """重写测试夹具及摘要，用于证明更新自报摘要仍不能绕过语义白名单。"""
        hashes = {}
        for name, contents in self.inputs.items():
            path = self.root/name
            if path.exists(): path.chmod(0o600)
            raw = contents.encode(); path.write_bytes(raw); path.chmod(0o444 if name in {"redis.conf", "observation.key"} else 0o600)
            hashes[name] = hashlib.sha256(raw).hexdigest()
        data = json.dumps(hashes).encode(); (self.root/"materials.json").write_bytes(data)
        self.value["materialsSha256"] = hashlib.sha256(data).hexdigest()

    def tearDown(self):
        """Windows删除只读假配置前仅调整本测试文件的属性。"""
        for path in self.root.glob("*"):
            if path.is_file(): path.chmod(0o600)

    def state(self):
        """读取没有口令的独占归属状态。"""
        return json.loads((self.root/"resource-state.json").read_text())

    def test_upper_coordinator_token_is_used_and_invalid_token_has_no_io(self):
        """上层固定同一随机token贯穿资源/库；外部非法值不能进入创建。"""
        for invalid in (None,True,"short"):
            with self.subTest(invalid=invalid),self.assertRaises(ValueError):subject.manage({**self.value,"ownerToken":invalid},"create")
        self.assertFalse(self.engine.calls)
        self.value["ownerToken"]="a"*64;subject.manage(self.value,"create")
        self.assertEqual(self.state()["ownerToken"],"a"*64)
        self.assertTrue(all(item[2]=="a"*64 for item in self.engine.objects.values()))
        self.assertTrue(subject.manage(self.value,"cleanup")["resourcesCleaned"])

    def test_create_only_redis_started_then_cleanup_independent_zero(self):
        """宿主必须created等待上层初始化；清理按容器后网络执行并逐个再观察。"""
        result = subject.manage(self.value, "create")
        self.assertFalse(result["hostsStarted"]); self.assertFalse(result["databaseInitialized"])
        self.assertEqual(len(self.engine.objects), 4)
        started = [c[-1] for c in self.engine.calls if c[:2] == ("container", "start")]
        self.assertEqual(started, ["metarill-perf-"+self.value["runId"]+"-redis"])
        self.assertTrue(subject.manage(self.value, "cleanup")["independentAbsenceVerified"])
        self.assertFalse(self.engine.objects); self.assertEqual(self.state()["status"], "ended")
        with self.assertRaises(ValueError): subject.manage(self.value, "cleanup")

    def test_hosts_share_limits_loopback_and_fixed_images(self):
        """两宿主限制相同，不发布Redis、不使用tag、特权或宿主网络。"""
        subject.manage(self.value, "create")
        commands = [c for c in self.engine.calls if c[:2] == ("container", "create")]
        self.assertEqual(len(commands), 3)
        redis, java, csharp = commands
        self.assertNotIn("--publish", redis)
        for key in ("--memory", "--memory-swap", "--cpu-period", "--cpu-quota", "--cpuset-cpus", "--pids-limit"):
            self.assertEqual(java[java.index(key)+1], csharp[csharp.index(key)+1])
        for target, command in (("java", java), ("csharp", csharp)):
            self.assertEqual(command[-1], subject.IMAGES[target]); self.assertIn("--read-only", command)
            self.assertEqual(command[command.index("--publish")+1], f"127.0.0.1:{self.value['ports'][target]}:8080")
            self.assertNotIn("--privileged", command)
        serialized = json.dumps(self.engine.calls) + json.dumps(self.state())
        self.assertNotIn("1"*64, serialized); self.assertNotIn("2"*64, serialized)

    def test_pending_ended_invalid_scope_no_files_or_subprocess(self):
        """批准和范围验证先于路径、凭据及任何系统操作。"""
        for change in ({"authorizationStatus": "pending"}, {"leaseStatus": "ended"}, {"runId": "../other"}, {"purpose": "agent"}, {"images": {**subject.IMAGES,"java":"java:latest"}}, {"ports": {"java":True,"csharp":28072}}, {"ports":{"java":28071,"csharp":28071}}):
            value = {**self.value, **change}
            with self.subTest(change=change), mock.patch.object(subject, "root_for", side_effect=AssertionError("不应读文件")), self.assertRaises(ValueError):
                subject.manage(value, "create")
        self.assertFalse(self.engine.calls)

    def test_changed_private_file_refuses_before_docker(self):
        """普通字节漂移不能通过，不能先创建网络再发现材料错误。"""
        (self.root/"java.env").write_text("changed")
        with self.assertRaises(ValueError): subject.manage(self.value, "create")
        self.assertFalse(self.engine.calls)

    def test_updated_hash_does_not_allow_foreign_db_or_env_injection(self):
        """即使更新清单，也不能转到既有库、共享Redis或任意加载参数。"""
        original = copy.deepcopy(self.inputs)
        for field, old, new in (("java.env", "192.168.31.200", "192.168.31.201"), ("java.env", "YZP_SAMPLE_REDIS_HOST=redis", "YZP_SAMPLE_REDIS_HOST=shared-redis"), ("csharp.env", "_paired_csharp;", "_existing;"), ("redis.conf", "appendonly no", "appendonly yes"), ("java.env", "YZP_SAMPLE_DB_USER=", "JAVA_TOOL_OPTIONS=-javaagent:x\nYZP_SAMPLE_DB_USER=")):
            self.inputs = copy.deepcopy(original); self.inputs[field] = self.inputs[field].replace(old, new); self.materialize()
            with self.subTest(field=field,new=new), self.assertRaises(ValueError): subject.manage(self.value, "create")
        self.assertFalse(self.engine.calls)

    def test_new_observed_runtime_requires_same_isolated_redis_before_docker(self):
        """新镜像只能使用本轮Redis、原口令和DB0；即使重算输入摘要也不能外接或关闭保护。"""
        self.value.update(purpose="paired-observed-resources-v1", images=dict(subject.OBSERVATION_IMAGES))
        self.inputs["observation.key"] = "7"*32
        base = self.inputs["csharp.env"]
        key = "Authentication__LoginFailures__ConnectionString="
        current = key + "redis:6379,password=" + "2"*64 + ",defaultDatabase=0\n"
        for candidate in ("", current.replace("redis:6379", "shared-redis:6379"), current.replace(":6379", ":6380"), current.replace("2"*64,"3"*64), current.replace("defaultDatabase=0","defaultDatabase=5"), current+"Authentication__Mode=Development\n"):
            self.inputs["csharp.env"] = base + candidate; self.materialize()
            with self.subTest(candidate=candidate), self.assertRaises(ValueError): subject.manage(self.value, "create")
        self.assertFalse(self.engine.calls)
        self.inputs["csharp.env"] = base + current; self.materialize()
        self.assertFalse(subject.manage(self.value, "create")["hostsStarted"])
        command = next(call for call in self.engine.calls if call[:2] == ("container", "create") and "metarill.performance.target=csharp" in call)
        self.assertIn(subject.OBSERVATION_IMAGES["csharp"], command)
        self.assertIn("--Metarill:Paired:SqlObservation:ExpectedDataSha256=" + subject.OBSERVATION_DATA_SHA256, command)
        self.assertNotIn("2"*64, json.dumps(self.engine.calls) + json.dumps(self.state()))
        self.assertTrue(subject.manage(self.value, "cleanup")["independentAbsenceVerified"])

    def test_name_collision_never_adopts_existing_object(self):
        """相同运行号但已有对象时不创建、不删除、不留下伪所有权。"""
        self.engine.objects["container", "metarill-perf-"+self.value["runId"]+"-java"] = ["9"*64,"foreign","token","java"]
        with self.assertRaises(ValueError): subject.manage(self.value, "create")
        self.assertFalse((self.root/"resource-state.json").exists())
        self.assertFalse(any(c[1] in ("create","rm") for c in self.engine.calls))

    def test_lost_create_receipt_can_only_clean_matching_token(self):
        """创建成功而回执失败时保留待判定项目；后续核同token才能清理。"""
        self.engine.fail_target = "java"
        with self.assertRaises(RuntimeError): subject.manage(self.value, "create")
        self.assertEqual(self.state()["items"][-1]["target"], "java")
        self.assertNotIn("id", self.state()["items"][-1])
        self.assertEqual(len(self.engine.objects), 3)
        self.assertTrue(subject.manage(self.value, "cleanup")["resourcesCleaned"])
        self.assertFalse(self.engine.objects)

    def test_foreign_replacement_refuses_before_any_removal(self):
        """同名替换或token变化均拒绝，不能先删除其它本轮对象再失败。"""
        subject.manage(self.value, "create")
        for offset in (0,2):
            key = ("container", "metarill-perf-"+self.value["runId"]+"-java")
            old = list(self.engine.objects[key]); self.engine.objects[key][offset] = "9"*64
            with self.subTest(offset=offset), self.assertRaises(RuntimeError): subject.manage(self.value, "cleanup")
            self.assertFalse(any(c[1]=="rm" for c in self.engine.calls)); self.engine.objects[key] = old

    def test_start_requires_explicit_initialization_confirmation_and_is_once(self):
        """这里只验证上层确认合同，布尔标记本身不是数据库核验凭证。"""
        subject.manage(self.value, "create")
        with self.assertRaises(ValueError): subject.manage(self.value, "start-hosts")
        value = {**self.value,"databaseInitializationVerified":True}
        self.assertTrue(subject.manage(value, "start-hosts")["hostsStarted"])
        with self.assertRaises(ValueError): subject.manage(value, "start-hosts")
        self.assertTrue(subject.manage(value, "cleanup")["resourcesCleaned"])

    def test_changed_descriptor_and_corrupt_item_refuse(self):
        """修改端口或归属对象名称不能借清理模式放宽范围。"""
        subject.manage(self.value, "create")
        with self.assertRaises(ValueError): subject.manage({**self.value,"ports":{"java":28073,"csharp":28072}}, "cleanup")
        state=self.state(); state["items"][2]["name"]="foreign"; subject.write_state(self.root/"resource-state.json",state)
        with self.assertRaises(ValueError): subject.manage(self.value, "cleanup")
        self.assertFalse(any(c[1]=="rm" for c in self.engine.calls))

    def test_existing_command_lock_blocks_without_docker(self):
        """另一个协调器或崩溃遗留锁阻止全部命令，不能删除锁后自动接管。"""
        (self.root/"resource-command.lock").write_text("999999")
        for operation in ("create", "cleanup", "start-hosts"):
            with self.subTest(operation=operation), self.assertRaises(FileExistsError):
                subject.manage(self.value, operation)
        self.assertEqual((self.root/"resource-command.lock").read_text(),"999999")
        self.assertFalse(self.engine.calls)

    @unittest.skipUnless(os.name == "posix", "POSIX权限和符号链接仅在Linux验收")
    def test_actual_posix_private_modes(self):
        """Linux用真实lstat/uid/mode核私有材料，非桩权限不能放行宽松配置。"""
        with mock.patch.object(subject, "regular", side_effect=REAL_REGULAR):
            subject.read_materials(self.root,self.value)
            (self.root/"java.env").chmod(0o644)
            with self.assertRaises(ValueError): subject.read_materials(self.root,self.value)
            (self.root/"java.env").chmod(0o600)
            self.root.chmod(0o755)
            try:
                with self.assertRaises(ValueError): subject.read_materials(self.root,self.value)
            finally: self.root.chmod(0o700)
        self.assertFalse(self.engine.calls)

    @unittest.skipUnless(os.name == "posix", "POSIX权限和符号链接仅在Linux验收")
    def test_actual_posix_link_input_refuses(self):
        """即使链接目标字节和摘要不变，也不接受私有环境文件链接。"""
        target=self.root/"java.env.saved"; (self.root/"java.env").rename(target)
        (self.root/"java.env").symlink_to(target)
        try:
            with mock.patch.object(subject, "regular", side_effect=REAL_REGULAR), self.assertRaises(ValueError):
                subject.read_materials(self.root,self.value)
        finally:
            (self.root/"java.env").unlink();target.rename(self.root/"java.env")
        self.assertFalse(self.engine.calls)

    def test_state_ended_rejects_start_create_and_cleanup(self):
        """完成状态不能被同一命令自动复活。"""
        subject.manage(self.value,"create"); subject.manage(self.value,"cleanup")
        for operation in ("start-hosts","create","cleanup"):
            with self.subTest(operation=operation), self.assertRaises(ValueError): subject.manage(self.value,operation)


class DockerBoundaryTests(unittest.TestCase):
    """真实封装只调用内存subprocess桩，验证每次身份核对及输出边界。"""
    def test_server_wrong_name_or_ip_blocks_docker(self):
        """域名相似或缺少固定IP均不能运行Docker。"""
        for outputs in (["Other\n"], ["Server-200\n","inet 10.0.0.1/24"]):
            with mock.patch.object(subject.subprocess,"check_output",side_effect=outputs), mock.patch.object(subject.subprocess,"run") as run, self.assertRaises(RuntimeError):
                subject.docker("image","inspect","id")
            run.assert_not_called()

    def test_each_docker_uses_explicit_socket_and_sanitized_environment(self):
        """客户端DOCKER_HOST不能将命令转到本机/其它服务器。"""
        with mock.patch.object(subject.subprocess,"check_output",side_effect=["Server-200\n","inet 192.168.31.200/24"]*2) as identity, mock.patch.dict(os.environ,{"DOCKER_HOST":"tcp://foreign:2375"}), mock.patch.object(subject.subprocess,"run",return_value=type("Result",(),{"returncode":0,"stdout":"ok"})()) as run:
            subject.docker("network","ls"); subject.docker("container","ls")
        self.assertEqual(identity.call_count,4)
        for call in run.call_args_list:
            self.assertEqual(call.args[0][:3],["docker","-H","unix:///var/run/docker.sock"])
            self.assertFalse(any(k.startswith("DOCKER_") for k in call.kwargs["env"]))

    def test_docker_error_does_not_print_secret(self):
        """Docker错误正文不随异常向上传播。"""
        with mock.patch.object(subject,"verify_server"), mock.patch.object(subject.subprocess,"run",return_value=type("Result",(),{"returncode":1,"stdout":"secret","stderr":"password"})()):
            with self.assertRaises(RuntimeError) as error: subject.docker("container","start","name")
        self.assertNotIn("secret",str(error.exception)); self.assertNotIn("password",str(error.exception))

    def test_cli_pending_returns_failure_without_external_or_file_operations(self):
        """实际CLI入口pending只读stdin，零服务器/凭据操作。"""
        value=configuration();value["authorizationStatus"]="pending"
        with mock.patch.object(sys_module:=subject.sys,"argv",["tool","create"]), mock.patch.object(sys_module,"stdin",io.StringIO(json.dumps(value))), mock.patch.object(subject,"root_for",side_effect=AssertionError("不读路径")), mock.patch.object(subject.subprocess,"run",side_effect=AssertionError("不运行命令")), contextlib.redirect_stderr(io.StringIO()) as output:
            self.assertEqual(subject.main(),1)
        self.assertIn("ValueError",output.getvalue())


if __name__ == "__main__":
    unittest.main()
