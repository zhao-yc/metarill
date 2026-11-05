"""Server-200双端独占容器资源工具；不安装数据库、不播种、不发送业务请求。"""
from __future__ import annotations
import contextlib
import hashlib
import json
import os
import pathlib
import re
import secrets
import stat
import subprocess
import sys
import time

IMAGES = {
    "java": "sha256:329f43e4d3678bf2d81cfd6d7f73baa3ad73676134798dc7018d3ab28fb85936",
    "csharp": "sha256:ee05d99dbb52de8a51f7d42fcbb7a67d1b588c8bb0b637f937a8cba236975200",
    "redis": "sha256:ff02b58f971e7d7d156a1267e283fcbbeee91773b6aa36c49dac28ecfe28eadf",
}
# Java经V51核验，C#使用V65原Cap／失败锁／审计固定新包；不覆盖V37旧用途。
OBSERVATION_IMAGES = {
    "java": "sha256:11ed25d417c5f5469e499ace6f81e92480cd86b94f046f6716be153f2b5fd57c",
    "csharp": "sha256:8dbbcfc05b0799cb5d6650644bbb0a88b4f5ae6d9be2c2af9241e06f797f4423",
    "redis": IMAGES["redis"],
}
OBSERVATION_DATA_SHA256 = "74db1feaf23f665e4712ce3b8519cf12e502a18abb30009017dea53671e95cae"


EXISTING_LINE_NATIVE = "paired-native-existing-lines-v1"
EXISTING_LINE_RESOURCES = "paired-existing-lines-resources-v1"
EXISTING_LINE_BUSINESS = "paired-existing-lines-business-v1"
MEASUREMENT_NATIVE = "paired-native-measurement-v1"
MEASUREMENT_RESOURCES = "paired-measurement-resources-v1"
WRITE_MEASUREMENT_NATIVE = "paired-native-write-measurement-v1"
WRITE_MEASUREMENT_RESOURCES = "paired-write-measurement-resources-v1"
# 当前首版镜像使用独立用途，历史报告继续绑定原固定内容 ID。
CURRENT_MEASUREMENT_NATIVE = "paired-native-current-measurement-v1"
CURRENT_MEASUREMENT_RESOURCES = "paired-current-measurement-resources-v1"
CURRENT_WRITE_MEASUREMENT_NATIVE = "paired-native-current-write-measurement-v1"
CURRENT_WRITE_MEASUREMENT_RESOURCES = "paired-current-write-measurement-resources-v1"
CURRENT_MEASUREMENT_IMAGES = {**OBSERVATION_IMAGES, "csharp": "sha256:cc67991c443490a24c0598f2f8861931cdf12d13a664c87839087035f7699732"}
# 两端同索引复测有独立用途和镜像，不能向原三表测量入口暗中加索引。
INDEXED_READ_MEASUREMENT_NATIVE = "paired-native-indexed-read-measurement-v1"
INDEXED_READ_MEASUREMENT_RESOURCES = "paired-indexed-read-measurement-resources-v1"
READ_REQUEST_BUDGET = 19336
INDEXED_WRITE_MEASUREMENT_NATIVE = "paired-native-indexed-write-measurement-v1"
INDEXED_WRITE_MEASUREMENT_RESOURCES = "paired-indexed-write-measurement-resources-v1"
RELATION_INDEX_PROFILE = "original-three-table-relation-indexes-v1"
INDEXED_WRITE_IMAGES = {**OBSERVATION_IMAGES, "csharp": "sha256:3e64578db6a01b25d6c83c22383d4c0ff40b0df9023d65faff315ace96d1cb70"}


# 完整Excel／文件／维护夹具先独立验初始化和普通身份，不把就绪当作业务或计时通过。
EXTENDED_READINESS_NATIVE = "paired-native-extended-readiness-v1"
EXTENDED_READINESS_RESOURCES = "paired-extended-readiness-resources-v1"
EXTENDED_BUSINESS_NATIVE = "paired-native-extended-business-v1"
EXTENDED_BUSINESS_RESOURCES = "paired-extended-business-resources-v1"
EXTENDED_BUSINESS = "paired-extended-business-v1"
EXTENDED_READINESS_IMAGES = {
    "java": "sha256:ecd22231140f1ee11ce89288abb243a5951177a7085f8b1da2be9211934b9728",
    "csharp": "sha256:de3ea9e8217e0f8574a9912db16637be0a1767bc5f1759732486848886043e93",
    "redis": IMAGES["redis"],
}


# 文件拒权修正后的完整产物有自己的用途；旧扩展镜像和历史报告不被替换。
FILE_AUTH_BUSINESS_NATIVE = "paired-native-file-read-auth-business-v1"
FILE_AUTH_BUSINESS_RESOURCES = "paired-file-read-auth-business-resources-v1"
FILE_AUTH_BUSINESS = "paired-file-read-auth-business-v1"
FILE_AUTH_BUSINESS_IMAGES = {**EXTENDED_READINESS_IMAGES,
    "csharp":"sha256:d9bd8b10223a30fb106a9add7852fad968178f21056993b666e3dcf87537869b"}


DEEP_EXCEL_NATIVE = "paired-native-deep-excel-import-v1"
DEEP_EXCEL_RESOURCES = "paired-deep-excel-import-resources-v1"
DEEP_EXCEL_BUSINESS = "paired-deep-excel-import-business-v1"


IMPORT_RULE_NATIVE = "paired-native-import-rule-business-v1"
IMPORT_RULE_RESOURCES = "paired-import-rule-business-resources-v1"
IMPORT_RULE_BUSINESS = "paired-import-rule-business-v1"
IMPORT_RULE_IMAGES = {**FILE_AUTH_BUSINESS_IMAGES,
    "csharp":"sha256:ae626b79ca2c9615eeccc1fb006e8b8f0a988c2456b4a9a1c0ac059f060d1068"}


