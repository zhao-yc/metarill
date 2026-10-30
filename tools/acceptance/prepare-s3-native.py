"""从干净提交准备独占S3验收的固定来源；只读SSH，不读取凭据，不创建远端资源。"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import uuid
import zipfile

FORMAL = "Server200_real_objects_are_immutable_verified_seekable_and_independently_cleaned"
GUARDS = "FullyQualifiedName~S3NativeAuthorizationTests|FullyQualifiedName~S3NativeCleanupTests|FullyQualifiedName~S3NativeArtifactTests"
IMAGE = "sha256:22e834ac4cec66d1337c09038140b7c92c0dff4962b56fedb18d6ca564b80f93"


def digest(path):
    """流式计算原文件摘要，避免将全部DLL读入内存。"""
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    """新建清单，不覆盖既有租约或证据。"""
    with open(path, "x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def no_links(path):
    """现存祖先不能为链接或Windows重解析点。"""
    for item in (path, *path.parents):
        if item.is_symlink() or (item.exists() and getattr(item.lstat(), "st_file_attributes", 0) & 0x400):
            raise RuntimeError("验收路径含链接，保留现场。")


def clean_source(source):
    """固定提交且含未跟踪文件也拒绝；不自动暂存、stash或提交。"""
    result = subprocess.run(["git", "-C", str(source), "status", "--porcelain", "--untracked-files=all"],
                            capture_output=True, check=True, text=True)
    if result.stdout.strip():
        raise RuntimeError("源码工作区不干净，不能固定验收来源。")
    commit = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if not re.fullmatch(r"[a-f0-9]{40}", commit):
        raise RuntimeError("源码提交不是完整SHA。")
    return commit


def safe_extract(archive, destination):
    """先验证整个zip再解包，拒绝穿越、重复路径和符号链接。"""
    with zipfile.ZipFile(archive) as zipped:
        seen = set()
        for info in zipped.infolist():
            name = info.orig_filename.rstrip("/")
            path = PurePosixPath(name)
            # 拒绝反斜杠及盘符，兼顾Windows和Linux的路径解释。
            if not name or "\\" in name or ":" in name or path.is_absolute() or any(
                    part in ("", ".", "..") for part in name.split("/")) or stat.S_ISLNK(info.external_attr >> 16):
                raise RuntimeError("源码归档路径或链接无效。")
            if name.casefold() in seen:
                raise RuntimeError("源码归档包含重复路径。")
            seen.add(name.casefold())
        zipped.extractall(destination)


def server_scope(inspector, run_id):
    """只读服务指纹，输出不允许出现任何凭据字段。"""
    payload = base64.b64encode(b'{"action":"inspect"}').decode("ascii")
    response = subprocess.run(["ssh", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
                               "-o", "ConnectTimeout=8", "deve-server.yinzon.com", "python3", "-", payload],
                              input=inspector.read_bytes(), capture_output=True, timeout=45)
    if response.returncode or len(response.stdout) > 16384:
        raise RuntimeError("只读S3服务器核验失败。")
    data = json.loads(response.stdout)
    if set(data) != {"host", "address", "containerId", "imageId", "imageName", "endpoint", "credentialNames"} or (
        data["host"] != "Server-200" or data["address"] != "192.168.31.200" or
        data["endpoint"] != "http://192.168.31.200:8333" or data["imageId"] != IMAGE or
        data["imageName"] != "yinzon-seaweedfs:4.48-object-acl-20261004" or
        not re.fullmatch(r"[a-f0-9]{64}", data["containerId"]) or
        data["credentialNames"] != ["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"]):
        raise RuntimeError("S3服务指纹或只读回执不匹配。")
    return dict(purpose="s3-objects-native-v1", runId=run_id, endpoint=data["endpoint"], region="us-east-1",
                bucket="metarill-native-" + run_id, prefix="objects", containerId=data["containerId"],
                imageId=data["imageId"], imageName=data["imageName"], maxAttempts=3, maxObjects=4, maxObjectBytes=1048576)


def local_command(command, cwd, log, environment):
    """长时间构建留在本轮私有日志，不回显依赖错误或环境配置。"""
    with open(log, "xb") as output:
        result = subprocess.run(command, cwd=cwd, env=environment, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError("固定来源构建或门禁失败，请查看本轮私有日志。")


def result_counts(path):
    """读真实TRX计数，非零退出本身不足以证明预期拒绝。"""
    import xml.etree.ElementTree as xml
    tree = xml.parse(path)
    nodes = tree.findall(".//{*}Counters")
    if len(nodes) != 1:
        raise RuntimeError("固定用例缺少唯一TRX计数。")
    return {key: int(nodes[0].attrib.get(key, "-1")) for key in ("total", "passed", "failed", "executed")}


def prepare(source, parent, dotnet, run_id):
    """一次性准备新编号；已有目录不覆盖，失败保留现场且不会获得真实批准。"""
    if not re.fullmatch(r"[a-f0-9]{32}", run_id):
        raise RuntimeError("本轮编号必须为32位小写十六进制。")
    source, parent, dotnet = source.absolute(), parent.absolute(), dotnet.absolute()
    for path in (source, parent, dotnet):
        no_links(path)
    commit = clean_source(source)
    root = parent / run_id
    if root.exists():
        raise RuntimeError("本轮目录已存在，不能重置租约或覆盖证据。")
    root.mkdir(parents=True)
    artifact_root = root / "artifacts" / commit
    artifact_root.mkdir(parents=True)
    archive = artifact_root / "source.zip"
    with archive.open("xb") as output:
        subprocess.run(["git", "-C", str(source), "archive", "--format=zip", commit], stdout=output, check=True)
    extracted = artifact_root / "source"
    extracted.mkdir()
    safe_extract(archive, extracted)
    # 只运行归档内的已提交检查器，不读取工作区中可能变化的脚本。
    scope = server_scope(extracted / "tools/acceptance/server200-s3-info.py", run_id)
    write_json(root / "scope.json", scope)
    environment = os.environ.copy()
    # 默认不消费任何用户现有正式租约，也不把本机实库配置带入离线门禁。
    for name in ("METARILL_S3_NATIVE_LEASE", "METARILL_TEST_SETTINGS"):
        environment.pop(name, None)
    environment["DOTNET_HOST_PATH"] = str(dotnet)
    project = extracted / "backend/tests/Metarill.Files.Tests/Metarill.Files.Tests.csproj"
    local_command([str(dotnet), "build", str(project), "--configuration", "Release", "--no-incremental",
                   "--nologo", "--disable-build-servers", "-warnaserror"], extracted, root / "build.log", environment)
    binary = project.parent / "bin/Release/net10.0"
    frozen = artifact_root / "frozen"
    shutil.copytree(binary, frozen, symlinks=True)
    files = {}
    for item in sorted(frozen.rglob("*")):
        no_links(item)
        if item.is_file():
            files[item.relative_to(frozen).as_posix()] = digest(item)
    required = {"Metarill.Files.Tests.dll", "Metarill.Files.dll", "AWSSDK.S3.dll", "AWSSDK.Core.dll",
                "native/server200-s3-info.py"}
    if not required <= files.keys():
        raise RuntimeError("固定编译闭包缺少必需输出。")
    artifact = dict(sourceCommit=commit, sourceArchiveSha256=digest(archive), binaryRoot=str(frozen), files=files)
    write_json(root / "artifact.json", artifact)
    write_json(artifact_root / "artifact.json", artifact)
    lease = dict(scopeSha256=digest(root / "scope.json"), artifactSha256=digest(root / "artifact.json"),
                 authorizationStatus="pending", leaseStatus="ready", approvalReference="", approvedAtUtc=None,
                 approvedUntilUtc=None, attempts=0, activeAttemptId=None)
    write_json(root / "lease.json", lease)
    results = root / "test-results"
    results.mkdir()
    assembly = frozen / "Metarill.Files.Tests.dll"
    local_command([str(dotnet), "vstest", str(assembly), "--TestCaseFilter:" + GUARDS,
                   "--ResultsDirectory:" + str(results), "--logger:trx;LogFileName=guards.trx"],
                  frozen, root / "guards.log", environment)
    counts = result_counts(results / "guards.trx")
    if counts != dict(total=37, passed=37, failed=0, executed=37):
        raise RuntimeError("冻结授权/清理门禁未完整执行37项。")
    environment["METARILL_S3_NATIVE_LEASE"] = str(root / "lease.json")
    lease_before = digest(root / "lease.json")
    with open(root / "pending-denied.log", "xb") as output:
        denied = subprocess.run([str(dotnet), "vstest", str(assembly), "--TestCaseFilter:FullyQualifiedName~" + FORMAL,
                                 "--ResultsDirectory:" + str(results), "--logger:trx;LogFileName=pending-denied.trx"],
                                cwd=frozen, env=environment, stdout=output, stderr=subprocess.STDOUT)
    counts = result_counts(results / "pending-denied.trx")
    if denied.returncode == 0 or counts != dict(total=1, passed=0, failed=1, executed=1) or (
            digest(root / "lease.json") != lease_before or (root / "runs").exists()):
        raise RuntimeError("pending正式入口未按预期拒绝，不能发起批准。")
    # vstest若意外产生额外文件或修改输出，不能继续使用原摘要申请批准。
    after = {p.relative_to(frozen).as_posix(): digest(p) for p in frozen.rglob("*") if p.is_file()}
    if after != files:
        raise RuntimeError("离线门禁改变了固定编译闭包。")
    plan = f"""# S3独占桶真实验收授权清单

