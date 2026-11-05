"""按共同种子核对原三页导出；XLS 和 XLSX 分别验字节，按实际单元格比较。"""
import base64
import hashlib
import re
import urllib.request
from decimal import Decimal

from paired_business_http import ROOTS
from paired_extended_business import HEADERS
from server200_paired_resources import EXCEL_EXPORT_PREFLIGHT_REQUESTS
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
        cells = [Decimal(row[name]) if name == "amount" else row[name] for name in columns]
        matches.append((row["id"], cells))
    # 子页元数据没有名称排序项，原查询固定主键降序；主页面仍按有效名称排序请求。
    matches.sort(key=lambda match: -match[0] if kind == "line" else match[1][0])
    return [cells for _, cells in matches]


def ticket_step(kind):
    """按原 initExport 路由申请本次下载票据，明确筛选和父范围。"""
    body = {"filters": {}}
    if kind == "line":
        body["parentId"] = PARENT_ID
    else:
        body["filters"]["name"] = PREFIX[kind]
        body["orderBy"] = {"column": "name", "order": "ascending"}
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


def workbook_read(raw):
    """只把下载所得字节和独立摘要交给原工作簿解析器。"""
    return {"version": 1, "operation": "read", "base64": base64.b64encode(raw).decode("ascii"),
            "expectedSha256": hashlib.sha256(raw).hexdigest()}


def workbook_proof(side, kind, workbook):
    """保留语义和实际格式／字节证据；调用者先完成来源绑定。"""
    return {**verify_cells(side, kind, workbook), "byteLength": workbook["byteLength"],
            "sha256": workbook["sha256"], "format": workbook["format"]}


def verify_workbooks(samples, parse_workbook, *, max_batches=6):
    """对所有下载做计时外验证，相同语言、页面和字节可复用逐格结论。"""
    sample_keys = []
    unique = {}
    for side, kind, raw in samples:
        if side not in FORMAT or kind not in HEADERS or type(raw) is not bytes or not 0 < len(raw) <= MAX_DOWNLOAD:
            raise ValueError("measurement-export-replay-workbook")
        checksum = hashlib.sha256(raw).hexdigest()
        key = (side, kind, checksum)
        sample_keys.append(key)
        unique.setdefault(key, raw)
    if not sample_keys:
        raise ValueError("measurement-export-replay-empty")

    empty = {"version": 1, "operation": "read-batch", "workbooks": []}
    initial_bytes = len(http.canonical(empty))
    batches, pending, size = [], [], initial_bytes
    for key, raw in unique.items():
        item = workbook_read(raw)
        item_bytes = len(http.canonical(item))
        if initial_bytes + item_bytes > oracle.MAX_INPUT:
            raise ValueError("measurement-export-replay-workbook-size")
        if len(pending) == oracle.MAX_BATCH or size + item_bytes + bool(pending) > oracle.MAX_INPUT:
            batches.append(pending)
            pending, size = [], initial_bytes
        size += item_bytes + bool(pending)
        pending.append((key, item))
    batches.append(pending)
    # 调用者按当前阶段为原容器额度保留余量。
    if not 1 <= max_batches <= oracle.MAX_JOBS or len(batches) > max_batches:
        raise ValueError("measurement-export-replay-batches")
    proofs = {}
    for batch in batches:
        request = {**empty, "workbooks": [item for _, item in batch]}
        checked = oracle.verify_result(http.canonical(parse_workbook(request)), request)
        for (key, _), workbook in zip(batch, checked["workbooks"], strict=True):
            proofs[key] = workbook_proof(*key[:2], workbook)
    return [proofs[key] for key in sample_keys]


MAX_DOWNLOAD = 4 * 1024 * 1024
LOOPBACK_API = re.compile(r"http://127[.]0[.]0[.]1:(?:[23][0-9]{4}|40000)/api")


def fetch_download(base_url, ticket, *, limit=MAX_DOWNLOAD):
    """按原 openUrl 无会话头下载，禁止代理、跳转、压缩变换及超限实体。"""
    if not LOOPBACK_API.fullmatch(base_url):
        raise ValueError("measurement-export-loopback")
    if not isinstance(ticket, str) or not re.fullmatch(r"[A-Za-z0-9_-]{16,128}", ticket):
        raise ValueError("measurement-export-ticket")
    request = urllib.request.Request(base_url + "/admin/sys/export/export?exportId=" + ticket,
                                     headers={"Accept-Encoding": "identity"})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), http.NoRedirect())
    with opener.open(request, timeout=15) as response:
        if (response.status != 200 or response.headers.get("Content-Encoding", "identity").strip().lower() != "identity"
                or response.headers.get("Content-Range")):
            raise ValueError("measurement-export-download-response")
        lengths = response.headers.get_all("Content-Length", [])
        if lengths and (len(lengths) != 1 or not re.fullmatch(r"[0-9]{1,7}", lengths[0]) or response.headers.get("Transfer-Encoding")):
            raise ValueError("measurement-export-download-length")
        raw = response.read(limit + 1)
    if lengths and int(lengths[0]) != len(raw):
        raise ValueError("measurement-export-download-length")
    if not 0 < len(raw) <= limit:
        raise ValueError("measurement-export-download-size")
    return raw


def run_preflight(root, targets, tokens, parse_workbook, *, limit=MAX_DOWNLOAD, max_batches=6):
    """执行两端三组原票据与完整下载，校验实际文件后生成预检收据。"""
    if set(targets) != set(FORMAT):
        raise ValueError("measurement-export-targets")
    urls = [target["baseUrl"] for target in targets.values()]
    if any(not LOOPBACK_API.fullmatch(url) for url in urls) or len(set(urls)) != len(urls):
        raise ValueError("measurement-export-loopback")
    output = root / "export-preflight"
    output.mkdir(mode=0o700)
    facts = {side: {} for side in ("java", "csharp")}
    samples = []
    for side in facts:
        target = targets[side]
        ticket_target = {**target, "headers": {"loginStateId": tokens[side, "writer"]}}
        tickets = set()
        for kind in HEADERS:
            variables = {}
            http.request(ticket_target, ticket_step(kind), variables, 15)
            ticket = variables["exportId"]
            if not isinstance(ticket, str) or not re.fullmatch(r"[A-Za-z0-9_-]{16,128}", ticket):
                raise ValueError("measurement-export-ticket")
            if ticket in tickets:
                raise ValueError("measurement-export-ticket-reused")
            tickets.add(ticket)
            raw = fetch_download(target["baseUrl"], ticket, limit=limit)
            (output / f"{side}-{kind}.{FORMAT[side]}").write_bytes(raw)
            samples.append((side, kind, raw))
    for (side, kind, _), proof in zip(samples, verify_workbooks(samples, parse_workbook, max_batches=max_batches), strict=True):
        facts[side][kind] = proof
    (output / "receipts.json").write_bytes(http.canonical(facts))
    if any(facts["java"][kind]["semanticSha256"] != facts["csharp"][kind]["semanticSha256"] for kind in HEADERS):
        raise ValueError("measurement-export-semantics")
    return {"passed": True, "requests": EXCEL_EXPORT_PREFLIGHT_REQUESTS, "workbooks": facts, "measurementValid": False}
