"""同模型普通账号的原CRUD验收流程；只供已批准独占双库协调器调用，不提供自由SQL写入口。"""
from __future__ import annotations
from decimal import Decimal
import re
import hashlib
import paired_identity_seed as seed
import server200_paired_resources as resources

PIN={'hostname':'3107e214a8a3','serverUuid':'6d3d024a-9d73-11f1-9939-ce69308a21dc'}
ROOTS={'customer':'/api/admin/auto/sample_customer/81001','contract':'/api/admin/auto/sample_contract/81002','line':'/api/admin/auto/sample_contract_line/81003'}
FIELDS={'sample_customer':{'name','remarks'},'sample_contract':{'name','sample_customer_id','sample_customer_name','amount','remarks','attachments'},'sample_contract_line':{'name','sample_contract_id','sample_contract_name','amount','remarks'}}
STEPS=('customer-add','customer-detail-create','customer-omit-update','customer-detail-omit','customer-clear-update','customer-detail-clear','customer-copy','customer-copy-update','reader-detail','reader-update-denied','denied-detail','customer-page','customer-count','contract-add','contract-detail-create','contract-pending','business-preview','business-rollback','contract-detail-after-rollback','business-complete','contract-detail-complete','business-replay-conflict','line-add','line-detail-create','child-page','customer-rename','contract-detail-rename','contract-rename','line-detail-rename','line-omit-update','line-detail-omit','line-delete','child-count-after-delete','contract-delete','customer-copy-delete','customer-delete','customer-count-after-delete')


def require(condition,code):
    """仅返回固定错误码，不泄漏响应正文、令牌、SQL或私有路径。"""
    if not condition:raise ValueError(code)


def record_id(value):
    """实际生成ID必须是高于JS安全整数范围的十进制字符串，禁止浮点或数字冒充。"""
    require(type(value) is str and re.fullmatch('[1-9][0-9]{0,18}',value) and 9007199254740991<int(value)<=2**63-1,'business-string-id')
    return value


def authorize(value,tokens):
    """所有I/O前核用途、直接批准、唯一双库、固定新镜像及三普通登录令牌。"""
    run=value.get('runId');require(type(run) is str and re.fullmatch('[a-f0-9]{32}',run),'business-run')
    databases=['metarill_fresh_'+run[:16]+'_paired_'+target for target in ('java','csharp')]
    require(value.get('version')==1 and type(value.get('version')) is int and value.get('purpose')=='paired-observed-business-v1' and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='running','business-lease')
    require(value.get('databases')==databases and value.get('user')=='mtf_'+run[:16] and value.get('instancePin')==PIN and value.get('images')==resources.OBSERVATION_IMAGES,'business-scope')
    require(type(tokens) is dict and set(tokens)=={'java','csharp'},'business-tokens')
    for target in ('java','csharp'):
        require(type(tokens[target]) is dict and set(tokens[target])==set(seed.KINDS),'business-tokens')
        pattern='[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}' if target=='java' else '[a-f0-9]{64}'
        require(all(type(token) is str and re.fullmatch(pattern,token) for token in tokens[target].values()) and len(set(tokens[target].values()))==3,'business-tokens')
    return run,dict(zip(('java','csharp'),databases))


def expect_record(data,expected):
    """只断言原页开放字段；原协议允许可空字段省略时仍以独立物理IS NULL验证主动清空。"""
    require(type(data) is dict,'business-record')
    for key,wanted in expected.items():
        actual=data.get(key)
        if key=='id' or key in {'sampleCustomerId','sampleContractId'}:
            record_id(actual);require(actual==wanted,'business-reference')
        elif type(wanted) in (int,float):
            require(type(actual) in (int,float) and Decimal(str(actual))==Decimal(str(wanted)),'business-number')
        else:require(actual==wanted,'business-field')
    return data


