"""固定普通业务的请求采样及关闭产物保存；只证明采集结构，绝不自动批准SQL次数或语义等价。"""
from __future__ import annotations
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import shutil
import socket
import time
import audit_sql_artifacts as reader
import paired_business_http as business
import server200_paired_resources as resources

IDENTITY_STEPS=('ready','wrong-password','login-writer','page-writer','login-reader','page-reader','reader-create-denied','login-denied','page-denied')


def authorize(value):
    """纯内存核新用途和双库范围，pending／ended在读取目录或执行网络前拒绝。"""
    run=value.get('runId');reader.require(type(run) is str and re.fullmatch('[a-f0-9]{32}',run),'capture-run')
    reader.require(type(value.get('version')) is int and value['version']==1 and value.get('purpose') in ('paired-native-observed-v1',resources.EXISTING_LINE_NATIVE,resources.CURRENT_OBSERVED_NATIVE,resources.FULL_OBSERVED_NATIVE,resources.PATTERN_OBSERVED_NATIVE,resources.UNICODE_OBSERVED_NATIVE,resources.EXCEL_CHILD_SQL_NATIVE,resources.EXCEL_CHILD_LAYOUT_SQL_NATIVE) and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='not-started','capture-lease')
    reader.require(resources.images_for(value) is not None and value.get('images')==resources.images_for(value) and value.get('instancePin')==business.PIN and value.get('databases')==['metarill_fresh_'+run[:16]+'_paired_'+target for target in ('java','csharp')] and value.get('user')=='mtf_'+run[:16],'capture-scope')
    ports=value.get('ports');reader.require(type(ports) is dict and set(ports)=={'java','csharp'} and all(type(port) is int and 20000<=port<=40000 for port in ports.values()) and len(set(ports.values()))==2,'capture-ports')
    return run


def sample(run,target,step):
    """同一逻辑请求只分配一个固定32位采样值，不从服务端记录或响应反推。"""
    return hashlib.sha256((run+'\0'+target+'\0'+step).encode()).hexdigest()[:32]


def identity_outcome(target, step):
    """原就绪、密码和权限请求的已知 HTTP 与 R 结果。"""
    if step=="wrong-password":
        return (200,500) if target=="java" else (401,401)
    if step in {"reader-create-denied","page-denied"}:
        return (200,500) if target=="java" else (403,403)
    return (200,200)


def expected_steps(value,target,*,existing_lines=False):
    """按原两端HTTP/R规则冻结请求，已有明细场景由独立协调器显式选择。"""
    if resources.is_excel_child_sql(value):
        steps = ("positive", "parent-page", "missing-parent", "deleted-parent", "late-row-rollback")
        return [
            dict(step="child:"+step, sample=sample(value["runId"], target, "child:"+step),
                 statusAtExit=200 if target=="java" or index<2 else 500,
                 expectedCode=200 if index<2 else 500, chainSucceeded=True)
            for index, step in enumerate(steps)
        ]
    rows=[]
    for step in IDENTITY_STEPS:
        rows.append(('identity:'+step,identity_outcome(target,step)))
    for name in (business.EXISTING_LINE_STEPS if existing_lines else business.STEPS):
        outcome=(200,200)
        if name in {'reader-update-denied','denied-detail'}:outcome=(200,500) if target=='java' else (403,403)
        elif name=='business-rollback':outcome=(200,500)
        elif name=='business-replay-conflict':outcome=(200,500) if target=='java' else (409,409)
        rows.append(('business:'+name,outcome))
        if resources.is_unicode_observed(value) and name=='customer-count':
            rows.extend(('unicode:count-'+code,(200,200)) for code,_ in business.UNICODE_BLANK_CASES)
    return [{'step':step,'sample':sample(value['runId'],target,step),'statusAtExit':outcome[0],'expectedCode':outcome[1],'chainSucceeded':True} for step,outcome in rows]


