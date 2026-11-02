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

IMAGES = {
    "java": "sha256:329f43e4d3678bf2d81cfd6d7f73baa3ad73676134798dc7018d3ab28fb85936",
    "csharp": "sha256:ee05d99dbb52de8a51f7d42fcbb7a67d1b588c8bb0b637f937a8cba236975200",
    "redis": "sha256:ff02b58f971e7d7d156a1267e283fcbbeee91773b6aa36c49dac28ecfe28eadf",
}
LABEL = "metarill.performance."


def validate(value):
    """纯内存核对精确用途和名称；未批准入口不接触文件或运行子进程。"""
    run = value.get("runId", "")
    if value.get("version") != 1 or value.get("purpose") != "paired-resources-v1" or not re.fullmatch(r"[a-f0-9]{32}", run):
        raise ValueError("资源用途或运行号无效")
    if value.get("authorizationStatus") != "approved" or value.get("leaseStatus") != "running":
        raise ValueError("本轮没有可执行资源租约")
    if value.get("images") != IMAGES:
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
    if set(items) != {"java.env", "csharp.env", "redis.conf"}:
        raise ValueError("只允许三个固定配置输入")
    contents = {}
    for name, checksum in items.items():
        path = root / name
        if stat.S_IMODE(regular(path).st_mode) != (0o444 if name == "redis.conf" else 0o600):
            raise ValueError("私有文件权限不符")
        raw = path.read_bytes()
        if len(raw) > 16384 or hashlib.sha256(raw).hexdigest() != checksum:
            raise ValueError("私有输入字节变化")
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
    if csharp != {"Database__Provider": "mysql", "Database__ExpectedDatabase": database, "Database__ConnectionString": connection}:
        raise ValueError("C#只能读取本轮受限连接，不允许改认证或应用入口")
    expected_redis = f"bind 0.0.0.0\nport 6379\nprotected-mode yes\nsave \"\"\nappendonly no\nrequirepass {redis_password}\nmaxmemory 128mb\nmaxmemory-policy noeviction\ndir /data\n"
    if contents["redis.conf"] != expected_redis:
        raise ValueError("Redis只允许本轮口令、无持久化的固定配置")


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
        for part in ("files", "exports", "resources", "local"):
            child = data / part; child.mkdir(mode=0o700); os.chown(child, 10001, 10001)
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
                args += ["--network-alias", "redis", "--tmpfs", "/data:rw,nosuid,nodev,size=16777216,uid=10001,gid=10001,mode=0700", "--mount", f"type=bind,src={root}/redis.conf,dst=/run/redis.conf,readonly", IMAGES[target], "redis-server", "/run/redis.conf"]
            else:
                args += ["--publish", f"127.0.0.1:{value['ports'][target]}:8080", "--env-file", str(root / (target + ".env")), "--mount", f"type=bind,src={root}/data-{target},dst=/data"]
                if target == "csharp":
                    args += ["--mount", f"type=bind,src={root}/data-csharp/local,dst=/app/.local", "--env", "Authentication__DevelopmentBootstrap__Enabled=false"]
                args += [IMAGES[target]]
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
        if len(sys.argv) != 2 or sys.argv[1] not in ("create", "start-hosts", "cleanup"):
            raise ValueError("只接受create、start-hosts、cleanup")
        print(json.dumps(manage(json.load(sys.stdin), sys.argv[1]), ensure_ascii=False))
        return 0
    except Exception as error:
        print("双端资源未通过：" + type(error).__name__ + "；保留归属和证据，不自动重试。", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
