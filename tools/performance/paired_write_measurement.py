"""原客户页写入回放和独占样本只读核验合同。"""
from __future__ import annotations

from threading import Event

import paired_http as http
from paired_business_http import ROOTS, record_id
from paired_measurement import run_trials
from paired_measurement_fixture import database_name, write_sample_condition
from server200_paired_resources import WRITE_REQUEST_BUDGET

SCENARIO = "customer-crud"
CONCURRENCY = (1, 4)
ROUNDS = 3
ITERATIONS = 100
WARMUP = 8
NAME_PATTERN = "perf-write-${run}-${phase}-${concurrency}-${round}-${iteration}"
REMARKS = "性能写入备注"
RECORD = "${record}"


def write_scenario():
    """比较原 R 响应，只排除两端各自分配的创建 ID。"""
    root = ROOTS["customer"]
    updated = NAME_PATTERN + "-u"

    def step(method, path, checks, body=None):
        """为每个真实接口固定成功码与独立业务断言。"""
        result = {
            "method": method, "path": root + path, "status": 200,
            "checks": [{"pointer": field, "equals": value} for field, value in {"/code": 200, **checks}.items()],
        }
        if body is not None:
            result["json"] = body
        return result

    def record(name, remarks):
        """所有回读及更新都核对原 ID、名称和备注。"""
        return {"/data/id": RECORD, "/data/name": name, "/data/remarks": remarks}

    created = step("POST", "/add", {"/data/name": NAME_PATTERN, "/data/remarks": REMARKS},
                   {"name": NAME_PATTERN, "remarks": REMARKS})
    created["capture"] = {"record": "/data/id"}
    steps = [
        created,
        step("GET", "/detail/" + RECORD, record(NAME_PATTERN, REMARKS)),
        step("POST", "/update", record(updated, REMARKS), {"id": RECORD, "name": updated}),
        step("POST", "/update", record(updated, None), {"id": RECORD, "remarks": None}),
        step("GET", "/detail/" + RECORD, record(updated, None)),
        step("DELETE", "", {"/data": "删除成功"}, {"id": RECORD}),
        step("POST", "/count", {"/data": 0}, {"pageNo": 1, "pageSize": 20, "filters": {"name": updated}}),
    ]
    for current in steps[:5]:
        # 详情和更新先逐一比对各自的捕获 ID，再为双端摘要消除这一处动态值。
        current["ignorePointers"] = ["/data/id"]
    return {"name": SCENARIO, "steps": steps}


def config_for(run, targets, tokens):
    """固定写入次数与普通写者身份。调用方必须另有独占写入授权。"""
    config = http.validate({
        "version": 1, "runId": run, "transportProof": "server200-loopback-v1",
        "rounds": ROUNDS, "iterations": ITERATIONS, "warmupIterations": WARMUP,
        "concurrency": list(CONCURRENCY), "requestTimeoutSeconds": 5,
        "maxP95Ratio": 1.10, "minThroughputRatio": 0.95,
        "targets": {side: {**targets[side], "headers": {"loginStateId": tokens[side, "writer"]}}
                    for side in ("java", "csharp")},
        "scenarios": [write_scenario()],
    })
    if 2 * len(expected_names(run)) * len(config["scenarios"][0]["steps"]) != WRITE_REQUEST_BUDGET:
        raise ValueError("measurement-write-budget")
    return config


def write_request(target, step, variables, timeout):
    """创建响应的 ID 必须是精确字符串，验证通过后才能驱动后续写入。"""
    fingerprint = http.request(target, step, variables, timeout)
    if "capture" in step:
        record_id(variables["record"])
    return fingerprint


def expected_names(run):
    """重建每个预热和正式场景最终使用的唯一名称。"""
    database_name(run, "java")
    return {
        http.expand(NAME_PATTERN, {"run": run, "phase": phase, "concurrency": concurrency,
                                  "round": round_index, "iteration": iteration}) + "-u"
        for concurrency in CONCURRENCY
        for phase, round_index, count in (
            ("warmup", -1, max(WARMUP, concurrency)),
            *(("measured", index, ITERATIONS) for index in range(ROUNDS)),
        )
        for iteration in range(count)
    }


def readback_sql(run, side):
    """只读取精确双库的客户表及本轮前缀，超出预算也能被发现。"""
    database = database_name(run, side)
    return (
        "SELECT id,HEX(name),IF(remarks IS NULL,1,0),IF(delete_no IS NULL,0,1) "
        f"FROM `{database}`.`sample_customer` "
        f"WHERE {write_sample_condition(run)} "
        f"LIMIT {len(expected_names(run)) + 1};"
    )


def verify_readback(run, data, *, before=False):
    """开跑前必须无样本，完成后逐名核对 ID、清空备注及墓碑。"""
    expected = expected_names(run)
    if before:
        expected.clear()
    names, identifiers = set(), set()
    for line in data.splitlines():
        fields = line.split("\t")
        if len(fields) != 4 or fields[2:] != ["1", "1"]:
            raise ValueError("写入样本缺少空备注或软删除证明")
        identifier = record_id(fields[0])
        try:
            name = bytes.fromhex(fields[1]).decode("ascii")
        except ValueError:
            raise ValueError("写入样本名称编码无效") from None
        if identifier in identifiers or name in names or name not in expected:
            raise ValueError("发现重复或额外的写入样本")
        identifiers.add(identifier)
        names.add(name)
    if names != expected:
        raise ValueError("缺少已计划的写入样本")
    return {"verifiedRows": len(names)}


def run_write_measurement(root, run, targets, tokens, probe, query):
    """写入正式阶段沿用统一测量器，保留失败证据并阻止后续请求。"""
    config = config_for(run, targets, tokens)
    output = root / "measurement"
    output.mkdir(mode=0o700)
    stopped = Event()
    first_results = {}

    def guarded_request(target, step, variables, timeout):
        """首个错误后拒绝后续请求，已发出的并发请求由本轮归属清理。"""
        if stopped.is_set():
            raise ValueError("measurement-write-stopped")
        try:
            return write_request(target, step, variables, timeout)
        except Exception:
            stopped.set()
            raise

    def check_event(event):
        """日志刷盘后再拒绝失败轮次或两端预热响应差异。"""
        if event["kind"] not in ("warmup", "trial"):
            return
        trial = event["trial"]
        if trial["failedScenarios"]:
            raise ValueError("measurement-write-http-failed")
        key = (event["kind"], event["concurrency"], event.get("round"))
        values = [row["fingerprints"] for row in trial["rows"]]
        if key not in first_results:
            first_results[key] = values
        elif first_results.pop(key) != values:
            raise ValueError("measurement-write-response-mismatch")

    return run_trials(output, config, probe, query, "original-customer-write",
                      transport=guarded_request, check_event=check_event)
