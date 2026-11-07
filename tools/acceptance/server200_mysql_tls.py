"""Server-200 独占 MySQL TLS 验收资源；不读取或修改共享 MySQL 的数据及证书。"""
from __future__ import annotations

import base64
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import stat
import subprocess

IMAGE = "sha256:b3b90af2a6552ae30c266fdb7d5dd55f3afb72404bb78d37fe8a23eb857fd3fb"
BASE = Path("/tmp")
LABEL = "metarill.mysql-tls."
FILES = {
    "ca-key.pem", "ca.pem", "wrong-ca-key.pem", "wrong-ca.pem",
    "server-key.pem", "server.pem", "client-key.pem", "client.pem",
    "wrong-client-key.pem", "wrong-client.pem", "root-password", "init.sql",
}
TEMP_FILES = {name + suffix for name in ("server", "client", "wrong-client") for suffix in (".csr", ".ext")}


def verify_server():
    """每条 Docker 命令前核对固定开发主机及局域网地址。"""
    if (subprocess.check_output(["hostname"], text=True).strip() != "Server-200" or
            not re.search(r"\binet 192\.168\.31\.200/", subprocess.check_output(["ip", "-4", "addr", "show"], text=True))):
        raise RuntimeError("拒绝非 Server-200 主机")


def docker(*args):
    """使用固定 socket，错误正文可能含凭据或配置，不向上回显。"""
    verify_server()
    result = subprocess.run(["docker", "-H", "unix:///var/run/docker.sock", *args],
                            env={k: v for k, v in os.environ.items() if not k.startswith("DOCKER_")},
                            capture_output=True, text=True, timeout=90)
    if result.returncode:
        raise RuntimeError("独占 TLS Docker 操作失败，保留本轮归属")
    return result.stdout.strip()


def inspect(kind, name):
    """不存在与命令失败分开，不把故障当作清理成功。"""
    names = docker(kind, "ls", *(("-a",) if kind == "container" else ()), "--format", "{{.Names}}" if kind == "container" else "{{.Name}}").splitlines()
    return json.loads(docker(kind, "inspect", name))[0] if name in names else None


def validate(scope):
    """仅接受本轮随机对象和固定镜像，调用方不能选择任意宿主路径。"""
    if (not re.fullmatch("[a-f0-9]{16}", scope.get("runId", "")) or
            not re.fullmatch("[a-f0-9]{64}", scope.get("ownerToken", "")) or
            scope.get("image") != IMAGE or type(scope.get("port")) is not int or
            not 28000 <= scope["port"] <= 28999):
        raise ValueError("独占 TLS 范围或归属无效")
    return BASE / ("metarill-mysql-tls-" + scope["runId"])


def regular(path, directory=False):
    """清理拒绝符号链接、非当前用户文件及目录外对象。"""
    info = path.lstat()
    if (info.st_uid != os.geteuid() or stat.S_ISLNK(info.st_mode) or
            not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode))):
        raise RuntimeError("独占 TLS 文件归属或类型变化")


def owner(scope):
    return hashlib.sha256(scope["ownerToken"].encode("ascii")).hexdigest()


def require_owned(item, scope):
    """按容器/网络实际标签验归属，删除时使用核验后的 ID。"""
    labels = item.get("Config", {}).get("Labels", {}) if "Config" in item else item.get("Labels", {})
    if any(labels.get(LABEL + key) != value for key, value in
           (("run", scope["runId"]), ("owner", owner(scope)))):
        raise RuntimeError("独占 TLS Docker 对象归属不符")


def read_state(root, scope):
    """根目录是独占 0700，文件集合和内容摘要均需与创建清单一致。"""
    regular(root, True)
    if stat.S_IMODE(root.stat().st_mode) != 0o700:
        raise RuntimeError("独占 TLS 私有根权限变化")
    regular(root / "state.json")
    state = json.loads((root / "state.json").read_text())
    if any(state.get(key) != value for key, value in
           (("runId", scope["runId"]), ("ownerHash", owner(scope)), ("image", IMAGE), ("port", scope["port"]))):
        raise RuntimeError("独占 TLS 文件清单归属不符")
    names = {p.name for p in root.iterdir()}
    if not names <= FILES | TEMP_FILES | {"state.json"}:
        raise RuntimeError("独占 TLS 根出现额外文件，拒绝删除")
    for name in names - {"state.json"}:
        regular(root / name)
    if state.get("files"):
        if set(state["files"]) != FILES:
            raise RuntimeError("独占 TLS 材料清单键变化")
        if names != FILES | {"state.json"}:
            raise RuntimeError("独占 TLS 证书集合变化")
        for name, checksum in state["files"].items():
            if hashlib.sha256((root / name).read_bytes()).hexdigest() != checksum:
                raise RuntimeError("独占 TLS 证书或凭据内容变化")
    return state


