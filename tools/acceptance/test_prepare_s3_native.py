"""离线验证S3准备器的来源、路径、只读服务回执及真实TRX检查；不访问网络。"""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location("prepare_s3_native", Path(__file__).with_name("prepare-s3-native.py"))
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class PreparationTests(unittest.TestCase):
    def scope_reply(self):
        """仅合成非敏感服务指纹。"""
        return dict(host="Server-200", address="192.168.31.200", containerId="a" * 64, imageId=module.IMAGE,
                    imageName="yinzon-seaweedfs:4.48-object-acl-20261004", endpoint="http://192.168.31.200:8333",
                    credentialNames=["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"])

    def test_invalid_run_id_precedes_git_and_ssh(self):
        with patch.object(module.subprocess, "run", side_effect=AssertionError("不得执行外部进程")):
            with self.assertRaises(RuntimeError):
                module.prepare(Path("source"), Path("evidence"), Path("dotnet"), "../wrong")

    def test_dirty_source_precedes_head_archive_and_network(self):
        with patch.object(module.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "?? owned.cs")):
            with patch.object(module.subprocess, "check_output", side_effect=AssertionError("不得固定不干净来源")):
                with self.assertRaises(RuntimeError):
                    module.clean_source(Path("source"))

    def test_clean_source_requires_complete_sha(self):
        with patch.object(module.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "")):
            for value in ["main", "a" * 39, "A" * 40, "a" * 40]:
                with self.subTest(value=value), patch.object(module.subprocess, "check_output", return_value=value):
                    if value == "a" * 40:
                        self.assertEqual(module.clean_source(Path("source")), value)
                    else:
                        with self.assertRaises(RuntimeError):
                            module.clean_source(Path("source"))

    def archive(self, root, entries):
        """写真实zip样本，校验拒绝发生在解包前。"""
        path = root / "input.zip"
        with zipfile.ZipFile(path, "w") as stream:
            for entry, value in entries:
                if isinstance(entry, str) and "\\" in entry:
                    info = zipfile.ZipInfo("raw")
                    info.filename = entry
                    entry = info
                stream.writestr(entry, value)
        return path

    def test_archive_rejects_all_unsafe_entries_before_any_extraction(self):
        for bad in ["../escape", "/absolute", "C:/disk", "dir\\escape", "dir/../escape", "dir//empty"]:
            with self.subTest(bad=bad), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                archive = self.archive(root, [("valid.cs", "sample"), (bad, "unsafe")])
                with self.assertRaises(RuntimeError):
                    module.safe_extract(archive, root / "out")
                self.assertFalse((root / "out").exists())

    def test_archive_rejects_case_collision(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = self.archive(root, [("Name.cs", "a"), ("name.cs", "b")])
            with self.assertRaises(RuntimeError):
                module.safe_extract(archive, root / "out")

    def test_archive_rejects_symbolic_link(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            link = zipfile.ZipInfo("link")
            link.create_system = 3
            link.external_attr = 0o120777 << 16
            archive = self.archive(root, [(link, "/outside")])
            with self.assertRaises(RuntimeError):
                module.safe_extract(archive, root / "out")

    def test_valid_archive_roundtrips_source_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = self.archive(root, [("backend/", ""), ("backend/source.cs", "// 中文源文件")])
            module.safe_extract(archive, root / "out")
            self.assertEqual((root / "out/backend/source.cs").read_text(encoding="utf-8"), "// 中文源文件")

    def test_server_inspection_uses_only_inspect_with_strict_host_checking(self):
        with tempfile.TemporaryDirectory() as temp:
            script = Path(temp) / "inspector.py"
            script.write_bytes(b"# fixed synthetic script")
            reply = subprocess.CompletedProcess([], 0, json.dumps(self.scope_reply()).encode(), b"")
            with patch.object(module.subprocess, "run", return_value=reply) as run:
                scope = module.server_scope(script, "c" * 32)
            args = run.call_args.args[0]
            self.assertIn("StrictHostKeyChecking=yes", args)
            self.assertEqual(json.loads(module.base64.b64decode(args[-1])), {"action": "inspect"})
            self.assertEqual(scope["bucket"], "metarill-native-" + "c" * 32)
            self.assertNotIn("accessKey", scope)

    def test_server_secret_extra_wrong_endpoint_or_image_is_rejected(self):
        for change in [dict(accessKey="must-not-be-returned"), dict(endpoint="http://another:8333"),
                       dict(imageId="sha256:" + "b" * 64), dict(containerId="bad")]:
            with self.subTest(change=change), tempfile.TemporaryDirectory() as temp:
                script = Path(temp) / "inspector.py"
                script.write_bytes(b"# synthetic")
                data = self.scope_reply() | change
                reply = subprocess.CompletedProcess([], 0, json.dumps(data).encode(), b"")
                with patch.object(module.subprocess, "run", return_value=reply):
                    with self.assertRaises(RuntimeError):
                        module.server_scope(script, "c" * 32)

    def test_trx_counts_require_actual_counters(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "result.trx"
            path.write_text('<TestRun xmlns="urn:trx"><ResultSummary><Counters total="1" passed="0" failed="1" executed="1"/></ResultSummary></TestRun>')
            self.assertEqual(module.result_counts(path), dict(total=1, passed=0, failed=1, executed=1))
            path.write_text("<TestRun/>")
            with self.assertRaises(RuntimeError):
                module.result_counts(path)

    def test_evidence_json_cannot_overwrite_existing_lease(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "lease.json"
            module.write_json(path, {"authorizationStatus": "pending"})
            with self.assertRaises(FileExistsError):
                module.write_json(path, {"authorizationStatus": "approved"})
            self.assertEqual(json.loads(path.read_text()), {"authorizationStatus": "pending"})


if __name__ == "__main__":
    unittest.main()
