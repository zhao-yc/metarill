"""离线验证随机库管理的服务器与所有权保护，不调用 Docker/MySQL。"""
import contextlib
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch
from unittest import mock

spec = importlib.util.spec_from_file_location("fresh_mysql", pathlib.Path(__file__).with_name("server200-fresh-mysql.py"))
subject = importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


class FreshMysqlOwnershipTests(unittest.TestCase):
    """创建中断与清理错误时，也不得触碰本轮所有权之外的库或账号。"""
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.state = pathlib.Path(self.directory.name) / "owner.json"
        self.run = "a" * 16
        self.password = "b" * 64
        self.databases = [f"metarill_fresh_{self.run}_{kind}" for kind in ("platform", "business", "package", "nonred")]
        self.commands = []
        self.instance = {"hostname":"fixture", "serverUuid":"00000000-0000-0000-0000-000000000001"}

    def tearDown(self):
        self.directory.cleanup()

    def invoke(self, operation, mysql=None):
        """所有外部输入和权限接口均替换为内存桩，记录真正准备执行的SQL。"""
        def execute(sql):
            self.commands.append(sql)
            if sql == "SELECT @@hostname,@@server_uuid;": return "fixture\t"+self.instance["serverUuid"]
            return "0" if sql.startswith("SELECT ") else ""
        captured = io.StringIO()
        with patch.object(subject, "ownership_path", return_value=self.state), patch.object(subject, "verify_server"), patch.object(subject, "mysql", side_effect=mysql or execute), patch("sys.stdin", io.StringIO(json.dumps({"runId": self.run, "operation": operation, "password": self.password}))), contextlib.redirect_stdout(captured):
            subject.main()
        return captured.getvalue()

    def test_wrong_server_name_stops_before_any_docker_command(self):
        """其他服务器即便可访问，也不可运行该开发验收工具。"""
        with patch.object(subject.subprocess, "check_output", return_value="Production-Server\n") as call:
            with self.assertRaises(RuntimeError): subject.verify_server()
            self.assertEqual(1, call.call_count)

    def test_wrong_server_address_is_rejected(self):
        """同名主机但固定地址不符时，拒绝后续所有操作。"""
        with patch.object(subject.subprocess, "check_output", side_effect=["Server-200\n", "inet 192.168.31.201/24"]):
            with self.assertRaises(RuntimeError): subject.verify_server()

    def test_existing_random_database_is_never_reused_or_dropped(self):
        """碰撞发生前没有获得所有权，清理不能依据随机命名猜测DROP。"""
        with self.assertRaises(RuntimeError): self.invoke("create", lambda sql: "fixture\t"+self.instance["serverUuid"] if sql == "SELECT @@hostname,@@server_uuid;" else "1")
        self.assertEqual([], json.loads(self.state.read_text())["databases"])
        self.invoke("cleanup")
        self.assertFalse(any(not sql.startswith("SELECT ") for sql in self.commands))

    def test_foreign_object_in_owner_record_blocks_cleanup(self):
        """被篡改的清单包含其他库时，整批停止而非清理合法子集。"""
        self.state.write_text(json.dumps({"runId": self.run, "databases": ["existing_business"], "user": None, "instance": self.instance}))
        with self.assertRaises(RuntimeError): self.invoke("cleanup")
        self.assertTrue(self.state.exists()); self.assertFalse(any(not sql.startswith("SELECT ") for sql in self.commands))

    def test_partial_create_only_cleans_successfully_created_database(self):
        """第二个CREATE失败后，第一个库有清单证明，第二个和其他库没有。"""
        def execute(sql):
            self.commands.append(sql)
            if sql.startswith("CREATE DATABASE") and self.databases[1] in sql: raise RuntimeError("模拟创建中断")
            if sql == "SELECT @@hostname,@@server_uuid;": return "fixture\t"+self.instance["serverUuid"]
            return "0" if sql.startswith("SELECT ") else ""
        with self.assertRaises(RuntimeError): self.invoke("create", execute)
        self.assertEqual([self.databases[0]], json.loads(self.state.read_text())["databases"])
        self.commands.clear(); self.invoke("cleanup")
        self.assertEqual([f"DROP DATABASE `{self.databases[0]}`;"], [sql for sql in self.commands if not sql.startswith("SELECT ")])

    def test_success_uses_only_four_database_grants_and_removes_owner_record(self):
        """临时账号权限绑定4库，输出和所有权清单都不含密码。"""
        result = self.invoke("create")
        self.assertNotIn(self.password, result); self.assertNotIn(self.password, self.state.read_text())
        grants = [sql for sql in self.commands if sql.startswith("GRANT ")]
        self.assertEqual(4, len(grants)); self.assertTrue(all("ON `metarill_fresh_" in sql and "*.*" not in sql for sql in grants))
        self.commands.clear(); result = self.invoke("cleanup")
        self.assertTrue(json.loads(result)["cleaned"]); self.assertFalse(self.state.exists())
        writes = [sql for sql in self.commands if not sql.startswith("SELECT ")]; self.assertEqual(5, len(writes)); self.assertTrue(writes[0].startswith("DROP USER "))


# 新增实例pin及独立清理反例，保留上方原有创建中断/碰撞覆盖。
fresh = subject

