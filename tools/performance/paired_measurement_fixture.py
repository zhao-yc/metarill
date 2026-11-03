"""固定原三表的双端性能种子及独立逐值回读合同。"""
from __future__ import annotations

import hashlib
import re

import paired_identity_seed as seed
from paired_business_http import PIN, mysql_assertion_sql

# 含原审计列和显式时间，防止两端数据库默认值生成不同的物理样本。
AUDIT_COLUMNS = (
    "import_no", "delete_no", "insert_time", "update_time",
    "insert_manager_id", "update_manager_id", "insert_manager_real_name",
    "update_manager_real_name", "insert_user_name", "update_user_name",
)
COLUMNS = {
    "sample_customer": ("id", "name", "remarks", *AUDIT_COLUMNS),
    "sample_contract": ("id", "name", "sample_customer_id", "sample_customer_name", "amount", "remarks", "attachments", *AUDIT_COLUMNS),
    "sample_contract_line": ("id", "sample_contract_id", "sample_contract_name", "name", "amount", "remarks", *AUDIT_COLUMNS),
}
COUNTS = {"sample_customer": 512, "sample_contract": 2048, "sample_contract_line": 8192}
ID_BASE = 9007199254750000
STAMP = "2026-10-01 09:00:00"
DELETED = "2026-10-02 09:00:00"
INDEX_PROFILE = (
    ("index", 0, (("delete_no", "A", None),)),
    ("index", 1, (("id", "A", None),)),
    ("primary", 1, (("id", "A", None),)),
)


def audit(deleted):
    """固定物理审计列，删除样本使用真实墓碑值。"""
    return (None, DELETED if deleted else None, STAMP, STAMP, None, None, None, None, None, None)


def amount(index, multiplier):
    """以整数构造定点文本，保留十进制两位精度。"""
    cents = (index * multiplier) % 1000000
    return f"{cents // 100}.{cents % 100:02}"


def rows(table):
    """在两端生成同一有序分布，活动明细不会引用已删除的父。"""
    if table not in COLUMNS:
        raise ValueError("未知性能样本表")
    for index in range(COUNTS[table]):
        if table == "sample_customer":
            yield (ID_BASE + index, f"性能客户-{index:04}", None if index % 3 == 0 else "", *audit(index % 16 == 15))
            continue
        contract = index if table == "sample_contract" else index // 4
        customer = contract // 4
        contract_id = ID_BASE + 10000 + contract
        contract_name = f"性能合同-{contract:04}"
        contract_deleted = customer % 16 == 15 or contract % 16 == 11
        if table == "sample_contract":
            yield (contract_id, contract_name, str(ID_BASE + customer), f"性能客户-{customer:04}",
                   amount(index, 137), None if index % 3 == 0 else "性能备注", "[]", *audit(contract_deleted))
        else:
            yield (ID_BASE + 20000 + index, str(contract_id), contract_name, f"性能明细-{index:04}",
                   amount(index, 79), None if index % 5 == 0 else "性能明细备注", *audit(contract_deleted or index % 4 == 3))


def database_name(run, target):
    """SQL 只能指向本轮两库之一。"""
    if type(run) is not str or not re.fullmatch(r"[a-f0-9]{32}", run) or target not in ("java", "csharp"):
        raise ValueError("性能样本库范围无效")
    return f"metarill_fresh_{run[:16]}_paired_{target}"


def literal(value):
    """原定点值用固定文本交给字段转换，其余沿现有安全字面量。"""
    return "NULL" if value is None else seed.literal(value)


def insert_batches(run, target):
    """构造确定性的分批 INSERT，附带每批预期行数。"""
    database = database_name(run, target)
    for table, columns in COLUMNS.items():
        fields = ",".join(f"`{name}`" for name in columns)
        batch = []
        for row in rows(table):
            batch.append("(" + ",".join(literal(value) for value in row) + ")")
            if len(batch) == 128:
                yield f"INSERT INTO `{database}`.`{table}` ({fields}) VALUES " + ",".join(batch) + ";", len(batch)
                batch.clear()
        if batch:
            yield f"INSERT INTO `{database}`.`{table}` ({fields}) VALUES " + ",".join(batch) + ";", len(batch)