def preview_digest(identifier,name,remarks):
    """独立按原Java/C#算法和发布金额scale=2计算，不用服务端最新摘要替代原值。"""
    values=(record_id(identifier),name,'12.50',remarks)
    source=''.join(str(len(text.encode('utf-16-le'))//2)+':'+text for text in values)
    return hashlib.sha256(source.encode('utf-8')).hexdigest()


def run_basic(value,tokens,request,query):
    """两端各37笔原HTTP，写入只走原生成接口；独立连接只做精确SELECT核数据和墓碑。"""
    run,databases=authorize(value,tokens)
    require(query('SELECT @@hostname,@@server_uuid;')==PIN['hostname']+'\t'+PIN['serverUuid'],'business-instance')
    for database in databases.values():
        for table in FIELDS:require(query(f'SELECT COUNT(*) FROM `{database}`.`{table}`;')=='0','business-not-empty')
    facts=[]
    for target,database in databases.items():
        calls=[];prefix='配对客户-'+run[:16];name=prefix+'-old';renamed=prefix+'-01';copy_name=prefix+'-02';final_name=prefix+'-11';remarks="中文'引号\\与换行\n原备注";copy_remarks='复制备注';contract_name='配对合同-'+run[:16]+'-01';contract_final='配对合同-'+run[:16]+'-11';line_name='配对明细-'+run[:16];line_remarks='明细备注'
        def call(step,kind,method,path,body=None,denied=False,domain_error=False,conflict=False):
            """固定步骤一次发出；原R和HTTP须同时符合，失败不自动重试或继续写入。"""
            require(len(calls)<len(STEPS) and STEPS[len(calls)]==step,'business-step-order');calls.append(step)
            status,response=request(target,step,method,path,body,tokens[target][kind]);require(type(response) is dict and type(response.get('code')) is int,'business-response')
            wanted=(200,500) if domain_error or (denied or conflict) and target=='java' else (409,409) if conflict else (403,403) if denied else (200,200)
            require(type(status) is int and (status,response['code'])==wanted,'business-http-result')
            return response.get('data')
        def physical(table,identifier,fields,deleted=False):
            """全是本轮三个固定表和已验证ID，值用UTF8字面量；不提供任意列或SQL写入。"""
            record_id(identifier);require(table in FIELDS and set(fields)<=FIELDS[table],'business-physical-fields')
            conditions=['id='+identifier,'delete_no IS '+('NOT NULL' if deleted else 'NULL')]
            for column,wanted in fields.items():
                if column=='attachments':require(wanted==[],'business-empty-files');conditions.append("JSON_LENGTH(COALESCE(NULLIF(`attachments`,''),'[]'))=0")
                elif wanted is None:conditions.append('`'+column+'` IS NULL')
                elif type(wanted) in (float,int):conditions.append('`'+column+'` <=> '+str(Decimal(str(wanted))))
                else:conditions.append('`'+column+'` <=> '+seed.literal(wanted))
            require(query(f'SELECT COUNT(*) FROM `{database}`.`{table}` WHERE '+ ' AND '.join(conditions)+';')=='1','business-physical-value')
        customer=call('customer-add','writer','POST',ROOTS['customer']+'/add',{'name':name,'remarks':remarks});cid=record_id(customer.get('id') if type(customer) is dict else None);expect_record(customer,dict(id=cid,name=name,remarks=remarks));physical('sample_customer',cid,{'name':name,'remarks':remarks})
        expect_record(call('customer-detail-create','writer','GET',ROOTS['customer']+'/detail/'+cid),dict(id=cid,name=name,remarks=remarks))
        call('customer-omit-update','writer','POST',ROOTS['customer']+'/update',{'id':cid,'name':renamed});expect_record(call('customer-detail-omit','writer','GET',ROOTS['customer']+'/detail/'+cid),dict(id=cid,name=renamed,remarks=remarks));physical('sample_customer',cid,{'name':renamed,'remarks':remarks})
        call('customer-clear-update','writer','POST',ROOTS['customer']+'/update',{'id':cid,'remarks':None});expect_record(call('customer-detail-clear','writer','GET',ROOTS['customer']+'/detail/'+cid),dict(id=cid,name=renamed,remarks=None));physical('sample_customer',cid,{'name':renamed,'remarks':None})
        copied=call('customer-copy','writer','POST',ROOTS['customer']+'/copy/'+cid);copy_id=record_id(copied.get('id') if type(copied) is dict else None);require(copy_id!=cid,'business-copy-id');expect_record(copied,dict(id=copy_id,name=renamed,remarks=None))
        call('customer-copy-update','writer','POST',ROOTS['customer']+'/update',{'id':copy_id,'name':copy_name,'remarks':copy_remarks});physical('sample_customer',copy_id,{'name':copy_name,'remarks':copy_remarks});physical('sample_customer',cid,{'name':renamed,'remarks':None})
        expect_record(call('reader-detail','reader','GET',ROOTS['customer']+'/detail/'+cid),dict(id=cid,name=renamed,remarks=None))
        call('reader-update-denied','reader','POST',ROOTS['customer']+'/update',{'id':cid,'name':'forbidden-'+run[:16]},denied=True);call('denied-detail','denied','GET',ROOTS['customer']+'/detail/'+cid,denied=True);physical('sample_customer',cid,{'name':renamed,'remarks':None})
        page_request={'pageNo':1,'pageSize':20,'filters':{'name':prefix},'orderBy':{'column':'name','order':'ascending'}}
        page=call('customer-page','writer','POST',ROOTS['customer']+'/page',page_request);require(type(page) is dict and type(page.get('total')) is int and page['total']==2 and type(page.get('records')) is list and len(page['records'])==2,'business-page')
        for row,identifier,wanted in zip(page['records'],(cid,copy_id),(renamed,copy_name)):expect_record(row,dict(id=identifier,name=wanted))
        count=call('customer-count','writer','POST',ROOTS['customer']+'/count',page_request);require(type(count) is int and count==2,'business-count')
        contract=call('contract-add','writer','POST',ROOTS['contract']+'/add',{'name':contract_name,'sampleCustomerId':cid,'amount':12.5,'remarks':'合同备注','attachments':[]});contract_id=record_id(contract.get('id') if type(contract) is dict else None);require(contract_id not in {cid,copy_id},'business-distinct-ids')
        expected_contract=dict(id=contract_id,name=contract_name,sampleCustomerId=cid,sampleCustomerName=renamed,amount=12.5,remarks='合同备注',attachments=[]);expect_record(call('contract-detail-create','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'name':contract_name,'sample_customer_id':cid,'sample_customer_name':renamed,'amount':12.5,'remarks':'合同备注','attachments':[]})
        parent='?parentId='+contract_id
        call('contract-pending','writer','POST',ROOTS['contract']+'/update',{'id':contract_id,'remarks':'待处理'});physical('sample_contract',contract_id,{'remarks':'待处理','amount':12.5})
        digest=preview_digest(contract_id,contract_name,'待处理');business='/api/admin/custom/sample-contract-agent'
        expect_record(call('business-preview','writer','POST',business+'/preview',{'id':contract_id}),dict(id=contract_id,name=contract_name,currentRemarks='待处理',nextRemarks='已处理',expectedDigest=digest))
        call('business-rollback','writer','POST',business+'/rollback',{'id':contract_id,'expectedDigest':digest},domain_error=True);expected_contract['remarks']='待处理';expect_record(call('contract-detail-after-rollback','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'remarks':'待处理','amount':12.5})
        expect_record(call('business-complete','writer','POST',business+'/complete',{'id':contract_id,'expectedDigest':digest}),dict(id=contract_id,remarks='已处理',previousDigest=digest));expected_contract['remarks']='已处理';expect_record(call('contract-detail-complete','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'remarks':'已处理','amount':12.5})
        call('business-replay-conflict','writer','POST',business+'/complete',{'id':contract_id,'expectedDigest':digest},conflict=True);physical('sample_contract',contract_id,{'remarks':'已处理','amount':12.5})
        line=call('line-add','writer','POST',ROOTS['line']+'/add'+parent,{'sampleContractId':contract_id,'name':line_name,'amount':3.25,'remarks':line_remarks});line_id=record_id(line.get('id') if type(line) is dict else None);require(line_id not in {cid,copy_id,contract_id},'business-distinct-ids')
        expected_line=dict(id=line_id,name=line_name,sampleContractId=contract_id,sampleContractName=contract_name,amount=3.25,remarks=line_remarks);expect_record(call('line-detail-create','writer','GET',ROOTS['line']+'/detail/'+line_id+parent),expected_line);physical('sample_contract_line',line_id,{'name':line_name,'sample_contract_id':contract_id,'sample_contract_name':contract_name,'amount':3.25,'remarks':line_remarks})
        child_request={'pageNo':1,'pageSize':20,'filters':{},'parentId':contract_id};child=call('child-page','writer','POST',ROOTS['line']+'/page',child_request);require(type(child) is dict and type(child.get('total')) is int and child['total']==1 and type(child.get('records')) is list and len(child['records'])==1,'business-child-page');expect_record(child['records'][0],dict(id=line_id))
        call('customer-rename','writer','POST',ROOTS['customer']+'/update',{'id':cid,'name':final_name});expected_contract['sampleCustomerName']=final_name;expect_record(call('contract-detail-rename','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'sample_customer_id':cid,'sample_customer_name':final_name,'amount':12.5,'remarks':expected_contract['remarks'],'attachments':[]})
        call('contract-rename','writer','POST',ROOTS['contract']+'/update',{'id':contract_id,'name':contract_final});expected_line['sampleContractName']=contract_final;expect_record(call('line-detail-rename','writer','GET',ROOTS['line']+'/detail/'+line_id+parent),expected_line);physical('sample_contract_line',line_id,{'sample_contract_name':contract_final,'amount':3.25,'remarks':line_remarks})
        call('line-omit-update','writer','POST',ROOTS['line']+'/update'+parent,{'id':line_id,'amount':4.25});expected_line['amount']=4.25;expect_record(call('line-detail-omit','writer','GET',ROOTS['line']+'/detail/'+line_id+parent),expected_line);physical('sample_contract_line',line_id,{'sample_contract_name':contract_final,'amount':4.25,'remarks':line_remarks})
        call('line-delete','writer','DELETE',ROOTS['line']+parent,{'id':line_id});physical('sample_contract_line',line_id,{},True);count=call('child-count-after-delete','writer','POST',ROOTS['line']+'/count',child_request);require(type(count) is int and count==0,'business-child-delete-count')
        call('contract-delete','writer','DELETE',ROOTS['contract'],{'id':contract_id});physical('sample_contract',contract_id,{},True)
        call('customer-copy-delete','writer','DELETE',ROOTS['customer'],{'id':copy_id});physical('sample_customer',copy_id,{},True)
        call('customer-delete','writer','DELETE',ROOTS['customer'],{'id':cid});physical('sample_customer',cid,{},True);count=call('customer-count-after-delete','writer','POST',ROOTS['customer']+'/count',page_request);require(type(count) is int and count==0,'business-delete-count')
        for table,total in (('sample_customer',2),('sample_contract',1),('sample_contract_line',1)):
            require(query(f'SELECT COUNT(*),SUM(delete_no IS NULL) FROM `{database}`.`{table}`;')==str(total)+'\t0','business-final-physical-count')
        require(tuple(calls)==STEPS,'business-step-completeness');facts.append({'target':target,'steps':len(calls),'stringIds':{'customer':cid,'copy':copy_id,'contract':contract_id,'line':line_id},'originalCrudVerified':True,'omittedAndNullVerified':True,'relationsAndRenameVerified':True,'originalBusinessAndRollbackVerified':True,'readonlyAndDeniedVerified':True,'physicalRowsAndTombstonesVerified':True})
    return {'acceptedScope':'paired-original-crud-business-and-two-cached-relations-only','targets':facts,'sqlSemanticAccepted':False,'performanceAccepted':False,'releaseAccepted':False}
