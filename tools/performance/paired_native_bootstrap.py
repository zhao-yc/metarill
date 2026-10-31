"""Server-200双端原生初始化与真实登录协调器；只消费直接批准的独占双库范围。"""
from __future__ import annotations
import base64
import contextlib
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import re
import secrets
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

import paired_http as http
import paired_identity_seed as seed
import server200_paired_resources as resources

PIN = {"hostname": "3107e214a8a3", "serverUuid": "6d3d024a-9d73-11f1-9939-ce69308a21dc"}
CLASS_HASHES = {
    "PairedJavaConfigurationBootstrap$1.class": "432d22be22ef0a0dfb883da186c8d3576360150bc9ba65d25f73e36ac744cc70",
    "PairedJavaConfigurationBootstrap$Configuration.class": "b9149913eb4fc727d30b8bf899d15b8a69226326d1f031cf86832ea6514e9c10",
    "PairedJavaConfigurationBootstrap$Plan.class": "3964344040f7b11e9167f0a250a3d6bdc9266527957f470ec0f6d699d5ec2aa9",
    "PairedJavaConfigurationBootstrap.class": "fbb1ea86a768d803fabe62c336c5378ab988108393bd4ab8ccf14f46390dcd3f",
}
JOBS = ("java-preview", "java-apply", "csharp-preview", "csharp-apply", "csharp-verify", "csharp-security")


def validate(value):
    """批准校验先于文件、凭据或系统命令；名称和目标镜像不能由调用方自由选择。"""
    if value.get("version") != 1 or value.get("purpose") != "paired-native-v1" or value.get("authorizationStatus") != "approved" or value.get("leaseStatus") != "not-started":
        raise ValueError("本轮没有可启动的原生双端直接授权")
    descriptor = {k: value.get(k) for k in ("version", "runId", "images", "ports", "materialsSha256")}
    descriptor.update(purpose="paired-resources-v1", authorizationStatus="approved", leaseStatus="running")
    run = resources.validate(descriptor)
    databases = ["metarill_fresh_" + run[:16] + "_paired_" + t for t in ("java", "csharp")]
    if value.get("databases") != databases or value.get("user") != "mtf_" + run[:16] or value.get("instancePin") != PIN:
        raise ValueError("双库、账号或MySQL实例不符")
    if any(not re.fullmatch(r"[a-f0-9]{64}", value.get(k, "")) for k in ("bootstrapSha256", "identitySha256")):
        raise ValueError("私有输入摘要缺失")
    return descriptor


def read_private(path, checksum):
    """读取固定路径的600私有材料并核字节，不输出配置值。"""
    if (resources.regular(path).st_mode & 0o777) != 0o600:
        raise ValueError("私有材料必须0600")
    data = path.read_bytes()
    if len(data) > 65536 or hashlib.sha256(data).hexdigest() != checksum:
        raise ValueError("私有材料字节变化")
    return http.read_json(data)


