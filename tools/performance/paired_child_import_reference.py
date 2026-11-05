"""用原子页协议验证修正 Java 对照包的实际持久化和整批回滚。"""
import base64
from decimal import Decimal

import paired_http as http
import paired_import_measurement as workbook
import paired_measurement_fixture as fixture
from paired_business_http import ROOTS, expect_child_record, record_id
from server200_paired_resources import EXCEL_CHILD_REFERENCE_REQUESTS

PARENT_ID = fixture.ID_BASE + 10000
DELETED_PARENT_ID = PARENT_ID + 11
MISSING_PARENT_ID = PARENT_ID + 100000
IMPORT = ROOTS["line"] + "/importDatas81003"
PAGE = ROOTS["line"] + "/page"


def verify_rows(raw, expected):
    """逐值核对物理父级、缓存、金额、备注及活动状态，再生成页面预期。"""
    records = {}
    names = set()
    for line in raw.splitlines():
        identifier, name, note, active, outside_seed, parent, cache, amount = line.split("\t")
        identifier = record_id(identifier)
        name, note = (bytes.fromhex(value).decode("utf-8") for value in (name, note))
        workbook.require(identifier not in records and name not in names and name in expected, "reference-row")
        workbook.require(active == outside_seed == "1" and parent == str(PARENT_ID) and
                         bytes.fromhex(cache).decode("utf-8") == "性能合同-0000", "reference-parent")
        workbook.require(note == expected[name]["备注"] and Decimal(amount) == Decimal(str(expected[name]["明细金额"])),
                         "reference-value")
        records[identifier] = dict(id=identifier, name=name, amount=expected[name]["明细金额"], remarks=note)
        names.add(name)
    workbook.require(names == set(expected), "reference-count")
    return records


def run_preflight(root, run, query, send, make_workbook):
    """固定两端各五个原 HTTP 请求，任何负例失败即停并留原物理证据。"""
    output = root / "measurement"
    output.mkdir(mode=0o700)
    positive = workbook.workbook_requests(run)["line"]
    prefix = fixture.import_sample_prefix(run)
    bad = {**positive, "rows": [[prefix + "rollback-first", 1.25, "末行失败前的合法写入"],
                               [prefix + "rollback-last-" + "长" * 2048, 2.50, "超出名称列长度"]]}
    parsed = {}
    for kind, request in (("positive", positive), ("rollback", bad)):
        result = workbook.oracle.verify_result(http.canonical(make_workbook(request)), request)
        (output / f"child-reference-{kind}.xlsx").write_bytes(base64.b64decode(result["workbookBase64"], validate=True))
        parsed[kind] = result
    expected = {row["明细名称"]: row for row in parsed["positive"]["rows"]}
    facts = {}
    requests = 0

    def call(side, step, path, body):
        """保存每次请求位置和状态，禁止把业务拒绝误记为物理回滚。"""
        nonlocal requests
        requests += 1
        workbook.require(requests <= EXCEL_CHILD_REFERENCE_REQUESTS, "reference-budget")
        status, payload = send(side, path, body)
        code = payload.get("code") if isinstance(payload, dict) else None
        with (output / "child-reference-events.jsonl").open("ab") as journal:
            journal.write(http.canonical(dict(side=side, step=step, http=status, code=code)) + b"\n")
        return status, code, payload

    for side in ("java", "csharp"):
        database = fixture.database_name(run, side)
        parent_table = f"`{database}`.`sample_contract`"
        parent_sql = (f"SELECT id,IF(delete_no IS NULL,1,0) FROM {parent_table} "
                      f"WHERE id IN ({PARENT_ID},{DELETED_PARENT_ID},{MISSING_PARENT_ID}) ORDER BY id;")
        workbook.require(query(parent_sql) == f"{PARENT_ID}\t1\n{DELETED_PARENT_ID}\t0", "reference-parent-fixture")
        limit = query(f"SELECT CHARACTER_MAXIMUM_LENGTH FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='{database}' "
                      "AND TABLE_NAME='sample_contract_line' AND COLUMN_NAME='name';")
        workbook.require(limit.isascii() and limit.isdecimal() and len(bad["rows"][0][0]) <= int(limit) <
                         len(bad["rows"][1][0]), "reference-length-fixture")
        sql = workbook.sample_sql(run, side, "line")

        def snapshot(step):
            raw = query(sql)
            (output / f"child-reference-{side}-{step}.tsv").write_bytes(raw.encode("utf-8"))
            return raw

        workbook.require(all(not query(workbook.sample_sql(run, side, kind)) for kind, *_ in workbook.TABLES),
                         "reference-before")
        status, code, payload = call(side, "positive", IMPORT, dict(parentId=str(PARENT_ID), requestJson=parsed["positive"]["rows"]))
        workbook.require((status, code, payload.get("data")) == (200, 200, f"导入成功{len(expected)}条数据"), "reference-success")
        baseline = snapshot("positive")
        records = verify_rows(baseline, expected)
        status, code, payload = call(side, "parent-page", PAGE, dict(parentId=str(PARENT_ID), pageNo=1, pageSize=20,
                                                                    filters={"name": prefix}))
        workbook.require((status, code) == (200, 200), "reference-page-response")
        page = payload["data"]
        ids = [record_id(row["id"]) for row in page["records"]]
        workbook.require(page["total"] == len(ids) == len(records) and set(ids) == set(records), "reference-page-scope")
        for identifier, row in zip(ids, page["records"]):
            expect_child_record(row, records[identifier])

        for step, parent, rows in (("missing-parent", MISSING_PARENT_ID, parsed["positive"]["rows"]),
                                    ("deleted-parent", DELETED_PARENT_ID, parsed["positive"]["rows"]),
                                    ("late-row-rollback", PARENT_ID, parsed["rollback"]["rows"])):
            status, code, _ = call(side, step, IMPORT, dict(parentId=str(parent), requestJson=rows))
            workbook.require(status in (200, 400, 500) and code not in (200, 401, 403) and
                             (status != 200 or type(code) is int), "reference-" + step + "-response")
            workbook.require(snapshot(step) == baseline, "reference-" + step + "-physical")
        workbook.require(all(not query(workbook.sample_sql(run, side, kind)) for kind in ("customer", "contract")),
                         "reference-other-tables")
        facts[side] = dict(rows=len(records), parentPage=True, missingParentRejected=True, deletedParentRejected=True,
                           lateRowRollback=True, nameColumnLimit=int(limit))
    workbook.require(requests == EXCEL_CHILD_REFERENCE_REQUESTS, "reference-budget")
    return dict(passed=True, requests=requests, timedRequests=0, sides=facts,
                workbooks={kind: item["sha256"] for kind, item in parsed.items()})