FILE_MEASUREMENT_NATIVE = "paired-native-protected-file-measurement-v1"
FILE_MEASUREMENT_RESOURCES = "paired-protected-file-measurement-resources-v1"
FILE_REQUEST_BUDGET = 4834
FILE_FIXTURE_REQUESTS = 18


COMMON_FILE_MEASUREMENT_NATIVE = "paired-native-common-file-measurement-v1"
COMMON_FILE_MEASUREMENT_RESOURCES = "paired-common-file-measurement-resources-v1"
UNICODE_FILE_MEASUREMENT_NATIVE = "paired-native-unicode-common-file-v1"
UNICODE_FILE_MEASUREMENT_RESOURCES = "paired-unicode-common-file-resources-v1"
COMMON_FILE_INDEX_PROFILE = "paired-common-file-access-indexes-v1"
COMMON_FILE_IMAGES = {**IMPORT_RULE_IMAGES,"csharp":"sha256:899779ac68a37c96423b3538c88387e973b65eacd23e8a66ca516745213c862d"}


def is_unicode_file_measurement(value):
    """当前镜像的受保护文件下载采用独立的共同索引用途。"""
    return value.get("purpose") in (UNICODE_FILE_MEASUREMENT_NATIVE, UNICODE_FILE_MEASUREMENT_RESOURCES)


def is_common_file_measurement(value):
    """共同文件逻辑索引独立用途，旧原生文件首测仍固定AE镜像和原索引。"""
    return is_unicode_file_measurement(value) or value.get("purpose") in (COMMON_FILE_MEASUREMENT_NATIVE,COMMON_FILE_MEASUREMENT_RESOURCES)


def is_file_measurement(value):
    """原受保护文件下载有独立固定预算和镜像，不复用只读或功能用途。"""
    return is_common_file_measurement(value) or value.get("purpose") in (FILE_MEASUREMENT_NATIVE,FILE_MEASUREMENT_RESOURCES)


def is_import_rule_business(value):
    """明确业务规则修正完整镜像独立登记，原深度Excel失败来源不能被替换。"""
    return value.get("purpose") in (IMPORT_RULE_NATIVE,IMPORT_RULE_RESOURCES,IMPORT_RULE_BUSINESS)


def is_deep_excel_import(value):
    """追加非空附件导入、父页Excel再导入及末行规则失败回滚；只用已核新镜像。"""
    return is_import_rule_business(value) or value.get("purpose") in (DEEP_EXCEL_NATIVE,DEEP_EXCEL_RESOURCES,DEEP_EXCEL_BUSINESS)


def is_file_auth_business(value):
    """文件拒权功能及其后续独立用途共用流程，各自镜像仍由images_for精确固定。"""
    return is_deep_excel_import(value) or value.get("purpose") in (FILE_AUTH_BUSINESS_NATIVE,FILE_AUTH_BUSINESS_RESOURCES,FILE_AUTH_BUSINESS)


def is_extended_readiness(value):
    """扩展夹具只在独立就绪用途启用；历史模型和测量用途保持。"""
    return value.get("purpose") in (EXTENDED_READINESS_NATIVE, EXTENDED_READINESS_RESOURCES)


def is_extended_business(value):
    """真实文件／Excel／维护只在独立业务用途执行，旧就绪用途不放宽。"""
    return is_file_auth_business(value) or value.get("purpose") in (EXTENDED_BUSINESS_NATIVE, EXTENDED_BUSINESS_RESOURCES, EXTENDED_BUSINESS)


def uses_relation_indexes(value):
    """索引工具只服务明确配置的共同索引测量或扩展夹具就绪。"""
    return is_indexed_measurement(value) or is_extended_readiness(value) or is_extended_business(value)


def is_indexed_measurement(value):
    """原六初始化后单独升级索引；仅该用途可调用镜像内固定索引工具。"""
    return is_file_measurement(value) or value.get("purpose") in (INDEXED_WRITE_MEASUREMENT_NATIVE, INDEXED_WRITE_MEASUREMENT_RESOURCES,
                                    INDEXED_READ_MEASUREMENT_NATIVE, INDEXED_READ_MEASUREMENT_RESOURCES) or is_unicode_read_measurement(value) or is_unicode_write_measurement(value) or is_excel_import_measurement(value) or is_excel_export_preflight(value)


