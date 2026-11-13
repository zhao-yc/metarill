"""Server-200 空库正式验收专用 Redis，独占回环端口和临时数据。"""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import re
import socket

import server200_paired_resources as server


PURPOSE = "platform-bootstrap-redis-v1"
IMAGE = server.IMAGES["redis"]
BASE = Path("/tmp")
CONFIG_NAME = "redis.conf"


def redis_config(password_hash):
    """仅把强随机口令的 SHA-256 写入 Redis ACL，数据不落盘。"""
    return ("bind 0.0.0.0\nport 6379\nprotected-mode yes\n"
            "save \"\"\nappendonly no\ndir /data\nmaxmemory 128mb\n"
            f"user default reset on #{password_hash} ~* &* +@all\n").encode("ascii")


def validate(scope, operation):
    """在 Docker、目录或端口操作前验证用途、批准状态和精确对象。"""
    if operation not in ("preflight", "create", "cleanup"):
        raise ValueError("未知 Redis 验收操作")
    run = scope.get("runId")
    port = scope.get("remotePort")
    if (scope.get("purpose") != PURPOSE or not isinstance(run, str) or not re.fullmatch("[0-9a-f]{16}", run)
            or type(port) is not int or not 28000 <= port <= 28999 or scope.get("image") != IMAGE):
        raise ValueError("隔离 Redis 固定范围不符")
    if operation == "preflight":
        if scope.get("authorizationStatus") != "pending":
            raise ValueError("只读预检仅接收待授权范围")
        return
    if scope.get("authorizationStatus") != "approved" or scope.get("leaseStatus") != "running":
        raise ValueError("隔离 Redis 未取得本轮运行租约")
    for key in ("ownerToken", "passwordSha256"):
        value = scope.get(key)
        if not isinstance(value, str) or not re.fullmatch("[0-9a-f]{64}", value):
            raise ValueError("隔离 Redis 归属或凭据摘要无效")


def preflight(root, port):
    """仅核对同名对象、镜像与远端回环端口可用性。"""
    if os.path.lexists(root) or server.inspect("container", root.name) is not None:
        raise RuntimeError("本轮 Redis 容器或私有目录已存在")
    if server.docker("image", "inspect", "--format", "{{.Id}}", IMAGE) != IMAGE:
        raise RuntimeError("本轮 Redis 固定镜像不存在")
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", port))


def require_config(root, expected):
    """清理前要求根及文件归属匹配，拒绝链接、额外文件和篡改。"""
    server.regular(root, directory=True)
    config = root / CONFIG_NAME
    if set(root.iterdir()) != {config}:
        raise RuntimeError("隔离 Redis 私有根内容变化")
    server.regular(config)
    if config.read_bytes() != expected:
        raise RuntimeError("隔离 Redis 配置内容变化")
    return config


def manage(scope, operation):
    """仅管理本轮唯一容器和配置；清理用容器 ID，失败保留现场。"""
    validate(scope, operation)
    run = scope["runId"]
    port = scope["remotePort"]
    root = BASE / ("metarill-fresh-redis-" + run)
    if operation == "preflight":
        preflight(root, port)
        return {"containerAbsent": True, "directoryAbsent": True, "portAvailable": port, "readOnly": True}

    owner = hashlib.sha256(scope["ownerToken"].encode("ascii")).hexdigest()
    identity = {"runId": run, "ownerToken": owner}
    item = {"target": "fresh-bootstrap-redis"}
    expected = redis_config(scope["passwordSha256"])
    if operation == "create":
        preflight(root, port)
        root.mkdir(mode=0o700)
        config = root / CONFIG_NAME
        with config.open("xb") as stream:
            stream.write(expected)
        config.chmod(0o444)
        labels = []
        for key, value in (("run", run), ("owner", owner), ("target", item["target"])):
            labels.extend(("--label", server.LABEL + key + "=" + value))
        item["id"] = server.docker(
            "container", "create", "--pull", "never", "--name", root.name, *labels,
            "--user", "10001:10001", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--restart", "no", "--log-driver", "none", "--pids-limit", "64", "--memory", "256m",
            "--memory-swap", "256m", "--cpus", "0.25", "--publish", f"127.0.0.1:{port}:6379",
            "--tmpfs", "/data:rw,nosuid,nodev,noexec,size=16777216,uid=10001,gid=10001,mode=0700",
            "--mount", f"type=bind,src={config},dst=/run/metarill-redis.conf,readonly",
            IMAGE, "redis-server", "/run/metarill-redis.conf",
        )
        actual = server.inspect("container", root.name)
        if actual is None:
            raise RuntimeError("本轮 Redis 创建后未找到归属")
        server.require_owned(actual, item, identity)
        server.docker("container", "start", actual[0])
        return {"containerId": actual[0], "remotePort": port, "created": True}

    actual = server.inspect("container", root.name)
    if actual is not None:
        server.require_owned(actual, item, identity)
    config = require_config(root, expected) if os.path.lexists(root) else None
    if actual is not None:
        if config is None:
            raise RuntimeError("Redis 容器缺少本轮私有配置归属，拒绝删除")
        server.docker("container", "rm", "--force", "--volumes", actual[0])
    if server.inspect("container", root.name) is not None:
        raise RuntimeError("本轮 Redis 容器仍存在")
    if config is not None:
        require_config(root, expected)
        config.unlink()
        root.rmdir()
    if os.path.lexists(root):
        raise RuntimeError("本轮 Redis 私有目录仍存在")
    return {"containerAbsent": True, "directoryAbsent": True, "cleaned": True}