class Exchange:
    """只把固定step关联到原sender；令牌、正文和响应数据均不写请求账本。"""
    def __init__(self,value,sender,*,unicode_counts=None):
        authorize(value);self.value=json.loads(json.dumps(value));self.sender=sender;self.sent={'java':[],'csharp':[]};self.finished={'java':[],'csharp':[]}
        self.existing_lines=resources.is_existing_lines(value)
        reader.require(callable(unicode_counts) if resources.is_unicode_observed(value) else unicode_counts is None,'capture-unicode-baseline')
        self.unicode_counts=unicode_counts;self.unicode_facts={'java':[],'csharp':[]}
        self.plans={target:expected_steps(value,target,existing_lines=self.existing_lines) for target in ('java','csharp')}

    def call(self,target,step,method,path,body=None,token=None,form=False):
        """发出前登记且只发一次；原错误响应必须符合预定状态，未知/重复/跳步不发网络请求。"""
        reader.require(target in self.plans,'capture-target');offset=len(self.sent[target]);plan=self.plans[target]
        reader.require(offset<len(plan) and plan[offset]['step']==step,'capture-request-order');entry=plan[offset];self.sent[target].append(entry)
        status,result=self.sender(self.value['ports'][target],method,path,body,token,form=form,sample=entry['sample'])
        reader.require(type(status) is int and status==entry['statusAtExit'] and type(result) is dict and type(result.get('code')) is int and result['code']==entry['expectedCode'],'capture-client-outcome');self.finished[target].append(entry)
        return status,result

    @staticmethod
    def wait_ready(port):
        """原生协调器透过已核对的采集模块等待，不额外提前导入未登记源码。"""
        wait_ready(port)

    def business_request(self,target,step,method,path,body,token):
        """原业务只消费已取得的真实令牌；不重新构造身份或向业务正文添加采集字段。"""
        status,result=self.call(target,'business:'+step,method,path,body,token)
        if resources.is_unicode_observed(self.value) and step=='customer-count':
            # 只在原客户及副本存在时插入八个只读对照；原业务仍收到未改写的原计数结果。
            prefix='配对客户-'+self.value['runId'][:16]
            reader.require(method=='POST' and path==business.ROOTS['customer']+'/count' and type(body) is dict and body.get('filters')=={'name':prefix} and type(result.get('data')) is int and result['data']==2,'capture-unicode-anchor')
            expected=self.unicode_counts(target)
            reader.require(type(expected) is dict and set(expected)=={code for code,_ in business.UNICODE_BLANK_CASES} and all(type(n) is int and n>=0 for n in expected.values()),'capture-unicode-baselines')
            for code,blank in business.UNICODE_BLANK_CASES:
                if blank:reader.require(expected[code]==2,'capture-unicode-blank-fixture')
                request=json.loads(json.dumps(body));request['filters']={'name':chr(int(code,16))}
                _,response=self.call(target,'unicode:count-'+code,method,path,request,token)
                actual=response.get('data');reader.require(type(actual) is int and actual==expected[code],'capture-unicode-count')
                self.unicode_facts[target].append({'codePoint':code,'javaBlank':blank,'expectedCount':expected[code],'actualCount':actual})
        return status,result

    def complete(self):
        """两端原身份和全部业务步骤都必须完成，不能只看一个successful trace。"""
        reader.require(all(len(self.finished[target])==len(self.plans[target]) for target in self.plans),'capture-client-coverage')
        if resources.is_unicode_observed(self.value):
            for target in self.plans:
                facts=self.unicode_facts[target]
                reader.require(type(facts) is list and all(type(row) is dict and set(row)=={'codePoint','javaBlank','expectedCount','actualCount'} and type(row['codePoint']) is str and type(row['javaBlank']) is bool for row in facts),'capture-unicode-shape')
                reader.require([(row.get('codePoint'),row.get('javaBlank')) for row in facts]==list(business.UNICODE_BLANK_CASES),'capture-unicode-coverage')
                reader.require(all(type(row.get('actualCount')) is int and type(row.get('expectedCount')) is int and row['expectedCount']>=0 and row['actualCount']==row['expectedCount'] and (not row['javaBlank'] or row['actualCount']==2) for row in facts),'capture-unicode-facts')

    def receipt(self):
        """可保留预期采样计划，不包含请求密码、令牌、正文或服务端返回的数据。"""
        receipt={'format':'metarill-paired-request-capture-plan-v1','runId':self.value['runId'],'targets':self.plans,'sent':{target:len(rows) for target,rows in self.sent.items()},'completed':{target:len(rows) for target,rows in self.finished.items()},'sqlCountExpectationsVerified':False}
        if resources.is_unicode_observed(self.value):receipt['unicodeCounts']=json.loads(json.dumps(self.unicode_facts))
        return receipt


