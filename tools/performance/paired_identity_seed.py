"""双端普通账号测试播种；表结构始终由原模板/元数据安装，不提供生产账号管理入口。"""
from __future__ import annotations
import base64
import hashlib
import json
import re

PROJECT = 810000000000000001
ADMIN = 85001
PAGES = {81001: 85011, 81002: 85012, 81003: 85013}
BUTTONS = (86000, 86001, 86002)
ACTIONS = ("is_query", "is_new", "is_update", "is_delete", "is_export", "is_all")
KINDS = ("writer", "reader", "denied")


def compact(value):
    """JSON集合保持字符串ID，双方语义一致，不通过浮点转换主键。"""
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def password_hashes(password, java_salt, csharp_salt):
    """测试密码使用两端原有摘要格式；不改变原产品密码算法。"""
    if not re.fullmatch(r"[a-f0-9]{64}", password) or not re.fullmatch(r"[a-f0-9]{32}", java_salt) or not isinstance(csharp_salt, bytes) or len(csharp_salt) != 24:
        raise ValueError("测试密码及两份盐格式无效")
    java = hashlib.md5((password + java_salt).encode("utf-8")).hexdigest()
    derived = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), csharp_salt, 350000, 32)
    csharp = "pbkdf2-sha256$350000$" + base64.b64encode(csharp_salt).decode("ascii") + "$" + base64.b64encode(derived).decode("ascii")
    return java, csharp


def prepare(run, credentials):
    """只构造双端对应种子，无文件/网络/数据库调用；账号角色均为普通身份。"""
    if not re.fullmatch(r"[a-f0-9]{32}", run) or set(credentials) != set(KINDS):
        raise ValueError("唯一运行号或三类测试账号不符")
    if len({credentials[k]["password"] for k in KINDS}) != 3:
        raise ValueError("三个账号必须使用不同测试密码")
    common = []; result = {target: {table: [] for table in ("yz_role", "yz_manager", "yz_role_permission")} for target in ("java", "csharp")}
    grant_ids = {target: 9007199254744201 for target in result}
    for index, kind in enumerate(KINDS):
        entry = credentials[kind]
        java_hash, csharp_hash = password_hashes(entry["password"], entry["javaSalt"], entry["csharpSalt"])
        account = 9007199254744001 + index; role = 9007199254744101 + index
        name = "paired-" + kind + "-" + run[:16]
        actions = list(ACTIONS) if kind == "writer" else ["is_query"] if kind == "reader" else []
        buttons = list(BUTTONS) if kind == "writer" else []
        common.append({"kind": kind, "accountId": str(account), "roleId": str(role), "name": name, "enabled": True, "super": False, "projectId": str(PROJECT), "adminTypeId": str(ADMIN), "pageActions": {str(p): actions for p in PAGES}, "buttons": [str(b) for b in buttons]})
        for target, rows in result.items():
            project = {"yz_project_id": PROJECT} if target == "csharp" else {}
            rows["yz_role"].append({"id": role, "name": "性能" + {"writer": "操作员", "reader": "只读员", "denied": "无权账号"}[kind], "is_super": 0, "is_scope": 0, "is_built_in": 0, "yz_admin_type_id": ADMIN, **project})
            rows["yz_manager"].append({"id": account, "name": name, "real_name": "性能" + kind, "password": java_hash if target == "java" else csharp_hash, "is_disabled": 1 if target == "java" else 0, "yz_admin_type_id": ADMIN, "yz_role_ids": compact([str(role)]), **({"salt": entry["javaSalt"], "is_super": 0} if target == "java" else project)})
            if not actions: continue
            for page, menu in PAGES.items():
                values = [*actions, *([str(b) for b in buttons] if target == "java" and page == 81002 else [])]
                rows["yz_role_permission"].append({"id": grant_ids[target], "yz_role_id": role, "permission_values": compact(values), **({"config_second_menu_id": menu} if target == "java" else {"config_page_id": page, **project})})
                grant_ids[target] += 1
            if target == "csharp":
                for button in buttons:
                    rows["yz_role_permission"].append({"id": grant_ids[target], "yz_role_id": role, "permission_values": "[]", "config_button_id": button, **project})
                    grant_ids[target] += 1
    return {"version": 1, "runId": run, "common": common, "rows": result}


def fingerprint(fixture):
    """上层固定完整种子摘要，执行时不能接入改过的列、角色或密码材料。"""
    return hashlib.sha256(json.dumps(fixture,ensure_ascii=False,sort_keys=True,separators=(",", ":")).encode("utf-8")).hexdigest()


