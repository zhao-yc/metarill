"""独占 TLS 资源的拒绝边界与精确清理验证；不运行 SSH 或 Docker。"""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import server200_mysql_tls as tls


class MySqlTlsResourcesTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.addCleanup(mock.patch.stopall)
        self.base = Path(self.directory.name)
        mock.patch.object(tls, "BASE", self.base).start()
        mock.patch.object(tls.os, "geteuid", return_value=self.base.stat().st_uid, create=True).start()
        self.verify = mock.patch.object(tls, "verify_server").start()
        self.inspect = mock.patch.object(tls, "inspect", return_value=None).start()
        self.docker = mock.patch.object(tls, "docker").start()
        self.scope = dict(runId="a" * 16, ownerToken="b" * 64, image=tls.IMAGE, port=28630)
        self.root = tls.validate(self.scope)

    def prepared(self):
        self.root.mkdir(mode=0o700)
        for name in tls.FILES:
            (self.root / name).write_bytes(b"synthetic-" + name.encode())
        state = dict(runId=self.scope["runId"], ownerHash=tls.owner(self.scope),
                     image=tls.IMAGE, port=self.scope["port"],
                     files={name: hashlib.sha256((self.root / name).read_bytes()).hexdigest() for name in tls.FILES})
        (self.root / "state.json").write_text(json.dumps(state))
        return state

    def owned(self, identity):
        return {"Id": identity, "Image": tls.IMAGE,
                "Config": {"Labels": {tls.LABEL + "run": self.scope["runId"], tls.LABEL + "owner": tls.owner(self.scope)}},
                "Labels": {tls.LABEL + "run": self.scope["runId"], tls.LABEL + "owner": tls.owner(self.scope)}}

    def test_invalid_scope_does_not_touch_server(self):
        """错误编号、镜像、归属和端口先于任何外部调用拒绝。"""
        for change in ({"runId": "../x"}, {"ownerToken": "x"}, {"image": "mysql:latest"}, {"port": 3306}, {"port": True}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                tls.manage({**self.scope, **change}, "cleanup")
        self.verify.assert_not_called()
        self.docker.assert_not_called()

    @unittest.skipIf(os.name == "nt", "0700 与服务端 uid 检查只在 Linux 执行。")
    def test_material_changes_or_extra_files_prevent_cleanup(self):
        """清理不接受被替换材料或夹带文件。"""
        self.prepared()
        (self.root / "extra").write_bytes(b"foreign")
        with self.assertRaises(RuntimeError):
            tls.manage(self.scope, "cleanup")
        (self.root / "extra").unlink()
        (self.root / "ca.pem").write_bytes(b"changed")
        with self.assertRaises(RuntimeError):
            tls.manage(self.scope, "cleanup")
        self.inspect.assert_not_called()
        self.docker.assert_not_called()

    @unittest.skipIf(os.name == "nt", "0700 与服务端 uid 检查只在 Linux 执行。")
    def test_owner_mismatch_prevents_resource_queries(self):
        """错误归属不能获取证书或删除容器。"""
        self.prepared()
        with self.assertRaises(RuntimeError):
            tls.manage({**self.scope, "ownerToken": "c" * 64}, "materials")
        self.inspect.assert_not_called()
        self.docker.assert_not_called()

    @unittest.skipIf(os.name == "nt", "0700 与服务端 uid 检查只在 Linux 执行。")
    def test_foreign_container_is_not_deleted(self):
        """Docker 标签不匹配时保留目录与对象。"""
        self.prepared()
        item = self.owned("d" * 64)
        item["Config"]["Labels"][tls.LABEL + "owner"] = "foreign"
        self.inspect.return_value = item
        with self.assertRaises(RuntimeError):
            tls.manage(self.scope, "cleanup")
        self.docker.assert_not_called()
        self.assertTrue(self.root.exists())

    @unittest.skipIf(os.name == "nt", "0700 与服务端 uid 检查只在 Linux 执行。")
    def test_cleanup_uses_owned_ids_and_rechecks_absence(self):
        """只删除已核验 ID，逐项重新查询，最后移除本轮材料。"""
        self.prepared()
        self.inspect.side_effect = [self.owned("d" * 64), None, self.owned("e" * 64), None]
        self.assertTrue(tls.manage(self.scope, "cleanup")["cleaned"])
        self.assertEqual([
            mock.call("container", "rm", "--force", "--volumes", "d" * 64),
            mock.call("network", "rm", "e" * 64)], self.docker.call_args_list)
        self.assertFalse(self.root.exists())

    @unittest.skipIf(os.name == "nt", "0700 与服务端 uid 检查只在 Linux 执行。")
    def test_create_binds_only_known_files_and_private_lan_port(self):
        """私有根本身不挂入容器，密钥按固定文件只读挂载且口令不进参数。"""
        self.prepared()
        self.inspect.side_effect = [None, None, self.owned("d" * 64)]
        self.docker.side_effect = ["e" * 64, "d" * 64, "d" * 64]
        self.assertTrue(tls.manage(self.scope, "create")["created"])
        command = self.docker.call_args_list[1].args
        self.assertIn("192.168.31.200:28630:3306", command)
        self.assertIn("--require-secure-transport=ON", command)
        self.assertIn("com.docker.network.bridge.enable_icc=false", self.docker.call_args_list[0].args)
        self.assertNotIn("--internal", self.docker.call_args_list[0].args)
        self.assertNotIn(f"type=bind,src={self.root},dst=/run/metarill-tls,readonly", command)
        self.assertNotIn(self.scope["ownerToken"], " ".join(command))
        self.assertFalse(any("synthetic-root-password" in arg for arg in command))

    @unittest.skipIf(os.name == "nt", "服务端拒绝符号链接；Windows 不创建需要特权的链接。")
    def test_certificate_symlink_is_rejected(self):
        """材料目录内的链接也不能越过精确范围。"""
        self.prepared()
        (self.root / "ca.pem").unlink()
        (self.root / "ca.pem").symlink_to(self.base / "outside")
        with self.assertRaises(RuntimeError):
            tls.manage(self.scope, "cleanup")
        self.docker.assert_not_called()

    def test_unknown_operation_does_not_prepare_resources(self):
        """操作名不在白名单时不能先触碰远端或读取材料。"""
        with self.assertRaises(ValueError):
            tls.manage(self.scope, "foreign")
        self.docker.assert_not_called()
        self.assertFalse(self.root.exists())


if __name__ == "__main__":
    unittest.main()