def wait_ready(port,timeout=90):
    """只做宿主回环TCP等待，避免health轮询混入正式采样；从不重试业务HTTP。"""
    deadline=time.monotonic()+timeout
    while True:
        try:
            with socket.create_connection(('127.0.0.1',port),timeout=min(0.5,max(0.01,deadline-time.monotonic()))):return
        except OSError:
            if time.monotonic()>=deadline:raise ValueError('capture-listener-timeout')
            time.sleep(0.1)


def sha(path):
    """流式读取已关闭产物，避免把完整SQL文件同时驻留内存。"""
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()


def verify_cache_transaction(trace,database,side,outcome):
    """核对同一事务的写入与结局，单独返回驱动是否报告两条影响行数。"""
    start,finish=('startedNanos','finishedNanos') if side=='java' else ('startedTimestamp','finishedTimestamp')
    prefix=r'(?is)^\s*UPDATE\s+(?:`?'+re.escape(database)+r'`?\s*\.\s*)?'
    parent=re.compile(prefix+r'`?sample_contract`?(?=\s)')
    child=re.compile(prefix+r'`?sample_contract_line`?\s+SET\s+`?sample_contract_name`?\s*=')
    main=[row for row in trace['commands'] if parent.match(row['sql'])]
    cache=[row for row in trace['commands'] if child.match(row['sql'])]
    reader.require(len(main)==len(cache)==1,'capture-cache-update-count')
    main,cache=main[0],cache[0]
    reference=(main['connection'],main['transaction'])
    reader.require(main['succeeded'] and cache['succeeded'] and reference[1] is not None
        and (cache['connection'],cache['transaction'])==reference and main[finish]<=cache[start],
        'capture-cache-update-boundary')
    # Java MyBatis 的 execute 只返回是否有结果集，观察桥不填影响行数。
    for row in (main,cache):
        if side=='java' and row['operation']=='execute':
            reader.require(row['affectedRows'] is None,'capture-cache-affected-rows')
        else:
            operations={'executeUpdate','executeLargeUpdate'} if side=='java' else {'nonQuery'}
            reader.require(row['operation'] in operations and type(row['affectedRows']) is int and row['affectedRows']==1,'capture-cache-affected-rows')
    endings=[row for row in trace['transactions'] if row['operation']==outcome and row['succeeded']
        and (row['connection'],row['transaction'])==reference and cache[finish]<=row[start]]
    reader.require(len(endings)==1,'capture-cache-transaction-outcome')
    return main['affectedRows']==cache['affectedRows']==1


