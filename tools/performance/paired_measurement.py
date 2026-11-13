"""当前双端独占种子的原接口只读性能回放。"""
from __future__ import annotations

import os
import time
from urllib.error import HTTPError

import paired_http as http
from server200_paired_resources import READ_REQUEST_BUDGET
from paired_measurement_fixture import ID_BASE


def read_scenario():
    """读取客户分页与计数、带缓存的合同和父范围明细，保留完整原响应比较。"""
    customer = "/admin/auto/sample_customer/81001"
    contract = "/admin/auto/sample_contract/81002"
    line = "/admin/auto/sample_contract_line/81003"
    ordering = {"column":"name","order":"ascending"}
    page = {"pageNo":1,"pageSize":20,"filters":{"name":"性能客户-"},"orderBy":ordering}
    # 原明细名称未开放自定义排序，沿页面固定主键降序读取父记录。
    child = {"pageNo":1,"pageSize":20,"filters":{},"parentId":str(ID_BASE+10001)}
    def step(method,path,checks,body=None):
        """按原 R 协议定义固定路径和独立预期值。"""
        result={"method":method,"path":path,"status":200,
                "checks":[{"pointer":key,"equals":value} for key,value in {"/code":200,**checks}.items()]}
        if body is not None:
            result["json"]=body
        return result
    return {"name":"customer-count-contract-child","steps":[
        step("POST",customer+"/page",{"/data/total":480,"/data/records/0/id":str(ID_BASE)},page),
        step("POST",customer+"/count",{"/data":480},page),
        step("GET",contract+f"/detail/{ID_BASE+10001}",{"/data/id":str(ID_BASE+10001),"/data/sampleCustomerId":str(ID_BASE),"/data/sampleCustomerName":"性能客户-0000","/data/amount":1.37}),
        step("POST",line+"/page",{"/data/total":3,"/data/records/0/id":str(ID_BASE+20006)},child),
    ]}


def config_for(run,targets,tokens):
    """固定次数、并发、原读接口及每端真实读者会话，配置仅在内存存在。"""
    config=http.validate({
        "version":1,"runId":run,"transportProof":"server200-loopback-v1",
        "rounds":3,"iterations":400,"warmupIterations":8,"concurrency":[1,4],
        "requestTimeoutSeconds":5,"maxP95Ratio":1.10,"minThroughputRatio":0.95,
        "targets":{side:{**targets[side],"headers":{"loginStateId":tokens[side,"reader"]}} for side in ("java","csharp")},
        "scenarios":[read_scenario()],
    })
    # 从真实场景、轮次和预热推导预算，防止新增步骤却仍声称原19336请求。
    steps=sum(len(scenario["steps"]) for scenario in config["scenarios"])
    requests=len(config["targets"])*steps*(len(config["concurrency"])*(config["rounds"]*config["iterations"]+config["warmupIterations"])+1)
    if requests!=READ_REQUEST_BUDGET:raise ValueError("measurement-read-budget")
    return config


DATABASE_SETTINGS = {
    "version": "@@version",
    "bufferPoolBytes": "@@global.innodb_buffer_pool_size",
    "maxConnections": "@@global.max_connections",
    "transactionIsolation": "@@global.transaction_isolation",
    "globalTimeZone": "@@global.time_zone",
    "globalSqlMode": "@@global.sql_mode",
    # 固定写入持久化和组提交条件，避免把刷盘策略差异算成应用性能差异。
    "logBin": "@@global.log_bin",
    "binlogFormat": "@@global.binlog_format",
    "binlogRowImage": "@@global.binlog_row_image",
    "syncBinlog": "@@global.sync_binlog",
    "innodbFlushLogAtTrxCommit": "@@global.innodb_flush_log_at_trx_commit",
    "binlogGroupCommitSyncDelay": "@@global.binlog_group_commit_sync_delay",
    "binlogGroupCommitSyncNoDelayCount": "@@global.binlog_group_commit_sync_no_delay_count",
}
VARIABLE_SQL = "SELECT "+",".join(DATABASE_SETTINGS.values())+";"
STATUS_NAMES = ("Threads_running","Threads_connected","Questions","Innodb_buffer_pool_reads","Innodb_buffer_pool_read_requests")
STATUS_SQL = "SHOW GLOBAL STATUS WHERE Variable_name IN ("+",".join("'"+name+"'" for name in STATUS_NAMES)+");"


