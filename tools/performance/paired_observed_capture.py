"""固定普通业务的请求采样及关闭产物保存；只证明采集结构，绝不自动批准SQL次数或语义等价。"""
from __future__ import annotations
import hashlib
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
    reader.require(type(value.get('version')) is int and value['version']==1 and value.get('purpose')=='paired-native-observed-v1' and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='not-started','capture-lease')
    reader.require(value.get('images')==resources.OBSERVATION_IMAGES and value.get('instancePin')==business.PIN and value.get('databases')==['metarill_fresh_'+run[:16]+'_paired_'+target for target in ('java','csharp')] and value.get('user')=='mtf_'+run[:16],'capture-scope')
    ports=value.get('ports');reader.require(type(ports) is dict and set(ports)=={'java','csharp'} and all(type(port) is int and 20000<=port<=40000 for port in ports.values()) and len(set(ports.values()))==2,'capture-ports')
    return run


def sample(run,target,step):
    """同一逻辑请求只分配一个固定32位采样值，不从服务端记录或响应反推。"""
    return hashlib.sha256((run+'\0'+target+'\0'+step).encode()).hexdigest()[:32]


def expected_steps(value,target):
    """按原两端HTTP/R规则冻结46请求；SQL次数必须另行审核，不在本计划臆测。"""
    rows=[]
    for step in ('identity:'+name for name in IDENTITY_STEPS):
        outcome=(200,200)
        if step=='identity:wrong-password':outcome=(200,500) if target=='java' else (401,401)
        elif step in {'identity:reader-create-denied','identity:page-denied'}:outcome=(200,500) if target=='java' else (403,403)
        rows.append((step,outcome))
    for name in business.STEPS:
        outcome=(200,200)
        if name in {'reader-update-denied','denied-detail'}:outcome=(200,500) if target=='java' else (403,403)
        elif name=='business-rollback':outcome=(200,500)
        elif name=='business-replay-conflict':outcome=(200,500) if target=='java' else (409,409)
        rows.append(('business:'+name,outcome))
    return [{'step':step,'sample':sample(value['runId'],target,step),'statusAtExit':outcome[0],'expectedCode':outcome[1],'chainSucceeded':True} for step,outcome in rows]


