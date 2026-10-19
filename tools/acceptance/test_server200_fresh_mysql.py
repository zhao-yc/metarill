"""离线验证随机库管理的服务器与所有权保护，不调用 Docker/MySQL。"""
import contextlib
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

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
        self.databases = [f"metarill_fresh_{self.run}_{kind}" for kind in ("platform", "business", "package")]
        self.commands = []

    def tearDown(self):
        self.directory.cleanup()

    def invoke(self, operation, mysql=None):
        """所有外部输入和权限接口均替换为内存桩，记录真正准备执行的SQL。"""
        def execute(sql):
            self.commands.append(sql)
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
        with self.assertRaises(RuntimeError): self.invoke("create", lambda sql: "1")
        self.assertEqual([], json.loads(self.state.read_text())["databases"])
        self.invoke("cleanup")
        self.assertEqual([], self.commands)

    def test_foreign_object_in_owner_record_blocks_cleanup(self):
        """被篡改的清单包含其他库时，整批停止而非清理合法子集。"""
        self.state.write_text(json.dumps({"runId": self.run, "databases": ["existing_business"], "user": None}))
        with self.assertRaises(RuntimeError): self.invoke("cleanup")
        self.assertTrue(self.state.exists()); self.assertEqual([], self.commands)

    def test_partial_create_only_cleans_successfully_created_database(self):
        """第二个CREATE失败后，第一个库有清单证明，第二个和其他库没有。"""
        def execute(sql):
            self.commands.append(sql)
            if sql.startswith("CREATE DATABASE") and self.databases[1] in sql: raise RuntimeError("模拟创建中断")
            return "0" if sql.startswith("SELECT ") else ""
        with self.assertRaises(RuntimeError): self.invoke("create", execute)
        self.assertEqual([self.databases[0]], json.loads(self.state.read_text())["databases"])
        self.commands.clear(); self.invoke("cleanup")
        self.assertEqual([f"DROP DATABASE `{self.databases[0]}`;"], self.commands)

    def test_success_uses_only_three_database_grants_and_removes_owner_record(self):
        """临时账号权限绑定3库，输出和所有权清单都不含密码。"""
        result = self.invoke("create")
        self.assertNotIn(self.password, result); self.assertNotIn(self.password, self.state.read_text())
        grants = [sql for sql in self.commands if sql.startswith("GRANT ")]
        self.assertEqual(3, len(grants)); self.assertTrue(all("ON `metarill_fresh_" in sql and "*.*" not in sql for sql in grants))
        self.commands.clear(); result = self.invoke("cleanup")
        self.assertTrue(json.loads(result)["cleaned"]); self.assertFalse(self.state.exists())
        self.assertEqual(4, len(self.commands)); self.assertTrue(self.commands[0].startswith("DROP USER "))


if __name__ == "__main__":
    unittest.main()
