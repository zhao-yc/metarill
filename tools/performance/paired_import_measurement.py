"""原三页 Excel 追加导入的同条件回放，工作簿准备和实库读回均在计时之外。"""
import base64
import hashlib
from collections import Counter
from decimal import Decimal
from threading import Event

import paired_http as http
import paired_measurement_fixture as fixture
import paired_workbook_container as oracle
from paired_business_http import ROOTS
from paired_measurement import run_trials
from server200_paired_resources import EXCEL_IMPORT_REQUEST_BUDGET, UNICODE_OBSERVED_IMAGES

PROFILE = "paired-original-excel-append-v1"
JAVA_CATALOG_SHA256 = "1e539b76af4ab434daaf15e6e4054a5184ee6c87aa6188daaeb596e5a88f17a8"
CONCURRENCY = (1, 4)
ROUNDS = 3
ITERATIONS = 100
WARMUP = 8
BATCH_ROWS = 8
REPLAYS = len(CONCURRENCY) * (ROUNDS * ITERATIONS + WARMUP)
REQUESTS = EXCEL_IMPORT_REQUEST_BUDGET
TABLES = (
    ("customer", "sample_customer", "81001", ("客户名称", "备注")),
    ("contract", "sample_contract", "81002", ("合同名称", "客户", "合同金额", "备注", "合同附件")),
    ("line", "sample_contract_line", "81003", ("明细名称", "明细金额", "备注")),
)


def require(ok, position):
    """只向上层报告断言位置，保留原计时与物理证据。"""
    if not ok:
        raise ValueError("measurement-import-" + position)


def verify_java_catalog(raw):
    """同一导入模式和原父页配置由已验证 Java 初始化材料固定。"""
    checksum = hashlib.sha256(raw).hexdigest()
    require(checksum == JAVA_CATALOG_SHA256, "java-catalog")
    return checksum


def workbook_requests(run):
    """从相同原种子挑选已验证的活动父记录，生成三份实际待导入工作簿。"""
    prefix = fixture.import_sample_prefix(run)
    result = {}
    for kind, _, _, headers in TABLES:
        rows = []
        for index in range(BATCH_ROWS):
            name = f"{prefix}{kind}-{index}"
            note = f"导入备注{index}"
            if kind == "customer":
                rows.append([name, note])
            elif kind == "contract":
                rows.append([name, "性能客户-0000", index + 0.25, note, "[]"])
            else:
                rows.append([name, index + 0.5, note])
        result[kind] = {"version": 1, "operation": "create", "headers": list(headers), "rows": rows}
    return result


def prepare_workbooks(run, invoke):
    """使用实际 SheetJS 创建后重新解析原始 XLSX，校验字节与传给 API 的值。"""
    result = {}
    for kind, request in workbook_requests(run).items():
        workbook = oracle.verify_result(http.canonical(invoke(request)), request)
        result[kind] = workbook
    return result


def config_for(run, targets, tokens, workbooks):
    """严格验证原列名和工作簿原始字节，原三个导入路由全部显式断言条数。"""
    require(set(targets) == {"java", "csharp"} and all(
        target.get("expectedImageId") == UNICODE_OBSERVED_IMAGES[side] and target.get("containerPort") == 8080
        for side, target in targets.items()), "targets")
    requests = workbook_requests(run)
    require(set(workbooks) == set(requests), "workbooks")
    steps = []
    for kind, _, page, _ in TABLES:
        workbook = oracle.verify_result(http.canonical(workbooks[kind]), requests[kind])
        body = {"requestJson": workbook["rows"]}
        if kind == "line":
            body["parentId"] = str(fixture.ID_BASE + 10000)
        steps.append({"method": "POST", "path": ROOTS[kind].removeprefix("/api") + "/importDatas" + page,
                      "status": 200, "json": body, "checks": [
                          {"pointer": "/code", "equals": 200},
                          {"pointer": "/data", "equals": f"导入成功{BATCH_ROWS}条数据"},
                      ]})
    config = http.validate({"version": 1, "runId": run, "transportProof": "server200-loopback-v1",
                            "rounds": ROUNDS, "iterations": ITERATIONS, "warmupIterations": WARMUP,
                            "concurrency": list(CONCURRENCY), "requestTimeoutSeconds": 15,
                            "maxP95Ratio": 1.10, "minThroughputRatio": 0.95,
                            "targets": {side: {**targets[side], "headers": {"loginStateId": tokens[side, "writer"]}}
                                        for side in ("java", "csharp")},
                            "scenarios": [{"name": "original-excel-import", "steps": steps}]})
    require(2 * REPLAYS * len(steps) == REQUESTS, "budget")
    return config


def sample_sql(run, side, kind):
    """只读本轮三表新增范围，校验 ID 和父键、缓存值、金额及附件形状。"""
    database = fixture.database_name(run, side)
    table = next(table for current, table, _, _ in TABLES if current == kind)
    common = "HEX(name),HEX(remarks),IF(delete_no IS NULL,1,0),IF(id BETWEEN "
    common += f"{fixture.ID_BASE} AND {fixture.ID_BASE + 29999},0,1)"
    attachments = "COALESCE(NULLIF(attachments,''),'[]')"
    fields = {
        "customer": "",
        "contract": (",sample_customer_id,HEX(sample_customer_name),amount,"
                     f"IF(JSON_VALID({attachments}),IF(JSON_TYPE({attachments})='ARRAY',JSON_LENGTH({attachments}),-1),-1)"),
        "line": ",sample_contract_id,HEX(sample_contract_name),amount",
    }
    return (f"SELECT id,{common}{fields[kind]} FROM `{database}`.`{table}` WHERE "
            f"{fixture.import_sample_condition(run)} ORDER BY id LIMIT {BATCH_ROWS * REPLAYS + 1};")


