"""从固定 Git 提交构建并校验 Metarill Linux x64 候选发行包。"""
import argparse
import hashlib
import io
import json
import os
import re
import shutil
import stat
import subprocess
import tempfile
from pathlib import Path, PurePosixPath
from zipfile import BadZipFile, ZIP_DEFLATED, ZipFile, ZipInfo

REPO = Path(__file__).resolve().parents[1]
TARGET = ".NETCoreApp,Version=v10.0/linux-x64"
MANIFEST = "release-manifest.json"
ROLES = {"platform": "Metarill.Api", "full": "Application.Host"}
METADATA = {
    "platform": {"global.json", *(f"backend/metadata/{name}.json" for name in ("platform", "demo", "files", "security"))},
    "full": {"host/metadata/project.json", "host/metadata/files.json", "host/metadata/security.json",
             "frontend/pages.json", "frontend/package-lock.json", ".metarill-package.json"},
}
PRIVATE = {".git", ".local", ".env", "appsettings.local.json", "private-settings.json", "node_modules", "unpackage"}
HEX = r"[0-9a-f]{64}"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def safe_path(name):
    """拒绝越过目录、在 Windows 上产生别名或指向私有配置的路径。"""
    path = PurePosixPath(name)
    require(path.parts and not path.is_absolute() and path.as_posix() == name, "归档包含非法路径")
    for part in path.parts:
        require(not re.search(r'[<>:"\\|?*\x00-\x1f]', part) and not part.endswith((".", " "))
                and part.casefold() not in PRIVATE
                and not re.fullmatch(r"(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])", part.split(".")[0]),
                "归档包含非法或私有路径")
    return path


def digest(stream):
    checksum = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        checksum.update(chunk)
    return checksum.hexdigest()


def file_digest(path):
    with path.open("rb") as source:
        return digest(source)


def zip_files(archive, *, directories=False):
    """在读取内容前拒绝同名条目、链接、文件与目录重叠。"""
    files = {}
    seen = set()
    explicit_directories = []
    for entry in archive.infolist():
        require(entry.orig_filename == entry.filename, "归档路径包含无效字符")
        name = entry.filename.removesuffix("/") if entry.is_dir() else entry.filename
        path = safe_path(name)
        mode = entry.external_attr >> 16
        key = name.casefold()
        require(key not in seen, "归档包含重复路径")
        seen.add(key)
        if entry.is_dir():
            require(directories and stat.S_IFMT(mode) in (0, stat.S_IFDIR), "归档包含非预期目录")
            explicit_directories.append(name)
            continue
        require(stat.S_IFMT(mode) in (0, stat.S_IFREG), "归档包含链接或特殊文件")
        files[name] = (entry, path)
    names = {name.casefold() for name in files}
    parents = {name.casefold(): name for name in explicit_directories}
    for _, path in files.values():
        for parent in path.parents:
            name = parent.as_posix()
            key = name.casefold()
            require(key not in names, "归档包含文件与目录重叠")
            require(parents.setdefault(key, name) == name, "归档包含目录大小写冲突")
    return files


