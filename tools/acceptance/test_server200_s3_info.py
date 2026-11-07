"""核对只读S3检查器的主机边界与凭据脱敏，不连接SSH、Docker或S3。"""
import importlib.util
import json
import pathlib
import unittest
from unittest.mock import patch

path = pathlib.Path(__file__).with_name("server200-s3-info.py")
spec = importlib.util.spec_from_file_location("metarill_s3_info", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ServerS3InfoTests(unittest.TestCase):
    """所有服务状态均为明确合成值，不能作为真实授权或云端验收。"""
    def container(self):
        """模拟预定容器的安全外形及两个合成凭据。"""
        return {"Id": "a" * 64, "Image": "sha256:" + "b" * 64,
                "State": {"Status": "running"},
                "Config": {"Image": "yinzon-seaweedfs:test",
                           "Env": ["AWS_ACCESS_KEY_ID=synthetic-access",
                                   "AWS_SECRET_ACCESS_KEY=synthetic-secret", "OTHER_SECRET=must-stay-private"]},
                "NetworkSettings": {"Ports": {"8333/tcp": [{"HostIp": "192.168.31.200", "HostPort": "8333"}]}}}

    def invoke(self, request, container=None, host="Server-200", address=True):
        """记录实际进程参数，防止测试仅比较近似常量。"""
        calls = []
        def output(args, **kwargs):
            calls.append(args)
            if args[0] == "ip":
                return "inet 192.168.31.200/24" if address else "inet 127.0.0.1/8"
            return json.dumps([container or self.container()])
        with patch.object(module.socket, "gethostname", return_value=host), patch.object(module.subprocess, "check_output", side_effect=output):
            return module.handle(request), calls

    def test_inspection_never_returns_credential_values(self):
        result, calls = self.invoke({"action": "inspect"})
        encoded = json.dumps(result)
        for secret in ["synthetic-access", "synthetic-secret", "must-stay-private"]:
            self.assertNotIn(secret, encoded)
        self.assertEqual(calls[-1], ["docker", "-H", "unix:///var/run/docker.sock", "inspect", "yinzon-storage"])

    def test_credentials_require_exact_service_fingerprint(self):
        result, _ = self.invoke({"action": "credentials", "expected": {"containerId": "a" * 64, "imageId": "sha256:" + "b" * 64}})
        self.assertEqual(result["accessKey"], "synthetic-access")
        self.assertEqual(result["secretKey"], "synthetic-secret")
        self.assertNotIn("OTHER_SECRET", json.dumps(result))

    def test_bad_requests_stop_before_any_process(self):
        for value in [{"action": "delete"}, {"action": "credentials"}, {"action": "inspect", "sql": "bad"}]:
            with self.subTest(value=value), patch.object(module.subprocess, "check_output", side_effect=AssertionError("不应执行进程")):
                with self.assertRaises(RuntimeError):
                    module.handle(value)

    def test_wrong_server_stops_before_docker(self):
        for host, address in [("another-host", True), ("Server-200", False)]:
            with self.subTest(host=host, address=address):
                with self.assertRaises(RuntimeError):
                    self.invoke({"action": "inspect"}, host=host, address=address)

    def test_drifted_service_or_port_is_rejected(self):
        with self.assertRaises(RuntimeError):
            self.invoke({"action": "credentials", "expected": {"containerId": "c" * 64, "imageId": "sha256:" + "b" * 64}})
        container = self.container()
        container["NetworkSettings"]["Ports"]["8333/tcp"] = [{"HostIp": "127.0.0.1", "HostPort": "8333"}]
        with self.assertRaises(RuntimeError):
            self.invoke({"action": "inspect"}, container=container)

    def test_duplicate_credentials_are_not_silently_selected(self):
        container = self.container()
        container["Config"]["Env"].append("AWS_SECRET_ACCESS_KEY=another-secret")
        with self.assertRaises(RuntimeError):
            self.invoke({"action": "inspect"}, container=container)


if __name__ == "__main__":
    unittest.main()
