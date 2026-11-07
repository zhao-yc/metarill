"""固定读取SQL审阅器的语义反例；不连接数据库或把替身当真实采集。"""
import copy
import hashlib
import hmac
import unittest
import paired_read_sql_review as review

KEY=bytes(range(32))
ID='905910565396811776'


def proof(kind,value):
    """仅测试用的固定假key，与任何真实审阅秘密无关。"""
    return hmac.digest(KEY,(kind+'\0'+value).encode(),'sha256').hex()


def command(side,sql,values):
    """按真实JDBC位置及Connector名称构造最小合成输入，实际证据另由归档器校验完整形状。"""
    params=[dict(position=i+1,kind=kind,valueHash=proof(kind,value)) if side=='java' else dict(name=name,valueKind=kind,valueHash=proof(kind,value)) for i,(name,kind,value) in enumerate(values)]
    return dict(sql=sql,parameters=params,succeeded=True,operation='execute' if side=='java' else 'reader')


def verify(c,side,**changes):
    """共同预期来自固定客户详情合同，反例仅改变待验证的一项。"""
    expected=dict(table='sample_customer',kind='rows',fields=['id','name','remarks'],predicates=[('delete_no','is-null',None,set()),('id','=',ID,{'string','integer'})],order=[('id','desc')] if side=='java' else [],limit=None if side=='java' else (0,1))
    return review.verify_read(c,side,KEY,**{**expected,**changes})