class Exchange:
    """只把固定step关联到原sender；令牌、正文和响应数据均不写请求账本。"""
    def __init__(self,value,sender):
        authorize(value);self.value=json.loads(json.dumps(value));self.sender=sender;self.sent={'java':[],'csharp':[]};self.finished={'java':[],'csharp':[]}
        self.plans={target:expected_steps(value,target) for target in ('java','csharp')}

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
        status,result=self.call(target,'business:'+step,method,path,body,token);return status,result

    def complete(self):
        """两端原身份和全部业务步骤都必须完成，不能只看一个successful trace。"""
        reader.require(all(len(self.finished[target])==len(self.plans[target]) for target in self.plans),'capture-client-coverage')

    def receipt(self):
        """可保留预期采样计划，不包含请求密码、令牌、正文或服务端返回的数据。"""
        return {'format':'metarill-paired-request-capture-plan-v1','runId':self.value['runId'],'targets':self.plans,'sent':{target:len(rows) for target,rows in self.sent.items()},'completed':{target:len(rows) for target,rows in self.finished.items()},'sqlCountExpectationsVerified':False}


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
    reader.require(not missing_instance,'capture-instance-count');reader.require(graceful,'capture-unfinalized-exit');exchange.complete();summaries={}
    for target,material in saved.items():
        directory=material['folder'];files=material['files'];index=reader.load(directory/'index.json',files.get('index.json'),4*1024*1024);java=target=='java'
        reader.shape(index,'format runId started completed observed problems requests finalized internallyConsistent '+('sourcesVerified dataSources' if java else 'dataAssemblySha256 expectedDatabases retainedEntries'))
        reader.require(index['format']==('metarill-paired-jdbc-index-v1' if java else 'metarill-paired-dotnet-index-v1') and index['runId']==value['runId'],'capture-index-format')
        reader.require(reader.flag(index['finalized']) and reader.flag(index['internallyConsistent']) and index['problems']==[],'capture-index-incomplete')
        expected={row['sample']:row for row in exchange.plans[target]};wanted=len(expected);reader.require(reader.integer(index['started'])==reader.integer(index['completed'])==reader.integer(index['observed'])==wanted,'capture-request-count')
        database='metarill_fresh_'+value['runId'][:16]+'_paired_'+target
        if java:reader.require(reader.flag(index['sourcesVerified']) and index['dataSources']==['dataSource'],'capture-sources')
        else:reader.require(index['dataAssemblySha256']==resources.OBSERVATION_DATA_SHA256 and index['expectedDatabases']==[database],'capture-data-assembly')
        refs=index['requests'];reader.require(type(refs) is list and len(refs)==wanted,'capture-reference-count');seen=set();names={'index.json'};counts={key:0 for key in ('commands','transactions','failedCommands','failedTransactions','nativeTemporalProofs')};rollback=False
        for reference in refs:
            reader.shape(reference,'id sha256');ident=reference['id'];reader.require(type(ident) is str and re.fullmatch(r'[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}' if java else '[a-f0-9]{32}',ident),'capture-record-id');name=ident+'.json';reader.require(name not in names,'capture-duplicate-record');names.add(name)
            record=reader.load(directory/name,reference['sha256'],64*1024*1024);reader.shape(record,'format runId id sample chainSucceeded errorType trace '+('statusAtFilterExit' if java else 'statusAtMiddlewareExit problems internallyConsistent'))
            reader.require(record['format']==('metarill-paired-jdbc-http-v1' if java else 'metarill-paired-dotnet-http-v1') and record['runId']==value['runId'] and record['id']==ident,'capture-record-format');label=record['sample'];reader.require(type(label) is str and label in expected and label not in seen,'capture-unplanned-sample');seen.add(label);entry=expected[label]
            reader.require(reader.integer(record['statusAtFilterExit' if java else 'statusAtMiddlewareExit'],100,599)==entry['statusAtExit'] and reader.flag(record['chainSucceeded']) and record['errorType'] is None,'capture-record-outcome')
            if not java:reader.require(reader.flag(record['internallyConsistent']) and record['problems']==[],'capture-record-incomplete')
            measured=reader.trace(record['trace'],target,{database});reader.require(measured['failedCommands']==0 and measured['failedTransactions']==0,'capture-driver-failure')
            for key in counts:counts[key]+=measured[key]
            if entry['step']=='business:business-rollback':
                ended={item['transaction'] for item in record['trace']['transactions'] if item['operation']=='rollback' and item['succeeded'] and item['transaction'] is not None}
                update=r'(?is)^\s*UPDATE\s+(?:`?'+re.escape(database)+r'`?\s*\.\s*)?`?sample_contract`?(?=\s)'
                rollback=any(command['succeeded'] and command['transaction'] in ended and re.match(update,command['sql']) for command in record['trace']['commands']);reader.require(rollback,'capture-update-before-rollback')
        reader.require(seen==set(expected) and names==set(files),'capture-record-file-coverage')
        if not java:reader.require(reader.integer(index['retainedEntries'])==counts['commands']+counts['transactions'],'capture-retained-count')
        reader.require(rollback,'capture-rollback-missing');summaries[target]={'indexSha256':files['index.json'],'files':files,'requests':wanted,**counts,'updateThenRollbackObserved':True,'requestCoverageVerified':True,'traceStructureVerified':True,'sqlCountExpectationsVerified':False,'sqlSemanticAccepted':False,'physicalTargetVerified':False,'performanceAccepted':False}
    result={'format':'metarill-paired-sql-capture-review-v1','runId':value['runId'],'targets':summaries,'privateKeyRetainedForReview':True,'sqlSemanticAccepted':False,'performanceAccepted':False,'releaseAccepted':False};(output/'capture-manifest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');return result