def bootstrap(root, value):
    """固定Java四个外置class、原20输入和C#Bootstrap环境；不接受额外脚本或入口。"""
    folder = root / "bootstrap"
    resources.regular(folder, True)
    manifest = read_private(folder / "inputs.json", value["bootstrapSha256"])
    expected = {"java/project-config.json", "java/business-schema.sql", "java/sources.json", "descriptor.json", "bootstrap.env"}
    for table in ("config_button", "config_child", "config_column", "config_first_menu", "config_index_column", "config_index", "config_page_column", "config_page", "config_second_menu", "config_table", "yz_admin_type", "yz_file_library_folder", "yz_file_library", "yz_file_ref"):
        expected.add("java/configuration-schema/" + table + ".sql")
    for table in ("yz_manager", "yz_role", "yz_role_permission", "yz_manager_log"):
        expected.add("java/account-schema/" + table + ".sql")
    expected.update("classes/com/yinzon/samplehost/" + n for n in CLASS_HASHES)
    if set(manifest) != expected: raise ValueError("初始化输入必须等于固定27项")
    actual = set()
    for directory, directories, files in os.walk(folder, followlinks=False):
        for name in directories: resources.regular(pathlib.Path(directory) / name, True)
        for name in files: actual.add((pathlib.Path(directory) / name).relative_to(folder).as_posix())
    if actual != expected | {"inputs.json"}: raise ValueError("初始化目录存在未固定文件，不能扩展外置类路径")
    for name, checksum in manifest.items():
        path = folder / name
        info = resources.regular(path)
        if not path.resolve().is_relative_to(folder.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != checksum:
            raise ValueError("初始化输入越界或摘要不符")
        if name.startswith("classes/") and checksum != CLASS_HASHES[path.name]:
            raise ValueError("外置Java初始化器不是原固定class")
        if (info.st_mode & 0o777) != (0o600 if name == "bootstrap.env" else 0o444):
            raise ValueError("初始化输入权限不符")
    descriptor = http.read_json((folder / "descriptor.json").read_bytes())
    run = value["runId"]
    required = {"Version": 1, "Purpose": "paired-runtime-bootstrap-v1", "RunId": run,
                "ExpectedDatabase": value["databases"][0], "BundleRoot": "/run/metarill/bootstrap/java",
                "SourcesManifestSha256": manifest["java/sources.json"], "AuthorizationStatus": "approved", "LeaseStatus": "running", "InstancePin": PIN}
    if any(descriptor.get(k) != v for k, v in required.items()) or not re.fullmatch(r"[a-f0-9]{64}", descriptor.get("ExpectedPlanSha256", "")):
        raise ValueError("Java实际安装描述与本轮不符")
    env = (folder / "bootstrap.env").read_text(encoding="utf-8").splitlines()
    if len(env) != 2 or env[0] != "Authentication__Bootstrap__Username=paired-owner-" + run[:16] or not re.fullmatch(r"Authentication__Bootstrap__Password=[a-f0-9]{64}", env[1]):
        raise ValueError("只接受本轮独占C#初始化账号环境")
    return descriptor


def load_mysql():
    """只加载同源固定管理工具；仍由它逐条核服务器/Unix socket和所有权。"""
    path = pathlib.Path(__file__).resolve().parents[1] / "acceptance/server200-fresh-mysql.py"
    spec = importlib.util.spec_from_file_location("paired_native_mysql", path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def mysql_operation(mysql, value, operation, owner_token, password=None):
    """密码只走stdin内存，不写命令行；旧profile和任意库名不能进入本执行器。"""
    payload = {"operation": operation, "scenario": "paired", "runId": value["runId"][:16], "expectedInstance": PIN, "ownerToken": owner_token}
    if operation == "create": payload["password"] = password
    output = io.StringIO()
    with contextlib.redirect_stdout(output), contextlib.redirect_stderr(io.StringIO()), contextlib.ExitStack() as stack:
        previous = sys.stdin
        try: sys.stdin = io.StringIO(json.dumps(payload)); mysql.main()
        finally: sys.stdin = previous
    return http.read_json(output.getvalue())


def capture(args, timeout=120):
    """只执行固定Docker参数；保留本轮日志，避免将私有内容回显到用户侧。"""
    resources.verify_server()
    return subprocess.run(["docker", "-H", "unix:///var/run/docker.sock", *args], env={k: v for k, v in os.environ.items() if not k.startswith("DOCKER_")}, capture_output=True, timeout=timeout)


def parse_job_output(data, target):
    """Java使用原有结果前缀，C#从首个完整JSON根读取；不靠日志关键词猜成功。"""
    text = data.decode("utf-8-sig")
    if target == "java":
        matches = [http.read_json(line[len("结果："):]) for line in text.splitlines() if line.startswith("结果：")]
    else:
        matches = []
        decoder = json.JSONDecoder(object_pairs_hook=http.unique_object, parse_constant=lambda _: (_ for _ in ()).throw(ValueError("非有限数")))
        for index, char in enumerate(text):
            if char != "{": continue
            try: result, consumed = decoder.raw_decode(text[index:])
            except ValueError: continue
            if text[index + consumed:].strip(): raise ValueError("初始化结果后存在额外输出对象")
            matches.append(result); break
    if len(matches) != 1 or not isinstance(matches[0], dict): raise ValueError("初始化没有唯一完整结果对象")
    return matches[0]


def initialization_job(root, value, name, state, state_path):
    """每个显式命令使用独占短期容器，先登记再创建；无论成败按同ID/token清理并核零。"""
    if name not in JOBS: raise ValueError("未知初始化入口")
    target = "java" if name.startswith("java-") else "csharp"
    resource_state = http.read_json((root / "resource-state.json").read_bytes())
    if resource_state.get("ownerToken") != state["ownerToken"] or resource_state.get("status") != "created" or resource_state.get("hostsStarted"):
        raise ValueError("初始化仅允许在业务宿主首次启动之前")
    owner = resource_state["ownerToken"]
    item = {"kind": "container", "target": "init-" + name, "name": "metarill-perf-" + value["runId"] + "-init-" + name}
    if any(j["target"] == item["target"] for j in state["jobs"]) or resources.inspect("container", item["name"]) is not None:
        raise ValueError("初始化命令已登记或名称已存在，不能自动重放")
    state["jobs"].append(item); resources.write_state(state_path, state)
    args = ["container", "create", "--name", item["name"], "--network", "metarill-perf-" + value["runId"],
            "--label", resources.LABEL + "run=" + value["runId"], "--label", resources.LABEL + "owner=" + owner,
            "--label", resources.LABEL + "target=" + item["target"], "--user", "10001:10001", "--read-only",
            "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--memory", "536870912", "--memory-swap", "536870912", "--cpus", "0.5", "--pids-limit", "128",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=134217728,uid=10001,gid=10001,mode=1770", "--env-file", str(root / (target + ".env")),
            "--mount", f"type=bind,src={root}/data-{target},dst=/data"]
    if target == "java":
        args += ["--mount", f"type=bind,src={root}/bootstrap,dst=/run/metarill/bootstrap,readonly", "--entrypoint", "java", resources.IMAGES[target],
                 "-Dloader.path=/run/metarill/bootstrap/classes/", "-Dloader.main=com.yinzon.samplehost.PairedJavaConfigurationBootstrap", "-cp", "/app/application.jar",
                 "org.springframework.boot.loader.launch.PropertiesLauncher", "preview" if name == "java-preview" else "apply-initial", "/run/metarill/bootstrap/descriptor.json"]
    else:
        if name == "csharp-security": args += ["--env-file", str(root / "bootstrap/bootstrap.env")]
        args += ["--mount", f"type=bind,src={root}/data-csharp/local,dst=/app/.local", "--env", "Authentication__DevelopmentBootstrap__Enabled=false", resources.IMAGES[target],
                 {"csharp-preview": "--schema-preview", "csharp-apply": "--schema-apply", "csharp-verify": "--schema-preview", "csharp-security": "--initialize-security"}[name]]
    try:
        identifier = resources.docker(*args)
        if not re.fullmatch(r"[a-f0-9]{64}", identifier): raise RuntimeError("初始化容器回执无效")
        item["id"] = identifier; resources.write_state(state_path, state)
        actual = resources.inspect("container", item["name"]); resources.require_owned(actual, item, resource_state)
        if resources.docker("container", "inspect", "--format", "{{.Image}}", item["name"]) != resources.IMAGES[target]: raise RuntimeError("初始化镜像不符")
        result = capture(["container", "start", "--attach", item["name"]])
        (root / (name + ".stdout.log")).write_bytes(result.stdout); (root / (name + ".stderr.log")).write_bytes(result.stderr)
        runtime = http.read_json(resources.docker("container", "inspect", "--format", '{"running":{{json .State.Running}},"exitCode":{{json .State.ExitCode}}}', item["name"]))
        if result.returncode or runtime != {"running": False, "exitCode": 0}: raise RuntimeError("原显式初始化命令失败，保留日志")
        return parse_job_output(result.stdout, target)
    finally:
        actual = resources.inspect("container", item["name"])
        if actual is not None:
            resources.require_owned(actual, item, resource_state); resources.docker("container", "rm", "--force", item["name"])
        if resources.inspect("container", item["name"]) is not None: raise RuntimeError("初始化容器清理未核零")
        item["containerAbsent"] = True; resources.write_state(state_path, state)


def send(port, method, path, body=None, token=None, form=False):
    """只走200宿主回环、禁代理/重定向；业务请求一次发出，拒绝自动重试。"""
    data = urllib.parse.urlencode(body).encode("utf-8") if form else http.canonical(body) if body is not None else None
    headers = {"Accept": "application/json"}
    if data is not None: headers["Content-Type"] = "application/x-www-form-urlencoded" if form else "application/json"
    if token is not None: headers["loginStateId"] = token
    request = urllib.request.Request(f"http://127.0.0.1:{port}" + path, data=data, headers=headers, method=method)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), http.NoRedirect())
    try: response = opener.open(request, timeout=5)
    except urllib.error.HTTPError as error: response = error
    with response:
        raw = response.read(1024 * 1024 + 1)
        if len(raw) > 1024 * 1024: raise ValueError("HTTP结果越界")
        return response.status, http.read_json(raw)


