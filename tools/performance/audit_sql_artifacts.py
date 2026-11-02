"""严格核对私有SQL采集产物；只读文件，不执行网络、SQL或性能负载。"""
from __future__ import annotations
import argparse
from bisect import bisect_left
import hashlib
import json
import os
from pathlib import Path
import re
import stat
from paired_http import ContractError, read_json

COMMON = {"null", "bytes", "string", "boolean", "integer", "decimal", "double-bits", "single-bits", "datetime:Unspecified", "date", "time", "timespan", "guid"}
NATIVE = {"java-datetime-nanos": "java.time.LocalDateTime", "java-time-nanos": "java.time.LocalTime", "java-duration-nanos": "java.time.Duration"}
HEX = re.compile(r"[a-f0-9]{64}\Z")
SAMPLE = re.compile(r"[a-f0-9]{32}\Z")


def require(condition, code):
    """错误仅返回固定原因，不回显SQL、参数、客户端文本或私有路径。"""
    if not condition:
        raise ContractError(code)


def shape(value, keys):
    """每种v1/v2结构各自固定字段，拒绝未知或遗漏字段。"""
    require(type(value) is dict and set(value) == set(keys.split()), "artifact-fields")
    return value


def integer(value, minimum=0, maximum=2**63-1):
    """bool不属于计数；拒绝浮点、负预算及超范围JSON整数。"""
    require(type(value) is int and minimum <= value <= maximum, "artifact-integer")
    return value


def flag(value):
    """完整性标志必须是JSON布尔值，不能靠真值转换通过。"""
    require(type(value) is bool, "artifact-boolean")
    return value


def text(value, maximum=2048, nullable=False):
    """标识及类型名有界且不含控制字符；SQL另按多行文本处理。"""
    if value is None and nullable:
        return value
    require(type(value) is str and 0 < len(value) <= maximum and not any(ord(ch) < 32 for ch in value), "artifact-text")
    return value


def names(value, pattern, maximum):
    """来源名单必须非空、唯一且排序固定，不能由本轮结果补出。"""
    require(type(value) is list and 1 <= len(value) <= maximum, "artifact-source-budget")
    require(all(type(item) is str and re.fullmatch(pattern, item) for item in value), "artifact-source-name")
    require(value == sorted(set(value)), "artifact-source-order")
    return value


def ordinary(path, directory=False):
    """拒绝链接/reparse和非普通文件，最后规范Windows短名；不跨目录跟随产物。"""
    path = Path(os.path.abspath(path))
    for part in (path, *path.parents):
        info = part.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 0x400, "artifact-link")
    require(path.is_dir() if directory else path.is_file(), "artifact-file-type")
    return path.resolve(strict=True)


def load(path, digest, maximum):
    """先有界读取并校验独立摘要，再解析严格JSON；不接受重复键或非有限数。"""
    require(type(digest) is str and HEX.fullmatch(digest), "artifact-digest")
    path = ordinary(path)
    require(path.stat().st_size <= maximum, "artifact-file-budget")
    with path.open("rb") as stream:
        data = stream.read(maximum+1)
    require(len(data) <= maximum and hashlib.sha256(data).hexdigest() == digest, "artifact-content-digest")
    return read_json(data)


def elapsed(start, end):
    """单调计时允许有符号nanoTime原点；仅检查同一条API跨度，不跨进程比较绝对时钟。"""
    integer(start, -2**63); integer(end, -2**63)
    require(((end-start) & (2**64-1)) < 2**63, "artifact-clock-order")


