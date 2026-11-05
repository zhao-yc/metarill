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


# 原Java实际依赖枚举证明的八项差异；只用于新Unicode采集，不扩大原37业务步骤。
UNICODE_BLANK_CASES=(("0085",False),("00A0",False),("2007",False),("202F",False),("001C",True),("001D",True),("001E",True),("001F",True))


def unicode_read_counts(value,target,query):
    """仅在独占当前用途读取固定客户表；用独立MySQL表达式求基线，不调用待测生成筛选。"""
    require(type(value.get('version')) is int and value['version']==1 and value.get('purpose')==resources.UNICODE_OBSERVED_BUSINESS and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='running','business-unicode-lease')
    run=value.get('runId');require(type(run) is str and re.fullmatch('[a-f0-9]{32}',run),'business-unicode-run')
    names=['metarill_fresh_'+run[:16]+'_paired_'+side for side in ('java','csharp')]
    require(target in ('java','csharp') and value.get('databases')==names and value.get('user')=='mtf_'+run[:16] and value.get('images')==resources.UNICODE_OBSERVED_IMAGES and value.get('instancePin')==PIN,'business-unicode-scope')
    require(query('SELECT @@hostname,@@server_uuid;')==PIN['hostname']+'\t'+PIN['serverUuid'],'business-unicode-instance')
    database=names[('java','csharp').index(target)]
    base='SELECT COUNT(*) FROM `'+database+'`.`sample_customer` WHERE delete_no IS NULL'
    require(query(base+';')=='2','business-unicode-nonempty-fixture')
    result={}
    for code,blank in UNICODE_BLANK_CASES:
        if blank:result[code]=2;continue
        # 字符来自固定代码点，SQL只嵌固定UTF8十六进制，绝不接收调用方关键词或任意表达式。
        encoded=chr(int(code,16)).encode('utf-8').hex()
        count=query(base+" AND name LIKE CONCAT('%',CONVERT(UNHEX('"+encoded+"') USING utf8mb4),'%');")
        require(type(count) is str and re.fullmatch('0|[1-9][0-9]*',count),'business-unicode-baseline')
        result[code]=int(count)
    return result


# 原37步保持可重放；新验收将既有明细提到按钮前，并加入同父墓碑与其它父对照。
CONTROL_SETUP=('control-contract-add','control-line-add','tombstone-line-add','tombstone-line-delete')
CONTROL_CLEANUP=('control-line-delete','control-contract-delete')
BEFORE_BUTTONS=STEPS.index('business-preview')
BEFORE_LINES=STEPS.index('line-add')
BEFORE_RENAME=STEPS.index('customer-rename')
BEFORE_CUSTOMER_CLEANUP=STEPS.index('customer-copy-delete')
EXISTING_LINE_STEPS=(STEPS[:BEFORE_BUTTONS]+STEPS[BEFORE_LINES:BEFORE_RENAME]+CONTROL_SETUP+
                     STEPS[BEFORE_BUTTONS:BEFORE_LINES]+STEPS[BEFORE_RENAME:BEFORE_CUSTOMER_CLEANUP]+
                     CONTROL_CLEANUP+STEPS[BEFORE_CUSTOMER_CLEANUP:])
OLD_APP_NAME='旧App用户名'
STALE_CACHE='验收过期缓存'
TOMBSTONE_CACHE='保留的墓碑缓存'
OTHER_CACHE='保留的其它合同缓存'


def require(condition,code):
    """仅返回固定错误码，不泄漏响应正文、令牌、SQL或私有路径。"""
    if not condition:raise ValueError(code)


def record_id(value):
    """实际生成ID必须是高于JS安全整数范围的十进制字符串，禁止浮点或数字冒充。"""
    require(type(value) is str and re.fullmatch('[1-9][0-9]{0,18}',value) and 9007199254740991<int(value)<=2**63-1,'business-string-id')
    return value