- 服务器：Server-200 / 192.168.31.200；SSH固定主机校验；端点：{scope['endpoint']}。
- 唯一桶：`{scope['bucket']}`；ownership.json标记及objects/下最多3个合成blob，单对象最多1MiB。
- 容器：{scope['containerId']}；镜像：{scope['imageId']}。
- 同一固定来源最多3次尝试，每次确认清理且独立HEAD为404后才可重试；未知结果或清理失败停止。新源码需重新准备和明确批准，不能悄悄重绑。
- 源码提交：{commit}；原归档SHA-256：{artifact['sourceArchiveSha256']}。
- 编译闭包：{len(files)}文件；artifact.json SHA-256：{lease['artifactSha256']}。
- scope.json SHA-256：{lease['scopeSha256']}。
- 已完成严格Release构建、37项冻结门禁、pending正式入口1项预期拒绝。没有执行正式S3验收。
- 获批后仅在验收进程内存读取既有服务的两项凭据，不写配置/日志，不新建密钥。
- 不改数据库/账号/容器/策略，不列出或访问其它桶，不部署。
- 清理先列本桶并逐对象核对真实字节摘要，遇未知对象/摘要不符即停止；只删本轮匹配样本。
- DROP桶及DELETE样本不可逆，仅限上述合成资源；异常可能留下测试桶/对象，不能盲目重试清理。
- 当前租约为pending，approvalReference为空；本清单及离线测试不构成用户授权。