def parameters(items, side, version):
    """共同值与Java纳秒值分开识别；原生精度证明不能自动变成跨语言同值。"""
    require(type(items) is list and len(items) <= 256, "artifact-parameter-budget")
    identities, native = set(), 0
    for item in items:
        if side == "java":
            shape(item, "position driverType javaType kind valueHash")
            identity = integer(item["position"], 1, 256); kind = item["kind"]
            text(item["driverType"], nullable=True); text(item["javaType"])
            require(type(kind) is str, "artifact-value-kind")
            if kind in NATIVE:
                require(version == "metarill-sql-values-v2" and item["javaType"] == NATIVE[kind], "artifact-native-value-version")
                native += 1
            else:
                require(kind in COMMON, "artifact-value-kind")
        else:
            shape(item, "name dbType providerType clrType valueKind valueHash direction size precision scale")
            identity = text(item["name"], 256); kind = item["valueKind"]; require(type(kind) is str, "artifact-value-kind")
            require(kind in COMMON | {"datetime:Utc", "datetime:Local"}, "artifact-value-kind")
            text(item["dbType"]); text(item["providerType"], nullable=True); text(item["clrType"])
            require(item["direction"] == "input", "artifact-output-parameter-unverified")
            integer(item["size"], -1, 2**31-1); integer(item["precision"], 0, 255); integer(item["scale"], 0, 255)
        require(identity not in identities, "artifact-duplicate-parameter"); identities.add(identity)
        require(type(item["valueHash"]) is str and HEX.fullmatch(item["valueHash"]), "artifact-parameter-proof")
    if side == "java":
        require(identities == set(range(1, len(items)+1)), "artifact-parameter-position")
    return native