def database_context(query):
    """只保留实例设置和负载计数，不采集其他会话、SQL 或数据。"""
    settings = query(VARIABLE_SQL).split("\t")
    counters = dict(line.split("\t") for line in query(STATUS_SQL).splitlines())
    if len(settings) != len(DATABASE_SETTINGS) or set(counters) != set(STATUS_NAMES):
        raise ValueError("measurement-database-context")
    variables = dict(zip(DATABASE_SETTINGS, settings))
    numeric = [variables[name] for name in ("bufferPoolBytes", "maxConnections", "syncBinlog",
        "binlogGroupCommitSyncDelay", "binlogGroupCommitSyncNoDelayCount")]
    if (not all(value.isascii() and value.isdigit() for value in [*numeric, *counters.values()])
            or variables["logBin"] not in ("0", "1")
            or variables["innodbFlushLogAtTrxCommit"] not in ("0", "1", "2")
            or variables["binlogFormat"] not in ("ROW", "STATEMENT", "MIXED")
            or variables["binlogRowImage"] not in ("FULL", "MINIMAL", "NOBLOB")):
        raise ValueError("measurement-database-context")
    return {"variables": variables, "globalCounters": {key: int(value) for key, value in counters.items()},
            "loadAverage": list(os.getloadavg())}


def run_read_measurement(root,run,targets,tokens,probe,query):
    """先验证两端原响应一致，再调用既有测量器；私有事件不含登录令牌。"""
    config=config_for(run,targets,tokens)
    output=root/"measurement"
    output.mkdir(mode=0o700)
    steps=config["scenarios"][0]["steps"]
    fingerprints = {side: [] for side in ("java", "csharp")}
    config_hash = http.digest(config)
    for side, responses in fingerprints.items():
        for ordinal, step in enumerate(steps, start=1):
            try:
                responses.append(http.request(config["targets"][side], step, {}, config["requestTimeoutSeconds"]))
            except (ValueError, OSError) as error:
                # 只记录固定目标、步骤、已完成响应摘要及枚举化断言，不保存正文或令牌。
                failure = {"runId": run, "configSha256": config_hash, "target": side, "step": ordinal,
                           "errorType": type(error).__name__, "responseFingerprints": fingerprints}
                if isinstance(error, http.ResponseAssertionError):
                    failure.update(assertion=error.ordinal, reason=error.reason)
                if isinstance(error, HTTPError):
                    failure["status"] = error.code
                (output / "preflight-failure.json").write_bytes(http.canonical(failure))
                raise ValueError("measurement-preflight-request-failed") from None
    (output/"preflight.json").write_bytes(http.canonical({"runId":run,"configSha256":config_hash,"responseFingerprints":fingerprints}))
    if fingerprints["java"]!=fingerprints["csharp"]:
        raise ValueError("measurement-preflight-response-mismatch")
    return run_trials(output,config,probe,query,"original-read-only")


def run_trials(output,config,probe,query,profile,*,transport=None,check_event=None,minimum_iterations=100,resource_probe=None):
    """两种测量共用资源、共享库状态和私有日志，保留各自请求边界。"""
    initial=database_context(query)
    settings_unchanged=True
    def journal(event):
        """在计时范围外记公共实例计数，异常仍保留已完成轮次。"""
        nonlocal settings_unchanged
        context=database_context(query) if event["kind"] in ("trial-intent","trial") else None
        if context is not None and context["variables"]!=initial["variables"]:
            settings_unchanged=False
        with (output/"events.jsonl").open("ab") as stream:
            stream.write(http.canonical({"observedAt":time.time(),**event,"databaseContext":context})+b"\n")
            stream.flush()
            os.fsync(stream.fileno())
        if check_event is not None:
            check_event(event)
    owned_probe = None
    if resource_probe is None and isinstance(probe, http.Server200LocalProbe):
        import paired_cgroup_probe as cgroups
        limits = http.equal_limits(probe, config["targets"])
        owned_probe = resource_probe = cgroups.CgroupProbe(probe, config["targets"], limits)
    options={} if transport is None else {"transport":transport}
    if minimum_iterations != 100: options["minimum_iterations"] = minimum_iterations
    if resource_probe is not None: options["resource_probe"] = resource_probe
    report=http.run(config,probe,on_event=journal,**options)
    if owned_probe is not None:
        owned_probe.verify()
        report["resourceSource"] = cgroups.METHOD
    final=database_context(query)
    report.update(databaseSettingsVersion=2, databaseBefore=initial, databaseAfter=final,
                  databaseVariablesUnchanged=settings_unchanged and initial["variables"]==final["variables"])
    (output/"measurement.json").write_bytes(http.canonical(report))
    results=report["results"]
    return {"measurementValid":report["databaseVariablesUnchanged"] and len(results)==len(config["concurrency"]) and all(item["measurementValid"] for item in results),
            "stableRegression":any(item["stableRegression"] for item in results),"profile":profile,"report":"measurement/measurement.json"}