def identity_http(value, fixture, credentials):
    """原三字段密码form登录与普通权限读/拒绝；Cap关闭时仍提交空兼容值，不预置身份。"""
    facts = []; tokens = {}
    for target in ("java", "csharp"):
        port = value["ports"][target]; ready_path = "/api/admin/sys/yz_manager/cap-config"
        deadline = time.monotonic() + 90
        while True:
            try:
                status, body = send(port, "GET", ready_path)
                if status == 200 and body.get("code") == 200 and body.get("data") == {"enabled": False, "apiEndpoint": ""}: break
            except (OSError, ValueError): pass
            if time.monotonic() >= deadline: raise RuntimeError("真实宿主公开配置未就绪")
            time.sleep(0.2)
        writer = fixture["common"][0]
        status, failure = send(port, "POST", "/api/admin/sys/yz_manager/login", {"username": writer["name"], "password": "wrong-" + credentials["writer"]["password"], "captchaVerification": ""}, form=True)
        if (status, failure.get("code")) != ((200, 500) if target == "java" else (401, 401)) or failure.get("map", {}).get("loginStateId"):
            raise RuntimeError("错误密码未按原实际宿主合同拒绝")
        for principal in fixture["common"]:
            kind = principal["kind"]
            status, body = send(port, "POST", "/api/admin/sys/yz_manager/login", {"username": principal["name"], "password": credentials[kind]["password"], "captchaVerification": ""}, form=True)
            user = body.get("data", {}); token = body.get("map", {}).get("loginStateId")
            if status != 200 or body.get("code") != 200 or user.get("id") != principal["accountId"] or user.get("name") != principal["name"] or user.get("yzAdminTypeId") != str(seed.ADMIN):
                raise RuntimeError("原密码登录或字符串ID/后台回执不符")
            if any(user.get(k) not in (None, "") for k in ("password", "salt", "oldPassword")) or not isinstance(token, str) or not re.fullmatch(r"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}" if target == "java" else r"[a-f0-9]{64}", token):
                raise RuntimeError("登录响应含凭据或令牌格式不符")
            tokens[target, kind] = token
            status, page = send(port, "POST", "/api/admin/auto/sample_contract/81002/page", {"pageNo": 1, "pageSize": 20, "filters": {}}, token)
            if kind == "denied":
                if (status, page.get("code")) != ((200, 500) if target == "java" else (403, 403)): raise RuntimeError("无权账号未按原宿主合同拒绝")
            elif status != 200 or page.get("code") != 200: raise RuntimeError("普通账号原页面查询失败")
            if kind == "reader":
                status, rejected = send(port, "POST", "/api/admin/auto/sample_customer/81001/add", {"name": "forbidden-readonly-" + value["runId"][:16]}, token)
                if (status, rejected.get("code")) != ((200, 500) if target == "java" else (403, 403)):
                    raise RuntimeError("只读账号未拒绝原新增接口")
            facts.append({"target": target, "kind": kind, "passwordLogin": True, "stringAccountId": True, "pageAllowed": kind != "denied", "readonlyCreateDenied": kind == "reader"})
    return facts, tokens