# 当前 SQL 诊断只绑定已验证镜像和数据程序集，不改变历史用途或计时用途。
CURRENT_OBSERVED_NATIVE = "paired-native-current-observed-v1"
CURRENT_OBSERVED_RESOURCES = "paired-current-observed-resources-v1"
CURRENT_OBSERVED_BUSINESS = "paired-current-observed-business-v1"
CURRENT_OBSERVATION_DATA_SHA256 = "f76055f27512c3e539d39f9381d1d19b73404298062753c22de3d7e1aee80503"
# 当前完整候选独立采集用途；历史9896573观察及计时用途保持原镜像和程序集摘要。
FULL_OBSERVED_NATIVE = "paired-native-full-candidate-observed-v1"
FULL_OBSERVED_RESOURCES = "paired-full-candidate-observed-resources-v1"
FULL_OBSERVED_BUSINESS = "paired-full-candidate-observed-business-v1"
FULL_OBSERVED_IMAGES = {**COMMON_FILE_IMAGES,"csharp":"sha256:46df751c89bbff99552cd776a4ee44479a17faad47a94b7a72d1720633fd5a20"}
FULL_OBSERVATION_DATA_SHA256 = "ad903d80e3171bf6324acb2cd0a76f8a8164df99784b4eddf100175aeae8bcc4"
# 普通LIKE修正后的完整包单独固定；旧完整包、观察和所有计时用途的镜像与摘要保持。
PATTERN_OBSERVED_NATIVE = "paired-native-plain-like-observed-v1"
PATTERN_OBSERVED_RESOURCES = "paired-plain-like-observed-resources-v1"
PATTERN_OBSERVED_BUSINESS = "paired-plain-like-observed-business-v1"
PATTERN_OBSERVED_IMAGES = {**COMMON_FILE_IMAGES,"csharp":"sha256:fcefb6492e4a58c89d40e86ad47ada2e6b8c012c2759d9c6ac5209ab7dc2e33c"}
PATTERN_OBSERVATION_DATA_SHA256 = "84b3835bcde19a5b88b0e0786c3ae68560c8ae6a2d31e5f792fac22c19735812"
# Unicode空白修正单独采集，额外八个原HTTP只读计数不进入历史46请求用途。
UNICODE_OBSERVED_NATIVE = "paired-native-unicode-like-observed-v1"
UNICODE_OBSERVED_RESOURCES = "paired-unicode-like-observed-resources-v1"
UNICODE_OBSERVED_BUSINESS = "paired-unicode-like-observed-business-v1"
UNICODE_OBSERVED_IMAGES = {**COMMON_FILE_IMAGES,"csharp":"sha256:6b93c3076139f7782d76afd4c83eb91c17d36bb9d81fede7cf9d952cc2583b4f"}
# 当前候选的读写性能沿用共同关联索引，分开用途与请求预算。
UNICODE_READ_MEASUREMENT_NATIVE = "paired-native-unicode-indexed-read-v1"
UNICODE_READ_MEASUREMENT_RESOURCES = "paired-unicode-indexed-read-resources-v1"
UNICODE_WRITE_MEASUREMENT_NATIVE = "paired-native-unicode-indexed-write-v1"
UNICODE_WRITE_MEASUREMENT_RESOURCES = "paired-unicode-indexed-write-resources-v1"
EXCEL_IMPORT_MEASUREMENT_NATIVE = "paired-native-unicode-excel-import-v1"
EXCEL_IMPORT_MEASUREMENT_RESOURCES = "paired-unicode-excel-import-resources-v1"
EXCEL_IMPORT_REQUEST_BUDGET = 3696
EXCEL_EXPORT_PREFLIGHT_NATIVE = "paired-native-unicode-excel-export-preflight-v1"
EXCEL_EXPORT_PREFLIGHT_RESOURCES = "paired-unicode-excel-export-preflight-resources-v1"
EXCEL_EXPORT_PREFLIGHT_REQUESTS = 12
UNICODE_OBSERVATION_DATA_SHA256 = "b0ace0df121fe7b98321eacfbc920a345dffd5e7c44f1c560e1fe858916c8bf7"
WRITE_REQUEST_BUDGET = 8624
EXISTING_LINE_IMAGES = {**OBSERVATION_IMAGES, "csharp": "sha256:491d411c8a79c08c5c3f591a64473cac1814e172d048c7bcd86bc9845ac615f0"}
EXISTING_LINE_DATA_SHA256 = "b6c8159c101d6bab32cc7cef941ad840987ad49615b554ca1c1d49098e2cfb6d"


def is_existing_lines(value):
    """已有明细属于单独批准的验收用途。"""
    return value.get("purpose") in (EXISTING_LINE_NATIVE, EXISTING_LINE_RESOURCES, EXISTING_LINE_BUSINESS)


def is_measurement(value):
    """固定性能用途使用无采集启动分支。"""
    return is_write_measurement(value) or is_indexed_measurement(value) or is_current_measurement(value) or value.get("purpose") in (MEASUREMENT_NATIVE, MEASUREMENT_RESOURCES)


def is_write_measurement(value):
    """写入测量须使用自己的明确用途，不能借用只读租约。"""
    return value.get("purpose") in (WRITE_MEASUREMENT_NATIVE, WRITE_MEASUREMENT_RESOURCES,
                                    CURRENT_WRITE_MEASUREMENT_NATIVE, CURRENT_WRITE_MEASUREMENT_RESOURCES,
                                    INDEXED_WRITE_MEASUREMENT_NATIVE, INDEXED_WRITE_MEASUREMENT_RESOURCES) or is_unicode_write_measurement(value)


def is_current_measurement(value):
    """当前只读和写入测量共用已逐字节核验的新镜像，预算仍按用途区分。"""
    return value.get("purpose") in (CURRENT_MEASUREMENT_NATIVE, CURRENT_MEASUREMENT_RESOURCES,
                                    CURRENT_WRITE_MEASUREMENT_NATIVE, CURRENT_WRITE_MEASUREMENT_RESOURCES)


def is_observed(value):
    """明确的采集用途共享资源约束，旧非采集用途保留。"""
    return is_existing_lines(value) or is_current_observed(value) or is_full_observed(value) or value.get("purpose") in ("paired-observed-resources-v1", "paired-native-observed-v1")


def is_unicode_observed(value):
    """只允许当前Unicode修正候选及其固定原HTTP空白对照。"""
    return value.get("purpose") in (UNICODE_OBSERVED_NATIVE,UNICODE_OBSERVED_RESOURCES,UNICODE_OBSERVED_BUSINESS)


def is_unicode_read_measurement(value):
    """按独立用途固定当前Unicode候选的共同索引只读测量。"""
    return value.get("purpose") in (UNICODE_READ_MEASUREMENT_NATIVE, UNICODE_READ_MEASUREMENT_RESOURCES)


def is_unicode_write_measurement(value):
    """当前镜像的原客户写入有专用范围和预算。"""
    return value.get("purpose") in (UNICODE_WRITE_MEASUREMENT_NATIVE, UNICODE_WRITE_MEASUREMENT_RESOURCES)


def is_excel_import_measurement(value):
    """只允许当前完整镜像和共同关系索引的三页追加导入负载。"""
    return value.get("purpose") in (EXCEL_IMPORT_MEASUREMENT_NATIVE, EXCEL_IMPORT_MEASUREMENT_RESOURCES)


def is_excel_export_preflight(value):
    """导出实文件预检使用共同关系索引与固定镜像。"""
    return value.get("purpose") in (EXCEL_EXPORT_PREFLIGHT_NATIVE, EXCEL_EXPORT_PREFLIGHT_RESOURCES)


