"""固定原三表的真实Excel／文件／维护验收；不用于计时，也不改变旧观测用途。"""
from __future__ import annotations
import base64
import hashlib
import json
from pathlib import Path
import re
import urllib.error
import urllib.parse
import urllib.request
import paired_business_http as basic
import paired_http as http
import paired_identity_seed as seed
import paired_workbook_container as oracle
import server200_paired_resources as resources

PURPOSE = 'paired-extended-business-v1'
WORKBOOK_OPERATIONS = 10
MAINTENANCE_PROFILE = 'original-auto-caches-without-join-columns-v1'
FILE_ROOT = '/api/admin/sys/file-library'
MAINTENANCE_ROOT = '/api/admin/sys/system_maintenance'
TABLE_IDS = ('810000000000000012', '810000000000000013')
FILE_BYTES = bytes(range(256)) * 256
HEADERS = {'customer':['客户名称','备注'], 'contract':['合同名称','客户','合同金额','备注','合同附件'], 'line':['明细名称','明细金额','备注']}


def require(ok, code):
    """固定错误码仅描述断言位置，不带令牌、私有数据或SQL。"""
    if not ok:
        raise ValueError('business-extended-' + code)


def names(run):
    """所有夹具名称由本轮运行号生成，维护写入不接受客户端任意原值。"""
    require(type(run) is str and re.fullmatch('[a-f0-9]{32}', run), 'run')
    prefix = '扩展-' + run[:16]
    return {key:[prefix + '-' + text + '-' + str(i) for i in (1,2)]
            for key,text in (('customer','客户'),('contract','合同'),('line','明细'))}


def validate_original_maintenance_catalog(catalog):
    """固定原模型没有声明联接列；不能把Java真实不支持当作双方维护已通过。"""
    expected={
        '810000000000000011':('sample_customer',{'name','remarks'}),
        '810000000000000012':('sample_contract',{'name','sample_customer_id','amount','remarks','attachments'}),
        '810000000000000013':('sample_contract_line',{'name','sample_contract_id','amount','remarks'}),
    }
    tables=catalog.get('configTables',[]);columns=catalog.get('configColumns',[])
    require(type(tables) is list and len(tables)==3 and {table.get('id') for table in tables}==set(expected),'maintenance-catalog-tables')
    for table in tables:require(table.get('tableName')==expected[table['id']][0] and table.get('isRongyu')==1,'maintenance-catalog-table')
    require(type(columns) is list and len(columns)==11 and {(column.get('configTableId'),column.get('columnName')) for column in columns}=={(table,name) for table,(_,fields) in expected.items() for name in fields},'maintenance-catalog-columns')
    require(all(type(column.get('isJoinColumn')) is int and column['isJoinColumn']==0 and column.get('isVirtualColumn')==0 for column in columns),'maintenance-catalog-join')
    return dict(profile=MAINTENANCE_PROFILE,declaredJoinColumns=0,automaticCacheColumns=['sample_customer_name','sample_contract_name'],originalAutomaticCacheBackfillSupported=False)