def tracked_source(commit, destination):
    """从 Git 对象解包，忽略本机工作区、未跟踪配置和构建结果。"""
    raw = subprocess.check_output(["git", "archive", "--format=zip", commit], cwd=REPO)
    modes = {}
    with ZipFile(io.BytesIO(raw)) as archive:
        for name, (entry, path) in zip_files(archive, directories=True).items():
            target = destination.joinpath(*path.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as source, target.open("xb") as result:
                shutil.copyfileobj(source, result)
            modes[name] = 0o755 if (entry.external_attr >> 16) & 0o111 else 0o644
    return modes


def source_hashes(folder):
    result = {}
    for path in sorted(folder.rglob("*")):
        require(not path.is_symlink()
                and not getattr(path.lstat(), "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT,
                "发行目录包含链接或重解析点")
        if path.is_file():
            name = path.relative_to(folder).as_posix()
            safe_path(name)
            result[name] = file_digest(path)
    return result


def write_package(output, role, commit, source, source_files, modes, host, host_files):
    require(not any(name.startswith("host/") or name == MANIFEST for name in source_files),
            "源码使用了发行包保留路径")
    payload = dict(source_files)
    payload.update({f"host/{name}": value for name, value in host_files.items()})
    manifest = dict(schemaVersion=1, role=role, commit=commit, runtimeTarget=TARGET,
                    siteWebBuildIncluded=False, files=payload)
    with ZipFile(output, "x", compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for name, checksum in sorted(payload.items()):
            path = host / name[5:] if name.startswith("host/") else source / name
            raw = path.read_bytes()
            require(hashlib.sha256(raw).hexdigest() == checksum, "打包期间来源发生变化")
            entry = ZipInfo(name)
            entry.create_system = 3
            entry.external_attr = (stat.S_IFREG | modes.get(name, 0o644)) << 16
            archive.writestr(entry, raw, compress_type=ZIP_DEFLATED, compresslevel=6)
        archive.writestr(ZipInfo(MANIFEST), json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n")


def read_json(raw):
    """拒绝重复属性，避免不同读取器解释出不同发行清单。"""
    def distinct(pairs):
        result = dict(pairs)
        require(len(result) == len(pairs), "JSON 包含重复属性")
        return result

    return json.loads(raw, object_pairs_hook=distinct)


def verify_package(path, role):
    with ZipFile(path) as archive:
        members = zip_files(archive)
        require(MANIFEST in members and members[MANIFEST][0].file_size < 10_000_000, "发行清单缺失或过大")
        manifest = read_json(archive.read(MANIFEST))
        require(isinstance(manifest, dict) and manifest.get("role") == role
                and manifest.get("schemaVersion") == 1 and manifest.get("runtimeTarget") == TARGET
                and manifest.get("siteWebBuildIncluded") is False, "发行清单角色、版本或运行目标不匹配")
        commit = manifest.get("commit")
        require(isinstance(commit, str) and re.fullmatch(r"[0-9a-f]{40}", commit)
                and path.name == f"{role}-linux-x64-{commit[:8]}.zip", "发行包名称和 Git 提交不匹配")
        files = manifest.get("files")
        require(isinstance(files, dict) and set(files) == set(members) - {MANIFEST}, "发行清单和 ZIP 文件不一致")
        dll = ROLES[role]
        deps_name = f"host/{dll}.deps.json"
        require(METADATA[role] | {"LICENSE", f"host/{dll}.dll", deps_name, f"host/{dll}.runtimeconfig.json"}
                <= files.keys(), "发行包缺少必需的宿主、许可或元数据")
        for name, expected in files.items():
            with archive.open(members[name][0]) as source:
                require(isinstance(expected, str) and re.fullmatch(HEX, expected)
                        and digest(source) == expected, f"文件摘要不匹配：{name}")
        require(members[deps_name][0].file_size < 10_000_000, "宿主依赖清单过大")
        deps = read_json(archive.read(deps_name))
        require(isinstance(deps, dict) and isinstance(deps.get("runtimeTarget"), dict)
                and deps["runtimeTarget"].get("name") == TARGET, "宿主依赖清单的运行目标不匹配")
        return {"commit": commit, "sourceFiles": sum(not n.startswith("host/") for n in files),
                "hostFiles": sum(n.startswith("host/") for n in files)}


def verify_release(folder):
    """先验证外层摘要，再逐个核对两包清单、内容与同源提交。"""
    sums = (folder / "SHA256SUMS").read_text(encoding="ascii").splitlines()
    require(len(sums) == len(ROLES), "必须提供平台与独立应用两个归档摘要")
    verified = {}
    for line in sums:
        match = re.fullmatch(rf"({HEX})  ((platform|full)-linux-x64-[0-9a-f]{{8}}\.zip)", line)
        require(match is not None, "归档摘要格式错误")
        expected, name, role = match.groups()
        require(role not in verified, "归档摘要包含重复角色")
        path = folder / name
        require(file_digest(path) == expected, f"归档摘要不匹配：{name}")
        verified[role] = dict(verify_package(path, role), archive=name, sha256=expected)
    require(len({item["commit"] for item in verified.values()}) == 1, "两份归档来自不同 Git 提交")
    return verified


def build_release(args):
    output = args.output.resolve()
    require(not output.exists(), "输出目录必须为新目录")
    require(output.parent.is_dir(), "输出目录的上级目录不存在")
    commit = subprocess.check_output(
        ["git", "rev-parse", "--verify", "--end-of-options", f"{args.commit}^{{commit}}"],
        cwd=REPO, text=True).strip()
    require(re.fullmatch(r"[0-9a-f]{40}", commit), "无法解析 Git 提交")
    # 私有运行配置不参与生成，保留系统 SDK 路径和 NuGet 缓存配置。
    ignored = ("METARILL", "ASPNETCORE_", "AUTHENTICATION__", "DATABASE__", "AGENT__",
               "DOTNET_ENV", "DOTNET_HOST_PATH", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE")
    env = {key: value for key, value in os.environ.items() if not key.upper().startswith(ignored)}
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false")

    # 归档目录可能位于另一 Git 检出之下，程序集只标记实际打包的提交。
    # 源码链接也不能沿上级检出推断仓库地址及路径。
    provenance = (f"-p:SourceRevisionId={commit}", "-p:EnableSourceControlManagerQueries=false", "-p:EnableSourceLink=false")

    def dotnet(cwd, *command):
        subprocess.run([args.dotnet, *map(str, command)], cwd=cwd, env=env, check=True)

    def restore(cwd, project, *options):
        source = ["--source", args.nuget_source] if args.nuget_source else []
        dotnet(cwd, "restore", project, "--nologo", "--disable-build-servers", "-m:1", *source, *options)

    def publish(cwd, project, destination):
        restore(cwd, project, "-r", "linux-x64")
        # 固定编译器的文档和调试路径，避免不同构建目录写入 DLL 或 PDB。
        dotnet(cwd, "publish", project, "-c", "Release", "-r", "linux-x64", "--self-contained", "false",
               "--no-restore", "--disable-build-servers", "-m:1", "-warnaserror", "--nologo",
               f"-p:PathMap={cwd}=/_/metarill", *provenance, "-o", destination)
        assembly = destination / (Path(project).stem + ".dll")
        require(commit.encode("ascii") in assembly.read_bytes(), "宿主程序集缺少目标 Git 提交")
        marker = cwd.parent.name.encode("utf-8")
        for artifact in destination.rglob("*"):
            if artifact.is_file() and artifact.suffix in (".dll", ".pdb"):
                require(marker not in artifact.read_bytes(), "编译产物仍包含本轮临时构建路径")
        return source_hashes(destination)

    with tempfile.TemporaryDirectory(prefix="metarill-release-", dir=output.parent) as stage:
        work = Path(stage)
        source = work / "source"
        modes = tracked_source(commit, source)
        public = {name: file_digest(source / name) for name in modes}
        cli = "backend/src/Metarill.Cli/Metarill.Cli.csproj"
        restore(source, cli)
        dotnet(source, "build", cli, "-c", "Release", "--no-restore", "--disable-build-servers", "-m:1",
               "-warnaserror", "--nologo", *provenance, "-o", work / "cli")
        full, repeat = work / "full", work / "repeat"
        for destination in (full, repeat):
            dotnet(source, work / "cli/Metarill.Cli.dll", "package", "--metadata", "backend/metadata/demo.json",
                   "--output", destination, "--namespace", "Metarill.ReleaseDemo", "--source-root", source)
        full_files = source_hashes(full)
        require(full_files == source_hashes(repeat), "两次 Full 生成的源码不同")
        manifest = read_json((full / ".metarill-package.json").read_bytes())
        entries = {item["path"]: item["sha256"] for item in manifest["files"]}
        require(manifest["packageMode"] == "FullApplication" and len(entries) == len(manifest["files"])
                and entries == {n: value for n, value in full_files.items() if n != ".metarill-package.json"},
                "Full 源码与生成清单不一致")
        full_build = work / "full-build"
        shutil.copytree(full, full_build)
        host_platform, host_full = work / "host-platform", work / "host-full"
        platform_files = publish(source, "backend/src/Metarill.Api/Metarill.Api.csproj", host_platform)
        app_files = publish(full_build, "backend/Application.Host/Application.Host.csproj", host_full)
        release = work / "release"
        release.mkdir()
        archives = []
        for role, tree, files, permissions, host, host_files in (
                ("platform", source, public, modes, host_platform, platform_files),
                ("full", full, full_files, {}, host_full, app_files)):
            path = release / f"{role}-linux-x64-{commit[:8]}.zip"
            write_package(path, role, commit, tree, files, permissions, host, host_files)
            archives.append(path)
        (release / "SHA256SUMS").write_text(
            "".join(f"{file_digest(path)}  {path.name}\n" for path in sorted(archives)), encoding="ascii", newline="\n")
        verified = verify_release(release)
        # 完成全部校验之后才创建输出目录，并拒绝覆盖已有文件。
        output.mkdir()
        for path in (*archives, release / "SHA256SUMS"):
            with path.open("rb") as source_stream, (output / path.name).open("xb") as result:
                shutil.copyfileobj(source_stream, result)
        require(verify_release(output) == verified, "输出目录读回与已核对发行包不同")
        return verified


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    build = sub.add_parser("build", help="构建并校验平台及示例 Full")
    build.add_argument("--commit", required=True, help="Git 提交或可解析的引用")
    build.add_argument("--output", required=True, type=Path, help="新的输出目录")
    build.add_argument("--dotnet", default="dotnet", help="可选的 SDK 可执行文件")
    build.add_argument("--nuget-source", help="可选的 NuGet 源或已缓存包目录")
    verify = sub.add_parser("verify", help="重新校验两份 ZIP 和 SHA256SUMS")
    verify.add_argument("--output", required=True, type=Path, help="已有发行目录")
    args = parser.parse_args()
    try:
        result = build_release(args) if args.action == "build" else verify_release(args.output)
        print(json.dumps(result, ensure_ascii=False, indent=2))
    except (OSError, ValueError, KeyError, TypeError, BadZipFile, RuntimeError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"发行包操作失败：{error}\n")


if __name__ == "__main__":
    main()