def cleanup_private(root):
    """容器停止后只清本轮固定秘密与数据副本，保留来源/摘要/日志及所有权回执。"""
    if not shutil.rmtree.avoids_symlink_attacks: raise RuntimeError("当前平台没有安全目录清理实现")
    folder = root / "bootstrap"
    if folder.exists() or folder.is_symlink(): resources.regular(folder, True); folder.chmod(0o700)
    for name in ("java.env", "csharp.env", "redis.conf", "identity.json", "bootstrap/bootstrap.env"):
        path = root / name
        if path.exists() or path.is_symlink(): resources.regular(path); path.unlink()
    for name in ("data-java", "data-csharp"):
        path = root / name
        if path.exists() or path.is_symlink():
            if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()): raise RuntimeError("数据目录清理越界")
            shutil.rmtree(path)
    return {"privateCredentialsRemoved": True, "ownedDataDirectoriesRemoved": True}


def cleanup_run(root, value, descriptor, mysql, state, state_path):
    """先确认所有本轮进程停止，再DROP独占双库/账号；发现非本调用token时不接管。"""
    errors = []; state["cleanup"] = {}
    resource_path = root / "resource-state.json"
    try:
        for job in state["jobs"]:
            actual = resources.inspect("container", job["name"])
            if actual is not None:
                resources.require_owned(actual, job, state); resources.docker("container", "rm", "--force", job["name"])
            if resources.inspect("container", job["name"]) is not None: raise RuntimeError("初始化进程未核零")
        if resource_path.exists():
            resource_state = http.read_json(resource_path.read_bytes())
            if resource_state.get("ownerToken") != state["ownerToken"]: raise RuntimeError("容器归属不属于本次调用")
            if resource_state.get("status") != "ended":
                state["cleanup"]["resources"] = resources.manage(descriptor, "cleanup")
        # 即使资源创建尚未开始或账本意外缺失，也不能只凭文件不存在宣称Docker核零。
        for kind, name in [("network", "metarill-perf-" + value["runId"]), *[("container", "metarill-perf-" + value["runId"] + "-" + t) for t in ("java", "csharp", "redis")]]:
            if resources.inspect(kind, name) is not None: raise RuntimeError("资源未独立核零")
        state["cleanup"]["allContainersAndNetworkIndependentlyAbsent"] = True
    except Exception as error: errors.append("resources:" + type(error).__name__)
    if not errors:
        try:
            if mysql.ownership_path(value["runId"][:16]).exists(): state["cleanup"]["database"] = mysql_operation(mysql, value, "cleanup", state["ownerToken"])
            for database in value["databases"]:
                if mysql.mysql("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='" + database + "';") != "0": raise RuntimeError("数据库未核零")
            if mysql.mysql("SELECT COUNT(*) FROM mysql.user WHERE User='" + value["user"] + "';") != "0": raise RuntimeError("账号未核零")
            state["cleanup"]["databasesAndUserIndependentlyAbsent"] = True
        except Exception as error: errors.append("database:" + type(error).__name__)
    if not errors:
        try: state["cleanup"]["private"] = cleanup_private(root)
        except Exception as error: errors.append("private:" + type(error).__name__)
    state["cleanupErrors"] = errors; state["passed"] = state["passed"] and not errors; state["leaseStatus"] = "ended"; resources.write_state(state_path, state)


