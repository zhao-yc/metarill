"""固定原受保护文件的下载测量；真实准备／回收不计时，原10752行必须独立逐值保持。"""
from __future__ import annotations
import hashlib
import re
import threading
import urllib.error
import urllib.request
import paired_http as http
import paired_business_http as basic
import paired_identity_seed as seed
import paired_measurement_fixture as fixture
import paired_measurement as measurement
import server200_paired_resources as resources

FILE_BYTES=bytes(range(256))*256
FILE_SHA=hashlib.sha256(FILE_BYTES).hexdigest()
FILE_ROOT='/api/admin/sys/file-library'


def require(ok,code):
    """只回传固定断言位置，原始合成业务响应保存在本轮私有目录。"""
    if not ok:raise ValueError('measurement-file-'+code)


def config_for(value,targets,tokens):
    """固定普通读者、完整64KiB文件、三轮400次及1／4并发；ID由实际准备结果注入。"""
    require(value.get('purpose')==resources.FILE_MEASUREMENT_NATIVE and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='not-started' and type(value.get('fileReplayRequests')) is int and value['fileReplayRequests']==resources.FILE_REQUEST_BUDGET and type(value.get('fileFixtureRequests')) is int and value['fileFixtureRequests']==resources.FILE_FIXTURE_REQUESTS,'scope')
    require(value.get('images')==resources.IMPORT_RULE_IMAGES and not resources.is_observed(value) and not resources.is_write_measurement(value),'images')
    require(set(targets)=={'java','csharp'} and all(t.get('baseUrl')==f"http://127.0.0.1:{value['ports'][side]}/api" and t.get('container')=='metarill-perf-'+value['runId']+'-'+side and t.get('expectedImageId')==resources.IMPORT_RULE_IMAGES[side] and t.get('containerPort')==8080 for side,t in targets.items()),'targets')
    config=http.validate(dict(version=1,runId=value['runId'],transportProof='server200-loopback-v1',rounds=3,iterations=400,warmupIterations=8,concurrency=[1,4],requestTimeoutSeconds=5,maxP95Ratio=1.10,minThroughputRatio=0.95,targets={side:{**targets[side],'headers':{'loginStateId':tokens[side,'reader']}} for side in ('java','csharp')},scenarios=[dict(name='protected-file-64k',steps=[dict(method='GET',path='/admin/sys/file-library/protected-content/${file}',status=200,responseKind='binary',expectedBytes=len(FILE_BYTES),expectedSha256=FILE_SHA)])]))
    count=2*(len(config['concurrency'])*(config['rounds']*config['iterations']+config['warmupIterations'])+1)
    require(count==resources.FILE_REQUEST_BUDGET,'budget');return config


class FixtureExchange:
    """前后真实样本HTTP恰18次，完整响应私有留证；没有测量回放或任意文件读取能力。"""
    def __init__(self,root,value):
        """只在专用用途和实际随机根下创建证据，入口不得借旧用途读写本机目录。"""
        require(resources.is_file_measurement(value) and root==resources.root_for(value['runId']),'exchange-scope')
        self.value=value;self.folder=root/'file-fixture';self.folder.mkdir(mode=0o700);self.requests=[];self.checks=[]

    def raw(self,side,step,method,path,body,token,content_type=None):
        """禁代理、禁重定向、单次有界请求；不将认证头或物理存储键写入捕获。"""
        require(side in ('java','csharp') and re.fullmatch('[a-z0-9-]{1,60}',step) and method in ('GET','POST','DELETE') and len(self.requests)<resources.FILE_FIXTURE_REQUESTS,'fixture-request')
        require(path.startswith('/api/admin/') and not any(ord(c)<32 for c in path) and '\\' not in path,'fixture-path')
        pattern='[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}' if side=='java' else '[a-f0-9]{64}';require(type(token) is str and re.fullmatch(pattern,token),'fixture-token')
        headers={'loginStateId':token,'Accept':'*/*'}
        if content_type:headers['Content-Type']=content_type
        request=urllib.request.Request(f"http://127.0.0.1:{self.value['ports'][side]}"+path,data=body,headers=headers,method=method)
        opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),http.NoRedirect())
        try:response=opener.open(request,timeout=15)
        except urllib.error.HTTPError as error:response=error
        with response:
            raw=response.read(1024*1024+1);require(len(raw)<=1024*1024 and not 300<=response.status<400,'fixture-response')
            name=f'{len(self.requests)+1:02d}-{side}-{step}.response';(self.folder/name).write_bytes(raw)
            self.requests.append(dict(side=side,step=step,status=response.status,responseFile=name,responseBytes=len(raw),responseSha256=hashlib.sha256(raw).hexdigest(),requestBodySha256=hashlib.sha256(body).hexdigest() if body is not None else None));resources.write_state(self.folder/'http-requests.json',self.requests)
            return response.status,http.read_json(raw)

    def call(self,side,step,method,path,body,token):
        """普通成功必须符合原HTTP和R，不能以200状态掩盖业务失败。"""
        status,response=self.raw(side,step,method,path,http.canonical(body) if body is not None else None,token,'application/json' if body is not None else None)
        require(type(response) is dict and (status,response.get('code'))==(200,200),'fixture-http-'+step);return response.get('data')

    def upload(self,side,file_id,token):
        """原控件file multipart上传固定合成内容，不接受本机路径或自选服务端对象键。"""
        boundary='metarill-'+self.value['runId'];body=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="metarill-perf.txt"\r\nContent-Type: text/plain\r\n\r\n'.encode()+FILE_BYTES+f'\r\n--{boundary}--\r\n'.encode())
        status,response=self.raw(side,'file-upload','POST',FILE_ROOT+'/local-upload/'+basic.record_id(file_id),body,token,'multipart/form-data; boundary='+boundary)
        require(type(response) is dict and (status,response.get('code'))==(200,200),'fixture-upload')

    def check(self,label,actual,expected):
        """逐项物理读回保留私有journal，不保存SQL、认证信息或其它会话数据。"""
        self.checks.append(dict(label=label,actual=actual,expected=expected,passed=actual==expected));resources.write_state(self.folder/'physical-readback.json',self.checks);require(actual==expected,label)