def preflight(root, scope):
    """同名对象和端口均需为空，不复用旧轮次。"""
    if os.path.lexists(root) or inspect("container", root.name) or inspect("network", root.name):
        raise RuntimeError("独占 TLS 本轮名称已存在")
    if docker("image", "inspect", "--format", "{{.Id}}", IMAGE) != IMAGE:
        raise RuntimeError("独占 TLS 固定镜像不存在")
    with socket.socket() as listener:
        listener.bind(("192.168.31.200", scope["port"]))


def openssl(root, *args):
    """密钥仅写本轮私有根，不把 OpenSSL 正文或随机材料写入公共输出。"""
    result = subprocess.run(["openssl", *args], cwd=root, capture_output=True, timeout=30)
    if result.returncode:
        raise RuntimeError("独占 TLS 证书生成失败")


def prepare(root, scope):
    """先生成材料并归档，再由独立 create 操作启动容器。"""
    for key in ("password", "rootPassword"):
        if not re.fullmatch("[a-f0-9]{64}", scope.get(key, "")):
            raise ValueError("独占 TLS 随机口令无效")
    preflight(root, scope)
    root.mkdir(mode=0o700)
    state = dict(runId=scope["runId"], ownerHash=owner(scope), image=IMAGE, port=scope["port"], files={})
    save = lambda: (root / "state.json").write_text(json.dumps(state), encoding="utf-8")
    save()
    for prefix in ("ca", "wrong-ca"):
        openssl(root, "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-sha256", "-days", "1",
                "-subj", "/CN=Metarill TLS " + prefix + " " + scope["runId"],
                "-addext", "basicConstraints=critical,CA:TRUE", "-addext", "keyUsage=critical,keyCertSign,cRLSign",
                "-keyout", prefix + "-key.pem", "-out", prefix + ".pem")
    for serial, (name, issuer, usage) in enumerate(
            (("server", "ca", "serverAuth"), ("client", "ca", "clientAuth"), ("wrong-client", "wrong-ca", "clientAuth")), 2):
        openssl(root, "req", "-new", "-newkey", "rsa:2048", "-nodes", "-sha256",
                "-subj", "/CN=Metarill " + name + " " + scope["runId"],
                "-keyout", name + "-key.pem", "-out", name + ".csr")
        extension = ("basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n"
                     "extendedKeyUsage=" + usage + "\n")
        if name == "server":
            extension += "subjectAltName=DNS:deve-server.yinzon.com\n"
        (root / (name + ".ext")).write_text(extension, encoding="ascii")
        openssl(root, "x509", "-req", "-in", name + ".csr", "-CA", issuer + ".pem",
                "-CAkey", issuer + "-key.pem", "-set_serial", str(serial), "-days", "1", "-sha256",
                "-extfile", name + ".ext", "-out", name + ".pem")
        for suffix in (".csr", ".ext"):
            (root / (name + suffix)).unlink()
    run, password = scope["runId"], scope["password"]
    database, user = "metarill_tls_" + run, "mtls_" + run
    (root / "root-password").write_text(scope["rootPassword"], encoding="ascii")
    sql = (f"CREATE DATABASE `{database}`;\nUSE `{database}`;\n"
           "CREATE TABLE tls_probe(id BIGINT PRIMARY KEY,value VARCHAR(64) NOT NULL);\n"
           "INSERT INTO tls_probe VALUES(1,'before');\n"
           f"CREATE USER '{user}'@'%' IDENTIFIED BY '{password}' REQUIRE SSL;\n"
           f"GRANT SELECT,UPDATE ON `{database}`.* TO '{user}'@'%';\n"
           f"CREATE USER '{user}_x509'@'%' IDENTIFIED BY '{password}' REQUIRE X509;\n"
           f"GRANT SELECT ON `{database}`.* TO '{user}_x509'@'%';\n"
           f"CREATE USER '{user}_plain'@'%' IDENTIFIED BY '{password}' REQUIRE NONE;\n"
           f"GRANT SELECT ON `{database}`.* TO '{user}_plain'@'%';\n"
           f"CREATE USER '{user}_cold'@'%' IDENTIFIED BY '{password}' REQUIRE SSL;\n"
           f"GRANT SELECT ON `{database}`.* TO '{user}_cold'@'%';\n")
    (root / "init.sql").write_text(sql, encoding="ascii")
    for name in FILES:
        (root / name).chmod(0o444)
    state["files"] = {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in sorted(FILES)}
    save()
    return dict(prepared=True, files=state["files"])


