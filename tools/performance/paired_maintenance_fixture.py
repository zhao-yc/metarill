"""固定原维护 HTTP 的显式联接夹具；构造 SQL 和校验，不自行连接。"""
from __future__ import annotations

from typing import NamedTuple

import paired_identity_seed as identity
import paired_measurement_fixture as fixture
from paired_business_http import PIN, mysql_assertion_sql


class Join(NamedTuple):
    """两条明确物理缓存从相同源关系读取目标名称。"""
    table_id: str
    table: str
    target_id: str
    target: str
    foreign: str
    cache: str
    column_id: int

    @property
    def path(self):
        return self.target + ".name"


JOINS = {
    "contract": Join("810000000000000012", "sample_contract", "810000000000000011",
                     "sample_customer", "sample_customer_id", "sample_customer_name", 913885530265028374),
    "line": Join("810000000000000013", "sample_contract_line", "810000000000000012",
                 "sample_contract", "sample_contract_id", "sample_contract_name", 961028852551803014),
}
SOURCE_COUNT = 132
STALE = "维护压测旧缓存"
PATH = "/api/admin/sys/system_maintenance/backfill_join_columns"


def require(ok, position):
    """错误只包含固定断言位置，不回显私有元数据或 SQL。"""
    if not ok:
        raise ValueError("measurement-maintenance-" + position)


def validate_catalogs(java, csharp):
    """原 Java 无显式列，C# 现有固定目录必须正好具备相同的两个声明。"""
    require(csharp.get("yzProjectId") == java.get("yzProjectId") == str(identity.PROJECT), "project")
    tables = csharp.get("configTables", [])
    expected = {"810000000000000011": "sample_customer", **{join.table_id: join.table for join in JOINS.values()}}
    require(type(tables) is list and len(tables) == 3 and
            {row.get("id"): row.get("tableName") for row in tables} == expected, "csharp-tables")
    require(all(row.get("isRongyu") == 1 and row.get("scopeKind") == "global" for row in tables), "csharp-scope")
    java_tables, java_columns = java.get("configTables", []), java.get("configColumns", [])
    require(type(java_tables) is list and len(java_tables) == 3
            and {row.get("id"): row.get("tableName") for row in java_tables} == expected
            and all(row.get("isRongyu") == 1 for row in java_tables), "java-tables")
    java_expected = {(table_id, name) for table_id, table in expected.items() for name in fixture.COLUMNS[table]
                     if name not in {"id", *fixture.AUDIT_COLUMNS, *(join.cache for join in JOINS.values())}}
    require(type(java_columns) is list and len(java_columns) == len(java_expected)
            and {(column.get("configTableId"), column.get("columnName")) for column in java_columns} == java_expected
            and all(type(column.get("isJoinColumn")) is int and column["isJoinColumn"] == 0
                    and column.get("isVirtualColumn") == 0 for column in java_columns), "java-columns")
    columns = csharp.get("configColumns", [])
    expected_columns = {(table_id, name) for table_id, table in expected.items()
                        for name in fixture.COLUMNS[table]}
    require(type(columns) is list and len(columns) == len(expected_columns), "csharp-column-count")
    by_key = {(column.get("configTableId"), column.get("columnName")): column for column in columns}
    require(set(by_key) == expected_columns, "csharp-columns")
    require(all(type(column.get("isJoinColumn", 0)) is int and column.get("isJoinColumn", 0) in (0, 1) for column in columns), "csharp-join-flag")
    require({key for key, column in by_key.items() if column.get("isJoinColumn") == 1}
            == {(join.table_id, join.cache) for join in JOINS.values()}, "csharp-explicit-joins")
    for join in JOINS.values():
        relation = by_key.get((join.table_id, join.foreign), {})
        cache = by_key.get((join.table_id, join.cache), {})
        target = by_key.get((join.target_id, "name"), {})
        require(relation.get("linkConfigTableId") == join.target_id and relation.get("columnLength") == "50"
                and relation.get("logicalType") == "string", "csharp-relation")
        require(cache.get("id") == str(join.column_id) and cache.get("columnFullName") == join.path
                and cache.get("logicalType") == "string" and cache.get("columnLength") == "50"
                and cache.get("isVirtualColumn", 0) == 0 and target.get("logicalType") == "string", "csharp-cache")
    maintenance = "/admin/sys/system_maintenance/"
    require(all(not str(button.get("sql", "")).startswith(maintenance)
                for catalog in (java, csharp) for button in catalog.get("configButtons", [])), "existing-authorization")


def _pin():
    return f"@@hostname={identity.literal(PIN['hostname'])} AND @@server_uuid={identity.literal(PIN['serverUuid'])}"


def java_declarations_sql(run):
    """只为本轮 Java 库登记已有物理列；失败断开整批回滚。"""
    database = fixture.database_name(run, "java")
    table = f"{database}.config_column"
    relation_ids = ("82004", "82008")
    guards = [_pin()]
    values = []
    for relation_id, join in zip(relation_ids, JOINS.values()):
        table_id = identity.literal(join.table_id)
        cache = identity.literal(join.cache)
        guards.append(f"NOT EXISTS(SELECT 1 FROM {table} WHERE id={join.column_id} OR (config_table_id={table_id} AND (is_join_column=1 OR column_name={cache})))")
        guards.append(f"EXISTS(SELECT 1 FROM {table} WHERE id={relation_id} AND config_table_id={table_id} AND column_name={identity.literal(join.foreign)} AND delete_no IS NULL)")
        values.append("(" + ",".join(map(identity.literal, (
            join.column_id, join.cache, join.table_id, join.table, join.cache, join.path,
            str(identity.PROJECT), "nvarchar", "50", 1, 0, 0, 0, 30))) + ")")
    columns = "id,name,config_table_id,config_table_name,column_name,column_full_name,yz_project_id,column_type,column_length,is_join_column,is_virtual_column,is_db_not_null,is_sort,sort_no"
    return "\n".join(["START TRANSACTION;", *mysql_assertion_sql(" AND ".join(guards)),
                      f"INSERT INTO {table} ({columns}) VALUES " + ",".join(values) + ";",
                      *mysql_assertion_sql("ROW_COUNT()=2"), "COMMIT;"])