def trace(snapshot, side, databases):
    """重算计数与事务引用，不相信产物自报internallyConsistent=true。"""
    fields = "valueProofFormat commands transactions problems started completed closed internallyConsistent startedTransactions completedTransactions"
    if side == "csharp":
        fields += " timestampFrequency"
    shape(snapshot, fields)
    version = text(snapshot["valueProofFormat"])
    require(version in ({"metarill-sql-values-v1", "metarill-sql-values-v2"} if side == "java" else {"metarill-sql-values-v1"}), "artifact-value-format")
    require(flag(snapshot["closed"]) and flag(snapshot["internallyConsistent"]) and snapshot["problems"] == [], "artifact-scope-incomplete")
    commands, transactions = snapshot["commands"], snapshot["transactions"]
    require(type(commands) is list and type(transactions) is list, "artifact-entry-list")
    require(integer(snapshot["started"], maximum=65536) == integer(snapshot["completed"], maximum=65536) == len(commands), "artifact-command-count")
    require(integer(snapshot["startedTransactions"], maximum=65536) == integer(snapshot["completedTransactions"], maximum=65536) == len(transactions), "artifact-transaction-count")
    if side == "csharp":
        integer(snapshot["timestampFrequency"], 1, 10**12)
    began, ended, active, numbers, failures, native = {}, {}, {}, set(), [0, 0], 0
    previous_boundary, outer = {}, {}
    for entry in transactions:
        fields = "number operation connection transaction succeeded errorType"
        fields += " booleanValue isolationLevel errorCode startedNanos finishedNanos" if side == "java" else " isolation canceled startedTimestamp finishedTimestamp"
        shape(entry, fields); number = integer(entry["number"], 1); require(number not in numbers, "artifact-entry-number"); numbers.add(number)
        connection = integer(entry["connection"], 1); transaction = entry["transaction"]
        if transaction is not None:
            integer(transaction, 1)
        success = flag(entry["succeeded"]); text(entry["errorType"], nullable=True)
        require((entry["errorType"] is None) == success, "artifact-outcome")
        failures[1] += not success
        start, finish = (entry["startedNanos"], entry["finishedNanos"]) if side == "java" else (entry["startedTimestamp"], entry["finishedTimestamp"])
        elapsed(start, finish); operation = entry["operation"]; text(operation)
        if connection in previous_boundary:
            elapsed(previous_boundary[connection], start)
        previous_boundary[connection] = finish
        if side == "java":
            require(operation in {"setAutoCommit", "commit", "rollback", "setReadOnly", "setTransactionIsolation"}, "artifact-transaction-operation")
            if entry["errorCode"] is not None:
                integer(entry["errorCode"], -2**31, 2**31-1)
            require(not success or entry["errorCode"] is None, "artifact-outcome")
            if operation in {"setAutoCommit", "setReadOnly"}:
                flag(entry["booleanValue"]); require(entry["isolationLevel"] is None, "artifact-transaction-setting")
            elif operation == "setTransactionIsolation":
                require(type(entry["isolationLevel"]) is int and entry["isolationLevel"] in {0, 1, 2, 4, 8}, "artifact-isolation"); require(entry["booleanValue"] is None, "artifact-transaction-setting")
            else:
                require(entry["booleanValue"] is None and entry["isolationLevel"] is None, "artifact-transaction-setting")
            is_begin = operation == "setAutoCommit" and entry["booleanValue"] is False
            is_end = operation in {"commit", "rollback"} or operation == "setAutoCommit" and entry["booleanValue"] is True
        else:
            require(operation in {"begin", "commit", "rollback"}, "artifact-transaction-operation"); flag(entry["canceled"]); text(entry["isolation"], nullable=True)
            require(not success or not entry["canceled"], "artifact-outcome"); is_begin, is_end = operation == "begin", operation in {"commit", "rollback"}
        if is_begin:
            if success:
                require(transaction is not None, "artifact-transaction-reference")
            elif side == "csharp":
                require(transaction is None, "artifact-failed-begin-reference")
            elif transaction is not None:
                require(active.get(connection) == transaction, "artifact-transaction-reference")
        elif transaction is not None:
            require(active.get(connection) == transaction, "artifact-transaction-reference")
        else:
            require(side == "java" and operation in {"setAutoCommit", "setReadOnly", "setTransactionIsolation"} and connection not in active, "artifact-missing-begin")
        if success and is_begin:
            require(transaction is not None, "artifact-transaction-reference")
            if transaction not in began:
                require(connection not in active, "artifact-overlapping-transaction"); began[transaction] = (connection, finish); outer[transaction] = [start, None]; active[connection] = transaction
            else:
                require(side == "java" and active.get(connection) == transaction, "artifact-duplicate-begin")
        elif success and is_end:
            if transaction is None:
                require(side == "java" and operation == "setAutoCommit" and connection not in active, "artifact-missing-begin")
            else:
                require(active.get(connection) == transaction and transaction not in ended, "artifact-missing-begin"); elapsed(began[transaction][1], start); ended[transaction] = start; outer[transaction][1] = finish; del active[connection]
    require(not active and set(began) == set(ended), "artifact-unresolved-transaction")
    # 每连接事务区间按实际API顺序建立索引，避免长采集文件逐命令扫描全部事务。
    intervals = {}
    for known, (connection, _) in began.items():
        intervals.setdefault(connection, []).append(outer[known])
    for connection, rows in intervals.items():
        origin = rows[0][0]
        relative = lambda value: ((value-origin+2**63) % 2**64)-2**63
        normalized = [(relative(first), relative(last)) for first, last in rows]
        require(all(0 <= first <= last for first, last in normalized), "artifact-transaction-clock-span")
        intervals[connection] = (origin, [first for first, _ in normalized], normalized)
    transaction_numbers = numbers.copy(); command_numbers = set()
    for entry in commands:
        fields = "number operation sql parameters connection transaction serverPrepared succeeded errorType errorCode affectedRows startedNanos finishedNanos" if side == "java" else "number operation sql parameters database connection transaction prepared driverCalled succeeded canceled errorType affectedRows startedTimestamp driverTimestamp finishedTimestamp"
        shape(entry, fields); number = integer(entry["number"], 1); require(number not in command_numbers and (side != "java" or number not in numbers), "artifact-entry-number"); command_numbers.add(number); numbers.add(number)
        text(entry["operation"]); require(entry["operation"] in ({"execute", "executeQuery", "executeUpdate", "executeLargeUpdate"} if side == "java" else {"nonQuery", "scalar", "reader"}), "artifact-command-operation")
        require(type(entry["sql"]) is str and 0 < len(entry["sql"]) <= 32768 and "\0" not in entry["sql"], "artifact-sql-text"); connection = integer(entry["connection"], 1); transaction = entry["transaction"]
        success = flag(entry["succeeded"]); text(entry["errorType"], nullable=True); require((entry["errorType"] is None) == success, "artifact-outcome"); failures[0] += not success
        native += parameters(entry["parameters"], side, version)
        if entry["affectedRows"] is not None:
            integer(entry["affectedRows"], -1, 2**63-1 if side == "java" else 2**31-1)
        row_operation = entry["operation"] in {"executeUpdate", "executeLargeUpdate", "nonQuery"}
        require((entry["affectedRows"] is not None) == (success and row_operation), "artifact-outcome")
        if side == "java":
            flag(entry["serverPrepared"])
            if entry["errorCode"] is not None:
                integer(entry["errorCode"], -2**31, 2**31-1)
            require(not success or entry["errorCode"] is None, "artifact-outcome")
            start, finish = entry["startedNanos"], entry["finishedNanos"]
        else:
            flag(entry["prepared"]); flag(entry["driverCalled"]); flag(entry["canceled"]); require(not success or entry["driverCalled"] and not entry["canceled"], "artifact-outcome")
            text(entry["database"], 64); require(entry["database"] in databases, "artifact-unexpected-database"); start, finish = entry["startedTimestamp"], entry["finishedTimestamp"]
            if entry["driverCalled"]:
                elapsed(start, entry["driverTimestamp"]); elapsed(entry["driverTimestamp"], finish)
            else:
                require(type(entry["driverTimestamp"]) is int and entry["driverTimestamp"] == 0 and not success, "artifact-driver-timestamp")
        elapsed(start, finish)
        if transaction is not None:
            integer(transaction, 1); require(transaction in began and began[transaction][0] == connection, "artifact-command-transaction")
            elapsed(began[transaction][1], start); elapsed(finish, ended[transaction])
        else:
            # 同一连接存在已知事务时，命令不能通过抹去事务引用绕过区间核验。
            if connection in intervals:
                origin, starts, rows = intervals[connection]
                first = ((start-origin+2**63) % 2**64)-2**63
                last = ((finish-origin+2**63) % 2**64)-2**63
                require(first <= last, "artifact-command-clock-span")
                previous = bisect_left(starts, last)-1
                require(previous < 0 or first >= rows[previous][1], "artifact-unreferenced-active-transaction")
    if side == "java":
        require(numbers == set(range(1, len(commands)+len(transactions)+1)), "artifact-entry-sequence")
        ordered = sorted(commands+transactions, key=lambda item: item["number"])
        for previous, current in zip(ordered, ordered[1:]):
            elapsed(previous["finishedNanos"], current["startedNanos"])
    else:
        require(command_numbers == set(range(1, len(commands)+1)) and transaction_numbers == set(range(1, len(transactions)+1)), "artifact-entry-sequence")
    return {"commands": len(commands), "transactions": len(transactions), "failedCommands": failures[0], "failedTransactions": failures[1], "nativeTemporalProofs": native}