def verify_sample(exchange,query,run,side,sample,deleted=False):
    """新增合同、文件与引用单独精确核验；原样本排除这一个ID后仍逐字段比较。"""
    database=fixture.database_name(run,side);contract_id=basic.record_id(sample['contractId']);file_id=basic.record_id(sample['fileId']);name=seed.literal('perf-file-'+run)
    active='IS NOT NULL' if deleted else 'IS NULL';attachment='JSON_LENGTH(attachments)=0' if deleted else "JSON_LENGTH(attachments)=1 AND JSON_UNQUOTE(JSON_EXTRACT(attachments,'$[0]'))="+seed.literal(file_id)
    condition=f'id={contract_id} AND BINARY name=BINARY {name} AND BINARY sample_customer_id=BINARY {seed.literal(str(fixture.ID_BASE))} AND BINARY sample_customer_name=BINARY '+seed.literal('性能客户-0000')+f' AND amount=12.50 AND {attachment} AND delete_no {active}'
    exchange.check(side+'-contract-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`sample_contract` WHERE '+condition+';'),'1')
    exchange.check(side+'-total-contracts-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`sample_contract`;'),str(fixture.COUNTS['sample_contract']+1))
    # 原固定Java用DELETED状态失效文件而不设置delete_no，不能误套C#的墓碑字段。
    size='size_bytes' if side=='java' else 'size';file_delete='IS NOT NULL' if deleted and side=='csharp' else 'IS NULL'
    status=('DELETED' if deleted else 'READY') if side=='java' else ('deleted' if deleted else 'ready')
    condition=f'id={file_id} AND {size}={len(FILE_BYTES)} AND delete_no {file_delete}'+" AND access_mode='PROTECTED' AND BINARY status=BINARY "+seed.literal(status)
    exchange.check(side+'-file-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_library` WHERE '+condition+';'),'1')
    exchange.check(side+'-total-files-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_library`;'),'1')
    reference=f'source_config_page_id=81002 AND source_config_column_id=82007 AND source_record_id={contract_id}' if side=='java' else f"page_id=81002 AND field_name='attachments' AND record_id={contract_id}"
    exchange.check(side+'-reference-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_ref` WHERE file_id={file_id} AND '+reference+f' AND delete_no {active};'),'1')
    exchange.check(side+'-active-refs-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_ref` WHERE file_id={file_id} AND delete_no IS NULL;'),'0' if deleted else '1')
    exchange.check(side+'-total-refs-'+str(deleted),query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_ref`;'),'1')


def run_file_measurement(root,value,targets,tokens,probe,query,require_ownership):
    """真实准备后先核两端完整下载一致，再按原阈值与采样测量；成功回收样本不掩盖测量失败。"""
    config=config_for(value,targets,tokens);require(root==resources.root_for(value['runId']) and callable(require_ownership),'root');require_ownership();exchange=FixtureExchange(root,value);run=value['runId'];samples={}
    for side in ('java','csharp'):
        database=fixture.database_name(run,side);writer=tokens[side,'writer'];reader=tokens[side,'reader']
        for table in ('yz_file_library','yz_file_ref'):exchange.check(side+'-initial-'+table,query(f'SELECT COUNT(*) FROM `{database}`.`{table}`;'),'0')
        plan=exchange.call(side,'file-plan','POST',FILE_ROOT+'/upload-plan',dict(originalName='metarill-perf.txt',mimeType='text/plain',sizeBytes=len(FILE_BYTES),configColumnId='82007',folderId=None),writer)
        require(type(plan) is dict and type(plan.get('plan')) is dict,'file-plan');file_id=basic.record_id(plan.get('fileId'));require(plan['plan'].get('uploadMode')=='SERVER_MULTIPART' and plan['plan'].get('uploadUrl')=='/admin/sys/file-library/local-upload/'+file_id,'file-url');exchange.upload(side,file_id,writer);exchange.call(side,'file-complete','POST',FILE_ROOT+'/complete',dict(fileId=file_id),writer)
        record=exchange.call(side,'contract-add','POST',basic.ROOTS['contract']+'/add',dict(name='perf-file-'+run,sampleCustomerId=str(fixture.ID_BASE),amount=12.5,remarks='受保护文件性能',attachments=[file_id]),writer)
        require(type(record) is dict,'contract-response');contract_id=basic.record_id(record.get('id'));require(not fixture.ID_BASE<=int(contract_id)<fixture.ID_BASE+30000,'contract-id-overlaps-original');samples[side]=dict(fileId=file_id,contractId=contract_id);verify_sample(exchange,query,run,side,samples[side])
        resolved=exchange.call(side,'reader-resolve','POST',FILE_ROOT+'/resolve',dict(fileIds=[file_id],pageId='81002',recordId=contract_id),reader);require(type(resolved) is list and len(resolved)==1 and resolved[0].get('fileId',resolved[0].get('id'))==file_id,'resolved-file')
        status,refused=exchange.raw(side,'denied-download','GET',FILE_ROOT+'/protected-content/'+file_id,None,tokens[side,'denied']);require(type(refused) is dict and (status,refused.get('code'))==((200,500) if side=='java' else (403,403)),'denied-file')
    stopped=threading.Event();failure_lock=threading.Lock()
    def transport(target,step,variables,timeout):
        """只为已验证目标注入本轮真实文件ID，逐次流式SHA256；失败后不再发送后续下载。"""
        require(not stopped.is_set(),'replay-stopped');side=next((s for s,t in config['targets'].items() if t==target),None);require(side is not None,'target')
        try:return http.request(target,step,{**variables,'file':samples[side]['fileId']},timeout)
        except Exception as error:
            # 只留首次固定目标及下载合同分类，不把原响应、令牌或内部错误正文写入报告。
            with failure_lock:
                if not stopped.is_set():
                    stopped.set();failure=dict(runId=run,target=side,errorType=type(error).__name__,reason={"下载完整性不符合固定长度与SHA-256":"binary-integrity","下载实体超过固定字节上限":"binary-too-long","下载长度头与固定完整实体不符":"binary-length-header","完整下载不接受编码变换或分段响应":"binary-encoding-or-range"}.get(str(error),'binary-transport'))
                    if isinstance(error,urllib.error.HTTPError):failure['status']=error.code
                    resources.write_state(output/'download-failure.json',failure)
            raise
    output=root/'measurement';output.mkdir(mode=0o700);fingerprints={}
    for side in ('java','csharp'):
        actual=transport(config['targets'][side],config['scenarios'][0]['steps'][0],{},config['requestTimeoutSeconds']);require(isinstance(actual,http.BinaryResponse),'preflight-binary');fingerprints[side]=actual.fingerprint
    require(fingerprints['java']==fingerprints['csharp'],'preflight-mismatch');resources.write_state(output/'preflight.json',dict(runId=run,configSha256=http.digest(config),responseFingerprints=fingerprints))
    result=measurement.run_trials(output,config,probe,query,'original-protected-file-64k',transport=transport)
    require_ownership()
    for side in ('java','csharp'):
        writer=tokens[side,'writer'];sample=samples[side];exchange.call(side,'contract-unbind','POST',basic.ROOTS['contract']+'/update',dict(id=sample['contractId'],attachments=[]),writer);exchange.call(side,'contract-delete','DELETE',basic.ROOTS['contract'],dict(id=sample['contractId']),writer);exchange.call(side,'file-delete','DELETE',FILE_ROOT+'/'+sample['fileId'],None,writer);verify_sample(exchange,query,run,side,sample,True)
    require(len(exchange.requests)==resources.FILE_FIXTURE_REQUESTS,'fixture-completeness');result['fileFixture']=dict(targets=samples,physicalReadbackChecks=len(exchange.checks),fixtureHttpRequests=len(exchange.requests),streamedBytesPerResponse=len(FILE_BYTES),fileSha256=FILE_SHA,fixtureRequestsTimed=False,originalFixtureRowsPerTarget=10752);return result