def layout_scope(database):
    """生成只涉及原三表的固定信息模式范围。"""
    tables = ",".join(seed.literal(table) for table in COLUMNS)
    return f"TABLE_SCHEMA={seed.literal(database)} AND TABLE_NAME IN ({tables})"


def seed_transaction_sql(run, target):
    """生成须由已批准协调器使用守护客户端执行的独占空表种子事务。"""
    database = database_name(run, target)
    instance = f"@@hostname={seed.literal(PIN['hostname'])} AND @@server_uuid={seed.literal(PIN['serverUuid'])}"
    engines = f"(SELECT COUNT(*) FROM information_schema.TABLES WHERE {layout_scope(database)} AND ENGINE='InnoDB' AND TABLE_COLLATION='utf8mb4_unicode_ci')=3"
    empty = [f"NOT EXISTS(SELECT 1 FROM `{database}`.`{table}`)" for table in COLUMNS]
    batch = ["START TRANSACTION;", *mysql_assertion_sql(" AND ".join((instance, engines, *empty))), "SET @mt_measurement_ok=1;"]
    for sql, count in insert_batches(run, target):
        batch.extend((sql, f"SET @mt_measurement_ok=@mt_measurement_ok AND ROW_COUNT()={count};"))
    batch.extend(mysql_assertion_sql("@mt_measurement_ok=1"))
    batch.append("COMMIT;")
    return "\n".join(batch)+"\n"


def readback_sql(run, target, table):
    """有界读回全部固定字段，按字节区分空串、NULL、中文和精确字符串 ID。"""
    database = database_name(run, target)
    if table not in COLUMNS:
        raise ValueError("未知性能样本表")
    fields = ",".join(f"IF(`{name}` IS NULL,'N',CONCAT('H',HEX(CAST(`{name}` AS CHAR CHARACTER SET utf8mb4))))" for name in COLUMNS[table])
    return f"SELECT {fields} FROM `{database}`.`{table}` ORDER BY `id` LIMIT {COUNTS[table]+1};"


def expected_wire(table):
    """生成可与 MySQL 文本逐字比较的固定行，不把数字转成浮点。"""
    for row in rows(table):
        yield "\t".join("N" if value is None else "H"+str(value).encode("utf-8").hex().upper() for value in row)


def index_sql(run, target):
    """读取实际索引属性，包含排序、前缀、可见性和存储方法。"""
    database = database_name(run, target)
    return ("SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,SEQ_IN_INDEX,COALESCE(COLUMN_NAME,''),"
            "COALESCE(COLLATION,''),COALESCE(CAST(SUB_PART AS CHAR),''),INDEX_TYPE,IS_VISIBLE "
            "FROM information_schema.STATISTICS "
            f"WHERE {layout_scope(database)} "
            "ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX LIMIT 257;")


def verify_indexes(raw):
    """索引名字可不同，主键、唯一性、列顺序、前缀、方向及可见性必须匹配。"""
    by_table = {table: {} for table in COLUMNS}
    for line in raw.splitlines():
        fields = line.split("\t")
        if len(fields) != 9:
            raise ValueError("性能索引回执字段不符")
        table,name,non_unique,sequence,column,direction,prefix,kind,visible = fields
        if table not in by_table or not name or non_unique not in ("0","1") or kind != "BTREE" or visible != "YES":
            raise ValueError("性能索引属性不符")
        identity = (name, "primary" if name == "PRIMARY" else "index", 1-int(non_unique))
        columns = by_table[table].setdefault(identity, [])
        if sequence != str(len(columns)+1) or not column or direction != "A" or prefix:
            raise ValueError("性能索引列顺序、方向或前缀不符")
        columns.append((column,direction,None))
    for indexes in by_table.values():
        signature=tuple(sorted((category,unique,tuple(columns)) for (_,category,unique),columns in indexes.items()))
        if signature != INDEX_PROFILE:
            raise ValueError("两端必须使用固定三表索引")