class FreshLeaseTests(unittest.TestCase):
    """拒绝错误服务器、实例和所有权，成功清理必须独立确认消失。"""
    def test_wrong_server_or_address_never_executes_docker(self):
        """主机或地址任意不符都必须在Docker命令之前失败。"""
        for hostname, address in (("other", "inet 192.168.31.200/24"), ("Server-200", "inet 192.168.31.201/24")):
            with self.subTest(hostname=hostname, address=address), mock.patch.object(fresh.subprocess, "check_output", side_effect=[hostname, address]), mock.patch.object(fresh.subprocess, "run") as run:
                with self.assertRaises(RuntimeError):
                    fresh.mysql("SELECT 1;")
                run.assert_not_called()

    def test_mysql_checks_server_and_uses_only_remote_local_socket(self):
        """只在已核验服务器的unix socket执行固定容器命令，SQL通过标准输入。"""
        with mock.patch.object(fresh.subprocess, "check_output", side_effect=["Server-200", "inet 192.168.31.200/24"]), mock.patch.object(fresh.subprocess, "run", return_value=mock.Mock(returncode=0, stdout="ok\n")) as run:
            self.assertEqual("ok", fresh.mysql("SELECT 1;"))
            self.assertEqual(["docker", "-H", "unix:///var/run/docker.sock", "exec", "-i", "yinzon-mysql"], run.call_args.args[0][:6])
            self.assertEqual("SELECT 1;", run.call_args.kwargs["input"])

    def invoke_cleanup(self, mutation=None, residue=None):
        """只在临时目录构造租约，保存执行序列，绝不访问真实服务器。"""
        run = "a" * 16
        names = [f"metarill_fresh_{run}_{kind}" for kind in ("platform", "business", "package", "nonred")]
        fingerprint = {"hostname": "fixture", "serverUuid": "00000000-0000-0000-0000-000000000001"}
        record = {"runId": run, "databases": names.copy(), "user": "mtf_" + run, "instance": fingerprint.copy()}
        if mutation:
            mutation(record)
        calls = []
        def sql(value):
            calls.append(value)
            if value == "SELECT @@hostname,@@server_uuid;":
                return fingerprint["hostname"] + "\t" + fingerprint["serverUuid"]
            if value.startswith("SELECT COUNT(*)"):
                return "1" if residue and residue in value else "0"
            return ""
        with tempfile.TemporaryDirectory(prefix="metarill-fresh-lease-offline-") as directory:
            path = pathlib.Path(directory) / "lease.json"
            path.write_text(json.dumps(record), encoding="utf-8")
            before = path.read_bytes()
            with mock.patch.object(fresh, "verify_server"), mock.patch.object(fresh, "mysql", side_effect=sql), mock.patch.object(fresh, "ownership_path", return_value=path), mock.patch.object(fresh.sys, "stdin", io.StringIO(json.dumps({"operation": "cleanup", "runId": run}))), contextlib.redirect_stdout(io.StringIO()) as output:
                try:
                    fresh.main()
                    error = None
                except RuntimeError as caught:
                    error = caught
            return calls, error, path.exists(), path.read_bytes() if path.exists() else None, before, output.getvalue(), names

    def test_changed_instance_or_unknown_owned_name_prevents_all_drop(self):
        """错误实例和混入既有库名时不执行任何DROP，原所有权记录保持。"""
        for mutation in (lambda r: r["instance"].update(serverUuid="00000000-0000-0000-0000-000000000002"), lambda r: r["databases"].append("metarill_business_test")):
            calls, error, exists, after, before, _, _ = self.invoke_cleanup(mutation)
            self.assertIsNotNone(error)
            self.assertTrue(exists)
            self.assertEqual(before, after)
            self.assertFalse(any(query.startswith("DROP") for query in calls))

    def test_complete_cleanup_checks_exact_four_databases_and_account(self):
        """成功收据只能来自精确自有对象的DROP及独立零检查。"""
        calls, error, exists, _, _, output, names = self.invoke_cleanup()
        self.assertIsNone(error)
        self.assertFalse(exists)
        self.assertEqual(4, sum(query.startswith("DROP DATABASE") for query in calls))
        self.assertEqual(1, sum(query.startswith("DROP USER") for query in calls))
        for name in names:
            self.assertTrue(any(query.startswith("SELECT COUNT(*)") and name in query for query in calls))
        result = json.loads(output)
        self.assertTrue(result["independentAbsenceVerified"])
        self.assertEqual(4, result["databaseCount"])

    def test_post_drop_residue_preserves_lease_without_success_receipt(self):
        """独立读仍见对象时不能报告成功或删除租约。"""
        _, error, exists, _, _, output, _ = self.invoke_cleanup(residue="_nonred")
        self.assertIsNotNone(error)
        self.assertTrue(exists)
        self.assertEqual("", output)


    def test_partial_lease_does_not_probe_or_drop_unowned_collision(self):
        """部分创建租约只清理有记录的对象，不要求未拥有的同名库消失。"""
        def partial(record):
            record["databases"] = record["databases"][:2]
            record["user"] = None
        calls, error, exists, _, _, output, _ = self.invoke_cleanup(partial, residue="_nonred")
        self.assertIsNone(error)
        self.assertFalse(exists)
        self.assertEqual(2, json.loads(output)["databaseCount"])
        self.assertFalse(any("_nonred" in query for query in calls))


if __name__ == "__main__":
    unittest.main()
