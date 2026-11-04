"""追加验证C#非空附件Excel导入、原子页再导入和末行业务规则失败；不作为性能成绩。"""
from __future__ import annotations
import hashlib
import json
import paired_business_http as basic
import paired_identity_seed as seed
import server200_paired_resources as resources

ROOT='/api/admin/sys/file-library'
HEADERS=['合同名称','客户','合同金额','备注','合同附件']


def require(ok,code):
    """只输出固定断言位置，不回显文件、令牌、SQL或导入内容。"""
    if not ok:raise ValueError('business-deep-excel-'+code)


def run_deep_import(value,tokens,exchange,query):
    """只在新用途执行；原两端144请求及十文件结束后，复用真实子页工作簿再增加两份合同工作簿。"""
    require(value.get('purpose')==resources.DEEP_EXCEL_BUSINESS and type(value.get('workbookOperations')) is int and value['workbookOperations']==12,'scope')
    run,databases=basic.authorize(value,tokens);database=databases['csharp'];target='csharp';writer=tokens[target]['writer']
    require(query('SELECT @@hostname,@@server_uuid;')==basic.PIN['hostname']+'\t'+basic.PIN['serverUuid'],'instance')
    from paired_extended_business import FILE_BYTES
    prefix='深度-'+run[:16];customer_name=prefix+'-客户';contracts=[prefix+'-合同-'+str(i) for i in (1,2)];failed=[prefix+'-回滚-'+str(i) for i in (1,2)]
    observations={'runId':run,'target':target,'checks':[],'javaDeepImportExecuted':False,'performanceAccepted':False}

    def save():
        """每个物理读回立即留固定标签／结果；后续HTTP分类失败不能抹去已验证回滚。"""
        resources.write_state(exchange.folder/'deep-excel-observation.json',observations)

    def check(label,actual,expected):
        """结果留在私有合成证据中，查询本身固定且不提供自由SQL或写入口。"""
        observations['checks'].append(dict(label=label,actual=actual,expected=expected,passed=actual==expected));save()
        require(actual==expected,label)

    def call(step,method,path,body=None):
        """普通成功请求必须同时满足原HTTP与R，且一次发出不重试。"""
        status,response=exchange.request(target,'deep-'+step,method,path,body,writer)
        require(type(response) is dict and (status,response.get('code'))==(200,200),'http-'+step)
        return response.get('data')

    def count_names(table,names,active=False):
        """只读三个固定样板表，名称全部来自本轮前缀或已解析原工作簿。"""
        require(table in basic.FIELDS and type(names) is list and all(type(name) is str for name in names),'count-shape')
        where='name IN ('+','.join(seed.literal(name) for name in names)+')'+(' AND delete_no IS NULL' if active else '')
        return query(f'SELECT COUNT(*) FROM `{database}`.`{table}` WHERE '+where+';')

    def references(file_id,active=False):
        """总引用和活动引用分别检查，不能以删除墓碑掩盖失败事务留下的引用。"""
        return query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_ref` WHERE file_id={basic.record_id(file_id)}'+(' AND delete_no IS NULL' if active else '')+';')

    def contract_rows(names,amounts,file_id):
        """始终由原模块实际写为XLSX并重新解析后再提交，不直接伪造导入行。"""
        return [[name,customer_name,amount,'非空附件导入',json.dumps([file_id],separators=(',',':'))] for name,amount in zip(names,amounts)]

    customer=call('customer-add','POST',basic.ROOTS['customer']+'/add',dict(name=customer_name,remarks='非空附件与父页导入'))
    customer_id=basic.record_id(customer.get('id') if type(customer) is dict else None)
    plan=call('file-plan','POST',ROOT+'/upload-plan',dict(originalName='metarill-deep.txt',mimeType='text/plain',sizeBytes=len(FILE_BYTES),configColumnId='82007',folderId=None))
    require(type(plan) is dict and type(plan.get('plan')) is dict,'file-plan');file_id=basic.record_id(plan.get('fileId'))
    require(plan['plan'].get('uploadMode')=='SERVER_MULTIPART' and plan['plan'].get('uploadUrl')=='/admin/sys/file-library/local-upload/'+file_id,'file-url')
    status,response=exchange.upload(target,'deep-file-upload',file_id,writer);require(status==200 and response.get('code')==200,'upload')
    call('file-complete','POST',ROOT+'/complete',dict(fileId=file_id));check('initial-file-refs',references(file_id),'0')
    good=exchange.workbook(target,'deep-create-file-contracts',dict(version=1,operation='create',headers=HEADERS,rows=contract_rows(contracts,[12.5,3.25],file_id)))
    call('file-contract-import','POST',basic.ROOTS['contract']+'/importDatas81002',dict(requestJson=good['rows']))
    check('import-contract-rows',count_names('sample_contract',contracts,True),'2')
    raw=query(f'SELECT CAST(id AS CHAR) FROM `{database}`.`sample_contract` WHERE delete_no IS NULL AND name IN ('+','.join(seed.literal(name) for name in contracts)+') ORDER BY name;').splitlines()
    require(len(raw)==2 and len(set(raw))==2,'contract-ids');ids=[basic.record_id(x) for x in raw]
    for index,identifier in enumerate(ids):
        condition=f'id={identifier} AND delete_no IS NULL AND BINARY name=BINARY {seed.literal(contracts[index])} AND BINARY sample_customer_id=BINARY {seed.literal(customer_id)} AND BINARY sample_customer_name=BINARY {seed.literal(customer_name)} AND amount={(12.5,3.25)[index]} AND JSON_LENGTH(attachments)=1 AND JSON_UNQUOTE(JSON_EXTRACT(attachments,\'$[0]\'))={seed.literal(file_id)}'
        check('import-contract-physical-'+str(index),query(f'SELECT COUNT(*) FROM `{database}`.`sample_contract` WHERE '+condition+';'),'1')
    check('import-two-active-refs',references(file_id,True),'2');check('import-two-total-refs',references(file_id),'2')
    check('import-ref-real-records',query(f'SELECT COUNT(*) FROM `{database}`.`yz_file_ref` WHERE file_id={file_id} AND delete_no IS NULL AND page_id=81002 AND field_name=\'attachments\' AND record_id IN ('+','.join(ids)+');'),'2')
    observations['nonemptyExcelImportFileCellsVerified']=True;observations['fileId']=file_id;observations['contractIds']=ids;save()
    # 使用本轮已经下载并由原SheetJS解码的子页导出，不增加伪造JSON或无来源的行。
    child=exchange.reused_child_export();require(child['headers']==['明细名称','明细金额','备注'] and len(child['rows'])==1,'child-workbook')
    row=child['rows'][0];require(set(row)==set(child['headers']) and type(row['明细名称']) is str,'child-row')
    check('child-before-active',count_names('sample_contract_line',[row['明细名称']],True),'0')
    call('child-import','POST',basic.ROOTS['line']+'/importDatas81003',dict(requestJson=child['rows'],parentId=ids[0]))
    line=query(f'SELECT CAST(id AS CHAR) FROM `{database}`.`sample_contract_line` WHERE delete_no IS NULL AND BINARY name=BINARY {seed.literal(row["明细名称"])};');line_id=basic.record_id(line)
    condition=f'id={line_id} AND delete_no IS NULL AND BINARY sample_contract_id=BINARY {seed.literal(ids[0])} AND BINARY sample_contract_name=BINARY {seed.literal(contracts[0])} AND amount=1.25 AND BINARY remarks=BINARY {seed.literal(row["备注"])}'
    check('child-physical-parent-and-cache',query(f'SELECT COUNT(*) FROM `{database}`.`sample_contract_line` WHERE '+condition+';'),'1')
    page=call('child-parent-page','POST',basic.ROOTS['line']+'/page',dict(pageNo=1,pageSize=20,filters={},parentId=ids[0]));require(type(page) is dict and page.get('total')==1 and len(page.get('records',[]))==1,'child-page');basic.expect_child_record(page['records'][0],dict(id=line_id,name=row['明细名称'],amount=1.25,remarks=row['备注']))
    observations['childExcelImportVerified']=True;observations['childWorkbookSha256']=child['sha256'];observations['childId']=line_id;save()
    check('rollback-before-rows',count_names('sample_contract',failed),'0')
    bad=exchange.workbook(target,'deep-create-late-rule-failure',dict(version=1,operation='create',headers=HEADERS,rows=contract_rows(failed,[12.5,-1],file_id)))
    status,response=exchange.request(target,'deep-import-late-rule-failure','POST',basic.ROOTS['contract']+'/importDatas81002',dict(requestJson=bad['rows']),writer)
    require(type(response) is dict,'rule-response')
    observations['lateRuleResponse']=dict(status=status,code=response.get('code'),message=response.get('msg'));save()
    # 两个物理记录及引用总数都须回到请求前；不能只查询未删除行或把异常响应当回滚。
    check('rollback-after-rows-including-tombstones',count_names('sample_contract',failed),'0')
    check('rollback-file-total-refs',references(file_id),'2');check('rollback-file-active-refs',references(file_id,True),'2')
    check('rollback-keeps-good-contracts',count_names('sample_contract',contracts,True),'2')
    observations['lateRowBusinessRuleRollsBackRecordsAndFileRefs']=True;save()
    # 明确业务规则仍须原HTTP200/R500；任意内部500不能被当作正确业务拒绝。
    require((status,response.get('code'))==(200,500),'rule-http-result')
    require('合同金额不能小于零' in str(response.get('msg','')),'rule-message')
    call('child-delete','DELETE',basic.ROOTS['line']+'?parentId='+ids[0],dict(id=line_id))
    for index,identifier in enumerate(ids):call('contract-delete-'+str(index),'DELETE',basic.ROOTS['contract'],dict(id=identifier))
    check('delete-clears-active-file-refs',references(file_id,True),'0');call('file-delete','DELETE',ROOT+'/'+file_id);call('customer-delete','DELETE',basic.ROOTS['customer'],dict(id=customer_id))
    for table,total in [('sample_customer',5),('sample_contract',5),('sample_contract_line',4)]:check('final-'+table,query(f'SELECT COUNT(*),SUM(delete_no IS NULL) FROM `{database}`.`{table}`;'),str(total)+'\t0')
    observations.update(completed=True,customerId=customer_id,sourceWorkbookOperationsAdded=2,reusedOriginalChildExport=True,originalBusinessRuleResponseVerified=True,releaseAccepted=False);save();return observations