def is_pattern_observed(value):
    """新参数化LIKE候选仅接受独立采集用途，不能覆盖旧证据对应的镜像。"""
    return value.get("purpose") in (PATTERN_OBSERVED_NATIVE,PATTERN_OBSERVED_RESOURCES,PATTERN_OBSERVED_BUSINESS)


def is_full_observed(value):
    """完整候选共用观察边界；额外只读对照须独立声明，不能借用计时或导入预算。"""
    return is_unicode_observed(value) or is_pattern_observed(value) or value.get("purpose") in (FULL_OBSERVED_NATIVE,FULL_OBSERVED_RESOURCES,FULL_OBSERVED_BUSINESS)


def is_current_observed(value):
    """当前诊断有独立入口，不能把正式测量隐式切换为 SQL 观察。"""
    return value.get("purpose") in (CURRENT_OBSERVED_NATIVE, CURRENT_OBSERVED_RESOURCES, CURRENT_OBSERVED_BUSINESS)


def images_for(value):
    """从固定的验收用途取得完整镜像内容 ID。"""
    if is_unicode_observed(value) or is_unicode_read_measurement(value) or is_unicode_write_measurement(value) or is_excel_import_measurement(value) or is_excel_export_preflight(value): return UNICODE_OBSERVED_IMAGES
    if is_pattern_observed(value): return PATTERN_OBSERVED_IMAGES
    if is_full_observed(value): return FULL_OBSERVED_IMAGES
    if is_unicode_file_measurement(value): return UNICODE_OBSERVED_IMAGES
    if is_common_file_measurement(value): return COMMON_FILE_IMAGES
    if is_file_measurement(value) or is_import_rule_business(value): return IMPORT_RULE_IMAGES
    if is_file_auth_business(value): return FILE_AUTH_BUSINESS_IMAGES
    if is_extended_readiness(value) or is_extended_business(value): return EXTENDED_READINESS_IMAGES
    if is_indexed_measurement(value): return INDEXED_WRITE_IMAGES
    if is_current_measurement(value) or is_current_observed(value): return CURRENT_MEASUREMENT_IMAGES
    if is_existing_lines(value) or is_measurement(value): return EXISTING_LINE_IMAGES
    return OBSERVATION_IMAGES if is_observed(value) else IMAGES


def observation_data_sha(value):
    """观察桥按对应发布包核验 Data 程序集。"""
    if is_unicode_observed(value): return UNICODE_OBSERVATION_DATA_SHA256
    if is_pattern_observed(value): return PATTERN_OBSERVATION_DATA_SHA256
    if is_full_observed(value): return FULL_OBSERVATION_DATA_SHA256
    if is_current_observed(value): return CURRENT_OBSERVATION_DATA_SHA256
    return EXISTING_LINE_DATA_SHA256 if is_existing_lines(value) else OBSERVATION_DATA_SHA256


LABEL = "metarill.performance."


def validate(value):
    """纯内存核对精确用途和名称；未批准入口不接触文件或运行子进程。"""
    run = value.get("runId", "")
    if type(value.get("version")) is not int or value.get("version") != 1 or value.get("purpose") not in {"paired-resources-v1", "paired-observed-resources-v1", EXISTING_LINE_RESOURCES, MEASUREMENT_RESOURCES, WRITE_MEASUREMENT_RESOURCES, CURRENT_MEASUREMENT_RESOURCES, CURRENT_WRITE_MEASUREMENT_RESOURCES, CURRENT_OBSERVED_RESOURCES, FULL_OBSERVED_RESOURCES, PATTERN_OBSERVED_RESOURCES, UNICODE_OBSERVED_RESOURCES, UNICODE_READ_MEASUREMENT_RESOURCES, UNICODE_WRITE_MEASUREMENT_RESOURCES, EXCEL_IMPORT_MEASUREMENT_RESOURCES, EXCEL_EXPORT_PREFLIGHT_RESOURCES, INDEXED_WRITE_MEASUREMENT_RESOURCES, INDEXED_READ_MEASUREMENT_RESOURCES, EXTENDED_READINESS_RESOURCES, EXTENDED_BUSINESS_RESOURCES, FILE_AUTH_BUSINESS_RESOURCES, DEEP_EXCEL_RESOURCES, IMPORT_RULE_RESOURCES, FILE_MEASUREMENT_RESOURCES, COMMON_FILE_MEASUREMENT_RESOURCES, UNICODE_FILE_MEASUREMENT_RESOURCES} or not re.fullmatch(r"[a-f0-9]{32}", run):
        raise ValueError("资源用途或运行号无效")
    if value.get("authorizationStatus") != "approved" or value.get("leaseStatus") != "running":
        raise ValueError("本轮没有可执行资源租约")
    if images_for(value) is None or value.get("images") != images_for(value):
        raise ValueError("镜像必须等于已核实的三个固定内容ID")
    ports = value.get("ports", {})
    if set(ports) != {"java", "csharp"} or any(type(n) is not int or not 20000 <= n <= 40000 for n in ports.values()) or len(set(ports.values())) != 2:
        raise ValueError("需要两个不同的明确回环端口")
    if not re.fullmatch(r"[a-f0-9]{64}", value.get("materialsSha256", "")):
        raise ValueError("缺少私有输入清单摘要")
    if "ownerToken" in value and (not isinstance(value["ownerToken"], str) or not re.fullmatch(r"[a-f0-9]{64}", value["ownerToken"])):
        raise ValueError("上层资源归属token无效")
    return run


def verify_server():
    """每条Docker命令前重新核验固定主机与IP，不使用本机或客户端上下文。"""
    if subprocess.check_output(["hostname"], text=True).strip() != "Server-200":
        raise RuntimeError("拒绝非Server-200主机")
    if not re.search(r"\binet 192\.168\.31\.200/", subprocess.check_output(["ip", "-4", "addr", "show"], text=True)):
        raise RuntimeError("Server-200地址不符")