def verify_child_transaction(trace, database, side, step, run, key):
    """将原工作簿值与写入命令关联，并核对同连接、同事务的提交或回滚。"""
    import paired_child_import_reference as fixture

    start, finish = ("startedNanos", "finishedNanos") if side=="java" else ("startedTimestamp", "finishedTimestamp")
    schema = r"(?:`?" + re.escape(database) + r"`?\s*\.\s*)?"
    child_insert = re.compile(r"(?is)^\s*INSERT\s+INTO\s+" + schema + r"`?sample_contract_line`?(?=\s|\()")
    cache_update = re.compile(r"(?is)^\s*UPDATE\s+" + schema + r"`?sample_contract_line`?\s+SET\s+`?sample_contract_name`?\s*=")
    cache_readback = re.compile(r"(?is);\s*SELECT c\.`id`,c\.`sample_contract_name` FROM " + schema +
                                r"`sample_contract_line` c WHERE 1=1 AND c\.`delete_no` IS NULL AND c\.`id`=@id FOR UPDATE\s*$")
    parent_select = re.compile(r"(?is)^\s*SELECT\b.*?\bFROM\s+" + schema + r"`?sample_contract`?(?=\s|$)")
    source = f"`{database}`.`sample_contract_line`"
    target = f"`{database}`.`sample_contract`"
    guarded_key = "b.`id`=CASE WHEN CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY) THEN @exact_id END"

    def combined_cache(row):
        """仅接受同一限定库、同一主键和显式锁读、写回、物理读回三段。"""
        parts = row["sql"].split("; ")
        return (len(parts)==4 and parts[0]=="SELECT DATABASE(),@@SESSION.max_allowed_packet"
                and f" FROM {source} b LEFT JOIN {target} r0 ON " in parts[1]
                and parts[1].endswith("FOR UPDATE OF b FOR SHARE OF r0")
                and parts[2].startswith(f"UPDATE {source} b SET `sample_contract_name`=(SELECT r0.`name` FROM {target} r0 WHERE ")
                and "FOR SHARE OF r0)" in parts[2] and "<=>" in parts[2]
                and parts[3].startswith(f"SELECT b.`id`,b.`sample_contract_name` FROM {source} b WHERE ")
                and parts[3].endswith("FOR UPDATE OF b") and all(guarded_key in part for part in parts[1:]))

    commands = trace["commands"]
    inserts = [row for row in commands if child_insert.match(row["sql"])]
    caches = [row for row in commands if cache_update.match(row["sql"]) or combined_cache(row)]
    if step=="parent-page":
        reader.require(not inserts and not caches and all(row["succeeded"] for row in commands), "capture-child-page-write")
        return {"commands":len(commands), "childInserts":0}

    def carries(row, expected):
        kinds = set()
        for param in row["parameters"]:
            kind = param["kind" if side=="java" else "valueKind"]
            if kind in {"integer", "string"}:
                proof = hmac.digest(key, (kind+"\0"+expected).encode(), "sha256").hex()
                if hmac.compare_digest(proof, param["valueHash"]):
                    kinds.add(kind)
        return kinds

    parent = str({"missing-parent":fixture.MISSING_PARENT_ID, "deleted-parent":fixture.DELETED_PARENT_ID}.get(step, fixture.PARENT_ID))
    # FreeSql 的固定候选 IN 使用数字字面量；只接受 id 条件中完全匹配的单个整数。
    exact_parent_literal = re.compile(r"(?i)(?:`?[a-z][a-z0-9_]*`?\s*\.\s*)?`id`\s*\)*\s+IN\s*\(\s*" + parent + r"\s*\)")
    def parent_binding(row):
        kinds = carries(row,parent)
        if side=="csharp" and exact_parent_literal.search(row["sql"]):
            kinds.add("literal-integer")
        return kinds

    selects = [row for row in commands if parent_select.match(row["sql"]) and not combined_cache(row)]
    locks = [row for row in selects if re.search(r"\bFOR\s+UPDATE\b", row["sql"], re.I)]
    reader.require(locks and all(row["succeeded"] and parent_binding(row) for row in locks), "capture-child-parent-lock")
    owner = (locks[0]["connection"], locks[0]["transaction"])
    writes = inserts+caches
    reader.require(owner[1] is not None and all((row["connection"],row["transaction"])==owner for row in locks+writes),
                   "capture-child-transaction-owner")
    successful = [row for row in inserts if row["succeeded"]]
    if step=="positive":
        names = {row[0] for row in fixture.workbook.workbook_requests(run)["line"]["rows"]}
        reader.require(len(inserts)==len(successful)==len(names), "capture-child-commit-writes")
    elif step=="late-row-rollback":
        names = {fixture.fixture.import_sample_prefix(run)+"rollback-first"}
        reader.require(1<=len(inserts)<=2 and successful==inserts[:1], "capture-child-rollback-writes")
    else:
        names = set()
        reader.require(not inserts, "capture-child-denied-write")
    reader.require(len(caches)==(len(successful) if side=="csharp" else 0), "capture-child-cache-write-count")

    def id_proof(row, name):
        identifiers = [param["valueHash"] for param in row["parameters"]
                       if param["name"].lstrip("@").lower()==name and param["valueKind"]=="integer"]
        reader.require(len(identifiers)==1, "capture-child-cache-id")
        return identifiers[0]

    caches_by_id = {id_proof(row,"exact_id" if combined_cache(row) else "id"):row for row in caches}
    reader.require(len(caches_by_id)==len(caches), "capture-child-cache-id")
    for row in successful:
        matched = {name for name in names if carries(row,name)}
        reader.require(len(matched)==1 and carries(row,parent) and locks[0][finish]<=row[start], "capture-child-insert-bindings")
        names -= matched
        # Java 在 INSERT 前生成缓存；C# 在同一事务内按新 ID 补写。
        if side=="csharp":
            cache = caches_by_id.pop(id_proof(row,"id_0"),None)
            # 多结果命令在业务中核验受影响行数，采集器只在原 reader 边界证明同一命令含精确物理回读。
            if cache is not None and combined_cache(cache):
                isolated = [item for item in commands
                            if item["sql"]=="SET TRANSACTION ISOLATION LEVEL REPEATABLE READ"
                            and item["succeeded"] and item["connection"]==owner[0] and item["transaction"] is None
                            and item[finish]<=locks[0][start]]
                verified_write = (len(isolated)==1 and cache["operation"]=="reader" and cache["affectedRows"] is None
                                  and carries(cache,database)=={"string"})
            else:
                verified_write = cache is not None and carries(cache,"性能合同-0000") and (
                    cache["operation"]=="nonQuery" and cache["affectedRows"]==1
                    or cache["operation"]=="reader" and cache["affectedRows"] is None and cache_readback.search(cache["sql"]))
            reader.require(verified_write and cache["succeeded"] and
                           row[finish]<=cache[start], "capture-child-cache-binding")
        else:
            reader.require(carries(row,"性能合同-0000"), "capture-child-cache-binding")
        if side!="java" or row["operation"]!="execute":
            reader.require(row["affectedRows"]==1, "capture-child-affected-rows")
    reader.require(not names and not caches_by_id, "capture-child-workbook-coverage")
    outcome = "commit" if step=="positive" else "rollback"
    endings = [row for row in trace["transactions"] if row["operation"]==outcome and row["succeeded"] and
               (row["connection"],row["transaction"])==owner and all(prior[finish]<=row[start] for prior in locks+writes)]
    reader.require(len(endings)==1, "capture-child-transaction-outcome")
    statements = [row["sql"].strip().lower() for row in commands]
    # 仅展开结构探测命令的结果段。身份、写入及其物理读回仍按原驱动命令计数。
    layout_statements = [part for sql in statements for part in
                         (sql.split("; ",2) if " limit 0; select engine from information_schema.tables " in sql else (sql,))]
    layouts = {
        "identityReads": sum(sql.startswith("select database(),@@session.max_allowed_packet") for sql in statements),
        "metadataPins": sum(sql.startswith("select ") and sql.endswith(" limit 0") for sql in layout_statements),
        "engineReads": sum(sql.startswith("select engine from information_schema.tables") for sql in layout_statements),
        "columnReads": sum(sql.startswith("select column_name,column_type,is_nullable,column_key,extra,generation_expression from information_schema.columns")
                           for sql in layout_statements),
    }
    return {"commands":len(commands), "parentReads":len(selects), "parentLockReads":len(locks),
            "parentLockBindings":sorted(set().union(*(parent_binding(row) for row in locks))),
            "childInserts":len(inserts), "successfulInserts":len(successful),
            "failedInserts":len(inserts)-len(successful), "affectedRowsReported":sum(row["affectedRows"] is not None for row in successful),
            "cacheUpdates":len(caches), "layoutChecks":layouts, "transaction":outcome}