def maintenance_fixture_sql(run, target, contract_ids, line_ids):
    """只污染本轮四行已有缓存，原值／行名／父键逐项匹配；失败断开即回滚。"""
    expected = names(run)
    require(target in ('java','csharp') and type(contract_ids) is list and type(line_ids) is list and len(contract_ids)==len(line_ids)==2, 'fixture-shape')
    ids = [basic.record_id(value) for value in contract_ids + line_ids]
    require(len(set(ids))==4, 'fixture-ids')
    database = 'metarill_fresh_' + run[:16] + '_paired_' + target
    customers = [expected['customer'][0]+'-改名', expected['customer'][1]]
    contracts = [expected['contract'][0]+'-改名', expected['contract'][1]]
    stale = seed.literal('扩展旧缓存-' + run[:16])
    lines = ['START TRANSACTION;']
    for index, identifier in enumerate(ids):
        table, column = ('sample_contract','sample_customer_name') if index<2 else ('sample_contract_line','sample_contract_name')
        offset=index%2;before=(customers if index<2 else contracts)[offset]
        row_name=(contracts if index<2 else expected['line'])[offset]
        where=f'id={identifier} AND delete_no IS NULL AND BINARY `name`=BINARY {seed.literal(row_name)} AND `{column}` <=> {seed.literal(before)}'
        if index>=2:where+=f' AND BINARY `sample_contract_id`=BINARY {seed.literal(contract_ids[offset])}'
        lines += [f'UPDATE `{database}`.`{table}` SET `{column}`={stale} WHERE {where};', f'SET @mt_extended_{index}=ROW_COUNT();', *basic.mysql_assertion_sql(f'@mt_extended_{index}=1')]
    lines += ['COMMIT;', 'SELECT '+','.join('@mt_extended_'+str(i) for i in range(4))+';']
    return '\n'.join(lines)


class Exchange:
    """只向本轮两个固定回环宿主单次发请求；原始业务响应留私有证据，认证头不落盘。"""
    def __init__(self, value, root, state, state_path):
        """入口验证先于目录创建；就绪／测量租约不能借此执行业务。"""
        require(value.get('purpose')==(resources.FILE_AUTH_BUSINESS if resources.is_file_auth_business(value) else PURPOSE) and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='running' and value.get('workbookOperations')==WORKBOOK_OPERATIONS and value.get('maintenanceProfile')==MAINTENANCE_PROFILE, 'lease')
        require(value.get('images')==resources.images_for(value) and root==resources.root_for(value['runId']) and state_path==root/'native-state.json' and state.get('runId')==value['runId'], 'scope')
        ports=value.get('ports',{});require(set(ports)=={'java','csharp'} and len(set(ports.values()))==2 and all(type(n) is int and 20000<=n<=40000 for n in ports.values()), 'ports')
        oracle.read_inputs(root)
        self.value,self.root,self.state,self.state_path=value,root,state,state_path
        self.folder=root/'extended-evidence';self.folder.mkdir(mode=0o700)
        self.requests=[];self.workbooks=[]

    def raw(self,target,step,method,path,body=None,token=None,content_type=None):
        """禁代理／重定向、有界响应，附件字节与工作簿直接保留，错误不自动重试。"""
        require(target in ('java','csharp') and re.fullmatch('[a-z0-9-]{1,90}',step) and method in ('GET','POST','DELETE') and len(self.requests)<200, 'request')
        require(type(path) is str and path.startswith('/api/admin/') and not any(ord(c)<32 for c in path) and '\\' not in path, 'path')
        if token is not None:
            pattern='[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}' if target=='java' else '[a-f0-9]{64}'
            require(type(token) is str and re.fullmatch(pattern,token), 'token')
        require(body is None or type(body) is bytes and len(body)<=1024*1024, 'body')
        headers={'Accept':'*/*'}
        if token is not None:headers['loginStateId']=token
        if content_type is not None:headers['Content-Type']=content_type
        request=urllib.request.Request(f"http://127.0.0.1:{self.value['ports'][target]}"+path,data=body,headers=headers,method=method)
        opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),http.NoRedirect())
        try:response=opener.open(request,timeout=15)
        except urllib.error.HTTPError as error:response=error
        with response:
            data=response.read(4*1024*1024+1);require(len(data)<=4*1024*1024,'response-size')
            require(300>response.status or response.status>=400,'redirect')
            ordinal=len(self.requests)+1;filename=f'{ordinal:03d}-{target}-{step}.response'
            (self.folder/filename).write_bytes(data)
            entry=dict(target=target,step=step,method=method,status=response.status,responseFile=filename,responseSha256=hashlib.sha256(data).hexdigest(),responseBytes=len(data),requestBodySha256=hashlib.sha256(body).hexdigest() if body is not None else None)
            self.requests.append(entry);resources.write_state(self.folder/'http-requests.json',self.requests)
            return response.status,data

    def request(self,target,step,method,path,body,token):
        """原37步及扩展JSON共用一次真实传输，不复用内存中的假响应。"""
        status,data=self.raw(target,step,method,path,http.canonical(body) if body is not None else None,token,'application/json' if body is not None else None)
        return status,http.read_json(data)

    def upload(self,target,step,file_id,token):
        """原文件控件的单个file multipart，不提交客户端存储路径或服务端对象键。"""
        basic.record_id(file_id);boundary='metarill-'+self.value['runId']
        data=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="metarill.txt"\r\nContent-Type: text/plain\r\n\r\n'.encode()+FILE_BYTES+f'\r\n--{boundary}--\r\n'.encode())
        status,raw=self.raw(target,step,'POST',FILE_ROOT+'/local-upload/'+file_id,data,token,'multipart/form-data; boundary='+boundary)
        return status,http.read_json(raw)

    def workbook(self,target,step,request):
        """原模块实际读文件所得rows才送导入；完整文件和读回证据按次保存。"""
        require(target in ('java','csharp') and re.fullmatch('[a-z0-9-]{1,90}',step) and len(self.workbooks)<WORKBOOK_OPERATIONS,'workbook-budget')
        result=oracle.invoke(oracle.descriptor(self.value['runId']),request,self.root,self.state,self.state_path)
        prefix=f'{len(self.workbooks)+1:02d}-{target}-{step}'
        resources.write_state(self.folder/(prefix+'.request.json'),request)
        resources.write_state(self.folder/(prefix+'.result.json'),result)
        data=base64.b64decode(result['workbookBase64'] if request['operation']=='create' else request['base64'],validate=True)
        (self.folder/(prefix+'.'+result['format'])).write_bytes(data)
        self.workbooks.append(dict(target=target,step=step,operation=request['operation'],sha256=result['sha256'],format=result['format'],byteLength=len(data),rows=len(result['rows'])))
        return result

    def receipt(self):
        """只在十次实际文件操作完整时返回完成范围，不把业务验收记作性能。"""
        require(len(self.workbooks)==WORKBOOK_OPERATIONS,'workbook-completeness')
        return dict(requests=self.requests,workbooks=self.workbooks,performanceAccepted=False,releaseAccepted=False)