正式命令（仅获批后，从固定输出执行）：

```powershell
$env:METARILL_S3_NATIVE_LEASE='{root / 'lease.json'}'
& '{dotnet}' vstest '{assembly}' '--TestCaseFilter:FullyQualifiedName~{FORMAL}'
```

此用例仅证明公开FileObjectStorage对象层的实际供应商条件写/摘要/读取/缓存/删除；
不替代MySQL、HTTP权限/Range、原浏览器、跨端或性能验收。
"""
    (root / "permission-plan.md").write_text(plan, encoding="utf-8", newline="\n")
    write_json(root / "preparation-receipt.json", dict(sourceCommit=commit, files=len(files), guards=37,
               pendingDenied=1, remoteMutationExecuted=False, scopeSha256=lease["scopeSha256"],
               artifactSha256=lease["artifactSha256"]))
    return root


def main():
    """只接受明确本地路径；失败输出不含环境、凭据或远端错误正文。"""
    parser = argparse.ArgumentParser(description="准备S3固定验收，不执行远端写入")
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--evidence-parent", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--run-id", default=None)
    args = parser.parse_args()
    try:
        root = prepare(args.source, args.evidence_parent, args.dotnet, args.run_id or uuid.uuid4().hex)
        print("固定来源准备完成，等待独立授权：" + str(root / "permission-plan.md"))
    except Exception as error:
        print("S3验收准备失败，现场保留；错误类型：" + type(error).__name__, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