def capture_for_review(root,value,exchange,graceful):
    """先保留有界原始文件和审阅key，再重算结构；不把实测次数反填严格验收预期。"""
    authorize(value);reader.require(Path(root).absolute()==resources.root_for(value['runId']).absolute(),'capture-root');reader.require(type(graceful) is bool,'capture-exit-flag')
    root=reader.ordinary(root,True)
    resource=json.loads((root/'resource-state.json').read_text(encoding='utf-8'));native=json.loads((root/'native-state.json').read_text(encoding='utf-8'))
    reader.require(resource.get('runId')==native.get('runId')==value['runId'] and resource.get('ownerToken')==native.get('ownerToken') and type(resource.get('ownerToken')) is str and re.fullmatch('[a-f0-9]{64}',resource['ownerToken']),'capture-resource-owner')
    reader.require(resource.get('status')==('stopped' if graceful else 'ended') and resource.get('hostsStarted') is True,'capture-resource-state')
    output=root/'observations';output.mkdir(mode=0o700)
    materials=reader.load(root/'materials.json',value['materialsSha256'],65536)
    key=reader.ordinary(root/'observation.key');reader.require(key.stat().st_size==32 and sha(key)==materials.get('observation.key'),'capture-key-size');shutil.copyfile(key,output/'value.key');(output/'value.key').chmod(0o600)
    (output/'request-plan.json').write_text(json.dumps(exchange.receipt(),ensure_ascii=False,indent=2),encoding='utf-8');saved={};total=0;missing_instance=False
    for target in ('java','csharp'):
        source=reader.ordinary(root/('data-'+target)/'sql-observation',True)
        reader.require((source/'.metarill-paired-observation-owner').read_text(encoding='utf-8')==value['runId'],'capture-owner')
        folders=[item for item in source.iterdir() if item.is_dir()]
        if not folders:missing_instance=True;continue
        reader.require(len(folders)==1,'capture-instance-count');folder=reader.ordinary(folders[0],True)
        pattern=r'[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}' if target=='java' else '[a-f0-9]{32}'
        reader.require(re.fullmatch(pattern,folder.name),'capture-instance-id');destination=output/target;destination.mkdir(mode=0o700);files={};paths=list(folder.iterdir());reader.require(len(paths)<=130,'capture-file-count')
        for path in paths:
            path=reader.ordinary(path);size=path.stat().st_size;total+=size;reader.require(size<=64*1024*1024 and total<=256*1024*1024,'capture-byte-budget')
            digest=sha(path);target_path=destination/path.name;shutil.copyfile(path,target_path);target_path.chmod(0o600);reader.require(digest==sha(target_path)==sha(path),'capture-copy-digest');files[path.name]=digest
        saved[target]={'folder':destination,'files':files}
    reader.require(not missing_instance,'capture-instance-count');reader.require(graceful,'capture-unfinalized-exit')
    result=review_saved_capture(value,exchange,saved)
    result['privateKeyRetainedForReview']=True
    (output/'capture-manifest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    return result


def review_saved_capture(value,exchange,saved):
    """按固定请求计划复核已保存字节，也可独立重审原始归档。"""
    authorize(value);exchange.complete()
    reader.require(set(saved)=={'java','csharp'},'capture-instance-count');summaries={}
    child_sql = resources.is_excel_child_sql(value)
    for target,material in saved.items():
        directory=material['folder'];files=material['files'];index=reader.load(directory/'index.json',files.get('index.json'),4*1024*1024);java=target=='java'
        if child_sql:
            key_path = reader.ordinary(directory.parent/'value.key')
            reader.require(key_path.stat().st_size==32, 'capture-key-size')
            proof_key = key_path.read_bytes()
            child_facts = {}
        reader.shape(index,'format runId started completed observed problems requests finalized internallyConsistent '+('sourcesVerified dataSources' if java else 'dataAssemblySha256 expectedDatabases retainedEntries'))
        reader.require(index['format']==('metarill-paired-jdbc-index-v1' if java else 'metarill-paired-dotnet-index-v1') and index['runId']==value['runId'],'capture-index-format')
        reader.require(reader.flag(index['finalized']) and reader.flag(index['internallyConsistent']) and index['problems']==[],'capture-index-incomplete')
        expected={row['sample']:row for row in exchange.plans[target]};wanted=len(expected)
        unlabeled = list(IDENTITY_STEPS) if child_sql else []
        recorded = wanted+len(unlabeled)
        reader.require(reader.integer(index['started'])==reader.integer(index['completed'])==reader.integer(index['observed'])==recorded,'capture-request-count')
        database='metarill_fresh_'+value['runId'][:16]+'_paired_'+target
        if java:reader.require(reader.flag(index['sourcesVerified']) and index['dataSources']==['dataSource'],'capture-sources')
        else:reader.require(index['dataAssemblySha256']==resources.observation_data_sha(value) and index['expectedDatabases']==[database],'capture-data-assembly')
        cache_transactions=set(); cache_counts_recorded=True
        refs=index['requests'];reader.require(type(refs) is list and len(refs)==recorded,'capture-reference-count');seen=set();names={'index.json'};counts={key:0 for key in ('commands','transactions','failedCommands','failedTransactions','nativeTemporalProofs')};rollback=False
        for reference in refs:
            reader.shape(reference,'id sha256');ident=reference['id'];reader.require(type(ident) is str and re.fullmatch(r'[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}' if java else '[a-f0-9]{32}',ident),'capture-record-id');name=ident+'.json';reader.require(name not in names,'capture-duplicate-record');names.add(name)
            reader.require(files.get(name)==reference['sha256'],'capture-reference-digest')
            record=reader.load(directory/name,reference['sha256'],64*1024*1024);reader.shape(record,'format runId id sample chainSucceeded errorType trace '+('statusAtFilterExit' if java else 'statusAtMiddlewareExit problems internallyConsistent'))
            reader.require(record['format']==('metarill-paired-jdbc-http-v1' if java else 'metarill-paired-dotnet-http-v1') and record['runId']==value['runId'] and record['id']==ident,'capture-record-format')
            label=record['sample']
            if label is None and child_sql:
                # 原观察器会自动记录已由真实入口核验的九个就绪／认证请求。
                reader.require(unlabeled and not seen,'capture-unplanned-identity')
                identity_step = unlabeled.pop(0)
                entry = {'step':'identity:'+identity_step,'statusAtExit':identity_outcome(target,identity_step)[0]}
            else:
                reader.require(not unlabeled and type(label) is str and label in expected and label not in seen,'capture-unplanned-sample')
                seen.add(label)
                entry=expected[label]
            reader.require(reader.integer(record['statusAtFilterExit' if java else 'statusAtMiddlewareExit'],100,599)==entry['statusAtExit'] and reader.flag(record['chainSucceeded']) and record['errorType'] is None,'capture-record-outcome')
            if not java:reader.require(reader.flag(record['internallyConsistent']) and record['problems']==[],'capture-record-incomplete')
            measured=reader.trace(record['trace'],target,{database})
            step = entry['step']
            reader.require(measured['failedTransactions']==0 and
                           (measured['failedCommands']==0 or child_sql and step=="child:late-row-rollback" and measured['failedCommands']==1),
                           'capture-driver-failure')
            for key in counts:counts[key]+=measured[key]
            if child_sql and label is None:
                continue
            if child_sql:
                child_step = step.removeprefix("child:")
                child_facts[child_step] = verify_child_transaction(record['trace'],database,target,child_step,value['runId'],proof_key)
                continue
            if exchange.existing_lines and entry['step'] in {'business:business-rollback','business:business-complete'}:
                outcome='rollback' if entry['step']=='business:business-rollback' else 'commit'
                counts_recorded=verify_cache_transaction(record['trace'],database,target,outcome)
                cache_counts_recorded=cache_counts_recorded and counts_recorded
                cache_transactions.add(outcome)
            if entry['step']=='business:business-rollback':
                ended={item['transaction'] for item in record['trace']['transactions'] if item['operation']=='rollback' and item['succeeded'] and item['transaction'] is not None}
                update=r'(?is)^\s*UPDATE\s+(?:`?'+re.escape(database)+r'`?\s*\.\s*)?`?sample_contract`?(?=\s)'
                rollback=any(command['succeeded'] and command['transaction'] in ended and re.match(update,command['sql']) for command in record['trace']['commands']);reader.require(rollback,'capture-update-before-rollback')
        reader.require(not unlabeled and seen==set(expected) and names==set(files),'capture-record-file-coverage')
        if not java:reader.require(reader.integer(index['retainedEntries'])==counts['commands']+counts['transactions'],'capture-retained-count')
        summaries[target]={'indexSha256':files['index.json'],'files':files,'requests':wanted,**counts,'requestCoverageVerified':True,'traceStructureVerified':True,'sqlCountExpectationsVerified':False,'sqlSemanticAccepted':False,'physicalTargetVerified':False,'performanceAccepted':False}
        if child_sql:
            summaries[target]['childImport'] = child_facts
            summaries[target]['automaticIdentityRequests'] = recorded-wanted
            continue
        reader.require(rollback,'capture-rollback-missing')
        summaries[target]['updateThenRollbackObserved'] = True
        if exchange.existing_lines:
            reader.require(cache_transactions=={'rollback','commit'},'capture-cache-transaction-coverage')
            summaries[target]['directCacheWriteThenCommitAndRollbackObserved']=True
            summaries[target]['directCacheAffectedRowsRecorded']=cache_counts_recorded
    return {'format':'metarill-paired-sql-capture-review-v1','runId':value['runId'],'targets':summaries,'sqlSemanticAccepted':False,'performanceAccepted':False,'releaseAccepted':False}