def audit(directory, expected, index_digest):
    """预期样本和来源由独立冻结计划提供；本函数不从结果反推放宽预期。"""
    shape(expected, "format side runId source samples"); require(expected["format"] == "metarill-sql-artifact-expectations-v1" and type(expected["side"]) is str and expected["side"] in {"java", "csharp"}, "artifact-expectation-format")
    side, run_id = expected["side"], expected["runId"]; require(type(run_id) is str and SAMPLE.fullmatch(run_id), "artifact-run")
    samples = expected["samples"]; require(type(samples) is list and 1 <= len(samples) <= 4096, "artifact-sample-budget")
    planned = {}
    for sample in samples:
        shape(sample, "sample statusAtExit chainSucceeded commands transactions failedCommands failedTransactions"); label = sample["sample"]; require(type(label) is str and SAMPLE.fullmatch(label) and label not in planned, "artifact-planned-sample")
        integer(sample["statusAtExit"], 100, 599); flag(sample["chainSucceeded"])
        for name in ("commands", "transactions", "failedCommands", "failedTransactions"):
            integer(sample[name], maximum=65536)
        planned[label] = sample
    directory = ordinary(directory, True); index = load(directory/"index.json", index_digest, 4*1024*1024)
    fields = "format runId started completed observed problems requests finalized internallyConsistent"
    fields += " sourcesVerified dataSources" if side == "java" else " dataAssemblySha256 expectedDatabases retainedEntries"
    shape(index, fields); require(index["format"] == ("metarill-paired-jdbc-index-v1" if side == "java" else "metarill-paired-dotnet-index-v1") and index["runId"] == run_id, "artifact-index-format")
    require(flag(index["finalized"]) and flag(index["internallyConsistent"]) and index["problems"] == [], "artifact-index-incomplete")
    require(integer(index["started"], maximum=4096) == integer(index["completed"], maximum=4096) == integer(index["observed"], maximum=4096) == len(samples), "artifact-request-count")
    source = expected["source"]
    if side == "java":
        shape(source, "dataSources"); names(source["dataSources"], r"[A-Za-z0-9_.-]{1,100}", 16); require(flag(index["sourcesVerified"]) and index["dataSources"] == source["dataSources"], "artifact-data-sources"); databases = set()
    else:
        shape(source, "dataAssemblySha256 databases"); require(type(source["dataAssemblySha256"]) is str and HEX.fullmatch(source["dataAssemblySha256"]) and index["dataAssemblySha256"] == source["dataAssemblySha256"], "artifact-data-assembly"); databases = set(names(source["databases"], r"[A-Za-z0-9_]{1,64}", 16)); require(index["expectedDatabases"] == sorted(databases) and len(databases) == len(source["databases"]), "artifact-data-bases")
    refs = index["requests"]; require(type(refs) is list and len(refs) == len(samples), "artifact-reference-count")
    seen, files, total_bytes = set(), {"index.json"}, 0; totals = {name: 0 for name in ("commands", "transactions", "failedCommands", "failedTransactions", "nativeTemporalProofs")}
    for reference in refs:
        shape(reference, "id sha256"); ident = reference["id"]; pattern = r"[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}" if side == "java" else r"[a-f0-9]{32}"
        require(type(ident) is str and re.fullmatch(pattern, ident) and ident+".json" not in files, "artifact-reference-id"); name = ident+".json"; files.add(name); total_bytes += ordinary(directory/name).stat().st_size; require(total_bytes <= 512*1024*1024, "artifact-total-budget")
        record = load(directory/name, reference["sha256"], 64*1024*1024)
        fields = "format runId id sample chainSucceeded errorType trace" + (" statusAtFilterExit" if side == "java" else " statusAtMiddlewareExit problems internallyConsistent")
        shape(record, fields); require(record["format"] == ("metarill-paired-jdbc-http-v1" if side == "java" else "metarill-paired-dotnet-http-v1") and record["runId"] == run_id and record["id"] == ident, "artifact-record-format")
        label = record["sample"]; require(type(label) is str and label in planned and label not in seen, "artifact-unplanned-sample"); seen.add(label); sample = planned[label]
        require(flag(record["chainSucceeded"]) == sample["chainSucceeded"] and integer(record["statusAtFilterExit" if side == "java" else "statusAtMiddlewareExit"], 100, 599) == sample["statusAtExit"], "artifact-sample-outcome"); text(record["errorType"], nullable=True); require((record["errorType"] is None) == record["chainSucceeded"], "artifact-outcome")
        if side == "csharp":
            require(flag(record["internallyConsistent"]) and record["problems"] == [], "artifact-record-incomplete")
        counts = trace(record["trace"], side, databases)
        for key in ("commands", "transactions", "failedCommands", "failedTransactions"):
            require(counts[key] == sample[key], "artifact-planned-entry-count")
        for key in totals:
            totals[key] += counts[key]
    require(seen == set(planned), "artifact-missing-sample")
    if side == "csharp":
        require(integer(index["retainedEntries"]) == totals["commands"]+totals["transactions"], "artifact-retained-entry-count")
    require({p.name for p in directory.iterdir()} == files, "artifact-unowned-or-missing-file")
    return {"format": "metarill-sql-artifact-audit-v1", "side": side, "indexSha256": index_digest, "samples": len(seen), **totals, "evidenceInternallyValid": True, "semanticComparisonAccepted": False, "physicalTargetVerified": False, "performanceAccepted": False, "releaseAccepted": False}


def main():
    """只读取固定预期和采集目录；预期文件摘要与索引摘要必须由可信协调器独立提供。"""
    parser = argparse.ArgumentParser(description="只读核验Java/C#私有SQL采集文件，不执行SQL或性能测量")
    for name in ("directory", "expectations", "expectations-sha256", "index-sha256", "output"):
        parser.add_argument("--"+name, required=True)
    args = parser.parse_args()
    try:
        expected = load(args.expectations, args.expectations_sha256, 4*1024*1024); result = audit(args.directory, expected, args.index_sha256)
        output = Path(args.output); parent = ordinary(output.parent, True); root = ordinary(args.directory, True)
        require(not output.exists() and parent != root and root not in parent.parents, "artifact-output-path")
        with output.open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(result, ensure_ascii=False, indent=2))
        print("私有采集文件内部核验通过；未证明SQL语义、物理目标或性能等价。")
    except (ContractError, OSError, ValueError, TypeError, KeyError, RecursionError):
        raise SystemExit("私有采集核验失败；未输出SQL、参数或私有路径。")

if __name__ == "__main__":
    main()
