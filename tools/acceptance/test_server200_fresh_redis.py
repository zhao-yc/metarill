"""隔离 Redis 的离线边界验证，不调用 Docker 或 SSH。"""
import hashlib
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "performance"))
import server200_fresh_redis as redis


class FreshRedisTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        base = Path(self.directory.name)
        self.addCleanup(mock.patch.stopall)
        mock.patch.object(redis, "BASE", base).start()
        mock.patch.object(redis.server.os, "geteuid", return_value=base.stat().st_uid, create=True).start()
        self.docker = mock.patch.object(redis.server, "docker").start()
        self.inspect = mock.patch.object(redis.server, "inspect", return_value=None).start()
        self.listener = mock.patch.object(redis.socket, "socket").start()
        self.scope = dict(purpose=redis.PURPOSE, runId="a" * 16, remotePort=28201,
                          image=redis.IMAGE, authorizationStatus="approved", leaseStatus="running",
                          ownerToken="b" * 64, passwordSha256="c" * 64)
        self.root = base / ("metarill-fresh-redis-" + self.scope["runId"])
        self.container = ["d" * 64, self.scope["runId"],
                          hashlib.sha256(self.scope["ownerToken"].encode()).hexdigest(), "fresh-bootstrap-redis"]

    def test_pending_create_refuses_before_touching_resources(self):
        """待授权、越界端口、其它镜像及结束租约均在任何资源操作前拒绝。"""
        for change in ({"authorizationStatus": "pending"}, {"remotePort": 6379},
                       {"image": "redis:latest"}, {"leaseStatus": "ended"}, {"runId": "../outside"}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                redis.manage({**self.scope, **change}, "create")
        self.inspect.assert_not_called()
        self.docker.assert_not_called()
        self.listener.assert_not_called()
        self.assertFalse(self.root.exists())

    def test_create_uses_loopback_and_does_not_store_raw_owner(self):
        """真实参数构造必须使用固定镜像、回环绑定、无持久化和已核验容器 ID。"""
        self.inspect.side_effect = [None, self.container]
        self.docker.side_effect = [redis.IMAGE, self.container[0], self.container[0]]
        result = redis.manage(self.scope, "create")
        self.assertTrue(result["created"])
        create = self.docker.call_args_list[1].args
        self.assertIn("127.0.0.1:28201:6379", create)
        self.assertIn("--read-only", create)
        self.assertEqual(("container", "start", self.container[0]), self.docker.call_args_list[2].args)
        payload = (self.root / redis.CONFIG_NAME).read_bytes()
        self.assertIn(b"save \"\"\nappendonly no\n", payload)
        self.assertIn(b"user default reset on #", payload)
        self.assertNotIn(self.scope["ownerToken"].encode(), payload)
        self.assertNotIn(self.scope["ownerToken"], " ".join(create))

    def test_cleanup_rejects_foreign_container_or_changed_config(self):
        """任何归属或文件篡改都不能触发删除。"""
        self.root.mkdir()
        config = self.root / redis.CONFIG_NAME
        config.write_bytes(redis.redis_config(self.scope["passwordSha256"]))
        self.inspect.return_value = [*self.container[:2], "foreign", self.container[3]]
        with self.assertRaises(RuntimeError):
            redis.manage(self.scope, "cleanup")
        self.inspect.return_value = self.container
        config.write_bytes(b"changed")
        with self.assertRaises(RuntimeError):
            redis.manage(self.scope, "cleanup")
        self.docker.assert_not_called()
        self.assertTrue(config.exists())

    def test_cleanup_removes_owned_container_and_only_known_file(self):
        """按归属清理，并以新查询及目录核对确认不存在。"""
        self.root.mkdir()
        (self.root / redis.CONFIG_NAME).write_bytes(redis.redis_config(self.scope["passwordSha256"]))
        self.inspect.side_effect = [self.container, None]
        proof = redis.manage(self.scope, "cleanup")
        self.docker.assert_called_once_with("container", "rm", "--force", "--volumes", self.container[0])
        self.assertTrue(proof["containerAbsent"] and proof["directoryAbsent"])
        self.assertFalse(self.root.exists())