def authorize(value,tokens,*,existing_lines=False):
    """所有I/O前核用途、直接批准、唯一双库、固定新镜像及三普通登录令牌。"""
    run=value.get('runId');require(type(run) is str and re.fullmatch('[a-f0-9]{32}',run),'business-run')
    databases=['metarill_fresh_'+run[:16]+'_paired_'+target for target in ('java','csharp')]
    purpose=resources.EXISTING_LINE_BUSINESS if existing_lines else resources.UNICODE_OBSERVED_BUSINESS if resources.is_unicode_observed(value) else resources.PATTERN_OBSERVED_BUSINESS if resources.is_pattern_observed(value) else resources.FULL_OBSERVED_BUSINESS if resources.is_full_observed(value) else resources.CURRENT_OBSERVED_BUSINESS if resources.is_current_observed(value) else 'paired-observed-business-v1'
    if resources.is_extended_business(value) and not existing_lines: purpose=resources.IMPORT_RULE_BUSINESS if resources.is_import_rule_business(value) else resources.DEEP_EXCEL_BUSINESS if resources.is_deep_excel_import(value) else resources.FILE_AUTH_BUSINESS if resources.is_file_auth_business(value) else resources.EXTENDED_BUSINESS
    images=resources.EXISTING_LINE_IMAGES if existing_lines else resources.images_for(value) if resources.is_full_observed(value) else resources.CURRENT_MEASUREMENT_IMAGES if resources.is_current_observed(value) else resources.OBSERVATION_IMAGES
    if resources.is_extended_business(value) and not existing_lines: images=resources.images_for(value)
    require(value.get('version')==1 and type(value.get('version')) is int and value.get('purpose')==purpose and value.get('authorizationStatus')=='approved' and value.get('leaseStatus')=='running','business-lease')
    require(value.get('databases')==databases and value.get('user')=='mtf_'+run[:16] and value.get('instancePin')==PIN and images is not None and value.get('images')==images,'business-scope')
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


def expect_child_record(data,expected):
    """原81003列表/详情只开放四字段；父键与缓存仍由独立物理读取逐项核验。"""
    expect_record(data,expected);require(set(data)<=set(("id","name","amount","remarks")),"business-child-projection")
    return data


