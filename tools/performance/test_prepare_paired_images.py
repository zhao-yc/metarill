"""固定镜像上下文的离线回归；不运行Docker、网络、.NET或Java进程。"""
import copy
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest
import zipfile
from unittest import mock

spec = importlib.util.spec_from_file_location("prepare_paired_images", pathlib.Path(__file__).with_name("prepare_paired_images.py"))
subject = importlib.util.module_from_spec(spec); spec.loader.exec_module(subject)


class ImageContextTests(unittest.TestCase):
    """复制完整冻结产物、固定基础镜像、保护旧证据并拒绝危险输入。"""
    def setUp(self):
        """只制造本机微型文件夹，不把假二进制当作真实Linux运行证据。"""
        self.temporary = tempfile.TemporaryDirectory(); self.root = pathlib.Path(self.temporary.name)
        self.jar = self.root / "application.jar"; self.app = self.root / "bin"; self.app.mkdir()
        with zipfile.ZipFile(self.jar, "w") as archive:
            archive.writestr("META-INF/MANIFEST.MF", "Main-Class: org.springframework.boot.loader.launch.JarLauncher\nStart-Class: example.Application\n")
            archive.writestr("BOOT-INF/classes/example/Application.class", b"dummy")
        files = {"Application.Host.dll": b"MZdummy", "Application.Host.deps.json": b"{}", "Application.Host.runtimeconfig.json": json.dumps({"runtimeOptions": {"tfm": "net10.0", "frameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.0"}, {"name": "Microsoft.AspNetCore.App", "version": "10.0.0"}]}}).encode(),
                 "metadata/project.json": b"{}", "metadata/files.json": b"{}", "metadata/security.json": b"{}", "runtimes/linux-x64/native/libSkiaSharp.so": b"\x7fELF\x02\x01" + b"\x00" * 12 + b"\x3e\x00", "Application.Host.exe": b"MZ-windows-host"}
        for path, data in files.items():
            file = self.app / path; file.parent.mkdir(parents=True, exist_ok=True); file.write_bytes(data)
        self.settings = {"version": 1, "runId": "a" * 32,
                         "java": {"jar": str(self.jar), "sha256": subject.sha256(self.jar), "sourceCommit": "b" * 40, "runtimeImage": "docker.io/library/eclipse-temurin@sha256:" + "c" * 64},
                         "csharp": {"outputRoot": str(self.app), "sourceCommit": "d" * 40, "runtimeImage": "mcr.microsoft.com/dotnet/aspnet@sha256:" + "e" * 64, "files": subject.inventory(self.app)}}

    def tearDown(self):
        """测试框架只回收本轮创建的系统临时目录。"""
        self.temporary.cleanup()

    def test_complete_context_preserves_bytes_has_no_secrets_and_does_not_execute(self):
        """完整清单逐字复制，Linux以dotnet DLL启动而不是Windows apphost。"""
        output = self.root / "context"
        with mock.patch("subprocess.run", side_effect=AssertionError("不能执行进程")), mock.patch("urllib.request.urlopen", side_effect=AssertionError("不能联网")):
            receipt = subject.prepare(self.settings, output)
        self.assertEqual(receipt["csharpFiles"], 8)
        self.assertEqual(subject.inventory(output / "csharp/app"), self.settings["csharp"]["files"])
        self.assertEqual(subject.sha256(output / "java/application.jar"), self.settings["java"]["sha256"])
        for name in ("java", "csharp"):
            text = (output / name / "Dockerfile").read_text(encoding="utf-8")
            self.assertIn("FROM " + self.settings[name]["runtimeImage"], text)
            self.assertIn('metarill.performance.run="' + "a" * 32 + '"', text)
            self.assertIn("USER 10001:10001", text); self.assertNotIn("Password", text)
        self.assertIn('ENTRYPOINT ["dotnet", "/app/Application.Host.dll"]', (output / "csharp/Dockerfile").read_text(encoding="utf-8"))
        self.assertIn("SERVER_ADDRESS=0.0.0.0", (output / "java/Dockerfile").read_text(encoding="utf-8"))
        self.assertIn("SPRING_APPLICATION_NAME=metarill-perf-" + "a" * 32 + "-java", (output / "java/Dockerfile").read_text(encoding="utf-8"))
        self.assertFalse(receipt["dockerExecuted"]); self.assertFalse(receipt["runtimeAccepted"]); self.assertFalse(receipt["performanceAccepted"])
        for path, checksum in receipt["contextFiles"].items(): self.assertEqual(subject.sha256(output / path), checksum)

    def test_mismatched_missing_and_extra_input_rejected_before_output(self):
        """既有文件修改、丢失或增加未冻结文件都必须拒绝。"""
        for mode in ("changed", "missing", "extra"):
            with self.subTest(mode=mode):
                file = self.app / "Application.Host.dll"; original = file.read_bytes()
                if mode == "changed": file.write_bytes(b"changed")
                elif mode == "missing": file.unlink()
                else: (self.app / "Metarill.Unfrozen.dll").write_bytes(b"extra")
                output = self.root / mode
                with self.assertRaises(subject.ContractError): subject.prepare(self.settings, output)
                self.assertFalse(output.exists())
                file.write_bytes(original)
                if mode == "extra": (self.app / "Metarill.Unfrozen.dll").unlink()

    def test_changed_jar_rejected_before_copy(self):
        """JAR不能靠源码标签或文件名冒充固定构建产物。"""
        self.jar.write_bytes(b"other")
        with self.assertRaises(subject.ContractError): subject.prepare(self.settings, self.root / "out")
        self.assertFalse((self.root / "out").exists())

    def test_private_config_and_path_escape_rejected_even_with_matching_hash(self):
        """私有配置、Dockerfile和父级路径不能因列入散列表而进入镜像。"""
        for path in ("../escape.dll", "/absolute.dll", "C:/private.dll", "runtimes/../escape.dll", "runtimes//linux-x64/native/lib.so", "metadata/../../escape.dll", "appsettings.local.json", "Dockerfile", "private.key", "metadata/credentials.json", "back\\slash.dll"):
            value = copy.deepcopy(self.settings); value["csharp"]["files"][path] = "f" * 64
            with self.subTest(path=path), self.assertRaises(subject.ContractError): subject.prepare(value, self.root / "out")
        self.assertFalse((self.root / "out").exists())

    def test_missing_boot_launcher_rejected_even_when_hash_matches(self):
        """只有类文件的普通ZIP不能冒充已可执行的Spring Boot JAR。"""
        with zipfile.ZipFile(self.jar, "w") as archive:
            archive.writestr("META-INF/MANIFEST.MF", "Start-Class: example.Application\n")
            archive.writestr("BOOT-INF/classes/example/Application.class", b"dummy")
        self.settings["java"]["sha256"] = subject.sha256(self.jar)
        with self.assertRaises(subject.ContractError): subject.prepare(self.settings, self.root / "out")
        self.assertFalse((self.root / "out").exists())

    def test_reparse_input_rejected_without_traversal(self):
        """在支持junction的平台按真实stat位拒绝，不依赖测试机创建链接的权限。"""
        original = pathlib.Path.lstat; root = self.app
        def metadata(path, *args, **kwargs):
            """仅将本轮根目录标为reparse对象，不读取其它目录。"""
            value = original(path, *args, **kwargs)
            if path == root:
                class Reparse:
                    st_mode = value.st_mode
                    st_file_attributes = 0x400
                return Reparse()
            return value
        with mock.patch.object(pathlib.Path, "lstat", metadata):
            with self.assertRaises(subject.ContractError): subject.prepare(self.settings, self.root / "out")
        self.assertFalse((self.root / "out").exists())

    def test_unknown_image_tag_registry_or_injection_rejected(self):
        """FROM只能是固定官方仓库digest，不接受可变tag或注入。"""
        for value in ("eclipse-temurin:21", "docker.io/library/eclipse-temurin:latest", "other.example/runtime@sha256:" + "a" * 64, self.settings["java"]["runtimeImage"] + "\nRUN echo bad"):
            settings = copy.deepcopy(self.settings); settings["java"]["runtimeImage"] = value
            with self.subTest(value=value), self.assertRaises(subject.ContractError): subject.prepare(settings, self.root / "out")

    def test_existing_output_and_nested_input_are_not_overwritten(self):
        """旧报告和输入目录不能作为新构建上下文覆盖。"""
        output = self.root / "old"; output.mkdir(); marker = output / "intent.json"; marker.write_text("保留", encoding="utf-8")
        for target in (output, self.app / "nested"):
            with self.subTest(target=target), self.assertRaises(subject.ContractError): subject.prepare(self.settings, target)
        self.assertEqual(marker.read_text(encoding="utf-8"), "保留")
        self.assertFalse((self.app / "nested").exists())

    def test_copy_race_preserves_incomplete_intent_without_success_receipt(self):
        """复制后校验不一致时保留首因，不能写完成回执或自动重试。"""
        output = self.root / "race"; original = subject.shutil.copyfile; calls = []
        def copy(source, destination):
            """在唯一被测副本注入损坏，不改变其它测试源文件。"""
            calls.append(destination); original(source, destination)
            if pathlib.Path(destination).name == "Application.Host.dll": pathlib.Path(destination).write_bytes(b"broken")
        with mock.patch.object(subject.shutil, "copyfile", side_effect=copy):
            with self.assertRaises(subject.ContractError): subject.prepare(self.settings, output)
        self.assertTrue((output / "intent.json").exists()); self.assertFalse((output / "context-receipt.json").exists())
        self.assertEqual(len(calls), 9)

    def test_linux_architecture_and_framework_must_match(self):
        """复制前核对真实文件头和共享框架声明，不能拿Windows库假装Linux。"""
        for path, value in (("runtimes/linux-x64/native/libSkiaSharp.so", b"MZ-not-elf"), ("Application.Host.runtimeconfig.json", b'{"runtimeOptions":{"tfm":"net8.0"}}')):
            file = self.app / path; original = file.read_bytes(); file.write_bytes(value)
            settings = copy.deepcopy(self.settings); settings["csharp"]["files"] = subject.inventory(self.app)
            with self.subTest(path=path), self.assertRaises(subject.ContractError): subject.prepare(settings, self.root / "out")
            file.write_bytes(original)


if __name__ == "__main__":
    unittest.main()