def docker(*args):
    """仅调用固定Unix socket；Docker错误/参数可能涉及私有配置，统一有界失败。"""
    verify_server()
    env = {k: v for k, v in os.environ.items() if not k.startswith("DOCKER_")}
    result = subprocess.run(["docker", "-H", "unix:///var/run/docker.sock", *args], env=env, text=True, capture_output=True, timeout=90)
    if result.returncode:
        raise RuntimeError("本轮Docker操作失败，保留归属记录，不自动重试")
    return result.stdout.strip()


def root_for(run):
    """服务器私有根目录由32位运行号决定，调用方不能指定删除或挂载路径。"""
    return pathlib.Path("/tmp") / ("metarill-paired-" + run)


def regular(path, directory=False):
    """拒绝链接及其它用户拥有的输入，路径类型与权限单独核对。"""
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or info.st_uid != os.geteuid() or not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)):
        raise ValueError("私有输入归属或文件类型不符")
    return info


def read_materials(root, value):
    """只读三个固定私有文件；核字节、权限、精确连接和Redis隔离，不回显秘密。"""
    if stat.S_IMODE(regular(root, True).st_mode) != 0o700:
        raise ValueError("资源私有根目录必须0700")
    manifest = root / "materials.json"
    regular(manifest)
    data = manifest.read_bytes()
    if hashlib.sha256(data).hexdigest() != value["materialsSha256"]:
        raise ValueError("私有清单变化")
    items = json.loads(data)
    expected = {"java.env", "csharp.env", "redis.conf"} | ({"observation.key"} if is_observed(value) else set())
    if set(items) != expected:
        raise ValueError("配置集合与明确验收用途不符")
    contents = {}
    for name, checksum in items.items():
        path = root / name
        if stat.S_IMODE(regular(path).st_mode) != (0o444 if name in {"redis.conf", "observation.key"} else 0o600):
            raise ValueError("私有文件权限不符")
        raw = path.read_bytes()
        if len(raw) > 16384 or hashlib.sha256(raw).hexdigest() != checksum:
            raise ValueError("私有输入字节变化")
        if name == "observation.key":
            if len(raw) != 32: raise ValueError("本轮值证明key必须32字节")
        else:
            contents[name] = raw.decode("utf-8")
    run = value["runId"]; user = "mtf_" + run[:16]
    parsed = {}
    for name in ("java", "csharp"):
        entries = contents[name + ".env"].splitlines()
        fields = [line.split("=", 1) for line in entries]
        if not fields or any(len(f) != 2 or not f[0] or not f[1] for f in fields) or len({f[0] for f in fields}) != len(fields):
            raise ValueError("环境配置为空、重复或格式错误")
        parsed[name] = dict(fields)
    java = parsed["java"]; csharp = parsed["csharp"]
    if set(java) != {"YZP_SAMPLE_JDBC_URL", "YZP_SAMPLE_DB_USER", "YZP_SAMPLE_DB_PASSWORD", "YZP_SAMPLE_REDIS_HOST", "YZP_SAMPLE_REDIS_PORT", "YZP_SAMPLE_REDIS_PASSWORD"}:
        raise ValueError("Java环境键不在固定白名单")
    password = java["YZP_SAMPLE_DB_PASSWORD"]; redis_password = java["YZP_SAMPLE_REDIS_PASSWORD"]
    if not all(re.fullmatch(r"[a-f0-9]{64}", p) for p in (password, redis_password)) or password == redis_password:
        raise ValueError("密码必须独立生成且仅在私有文件传递")
    expected_url = "jdbc:mysql://192.168.31.200:3306/metarill_fresh_" + run[:16] + "_paired_java?useUnicode=true&characterEncoding=utf8&serverTimezone=Asia%2FShanghai"
    if java["YZP_SAMPLE_JDBC_URL"] != expected_url or java["YZP_SAMPLE_DB_USER"] != user or java["YZP_SAMPLE_REDIS_HOST"] != "redis" or java["YZP_SAMPLE_REDIS_PORT"] != "6379":
        raise ValueError("Java必须绑定本轮受限库账号及独占Redis别名")
    database = "metarill_fresh_" + run[:16] + "_paired_csharp"
    connection = f"Server=192.168.31.200;Port=3306;Database={database};User ID={user};Password={password};Allow User Variables=true;"
    expected_csharp = {"Database__Provider": "mysql", "Database__ExpectedDatabase": database, "Database__ConnectionString": connection}
    # 新完整宿主必须接同一轮独占Redis、原口令和DB0；历史v1用途仍要求原三字段。
    if is_observed(value) or is_measurement(value) or is_extended_readiness(value) or is_extended_business(value):
        expected_csharp["Authentication__LoginFailures__ConnectionString"] = f"redis:6379,password={redis_password},defaultDatabase=0"
    if csharp != expected_csharp:
        raise ValueError("C#只能读取本轮受限数据库和独占Redis连接，不允许改认证或应用入口")
    expected_redis = f"bind 0.0.0.0\nport 6379\nprotected-mode yes\nsave \"\"\nappendonly no\nrequirepass {redis_password}\nmaxmemory 128mb\nmaxmemory-policy noeviction\ndir /data\n"
    if contents["redis.conf"] != expected_redis:
        raise ValueError("Redis只允许本轮口令、无持久化的固定配置")


def observation_arguments(value, target):
    """只返回固定非秘密配置；HMAC值只走只读文件，输出库名由本轮唯一范围推导。"""
    if is_measurement(value) or is_extended_readiness(value) or is_extended_business(value):
        disabled={"java":"--metarill.paired.sql-observation.enabled=false","csharp":"--Metarill:Paired:SqlObservation:Enabled=false"}
        if target not in disabled: raise ValueError("未知测量宿主")
        return [disabled[target]]
    if not is_observed(value): return []
    if target not in {"java", "csharp"}: raise ValueError("未知采集宿主")
    run = value["runId"]; database = "metarill_fresh_" + run[:16] + "_paired_" + target
    if target == "java":
        settings = {"enabled": "true", "run-id": run, "data-source-names": "dataSource", "key-file": "/run/metarill-observation.key", "output-root": "/data/sql-observation", "maximum-requests": "128", "maximum-commands": "4096"}
        prefix = "--metarill.paired.sql-observation."
    else:
        settings = {"Enabled": "true", "RunId": run, "KeyFile": "/run/metarill-observation.key", "OutputRoot": "/data/sql-observation", "ExpectedDataSha256": observation_data_sha(value), "ExpectedDatabases": database, "MaximumRequests": "128", "MaximumCommands": "4096"}
        prefix = "--Metarill:Paired:SqlObservation:"
    return [prefix + name + "=" + text for name, text in settings.items()]