def run_extended(value,tokens,exchange,query,fixture_write):
    """保留原37步，再执行实际导入／导出、普通附件权限、改名与缓存维护。"""
    run,databases=basic.authorize(value,tokens)
    require(value.get('workbookOperations')==WORKBOOK_OPERATIONS and value.get('maintenanceProfile')==MAINTENANCE_PROFILE and callable(fixture_write), 'scope')
    basic_facts=basic.run_basic(value,tokens,exchange.request,query)
    expected=names(run);facts=[]
    for target,database in databases.items():
        writer=tokens[target]['writer'];reader=tokens[target]['reader'];denied=tokens[target]['denied']
        def call(step,method,path,body=None,kind='writer',error=False):
            """同时核HTTP和原R；正常业务规则拒绝沿原HTTP200/R500。"""
            status,response=exchange.request(target,step,method,path,body,tokens[target][kind])
            require(type(response) is dict and type(response.get('code')) is int and (status,response['code'])==((200,500) if error else (200,200)),'http-'+step)
            return response.get('data')
        def physical(table,identifier,fields):
            """只读固定三表与已验证ID，字符串使用UTF8字面量，缓存用物理读独立证明。"""
            basic.record_id(identifier);require(table in basic.FIELDS and set(fields)<=basic.FIELDS[table],'physical-fields')
            conditions=['id='+identifier,'delete_no IS NULL']
            for column,wanted in fields.items():
                if column=='attachments':conditions.append(f"JSON_CONTAINS(COALESCE(NULLIF(attachments,''),'[]'),{seed.literal(json.dumps(wanted,separators=(',',':')))}) AND JSON_LENGTH(COALESCE(NULLIF(attachments,''),'[]'))={len(wanted)}")
                elif type(wanted) in (int,float):conditions.append('`'+column+'` <=> '+str(wanted))
                elif wanted is None:conditions.append('`'+column+'` IS NULL')
                else:conditions.append('BINARY `'+column+'` <=> BINARY '+seed.literal(wanted))
            require(query(f'SELECT COUNT(*) FROM `{database}`.`{table}` WHERE '+' AND '.join(conditions)+';')=='1','physical-value')
        def ids_by_names(table,values):
            require(table in basic.FIELDS and len(values)==2,'lookup')
            rows=query(f"SELECT CAST(id AS CHAR) FROM `{database}`.`{table}` WHERE delete_no IS NULL AND name IN ("+','.join(seed.literal(name) for name in values)+') ORDER BY name;').splitlines()
            require(len(rows)==2 and len(set(rows))==2,'lookup-rows')
            return [basic.record_id(identifier) for identifier in rows]
        def refs(file_id,identifiers):
            column='source_record_id' if target=='java' else 'record_id'
            ids=','.join(basic.record_id(identifier) for identifier in identifiers)
            return query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_ref` WHERE delete_no IS NULL AND file_id={basic.record_id(file_id)} AND `{column}` IN ({ids});')
        def exported(kind,body,headers,rows):
            grant=call('export-'+kind,'POST',basic.ROOTS[kind]+'/initExport',body)
            require(type(grant) is str and re.fullmatch('[A-Za-z0-9_-]{16,128}',grant),'export-grant')
            status,data=exchange.raw(target,'download-'+kind,'GET','/api/admin/sys/export/export?exportId='+urllib.parse.quote(grant,safe=''))
            require(status==200,'export-download')
            output=exchange.workbook(target,'read-export-'+kind,dict(version=1,operation='read',base64=base64.b64encode(data).decode(),expectedSha256=hashlib.sha256(data).hexdigest()))
            require(output['headers']==headers and output['rows']==rows and output['format']==('xls' if target=='java' else 'xlsx'),'export-values-'+kind)
        # 每页采用原真实工作簿解析链，附件空数组先验证原两端共同导入协议；非空绑定随后走原标准更新。
        customer_rows=[[name,'客户备注'] for name in expected['customer']]
        workbook=exchange.workbook(target,'create-customers',dict(version=1,operation='create',headers=HEADERS['customer'],rows=customer_rows))
        call('import-customers','POST',basic.ROOTS['customer']+'/importDatas81001',{'requestJson':workbook['rows']})
        customers=ids_by_names('sample_customer',expected['customer'])
        for identifier,name in zip(customers,expected['customer']):physical('sample_customer',identifier,dict(name=name,remarks='客户备注'))
        contract_rows=[[name,customer,amount,'合同备注','[]'] for name,customer,amount in zip(expected['contract'],expected['customer'],(12.5,3.25))]
        workbook=exchange.workbook(target,'create-contracts',dict(version=1,operation='create',headers=HEADERS['contract'],rows=contract_rows))
        call('import-contracts','POST',basic.ROOTS['contract']+'/importDatas81002',{'requestJson':workbook['rows']})
        contracts=ids_by_names('sample_contract',expected['contract']);lines=[]
        for index,identifier in enumerate(contracts):
            physical('sample_contract',identifier,dict(name=expected['contract'][index],sample_customer_id=customers[index],sample_customer_name=expected['customer'][index],amount=(12.5,3.25)[index],remarks='合同备注',attachments=[]))
            data=call('line-add-'+str(index),'POST',basic.ROOTS['line']+'/add?parentId='+identifier,dict(name=expected['line'][index],sampleContractId=identifier,amount=1.25,remarks='明细备注'))
            line=basic.record_id(data.get('id') if type(data) is dict else None);lines.append(line)
            physical('sample_contract_line',line,dict(sample_contract_id=identifier,sample_contract_name=expected['contract'][index],name=expected['line'][index],amount=1.25,remarks='明细备注'))
        plan=call('file-plan','POST',FILE_ROOT+'/upload-plan',dict(originalName='metarill.txt',mimeType='text/plain',sizeBytes=len(FILE_BYTES),configColumnId='82007',folderId=None))
        require(type(plan) is dict and type(plan.get('plan')) is dict,'file-plan')
        file_id=basic.record_id(plan.get('fileId'));require(plan['plan'].get('uploadMode')=='SERVER_MULTIPART' and plan['plan'].get('uploadUrl')=='/admin/sys/file-library/local-upload/'+file_id,'file-plan-url')
        status,response=exchange.upload(target,'file-upload',file_id,writer)
        require(status==200 and type(response) is dict and response.get('code')==200,'file-upload')
        descriptor=call('file-complete','POST',FILE_ROOT+'/complete',dict(fileId=file_id))
        require(type(descriptor) is dict and descriptor.get('fileId',descriptor.get('id'))==file_id,'file-descriptor')
        for index,identifier in enumerate(contracts):
            call('file-bind-'+str(index),'POST',basic.ROOTS['contract']+'/update',dict(id=identifier,attachments=[file_id]))
            physical('sample_contract',identifier,dict(attachments=[file_id]))
        require(refs(file_id,contracts)=='2','file-references')
        resolved=call('file-reader-resolve','POST',FILE_ROOT+'/resolve',dict(fileIds=[file_id],pageId='81002',recordId=contracts[0]),kind='reader')
        require(type(resolved) is list and len(resolved)==1 and resolved[0].get('fileId',resolved[0].get('id'))==file_id,'file-resolve')
        status,data=exchange.raw(target,'file-reader-content','GET',FILE_ROOT+'/protected-content/'+file_id,token=reader)
        require(status==200 and data==FILE_BYTES,'file-binary')
        status,data=exchange.raw(target,'file-denied-content','GET',FILE_ROOT+'/protected-content/'+file_id,token=denied)
        failure=http.read_json(data);require(type(failure) is dict and (status,failure.get('code'))==((200,500) if target=='java' else (403,403)),'file-denied')
        require(refs(file_id,contracts)=='2','file-denied-no-write')
        call('file-delete-bound','DELETE',FILE_ROOT+'/'+file_id,error=True)
        exported('customer',dict(pageNo=1,pageSize=20,filters={'name':'扩展-'+run[:16]},orderBy={'column':'name','order':'ascending'}),HEADERS['customer'],[dict(zip(HEADERS['customer'],row)) for row in customer_rows])
        # 保留非空附件格执行真实下载，字符串长ID必须逐字读回，不能先清空再验导出。
        exported_rows=[dict(zip(HEADERS['contract'],row[:-1]+[json.dumps([file_id],separators=(',',':'))])) for row in contract_rows]
        exported('contract',dict(pageNo=1,pageSize=20,filters={'name':'扩展-'+run[:16]},orderBy={'column':'name','order':'ascending'}),HEADERS['contract'],exported_rows)
        exported('line',dict(pageNo=1,pageSize=20,filters={},parentId=contracts[0]),HEADERS['line'],[dict(zip(HEADERS['line'],[expected['line'][0],1.25,'明细备注']))])
        for index,identifier in enumerate(contracts):call('file-unbind-'+str(index),'POST',basic.ROOTS['contract']+'/update',dict(id=identifier,attachments=[]))
        require(refs(file_id,contracts)=='0','file-unbound')
        call('file-delete-unbound','DELETE',FILE_ROOT+'/'+file_id)
        call('customer-rename','POST',basic.ROOTS['customer']+'/update',dict(id=customers[0],name=expected['customer'][0]+'-改名'))
        call('contract-rename','POST',basic.ROOTS['contract']+'/update',dict(id=contracts[0],name=expected['contract'][0]+'-改名'))
        for index,identifier in enumerate(contracts):physical('sample_contract',identifier,dict(sample_customer_name=expected['customer'][index]+('-改名' if index==0 else '')))
        for index,identifier in enumerate(lines):physical('sample_contract_line',identifier,dict(sample_contract_name=expected['contract'][index]+('-改名' if index==0 else '')))
        require(fixture_write(target,contracts,lines)==(1,1,1,1),'maintenance-fixture')
        stale='扩展旧缓存-'+run[:16]
        for identifier in contracts:physical('sample_contract',identifier,dict(sample_customer_name=stale))
        for identifier in lines:physical('sample_contract_line',identifier,dict(sample_contract_name=stale))
        tables=call('maintenance-tables','POST',MAINTENANCE_ROOT+'/tables',{})
        require(type(tables) is list and set(TABLE_IDS)<={str(item.get('id')) for item in tables},'maintenance-options')
        maintenance=[]
        for repeat in (0,1):
            for index,table_id in enumerate(TABLE_IDS):
                result=call('maintenance-'+str(repeat)+'-'+str(index),'POST',MAINTENANCE_ROOT+'/backfill_join_columns',dict(configTableId=table_id))
                require(type(result) is dict,'maintenance-result');maintenance.append(result)
                if target=='java':
                    # 原冻结模型无显式join列：严格核其真实零计划与不修复，不能将空操作冒充维护通过。
                    require(result.get('configTableId')==table_id and result.get('joinColumnCount')==0 and result.get('affectedRows')==0 and result.get('details')==[],'original-maintenance-empty-plan')
                else:
                    require(result.get('configTableId')==table_id and type(result.get('joinColumnCount')) is int and result['joinColumnCount']>=1,'csharp-maintenance-plan')
                    require(type(result.get('affectedRows')) is int and result['affectedRows']==(2 if repeat==0 else 0),'csharp-maintenance-repair-count')
            for index,identifier in enumerate(contracts):physical('sample_contract',identifier,dict(sample_customer_name=stale if target=='java' else expected['customer'][index]+('-改名' if index==0 else '')))
            for index,identifier in enumerate(lines):physical('sample_contract_line',identifier,dict(sample_contract_name=stale if target=='java' else expected['contract'][index]+('-改名' if index==0 else '')))
        for index,identifier in enumerate(lines):call('line-delete-'+str(index),'DELETE',basic.ROOTS['line']+'?parentId='+contracts[index],dict(id=identifier))
        for index,identifier in enumerate(contracts):call('contract-delete-'+str(index),'DELETE',basic.ROOTS['contract'],dict(id=identifier))
        for index,identifier in enumerate(customers):call('customer-delete-'+str(index),'DELETE',basic.ROOTS['customer'],dict(id=identifier))
        for table,total in (('sample_customer',4),('sample_contract',3),('sample_contract_line',3)):
            require(query(f'SELECT COUNT(*),SUM(delete_no IS NULL) FROM `{database}`.`{table}`;')==str(total)+'\t0','final-physical')
        require(refs(file_id,contracts)=='0','final-file-refs')
        facts.append(dict(target=target,customerIds=customers,contractIds=contracts,lineIds=lines,fileId=file_id,originalWorkbookImportAndExports=True,protectedFileBytes=len(FILE_BYTES),protectedFileSha256=hashlib.sha256(FILE_BYTES).hexdigest(),twoActiveReferencesAndUnbindVerified=True,renamePropagationVerified=True,fourStaleCellsRepairedAndRepeated=target=='csharp',originalAutomaticCacheMaintenanceUnsupported=target=='java',maintenanceReceipts=maintenance,nonemptyExcelExportFileCellsVerified=True,nonemptyExcelImportFileCellsVerified=False,childExcelImportVerified=False))
    return dict(acceptedScope='paired-original-crud-excel-file-maintenance-functional-only',originalAutomaticCacheMaintenanceParityAccepted=False,basic=basic_facts,targets=facts,evidence=exchange.receipt(),sqlSemanticAccepted=False,performanceAccepted=False,releaseAccepted=False)
