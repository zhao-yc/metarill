"""普通业务执行器的原协议／失败终止／独立物理核验合同；HTTP和MySQL均为明确内存替身。"""
import copy
import hashlib
from decimal import Decimal
import re
import unittest
from unittest import mock
import paired_business_http as subject
import test_paired_native_bootstrap as fixtures


def lease():
    """虚构已批准描述只供内存回归，不能作为外部对象的用户授权。"""
    return {**fixtures.settings(),'purpose':'paired-observed-business-v1','leaseStatus':'running','images':dict(subject.resources.OBSERVATION_IMAGES)}


def tokens():
    """固定三个不同假令牌，不生成或建立真实会话。"""
    return {'java':{kind:str(n)*8+'-'+str(n)*4+'-4'+str(n)*3+'-8'+str(n)*3+'-'+str(n)*12 for n,kind in enumerate(('writer','reader','denied'),1)},'csharp':{kind:str(n)*64 for n,kind in enumerate(('writer','reader','denied'),1)}}


class Gateway:
    """用普通记录字典独立模拟原接口，不调用被测断言生成期望结果。"""
    paths={'/api/admin/auto/sample_customer/81001':'sample_customer','/api/admin/auto/sample_contract/81002':'sample_contract','/api/admin/auto/sample_contract_line/81003':'sample_contract_line'}
    def __init__(self,fault=None):
        self.rows={target:{table:{} for table in self.paths.values()} for target in ('java','csharp')};self.calls=[];self.sql=[];self.next=0;self.fault=fault;self.credentials=tokens()

    def request(self,target,step,method,path,body,token):
        """按真实方法和路径解析操作，保留省略／null，模拟缓存传播和逻辑删除。"""
        self.calls.append((target,step,method,path,copy.deepcopy(body)));kind=next(kind for kind,value in self.credentials[target].items() if value==token)
        if self.fault=='http-business-failure':return 200,{'code':500,'data':None,'msg':'PRIVATE-MESSAGE'}
        if kind=='denied' or kind=='reader' and method!='GET':return (200,{'code':500}) if target=='java' else (403,{'code':403})
        if path.startswith('/api/admin/custom/sample-contract-agent/'):
            row=self.rows[target]['sample_contract'][body['id']]
            fields=(row['id'],row['name'],format(Decimal(str(row['amount'])),'.2f'),row['remarks'])
            digest=hashlib.sha256(''.join(str(len(text.encode('utf-16-le'))//2)+':'+text for text in fields).encode()).hexdigest()
            if path.endswith('/preview'):
                data=dict(id=row['id'],name=row['name'],currentRemarks=row['remarks'],nextRemarks='已处理',expectedDigest=digest)
                if self.fault=='bad-preview':data['expectedDigest']='f'*64
                return 200,{'code':200,'data':data}
            if row['remarks']!='待处理' or body.get('expectedDigest')!=digest:
                if self.fault=='replayed-business':return 200,{'code':200,'data':None}
                return (200,{'code':500}) if target=='java' else (409,{'code':409})
            old=copy.deepcopy(row);row['remarks']='已处理'
            if path.endswith('/rollback'):
                if self.fault!='lost-rollback':row.clear();row.update(old)
                return 200,{'code':500}
            return 200,{'code':200,'data':{'id':row['id'],'remarks':'已处理','previousDigest':digest}}
        root=next(root for root in self.paths if path.startswith(root));table=self.paths[root];suffix=path[len(root):].split('?')[0];store=self.rows[target][table];data=None
        if suffix in ('/add',) or suffix.startswith('/copy/'):
            self.next+=1;identifier=str(9007199254750000+self.next+(100000 if target=='csharp' else 0))
            data=copy.deepcopy(body) if suffix=='/add' else {key:value for key,value in store[suffix.rsplit('/',1)[1]].items() if key not in ('id','deleted')}
            data.update(id=identifier,deleted=False);self.decorate(target,table,data);store[identifier]=copy.deepcopy(data)
        elif suffix=='/update':
            data=store[body['id']];data.update(copy.deepcopy(body))
            if self.fault=='clear-omitted' and 'remarks' not in body:data['remarks']=None
            if self.fault!='stale-cache':
                if table=='sample_customer':
                    for related in self.rows[target]['sample_contract'].values():
                        if related.get('sampleCustomerId')==data['id']:related['sampleCustomerName']=data['name']
                if table=='sample_contract':
                    for related in self.rows[target]['sample_contract_line'].values():
                        if related.get('sampleContractId')==data['id']:related['sampleContractName']=data['name']
        elif suffix.startswith('/detail/'):
            data=store[suffix.rsplit('/',1)[1]]
        elif suffix in ('/page','/count'):
            rows=[row for row in store.values() if not row['deleted']]
            if body.get('filters',{}).get('name'):rows=[row for row in rows if body['filters']['name'] in row['name']]
            if body.get('parentId'):rows=[row for row in rows if row.get('sampleContractId')==body['parentId']]
            if suffix=='/count':data=len(rows)
            else:
                rows=sorted(rows,key=lambda row:row.get('name',''));data={'total':len(rows),'records':copy.deepcopy(rows),'current':1,'size':20,'pages':1 if rows else 0}
                if self.fault=='reverse-page':data['records'].reverse()
                if self.fault=='bool-child-total' and table=='sample_contract_line':data['total']=True
        elif suffix=='' and method=='DELETE':
            if self.fault=='hard-delete':store.pop(body['id'])
            else:store[body['id']]['deleted']=True
            data='删除成功'
        else:raise AssertionError('未预期的原接口')
        data=copy.deepcopy(data)
        if self.fault=='numeric-id' and step=='customer-add':data['id']=int(data['id'])
        if self.fault=='duplicate-copy-id' and step=='customer-copy':data['id']=path.rsplit('/',1)[1]
        return 200,{'code':True if self.fault=='bool-r-code' else 200,'data':data,'map':{}}

    def decorate(self,target,table,row):
        """由已存在的真实关系键独立补充缓存字段，未复制被测期望字典。"""
        if table=='sample_contract':row['sampleCustomerName']=self.rows[target]['sample_customer'][row['sampleCustomerId']]['name']
        if table=='sample_contract_line':row['sampleContractName']=self.rows[target]['sample_contract'][row['sampleContractId']]['name']

    def query(self,sql):
        """仅接受固定SELECT形状并按内存状态核where；不执行／翻译数据库写入。"""
        self.sql.append(sql);self.assert_select(sql)
        if sql=='SELECT @@hostname,@@server_uuid;':return 'wrong-instance' if self.fault=='instance' else subject.PIN['hostname']+'\t'+subject.PIN['serverUuid']
        match=re.search(r'FROM `metarill_fresh_a{16}_paired_(java|csharp)`\.`(sample_customer|sample_contract|sample_contract_line)`',sql);assert match,sql
        target,table=match.groups();rows=list(self.rows[target][table].values())
        if sql.startswith('SELECT COUNT(*),SUM'):return str(len(rows))+'\t'+str(sum(not row['deleted'] for row in rows))
        if ' WHERE ' not in sql:return str(len(rows))
        if self.fault=='physical-mismatch':return '0'
        identifier=re.search(r'WHERE id=([0-9]+)',sql).group(1);rows=[row for row in rows if row['id']==identifier and row['deleted']==('delete_no IS NOT NULL' in sql)]
        aliases={'sample_customer_id':'sampleCustomerId','sample_customer_name':'sampleCustomerName','sample_contract_id':'sampleContractId','sample_contract_name':'sampleContractName'}
        for column,encoded in re.findall(r'`([a-z_]+)` <=> (_utf8mb4 0x[0-9a-f]+|[0-9]+(?:\.[0-9]+)?)',sql):
            value=bytes.fromhex(encoded.split('0x')[1]).decode() if encoded.startswith('_utf8mb4') else Decimal(encoded);key=aliases.get(column,column)
            rows=[row for row in rows if (Decimal(str(row[key]))==value if isinstance(value,Decimal) else row[key]==value)]
        for column in re.findall(r'`([a-z_]+)` IS NULL',sql):rows=[row for row in rows if row.get(column) is None]
        if 'JSON_LENGTH' in sql:rows=[row for row in rows if row.get('attachments') in ([],None,'')]
        return str(len(rows))

    @staticmethod
    def assert_select(sql):
        """测试自身不接受DDL/DML或多语句，防止误将验收工具当播种写入口。"""
        assert sql.startswith('SELECT ') and sql.count(';')==1 and sql.endswith(';')


class BusinessContractTests(unittest.TestCase):
    def test_two_targets_run_original_thirty_seven_steps_with_only_select_physical_audits(self):
        """原CRUD／关系传播及墓碑完整运行；未把替身回归标记为真实SQL或性能。"""
        gateway=Gateway();result=subject.run_basic(lease(),gateway.credentials,gateway.request,gateway.query)
        self.assertEqual(74,len(gateway.calls));self.assertEqual(8,len([call for call in gateway.calls if call[2]=='DELETE']))
        self.assertEqual(2,len(result['targets']));self.assertTrue(all(row['steps']==37 and row['physicalRowsAndTombstonesVerified'] for row in result['targets']))
        self.assertFalse(result['sqlSemanticAccepted']);self.assertFalse(result['performanceAccepted']);self.assertFalse(result['releaseAccepted'])
        self.assertTrue(all(row['deleted'] for target in gateway.rows.values() for table in target.values() for row in table.values()))
        self.assertTrue(all(sql.startswith('SELECT ') for sql in gateway.sql))

    def test_digest_matches_actual_original_java_private_method_vectors(self):
        """2026-11-03直接调用固定原Java私有方法取得预期，保留UTF16长度和scale2。"""
        vectors=[('9007199254740993', '合同甲', '待处理', '93df0e02f436d271839e9f1c0a178b7e5555172b30adc9d9ae59600566b329a1'), ('9223372036854775806', '名称😀\\"\n', '备注𠮷', '5802bed73a6591d010856b3c628de30f656fb7a08ca0f7dac7d1d192ac128e87'), ('9007199254740995', '', '', 'dc02715e9512e0711e83e9ee1b7a9a89c8106a1e860736bb6ab2a56bf82d1c2f')]
        for identifier,name,remarks,expected in vectors:
            self.assertEqual(expected,subject.preview_digest(identifier,name,remarks))

    def test_omitted_and_null_are_distinct_in_actual_outgoing_payloads(self):
        """请求字典本身检查省略与主动null，不只检查最终内存结果。"""
        gateway=Gateway();subject.run_basic(lease(),gateway.credentials,gateway.request,gateway.query)
        for target in ('java','csharp'):
            omitted=next(call[4] for call in gateway.calls if call[:2]==(target,'customer-omit-update'));cleared=next(call[4] for call in gateway.calls if call[:2]==(target,'customer-clear-update'));self.assertNotIn('remarks',omitted);self.assertIn('remarks',cleared);self.assertIsNone(cleared['remarks'])

    def test_pending_old_scope_foreign_database_or_images_reject_before_io(self):
        """独占范围或批准错误时，HTTP与SELECT均不得运行。"""
        for changes in ({'authorizationStatus':'pending'},{'leaseStatus':'ended'},{'purpose':'paired-native-v1'},{'version':True},{'databases':['metarill_dev']},{'images':dict(subject.resources.IMAGES)},{'user':'root'},{'instancePin':{}}):
            with self.subTest(changes=changes),self.assertRaises(ValueError):subject.run_basic({**lease(),**changes},tokens(),mock.Mock(side_effect=AssertionError('禁止HTTP')),mock.Mock(side_effect=AssertionError('禁止SQL')))

    def test_invalid_principal_tokens_reject_before_io(self):
        """缺少角色、重复或可注入头的令牌不进入请求器。"""
        for mode in ('missing','duplicate','newline'):
            values=tokens()
            if mode=='missing':values['java'].pop('reader')
            elif mode=='duplicate':values['csharp']['reader']=values['csharp']['writer']
            else:values['java']['writer']='bad\nheader'
            with self.subTest(mode=mode),self.assertRaises(ValueError):subject.run_basic(lease(),values,mock.Mock(side_effect=AssertionError('禁止HTTP')),mock.Mock(side_effect=AssertionError('禁止SQL')))

    def test_string_id_precision_is_not_a_float_conversion(self):
        """只接受实际高位长整数字符串，拒绝JS不安全数字及范围外文本。"""
        self.assertEqual('9007199254740993',subject.record_id('9007199254740993'))
        for value in (9007199254740993,9007199254740992.0,'9007199254740991','9223372036854775808','1;DROP TABLE x','01',None):
            with self.subTest(value=value),self.assertRaises(ValueError):subject.record_id(value)

    def test_wrong_real_instance_refuses_before_any_http(self):
        """实例pin由独立SELECT重新核对，不相信租约里的自报字段。"""
        gateway=Gateway('instance')
        with self.assertRaisesRegex(ValueError,'business-instance'):subject.run_basic(lease(),gateway.credentials,gateway.request,gateway.query)
        self.assertEqual([],gateway.calls);self.assertEqual(1,len(gateway.sql))

    def test_http_success_cannot_mask_a_physical_mismatch_and_never_retries(self):
        """新增HTTP成功但库值不匹配时立即停止，只发出一笔写请求。"""
        gateway=Gateway('physical-mismatch')
        with self.assertRaisesRegex(ValueError,'business-physical-value'):subject.run_basic(lease(),gateway.credentials,gateway.request,gateway.query)
        self.assertEqual(1,len(gateway.calls))

    def test_common_backend_regressions_are_rejected(self):
        """业务失败、数值ID、覆盖省略值、旧缓存、倒序、布尔计数及硬删均不能通过。"""
        for fault in ('http-business-failure','numeric-id','duplicate-copy-id','clear-omitted','stale-cache','reverse-page','bool-child-total','bool-r-code','hard-delete','bad-preview','lost-rollback','replayed-business'):
            gateway=Gateway(fault)
            with self.subTest(fault=fault),self.assertRaises(ValueError):subject.run_basic(lease(),gateway.credentials,gateway.request,gateway.query)
            self.assertLess(len(gateway.calls),74)


if __name__=='__main__':unittest.main()