def preview_digest(identifier,name,remarks):
    """独立按原Java/C#算法和发布金额scale=2计算，不用服务端最新摘要替代原值。"""
    values=(record_id(identifier),name,'12.50',remarks)
    source=''.join(str(len(text.encode('utf-16-le'))//2)+':'+text for text in values)
    return hashlib.sha256(source.encode('utf-8')).hexdigest()


def _prepare_stale_lines(database,contract_id,lines,fixture_write):
    """仅为本轮合成行构造限定原值、父键及墓碑状态的夹具写入。"""
    record_id(contract_id)
    require(re.fullmatch(r'metarill_fresh_[a-f0-9]{16}_paired_(java|csharp)',database),'business-fixture-database')
    parent=f"UPDATE `{database}`.`sample_contract` SET `update_user_name`={seed.literal(OLD_APP_NAME)} WHERE id={contract_id} AND delete_no IS NULL AND `update_user_name` IS NULL;"
    statements=[parent]
    for identifier,owner,name,stale,deleted in lines:
        record_id(identifier);record_id(owner)
        status='NOT NULL' if deleted else 'NULL'
        statements.append(f"UPDATE `{database}`.`sample_contract_line` SET `sample_contract_name`={seed.literal(stale)} WHERE id={identifier} AND BINARY `sample_contract_id`=BINARY {seed.literal(owner)} AND delete_no IS {status} AND `sample_contract_name` <=> {seed.literal(name)};")
    # 只有调用方已批准的夹具事务可以执行；缺行时应回滚，禁止业务继续。
    counts=fixture_write(*statements)
    require(type(counts) is tuple and len(counts)==len(statements) and all(type(count) is int and count==1 for count in counts),'business-fixture-match')


def mysql_assertion_sql(condition):
    """守护客户端遇非法 PREPARE 即断开，尚未提交的写入随连接回滚。"""
    return [
        f"SET @mt_fixture_guard=IF({condition}, 'SELECT 1', 'SELECT FROM');",
        "PREPARE mt_fixture_gate FROM @mt_fixture_guard;",
        "EXECUTE mt_fixture_gate;",
        "DEALLOCATE PREPARE mt_fixture_gate;",
    ]


def fixture_transaction_sql(run,statements):
    """只接受该次刚生成的四条固定写入；返回失败会断开且无法提交的批次。"""
    require(type(run) is str and re.fullmatch('[a-f0-9]{32}',run),'business-fixture-run')
    require(type(statements) is tuple and len(statements)==4 and all(type(sql) is str for sql in statements),'business-fixture-shape')
    parent=re.fullmatch(r'UPDATE `(metarill_fresh_'+run[:16]+r'_paired_(?:java|csharp))`\.`sample_contract` SET `update_user_name`='+re.escape(seed.literal(OLD_APP_NAME))+r' WHERE id=([0-9]+) AND delete_no IS NULL AND `update_user_name` IS NULL;',statements[0])
    require(parent is not None,'business-fixture-parent')
    database,parent_id=parent.groups()
    ids={record_id(parent_id)}
    for offset,(statement,stale,deleted) in enumerate(zip(statements[1:],(STALE_CACHE,TOMBSTONE_CACHE,OTHER_CACHE),('NULL','NOT NULL','NULL'))):
        name='配对合同-'+run[:16]+'-01'+('-对照' if offset==2 else '')
        shape=(r'UPDATE `'+database+r'`\.`sample_contract_line` SET `sample_contract_name`='+re.escape(seed.literal(stale))
            +r' WHERE id=([0-9]+) AND BINARY `sample_contract_id`=BINARY _utf8mb4 0x([0-9a-f]{32,38}) AND delete_no IS '+deleted
            +r' AND `sample_contract_name` <=> '+re.escape(seed.literal(name))+';')
        match=re.fullmatch(shape,statement)
        require(match is not None,'business-fixture-line')
        identifier=record_id(match[1])
        owner=bytes.fromhex(match[2])
        require(owner.isdigit(),'business-fixture-line-owner')
        owner=record_id(owner.decode('ascii'))
        require(identifier not in ids and (owner==parent_id)==(offset!=2),'business-fixture-line-scope')
        ids.add(identifier)
    require(owner not in ids,'business-fixture-line-scope')

    scope=(f"@@hostname={seed.literal(PIN['hostname'])} AND @@server_uuid={seed.literal(PIN['serverUuid'])} AND "
           "(SELECT COUNT(*) FROM information_schema.TABLES WHERE "
           f"TABLE_SCHEMA={seed.literal(database)} AND TABLE_NAME IN ('sample_contract','sample_contract_line') AND ENGINE='InnoDB')=2")
    batch=['START TRANSACTION;',*mysql_assertion_sql(scope)]
    counters=[]
    for number,statement in enumerate(statements):
        counter='@mt_fixture_count_'+str(number)
        batch.extend((statement,f'SET {counter}=ROW_COUNT();'))
        counters.append(counter)
    batch.extend(mysql_assertion_sql(' AND '.join(counter+'=1' for counter in counters)))
    batch.extend(('COMMIT;','SELECT '+','.join(counters)+';'))
    return '\n'.join(batch)+'\n'


def run_basic(value,tokens,request,query,*,fixture_write=None):
    """普通执行沿原37步；显式注入独占夹具写入器才运行已有明细对照用例。"""
    existing_lines=fixture_write is not None
    steps=EXISTING_LINE_STEPS if existing_lines else STEPS
    run,databases=authorize(value,tokens,existing_lines=existing_lines)
    require(query('SELECT @@hostname,@@server_uuid;')==PIN['hostname']+'\t'+PIN['serverUuid'],'business-instance')
    for database in databases.values():
        for table in FIELDS:require(query(f'SELECT COUNT(*) FROM `{database}`.`{table}`;')=='0','business-not-empty')
    facts=[]
    for target,database in databases.items():
        calls=[];prefix='配对客户-'+run[:16];name=prefix+'-old';renamed=prefix+'-01';copy_name=prefix+'-02';final_name=prefix+'-11';remarks="中文'引号\\与换行\n原备注";copy_remarks='复制备注';contract_name='配对合同-'+run[:16]+'-01';contract_final='配对合同-'+run[:16]+'-11';line_name='配对明细-'+run[:16];line_remarks='明细备注'
        def call(step,kind,method,path,body=None,denied=False,domain_error=False,conflict=False):
            """固定步骤一次发出；原R和HTTP须同时符合，失败不自动重试或继续写入。"""
            require(len(calls)<len(steps) and steps[len(calls)]==step,'business-step-order');calls.append(step)
            status,response=request(target,step,method,path,body,tokens[target][kind]);require(type(response) is dict and type(response.get('code')) is int,'business-response')
            wanted=(200,500) if domain_error or (denied or conflict) and target=='java' else (409,409) if conflict else (403,403) if denied else (200,200)
            require(type(status) is int and (status,response['code'])==wanted,'business-http-result')
            return response.get('data')
        def physical(table,identifier,fields,deleted=False):
            """全是本轮三个固定表和已验证ID，值用UTF8字面量；不提供任意列或SQL写入。"""
            record_id(identifier);require(table in FIELDS,'business-physical-fields')
            audit={'update_user_name','update_manager_real_name'} if existing_lines and table=='sample_contract' else set()
            require(set(fields)<=FIELDS[table]|audit,'business-physical-fields')
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
        def add_and_verify_line():
            """保持原子页四字段回读，物理查询独立核对父键和缓存。"""
            line=call('line-add','writer','POST',ROOTS['line']+'/add'+parent,{'sampleContractId':contract_id,'name':line_name,'amount':3.25,'remarks':line_remarks});line_id=record_id(line.get('id') if type(line) is dict else None);require(line_id not in {cid,copy_id,contract_id},'business-distinct-ids')
            expected_line=dict(id=line_id,name=line_name,amount=3.25,remarks=line_remarks);expect_child_record(call('line-detail-create','writer','GET',ROOTS['line']+'/detail/'+line_id+parent),expected_line);physical('sample_contract_line',line_id,{'name':line_name,'sample_contract_id':contract_id,'sample_contract_name':contract_name,'amount':3.25,'remarks':line_remarks})
            child_request={'pageNo':1,'pageSize':20,'filters':{},'parentId':contract_id};child=call('child-page','writer','POST',ROOTS['line']+'/page',child_request);require(type(child) is dict and type(child.get('total')) is int and child['total']==1 and type(child.get('records')) is list and len(child['records'])==1,'business-child-page');expect_child_record(child['records'][0],expected_line)
            return line_id,expected_line,child_request
        audit={}
        if existing_lines:
            audit={'update_manager_real_name':'性能writer','update_user_name':None}
            physical('sample_contract',contract_id,audit)
            line_id,expected_line,child_request=add_and_verify_line()
            control_name=contract_name+'-对照'
            other=call('control-contract-add','writer','POST',ROOTS['contract']+'/add',{'name':control_name,'sampleCustomerId':cid,'amount':1.25,'attachments':[]})
            other_id=record_id(other.get('id') if type(other) is dict else None)
            require(other_id not in {cid,copy_id,contract_id,line_id},'business-distinct-ids')
            other_parent='?parentId='+other_id
            other_line=call('control-line-add','writer','POST',ROOTS['line']+'/add'+other_parent,{'sampleContractId':other_id,'name':'其它合同明细','amount':2.5})
            other_line_id=record_id(other_line.get('id') if type(other_line) is dict else None)
            tombstone=call('tombstone-line-add','writer','POST',ROOTS['line']+'/add'+parent,{'sampleContractId':contract_id,'name':'已删除的同父明细','amount':1.5})
            tombstone_id=record_id(tombstone.get('id') if type(tombstone) is dict else None)
            require(len({cid,copy_id,contract_id,line_id,other_id,other_line_id,tombstone_id})==7,'business-distinct-ids')
            call('tombstone-line-delete','writer','DELETE',ROOTS['line']+parent,{'id':tombstone_id})
            _prepare_stale_lines(database,contract_id,(
                (line_id,contract_id,contract_name,STALE_CACHE,False),
                (tombstone_id,contract_id,contract_name,TOMBSTONE_CACHE,True),
                (other_line_id,other_id,control_name,OTHER_CACHE,False),
            ),fixture_write)
            audit['update_user_name']=OLD_APP_NAME

        def existing_line_cache(expected):
            """核对活动匹配行、同父墓碑、其它父活动行及旧 App 审计列。"""
            if not existing_lines:return
            physical('sample_contract',contract_id,audit)
            physical('sample_contract_line',line_id,{'sample_contract_id':contract_id,'sample_contract_name':expected})
            physical('sample_contract_line',tombstone_id,{'sample_contract_id':contract_id,'sample_contract_name':TOMBSTONE_CACHE},True)
            physical('sample_contract_line',other_line_id,{'sample_contract_id':other_id,'sample_contract_name':OTHER_CACHE})

        if existing_lines:existing_line_cache(STALE_CACHE)
        digest=preview_digest(contract_id,contract_name,'待处理');business='/api/admin/custom/sample-contract-agent'
        expect_record(call('business-preview','writer','POST',business+'/preview',{'id':contract_id}),dict(id=contract_id,name=contract_name,currentRemarks='待处理',nextRemarks='已处理',expectedDigest=digest))
        call('business-rollback','writer','POST',business+'/rollback',{'id':contract_id,'expectedDigest':digest},domain_error=True);expected_contract['remarks']='待处理';expect_record(call('contract-detail-after-rollback','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'remarks':'待处理','amount':12.5})
        existing_line_cache(STALE_CACHE)
        expect_record(call('business-complete','writer','POST',business+'/complete',{'id':contract_id,'expectedDigest':digest}),dict(id=contract_id,remarks='已处理',previousDigest=digest));expected_contract['remarks']='已处理';expect_record(call('contract-detail-complete','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'remarks':'已处理','amount':12.5})
        existing_line_cache(contract_name)
        call('business-replay-conflict','writer','POST',business+'/complete',{'id':contract_id,'expectedDigest':digest},conflict=True);physical('sample_contract',contract_id,{'remarks':'已处理','amount':12.5})
        existing_line_cache(contract_name)
        if not existing_lines:line_id,expected_line,child_request=add_and_verify_line()
        call('customer-rename','writer','POST',ROOTS['customer']+'/update',{'id':cid,'name':final_name});expected_contract['sampleCustomerName']=final_name;expect_record(call('contract-detail-rename','writer','GET',ROOTS['contract']+'/detail/'+contract_id),expected_contract);physical('sample_contract',contract_id,{'sample_customer_id':cid,'sample_customer_name':final_name,'amount':12.5,'remarks':expected_contract['remarks'],'attachments':[]})
        call('contract-rename','writer','POST',ROOTS['contract']+'/update',{'id':contract_id,'name':contract_final});expect_child_record(call('line-detail-rename','writer','GET',ROOTS['line']+'/detail/'+line_id+parent),expected_line);physical('sample_contract_line',line_id,{'sample_contract_id':contract_id,'sample_contract_name':contract_final,'amount':3.25,'remarks':line_remarks})
        existing_line_cache(contract_final)
        call('line-omit-update','writer','POST',ROOTS['line']+'/update'+parent,{'id':line_id,'amount':4.25});expected_line['amount']=4.25;expect_child_record(call('line-detail-omit','writer','GET',ROOTS['line']+'/detail/'+line_id+parent),expected_line);physical('sample_contract_line',line_id,{'sample_contract_id':contract_id,'sample_contract_name':contract_final,'amount':4.25,'remarks':line_remarks})
        call('line-delete','writer','DELETE',ROOTS['line']+parent,{'id':line_id});physical('sample_contract_line',line_id,{},True);count=call('child-count-after-delete','writer','POST',ROOTS['line']+'/count',child_request);require(type(count) is int and count==0,'business-child-delete-count')
        call('contract-delete','writer','DELETE',ROOTS['contract'],{'id':contract_id});physical('sample_contract',contract_id,{},True)
        if existing_lines:
            call('control-line-delete','writer','DELETE',ROOTS['line']+other_parent,{'id':other_line_id});physical('sample_contract_line',other_line_id,{},True)
            call('control-contract-delete','writer','DELETE',ROOTS['contract'],{'id':other_id});physical('sample_contract',other_id,{},True)
        call('customer-copy-delete','writer','DELETE',ROOTS['customer'],{'id':copy_id});physical('sample_customer',copy_id,{},True)
        call('customer-delete','writer','DELETE',ROOTS['customer'],{'id':cid});physical('sample_customer',cid,{},True);count=call('customer-count-after-delete','writer','POST',ROOTS['customer']+'/count',page_request);require(type(count) is int and count==0,'business-delete-count')
        for table,total in (('sample_customer',2),('sample_contract',2 if existing_lines else 1),('sample_contract_line',3 if existing_lines else 1)):
            require(query(f'SELECT COUNT(*),SUM(delete_no IS NULL) FROM `{database}`.`{table}`;')==str(total)+'\t0','business-final-physical-count')
        require(tuple(calls)==steps,'business-step-completeness');facts.append({'target':target,'steps':len(calls),'stringIds':{'customer':cid,'copy':copy_id,'contract':contract_id,'line':line_id},'originalCrudVerified':True,'omittedAndNullVerified':True,'relationsAndRenameVerified':True,'originalBusinessAndRollbackVerified':True,'readonlyAndDeniedVerified':True,'physicalRowsAndTombstonesVerified':True})
        if existing_lines:facts[-1].update(existingLineValuesAfterCommitAndRollbackVerified=True,nonMatchingAndDeletedLineUnchanged=True,managerAuditAndLegacyAppValueVerified=True)
    return {'acceptedScope':'paired-existing-line-cache-audit-and-original-crud' if existing_lines else 'paired-original-crud-business-and-two-cached-relations-only','targets':facts,'sqlSemanticAccepted':False,'performanceAccepted':False,'releaseAccepted':False}