def manage(scope, operation):
    """创建、观测、清理只作用于同一随机归属；独立 status 用于回收后核验。"""
    if operation not in ("preflight", "status", "prepare", "materials", "create", "pin", "cleanup"):
        raise ValueError("未知独占 TLS 操作")
    root = validate(scope)
    verify_server()
    if operation == "preflight":
        preflight(root, scope)
        return dict(containerAbsent=True, networkAbsent=True, directoryAbsent=True, portAvailable=True)
    if operation == "status":
        return dict(containerAbsent=inspect("container", root.name) is None,
                    networkAbsent=inspect("network", root.name) is None, directoryAbsent=not os.path.lexists(root))
    if operation == "prepare":
        return prepare(root, scope)
    state = read_state(root, scope)
    if operation == "materials":
        return {name: base64.b64encode((root / name).read_bytes()).decode("ascii") for name in sorted(FILES)}
    if operation == "create":
        if set(state.get("files", {})) != FILES:
            raise RuntimeError("独占 TLS 材料未准备完整")
        labels = ["--label", LABEL + "run=" + scope["runId"], "--label", LABEL + "owner=" + owner(scope)]
        if inspect("container", root.name) or inspect("network", root.name):
            raise RuntimeError("独占 TLS 容器或网络已存在")
        docker("network", "create", "--driver", "bridge", "--opt", "com.docker.network.bridge.enable_icc=false", *labels, root.name)
        mounts = []
        for name in ("ca.pem", "server.pem", "server-key.pem", "root-password"):
            mounts += ["--mount", f"type=bind,src={root / name},dst=/run/metarill-tls/{name},readonly"]
        container = docker("container", "create", "--pull", "never", "--name", root.name,
                           "--hostname", "mt-tls-" + scope["runId"], *labels, "--network", root.name,
                           "--restart", "no", "--memory", "1g", "--memory-swap", "1g", "--cpus", "1",
                           "--pids-limit", "256", "--log-driver", "none",
                           "--publish", f"192.168.31.200:{scope['port']}:3306",
                           "--tmpfs", "/var/lib/mysql:rw,nosuid,nodev,size=536870912",
                           *mounts,
                           "--mount", f"type=bind,src={root / 'init.sql'},dst=/docker-entrypoint-initdb.d/01-tls.sql,readonly",
                           "--env", "MYSQL_ROOT_PASSWORD_FILE=/run/metarill-tls/root-password",
                           IMAGE, "--ssl-ca=/run/metarill-tls/ca.pem", "--ssl-cert=/run/metarill-tls/server.pem",
                           "--ssl-key=/run/metarill-tls/server-key.pem", "--require-secure-transport=ON",
                           "--tls-version=TLSv1.2,TLSv1.3", "--mysql-native-password=OFF",
                           "--innodb-buffer-pool-size=67108864", "--performance-schema=OFF", "--skip-log-bin")
        actual = inspect("container", root.name)
        require_owned(actual, scope)
        if actual["Id"] != container or actual["Image"] != IMAGE:
            raise RuntimeError("独占 TLS 创建后 ID 或镜像不符")
        docker("container", "start", container)
        return dict(created=True, containerId=container)
    if operation == "pin":
        actual = inspect("container", root.name)
        require_owned(actual, scope)
        sql = "SELECT @@hostname,@@server_uuid,@@require_secure_transport,@@version;SELECT COUNT(*) FROM " + "metarill_tls_" + scope["runId"] + ".tls_probe;"
        result = docker("container", "exec", actual["Id"], "sh", "-c",
                        'MYSQL_PWD="$(cat /run/metarill-tls/root-password)" exec mysql -uroot --batch --skip-column-names -e "$1"',
                        "tls-pin", sql).splitlines()
        hostname, uuid, secure, version = result[0].split("\t")
        if hostname != "mt-tls-" + scope["runId"] or secure != "1" or result[1] != "1":
            raise RuntimeError("独占 TLS 实例或样本不符")
        return dict(hostname=hostname, serverUuid=uuid, requireSecureTransport=True, version=version, rows=1)
    if operation == "cleanup":
        for kind in ("container", "network"):
            item = inspect(kind, root.name)
            if item is not None:
                require_owned(item, scope)
                args = ("rm", "--force", "--volumes") if kind == "container" else ("rm",)
                docker(kind, *args, item["Id"])
                if inspect(kind, root.name) is not None:
                    raise RuntimeError("独占 TLS Docker 对象清理后仍存在")
        read_state(root, scope)
        for path in root.iterdir():
            path.unlink()
        root.rmdir()
        return dict(cleaned=True)
    raise ValueError("未知独占 TLS 操作")
