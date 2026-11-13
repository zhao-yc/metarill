"""离线固定双端Linux镜像构建上下文；不运行Docker、网络、应用或数据库命令。"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import stat
import zipfile

from paired_http import ContractError, canonical, read_json


# 已真实验过的旧完整应用；只允许明确继承这对内容ID，不将任意镜像当官方运行时。
PINNED_APPLICATION_BASES = {
    "java": "sha256:329f43e4d3678bf2d81cfd6d7f73baa3ad73676134798dc7018d3ab28fb85936",
    "csharp": "sha256:ee05d99dbb52de8a51f7d42fcbb7a67d1b588c8bb0b637f937a8cba236975200",
}
# BuildKit把裸sha256:ID解析成仓库名，因此FROM使用已验缓存标签；执行器仍须前后核内容ID及父层。
PINNED_APPLICATION_TAGS = {
    "java": "metarill-perf-f85f7ad246414026a3f257ee936f0a5c-java:login-v1",
    "csharp": "metarill-paired-current-548b4e9bac414556d66ff8877c59cd42:csharp-v1",
}
PINNED_RUNTIME_BASES = {
    "java": "docker.io/library/eclipse-temurin@sha256:db724162e4ff5f71fa0a2815026314d9e5e0e9a139f49be1d4717e6930e93341",
    "csharp": "mcr.microsoft.com/dotnet/aspnet@sha256:43cbcf11fd6f88c0375c9c0f7fe59af619abeb323befe5f57dc1502b5a05d8c3",
}


def from_images(settings):
    """显式继承固定旧应用可复用同一运行时层；完整新载荷仍须逐文件覆盖及另行实测。"""
    runtimes = {name: settings[name]["runtimeImage"] for name in ("java", "csharp")}
    if "baseApplicationImages" not in settings:
        return runtimes
    bases = settings["baseApplicationImages"]
    if type(bases) is not dict or bases != PINNED_APPLICATION_BASES or runtimes != PINNED_RUNTIME_BASES:
        raise ContractError("应用基底必须是已验固定双镜像及原运行时来源，不接受自动回退或任意替换")
    return dict(PINNED_APPLICATION_TAGS)


def sha256(path):
    """分块计算现有文件摘要，JAR和原生库不一次性读入内存。"""
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def ordinary(path):
    """不跟随符号链接、Windows junction或其它reparse对象。"""
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400):
        raise ContractError("构建输入不能包含链接或reparse对象")
    return info


def inventory(root):
    """精确枚举全部实际文件，拒绝未冻结配置；不进入链接目录。"""
    if not stat.S_ISDIR(ordinary(root).st_mode):
        raise ContractError("C#运行输出必须是普通目录")
    values = {}
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in directories:
            if not stat.S_ISDIR(ordinary(pathlib.Path(directory) / name).st_mode):
                raise ContractError("运行输出目录类型无效")
        for name in files:
            file = pathlib.Path(directory) / name
            if not stat.S_ISREG(ordinary(file).st_mode):
                raise ContractError("运行输出只能包含普通文件")
            values[file.relative_to(root).as_posix()] = sha256(file)
    return values


def image_reference(value, repository):
    """只接受已核对的官方仓库及完整内容摘要，禁止浮动tag和Dockerfile注入。"""
    if not isinstance(value, str) or not re.fullmatch(re.escape(repository) + r"@sha256:[a-f0-9]{64}", value):
        raise ContractError("运行时必须固定官方仓库及完整镜像manifest摘要")


def safe_relative(value):
    """清单路径固定POSIX形式，不让绝对路径、反斜杠或父级穿越进入输出。"""
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ContractError("运行文件清单路径无效")
    path = pathlib.PurePosixPath(value)
    if path.is_absolute() or path.as_posix() != value or any(part in (".", "..") for part in path.parts):
        raise ContractError("运行文件清单必须是规范相对路径")
    allowed_root = (len(path.parts) == 1 and (path.suffix in (".dll", ".pdb", ".exe") or value in ("Application.Host.deps.json", "Application.Host.runtimeconfig.json", "Application.Host.staticwebassets.endpoints.json", "appsettings.example.json")))
    allowed_data = value in ("metadata/project.json", "metadata/files.json", "metadata/security.json", "licenses/LICENSE.txt", "licenses/THIRD-PARTY-NOTICES.txt")
    allowed_native = len(path.parts) == 4 and path.parts[0] == "runtimes" and path.parts[2] == "native" and path.suffix in (".so", ".dll", ".dylib", ".pdb")
    if not (allowed_root or allowed_data or allowed_native):
        raise ContractError("运行输出含未允许的配置或文件，不可加入镜像")
    return path


def validate(settings):
    """验证固定二进制及完整目录清单；这里只证明输入身份，不证明Linux已可运行。"""
    if settings.get("version") != 1 or not re.fullmatch(r"[a-f0-9]{32}", settings.get("runId", "")):
        raise ContractError("必须声明版本和唯一32位runId")
    java, csharp = settings.get("java", {}), settings.get("csharp", {})
    for target in (java, csharp):
        if not re.fullmatch(r"[a-f0-9]{40}", target.get("sourceCommit", "")):
            raise ContractError("必须固定两端源码提交")
    image_reference(java.get("runtimeImage"), "docker.io/library/eclipse-temurin")
    image_reference(csharp.get("runtimeImage"), "mcr.microsoft.com/dotnet/aspnet")
    from_images(settings)
    jar = pathlib.Path(java.get("jar", ""))
    root = pathlib.Path(csharp.get("outputRoot", ""))
    if not jar.is_absolute() or not root.is_absolute():
        raise ContractError("固定输入必须使用明确绝对路径")
    if not stat.S_ISREG(ordinary(jar).st_mode) or not re.fullmatch(r"[a-f0-9]{64}", java.get("sha256", "")) or sha256(jar) != java["sha256"]:
        raise ContractError("Java JAR与固定摘要不符")
    with zipfile.ZipFile(jar) as archive:
        names = archive.namelist()
        if "META-INF/MANIFEST.MF" not in names or not any(name.startswith("BOOT-INF/classes/") and name.endswith(".class") for name in names):
            raise ContractError("Java输入必须是包含原应用类的Spring Boot可执行JAR")
        with archive.open("META-INF/MANIFEST.MF") as stream:
            manifest = stream.read(16 * 1024 + 1)
        if len(manifest) > 16 * 1024 or b"Main-Class: org.springframework.boot.loader.launch.JarLauncher" not in manifest or b"Start-Class: " not in manifest:
            raise ContractError("Java JAR缺少固定Spring Boot启动器或原应用入口")
    expected = csharp.get("files")
    if not isinstance(expected, dict) or not expected:
        raise ContractError("缺少C#完整运行输出清单")
    for path, checksum in expected.items():
        safe_relative(path)
        if not isinstance(checksum, str) or not re.fullmatch(r"[a-f0-9]{64}", checksum):
            raise ContractError("运行文件缺少完整摘要")
    if inventory(root) != expected:
        raise ContractError("C#实际运行输出与固定完整清单不符")
    required = {"Application.Host.dll", "Application.Host.runtimeconfig.json", "Application.Host.deps.json", "metadata/project.json", "metadata/files.json", "metadata/security.json", "runtimes/linux-x64/native/libSkiaSharp.so"}
    if not required <= expected.keys():
        raise ContractError("缺少宿主、元数据或Linux x64原生依赖")
    runtime = read_json((root / "Application.Host.runtimeconfig.json").read_bytes()).get("runtimeOptions", {})
    frameworks = {item.get("name"): item.get("version") for item in runtime.get("frameworks", [])}
    if runtime.get("tfm") != "net10.0" or any(not isinstance(frameworks.get(name), str) or not frameworks[name].startswith("10.0.") for name in ("Microsoft.NETCore.App", "Microsoft.AspNetCore.App")):
        raise ContractError("运行输出不满足.NET10与ASP.NET10共享框架合同")
    with (root / "runtimes/linux-x64/native/libSkiaSharp.so").open("rb") as stream:
        header = stream.read(20)
    if len(header) < 20 or header[:6] != b"\x7fELF\x02\x01" or int.from_bytes(header[18:20], "little") != 62:
        raise ContractError("固定原生库不是Linux x64 ELF；尚未在Linux加载")
    return jar, root


def dockerfile(settings, name):
    """只复制已构建产物；不restore、重生成、改Program或写入任何连接凭据。"""
    target = settings[name]
    common = ["# 固定产物上下文；仅供Server-200后续受控构建，本文件本身不执行部署。",
              "FROM " + from_images(settings)[name], "USER 0:0", "WORKDIR /app",
              "RUN mkdir -p /data/files /data/exports /data/resources /app/.local && chown -R 10001:10001 /data /app/.local",
              f'LABEL metarill.performance.run="{settings["runId"]}" metarill.performance.target="{name}"',
              f'LABEL metarill.source.commit="{target["sourceCommit"]}"',
              "ENV HOME=/data LANG=C.UTF-8 LC_ALL=C.UTF-8 TZ=Asia/Shanghai"]
    if name == "java":
        common.extend(["COPY --chown=10001:10001 application.jar /app/application.jar",
                       "ENV SERVER_ADDRESS=0.0.0.0 YZP_SAMPLE_PORT=8080 YZP_SAMPLE_RESOURCES_PATH=/data/resources YZP_SAMPLE_REDIS_DATABASE=0",
                       f'ENV SPRING_APPLICATION_NAME=metarill-perf-{settings["runId"]}-java'])
        entry = ["java", "-jar", "/app/application.jar"]
    else:
        common.extend(["COPY --chown=10001:10001 app/ /app/",
                       "ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://0.0.0.0:8080 Authentication__Mode=Web Files__RootDirectory=/data/files Exports__RootDirectory=/data/exports"])
        entry = ["dotnet", "/app/Application.Host.dll"]
    common.extend(["USER 10001:10001", "EXPOSE 8080", "ENTRYPOINT " + json.dumps(entry)])
    return "\n".join(common) + "\n"


def prepare(settings, output):
    """先完整核验后创建全新上下文；失败保留不完整证据，不删除、不覆盖、不自动重试。"""
    jar, root = validate(settings)
    output = pathlib.Path(output).absolute()
    if output.exists() or output.is_symlink() or output.resolve().is_relative_to(root.resolve()):
        raise ContractError("上下文必须位于运行输入之外的新目录")
    output.mkdir(parents=True, exist_ok=False)
    (output / "intent.json").write_bytes(canonical({"runId": settings["runId"], "settingsSha256": hashlib.sha256(canonical(settings)).hexdigest(), "dockerExecuted": False}))
    java = output / "java"; csharp = output / "csharp"
    java.mkdir(); (csharp / "app").mkdir(parents=True)
    shutil.copyfile(jar, java / "application.jar")
    for relative in settings["csharp"]["files"]:
        destination = csharp / "app" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / relative, destination)
    if sha256(java / "application.jar") != settings["java"]["sha256"] or inventory(csharp / "app") != settings["csharp"]["files"]:
        raise ContractError("复制的运行产物与固定输入不符")
    # 再次核对输入，避免复制期间的源文件变化被单端拷贝成功掩盖。
    validate(settings)
    for name in ("java", "csharp"):
        directory = output / name
        (directory / "Dockerfile").write_text(dockerfile(settings, name), encoding="utf-8", newline="\n")
        (directory / ".dockerignore").write_text("**\n!Dockerfile\n!application.jar\n!app/\n!app/**\n", encoding="utf-8", newline="\n")
    receipt = {"version": 1, "runId": settings["runId"], "platform": "linux/amd64", "dockerExecuted": False, "containersStarted": False,
               "runtimeAccepted": False, "performanceAccepted": False, "javaJarSha256": settings["java"]["sha256"], "csharpFiles": len(settings["csharp"]["files"]),
               "runtimeImages": {name: settings[name]["runtimeImage"] for name in ("java", "csharp")},
               "fromImages": from_images(settings),
               "baseApplicationImages": dict(settings.get("baseApplicationImages", {})),
               "contextFiles": inventory(output)}
    (output / "context-receipt.json").write_bytes(canonical(receipt))
    return receipt


def main():
    """命令行只生成私有离线上下文，不调用Docker或外部程序。"""
    parser = argparse.ArgumentParser(description="固定Java/C# Linux镜像上下文；不执行Docker")
    parser.add_argument("--config", required=True); parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        receipt = prepare(read_json(pathlib.Path(args.config).read_bytes()), args.output)
        print(f"双端离线上下文已固定：C# {receipt['csharpFiles']}文件；未构建镜像、启动容器或通过性能验收。")
        return 0
    except Exception as error:
        print("构建上下文未通过：" + type(error).__name__ + "；保留本轮证据，不自动重试。")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