def write_state(path, state):
    """单协调器逐步保存成功和待判定对象；临时替换避免部分JSON丢失归属。"""
    temporary = path.with_suffix(".tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
        json.dump(state, stream, sort_keys=True)
    os.replace(temporary, path)


def inspect(kind, name):
    """先用名称过滤列出精确对象，再只读取ID和三个归属标签，绝不输出环境。"""
    listing = docker(kind, "ls", "--format", "{{.Name}}" if kind == "network" else "{{.Names}}", "--filter", "name=" + name, *(["-a"] if kind == "container" else []))
    if name not in listing.splitlines():
        return None
    expression = '{{json .Id}}\t{{json (index .Labels "' + LABEL + 'run")}}\t{{json (index .Labels "' + LABEL + 'owner")}}\t{{json (index .Labels "' + LABEL + 'target")}}'
    if kind == "container":
        expression = expression.replace(".Labels", ".Config.Labels")
    result = [json.loads(p) for p in docker(kind, "inspect", "--format", expression, name).split("\t")]
    if len(result) != 4 or not re.fullmatch(r"[a-f0-9]{64}", result[0]):
        raise RuntimeError("对象只读身份格式无效")
    return result


def require_owned(actual, item, state):
    """清理和启动都逐项核对本轮随机归属token；同名外来对象不接管。"""
    expected = [state["runId"], state["ownerToken"], item["target"]]
    if actual[1:] != expected or item.get("id") not in (None, actual[0]):
        raise RuntimeError("资源身份变化，拒绝启动或删除")


def provision(value, root, path):
    """仅创建一个网络和三个容器，先启动Redis；两业务宿主保持created等待显式初始化。"""
    if path.exists():
        raise ValueError("已有本轮资源记录，不允许重复创建")
    read_materials(root, value)
    run = value["runId"]
    items = [{"kind": "network", "target": "network", "name": "metarill-perf-" + run}, *[{"kind": "container", "target": target, "name": f"metarill-perf-{run}-{target}"} for target in ("redis", "java", "csharp")]]
    for item in items:
        if inspect(item["kind"], item["name"]) is not None:
            raise ValueError("本轮名称已存在，拒绝复用")
    state = {"runId": run, "ownerToken": value.get("ownerToken") or secrets.token_hex(32), "descriptorSha256": digest(value), "status": "creating", "items": [], "hostsStarted": False}
    write_state(path, state)
    for target in ("java", "csharp"):
        data = root / ("data-" + target)
        data.mkdir(mode=0o700)
        os.chown(data, 10001, 10001)
        for part in ("files", "exports", "resources", "local", *(["sql-observation"] if is_observed(value) else [])):
            child = data / part; child.mkdir(mode=0o700); os.chown(child, 10001, 10001)
        if is_observed(value):
            owner = data / "sql-observation/.metarill-paired-observation-owner"
            owner.write_text(run, encoding="utf-8"); owner.chmod(0o400); os.chown(owner, 10001, 10001)
    network = items[0]["name"]
    for item in items:
        state["items"].append(item); write_state(path, state)
        labels = sum((["--label", LABEL + key + "=" + text] for key, text in {"run": run, "owner": state["ownerToken"], "target": item["target"]}.items()), [])
        if item["kind"] == "network":
            args = ["network", "create", "--driver", "bridge", *labels, item["name"]]
        else:
            target = item["target"]
            args = ["container", "create", "--name", item["name"], *labels, "--network", network, "--user", "10001:10001", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--restart", "no", "--log-driver", "local", "--log-opt", "max-size=10m", "--log-opt", "max-file=2", "--pids-limit", "64" if target == "redis" else "256", "--memory", "268435456" if target == "redis" else "1073741824", "--memory-swap", "268435456" if target == "redis" else "1073741824", "--cpu-period", "100000", "--cpu-quota", "25000" if target == "redis" else "100000", "--cpuset-cpus", "0", "--tmpfs", "/tmp:rw,nosuid,nodev,size=134217728,uid=10001,gid=10001,mode=1770"]
            if target == "redis":
                args += ["--network-alias", "redis", "--tmpfs", "/data:rw,nosuid,nodev,size=16777216,uid=10001,gid=10001,mode=0700", "--mount", f"type=bind,src={root}/redis.conf,dst=/run/redis.conf,readonly", images_for(value)[target], "redis-server", "/run/redis.conf"]
            else:
                args += ["--publish", f"127.0.0.1:{value['ports'][target]}:8080", "--env-file", str(root / (target + ".env")), "--mount", f"type=bind,src={root}/data-{target},dst=/data"]
                if target == "csharp":
                    args += ["--mount", f"type=bind,src={root}/data-csharp/local,dst=/app/.local", "--env", "Authentication__DevelopmentBootstrap__Enabled=false"]
                if is_observed(value):
                    args += ["--mount", f"type=bind,src={root}/observation.key,dst=/run/metarill-observation.key,readonly"]
                    if target == "csharp": args += ["--env", "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Metarill.Paired.SqlObservation"]
                    # 新完整包按编译器生成的补充deps解析独立观察桥，不改产品宿主依赖。
                    if target == "csharp" and is_full_observed(value): args += ["--env", "DOTNET_ADDITIONAL_DEPS=/app/Metarill.Paired.SqlObservation.deps.json"]
                args += [images_for(value)[target], *observation_arguments(value, target)]
                # 原Java默认写./file-storage；新文件验收显式接本轮/data，根文件系统继续只读。
                if target == "java" and (is_extended_business(value) or is_file_measurement(value)): args += ["--custom.file-storage.local.root-path=/data/files"]
        identifier = docker(*args)
        if not re.fullmatch(r"[a-f0-9]{64}", identifier):
            raise RuntimeError("创建回执不是完整对象ID，保留待核记录")
        item["id"] = identifier; write_state(path, state)
        require_owned(inspect(item["kind"], item["name"]), item, state)
    docker("container", "start", items[1]["name"])
    state["status"] = "created"; write_state(path, state)
    return {"resourcesCreated": True, "redisStarted": True, "hostsStarted": False, "databaseInitialized": False, "performanceAccepted": False}


def digest(value):
    """归属绑定完整资源描述，不将环境口令放入记录。"""
    stable = {k: v for k, v in value.items() if k != "databaseInitializationVerified"}
    return hashlib.sha256(json.dumps(stable, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


@contextlib.contextmanager
def exclusive(root):
    """同一运行号只允许一条资源命令；崩溃残留锁必须人工核对，绝不猜测接管。"""
    regular(root, True)
    path = root / "resource-command.lock"
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, "w") as stream:
            stream.write(str(os.getpid()))
        yield
    finally:
        path.unlink()


def has_listener(text, port=8080):
    """只认内核TCP表的实际LISTEN；宿主docker-proxy握手不能代替容器监听。"""
    for line in text.splitlines()[1:]:
        fields=line.split()
        if len(fields)>=4 and fields[3]=='0A' and re.fullmatch('[A-Fa-f0-9]+:'+format(port,'04X'),fields[1]):return True
    return False


def process_listening(pid):
    """PID来自已核身份的Docker状态，只读该进程网络命名空间及前后启动时钟。"""
    if type(pid) is not int or pid<=1:raise RuntimeError('容器PID无效')
    root=pathlib.Path('/proc')/str(pid);before=(root/'stat').read_text().rsplit(') ',1)[1].split()[19]
    listening=any(has_listener((root/'net'/name).read_text()) for name in ('tcp','tcp6') if (root/'net'/name).exists())
    if (root/'stat').read_text().rsplit(') ',1)[1].split()[19]!=before:raise RuntimeError('监听核验期间进程被替换')
    return listening


def wait_hosts(value,root,state,timeout=90):
    """验证两个原进程实际监听；采集用途另要求索引及 Boot 就绪标记。"""
    observed = is_observed(value)
    deadline=time.monotonic()+timeout
    while True:
        ready=[]
        for item in state['items']:
            if item['target'] not in {'java','csharp'}:continue
            require_owned(inspect('container',item['name']),item,state)
            runtime=json.loads(docker('container','inspect','--format','{"running":{{json .State.Running}},"pid":{{json .State.Pid}}}',item['name']))
            if runtime.get('running') is not True:raise RuntimeError('原宿主在就绪前退出')
            listening=process_listening(runtime.get('pid'))
            if not observed:
                ready.append(listening)
                continue
            base=root/('data-'+item['target'])/'sql-observation';folders=[path for path in base.iterdir() if path.is_dir() and not path.is_symlink()]
            if len(folders)>1:raise RuntimeError('采集宿主实例不唯一')
            configured=False
            if folders and (folders[0]/'index.json').is_file():
                index=json.loads((folders[0]/'index.json').read_text(encoding='utf-8'))
                if index.get('runId')!=value['runId']:raise RuntimeError('就绪索引不属于本轮')
                configured=index.get('sourcesVerified') is True if item['target']=='java' else index.get('dataAssemblySha256')==observation_data_sha(value) and index.get('expectedDatabases')==['metarill_fresh_'+value['runId'][:16]+'_paired_csharp']
                if item['target']=='java':
                    marker=base/'.metarill-paired-application-ready';application_ready=False
                    if marker.exists():
                        if marker.is_symlink() or not marker.is_file() or marker.stat().st_size>4096:raise RuntimeError('原Boot就绪标记无效')
                        proof=json.loads(marker.read_text(encoding='utf-8'))
                        if proof!={'format':'metarill-paired-java-ready-v1','runId':value['runId'],'instance':folders[0].name}:raise RuntimeError('原Boot就绪标记归属不符')
                        application_ready=True
                    configured=configured and application_ready
            ready.append(listening and configured)
        if len(ready)==2 and all(ready):
            proof={'originalProcessListenersVerified':True,'httpRequestsSent':0}
            if observed:
                proof.update(observationSourcesConfigured=True,originalBootRunnersCompleted=True)
            return proof
        if time.monotonic()>=deadline:raise RuntimeError('原进程监听或采集索引未就绪' if observed else 'measurement-host-listener-timeout')
        time.sleep(0.2)


def save_host_diagnostics(root,state):
    """删除前保留已核归属宿主的有界私有日志和退出状态，不回显正文或环境。"""
    result={}
    for item in state['items']:
        if item['target'] not in {'java','csharp'}:continue
        require_owned(inspect('container',item['name']),item,state)
    for item in state['items']:
        if item['target'] not in {'java','csharp'}:continue
        runtime=json.loads(docker('container','inspect','--format','{"running":{{json .State.Running}},"exitCode":{{json .State.ExitCode}},"oomKilled":{{json .State.OOMKilled}},"error":{{json .State.Error}}}',item['name']))
        verify_server();env={k:v for k,v in os.environ.items() if not k.startswith('DOCKER_')}
        logs=subprocess.run(['docker','-H','unix:///var/run/docker.sock','container','logs','--timestamps','--tail','2000',item['name']],env=env,capture_output=True,timeout=30)
        if logs.returncode or len(logs.stdout)+len(logs.stderr)>24*1024*1024:raise RuntimeError('私有宿主日志未完整留存')
        for label,data in (('stdout',logs.stdout),('stderr',logs.stderr)):
            path=root/('host-'+item['target']+'.'+label+'.log');path.write_bytes(data);path.chmod(0o600)
        result[item['target']]=runtime
    (root/'host-diagnostics.json').write_text(json.dumps(result),encoding='utf-8');return {'privateLogsSaved':True,'hostStates':result}


def manage(value, operation):
    """授权校验先于系统操作，命令锁覆盖读取、创建、启动和清理全过程。"""
    run = validate(value)
    root = root_for(run)
    with exclusive(root):
        return manage_exclusive(value, operation, root)


def manage_exclusive(value, operation, root):
    """每个命令均核本轮授权，失败保留现场；清理只能删除本工具记录的同ID对象。"""
    run = validate(value)
    path = root / "resource-state.json"
    if operation == "create":
        return provision(value, root, path)
    regular(root, True); regular(path)
    state = json.loads(path.read_text(encoding="utf-8"))
    if state.get("runId") != run or state.get("descriptorSha256") != digest(value) or not re.fullmatch(r"[a-f0-9]{64}", state.get("ownerToken", "")):
        raise ValueError("资源归属记录与本轮描述不符")
    expected = {( "network", "network", "metarill-perf-" + run), *[("container", t, f"metarill-perf-{run}-{t}") for t in ("redis", "java", "csharp")]}
    observed = [(i.get("kind"), i.get("target"), i.get("name")) for i in state.get("items", [])]
    if len(set(observed)) != len(observed) or any(i not in expected for i in observed):
        raise ValueError("归属记录含非白名单或重复对象")
    if operation == "start-hosts":
        if set(observed) != expected:
            raise ValueError("完整四个资源尚未创建")
        if state.get("status") != "created" or state.get("hostsStarted") or value.get("databaseInitializationVerified") is not True:
            raise ValueError("两端显式初始化必须先由上层执行器核验")
        read_materials(root, value)
        for item in state["items"]:
            actual = inspect(item["kind"], item["name"])
            if actual is None: raise RuntimeError("资源已缺失")
            require_owned(actual, item, state)
        # 标记先于启动；第二宿主失败也不允许重放，必须保留本轮失败并清理。
        state["hostsStarted"] = True; write_state(path, state)
        for item in state["items"]:
            if item["target"] in ("java", "csharp"): docker("container", "start", item["name"])
        state["status"] = "started"; write_state(path, state)
        return {"hostsStarted": True, "runtimeAccepted": False, "performanceAccepted": False}
    if operation in {"wait-hosts","snapshot-hosts"}:
        if not (is_observed(value) or is_measurement(value) or is_extended_readiness(value) or is_extended_business(value)) or state.get("status") != "started" or state.get("hostsStarted") is not True or set(observed)!=expected:
            raise ValueError("仅完整已启动的采集或测量用途允许就绪核验及私有留证")
        return wait_hosts(value,root,state) if operation=="wait-hosts" else save_host_diagnostics(root,state)
    if operation == "stop-hosts":
        if not is_observed(value) or state.get("status") != "started" or not state.get("hostsStarted"):
            raise ValueError("仅已启动的新采集用途允许正常关闭")
        # 先核全部身份，再逐个SIGTERM；停止失败保留原状态，不以强杀冒充finalized。
        for item in state["items"]:
            actual = inspect(item["kind"], item["name"])
            if actual is None: raise RuntimeError("停止前资源缺失")
            require_owned(actual, item, state)
        state["status"] = "stopping"; write_state(path, state)
        for item in state["items"]:
            if item["target"] in ("java", "csharp"):
                docker("container", "stop", "--time", "20", item["name"])
                require_owned(inspect(item["kind"], item["name"]), item, state)
                status = json.loads(docker("container", "inspect", "--format", '{"running":{{json .State.Running}},"exitCode":{{json .State.ExitCode}},"oomKilled":{{json .State.OOMKilled}},"error":{{json .State.Error}}}', item["name"]))
                # JDK21 Terminator按128+信号号退出，SIGTERM为143；仍须后续严格核finalized与请求记录。
                expected_codes = {0, 143} if item["target"] == "java" else {0}
                if type(status) is not dict or set(status) != {"running", "exitCode", "oomKilled", "error"} or status["running"] is not False or type(status["exitCode"]) is not int or status["exitCode"] not in expected_codes or status["oomKilled"] is not False or status["error"] != "": raise RuntimeError("宿主退出不符合原运行时信号规则，采集不能记完整")
        state["status"] = "stopped"; write_state(path, state)
        return {"hostsStoppedGracefully": True, "traceReviewRequiredSeparately": True, "performanceAccepted": False}
    if operation != "cleanup" or state.get("status") == "ended":
        raise ValueError("未知操作或租约已结束")
    # 先核全部现存身份，不能删了一半后才发现另一个对象已被替换。
    for item in state["items"]:
        actual = inspect(item["kind"], item["name"])
        if actual is not None: require_owned(actual, item, state)
    for item in reversed(state["items"]):
        actual = inspect(item["kind"], item["name"])
        if actual is not None:
            require_owned(actual, item, state)
            docker(item["kind"], "rm", *(["--force"] if item["kind"] == "container" else []), item["name"])
        if inspect(item["kind"], item["name"]) is not None:
            raise RuntimeError("删除后独立观察仍存在本轮对象")
    state["status"] = "ended"; write_state(path, state)
    return {"resourcesCleaned": True, "independentAbsenceVerified": True, "databaseCleanupRequiredSeparately": True, "privateFilesCleanupRequiredSeparately": True}


def main():
    """stdin传递不含秘密的描述；异常不回显Docker/连接/私有配置正文。"""
    try:
        if len(sys.argv) != 2 or sys.argv[1] not in ("create", "start-hosts", "wait-hosts", "snapshot-hosts", "stop-hosts", "cleanup"):
            raise ValueError("只接受create、start-hosts、wait-hosts、snapshot-hosts、stop-hosts、cleanup")
        print(json.dumps(manage(json.load(sys.stdin), sys.argv[1]), ensure_ascii=False))
        return 0
    except Exception as error:
        print("双端资源未通过：" + type(error).__name__ + "；保留归属和证据，不自动重试。", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