def literal(value):
    """UTF8十六进制文本字面量保持低优先级，使用原列排序规则且不依赖转义模式。"""
    if type(value) is int: return str(value)
    # CONVERT(... USING utf8mb4)与原列同为隐式排序规则，IN／回读可能报1271／1267。
    if isinstance(value, str): return "_utf8mb4 0x" + value.encode("utf-8").hex() if value else "''"
    raise ValueError("播种只允许固定整数或文本值")


def statements(database, fixture, target):
    """只生成三类账号种子INSERT，不含DDL、UPDATE、DELETE或既有数据覆盖。"""
    expected = "metarill_fresh_" + fixture["runId"][:16] + "_paired_" + target
    if target not in ("java", "csharp") or database != expected or not re.fullmatch(r"[a-f0-9]{32}", fixture["runId"]):
        raise ValueError("播种目标只能是本轮精确双库")
    result = []
    for table in ("yz_role", "yz_manager", "yz_role_permission"):
        for row in fixture["rows"][target][table]:
            if any(not re.fullmatch(r"[a-z][a-z_]*", key) for key in row): raise ValueError("种子列名无效")
            if table == "yz_role" and row.get("is_super") != 0: raise ValueError("性能账号不能播种超级角色")
            columns = ",".join("`" + key + "`" for key in row)
            result.append(f"INSERT INTO `{database}`.`{table}` ({columns}) VALUES (" + ",".join(literal(v) for v in row.values()) + ");")
    return result


def authorize(lease, target, fixture):
    """执行前核用途/批准/唯一双库及实例pin；离线构造种子不代表得到写库授权。"""
    run = fixture["runId"]
    if lease.get("version") != 1 or lease.get("purpose") != "paired-identity-seed-v1" or lease.get("runId") != run or lease.get("authorizationStatus") != "approved" or lease.get("leaseStatus") != "running" or target not in ("java", "csharp"):
        raise ValueError("本轮没有可执行播种租约")
    if lease.get("fixtureSha256") != fingerprint(fixture): raise ValueError("冻结播种内容摘要不符")
    databases = ["metarill_fresh_" + run[:16] + "_paired_" + t for t in ("java", "csharp")]
    pin = lease.get("instancePin", {})
    if lease.get("databases") != databases or pin != {"hostname": "3107e214a8a3", "serverUuid": "6d3d024a-9d73-11f1-9939-ce69308a21dc"}:
        raise ValueError("双库或固定MySQL实例不符")
    return databases[0 if target == "java" else 1]


def apply(lease, target, fixture, query):
    """只供单次上层协调器调用；先核真实实例、后台与碰撞，再事务INSERT并逐值回读。"""
    database = authorize(lease, target, fixture)
    sql = statements(database, fixture, target)
    pin = lease["instancePin"]
    if query("SELECT @@hostname,@@server_uuid;") != pin["hostname"] + "\t" + pin["serverUuid"]:
        raise RuntimeError("真实MySQL实例与pin不符")
    realm = f"SELECT COUNT(*) FROM `{database}`.`yz_admin_type` WHERE id={ADMIN} AND yz_project_id={PROJECT} AND delete_no IS NULL;"
    if query(realm) != "1": raise RuntimeError("原后台归属尚未正确初始化")
    if target == "csharp" and query(f"SELECT COUNT(*) FROM `{database}`.`yz_project` WHERE id={PROJECT} AND delete_no IS NULL;") != "1":
        raise RuntimeError("C#原安全初始化未建立项目归属")
    # 不重跑、不幂等覆盖；即使只剩一条同ID或同名记录，也必须保留失败而非修补。
    for table, rows in fixture["rows"][target].items():
        ids = ",".join(str(row["id"]) for row in rows)
        names = " OR name IN (" + ",".join(literal(row["name"]) for row in rows) + ")" if table in ("yz_manager", "yz_role") else ""
        if query(f"SELECT COUNT(*) FROM `{database}`.`{table}` WHERE id IN ({ids}){names};") != "0":
            raise RuntimeError("测试账号、角色或授权种子已存在，拒绝覆盖")
    query("START TRANSACTION;\n" + "\n".join(sql) + "\nCOMMIT;")
    verified = 0
    for table, rows in fixture["rows"][target].items():
        for row in rows:
            # 只回传计数，密码摘要/盐/权限JSON不进入进程输出或公开回执。
            predicates = " AND ".join("`" + key + "` <=> " + literal(value) for key, value in row.items())
            if query(f"SELECT COUNT(*) FROM `{database}`.`{table}` WHERE {predicates} AND delete_no IS NULL;") != "1":
                raise RuntimeError("播种后逐字段独立回读不符")
            verified += 1
    return {"target": target, "ordinaryAccounts": 3, "verifiedRows": verified, "superAccounts": 0, "actualPasswordLoginVerified": False, "permissionBehaviorVerified": False}