def verify_tool_sources(value):
    """实际执行字节对照上层固定Git归档清单，不把自报版本号当作来源证明。"""
    expected = {"performance/paired_native_bootstrap.py", "performance/paired_http.py", "performance/paired_identity_seed.py", "performance/server200_paired_resources.py", "acceptance/server200-fresh-mysql.py"}
    hashes = value.get("toolSha256", {})
    if set(hashes) != expected or not re.fullmatch(r"[a-f0-9]{40}", value.get("toolSourceCommit", "")):
        raise ValueError("缺少固定五文件Git来源")
    root = pathlib.Path(__file__).resolve().parents[1]
    for name, checksum in hashes.items():
        if not re.fullmatch(r"[a-f0-9]{64}", checksum) or hashlib.sha256((root / name).read_bytes()).hexdigest() != checksum:
            raise ValueError("实际执行器来源字节变化")


def business_counts(mysql, value):
    """拒绝写入前后读真实业务物理行数，不能仅凭HTTP错误断言没有写入。"""
    return {database: {table: int(mysql.mysql(f"SELECT COUNT(*) FROM `{database}`.`{table}`;")) for table in ("sample_customer", "sample_contract", "sample_contract_line")} for database in value["databases"]}


def run(value):
    """一次串起显式初始化/播种/HTTP；finally独立清理，失败不自动重放已执行的写入。"""
    descriptor = validate(value); verify_tool_sources(value); root = resources.root_for(value["runId"])
    resources.read_materials(root, descriptor); java = bootstrap(root, value)
    credentials = read_private(root / "identity.json", value["identitySha256"])
    for entry in credentials.values(): entry["csharpSalt"] = base64.b64decode(entry["csharpSalt"], validate=True)
    fixture = seed.prepare(value["runId"], credentials)
    mysql = load_mysql(); state_path = root / "native-state.json"
    handle = os.open(state_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    state = {"runId": value["runId"], "ownerToken": secrets.token_hex(32), "inputSha256": http.digest(value), "stage": "preflight", "jobs": [], "passed": False, "leaseStatus": "running", "performanceAccepted": False, "releaseAccepted": False, "acceptedScope": "paired-initialization-and-identity-only"}
    with os.fdopen(handle, "w", encoding="utf-8") as output: json.dump(state, output)
    descriptor["ownerToken"] = state["ownerToken"]
    def stage(name):
        state["stage"] = name; resources.write_state(state_path, state); print("阶段：" + name, flush=True)
    try:
        resources.verify_server()
        if (root / "resource-state.json").exists() or mysql.ownership_path(value["runId"][:16]).exists(): raise RuntimeError("已有资源归属记录，拒绝复用")
        if mysql.mysql("SELECT @@hostname,@@server_uuid;") != PIN["hostname"] + "\t" + PIN["serverUuid"]: raise RuntimeError("MySQL实例不符")
        for image in resources.IMAGES.values():
            actual = http.read_json(resources.docker("image", "inspect", "--format", '{"id":{{json .Id}},"os":{{json .Os}},"architecture":{{json .Architecture}}}', image))
            if actual != {"id": image, "os": "linux", "architecture": "amd64"}: raise RuntimeError("固定镜像不符")
        java_env = dict(line.split("=", 1) for line in (root / "java.env").read_text(encoding="utf-8").splitlines())
        stage("创建本轮双库账号"); state["databaseCreation"] = mysql_operation(mysql, value, "create", state["ownerToken"], java_env["YZP_SAMPLE_DB_PASSWORD"])
        stage("创建本轮网络和三个容器"); resources.manage(descriptor, "create")
        stage("Java空库预览与原配置安装")
        preview = initialization_job(root, value, "java-preview", state, state_path)
        if preview.get("observedTableCount") != 0 or preview.get("planSha256") != java["ExpectedPlanSha256"]: raise RuntimeError("Java原空库预览不符")
        applied = initialization_job(root, value, "java-apply", state, state_path)
        if applied.get("applied") is not True or applied.get("verifiedTables") != 22: raise RuntimeError("Java原配置安装不符")
        stage("C#原结构预览执行核验与安全初始化")
        preview = initialization_job(root, value, "csharp-preview", state, state_path)
        if not preview.get("changes"): raise RuntimeError("C#初始空库没有结构计划")
        applied = initialization_job(root, value, "csharp-apply", state, state_path)
        if applied.get("succeeded") is not True: raise RuntimeError("C#结构执行没有成功回执")
        verified = initialization_job(root, value, "csharp-verify", state, state_path)
        if verified.get("changes") != []: raise RuntimeError("C#结构核验仍有差异")
        security = initialization_job(root, value, "csharp-security", state, state_path)
        if security.get("status") != "initialized": raise RuntimeError("C#原安全初始化未成功")
        stage("同逻辑普通身份播种与逐字段回读")
        seed_lease = {**value, "purpose": "paired-identity-seed-v1", "leaseStatus": "running", "fixtureSha256": seed.fingerprint(fixture)}
        state["identitySeeds"] = [seed.apply(seed_lease, t, fixture, mysql.mysql) for t in ("java", "csharp")]
        stage("启动两个原正式Web宿主"); resources.manage({**descriptor, "databaseInitializationVerified": True}, "start-hosts")
        targets = {t: {"container": "metarill-perf-" + value["runId"] + "-" + t, "expectedImageId": resources.IMAGES[t], "containerPort": 8080, "baseUrl": f"http://127.0.0.1:{value['ports'][t]}/api"} for t in ("java", "csharp")}
        probe = http.Server200LocalProbe(); before = http.equal_limits(probe, targets)
        state["transport"] = http.verify_transport({"transportProof": "server200-loopback-v1", "runId": value["runId"], "targets": targets}, probe, before)
        stage("真实form密码登录和原页普通权限")
        physical_before = business_counts(mysql, value)
        state["httpFacts"], tokens = identity_http(value, fixture, credentials)
        if business_counts(mysql, value) != physical_before: raise RuntimeError("只读/无权验收改变了物理业务行")
        state["deniedWritesPhysicalRowsUnchanged"] = True
        after = http.equal_limits(probe, targets)
        if before != after: raise RuntimeError("HTTP前后资源/镜像/容器身份变化")
        state["resourceEvidence"] = before; state["passed"] = True
    except Exception as error:
        state["errorType"] = type(error).__name__
    finally:
        cleanup_run(root, value, descriptor, mysql, state, state_path)
    return state


def cleanup_only(value):
    """原范围内恢复清理只读已有账本，不重新建库/建容器、不重跑任何业务请求。"""
    descriptor = validate(value); verify_tool_sources(value); root = resources.root_for(value["runId"])
    state_path = root / "native-state.json"; resources.regular(state_path)
    state = http.read_json(state_path.read_bytes())
    if state.get("runId") != value["runId"] or state.get("inputSha256") != http.digest(value) or not re.fullmatch(r"[a-f0-9]{64}", state.get("ownerToken", "")):
        raise ValueError("清理账本不属于该直接批准输入")
    descriptor["ownerToken"] = state["ownerToken"]
    cleanup_run(root, value, descriptor, load_mysql(), state, state_path)
    return state


def main():
    """未批准时零外部操作；实际结果仅证明本轮已实现的初始化/身份子集，绝不宣布性能或首版发布。"""
    try:
        if len(sys.argv) > 2 or len(sys.argv) == 2 and sys.argv[1] != "--cleanup": raise ValueError("只支持默认执行或--cleanup")
        value = http.read_json(sys.stdin.read())
        result = cleanup_only(value) if len(sys.argv) == 2 else run(value)
        print(json.dumps(result, ensure_ascii=False)); return 0 if (not result.get("cleanupErrors") if len(sys.argv) == 2 else result["passed"]) else 1
    except Exception as error:
        print("双端原生执行器未启动或前置输入失败：" + type(error).__name__ + "；不自动重试。", file=sys.stderr); return 1


if __name__ == "__main__":
    raise SystemExit(main())
