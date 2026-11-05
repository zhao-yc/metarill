"""按共同种子核对原三页导出；XLS 和 XLSX 分别验字节，按实际单元格比较。"""
import base64
import hashlib
from decimal import Decimal

from paired_business_http import ROOTS
from paired_extended_business import HEADERS
import paired_http as http
import paired_measurement_fixture as fixture
import paired_workbook_container as oracle

FORMAT = {"java": "xls", "csharp": "xlsx"}
PARENT_ID = str(fixture.ID_BASE + 10000)
PREFIX = {"customer": "性能客户-00", "contract": "性能合同-00"}


def expected_export(kind):
    """用物理共同种子独立推导筛选、活动行、原列顺序及子页父范围。"""
    table = {"customer": "sample_customer", "contract": "sample_contract", "line": "sample_contract_line"}[kind]
    columns = {
        "customer": ("name", "remarks"),
        "contract": ("name", "sample_customer_name", "amount", "remarks", "attachments"),
        "line": ("name", "amount", "remarks"),
    }[kind]
    matches = []
    for physical in fixture.rows(table):
        row = dict(zip(fixture.COLUMNS[table], physical))
        if row["delete_no"] is not None:
            continue
        if kind == "line":
            if row["sample_contract_id"] != PARENT_ID:
                continue
        elif not row["name"].startswith(PREFIX[kind]):
            continue
        matches.append([Decimal(row[name]) if name == "amount" else row[name] for name in columns])
    return sorted(matches, key=lambda row: row[0])


def ticket_step(kind):
    """按原 initExport 路由申请本次下载票据，明确筛选和父范围。"""
    body = {"filters": {}, "orderBy": {"column": "name", "order": "ascending"}}
    if kind == "line":
        body["parentId"] = PARENT_ID
    else:
        body["filters"]["name"] = PREFIX[kind]
    return {"method": "POST", "path": ROOTS[kind].removeprefix("/api") + "/initExport", "status": 200,
            "json": body, "checks": [{"pointer": "/code", "equals": 200}],
            "capture": {"exportId": "/data"}, "ignorePointers": ["/data"]}


def verify_cells(side, kind, workbook):
    """严格验证格值类型和顺序，仅在预期为空备注时接受空格两种文件表示。"""
    headers = HEADERS[kind]
    expected = expected_export(kind)
    cells = workbook["cells"]
    if workbook["format"] != FORMAT[side] or workbook["headers"] != headers or cells[:1] != [headers]:
        raise ValueError("measurement-export-format-or-header")
    if len(cells) != len(expected) + 1:
        raise ValueError("measurement-export-row-count")
    for actual, wanted in zip(cells[1:], expected, strict=True):
        if len(actual) != len(headers):
            raise ValueError("measurement-export-column-count")
        for title, value, exact in zip(headers, actual, wanted, strict=True):
            if title == "备注" and exact in (None, "") and (value is None or value == ""):
                continue
            if isinstance(exact, Decimal):
                same = type(value) in (int, float) and Decimal(str(value)) == exact
            else:
                same = type(value) is type(exact) and value == exact
            if not same:
                raise ValueError("measurement-export-cell-value")
    # 摘要使用经过实际文件核验的共同预期，文件格式和实际字节摘要另行记录。
    semantic_rows = [[format(value, "f") if isinstance(value, Decimal) else value for value in row] for row in expected]
    return {"rows": len(expected), "semanticSha256": http.digest([headers, *semantic_rows])}


def verify_download(side, kind, raw, parse_workbook):
    """将本次真实响应交给锁定的原 SheetJS，逐字绑定响应与解析结果。"""
    request = {"version": 1, "operation": "read", "base64": base64.b64encode(raw).decode("ascii"),
               "expectedSha256": hashlib.sha256(raw).hexdigest()}
    workbook = oracle.verify_result(http.canonical(parse_workbook(request)), request)
    return {**verify_cells(side, kind, workbook), "byteLength": len(raw), "sha256": workbook["sha256"],
            "format": workbook["format"]}