class ReadSqlReviewTests(unittest.TestCase):
    def test_value_proof_uses_actual_nul_separator_and_read_driver_operation(self):
        """固定字节向量防止双方同错；字符串反斜线零不能冒充协议NUL分隔符。"""
        digest=hmac.digest(KEY,b'string'+bytes([0])+ID.encode(),'sha256').hex()
        review.proves(dict(kind='string',valueHash=digest),KEY,ID,{'string'})
        c=command('java','SELECT id,name,remarks FROM sample_customer WHERE delete_no IS NULL AND id=? ORDER BY id DESC',[('','string',ID)])
        with self.assertRaises(review.ReadReviewError):verify({**c,'operation':'executeUpdate'},'java')

    def test_jdbc_and_connector_details_match_same_input_without_changing_original_syntax(self):
        """原Java无LIMIT的主键详情与C#LIMIT1允许不同形状，字段／删除范围／ID证明相同。"""
        java=command('java','SELECT name,remarks,id FROM sample_customer WHERE delete_no IS NULL AND (id = ?) ORDER BY id DESC',[('', 'string',ID)])
        csharp=command('csharp','SELECT a.`id` as1,a.`name` as2,a.`remarks` as3 FROM `sample_customer` a WHERE (a.`delete_no` IS NULL) AND (a.`id`=@record_id) LIMIT 0,1',[('record_id','integer',ID)])
        for side,c in [('java',java),('csharp',csharp)]:self.assertEqual(1,verify(c,side)['parameterValuesVerified'])

    def test_count_and_sum_require_same_fixed_scope_as_rows(self):
        """COUNT与SUM不能漏主子父范围；只消除固定1=1，不忽略额外谓词。"""
        predicates=[('delete_no','is-null',None,set()),('sample_contract_id','=',ID,{'string'})]
        for side,select,condition,params,order in [('java','COALESCE(SUM(`amount`),0)','1=1 AND delete_no IS NULL AND ((sample_contract_id = ?))',[('','string',ID)],[]),('csharp','sum(a.`amount`) as1','(a.`delete_no` IS NULL) AND (a.`sample_contract_id`=@parent)',[('parent','string',ID)],[])]:
            c=command(side,'SELECT '+select+' FROM sample_contract_line'+(' a' if side=='csharp' else '')+' WHERE '+condition,params)
            self.assertTrue(review.verify_read(c,side,KEY,table='sample_contract_line',kind='sum',fields=['amount'],predicates=predicates,order=order,limit=None)['readOnly'])
            bad=copy.deepcopy(c);bad['sql']=bad['sql'].replace('sample_contract_id','wrong_parent')
            with self.assertRaises(review.ReadReviewError):review.verify_read(bad,side,KEY,table='sample_contract_line',kind='sum',fields=['amount'],predicates=predicates,order=order,limit=None)

    def test_like_and_jdbc_limit_values_are_independently_proved(self):
        """LIKE及分页大小均重算HMAC；不能仅看问号数量或常量LIMIT。"""
        c=command('java','SELECT id,name,remarks FROM sample_customer WHERE delete_no IS NULL AND (name LIKE ?) ORDER BY name ASC,id DESC LIMIT ?',[('','string','%固定客户%'),('','integer','20')])
        expected=dict(predicates=[('delete_no','is-null',None,set()),('name','like','%固定客户%',{'string'})],order=[('name','asc'),('id','desc')],limit=(0,20))
        self.assertEqual(2,verify(c,'java',**expected)['parameterValuesVerified'])
        for change in [dict(limit=(0,200)),dict(limit=(1,20)),dict(predicates=[('delete_no','is-null',None,set()),('name','like','%其他客户%',{'string'})])]:
            with self.assertRaises(review.ReadReviewError):verify(c,'java',**{**expected,**change})

    def test_hidden_projection_other_table_or_unexpected_lock_is_rejected(self):
        """不能把可执行SELECT当授权证明；投影、表和锁用途都必须与输入合同一致。"""
        sql='SELECT id,name,remarks FROM sample_customer WHERE delete_no IS NULL AND id=? ORDER BY id DESC';c=command('java',sql,[('','string',ID)])
        for bad_sql in [sql.replace('id,name,remarks','id,name,remarks,password_hash'),sql.replace('sample_customer','sample_contract'),sql+' FOR UPDATE']:
            with self.assertRaises(review.ReadReviewError):verify({**c,'sql':bad_sql},'java')

    def test_missing_soft_delete_extra_or_or_subquery_never_passes(self):
        """删除范围、未知AND、OR／子查询／注释／多语句均拒绝，不靠去空格后子串判断。"""
        c=command('java','SELECT id,name,remarks FROM sample_customer WHERE delete_no IS NULL AND id=? ORDER BY id DESC',[('','string',ID)])
        for text in ['id=?','delete_no IS NULL OR id=?','delete_no IS NULL AND id=? AND 1=2','delete_no IS NULL AND id=(SELECT ?)','delete_no IS NULL) AND (id=?','delete_no IS NULL AND id=? -- ignored','delete_no IS NULL AND id=?; SELECT 1']:
            with self.subTest(text=text),self.assertRaises(review.ReadReviewError):verify({**c,'sql':'SELECT id,name,remarks FROM sample_customer WHERE '+text+' ORDER BY id DESC'},'java')

    def test_named_alias_does_not_hide_duplicate_unused_or_wrong_value_parameters(self):
        """实际名字可带一个@，但不能重复、额外、漏绑定或给另一记录的证明。"""
        c=command('csharp','SELECT a.id as1,a.name as2,a.remarks as3 FROM sample_customer a WHERE a.delete_no IS NULL AND a.id=@id LIMIT 0,1',[('id','string',ID)]);verify(c,'csharp')
        for params in [[],c['parameters']*2,c['parameters']+[dict(name='unused',valueKind='integer',valueHash=proof('integer','0'))],[dict(name='id',valueKind='string',valueHash=proof('string','905910565396811777'))]]:
            with self.assertRaises(review.ReadReviewError):verify({**c,'parameters':params},'csharp')

    def test_unsafe_integer_kinds_and_zero_or_untrusted_proofs_are_rejected(self):
        """ID不能浮点化；无效proof、错误key和超过Int64的输入不会被字符串比较掩盖。"""
        c=command('java','SELECT id,name,remarks FROM sample_customer WHERE delete_no IS NULL AND id=? ORDER BY id DESC',[('','integer',ID)])
        for param in [{**c['parameters'][0],'kind':'number'},{**c['parameters'][0],'valueHash':'x'*64}]:
            with self.assertRaises(review.ReadReviewError):verify({**c,'parameters':[param]},'java')
        with self.assertRaises(review.ReadReviewError):review.proves(dict(kind='integer',valueHash=proof('integer',str(2**63))),KEY,str(2**63),{'integer'})
        with self.assertRaises(review.ReadReviewError):review.proves(dict(kind='integer',valueHash=proof('integer',ID)),b'x',ID,{'integer'})


if __name__=='__main__':unittest.main()