def sample(kind):
    """从原固定种子中选择超过一批的 132 条活动记录，保留准确旧值。"""
    join = JOINS[kind]
    columns = fixture.COLUMNS[join.table]
    cache_index = columns.index(join.cache)
    deleted_index = columns.index("delete_no")
    return tuple((row[0], row[cache_index]) for row in fixture.rows(join.table)
                 if row[deleted_index] is None)[:SOURCE_COUNT]


def damage_sql(run, side, kind):
    """仅污染原值完全匹配的精确 ID；所有重置操作发生在计时窗口外。"""
    database = fixture.database_name(run, side)
    join = JOINS[kind]
    records = sample(kind)
    require(len(records) == SOURCE_COUNT, "sample-size")
    table = f"{database}.{join.table}"
    ids = ",".join(str(identifier) for identifier, _ in records)
    matches = " OR ".join(f"(id={identifier} AND BINARY {join.cache}=BINARY {identity.literal(name)})"
                          for identifier, name in records)
    guard = f"({_pin()}) AND (SELECT COUNT(*) FROM {table} WHERE delete_no IS NULL AND ({matches}))={SOURCE_COUNT}"
    return "\n".join(["START TRANSACTION;", *mysql_assertion_sql(guard),
                      f"UPDATE {table} SET {join.cache}={identity.literal(STALE)} WHERE id IN ({ids}) AND delete_no IS NULL;",
                      *mysql_assertion_sql(f"ROW_COUNT()={SOURCE_COUNT}"), "COMMIT;"])


def verify_cache(run, side, kind, query, *, stale):
    """逐条按实际二进制值核对缓存，拒绝丢行、重排、NULL 和多余记录。"""
    join = JOINS[kind]
    records = sample(kind)
    ids = ",".join(str(identifier) for identifier, _ in records)
    database = fixture.database_name(run, side)
    actual = query(f"SELECT id,HEX({join.cache}) FROM {database}.{join.table} "
                   f"WHERE id IN ({ids}) AND delete_no IS NULL ORDER BY id;")
    expected = [f"{identifier}\t{(STALE if stale else name).encode('utf-8').hex().upper()}"
                for identifier, name in records]
    require(actual.splitlines() == expected, "stale-readback" if stale else "repaired-readback")


def verify_response(side, kind, body, *, repeated):
    """HTTP 必须返回非空的精确维护计划，Java JDBC 匹配行与 C# 变化行分开记录。"""
    join = JOINS[kind]
    require(side in ("java", "csharp") and type(body) is dict and body.get("code") == 200, "response")
    data = body.get("data")
    require(type(data) is dict and data.get("configTableId") == join.table_id
            and data.get("tableName") == join.table and type(data.get("joinColumnCount")) is int
            and data["joinColumnCount"] == 1, "published-plan")
    details = data.get("details")
    require(type(details) is list and len(details) == 1 and type(details[0]) is dict, "plan-details")
    item = details[0]
    paths = (join.path,) if side == "java" else (join.path, join.foreign + ".name")
    require(item.get("columnName") == join.cache and item.get("columnFullName") in paths
            and item.get("relationColumnName") == join.foreign and item.get("targetTableName") == join.target
            and item.get("targetColumnName") == "name", "join-details")
    changed = 0 if repeated else SOURCE_COUNT
    affected = data.get("affectedRows")
    allowed = {changed} if side == "csharp" else {changed, sum(row[fixture.COLUMNS[join.table].index("delete_no")] is None for row in fixture.rows(join.table))}
    require(type(affected) is int and affected in allowed and item.get("affectedRows") == affected, "affected-rows")
    return affected


def run_preflight(run, java, csharp, query, execute_owned, request):
    """双端先核原索引、种子和发布配置，再实跑维护及重复读回，预检不参与计时。"""
    validate_catalogs(java, csharp)
    before = {side: fixture.verify_readback(run, side, query, relation_indexes=True)
              for side in ("java", "csharp")}
    require(execute_owned(java_declarations_sql(run)) == "1\n1", "declarations")
    receipts = []
    for side in ("java", "csharp"):
        for kind, join in JOINS.items():
            require(execute_owned(damage_sql(run, side, kind)) == "1\n1", "damage")
            verify_cache(run, side, kind, query, stale=True)
            body = {"configTableId": join.table_id}
            affected = verify_response(side, kind, request(side, PATH, body), repeated=False)
            verify_cache(run, side, kind, query, stale=False)
            repeated = verify_response(side, kind, request(side, PATH, body), repeated=True)
            verify_cache(run, side, kind, query, stale=False)
            receipts.append({"side": side, "table": join.table, "affectedRows": affected,
                             "repeatAffectedRows": repeated, "physicalChanges": SOURCE_COUNT})
    after = {side: fixture.verify_readback(run, side, query, relation_indexes=True)
             for side in ("java", "csharp")}
    require(before == after, "complete-seed-drift")
    return receipts
