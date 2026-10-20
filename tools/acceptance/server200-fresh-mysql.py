"""只在已核验的开发服务器创建/清理本轮随机验收库；不持久保存密码。"""
import json
import os
import pathlib
import re
import subprocess
import sys


def verify_server():
    """每条 Docker 命令前核对主机名与固定局域网地址。"""
    if subprocess.check_output(["hostname"], text=True).strip() != "Server-200":
        raise RuntimeError("拒绝操作非 Server-200 主机。")
    addresses = subprocess.check_output(["ip", "-4", "addr", "show"], text=True)
    if not re.search(r"\binet 192\.168\.31\.200/", addresses):
        raise RuntimeError("Server-200 地址核验失败。")


def mysql(sql):
    """管理凭据仅在容器环境中读取；SQL 和错误均不回显。"""
    verify_server()
    result = subprocess.run(["docker", "-H", "unix:///var/run/docker.sock", "exec", "-i", "yinzon-mysql", "sh", "-c",
        'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -uroot --batch --skip-column-names'],
        input=sql, text=True, capture_output=True, timeout=45)
    if result.returncode:
        raise RuntimeError("MySQL 随机验收对象操作失败，保留所有权记录供安全清理。")
    return result.stdout.strip()


def ownership_path(run):
    """固定服务器临时目录下的本轮所有权记录，不接受调用方自选路径。"""
    return pathlib.Path(f"/tmp/metarill-fresh-{run}.json")


def main():
    """所有名称来自同一随机编号；清理必须与创建成功后的所有权清单完全一致。"""
    payload = json.load(sys.stdin)
    run = payload["runId"]
    if not re.fullmatch(r"[a-f0-9]{16}", run):
        raise RuntimeError("验收编号不合法。")
    databases = [f"metarill_fresh_{run}_{kind}" for kind in ("platform", "business", "package", "nonred")]
    user = f"mtf_{run}"
    state = ownership_path(run)
    verify_server()
    fingerprint = mysql("SELECT @@hostname,@@server_uuid;").split("\t")
    if len(fingerprint) != 2 or not re.fullmatch(r"[0-9a-f-]{36}", fingerprint[1]):
        raise RuntimeError("MySQL 实例指纹无效。")
    instance = {"hostname": fingerprint[0], "serverUuid": fingerprint[1]}
    if payload["operation"] == "create":
        password = payload["password"]
        if not re.fullmatch(r"[a-f0-9]{64}", password) or state.exists():
            raise RuntimeError("验收凭据或所有权清单状态不合法。")
        expected = {"runId": run, "databases": [], "user": None, "instance": instance}
        descriptor = os.open(state, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as stream:
            json.dump(expected, stream)
        def save():
            state.write_text(json.dumps(expected), encoding="utf-8")
        for database in databases:
            if mysql(f"SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='{database}';") != "0":
                raise RuntimeError("随机库名已存在，拒绝复用或覆盖。")
            mysql(f"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
            expected["databases"].append(database)
            save()
        if mysql(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") != "0":
            raise RuntimeError("随机账号已存在，拒绝覆盖。")
        mysql(f"CREATE USER '{user}'@'%' IDENTIFIED BY '{password}';")
        expected["user"] = user
        save()
        for database in databases:
            mysql(f"GRANT SELECT,INSERT,UPDATE,DELETE,CREATE,ALTER,DROP,INDEX,REFERENCES ON `{database}`.* TO '{user}'@'%';")
        print(json.dumps({"createdDatabases": databases, "createdUser": user, "server": "Server-200", "instance": instance}))
    elif payload["operation"] == "cleanup":
        if not state.exists():
            raise RuntimeError("没有本轮所有权清单，拒绝猜测清理对象。")
        expected = json.loads(state.read_text(encoding="utf-8"))
        if expected.get("instance") != instance or expected.get("runId") != run or any(name not in databases for name in expected.get("databases", [])) or expected.get("user") not in (None, user):
            raise RuntimeError("所有权清单不匹配，拒绝清理。")
        owned_databases = list(expected["databases"])
        owned_user = expected["user"]
        if expected["user"]:
            mysql(f"DROP USER '{user}'@'%';")
            if mysql(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") != "0":
                raise RuntimeError("随机账号清理后仍存在。")
            expected["user"] = None
            state.write_text(json.dumps(expected), encoding="utf-8")
        for database in list(expected["databases"]):
            mysql(f"DROP DATABASE `{database}`;")
            if mysql(f"SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='{database}';") != "0":
                raise RuntimeError("随机库清理后仍存在。")
            expected["databases"].remove(database)
            state.write_text(json.dumps(expected), encoding="utf-8")
        # 另起独立mysql命令核对本轮实际拥有的库和临时账号确实不存在，不能只凭DROP退出码报告清理。
        for database in owned_databases:
            if mysql(f"SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='{database}';") != "0":
                raise RuntimeError("随机库清理后仍存在。")
        if owned_user and mysql(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") != "0":
            raise RuntimeError("随机账号清理后仍存在。")
        state.unlink()
        print(json.dumps({"cleaned": True, "server": "Server-200", "instance": instance, "independentAbsenceVerified": True, "databaseCount": len(owned_databases)}))
    else:
        raise RuntimeError("未知验收操作。")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print("空库验收管理失败：" + str(error), file=sys.stderr)
        sys.exit(1)
