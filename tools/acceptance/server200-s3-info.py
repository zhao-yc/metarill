"""只读核验Server-200的既有S3服务；凭据模式仅供验收进程内存捕获，禁止打印到日志。"""
import base64
import json
import re
import socket
import subprocess
import sys


def inspect_server():
    """每次Docker读取前核实主机和地址，并显式使用该主机本地Docker套接字。"""
    if socket.gethostname() != "Server-200":
        raise RuntimeError("服务器身份不匹配")
    addresses = subprocess.check_output(["ip", "-4", "addr", "show"], text=True, timeout=10)
    if not re.search(r"\binet 192\.168\.31\.200/", addresses):
        raise RuntimeError("服务器地址不匹配")
    data = json.loads(subprocess.check_output([
        "docker", "-H", "unix:///var/run/docker.sock", "inspect", "yinzon-storage"
    ], text=True, timeout=20))
    if len(data) != 1 or data[0].get("State", {}).get("Status") != "running":
        raise RuntimeError("存储容器不在运行")
    container = data[0]
    ports = container.get("NetworkSettings", {}).get("Ports", {}).get("8333/tcp", [])
    if not any(p.get("HostIp") == "192.168.31.200" and p.get("HostPort") == "8333" for p in ports or []):
        raise RuntimeError("S3端口绑定不匹配")
    return container


def handle(payload):
    """inspect只给非敏感指纹；credentials必须同时匹配预先固定的容器和镜像。"""
    if not isinstance(payload, dict) or set(payload) - {"action", "expected"}:
        raise RuntimeError("只读检查参数无效")
    action = payload.get("action")
    if action not in {"inspect", "credentials"}:
        raise RuntimeError("不支持该操作")
    expected = payload.get("expected")
    if action == "credentials" and (
        not isinstance(expected, dict) or set(expected) != {"containerId", "imageId"} or
        not re.fullmatch(r"[a-f0-9]{64}", expected["containerId"]) or
        not re.fullmatch(r"sha256:[a-f0-9]{64}", expected["imageId"])
    ):
        raise RuntimeError("凭据读取缺少固定服务指纹")
    container = inspect_server()
    result = {
        "host": "Server-200", "address": "192.168.31.200", "containerId": container["Id"],
        "imageId": container["Image"], "imageName": container["Config"]["Image"],
        "endpoint": "http://192.168.31.200:8333",
    }
    if expected is not None and (
        result["containerId"] != expected.get("containerId") or result["imageId"] != expected.get("imageId")
    ):
        raise RuntimeError("存储服务指纹变化")
    environment = container["Config"].get("Env", [])
    names = ["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"]
    values = {}
    for name in names:
        found = [v[len(name) + 1:] for v in environment if v.startswith(name + "=")]
        if len(found) != 1 or not found[0]:
            raise RuntimeError("存储凭据来源不完整")
        values[name] = found[0]
    if action == "inspect":
        result["credentialNames"] = names
    else:
        # 仅由受租约保护的验收子进程消费，不落盘，也不打印容器其它环境变量。
        result["accessKey"] = values[names[0]]
        result["secretKey"] = values[names[1]]
    return result


def main():
    """参数仅为Base64 JSON，便于通过SSH传输脚本，避免命令行中出现凭据或任意shell片段。"""
    try:
        if len(sys.argv) != 2:
            raise RuntimeError("检查请求缺失")
        payload = json.loads(base64.b64decode(sys.argv[1], validate=True))
        print(json.dumps(handle(payload), ensure_ascii=True))
    except Exception:
        # 不回显Docker错误、环境变量或请求正文。
        print("S3只读服务检查失败。", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
