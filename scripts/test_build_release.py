"""发行工具的外层摘要、内置清单及跨平台解包边界。"""
import argparse
import importlib.util
import io
import json
import stat
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile, ZipInfo

SPEC = importlib.util.spec_from_file_location("build_release", Path(__file__).with_name("build-release.py"))
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def fixture(self, directory, role, commit="a" * 40):
        """构造最小的版本化发行布局，不调用 SDK。"""
        source, host = directory / f"{role}-source", directory / f"{role}-host"
        dll = release.ROLES[role]
        files = release.METADATA[role] | {"LICENSE", f"host/{dll}.dll", f"host/{dll}.runtimeconfig.json"}
        for name in files:
            path = host / name[5:] if name.startswith("host/") else source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture")
        (host / f"{dll}.deps.json").write_text(
            json.dumps({"runtimeTarget": {"name": release.TARGET}}), encoding="utf-8")
        path = directory / f"{role}-linux-x64-{commit[:8]}.zip"
        release.write_package(path, role, commit, source, release.source_hashes(source), {},
                              host, release.source_hashes(host))
        return path

    def sums(self, directory, *packages):
        (directory / "SHA256SUMS").write_text(
            "".join(f"{release.file_digest(path)}  {path.name}\n" for path in packages), encoding="ascii")

    def test_verifies_both_roles_and_rejects_outer_tampering(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            platform, full = (self.fixture(directory, role) for role in release.ROLES)
            self.sums(directory, platform, full)
            self.assertEqual(set(release.verify_release(directory)), set(release.ROLES))
            with full.open("ab") as archive:
                archive.write(b"changed")
            with self.assertRaisesRegex(ValueError, "归档摘要不匹配"):
                release.verify_release(directory)

    def test_rejects_mixed_commits(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            platform = self.fixture(directory, "platform")
            full = self.fixture(directory, "full", "b" * 40)
            self.sums(directory, platform, full)
            with self.assertRaisesRegex(ValueError, "不同 Git 提交"):
                release.verify_release(directory)

    def test_rejects_updated_outer_hash_when_payload_no_longer_matches_manifest(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            platform = self.fixture(directory, "platform")
            full = self.fixture(directory, "full")
            with ZipFile(full) as archive:
                content = {name: archive.read(name) for name in archive.namelist()}
            content["LICENSE"] = b"replaced"
            with ZipFile(full, "w") as archive:
                for name, raw in content.items():
                    archive.writestr(name, raw)
            self.sums(directory, platform, full)
            with self.assertRaisesRegex(ValueError, "文件摘要不匹配"):
                release.verify_release(directory)

    def test_rejects_private_paths_and_cross_platform_aliases(self):
        for name in ("../escape", "/absolute", "folder\\escape", "a/.env", "a/PRIVATE-SETTINGS.JSON",
                     "a/NUL.txt", "a/name. ", "a/./b", "a//b"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                release.safe_path(name)

    def test_rejects_link_case_collision_and_file_parent(self):
        link = ZipInfo("link")
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        for entries in ((link,), ("File", "file"), ("parent", "parent/leaf"), ("Parent/a", "parent/b")):
            with self.subTest(entries=entries), io.BytesIO() as data:
                with ZipFile(data, "w") as archive:
                    for entry in entries:
                        archive.writestr(entry, b"fixture")
                with ZipFile(data) as archive, self.assertRaises(ValueError):
                    release.zip_files(archive)

    def test_rejects_duplicate_manifest_properties_and_occupied_output(self):
        with self.assertRaisesRegex(ValueError, "重复属性"):
            release.read_json(b'{"commit":"a","commit":"b"}')
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaisesRegex(ValueError, "输出目录必须为新目录"):
                release.build_release(argparse.Namespace(output=Path(tmp)))