def column_layout_sql(database):
    """读取同条件布局所需的列属性，避免将显示宽度或列声明顺序当成类型差异。"""
    return ("SELECT TABLE_NAME,COLUMN_NAME,DATA_TYPE,IS_NULLABLE,"
            "COALESCE(CHARACTER_MAXIMUM_LENGTH,0),COALESCE(NUMERIC_PRECISION,0),"
            "COALESCE(NUMERIC_SCALE,0),COALESCE(DATETIME_PRECISION,0),COALESCE(COLLATION_NAME,''),"
            "CONCAT('D',CASE WHEN LOWER(COLUMN_DEFAULT) IN ('current_timestamp','current_timestamp()') THEN 'current_timestamp' "
            "ELSE COALESCE(LOWER(COLUMN_DEFAULT),'') END) FROM information_schema.COLUMNS "
            f"WHERE {layout_scope(database)} ORDER BY TABLE_NAME,COLUMN_NAME LIMIT 47;")


def expected_column_layout():
    """来自原 46 列 DDL，固定范围和会影响查询的字符串排序规则。"""
    widths = {
        "name":100, "remarks":100, "import_no":45,
        "sample_customer_id":50, "sample_customer_name":50,
        "sample_contract_id":50, "sample_contract_name":50,
        "insert_manager_real_name":50, "update_manager_real_name":50,
        "insert_user_name":45, "update_user_name":45,
    }
    for table in sorted(COLUMNS):
        for name in sorted(COLUMNS[table]):
            default = ""
            if name in ("id","insert_manager_id","update_manager_id"):
                shape = ("bigint", "NO" if name=="id" else "YES", 0, 19, 0, 0, "")
            elif name in ("delete_no","insert_time","update_time"):
                shape = ("datetime","YES",0,0,0,0,"")
                default = "" if name=="delete_no" else "current_timestamp"
            elif name == "amount":
                shape = ("decimal","YES",0,18,2,0,"")
            else:
                shape = ("longtext","YES",4294967295,0,0,0,"utf8mb4_unicode_ci") if name=="attachments" else ("varchar","YES",widths[name],0,0,0,"utf8mb4_unicode_ci")
            yield "\t".join(map(str,(table,name,*shape,"D"+default)))


def verify_readback(run, target, query):
    """逐字核对独立读回，回执仅保留摘要和固定行数。"""
    database = database_name(run, target)
    actual_pin = query("SELECT @@hostname,@@server_uuid;")
    if actual_pin != PIN["hostname"]+"\t"+PIN["serverUuid"]:
        raise ValueError("性能样本实例不符")
    layout = query(f"SELECT TABLE_NAME,ENGINE,TABLE_COLLATION FROM information_schema.TABLES WHERE {layout_scope(database)} ORDER BY TABLE_NAME;")
    if layout.splitlines() != [table+"\tInnoDB\tutf8mb4_unicode_ci" for table in sorted(COLUMNS)]:
        raise ValueError("性能样本引擎或排序规则不符")
    columns = query(column_layout_sql(database)).splitlines()
    if columns != list(expected_column_layout()):
        raise ValueError("性能样本列类型、默认值或排序规则不符")
    hashes={}
    for table,count in COUNTS.items():
        actual=query(readback_sql(run,target,table)).splitlines()
        if len(actual)!=count or any(observed!=expected for observed,expected in zip(actual,expected_wire(table))):
            raise ValueError(f"性能样本物理回读不符：{table}")
        hashes[table]=hashlib.sha256(("\n".join(actual)+"\n").encode("ascii")).hexdigest()
    verify_indexes(query(index_sql(run,target)))
    return {"target":target,"rows":dict(COUNTS),"tableSha256":hashes,"physicalReadbackVerified":True,"fixedIndexesVerified":True}