def verify_readback(run, side, query, *, before=False, replays=REPLAYS):
    """逐行验证预热和正式写入，并以每个原格值的精确重复次数校验追加语义。"""
    require(replays in (WARMUP, ROUNDS * ITERATIONS + 2 * WARMUP, REPLAYS), "readback-budget")
    requests = workbook_requests(run)
    facts = {}
    for kind, _, _, _ in TABLES:
        raw = query(sample_sql(run, side, kind))
        if before:
            require(not raw.strip(), "sample-collision")
            continue
        expected = Counter({row[0]: replays for row in requests[kind]["rows"]})
        by_name = {row[0]: row for row in requests[kind]["rows"]}
        ids = set()
        for row in raw.splitlines():
            values = row.split("\t")
            require(len(values) == {"customer": 5, "contract": 9, "line": 8}[kind], "sample-shape")
            identifier, name, note, active, outside_seed, *extra = values
            require(identifier.isascii() and identifier.isdecimal() and 0 < int(identifier) <= 2**63 - 1
                    and identifier not in ids and active == outside_seed == "1", "sample-identity")
            ids.add(identifier)
            name, note = (bytes.fromhex(value).decode("utf-8") for value in (name, note))
            require(expected[name] > 0, "sample-name")
            expected[name] -= 1
            source = by_name[name]
            require(note == source[-2 if kind == "contract" else -1], "sample-remarks")
            if kind != "customer":
                parent_id, parent_name, amount, *files = extra
                expected_parent = fixture.ID_BASE + (10000 if kind == "line" else 0)
                require(parent_id == str(expected_parent) and bytes.fromhex(parent_name).decode("utf-8") ==
                        ("性能合同-0000" if kind == "line" else "性能客户-0000"), "sample-parent")
                require(Decimal(amount) == Decimal(str(source[1 if kind == "line" else 2])), "sample-amount")
                if kind == "contract":
                    require(files == ["0"], "sample-files")
        require(not any(expected.values()), "sample-count")
        facts[kind] = len(ids)
    return facts


def run_import_measurement(root, run, targets, tokens, probe, query, make_workbook):
    """读回空前缀后准备真实工作簿，原三页导入计时结束再逐条回读。"""
    output = root / "measurement"
    output.mkdir(mode=0o700)

    def snapshot(phase, side):
        """先完整保留原始读回，即使另一端字段校验失败也可独立复核。"""
        rows = {}
        for kind, _, _, _ in TABLES:
            sql = sample_sql(run, side, kind)
            raw = query(sql)
            (output / f"import-{phase}-{side}-{kind}.tsv").write_bytes(raw.encode("utf-8"))
            rows[sql] = raw
        return rows.__getitem__

    for side in ("java", "csharp"):
        verify_readback(run, side, snapshot("before", side), before=True)
    workbooks = prepare_workbooks(run, make_workbook)
    config = config_for(run, targets, tokens, workbooks)
    for kind, workbook in workbooks.items():
        (output / (kind + ".xlsx")).write_bytes(base64.b64decode(workbook["workbookBase64"], validate=True))
    stopped = Event()

    def send(target, step, variables, timeout):
        """失败即停后续请求，错误由原测量器记录，禁止自动重放。"""
        require(not stopped.is_set(), "stopped")
        try:
            return http.request(target, step, variables, timeout)
        except Exception:
            stopped.set()
            raise

    warmups = {}
    warmup_readbacks = {}

    def check_warmup(event):
        """两端预热回包及实际追加都匹配后，才允许进入对应并发档位的正式回放。"""
        if event["kind"] != "warmup" or event["trial"]["failedScenarios"]:
            return
        concurrency, target = event["concurrency"], event["target"]
        rows = event["trial"]["rows"]
        require(len(rows) == WARMUP and all(len(row["fingerprints"]) == len(TABLES) for row in rows), "warmup-count")
        warmups[concurrency, target] = [row["fingerprints"] for row in rows]
        if target == "csharp":
            require(warmups[concurrency, "java"] == warmups[concurrency, "csharp"], "warmup-response")
            replays = WARMUP if concurrency == CONCURRENCY[0] else ROUNDS * ITERATIONS + 2 * WARMUP
            snapshots = {side: snapshot(f"warmup-{concurrency}", side) for side in ("java", "csharp")}
            warmup_readbacks[str(concurrency)] = {
                side: verify_readback(run, side, raw, replays=replays) for side, raw in snapshots.items()
            }
            (output / "warmup-readback.json").write_bytes(http.canonical(warmup_readbacks))

    result = run_trials(output, config, probe, query, PROFILE, transport=send, check_event=check_warmup)
    if result["measurementValid"]:
        require(set(warmup_readbacks) == {str(value) for value in CONCURRENCY}, "warmup-incomplete")
        snapshots = {side: snapshot("after", side) for side in ("java", "csharp")}
        result["importReadback"] = {side: verify_readback(run, side, raw) for side, raw in snapshots.items()}
    result["workbooks"] = {kind: {"sha256": item["sha256"], "rows": len(item["rows"])} for kind, item in workbooks.items()}
    return result
